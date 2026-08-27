using DbsViewer.Relational;

namespace DbsViewer.Tests.Relational;

/// <summary>
/// Sestavení schématu ze surových dat. Testuje se bez databáze — assembler je čistá funkce.
/// </summary>
public class LiveSchemaAssemblerTests
{
    private static DatabaseSchema Build(RawSchema raw, SchemaReadOptions? options = null) =>
        LiveSchemaAssembler.Build(
            raw,
            options ?? new SchemaReadOptions { IncludeMigrations = false },
            DbProviderKind.SqlServer,
            "Microsoft.Data.SqlClient",
            "SQL Server (Test)");

    private static RawSchema OrdersAndCustomers() => new()
    {
        DatabaseName = "Shop",
        DefaultSchema = "dbo",
        Tables =
        [
            new RawTable("dbo", "Customers", Comment: "Zákazníci"),
            new RawTable("dbo", "Orders"),
        ],
        Columns =
        [
            new RawColumn("dbo", "Customers", "Id", 1, "int", false, IsIdentity: true),
            new RawColumn("dbo", "Customers", "Email", 2, "nvarchar(256)", false, MaxLength: 256),
            new RawColumn("dbo", "Orders", "Id", 1, "int", false, IsIdentity: true),
            new RawColumn("dbo", "Orders", "CustomerId", 2, "int", false),
            new RawColumn("dbo", "Orders", "Note", 3, "nvarchar(max)", true),
        ],
        KeyColumns =
        [
            new RawKeyColumn("dbo", "Customers", "PK_Customers", "Id", 1, IsClustered: true),
            new RawKeyColumn("dbo", "Orders", "PK_Orders", "Id", 1),
        ],
        Indexes = [new RawIndex("dbo", "Customers", "UX_Customers_Email", IsUnique: true)],
        IndexColumns = [new RawIndexColumn("dbo", "Customers", "UX_Customers_Email", "Email", 1)],
        ForeignKeys =
        [
            new RawForeignKey("dbo", "Orders", "FK_Orders_Customers", "dbo", "Customers", "CASCADE"),
        ],
        ForeignKeyColumns =
        [
            new RawForeignKeyColumn("dbo", "Orders", "FK_Orders_Customers", "CustomerId", "Id", 1),
        ],
    };

    [Fact]
    public void Hlavicka_nese_zdroj_i_providera()
    {
        var schema = Build(OrdersAndCustomers());

        Assert.Equal(SchemaSourceKind.LiveDatabase, schema.SourceKind);
        Assert.Equal(DbProviderKind.SqlServer, schema.Provider);
        Assert.Equal("Microsoft.Data.SqlClient", schema.ProviderName);
        Assert.Equal("SQL Server (Test)", schema.SourceName);
        Assert.Equal("Shop", schema.DatabaseName);
        Assert.Equal("dbo", schema.DefaultSchema);
    }

    [Fact]
    public void Chybejici_argumenty_jsou_chyba()
    {
        Assert.Throws<ArgumentNullException>(() =>
            LiveSchemaAssembler.Build(null!, SchemaReadOptions.Default, DbProviderKind.Sqlite, null, "x"));

        Assert.Throws<ArgumentNullException>(() =>
            LiveSchemaAssembler.Build(new RawSchema(), null!, DbProviderKind.Sqlite, null, "x"));
    }

    [Fact]
    public void Prazdna_databaze_da_prazdne_schema()
    {
        var schema = Build(new RawSchema());

        Assert.Empty(schema.Tables);
        Assert.Empty(schema.Relationships);
        Assert.Empty(schema.Migrations);
    }

    [Fact]
    public void Tabulky_jsou_serazene_a_nesou_komentar()
    {
        var schema = Build(OrdersAndCustomers());

        Assert.Equal(["dbo.Customers", "dbo.Orders"], schema.Tables.Select(t => t.Qualified).ToList());
        Assert.Equal("Zákazníci", schema.Tables[0].Comment);
    }

    [Fact]
    public void Sloupce_se_radi_podle_poradi_v_databazi()
    {
        var schema = Build(OrdersAndCustomers());
        var orders = schema.FindTable(new DbObjectName("dbo", "Orders"))!;

        Assert.Equal(["Id", "CustomerId", "Note"], orders.Columns.Select(c => c.Name).ToList());
        Assert.True(orders.Columns[2].IsNullable);
    }

