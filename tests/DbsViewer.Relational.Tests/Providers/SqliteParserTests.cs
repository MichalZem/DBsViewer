using DbsViewer.Sqlite;
using Microsoft.Data.Sqlite;

namespace DbsViewer.Tests.Relational;

/// <summary>
/// Čtení údajů, které SQLite přes <c>PRAGMA</c> nevystavuje a musí se vyčíst
/// z deklarovaného typu nebo z původního <c>CREATE TABLE</c>.
/// </summary>
public class SqliteTypeParserTests
{
    [Theory]
    [InlineData("nvarchar(200)", 200, null, null)]
    [InlineData("varchar (50)", 50, null, null)]
    [InlineData("TEXT(10)", 10, null, null)]
    public void Delka_se_vycte_z_deklarovaneho_typu(
        string declared, int? maxLength, int? precision, int? scale)
    {
        var facets = SqliteTypeParser.ParseFacets(declared);

        Assert.Equal(maxLength, facets.MaxLength);
        Assert.Equal(precision, facets.Precision);
        Assert.Equal(scale, facets.Scale);
    }

    [Theory]
    [InlineData("decimal(18, 2)", 18, 2)]
    [InlineData("numeric(9,4)", 9, 4)]
    public void Presnost_a_meritko_se_vyctou(string declared, int precision, int scale)
    {
        var facets = SqliteTypeParser.ParseFacets(declared);

        Assert.Null(facets.MaxLength);
        Assert.Equal(precision, facets.Precision);
        Assert.Equal(scale, facets.Scale);
    }

    [Theory]
    [InlineData("INTEGER")]
    [InlineData("TEXT")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("nvarchar()")]
    public void Typ_bez_zavorek_nema_zadne_udaje(string? declared)
    {
        var facets = SqliteTypeParser.ParseFacets(declared);

        Assert.Null(facets.MaxLength);
        Assert.Null(facets.Precision);
        Assert.Null(facets.Scale);
    }

    [Fact]
    public void Prilis_velke_cislo_v_typu_se_ignoruje()
    {
        // Číslo mimo rozsah int se nemá pokusit uložit — raději se údaj vynechá.
        var facets = SqliteTypeParser.ParseFacets("nvarchar(99999999999999999999)");

        Assert.Null(facets.MaxLength);
    }

    [Fact]
    public void Generovany_sloupec_se_najde_v_DDL()
    {
        const string Ddl = """
            CREATE TABLE Orders (
                Quantity  INTEGER NOT NULL,
                UnitPrice TEXT    NOT NULL,
                Total     TEXT    GENERATED ALWAYS AS (Quantity * UnitPrice) STORED
            )
            """;

        var generated = SqliteTypeParser.FindGeneratedColumns(Ddl);

        Assert.Equal("Quantity * UnitPrice", generated["Total"].Expression);
        Assert.True(generated["Total"].IsStored);
        Assert.Single(generated);
    }

    [Fact]
    public void Virtualni_generovany_sloupec_se_pozna()
    {
        const string Ddl = """
            CREATE TABLE T ("Full" TEXT GENERATED ALWAYS AS (A || B) VIRTUAL)
            """;

        Assert.False(SqliteTypeParser.FindGeneratedColumns(Ddl)["Full"].IsStored);
    }

    [Fact]
    public void Generovany_sloupec_bez_urceni_ulozeni_je_virtualni()
    {
        const string Ddl = "CREATE TABLE T (X TEXT GENERATED ALWAYS AS (A + 1))";

        Assert.False(SqliteTypeParser.FindGeneratedColumns(Ddl)["X"].IsStored);
    }

    [Fact]
    public void Jmeno_sloupce_se_zbavi_uvozovek()
    {
        const string Ddl = """
            CREATE TABLE T ([Order Total] TEXT GENERATED ALWAYS AS (A * B) STORED)
            """;

        Assert.True(SqliteTypeParser.FindGeneratedColumns(Ddl).ContainsKey("Order Total"));
    }

    [Fact]
    public void Vnorene_zavorky_ve_vyrazu_se_zvladnou()
    {
        const string Ddl = "CREATE TABLE T (X TEXT GENERATED ALWAYS AS (ROUND(A * B, 2)) STORED)";

        Assert.Equal("ROUND(A * B, 2)", SqliteTypeParser.FindGeneratedColumns(Ddl)["X"].Expression);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("CREATE TABLE T (A INTEGER)")]
    public void DDL_bez_generovanych_sloupcu_da_prazdny_vysledek(string? ddl) =>
        Assert.Empty(SqliteTypeParser.FindGeneratedColumns(ddl));

    [Fact]
    public void Jmeno_ciziho_klice_odpovida_konvenci_EF()
    {
        Assert.Equal("FK_Orders_Customers", SqliteTypeParser.ForeignKeyName("Orders", "Customers", 0));
        Assert.Equal("FK_Orders_Customers_1", SqliteTypeParser.ForeignKeyName("Orders", "Customers", 1));
    }
}

public class SqliteQueryTests
{
    [Theory]
    [InlineData("Orders", "\"Orders\"")]
    [InlineData("Order\"s", "\"Order\"\"s\"")]
    [InlineData("", "\"\"")]
    public void Identifikator_se_escapuje(string identifier, string expected) =>
        Assert.Equal(expected, SqliteQueries.Quote(identifier));

    [Fact]
    public void Chybejici_identifikator_je_chyba() =>
        Assert.Throws<ArgumentNullException>(() => SqliteQueries.Quote(null!));

    [Fact]
    public void Dotazy_pouzivaji_escapovane_jmeno()
    {
        Assert.Equal("PRAGMA table_xinfo(\"Orders\")", SqliteQueries.TableInfo("Orders"));
        Assert.Equal("PRAGMA index_list(\"Orders\")", SqliteQueries.IndexList("Orders"));
        Assert.Equal("PRAGMA index_info(\"IX\")", SqliteQueries.IndexInfo("IX"));
        Assert.Equal("PRAGMA foreign_key_list(\"Orders\")", SqliteQueries.ForeignKeyList("Orders"));
        Assert.Equal("SELECT COUNT(*) FROM \"Orders\"", SqliteQueries.RowCount("Orders"));
    }
}

/// <summary>
/// Chování při rozbité historii migrací. Nedostupná nebo poškozená tabulka nesmí
/// shodit načtení celého schématu.
/// </summary>
public class MigrationHistoryFailureTests
{
    [Fact]
    public async Task Poskozena_historie_migraci_v_SQLite_jen_upozorni()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await using (var command = connection.CreateCommand())
        {
            // Tabulka existuje, ale nemá očekávaný sloupec — dotaz na MigrationId selže.
            command.CommandText = """
                CREATE TABLE T (Id INTEGER PRIMARY KEY);
                CREATE TABLE __EFMigrationsHistory (NecoJineho TEXT NOT NULL PRIMARY KEY);
                """;
            await command.ExecuteNonQueryAsync();
        }

        var schema = await new SqliteSchemaSource(connection).ReadAsync(SchemaReadOptions.Default);

        var warning = Assert.Single(schema.Warnings);
        Assert.Contains("Historii migrací", warning, StringComparison.Ordinal);
        Assert.Empty(schema.Migrations);

        // Zbytek schématu se načíst musí.
        Assert.Equal(2, schema.Tables.Count);
    }
}
