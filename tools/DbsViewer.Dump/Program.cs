using System.Text;
using System.Text.Json;
using DbsViewer.Analysis;
using DbsViewer.EfCore;
using DbsViewer.SampleShop;
using DbsViewer.Sqlite;
using DbsViewer.SqlServer;

namespace DbsViewer.Dump;

/// <summary>
/// Ověřovací nástroj: načte schéma z EF modelu nebo z živé databáze, vypíše ho
/// a volitelně porovná obojí.
/// </summary>
public static class Program
{
    private const string Usage = """
        DbsViewer.Dump — výpis databázového schématu

        Zdroj (bez zadání se použije ukázkový EF model nad SQLite):
          --ef                       ukázkový EF model
          --sqlserver <conn>         živá SQL Server databáze
          --sqlite <conn|cesta>      živá SQLite databáze
          --diff <conn>              porovná ukázkový EF model proti databázi
          --merged <conn>            sloučí ukázkový EF model s databází

        Volby:
          --json <soubor>            uloží výsledek jako JSON
          --rows                     zjistí odhad počtu řádků
          --hide <vzor>[,<vzor>]     skryje tabulky (podporuje *)
          --schemas <a>[,<b>]        načte jen uvedená schémata
        """;

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Contains("--help") || args.Contains("-h"))
        {
            Console.WriteLine(Usage);
            return 0;
        }

        try
        {
            return await RunAsync(args).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Chyba: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        var options = new SchemaReadOptions
        {
            IncludeRowCounts = args.Contains("--rows"),
            IncludeMigrations = true,
            HideTables = SplitList(GetOption(args, "--hide")),
            IncludeSchemas = SplitList(GetOption(args, "--schemas")),
        };

        if (GetOption(args, "--diff") is { } diffConnection)
        {
            return await RunDiffAsync(diffConnection, options, GetOption(args, "--json")).ConfigureAwait(false);
        }

        if (GetOption(args, "--merged") is { } mergedConnection)
        {
            return await RunMergedAsync(mergedConnection, options, GetOption(args, "--json")).ConfigureAwait(false);
        }

        var schema = await ReadSingleAsync(args, options).ConfigureAwait(false);
        Console.WriteLine(SchemaTextWriter.Render(schema));

        await WriteJsonAsync(GetOption(args, "--json"), schema).ConfigureAwait(false);
        return 0;
    }

    private static async Task<DatabaseSchema> ReadSingleAsync(string[] args, SchemaReadOptions options)
    {
        if (GetOption(args, "--sqlserver") is { } sqlServer)
        {
            return await new SqlServerSchemaSource(sqlServer).ReadAsync(options).ConfigureAwait(false);
        }

        if (GetOption(args, "--sqlite") is { } sqlite)
        {
            return await new SqliteSchemaSource(AsConnectionString(sqlite)).ReadAsync(options)
                .ConfigureAwait(false);
        }

        await using var context = ShopContextFactory.CreateSqlite();
        return await new EfCoreModelSchemaSource(context)
            .ReadAsync(options with { IncludeMigrations = false })
            .ConfigureAwait(false);
    }

    private static async Task<int> RunDiffAsync(
        string connectionString,
        SchemaReadOptions options,
        string? jsonPath)
    {
        var (model, database) = await ReadBothAsync(connectionString, options).ConfigureAwait(false);
        var diff = SchemaComparer.Compare(model, database);

        Console.WriteLine(SchemaTextWriter.RenderDiff(diff, model, database));

        if (jsonPath is not null)
        {
            var json = JsonSerializer.Serialize(diff, DbsViewerJson.Readable);
            await File.WriteAllTextAsync(jsonPath, json, Encoding.UTF8).ConfigureAwait(false);
            Console.WriteLine($"JSON zapsán do {Path.GetFullPath(jsonPath)}");
        }

        // Nenulový návratový kód umožňuje použít nástroj jako kontrolu v CI.
        return diff.ErrorCount > 0 ? 2 : 0;
    }

    private static async Task<int> RunMergedAsync(
        string connectionString,
        SchemaReadOptions options,
        string? jsonPath)
    {
        var (model, database) = await ReadBothAsync(connectionString, options).ConfigureAwait(false);
        var merged = SchemaMerger.Merge(model, database);

        Console.WriteLine(SchemaTextWriter.Render(merged));
        await WriteJsonAsync(jsonPath, merged).ConfigureAwait(false);
        return 0;
    }

    private static async Task<(DatabaseSchema Model, DatabaseSchema Database)> ReadBothAsync(
        string connectionString,
        SchemaReadOptions options)
    {
        // SQL Server connection string vždy obsahuje Server= nebo Data Source= s instancí;
        // cokoli jiného se bere jako cesta k souboru SQLite.
        var isSqlite = !connectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase)
            && !connectionString.Contains("Initial Catalog", StringComparison.OrdinalIgnoreCase);

        var sqliteConnectionString = AsConnectionString(connectionString);

        await using var context = isSqlite
            ? ShopContextFactory.CreateSqliteRaw(sqliteConnectionString)
            : ShopContextFactory.CreateSqlServer(connectionString);

        var model = await new EfCoreModelSchemaSource(context).ReadAsync(options).ConfigureAwait(false);

        ISchemaSource live = isSqlite
            ? new SqliteSchemaSource(sqliteConnectionString)
            : new SqlServerSchemaSource(connectionString);

        var database = await live.ReadAsync(options).ConfigureAwait(false);

        return (model, database);
    }

    private static async Task WriteJsonAsync(string? path, DatabaseSchema schema)
    {
        if (path is null)
        {
            return;
        }

        var json = JsonSerializer.Serialize(schema, DbsViewerJson.Readable);
        await File.WriteAllTextAsync(path, json, Encoding.UTF8).ConfigureAwait(false);
        Console.WriteLine($"JSON zapsán do {Path.GetFullPath(path)} ({json.Length:N0} znaků)");
    }

    /// <summary>Holá cesta k souboru se doplní na connection string.</summary>
    private static string AsConnectionString(string value) =>
        value.Contains('=', StringComparison.Ordinal) ? value : $"Data Source={value}";

    private static IReadOnlyList<string> SplitList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
