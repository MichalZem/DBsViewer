using DbsViewer.Analysis;
using DbsViewer.TestKit;

namespace DbsViewer.Tests.Relational;

public class SchemaMergerTests
{
    private static DatabaseSchema Model(params DbTable[] tables) => new()
    {
        SourceKind = SchemaSourceKind.EfModel,
        SourceName = "EF model",
        Provider = DbProviderKind.SqlServer,
        ProviderName = "Microsoft.EntityFrameworkCore.SqlServer",
        DefaultSchema = "dbo",
        Tables = tables,
    };

    private static DatabaseSchema Database(params DbTable[] tables) => new()
    {
        SourceKind = SchemaSourceKind.LiveDatabase,
        SourceName = "SQL Server",
        Provider = DbProviderKind.SqlServer,
        DatabaseName = "Shop",
        Tables = tables,
    };

    [Fact]
    public void Slouceni_oznaci_zdroj_jako_Merged()
    {
        var merged = SchemaMerger.Merge(Model(), Database());

        Assert.Equal(SchemaSourceKind.Merged, merged.SourceKind);
        Assert.Equal("EF model + SQL Server", merged.SourceName);
        Assert.Equal("Shop", merged.DatabaseName);
        Assert.Equal("dbo", merged.DefaultSchema);
        Assert.Equal(DbProviderKind.SqlServer, merged.Provider);
    }

    [Fact]
    public void Provider_se_vezme_z_databaze_kdyz_ho_model_nezna()
    {
        var model = new DatabaseSchema { Provider = DbProviderKind.Unknown };
        var database = new DatabaseSchema { Provider = DbProviderKind.Sqlite, ProviderName = "Microsoft.Data.Sqlite" };

        var merged = SchemaMerger.Merge(model, database);

        Assert.Equal(DbProviderKind.Sqlite, merged.Provider);
        Assert.Equal("Microsoft.Data.Sqlite", merged.ProviderName);
    }

