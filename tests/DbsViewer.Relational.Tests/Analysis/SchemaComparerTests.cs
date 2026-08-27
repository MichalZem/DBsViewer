using DbsViewer.Analysis;
using DbsViewer.TestKit;

namespace DbsViewer.Tests.Relational;

public class SchemaComparerTests
{
    private static DatabaseSchema Schema(params DbTable[] tables) => new()
    {
        SourceKind = SchemaSourceKind.EfModel,
        Tables = tables,
    };

    private static DbTable Table(string name, params DbColumn[] columns) => new()
    {
        Name = new DbObjectName("dbo", name),
        Columns = columns,
    };

    private static DbColumn Column(
        string name,
        string storeType = "int",
        bool nullable = false,
        int? maxLength = null,
        string? defaultSql = null) => new()
        {
            Name = name,
            Ordinal = 1,
            StoreType = storeType,
            IsNullable = nullable,
            MaxLength = maxLength,
            DefaultValueSql = defaultSql,
        };

    private static DiffFinding Single(SchemaDiff diff, DiffKind kind) =>
        Assert.Single(diff.Findings.Where(f => f.Kind == kind));

    // ---------- tabulky ----------

    [Fact]
    public void Shodna_schemata_nemaji_nalezy()
    {
        var model = Schema(Table("Orders", Column("Id")));
        var database = Schema(Table("Orders", Column("Id")));

        var diff = SchemaComparer.Compare(model, database);

        Assert.Empty(diff.Findings);
        Assert.True(diff.IsClean);
        Assert.Equal(0, diff.ErrorCount);
        Assert.Equal(0, diff.WarningCount);
    }

