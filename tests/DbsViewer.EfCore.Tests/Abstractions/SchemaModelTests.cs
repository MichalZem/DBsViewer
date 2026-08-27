using System.Text.Json;

namespace DbsViewer.Tests.Abstractions;

public class SchemaModelTests
{
    private static DbTable Table(string name, params string[] columns) => new()
    {
        Name = new DbObjectName("dbo", name),
        Columns = [.. columns.Select((c, i) => new DbColumn
        {
            Name = c,
            Ordinal = i + 1,
            StoreType = "int",
        })],
    };

    [Fact]
    public void FindTable_hleda_bez_ohledu_na_velikost_pismen()
    {
        var schema = new DatabaseSchema { Tables = [Table("Orders", "Id"), Table("Customers", "Id")] };

        Assert.NotNull(schema.FindTable(new DbObjectName("DBO", "orders")));
        Assert.Null(schema.FindTable(new DbObjectName("dbo", "Products")));
        Assert.Null(schema.FindTable(new DbObjectName("sales", "Orders")));
        Assert.Equal(2, schema.TableCount);
    }

    [Fact]
    public void FindTable_v_prazdnem_schematu_vraci_null() =>
        Assert.Null(new DatabaseSchema().FindTable(new DbObjectName(null, "Orders")));

    [Fact]
    public void FindColumn_hleda_bez_ohledu_na_velikost_pismen()
    {
        var table = Table("Orders", "Id", "Number");

        Assert.NotNull(table.FindColumn("NUMBER"));
        Assert.Null(table.FindColumn("Missing"));
        Assert.Equal("dbo.Orders", table.Qualified);
        Assert.Equal("dbo.Orders", table.ToString());
    }

    [Fact]
    public void Vychozi_schema_ma_prazdne_kolekce()
    {
        var schema = new DatabaseSchema();

        Assert.Empty(schema.Tables);
        Assert.Empty(schema.Relationships);
        Assert.Empty(schema.Migrations);
        Assert.Empty(schema.Warnings);
        Assert.Equal(SchemaSourceKind.EfModel, schema.SourceKind);
        Assert.True(schema.GeneratedAtUtc <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Popisky_modelu_jsou_citelne()
    {
        var column = new DbColumn { Name = "Email", StoreType = "nvarchar(256)", IsNullable = true };
        Assert.Equal("Email nvarchar(256) NULL", column.ToString());

        var required = new DbColumn { Name = "Id", StoreType = "int" };
        Assert.Equal("Id int NOT NULL", required.ToString());

        var primaryKey = new DbPrimaryKey { Columns = ["OrderId", "LineNumber"] };
        Assert.Equal("PK (OrderId, LineNumber)", primaryKey.ToString());

        var index = new DbIndex { Name = "UX_Email", Columns = ["Email"], IsUnique = true };
        Assert.Equal("UNIQUE INDEX UX_Email (Email)", index.ToString());

        var plainIndex = new DbIndex { Name = "IX_Name", Columns = ["Name"] };
        Assert.Equal("INDEX IX_Name (Name)", plainIndex.ToString());

        var foreignKey = new DbForeignKey
        {
            Name = "FK_Orders_Customers",
            Columns = ["CustomerId"],
            PrincipalTable = new DbObjectName("dbo", "Customers"),
            PrincipalColumns = ["Id"],
        };
        Assert.Equal(
            "FK FK_Orders_Customers: (CustomerId) -> dbo.Customers(Id)",
            foreignKey.ToString());
    }

    [Theory]
    [InlineData(DbCardinality.OneToMany, "dbo.Orders >--- dbo.Customers")]
    [InlineData(DbCardinality.OneToOne, "dbo.Orders --- dbo.Customers")]
    public void Popisek_vztahu_odpovida_kardinalite(DbCardinality cardinality, string expected)
    {
        var relationship = new DbRelationship
        {
            Id = "x",
            From = new DbObjectName("dbo", "Orders"),
            To = new DbObjectName("dbo", "Customers"),
            Cardinality = cardinality,
        };

        Assert.Equal(expected, relationship.ToString());
    }

    [Fact]
    public void Popisek_vztahu_NM_uvadi_vazebni_tabulku()
    {
        var relationship = new DbRelationship
        {
            Id = "x",
            From = new DbObjectName(null, "Products"),
            To = new DbObjectName(null, "Tags"),
            Cardinality = DbCardinality.ManyToMany,
            ViaJoinTable = new DbObjectName(null, "ProductTags"),
        };

        Assert.Equal("Products >--< Tags (via ProductTags)", relationship.ToString());
    }

    [Fact]
    public void Vztah_pozna_self_referenci()
    {
        var self = new DbObjectName("dbo", "Categories");
        var relationship = new DbRelationship { Id = "x", From = self, To = self };

        Assert.True(relationship.IsSelfReference);
        Assert.Empty(relationship.FromColumns);
        Assert.Empty(relationship.ToColumns);
    }

    [Theory]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, false, true)]
    [InlineData(true, true, false, false)]
    [InlineData(false, false, false, false)]
    public void Stav_migrace_vychazi_z_pritomnosti_v_kodu_a_v_databazi(
        bool inAssembly,
        bool inDatabase,
        bool pending,
        bool orphaned)
    {
        var migration = new DbMigration
        {
            Id = "20260101_Init",
            PresentInAssembly = inAssembly,
            AppliedInDatabase = inDatabase,
        };

        Assert.Equal(pending, migration.IsPending);
        Assert.Equal(orphaned, migration.IsOrphaned);
    }

    [Fact]
    public void Serializace_pouziva_camelCase_a_textove_enumy()
    {
        var schema = new DatabaseSchema
        {
            DatabaseName = "Shop",
            Provider = DbProviderKind.SqlServer,
            SourceKind = SchemaSourceKind.Merged,
            Tables = [Table("Orders", "Id")],
        };

        var json = JsonSerializer.Serialize(schema, DbsViewerJson.Compact);

        Assert.Contains("\"databaseName\":\"Shop\"", json, StringComparison.Ordinal);
        Assert.Contains("\"provider\":\"SqlServer\"", json, StringComparison.Ordinal);
        Assert.Contains("\"sourceKind\":\"Merged\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"defaultSchema\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Citelna_serializace_je_odsazena_a_da_se_nacist_zpet()
    {
        var schema = new DatabaseSchema { DatabaseName = "Shop", Tables = [Table("Orders", "Id")] };

        var json = JsonSerializer.Serialize(schema, DbsViewerJson.Readable);
        var roundTrip = JsonSerializer.Deserialize<DatabaseSchema>(json, DbsViewerJson.Readable);

        Assert.Contains("\n", json, StringComparison.Ordinal);
        Assert.NotNull(roundTrip);
        Assert.Equal("Shop", roundTrip.DatabaseName);
        Assert.Equal("dbo.Orders", roundTrip.Tables.Single().Qualified);
    }

    [Fact]
    public void Serializacni_kontext_zna_datovy_model()
    {
        var context = DbsViewerJsonContext.Default;

        Assert.NotNull(context.DatabaseSchema);
        Assert.NotNull(context.DbTable);
        Assert.NotNull(context.DbRelationship);
    }
}