    [Fact]
    public void Sloupec_zna_svou_roli_v_klicich()
    {
        var schema = Build(OrdersAndCustomers());
        var orders = schema.FindTable(new DbObjectName("dbo", "Orders"))!;

        Assert.True(orders.FindColumn("Id")!.IsPrimaryKey);
        Assert.True(orders.FindColumn("CustomerId")!.IsForeignKey);
        Assert.False(orders.FindColumn("Note")!.IsPrimaryKey);
        Assert.False(orders.FindColumn("Note")!.IsForeignKey);
    }

    [Fact]
    public void Identity_sloupec_ma_odpovidajici_generovani()
    {
        var schema = Build(OrdersAndCustomers());
        var customers = schema.FindTable(new DbObjectName("dbo", "Customers"))!;

        Assert.Equal(DbValueGenerated.OnAdd, customers.FindColumn("Id")!.ValueGenerated);
        Assert.Equal(DbValueGenerated.Never, customers.FindColumn("Email")!.ValueGenerated);
    }

    [Fact]
    public void Primarni_klic_zachova_poradi_sloupcu()
    {
        var raw = new RawSchema
        {
            Tables = [new RawTable(null, "OrderLines")],
            Columns =
            [
                new RawColumn(null, "OrderLines", "LineNumber", 1, "int", false),
                new RawColumn(null, "OrderLines", "OrderId", 2, "int", false),
            ],
            KeyColumns =
            [
                new RawKeyColumn(null, "OrderLines", "PK_OrderLines", "OrderId", 1),
                new RawKeyColumn(null, "OrderLines", "PK_OrderLines", "LineNumber", 2),
            ],
        };

        var table = Build(raw).Tables.Single();

        Assert.Equal(["OrderId", "LineNumber"], table.PrimaryKey!.Columns.ToList());
        Assert.Equal("PK_OrderLines", table.PrimaryKey.Name);
    }

    [Fact]
    public void Tabulka_bez_klice_nema_primarni_klic()
    {
        var raw = new RawSchema
        {
            Tables = [new RawTable(null, "AuditLog")],
            Columns = [new RawColumn(null, "AuditLog", "Message", 1, "nvarchar(max)", true)],
        };

        Assert.Null(Build(raw).Tables.Single().PrimaryKey);
    }

    [Fact]
    public void Klic_nese_priznak_clustered()
    {
        var schema = Build(OrdersAndCustomers());

        Assert.True(schema.FindTable(new DbObjectName("dbo", "Customers"))!.PrimaryKey!.IsClustered);
    }

    [Fact]
    public void Index_oddeli_klicove_sloupce_od_INCLUDE()
    {
        var raw = new RawSchema
        {
            Tables = [new RawTable("dbo", "Orders")],
            Columns = [new RawColumn("dbo", "Orders", "Id", 1, "int", false)],
            Indexes = [new RawIndex("dbo", "Orders", "IX_Orders", false, FilterSql: "[Id] > 0")],
            IndexColumns =
            [
                new RawIndexColumn("dbo", "Orders", "IX_Orders", "PlacedAt", 1, IsDescending: true),
                new RawIndexColumn("dbo", "Orders", "IX_Orders", "Total", 1, IsIncluded: true),
                new RawIndexColumn("dbo", "Orders", "IX_Orders", "CustomerId", 2),
            ],
        };

        var index = Build(raw).Tables.Single().Indexes.Single();

        Assert.Equal(["PlacedAt", "CustomerId"], index.Columns.ToList());
        Assert.Equal(["Total"], index.IncludedColumns.ToList());
        Assert.Equal([true, false], index.IsDescending.ToList());
        Assert.Equal("[Id] > 0", index.FilterSql);
    }

    [Fact]
    public void Vzestupny_index_nema_vyplneny_smer()
    {
        var schema = Build(OrdersAndCustomers());
        var index = schema.FindTable(new DbObjectName("dbo", "Customers"))!.Indexes.Single();

        Assert.Empty(index.IsDescending);
        Assert.True(index.IsUnique);
        Assert.Equal(["Email"], index.Columns.ToList());
    }

    [Fact]
    public void Index_bez_sloupcu_zustane_prazdny()
    {
        var raw = new RawSchema
        {
            Tables = [new RawTable(null, "T")],
            Indexes = [new RawIndex(null, "T", "IX_Osirely", false)],
        };

        var index = Build(raw).Tables.Single().Indexes.Single();

        Assert.Empty(index.Columns);
        Assert.Empty(index.IncludedColumns);
    }

