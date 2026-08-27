using DbsViewer.Analysis;
using DbsViewer.Relational;
using DbsViewer.Sqlite;
using Microsoft.Data.Sqlite;

namespace DbsViewer.Tests.Relational;

public class ConnectionScopeTests
{
    [Fact]
    public async Task Zavrene_pripojeni_se_otevre_a_zase_zavre()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);

        await using (await ConnectionScope.OpenAsync(connection))
        {
            Assert.Equal(System.Data.ConnectionState.Open, connection.State);
        }

        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
    }

    [Fact]
    public async Task Uz_otevrene_pripojeni_zustane_otevrene()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await using (await ConnectionScope.OpenAsync(connection))
        {
            Assert.Equal(System.Data.ConnectionState.Open, connection.State);
        }

        // Cizí připojení patří volajícímu — scope ho zavřít nesmí.
        Assert.Equal(System.Data.ConnectionState.Open, connection.State);
    }

    [Fact]
    public void Zdroj_pujci_sve_pripojeni()
    {
        var source = new SqliteSchemaSource("Data Source=:memory:");

        using var connection = ((IDbConnectionProvider)source).GetConnection();

        Assert.IsType<SqliteConnection>(connection);
    }

    [Fact]
    public void Zdroj_nad_cizim_pripojenim_vraci_to_same()
    {
        using var original = new SqliteConnection("Data Source=:memory:");
        var source = new SqliteSchemaSource(original);

        Assert.Same(original, ((IDbConnectionProvider)source).GetConnection());
    }
}

public class ViewComparisonTests
{
    private static DatabaseSchema Schema(params DbTable[] tables) => new() { Tables = tables };

    private static DbTable View(string name, bool isView, params DbColumn[] columns) => new()
    {
        Name = new DbObjectName(null, name),
        IsView = isView,
        Columns = columns,
    };

    private static DbColumn Column(string name, bool nullable = false, string storeType = "int") => new()
    {
        Name = name,
        Ordinal = 1,
        StoreType = storeType,
        IsNullable = nullable,
    };

    [Fact]
    public void U_pohledu_se_nullabilita_sloupcu_neporovnava()
    {
        // Pohled atributy nedeklaruje — plynou z dotazu a databáze je často nevystavuje.
        var model = Schema(View("V", true, Column("A")));
        var database = Schema(View("V", true, Column("A", nullable: true, storeType: "text")));

        Assert.Empty(SchemaComparer.Compare(model, database).Findings);
    }

    [Fact]
    public void U_pohledu_se_chybejici_sloupec_stale_hlasi()
    {
        var model = Schema(View("V", true, Column("A"), Column("B")));
        var database = Schema(View("V", true, Column("A")));

        var finding = Assert.Single(SchemaComparer.Compare(model, database).Findings);

        Assert.Equal(DiffKind.ColumnMissingInDatabase, finding.Kind);
        Assert.Equal("B", finding.Object);
    }

    [Fact]
    public void Priznak_pohledu_z_jedne_strany_staci()
    {
        var model = Schema(View("V", true, Column("A")));
        var database = Schema(View("V", false, Column("A", nullable: true)));

        Assert.Empty(SchemaComparer.Compare(model, database).Findings);
    }

    [Fact]
    public void U_tabulky_se_nullabilita_hlasi()
    {
        var model = Schema(View("T", false, Column("A")));
        var database = Schema(View("T", false, Column("A", nullable: true)));

        Assert.Equal(
            DiffKind.ColumnNullabilityMismatch,
            Assert.Single(SchemaComparer.Compare(model, database).Findings).Kind);
    }

    [Fact]
    public void U_pohledu_se_neporovnavaji_klice_ani_indexy()
    {
        var model = new DatabaseSchema
        {
            Tables =
            [
                new DbTable
                {
                    Name = new DbObjectName(null, "V"),
                    IsView = true,
                    PrimaryKey = new DbPrimaryKey { Columns = ["A"] },
                    Indexes = [new DbIndex { Name = "IX", Columns = ["A"] }],
                },
            ],
        };

        var database = Schema(View("V", true));

        Assert.Empty(SchemaComparer.Compare(model, database).Findings);
    }
}