    [Fact]
    public void Tabulka_chybejici_v_databazi_je_chyba()
    {
        var diff = SchemaComparer.Compare(Schema(Table("Orders", Column("Id"))), Schema());

        var finding = Single(diff, DiffKind.TableMissingInDatabase);
        Assert.Equal(DiffSeverity.Error, finding.Severity);
        Assert.Equal("dbo.Orders", finding.Table!.Value.Qualified);
        Assert.Contains("neaplikovaná migrace", finding.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(diff.IsClean);
        Assert.Equal(1, diff.ErrorCount);
    }

    [Fact]
    public void Tabulka_navic_v_databazi_je_varovani()
    {
        var diff = SchemaComparer.Compare(Schema(), Schema(Table("Legacy", Column("Id"))));

        Assert.Equal(DiffSeverity.Warning, Single(diff, DiffKind.TableMissingInModel).Severity);
        Assert.Equal(1, diff.WarningCount);
    }

    [Fact]
    public void Historie_migraci_se_nehlasi_jako_tabulka_navic()
    {
        var diff = SchemaComparer.Compare(Schema(), Schema(Table("__EFMigrationsHistory", Column("MigrationId"))));

        Assert.Empty(diff.Findings);
    }

    [Fact]
    public void Ignorovane_tabulky_se_nehlasi()
    {
        var database = Schema(Table("AspNetUsers", Column("Id")), Table("Legacy", Column("Id")));
        var options = new DiffOptions { IgnoreTables = ["AspNet*"] };

        var diff = SchemaComparer.Compare(Schema(), database, options);

        Assert.Equal("dbo.Legacy", Assert.Single(diff.Findings).Table!.Value.Qualified);
    }

    // ---------- sloupce ----------

    [Fact]
    public void Sloupec_chybejici_v_databazi_je_chyba()
    {
        var model = Schema(Table("Orders", Column("Id"), Column("Note")));
        var database = Schema(Table("Orders", Column("Id")));

        var finding = Single(SchemaComparer.Compare(model, database), DiffKind.ColumnMissingInDatabase);

        Assert.Equal(DiffSeverity.Error, finding.Severity);
        Assert.Equal("Note", finding.Object);
    }

    [Fact]
    public void Sloupec_navic_v_databazi_je_varovani()
    {
        var model = Schema(Table("Orders", Column("Id")));
        var database = Schema(Table("Orders", Column("Id"), Column("LegacyFlag", "bit")));

        var finding = Single(SchemaComparer.Compare(model, database), DiffKind.ColumnMissingInModel);

        Assert.Equal(DiffSeverity.Warning, finding.Severity);
        Assert.Equal("bit", finding.DatabaseValue);
    }

    [Fact]
    public void Rozdilny_typ_sloupce_je_chyba()
    {
        var model = Schema(Table("Orders", Column("Total", "decimal(18,2)")));
        var database = Schema(Table("Orders", Column("Total", "money")));

        var finding = Single(SchemaComparer.Compare(model, database), DiffKind.ColumnTypeMismatch);

        Assert.Equal(DiffSeverity.Error, finding.Severity);
        Assert.Equal("decimal(18,2)", finding.ModelValue);
        Assert.Equal("money", finding.DatabaseValue);
    }

    [Fact]
    public void Porovnani_typu_se_da_vypnout()
    {
        var model = Schema(Table("Orders", Column("Total", "decimal(18,2)")));
        var database = Schema(Table("Orders", Column("Total", "money")));

        var diff = SchemaComparer.Compare(model, database, new DiffOptions { CompareStoreTypes = false });

        Assert.Empty(diff.Findings);
    }

    [Fact]
    public void Model_pripousti_NULL_databaze_ne()
    {
        var model = Schema(Table("Orders", Column("Note", nullable: true)));
        var database = Schema(Table("Orders", Column("Note")));

        var finding = Single(SchemaComparer.Compare(model, database), DiffKind.ColumnNullabilityMismatch);

        Assert.Equal(DiffSeverity.Error, finding.Severity);
        Assert.Contains("Uložení prázdné hodnoty selže", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Databaze_pripousti_NULL_model_ne()
    {
        var model = Schema(Table("Orders", Column("Note")));
        var database = Schema(Table("Orders", Column("Note", nullable: true)));

        var finding = Single(SchemaComparer.Compare(model, database), DiffKind.ColumnNullabilityMismatch);

        Assert.Contains("vyhodí výjimku", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Kratsi_sloupec_v_databazi_je_chyba()
    {
        var model = Schema(Table("Orders", Column("Note", "nvarchar(200)", maxLength: 200)));
        var database = Schema(Table("Orders", Column("Note", "nvarchar(50)", maxLength: 50)));

        var finding = Single(SchemaComparer.Compare(model, database), DiffKind.ColumnLengthMismatch);

        Assert.Equal(DiffSeverity.Error, finding.Severity);
        Assert.Contains("neuloží", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Delsi_sloupec_v_databazi_je_jen_varovani()
    {
        var model = Schema(Table("Orders", Column("Note", "nvarchar(50)", maxLength: 50)));
        var database = Schema(Table("Orders", Column("Note", "nvarchar(200)", maxLength: 200)));

        Assert.Equal(
            DiffSeverity.Warning,
            Single(SchemaComparer.Compare(model, database), DiffKind.ColumnLengthMismatch).Severity);
    }

    [Fact]
    public void Porovnani_delek_se_da_vypnout()
    {
        var model = Schema(Table("Orders", Column("Note", "nvarchar(50)", maxLength: 50)));
        var database = Schema(Table("Orders", Column("Note", "nvarchar(50)", maxLength: 200)));

        var diff = SchemaComparer.Compare(model, database, new DiffOptions { CompareLengths = false });

        Assert.Empty(diff.Findings);
    }

    [Fact]
    public void Neznama_delka_na_jedne_strane_se_nehlasi()
    {
        var model = Schema(Table("Orders", Column("Note", "nvarchar(max)")));
        var database = Schema(Table("Orders", Column("Note", "nvarchar(max)", maxLength: 200)));

        Assert.Empty(SchemaComparer.Compare(model, database).Findings);
    }

    [Fact]
    public void Defaulty_se_ve_vychozim_stavu_neporovnavaji()
    {
        var model = Schema(Table("Orders", Column("Created", defaultSql: "GETDATE()")));
        var database = Schema(Table("Orders", Column("Created", defaultSql: "(getutcdate())")));

        Assert.Empty(SchemaComparer.Compare(model, database).Findings);
    }

    [Fact]
    public void Zapnute_porovnani_defaultu_najde_rozdil()
    {
        var model = Schema(Table("Orders", Column("Created", defaultSql: "GETDATE()")));
        var database = Schema(Table("Orders", Column("Created", defaultSql: "(getutcdate())")));

        var diff = SchemaComparer.Compare(model, database, new DiffOptions { CompareDefaults = true });

        Assert.Equal(DiffSeverity.Warning, Single(diff, DiffKind.ColumnDefaultMismatch).Severity);
    }

    [Fact]
    public void Zavorky_kolem_defaultu_nejsou_rozdil()
    {
        var model = Schema(Table("Orders", Column("Created", defaultSql: "GETDATE()")));
        var database = Schema(Table("Orders", Column("Created", defaultSql: "(getdate())")));

        var diff = SchemaComparer.Compare(model, database, new DiffOptions { CompareDefaults = true });

        Assert.Empty(diff.Findings);
    }

    // ---------- klíče a indexy ----------

    [Fact]
    public void Rozdilny_primarni_klic_je_chyba()
    {
        var model = Schema(new DbTable
        {
            Name = new DbObjectName("dbo", "T"),
            PrimaryKey = new DbPrimaryKey { Columns = ["A", "B"] },
        });

        var database = Schema(new DbTable
        {
            Name = new DbObjectName("dbo", "T"),
            PrimaryKey = new DbPrimaryKey { Columns = ["A"] },
        });

        var finding = Single(SchemaComparer.Compare(model, database), DiffKind.PrimaryKeyMismatch);

        Assert.Equal("A, B", finding.ModelValue);
        Assert.Equal("A", finding.DatabaseValue);
    }

    [Fact]
    public void Chybejici_klic_na_obou_stranach_neni_rozdil()
    {
        var diff = SchemaComparer.Compare(Schema(Table("T")), Schema(Table("T")));

        Assert.Empty(diff.Findings);
    }

    [Fact]
    public void Klic_jen_v_databazi_je_rozdil()
    {
        var model = Schema(Table("T"));
        var database = Schema(new DbTable
        {
            Name = new DbObjectName("dbo", "T"),
            PrimaryKey = new DbPrimaryKey { Columns = ["Id"] },
        });

        Assert.Equal("(žádné)", Single(SchemaComparer.Compare(model, database), DiffKind.PrimaryKeyMismatch).ModelValue);
    }

    [Fact]
    public void Index_chybejici_v_databazi_je_varovani()
    {
        var model = Schema(WithIndexes("T", Build.Index("IX", ["A"])));
        var database = Schema(Table("T"));

        var finding = Single(SchemaComparer.Compare(model, database), DiffKind.IndexMissingInDatabase);

        Assert.Equal(DiffSeverity.Warning, finding.Severity);
        Assert.Equal("A", finding.ModelValue);
    }

    [Fact]
    public void Index_navic_v_databazi_je_varovani()
    {
        var model = Schema(Table("T"));
        var database = Schema(WithIndexes("T", Build.Index("IX_Rucni", ["A"])));

        var finding = Single(SchemaComparer.Compare(model, database), DiffKind.IndexMissingInModel);

        Assert.Contains("mimo migrace", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rozdilna_unikatnost_indexu_je_chyba()
    {
        var model = Schema(WithIndexes("T", Build.Index("IX", ["A"], isUnique: true)));
        var database = Schema(WithIndexes("T", Build.Index("IX", ["A"])));

        var finding = Single(SchemaComparer.Compare(model, database), DiffKind.IndexUniquenessMismatch);

        Assert.Equal(DiffSeverity.Error, finding.Severity);
        Assert.Contains("Duplicita projde", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unikatnost_navic_v_databazi_se_hlasi_opacne()
    {
        var model = Schema(WithIndexes("T", Build.Index("IX", ["A"])));
        var database = Schema(WithIndexes("T", Build.Index("IX", ["A"], isUnique: true)));

        Assert.Contains(
            "Uložení duplicity selže",
            Single(SchemaComparer.Compare(model, database), DiffKind.IndexUniquenessMismatch).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Rozdilne_sloupce_indexu_jsou_varovani()
    {
        var model = Schema(WithIndexes("T", Build.Index("IX", ["A", "B"])));
        var database = Schema(WithIndexes("T", Build.Index("IX", ["B", "A"])));

        var finding = Single(SchemaComparer.Compare(model, database), DiffKind.IndexColumnsMismatch);

        Assert.Equal("A, B", finding.ModelValue);
        Assert.Equal("B, A", finding.DatabaseValue);
    }

    // ---------- cizí klíče ----------

    [Fact]
    public void Cizi_klic_chybejici_v_databazi_je_chyba()
    {
        var model = Schema(WithForeignKeys("T", Build.ForeignKey("FK", ["AId"], "A")));
        var database = Schema(Table("T"));

        var finding = Single(SchemaComparer.Compare(model, database), DiffKind.ForeignKeyMissingInDatabase);

        Assert.Equal(DiffSeverity.Error, finding.Severity);
        Assert.Contains("Integrita se nevynucuje", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cizi_klic_navic_v_databazi_je_varovani()
    {
        var model = Schema(Table("T"));
        var database = Schema(WithForeignKeys("T", Build.ForeignKey("FK", ["AId"], "A")));

        Assert.Equal(
            DiffSeverity.Warning,
            Single(SchemaComparer.Compare(model, database), DiffKind.ForeignKeyMissingInModel).Severity);
    }

    [Fact]
    public void Cizi_klic_na_jinou_tabulku_je_chyba()
    {
        var model = Schema(WithForeignKeys("T", Build.ForeignKey("FK", ["X"], "A")));
        var database = Schema(WithForeignKeys("T", Build.ForeignKey("FK", ["X"], "B")));

        var finding = Single(SchemaComparer.Compare(model, database), DiffKind.ForeignKeyTargetMismatch);

        Assert.Equal("A", finding.ModelValue);
        Assert.Equal("B", finding.DatabaseValue);
    }

    [Fact]
    public void Rozdilne_chovani_pri_mazani_je_chyba()
    {
        var model = Schema(WithForeignKeys("T",
            Build.ForeignKey("FK", ["X"], "A", delete: DbDeleteBehavior.Cascade)));
        var database = Schema(WithForeignKeys("T",
            Build.ForeignKey("FK", ["X"], "A", delete: DbDeleteBehavior.NoAction)));

        var finding = Single(SchemaComparer.Compare(model, database), DiffKind.ForeignKeyDeleteBehaviorMismatch);

        Assert.Equal("Cascade", finding.ModelValue);
        Assert.Equal("NoAction", finding.DatabaseValue);
    }

    // ---------- migrace ----------

    [Fact]
    public void Nenasazena_migrace_je_chyba()
    {
        var model = new DatabaseSchema
        {
            Migrations = [new DbMigration { Id = "20260101_Init", PresentInAssembly = true }],
        };

        var finding = Single(SchemaComparer.Compare(model, new DatabaseSchema()), DiffKind.MigrationPending);

        Assert.Equal(DiffSeverity.Error, finding.Severity);
        Assert.Equal("20260101_Init", finding.Object);
        Assert.Null(finding.Table);
    }

    [Fact]
    public void Osirela_migrace_je_varovani()
    {
        var model = new DatabaseSchema
        {
            Migrations = [new DbMigration { Id = "20250101_Old", AppliedInDatabase = true }],
        };

        Assert.Equal(
            DiffSeverity.Warning,
            Single(SchemaComparer.Compare(model, new DatabaseSchema()), DiffKind.MigrationOrphaned).Severity);
    }

    [Fact]
    public void Nasazena_migrace_se_nehlasi()
    {
        var model = new DatabaseSchema
        {
            Migrations =
            [
                new DbMigration { Id = "20260101_Init", PresentInAssembly = true, AppliedInDatabase = true },
            ],
        };

        Assert.Empty(SchemaComparer.Compare(model, new DatabaseSchema()).Findings);
    }

    // ---------- řazení a dotazy nad výsledkem ----------

    [Fact]
    public void Nalezy_jsou_serazene_od_nejzavaznejsich()
    {
        var model = Schema(Table("A", Column("X", nullable: true)), Table("B", Column("Y")));
        var database = Schema(Table("A", Column("X")), Table("C", Column("Z")));

        var diff = SchemaComparer.Compare(model, database);
        var severities = diff.Findings.Select(f => f.Severity).ToList();

        Assert.Equal(severities.Order().ToList(), severities);
    }

    [Fact]
    public void Nalezy_jde_filtrovat_podle_tabulky()
    {
        var model = Schema(Table("A", Column("X"), Column("Y")), Table("B", Column("Z")));
        var database = Schema(Table("A", Column("X")), Table("B"));

        var diff = SchemaComparer.Compare(model, database);
        var forA = diff.ForTable(new DbObjectName("dbo", "A"));

        Assert.Equal("Y", Assert.Single(forA).Object);
        Assert.Equal(DiffSeverity.Error, diff.SeverityOf(new DbObjectName("dbo", "A")));
        Assert.Null(diff.SeverityOf(new DbObjectName("dbo", "Neexistuje")));
    }

    [Fact]
    public void Zavaznost_tabulky_je_nejhorsi_z_nalezu()
    {
        var model = Schema(Table("A", Column("X")));
        var database = Schema(Table("A", Column("X"), Column("Navic")));

        var diff = SchemaComparer.Compare(model, database);

        Assert.Equal(DiffSeverity.Warning, diff.SeverityOf(new DbObjectName("dbo", "A")));
    }

    [Fact]
    public void Popisek_nalezu_je_citelny()
    {
        var withTable = new DiffFinding
        {
            Kind = DiffKind.ColumnMissingInDatabase,
            Severity = DiffSeverity.Error,
            Table = new DbObjectName("dbo", "Orders"),
            Object = "Note",
            Message = "Sloupec chybí.",
        };

        Assert.Equal("[Error] dbo.Orders.Note: Sloupec chybí.", withTable.ToString());

        var withoutObject = new DiffFinding
        {
            Kind = DiffKind.TableMissingInDatabase,
            Severity = DiffSeverity.Error,
            Table = new DbObjectName("dbo", "Orders"),
            Message = "Tabulka chybí.",
        };

        Assert.Equal("[Error] dbo.Orders: Tabulka chybí.", withoutObject.ToString());

        var migration = new DiffFinding
        {
            Kind = DiffKind.MigrationPending,
            Severity = DiffSeverity.Warning,
            Message = "Migrace čeká.",
        };

        Assert.Equal("[Warning] Migrace čeká.", migration.ToString());
    }

    [Fact]
    public void Chybejici_vstupy_jsou_chyba()
    {
        Assert.Throws<ArgumentNullException>(() => SchemaComparer.Compare(null!, new DatabaseSchema()));
        Assert.Throws<ArgumentNullException>(() => SchemaComparer.Compare(new DatabaseSchema(), null!));
    }

    private static DbTable WithIndexes(string name, params DbIndex[] indexes) => new()
    {
        Name = new DbObjectName("dbo", name),
        Indexes = indexes,
    };

    private static DbTable WithForeignKeys(string name, params DbForeignKey[] foreignKeys) => new()
    {
        Name = new DbObjectName("dbo", name),
        ForeignKeys = foreignKeys,
    };
}

public class ComparisonRuleTests
{
    [Theory]
    [InlineData("nvarchar(200)", "nvarchar(200)")]
    [InlineData("nvarchar(200)", "NVARCHAR(200)")]
    [InlineData("decimal(18, 2)", "decimal(18,2)")]
    [InlineData("int", "  int  ")]
    [InlineData(null, "int")]
    [InlineData("int", null)]
    [InlineData("", "int")]
    public void Typy_se_shoduji(string? model, string? database) =>
        Assert.True(SchemaComparer.StoreTypesMatch(model, database));

    [Theory]
    [InlineData("int", "bigint")]
    [InlineData("nvarchar(200)", "nvarchar(50)")]
    public void Typy_se_lisi(string model, string database) =>
        Assert.False(SchemaComparer.StoreTypesMatch(model, database));

    [Theory]
    [InlineData("GETDATE()", "(getdate())")]
    [InlineData("((0))", "0")]
    [InlineData(null, null)]
    [InlineData("", "   ")]
    [InlineData("N'x'", "(N'x')")]
    public void Defaulty_se_shoduji(string? model, string? database) =>
        Assert.True(SchemaComparer.DefaultsMatch(model, database));

    [Theory]
    [InlineData("GETDATE()", null)]
    [InlineData(null, "0")]
    [InlineData("0", "1")]
    public void Defaulty_se_lisi(string? model, string? database) =>
        Assert.False(SchemaComparer.DefaultsMatch(model, database));

    [Fact]
    public void Sloupce_se_porovnavaji_vcetne_poradi()
    {
        Assert.True(SchemaComparer.ColumnsMatch(["A", "B"], ["a", "b"]));
        Assert.False(SchemaComparer.ColumnsMatch(["A", "B"], ["B", "A"]));
        Assert.False(SchemaComparer.ColumnsMatch(["A"], ["A", "B"]));
        Assert.True(SchemaComparer.ColumnsMatch([], []));
    }

    [Fact]
    public void Chybejici_seznam_sloupcu_je_chyba()
    {
        Assert.Throws<ArgumentNullException>(() => SchemaComparer.ColumnsMatch(null!, []));
        Assert.Throws<ArgumentNullException>(() => SchemaComparer.ColumnsMatch([], null!));
    }

    [Theory]
    [InlineData(DbDeleteBehavior.Cascade, DbDeleteBehavior.Cascade)]
    [InlineData(DbDeleteBehavior.Restrict, DbDeleteBehavior.NoAction)]
    [InlineData(DbDeleteBehavior.NoAction, DbDeleteBehavior.Restrict)]
    [InlineData(DbDeleteBehavior.Unknown, DbDeleteBehavior.Cascade)]
    [InlineData(DbDeleteBehavior.Cascade, DbDeleteBehavior.Unknown)]
    public void Chovani_pri_mazani_se_neliší(DbDeleteBehavior model, DbDeleteBehavior database) =>
        Assert.False(SchemaComparer.DeleteBehaviorDiffers(model, database));

    [Theory]
    [InlineData(DbDeleteBehavior.Cascade, DbDeleteBehavior.NoAction)]
    [InlineData(DbDeleteBehavior.SetNull, DbDeleteBehavior.Cascade)]
    [InlineData(DbDeleteBehavior.Restrict, DbDeleteBehavior.SetDefault)]
    public void Chovani_pri_mazani_se_lisi(DbDeleteBehavior model, DbDeleteBehavior database) =>
        Assert.True(SchemaComparer.DeleteBehaviorDiffers(model, database));
}