    [Fact]
    public void Cizi_klic_paruje_sloupce_v_poradi()
    {
        var raw = new RawSchema
        {
            Tables = [new RawTable(null, "Child"), new RawTable(null, "Parent")],
            Columns =
            [
                new RawColumn(null, "Child", "A", 1, "int", false),
                new RawColumn(null, "Child", "B", 2, "int", false),
                new RawColumn(null, "Parent", "X", 1, "int", false),
            ],
            ForeignKeys = [new RawForeignKey(null, "Child", "FK", null, "Parent", "SET_NULL")],
            ForeignKeyColumns =
            [
                new RawForeignKeyColumn(null, "Child", "FK", "B", "Y", 2),
                new RawForeignKeyColumn(null, "Child", "FK", "A", "X", 1),
            ],
        };

        var foreignKey = Build(raw).FindTable(new DbObjectName(null, "Child"))!.ForeignKeys.Single();

        Assert.Equal(["A", "B"], foreignKey.Columns.ToList());
        Assert.Equal(["X", "Y"], foreignKey.PrincipalColumns.ToList());
        Assert.Equal(DbDeleteBehavior.SetNull, foreignKey.DeleteBehavior);
    }

    [Fact]
    public void Cizi_klic_bez_sloupcu_zustane_prazdny()
    {
        var raw = new RawSchema
        {
            Tables = [new RawTable(null, "Child")],
            ForeignKeys = [new RawForeignKey(null, "Child", "FK", null, "Parent")],
        };

        var foreignKey = Build(raw).Tables.Single().ForeignKeys.Single();

        Assert.Empty(foreignKey.Columns);
        Assert.Equal(DbDeleteBehavior.Unknown, foreignKey.DeleteBehavior);
    }

    [Fact]
    public void Check_constrainty_jsou_serazene()
    {
        var raw = new RawSchema
        {
            Tables = [new RawTable(null, "T")],
            CheckConstraints =
            [
                new RawCheckConstraint(null, "T", "CK_B", "[B] > 0"),
                new RawCheckConstraint(null, "T", "CK_A", "[A] > 0"),
            ],
        };

        var checks = Build(raw).Tables.Single().CheckConstraints;

        Assert.Equal(["CK_A", "CK_B"], checks.Select(c => c.Name).ToList());
    }

    [Fact]
    public void Pohled_se_pozna()
    {
        var raw = new RawSchema
        {
            Tables = [new RawTable("dbo", "OrderSummary", IsView: true)],
            Columns = [new RawColumn("dbo", "OrderSummary", "Total", 1, "decimal(18,2)", false, Precision: 18, Scale: 2)],
        };

        var view = Build(raw).Tables.Single();

        Assert.True(view.IsView);
        Assert.Equal(18, view.Columns[0].Precision);
        Assert.Equal(2, view.Columns[0].Scale);
    }

    [Fact]
    public void Pocty_radku_se_nacitaji_jen_na_vyzadani()
    {
        var raw = OrdersAndCustomers() with
        {
            RowCounts = [new RawRowCount("dbo", "Orders", 4200)],
        };

        var without = Build(raw);
        Assert.Null(without.FindTable(new DbObjectName("dbo", "Orders"))!.RowCountEstimate);

        var with = Build(raw, new SchemaReadOptions { IncludeMigrations = false, IncludeRowCounts = true });
        Assert.Equal(4200, with.FindTable(new DbObjectName("dbo", "Orders"))!.RowCountEstimate);
        Assert.Null(with.FindTable(new DbObjectName("dbo", "Customers"))!.RowCountEstimate);
    }

    [Fact]
    public void Migrace_z_databaze_jsou_oznacene_jako_nasazene()
    {
        var raw = new RawSchema { AppliedMigrations = ["20260201_B", "20260101_A", "20260101_A"] };

        var migrations = Build(raw, new SchemaReadOptions { IncludeMigrations = true }).Migrations;

        Assert.Equal(["20260101_A", "20260201_B"], migrations.Select(m => m.Id).ToList());
        Assert.All(migrations, m => Assert.True(m.AppliedInDatabase));
        Assert.All(migrations, m => Assert.False(m.PresentInAssembly));
        Assert.All(migrations, m => Assert.True(m.IsOrphaned));
    }

    [Fact]
    public void Vypnute_migrace_se_do_vysledku_nedostanou()
    {
        var raw = new RawSchema { AppliedMigrations = ["20260101_A"] };

        Assert.Empty(Build(raw, new SchemaReadOptions { IncludeMigrations = false }).Migrations);
    }

