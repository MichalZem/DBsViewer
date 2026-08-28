using DbsViewer.Analysis;
using DbsViewer.Dump;
using DbsViewer.TestKit;
using DbsViewer.Ui.Model;
using Microsoft.Data.Sqlite;

namespace DbsViewer.Tests.DumpTool;

public class CommandLineTests
{
    [Fact]
    public void Bez_argumentu_se_pouzije_ukazkovy_model()
    {
        var options = CommandLine.Parse([]);

        Assert.Equal(DumpAction.Show, options.Action);
        Assert.Equal(DumpSource.Sample, options.Source);
        Assert.Null(options.Connection);
        Assert.Null(options.Error);
        Assert.False(options.IncludeRowCounts);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Napoveda_se_pozna(string arg) =>
        Assert.Equal(DumpAction.Help, CommandLine.Parse([arg]).Action);

    [Fact]
    public void SqlServer_se_pozna_z_prepinace()
    {
        var options = CommandLine.Parse(["--sqlserver", "Server=.;Database=X"]);

        Assert.Equal(DumpSource.SqlServer, options.Source);
        Assert.Equal("Server=.;Database=X", options.Connection);
    }

    [Fact]
    public void Sqlite_se_pozna_z_prepinace()
    {
        var options = CommandLine.Parse(["--sqlite", "./app.db"]);

        Assert.Equal(DumpSource.Sqlite, options.Source);
        Assert.Equal("./app.db", options.Connection);
    }

    [Fact]
    public void Diff_a_merged_se_poznaji_i_s_providerem()
    {
        var diff = CommandLine.Parse(["--diff", "./app.db"]);
        Assert.Equal(DumpAction.Diff, diff.Action);
        Assert.Equal(DumpSource.Sqlite, diff.Source);

        var merged = CommandLine.Parse(["--merged", "Server=.;Database=X"]);
        Assert.Equal(DumpAction.Merged, merged.Action);
        Assert.Equal(DumpSource.SqlServer, merged.Source);
    }

    [Fact]
    public void Vystupni_volby_se_prectou()
    {
        var options = CommandLine.Parse(
            ["--json", "s.json", "--export", "s.md", "--format", "mermaid", "--rows"]);

        Assert.Equal("s.json", options.JsonPath);
        Assert.Equal("s.md", options.ExportPath);
        Assert.Equal("mermaid", options.ExportFormat);
        Assert.True(options.IncludeRowCounts);
    }

    [Fact]
    public void Filtry_se_rozdeli_carkou()
    {
        var options = CommandLine.Parse(["--hide", "A*, B*", "--schemas", "dbo,sales"]);

        Assert.Equal(["A*", "B*"], options.HideTables.ToList());
        Assert.Equal(["dbo", "sales"], options.IncludeSchemas.ToList());
    }

    [Fact]
    public void Prazdny_filtr_da_prazdny_seznam()
    {
        var options = CommandLine.Parse(["--hide", "  "]);

        Assert.Empty(options.HideTables);
    }

    [Fact]
    public void Neznamy_format_je_chyba()
    {
        var options = CommandLine.Parse(["--format", "pdf"]);

        Assert.NotNull(options.Error);
        Assert.Contains("mermaid", options.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Prepinac_bez_hodnoty_se_ignoruje()
    {
        // Poslední argument bez hodnoty by jinak vedl na přetečení pole.
        var options = CommandLine.Parse(["--json"]);

        Assert.Null(options.JsonPath);
    }

    [Theory]
    [InlineData("mermaid", ExportFormat.Mermaid)]
    [InlineData("mmd", ExportFormat.Mermaid)]
    [InlineData("MERMAID", ExportFormat.Mermaid)]
    [InlineData("dbml", ExportFormat.Dbml)]
    [InlineData("markdown", ExportFormat.Markdown)]
    [InlineData("md", ExportFormat.Markdown)]
    public void Formaty_se_prelozi(string text, ExportFormat expected) =>
        Assert.Equal(expected, CommandLine.ParseFormat(text));

    [Theory]
    [InlineData("pdf")]
    [InlineData("")]
    [InlineData(null)]
    public void Neznamy_format_se_neprelozi(string? text) =>
        Assert.Null(CommandLine.ParseFormat(text));

    [Theory]
    [InlineData("Server=.;Database=X", DumpSource.SqlServer)]
    [InlineData("server=localhost", DumpSource.SqlServer)]
    [InlineData("Data Source=x;Initial Catalog=Y", DumpSource.SqlServer)]
    [InlineData("./app.db", DumpSource.Sqlite)]
    [InlineData("Data Source=app.db", DumpSource.Sqlite)]
    public void Provider_se_pozna_z_connection_stringu(string connection, DumpSource expected) =>
        Assert.Equal(expected, CommandLine.Detect(connection));

    [Theory]
    [InlineData("./app.db", "Data Source=./app.db")]
    [InlineData("Data Source=x.db", "Data Source=x.db")]
    public void Cesta_se_doplni_na_connection_string(string input, string expected) =>
        Assert.Equal(expected, CommandLine.AsSqliteConnectionString(input));

    [Fact]
    public void Chybejici_vstupy_jsou_chyba()
    {
        Assert.Throws<ArgumentNullException>(() => CommandLine.Parse(null!));
        Assert.Throws<ArgumentNullException>(() => CommandLine.Detect(null!));
        Assert.Throws<ArgumentNullException>(() => CommandLine.AsSqliteConnectionString(null!));
    }

    [Fact]
    public void Prevod_na_volby_cteni_prenasi_filtry()
    {
        var read = CommandLine.Parse(["--rows", "--hide", "X*"]).ToReadOptions();

        Assert.True(read.IncludeRowCounts);
        Assert.Equal(["X*"], read.HideTables.ToList());
    }
}

/// <summary>Chování nástroje od začátku do konce, včetně zápisu souborů.</summary>
public class ProgramTests : IDisposable
{
    private readonly string _slozka = Path.Combine(
        Path.GetTempPath(),
        $"dbsview-testy-{Guid.NewGuid():N}");

    public ProgramTests() => Directory.CreateDirectory(_slozka);

    private string Cesta(string name) => Path.Combine(_slozka, name);

    private static async Task<(int Code, string Output, string Error)> RunAsync(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var code = await Program.RunAsync(args, output, error);

        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task Napoveda_se_vypise_a_skonci_v_poradku()
    {
        var (code, output, _) = await RunAsync("--help");

        Assert.Equal(0, code);
        Assert.Contains("dbsview", output, StringComparison.Ordinal);
        Assert.Contains("--diff", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bez_argumentu_se_vypise_ukazkovy_model()
    {
        var (code, output, _) = await RunAsync();

        Assert.Equal(0, code);
        Assert.Contains("Customers", output, StringComparison.Ordinal);
        Assert.Contains("Vztahy", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chyba_v_argumentech_vrati_kod_jedna()
    {
        var (code, _, error) = await RunAsync("--format", "pdf");

        Assert.Equal(1, code);
        Assert.Contains("Neznámý formát", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nedostupna_databaze_vrati_kod_jedna()
    {
        var (code, _, error) = await RunAsync("--sqlserver", "Server=neexistuje;Database=X;Connect Timeout=1");

        Assert.Equal(1, code);
        Assert.StartsWith("Chyba:", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JSON_se_zapise_na_disk()
    {
        var cesta = Cesta("schema.json");

        var (code, output, _) = await RunAsync("--json", cesta);

        Assert.Equal(0, code);
        Assert.True(File.Exists(cesta));
        Assert.Contains("JSON zapsán", output, StringComparison.Ordinal);
        Assert.Contains("\"tables\"", await File.ReadAllTextAsync(cesta), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("markdown", "# Schéma databáze")]
    [InlineData("mermaid", "erDiagram")]
    [InlineData("dbml", "Table \"")]
    public async Task Export_zapise_zvoleny_format(string format, string expected)
    {
        var cesta = Cesta($"schema.{format}");

        var (code, output, _) = await RunAsync("--export", cesta, "--format", format);

        Assert.Equal(0, code);
        Assert.Contains("Export", output, StringComparison.Ordinal);
        Assert.Contains(expected, await File.ReadAllTextAsync(cesta), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Skryte_tabulky_se_nevypisou()
    {
        var (_, output, _) = await RunAsync("--hide", "Product*");

        Assert.DoesNotContain("■ Products", output, StringComparison.Ordinal);
        Assert.Contains("■ Customers", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ziva_SQLite_databaze_se_precte()
    {
        var cesta = Cesta("ziva.db");
        await VytvorDatabaziAsync(cesta);

        var (code, output, _) = await RunAsync("--sqlite", cesta, "--rows");

        Assert.Equal(0, code);
        Assert.Contains("Blogs", output, StringComparison.Ordinal);
        Assert.Contains("řádků", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diff_shodneho_schematu_vrati_nulu()
    {
        var cesta = Cesta("shodna.db");
        await VytvorSchemaShopAsync(cesta);

        var (code, output, _) = await RunAsync("--diff", cesta);

        Assert.Equal(0, code);
        Assert.Contains("Model", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diff_s_driftem_vrati_kod_dva()
    {
        var cesta = Cesta("drift.db");
        await VytvorDatabaziAsync(cesta);

        var (code, output, _) = await RunAsync("--diff", cesta);

        // Databáze neodpovídá ukázkovému modelu, takže nálezů je spousta.
        Assert.Equal(2, code);
        Assert.Contains("Chyby", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diff_jde_ulozit_jako_JSON()
    {
        var databaze = Cesta("diff.db");
        var json = Cesta("diff.json");
        await VytvorDatabaziAsync(databaze);

        await RunAsync("--diff", databaze, "--json", json);

        Assert.True(File.Exists(json));
        Assert.Contains("findings", await File.ReadAllTextAsync(json), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Slouceny_pohled_spoji_model_i_databazi()
    {
        var cesta = Cesta("merged.db");
        await VytvorSchemaShopAsync(cesta);

        var (code, output, _) = await RunAsync("--merged", cesta);

        Assert.Equal(0, code);
        Assert.Contains("Merged", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chybejici_vystupy_jsou_chyba()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Program.RunAsync(null!, TextWriter.Null, TextWriter.Null));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Program.RunAsync([], null!, TextWriter.Null));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Program.RunAsync([], TextWriter.Null, null!));
    }

    private static async Task VytvorDatabaziAsync(string cesta)
    {
        await using var connection = new SqliteConnection($"Data Source={cesta}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE Blogs (Id INTEGER PRIMARY KEY, Nazev TEXT NOT NULL);
            INSERT INTO Blogs (Nazev) VALUES ('první'), ('druhý');
            """;
        await command.ExecuteNonQueryAsync();

        SqliteConnection.ClearAllPools();
    }

    /// <summary>Vytvoří databázi odpovídající ukázkovému modelu, aby byl diff čistý.</summary>
    private static async Task VytvorSchemaShopAsync(string cesta)
    {
        await using (var context = SampleShop.ShopContextFactory.CreateSqliteRaw($"Data Source={cesta}"))
        {
            await context.Database.EnsureCreatedAsync();
        }

        // EnsureCreated pohledy nevytváří, ale model je zná — bez toho by diff hlásil
        // chybějící tabulku, která z EF vůbec vytvořitelná není.
        await using (var connection = new SqliteConnection($"Data Source={cesta}"))
        {
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE VIEW OrderSummaries AS
                SELECT o.Id AS OrderId, o.Number, c.Email AS CustomerEmail, 0 AS Total
                FROM Orders o JOIN Customers c ON c.Id = o.CustomerId
                """;
            await command.ExecuteNonQueryAsync();
        }

        SqliteConnection.ClearAllPools();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_slozka, recursive: true);
        }
        catch (IOException)
        {
            // Soubor drží jiný proces — dočasná složka zůstane, test kvůli tomu selhat nemá.
        }
    }
}