    [Fact]
    public void Cas_snimku_je_ten_novejsi()
    {
        var older = new DatabaseSchema { GeneratedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) };
        var newer = new DatabaseSchema { GeneratedAtUtc = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero) };

        Assert.Equal(newer.GeneratedAtUtc, SchemaMerger.Merge(older, newer).GeneratedAtUtc);
        Assert.Equal(newer.GeneratedAtUtc, SchemaMerger.Merge(newer, older).GeneratedAtUtc);
    }

    [Fact]
    public void Tabulky_z_obou_stran_zustanou_zachovane()
    {
        var merged = SchemaMerger.Merge(
            Model(Build.Table("Orders"), Build.Table("JenVModelu")),
            Database(Build.Table("Orders"), Build.Table("JenVDatabazi")));

        Assert.Equal(
            ["JenVDatabazi", "JenVModelu", "Orders"],
            merged.Tables.Select(t => t.Name.Name).ToList());
    }

    [Fact]
    public void Metadata_modelu_prezijou_slouceni()
    {
        var modelTable = new DbTable
        {
            Name = new DbObjectName(null, "Payments"),
            EntityClrNames = ["CardPayment", "Payment"],
            DiscriminatorColumn = "PaymentType",
            Comment = "Platby",
            IsExcludedFromMigrations = true,
        };

        var merged = SchemaMerger.Merge(Model(modelTable), Database(Build.Table("Payments")));
        var table = merged.Tables.Single();

        Assert.Equal(["CardPayment", "Payment"], table.EntityClrNames.ToList());
        Assert.Equal("PaymentType", table.DiscriminatorColumn);
        Assert.Equal("Platby", table.Comment);
        Assert.True(table.IsExcludedFromMigrations);
    }

    [Fact]
    public void Pocet_radku_zna_jen_databaze()
    {
        var databaseTable = new DbTable
        {
            Name = new DbObjectName(null, "Orders"),
            RowCountEstimate = 5000,
        };

        Assert.Equal(5000, SchemaMerger.Merge(Model(Build.Table("Orders")), Database(databaseTable))
            .Tables.Single().RowCountEstimate);
    }

    [Fact]
    public void Komentar_z_databaze_se_pouzije_kdyz_model_zadny_nema()
    {
        var databaseTable = new DbTable { Name = new DbObjectName(null, "T"), Comment = "Z databáze" };

        Assert.Equal("Z databáze",
            SchemaMerger.Merge(Model(Build.Table("T")), Database(databaseTable)).Tables.Single().Comment);
    }

    [Fact]
    public void Priznak_vazebni_tabulky_staci_z_jedne_strany()
    {
        var modelTable = new DbTable { Name = new DbObjectName(null, "PT"), IsJoinTable = true };

        Assert.True(SchemaMerger.Merge(Model(modelTable), Database(Build.Table("PT")))
            .Tables.Single().IsJoinTable);
    }

    [Fact]
    public void Sloupec_vezme_typ_z_databaze_a_zamer_z_modelu()
    {
        var modelTable = TableWith("T", new DbColumn
        {
            Name = "Total",
            Ordinal = 1,
            StoreType = "decimal(18,2)",
            ClrType = "System.Decimal",
            PropertyNames = ["Total"],
            IsConcurrencyToken = true,
            Comment = "Celkem",
        });

        var databaseTable = TableWith("T", new DbColumn
        {
            Name = "Total",
            Ordinal = 3,
            StoreType = "money",
            IsNullable = true,
            IsIdentity = true,
            Collation = "Czech_CI_AS",
        });

        var column = SchemaMerger.Merge(Model(modelTable), Database(databaseTable))
            .Tables.Single().Columns.Single();

        // Skutečnost v databázi.
        Assert.Equal("money", column.StoreType);
        Assert.True(column.IsNullable);
        Assert.True(column.IsIdentity);
        Assert.Equal(3, column.Ordinal);
        Assert.Equal("Czech_CI_AS", column.Collation);

        // Záměr modelu.
        Assert.Equal("System.Decimal", column.ClrType);
        Assert.Equal(["Total"], column.PropertyNames.ToList());
        Assert.True(column.IsConcurrencyToken);
        Assert.Equal("Celkem", column.Comment);
    }

    [Fact]
    public void Sloupec_jen_v_modelu_se_prida_na_konec()
    {
        var modelTable = TableWith("T",
            new DbColumn { Name = "A", Ordinal = 1, StoreType = "int" },
            new DbColumn { Name = "Novy", Ordinal = 2, StoreType = "int" });

        var databaseTable = TableWith("T", new DbColumn { Name = "A", Ordinal = 1, StoreType = "int" });

        var columns = SchemaMerger.Merge(Model(modelTable), Database(databaseTable))
            .Tables.Single().Columns;

        Assert.Equal(["A", "Novy"], columns.Select(c => c.Name).ToList());
    }

    [Fact]
    public void Sloupec_jen_v_databazi_zustane_beze_zmeny()
    {
        var databaseTable = TableWith("T",
            new DbColumn { Name = "Legacy", Ordinal = 1, StoreType = "bit" });

        var column = SchemaMerger.Merge(Model(Build.Table("T")), Database(databaseTable))
            .Tables.Single().Columns.Single();

        Assert.Equal("Legacy", column.Name);
        Assert.Null(column.ClrType);
    }

    [Fact]
    public void Chybejici_udaj_v_databazi_doplni_model()
    {
        var modelTable = TableWith("T", new DbColumn
        {
            Name = "A",
            Ordinal = 1,
            StoreType = "int",
            MaxLength = 50,
            DefaultValueSql = "0",
            ComputedSql = "1+1",
            IsStored = true,
            Precision = 18,
            Scale = 2,
        });

        var databaseTable = TableWith("T", new DbColumn { Name = "A", Ordinal = 1, StoreType = "int" });

        var column = SchemaMerger.Merge(Model(modelTable), Database(databaseTable))
            .Tables.Single().Columns.Single();

        Assert.Equal(50, column.MaxLength);
        Assert.Equal("0", column.DefaultValueSql);
        Assert.Equal("1+1", column.ComputedSql);
        Assert.True(column.IsStored);
        Assert.Equal(18, column.Precision);
        Assert.Equal(2, column.Scale);
    }

    [Fact]
    public void Generovani_hodnoty_z_modelu_ma_prednost()
    {
        var modelTable = TableWith("T", new DbColumn
        {
            Name = "A",
            Ordinal = 1,
            StoreType = "int",
            ValueGenerated = DbValueGenerated.OnAddOrUpdate,
        });

        var databaseTable = TableWith("T", new DbColumn
        {
            Name = "A",
            Ordinal = 1,
            StoreType = "int",
            ValueGenerated = DbValueGenerated.OnAdd,
        });

        Assert.Equal(DbValueGenerated.OnAddOrUpdate,
            SchemaMerger.Merge(Model(modelTable), Database(databaseTable))
                .Tables.Single().Columns.Single().ValueGenerated);
    }

    [Fact]
    public void Index_bere_skutecnost_z_databaze_ale_doplni_se_z_modelu()
    {
        var modelTable = WithIndexes("T", new DbIndex
        {
            Name = "IX",
            Columns = ["A"],
            IsUnique = true,
            IsClustered = true,
            FilterSql = "[A] > 0",
            IncludedColumns = ["B"],
        });

        var databaseTable = WithIndexes("T", new DbIndex { Name = "IX", Columns = ["A", "B"] });

        var index = SchemaMerger.Merge(Model(modelTable), Database(databaseTable))
            .Tables.Single().Indexes.Single();

        Assert.Equal(["A", "B"], index.Columns.ToList());
        Assert.False(index.IsUnique);
        Assert.True(index.IsClustered);
        Assert.Equal("[A] > 0", index.FilterSql);
        Assert.Equal(["B"], index.IncludedColumns.ToList());
    }

    [Fact]
    public void Indexy_z_obou_stran_zustanou()
    {
        var modelTable = WithIndexes("T", new DbIndex { Name = "IX_Model", Columns = ["A"] });
        var databaseTable = WithIndexes("T", new DbIndex { Name = "IX_Db", Columns = ["B"] });

        var indexes = SchemaMerger.Merge(Model(modelTable), Database(databaseTable))
            .Tables.Single().Indexes;

        Assert.Equal(["IX_Db", "IX_Model"], indexes.Select(i => i.Name).ToList());
    }

    [Fact]
    public void Cizi_klic_si_z_modelu_vezme_navigace()
    {
        var modelKey = new DbForeignKey
        {
            Name = "FK",
            Columns = ["AId"],
            PrincipalTable = new DbObjectName(null, "A"),
            PrincipalColumns = ["Id"],
            NavigationName = "A",
            InverseNavigationName = "Bs",
            IsRequired = true,
            IsUnique = true,
            DeleteBehavior = DbDeleteBehavior.Cascade,
        };

        var databaseKey = new DbForeignKey
        {
            Name = "FK",
            Columns = ["AId"],
            PrincipalTable = new DbObjectName(null, "A"),
            PrincipalColumns = ["Id"],
            DeleteBehavior = DbDeleteBehavior.NoAction,
        };

        var merged = SchemaMerger
            .Merge(Model(WithForeignKeys("B", modelKey)), Database(WithForeignKeys("B", databaseKey)))
            .Tables.Single().ForeignKeys.Single();

        Assert.Equal("A", merged.NavigationName);
        Assert.Equal("Bs", merged.InverseNavigationName);
        Assert.True(merged.IsRequired);
        Assert.True(merged.IsUnique);

        // Chování při mazání říká databáze.
        Assert.Equal(DbDeleteBehavior.NoAction, merged.DeleteBehavior);
    }

    [Fact]
    public void Nezname_chovani_v_databazi_doplni_model()
    {
        var modelKey = Build.ForeignKey("FK", ["AId"], "A", delete: DbDeleteBehavior.Cascade);
        var databaseKey = Build.ForeignKey("FK", ["AId"], "A", delete: DbDeleteBehavior.Unknown);

        Assert.Equal(DbDeleteBehavior.Cascade, SchemaMerger
            .Merge(Model(WithForeignKeys("B", modelKey)), Database(WithForeignKeys("B", databaseKey)))
            .Tables.Single().ForeignKeys.Single().DeleteBehavior);
    }

    [Fact]
    public void Vztahy_z_modelu_maji_prednost()
    {
        var model = Model() with
        {
            Relationships =
            [
                new DbRelationship
                {
                    Id = "fk:Orders|FK",
                    From = new DbObjectName(null, "Orders"),
                    To = new DbObjectName(null, "Customers"),
                    FromNavigation = "Customer",
                },
            ],
        };

        var database = Database() with
        {
            Relationships =
            [
                new DbRelationship
                {
                    Id = "fk:Orders|FK",
                    From = new DbObjectName(null, "Orders"),
                    To = new DbObjectName(null, "Customers"),
                },
            ],
        };

        var relationship = Assert.Single(SchemaMerger.Merge(model, database).Relationships);

        Assert.Equal("Customer", relationship.FromNavigation);
    }

    [Fact]
    public void Vztah_jen_v_databazi_se_doplni()
    {
        var database = Database() with
        {
            Relationships =
            [
                new DbRelationship
                {
                    Id = "fk:Legacy|FK",
                    From = new DbObjectName(null, "Legacy"),
                    To = new DbObjectName(null, "X"),
                },
            ],
        };

        Assert.Single(SchemaMerger.Merge(Model(), database).Relationships);
    }

    [Fact]
    public void Sbalene_NM_v_modelu_potlaci_hrany_vazebni_tabulky_z_databaze()
    {
        var model = Model() with
        {
            Relationships =
            [
                new DbRelationship
                {
                    Id = "m2m:Products<->Tags|ProductTags",
                    From = new DbObjectName(null, "Products"),
                    To = new DbObjectName(null, "Tags"),
                    Cardinality = DbCardinality.ManyToMany,
                    ViaJoinTable = new DbObjectName(null, "ProductTags"),
                },
            ],
        };

        var database = Database() with
        {
            Relationships =
            [
                new DbRelationship
                {
                    Id = "fk:ProductTags|FK_P",
                    From = new DbObjectName(null, "ProductTags"),
                    To = new DbObjectName(null, "Products"),
                },
                new DbRelationship
                {
                    Id = "fk:ProductTags|FK_T",
                    From = new DbObjectName(null, "ProductTags"),
                    To = new DbObjectName(null, "Tags"),
                },
            ],
        };

        var relationship = Assert.Single(SchemaMerger.Merge(model, database).Relationships);

        Assert.Equal(DbCardinality.ManyToMany, relationship.Cardinality);
    }

    [Fact]
    public void Migrace_z_obou_stran_se_spoji()
    {
        var model = Model() with
        {
            Migrations =
            [
                new DbMigration { Id = "20260101_A", PresentInAssembly = true, AppliedInDatabase = true },
                new DbMigration { Id = "20260301_C", PresentInAssembly = true },
            ],
        };

        var database = Database() with
        {
            Migrations =
            [
                new DbMigration { Id = "20260101_A", AppliedInDatabase = true },
                new DbMigration { Id = "20260201_B", AppliedInDatabase = true },
            ],
        };

        var migrations = SchemaMerger.Merge(model, database).Migrations;

        Assert.Equal(["20260101_A", "20260201_B", "20260301_C"], migrations.Select(m => m.Id).ToList());
        Assert.True(migrations[0] is { PresentInAssembly: true, AppliedInDatabase: true });
        Assert.True(migrations[1].IsOrphaned);
        Assert.True(migrations[2].IsPending);
    }

    [Fact]
    public void Upozorneni_z_obou_stran_se_spoji()
    {
        var model = Model() with { Warnings = ["z modelu"] };
        var database = Database() with { Warnings = ["z databáze"] };

        Assert.Equal(["z modelu", "z databáze"], SchemaMerger.Merge(model, database).Warnings.ToList());
    }

    [Fact]
    public void Check_constrainty_bere_databaze_kdyz_nejake_ma()
    {
        var modelTable = WithChecks("T", new DbCheckConstraint { Name = "CK_Model" });
        var databaseTable = WithChecks("T", new DbCheckConstraint { Name = "CK_Db" });

        Assert.Equal("CK_Db", SchemaMerger.Merge(Model(modelTable), Database(databaseTable))
            .Tables.Single().CheckConstraints.Single().Name);

        Assert.Equal("CK_Model", SchemaMerger.Merge(Model(modelTable), Database(Build.Table("T")))
            .Tables.Single().CheckConstraints.Single().Name);
    }

    [Fact]
    public void Klic_z_modelu_se_pouzije_kdyz_ho_databaze_nema()
    {
        var modelTable = new DbTable
        {
            Name = new DbObjectName(null, "T"),
            PrimaryKey = new DbPrimaryKey { Columns = ["Id"] },
        };

        Assert.Equal(["Id"], SchemaMerger.Merge(Model(modelTable), Database(Build.Table("T")))
            .Tables.Single().PrimaryKey!.Columns.ToList());
    }

    [Fact]
    public void Chybejici_vstupy_jsou_chyba()
    {
        Assert.Throws<ArgumentNullException>(() => SchemaMerger.Merge(null!, new DatabaseSchema()));
        Assert.Throws<ArgumentNullException>(() => SchemaMerger.Merge(new DatabaseSchema(), null!));
    }

    private static DbTable TableWith(string name, params DbColumn[] columns) => new()
    {
        Name = new DbObjectName(null, name),
        Columns = columns,
    };

    private static DbTable WithIndexes(string name, params DbIndex[] indexes) => new()
    {
        Name = new DbObjectName(null, name),
        Indexes = indexes,
    };

    private static DbTable WithForeignKeys(string name, params DbForeignKey[] foreignKeys) => new()
    {
        Name = new DbObjectName(null, name),
        ForeignKeys = foreignKeys,
    };

    private static DbTable WithChecks(string name, params DbCheckConstraint[] checks) => new()
    {
        Name = new DbObjectName(null, name),
        CheckConstraints = checks,
    };
}