    [Fact]
    public void Skryte_tabulky_se_nenactou_ani_jejich_vztahy()
    {
        var schema = Build(OrdersAndCustomers(), new SchemaReadOptions
        {
            IncludeMigrations = false,
            HideTables = ["Customers"],
        });

        Assert.Equal("dbo.Orders", schema.Tables.Single().Qualified);
        Assert.Empty(schema.Relationships);
    }

    [Fact]
    public void Upozorneni_ze_cteni_projdou_do_vysledku()
    {
        var raw = new RawSchema { Warnings = ["něco se nepovedlo"] };

        Assert.Equal("něco se nepovedlo", Assert.Single(Build(raw).Warnings));
    }

    [Fact]
    public void Vazebni_tabulka_se_pozna_a_hrany_se_sbali()
    {
        var schema = Build(JoinTableSchema());

        var joinTable = schema.FindTable(new DbObjectName(null, "ProductTags"))!;
        Assert.True(joinTable.IsJoinTable);

        var relationship = Assert.Single(schema.Relationships);
        Assert.Equal(DbCardinality.ManyToMany, relationship.Cardinality);
        Assert.Equal("Products", relationship.From.Name);
        Assert.Equal("Tags", relationship.To.Name);
        Assert.Equal("ProductTags", relationship.ViaJoinTable!.Value.Name);
    }

    [Fact]
    public void Vypnuta_detekce_nechá_vazebni_tabulku_jako_dva_vztahy()
    {
        var schema = Build(JoinTableSchema(), new SchemaReadOptions
        {
            IncludeMigrations = false,
            DetectJoinTables = false,
        });

        Assert.False(schema.FindTable(new DbObjectName(null, "ProductTags"))!.IsJoinTable);
        Assert.Equal(2, schema.Relationships.Count);
        Assert.All(schema.Relationships, r => Assert.Equal(DbCardinality.OneToMany, r.Cardinality));
    }

    private static RawSchema JoinTableSchema() => new()
    {
        Tables =
        [
            new RawTable(null, "Products"),
            new RawTable(null, "Tags"),
            new RawTable(null, "ProductTags"),
        ],
        Columns =
        [
            new RawColumn(null, "Products", "Id", 1, "int", false),
            new RawColumn(null, "Tags", "Id", 1, "int", false),
            new RawColumn(null, "ProductTags", "ProductId", 1, "int", false),
            new RawColumn(null, "ProductTags", "TagId", 2, "int", false),
        ],
        KeyColumns =
        [
            new RawKeyColumn(null, "Products", "PK_Products", "Id", 1),
            new RawKeyColumn(null, "Tags", "PK_Tags", "Id", 1),
            new RawKeyColumn(null, "ProductTags", "PK_ProductTags", "ProductId", 1),
            new RawKeyColumn(null, "ProductTags", "PK_ProductTags", "TagId", 2),
        ],
        ForeignKeys =
        [
            new RawForeignKey(null, "ProductTags", "FK_ProductTags_Products", null, "Products", "CASCADE"),
            new RawForeignKey(null, "ProductTags", "FK_ProductTags_Tags", null, "Tags", "CASCADE"),
        ],
        ForeignKeyColumns =
        [
            new RawForeignKeyColumn(null, "ProductTags", "FK_ProductTags_Products", "ProductId", "Id", 1),
            new RawForeignKeyColumn(null, "ProductTags", "FK_ProductTags_Tags", "TagId", "Id", 1),
        ],
    };
}

public class DeleteActionParsingTests
{
    [Theory]
    [InlineData("NO_ACTION", DbDeleteBehavior.NoAction)]
    [InlineData("NO ACTION", DbDeleteBehavior.NoAction)]
    [InlineData("no action", DbDeleteBehavior.NoAction)]
    [InlineData("RESTRICT", DbDeleteBehavior.Restrict)]
    [InlineData("CASCADE", DbDeleteBehavior.Cascade)]
    [InlineData("SET_NULL", DbDeleteBehavior.SetNull)]
    [InlineData("SET NULL", DbDeleteBehavior.SetNull)]
    [InlineData("SET_DEFAULT", DbDeleteBehavior.SetDefault)]
    [InlineData("  cascade  ", DbDeleteBehavior.Cascade)]
    public void Znama_akce_se_prelozi(string action, DbDeleteBehavior expected) =>
        Assert.Equal(expected, LiveSchemaAssembler.ParseDeleteAction(action));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NĚCO_JINÉHO")]
    public void Nezname_nebo_chybejici_akce_je_Unknown(string? action) =>
        Assert.Equal(DbDeleteBehavior.Unknown, LiveSchemaAssembler.ParseDeleteAction(action));
}
