using DbsViewer.Server;

namespace DbsViewer.Tests.Server;

/// <summary>
/// Skládání SQL pro stránkovaný náhled. Jako jediné místo v komponentě staví dotaz
/// z něčeho, co přišlo z požadavku — testy proto hlídají hlavně to, co se do textu
/// dotazu smí a nesmí dostat.
/// </summary>
public class DataQueryBuilderTests
{
    private static DbTable Table(params string[] columns) => new()
    {
        Name = new DbObjectName(null, "Zakaznici"),
        Columns = [.. columns.Select((c, i) => new DbColumn { Name = c, Ordinal = i + 1, StoreType = "int" })],
        PrimaryKey = columns.Length > 0 ? new DbPrimaryKey { Columns = [columns[0]] } : null,
    };

    // ---------- stránkování ----------

    [Fact]
    public void Prvni_stranka_ma_nulovy_offset()
    {
        var sql = DataQueryBuilder
            .BuildPage(Table("Id"), new DataQuery { Page = 0, PageSize = 25 }, isSqlite: true)
            .Sql;

        Assert.EndsWith("LIMIT 25 OFFSET 0", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Offset_se_pocita_ze_stranky_a_velikosti()
    {
        var sql = DataQueryBuilder
            .BuildPage(Table("Id"), new DataQuery { Page = 4, PageSize = 100 }, isSqlite: true)
            .Sql;

        Assert.EndsWith("LIMIT 100 OFFSET 400", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Velky_offset_nepreteče()
    {
        // Stránka 30 milionů po 100 řádcích přeteče int; počítá se proto v long.
        var sql = DataQueryBuilder
            .BuildPage(Table("Id"), new DataQuery { Page = 30_000_000, PageSize = 100 }, isSqlite: true)
            .Sql;

        Assert.EndsWith("OFFSET 3000000000", sql, StringComparison.Ordinal);
    }

    // ---------- řazení ----------

    [Fact]
    public void Radi_se_podle_zvoleneho_sloupce()
    {
        var query = new DataQuery { SortColumn = "Jmeno", SortDescending = true };

        Assert.Contains(
            "ORDER BY \"Jmeno\" DESC",
            DataQueryBuilder.BuildPage(Table("Id", "Jmeno"), query, isSqlite: true).Sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Bez_razeni_se_pouzije_primarni_klic()
    {
        // Bez ORDER BY může databáze vracet řádky pokaždé jinak a při listování
        // by některé chyběly a jiné se opakovaly.
        Assert.Contains(
            "ORDER BY \"Id\"",
            DataQueryBuilder.BuildPage(Table("Id", "Jmeno"), new DataQuery(), isSqlite: true).Sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Slozeny_primarni_klic_se_pouzije_cely()
    {
        var table = Table("A", "B") with { PrimaryKey = new DbPrimaryKey { Columns = ["A", "B"] } };

        Assert.Contains(
            "ORDER BY \"A\", \"B\"",
            DataQueryBuilder.BuildPage(table, new DataQuery(), isSqlite: true).Sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Klic_ze_sloupcu_mimo_tabulku_spadne_na_nahradni_razeni()
    {
        // Primární klíč, jehož sloupce v tabulce nejsou — schéma se dá načíst po částech
        // a klíč může přežít sloupec, který zmizel. Do dotazu se smí dostat jen jméno
        // ze schématu, takže zbude náhradní řazení.
        var table = Table("Id") with { PrimaryKey = new DbPrimaryKey { Columns = ["Zmizely"] } };

        Assert.Contains(
            "ORDER BY (SELECT NULL)",
            DataQueryBuilder.BuildPage(table, new DataQuery(), isSqlite: false).Sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Tabulka_bez_klice_ma_v_SqlServeru_nahradni_razeni()
    {
        // SQL Server bez ORDER BY nepovolí OFFSET/FETCH, takže něco stát musí.
        var table = Table("Id") with { PrimaryKey = null };

        Assert.Contains(
            "ORDER BY (SELECT NULL)",
            DataQueryBuilder.BuildPage(table, new DataQuery(), isSqlite: false).Sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Tabulka_bez_klice_v_SQLite_razeni_nepotrebuje()
    {
        var table = Table("Id") with { PrimaryKey = null };
        var sql = DataQueryBuilder.BuildPage(table, new DataQuery(), isSqlite: true).Sql;

        Assert.DoesNotContain("ORDER BY", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Neznamy_sloupec_razeni_se_ignoruje()
    {
        // Řazení může zůstat z jiné tabulky; do dotazu se nesmí dostat.
        var query = new DataQuery { SortColumn = "Neexistuje" };
        var sql = DataQueryBuilder.BuildPage(Table("Id"), query, isSqlite: true).Sql;

        Assert.DoesNotContain("Neexistuje", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY \"Id\"", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Sloupec_razeni_se_bere_ze_schematu_ne_z_pozadavku()
    {
        // Do dotazu jde jméno ze schématu, takže se v něm objeví schématem daný zápis
        // bez ohledu na to, jak ho poslal klient.
        var query = new DataQuery { SortColumn = "jMeNo" };

        Assert.Contains(
            "ORDER BY \"Jmeno\"",
            DataQueryBuilder.BuildPage(Table("Id", "Jmeno"), query, isSqlite: true).Sql,
            StringComparison.Ordinal);
    }

    // ---------- filtry ----------

    [Fact]
    public void Hodnota_filtru_se_do_textu_dotazu_nedostane()
    {
        var query = new DataQuery
        {
            Filters = [new DataFilter("Jmeno", FilterOperator.Contains, "'; DROP TABLE Zakaznici;--")],
        };

        var built = DataQueryBuilder.BuildPage(Table("Id", "Jmeno"), query, isSqlite: true);

        Assert.DoesNotContain("DROP", built.Sql, StringComparison.Ordinal);
        Assert.Contains("@p0", built.Sql, StringComparison.Ordinal);
        Assert.Single(built.Parameters);
    }

    [Fact]
    public void Filtr_nad_neznamym_sloupcem_se_preskoci()
    {
        var query = new DataQuery
        {
            Filters = [new DataFilter("Neexistuje", FilterOperator.Contains, "x")],
        };

        var built = DataQueryBuilder.BuildPage(Table("Id"), query, isSqlite: true);

        Assert.DoesNotContain("WHERE", built.Sql, StringComparison.Ordinal);
        Assert.Empty(built.Parameters);
    }

    [Fact]
    public void Prazdna_hodnota_filtr_nevytvori()
    {
        var query = new DataQuery
        {
            Filters = [new DataFilter("Jmeno", FilterOperator.Contains, "")],
        };

        Assert.DoesNotContain(
            "WHERE",
            DataQueryBuilder.BuildPage(Table("Id", "Jmeno"), query, isSqlite: true).Sql,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(FilterOperator.Equals, "= @p0")]
    [InlineData(FilterOperator.GreaterThan, "> @p0")]
    [InlineData(FilterOperator.LessThan, "< @p0")]
    public void Porovnavaci_operatory_pouzivaji_parametr(FilterOperator op, string expected)
    {
        var query = new DataQuery { Filters = [new DataFilter("Id", op, "5")] };
        var built = DataQueryBuilder.BuildPage(Table("Id"), query, isSqlite: true);

        Assert.Contains(expected, built.Sql, StringComparison.Ordinal);
        Assert.Equal(["5"], built.Parameters);
    }

    [Theory]
    [InlineData(FilterOperator.IsNull, "IS NULL")]
    [InlineData(FilterOperator.IsNotNull, "IS NOT NULL")]
    public void Testy_na_NULL_parametr_nepotrebuji(FilterOperator op, string expected)
    {
        var query = new DataQuery { Filters = [new DataFilter("Jmeno", op, null)] };
        var built = DataQueryBuilder.BuildPage(Table("Id", "Jmeno"), query, isSqlite: true);

        Assert.Contains($"\"Jmeno\" {expected}", built.Sql, StringComparison.Ordinal);
        Assert.Empty(built.Parameters);
    }

    [Fact]
    public void Textove_hledani_pouziva_LIKE_nad_prevedenou_hodnotou()
    {
        // LIKE musí fungovat i nad čísly a daty — v mřížce uživatel typ nerozlišuje.
        var query = new DataQuery { Filters = [new DataFilter("Id", FilterOperator.Contains, "42")] };

        Assert.Contains(
            "CAST(\"Id\" AS TEXT) LIKE @p0",
            DataQueryBuilder.BuildPage(Table("Id"), query, isSqlite: true).Sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SqlServer_prevadi_na_nvarchar()
    {
        var query = new DataQuery { Filters = [new DataFilter("Id", FilterOperator.Contains, "42")] };

        Assert.Contains(
            "CAST([Id] AS NVARCHAR(MAX)) LIKE @p0",
            DataQueryBuilder.BuildPage(Table("Id"), query, isSqlite: false).Sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Vice_filtru_se_spoji_pres_AND()
    {
        var query = new DataQuery
        {
            Filters =
            [
                new DataFilter("Id", FilterOperator.Equals, "1"),
                new DataFilter("Jmeno", FilterOperator.Contains, "Adam"),
            ],
        };

        var built = DataQueryBuilder.BuildPage(Table("Id", "Jmeno"), query, isSqlite: true);

        Assert.Contains(" AND ", built.Sql, StringComparison.Ordinal);
        Assert.Equal(2, built.Parameters.Count);
        Assert.Contains("@p1", built.Sql, StringComparison.Ordinal);
    }

    // ---------- zástupné znaky ----------

    [Theory]
    [InlineData(FilterOperator.Contains, "%Adam%")]
    [InlineData(FilterOperator.StartsWith, "Adam%")]
    [InlineData(FilterOperator.EndsWith, "%Adam")]
    public void Vzor_odpovida_operatoru(FilterOperator op, string expected) =>
        Assert.Equal(expected, DataQueryBuilder.Wildcards(op, "Adam"));

    [Fact]
    public void Zastupne_znaky_v_hodnote_se_escapuji()
    {
        // Uživatel hledá text, ne zadává vzor: „100%" nesmí najít cokoli.
        Assert.Equal("%100\\%%", DataQueryBuilder.Wildcards(FilterOperator.Contains, "100%"));
        Assert.Equal("%a\\_b%", DataQueryBuilder.Wildcards(FilterOperator.Contains, "a_b"));
    }

    [Fact]
    public void Zpetne_lomitko_se_escapuje_prvni()
    {
        // Kdyby šlo až po procentu, escapovalo by se i to, co přidáváme my.
        Assert.Equal("%a\\\\b%", DataQueryBuilder.Wildcards(FilterOperator.Contains, "a\\b"));
    }

    [Fact]
    public void LIKE_ma_klauzuli_ESCAPE()
    {
        // Bez ní by zpětné lomítko bylo obyčejný znak a escapování by nefungovalo.
        var query = new DataQuery { Filters = [new DataFilter("Id", FilterOperator.Contains, "x")] };

        Assert.Contains(
            "ESCAPE '\\'",
            DataQueryBuilder.BuildPage(Table("Id"), query, isSqlite: true).Sql,
            StringComparison.Ordinal);
    }

    // ---------- počítání ----------

    [Fact]
    public void Pocet_respektuje_filtry()
    {
        var query = new DataQuery
        {
            Filters = [new DataFilter("Jmeno", FilterOperator.Contains, "Adam")],
        };

        var built = DataQueryBuilder.BuildCount(Table("Id", "Jmeno"), query, isSqlite: true);

        Assert.StartsWith("SELECT COUNT(*)", built.Sql, StringComparison.Ordinal);
        Assert.Contains("WHERE", built.Sql, StringComparison.Ordinal);
        Assert.Single(built.Parameters);
    }

    [Fact]
    public void Pocet_neradi_ani_nestrankuje()
    {
        // ORDER BY ani LIMIT v COUNT nic nepřinesou a jen by dotaz zdražily.
        var query = new DataQuery { Page = 3, SortColumn = "Jmeno" };
        var sql = DataQueryBuilder.BuildCount(Table("Id", "Jmeno"), query, isSqlite: true).Sql;

        Assert.DoesNotContain("ORDER BY", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("LIMIT", sql, StringComparison.Ordinal);
    }

    // ---------- escapování identifikátorů ----------

    [Theory]
    [InlineData("Jmeno", false, "[Jmeno]")]
    [InlineData("Jme]no", false, "[Jme]]no]")]
    [InlineData("Jmeno", true, "\"Jmeno\"")]
    [InlineData("Jme\"no", true, "\"Jme\"\"no\"")]
    public void Jmeno_sloupce_se_escapuje(string name, bool isSqlite, string expected) =>
        Assert.Equal(expected, DataQueryBuilder.QuoteColumn(name, isSqlite));

    [Fact]
    public void Sloupec_se_hleda_bez_ohledu_na_velikost_pismen()
    {
        Assert.NotNull(DataQueryBuilder.FindColumn(Table("Jmeno"), "JMENO"));
        Assert.Null(DataQueryBuilder.FindColumn(Table("Jmeno"), "Prijmeni"));
        Assert.Null(DataQueryBuilder.FindColumn(Table("Jmeno"), null));
        Assert.Null(DataQueryBuilder.FindColumn(Table("Jmeno"), ""));
    }

    // ---------- argumenty ----------

    [Fact]
    public void Null_argumenty_jsou_chyba()
    {
        Assert.Throws<ArgumentNullException>(
            () => DataQueryBuilder.BuildPage(null!, new DataQuery(), isSqlite: true));

        Assert.Throws<ArgumentNullException>(
            () => DataQueryBuilder.BuildPage(Table("Id"), null!, isSqlite: true));

        Assert.Throws<ArgumentNullException>(
            () => DataQueryBuilder.BuildCount(null!, new DataQuery(), isSqlite: true));

        Assert.Throws<ArgumentNullException>(
            () => DataQueryBuilder.BuildCount(Table("Id"), null!, isSqlite: true));

        Assert.Throws<ArgumentNullException>(() => DataQueryBuilder.Bind(null!, []));
    }

    [Fact]
    public void Bind_prida_parametry_ve_spravnem_poradi()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        using var command = connection.CreateCommand();

        DataQueryBuilder.Bind(command, ["prvni", "druha"]);

        Assert.Equal(2, command.Parameters.Count);
        Assert.Equal("@p0", command.Parameters[0].ParameterName);
        Assert.Equal("druha", command.Parameters[1].Value);
    }

    [Fact]
    public void Bind_bez_parametru_je_v_poradku()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        using var command = connection.CreateCommand();

        DataQueryBuilder.Bind(command, []);

        Assert.Empty(command.Parameters);
        Assert.Throws<ArgumentNullException>(() => DataQueryBuilder.Bind(command, null!));
    }

    [Fact]
    public void Filtr_bez_hodnoty_vi_ze_ji_nepotrebuje()
    {
        Assert.False(new DataFilter("A", FilterOperator.IsNull, null).NeedsValue);
        Assert.False(new DataFilter("A", FilterOperator.IsNotNull, null).NeedsValue);
        Assert.True(new DataFilter("A", FilterOperator.Contains, "x").NeedsValue);
    }

    // ---------- nastavení ----------

    [Theory]
    [InlineData(60, 60)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(99999, 300)]
    public void Casovy_limit_dotazu_se_orizne_na_rozumne_meze(int zadano, int ocekavano)
    {
        // Nula ani záporná hodnota nesmí znamenat "bez limitu" — o to tu jde.
        var options = new DbsViewerOptions();
        options.DataPreview.CommandTimeoutSeconds = zadano;

        Assert.Equal(ocekavano, options.DataPreview.CommandTimeoutSeconds);
    }
}
