using System.Text;
using System.Text.Json;
using DbsViewer.Analysis;
using DbsViewer.EfCore;
using DbsViewer.SampleShop;
using DbsViewer.Sqlite;
using DbsViewer.SqlServer;
using DbsViewer.Ui.Model;

namespace DbsViewer.Dump;

/// <summary>
/// Konzolový nástroj: výpis schématu, porovnání s EF modelem a export dokumentace.
/// </summary>
public static class Program
{
    /// <summary>
    /// Vstupní bod. Jen nastaví kódování a předá řízení dál — testovat se dá
    /// <see cref="RunAsync"/>, kam jde podstrčit vlastní výstupy.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage(
        Justification = "Vstupní bod procesu; veškerá logika je v RunAsync, které testy volají.")]
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        return await RunAsync(args, Console.Out, Console.Error).ConfigureAwait(false);
    }

    /// <summary>
    /// Spuštění se zadanými výstupy. Testy sem podstrčí vlastní, aby šlo ověřit,
    /// co nástroj skutečně vypíše.
    /// </summary>
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var options = CommandLine.Parse(args);

        if (options.Action == DumpAction.Help)
        {
            output.WriteLine(CommandLine.Usage);
            return 0;
        }

        if (options.Error is { } chyba)
        {
            error.WriteLine($"Chyba: {chyba}");
            return 1;
        }

        try
        {
            return await ExecuteAsync(options, output).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            error.WriteLine($"Chyba: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> ExecuteAsync(DumpOptions options, TextWriter output)
    {
        if (options.Action is DumpAction.Diff or DumpAction.Merged)
        {
            var (model, database) = await ReadBothAsync(options).ConfigureAwait(false);

            if (options.Action == DumpAction.Diff)
            {
                var diff = SchemaComparer.Compare(model, database);

                output.WriteLine(SchemaTextWriter.RenderDiff(diff, model, database));
                await WriteJsonAsync(options.JsonPath, diff, output).ConfigureAwait(false);

                // Nenulový kód umožňuje použít nástroj jako kontrolu v CI.
                return diff.ErrorCount > 0 ? 2 : 0;
            }

            var merged = SchemaMerger.Merge(model, database);
            await WriteResultAsync(merged, options, output).ConfigureAwait(false);
            return 0;
        }

        var schema = await ReadAsync(options).ConfigureAwait(false);
        await WriteResultAsync(schema, options, output).ConfigureAwait(false);
        return 0;
    }

    private static async Task WriteResultAsync(
        DatabaseSchema schema,
        DumpOptions options,
        TextWriter output)
    {
        output.WriteLine(SchemaTextWriter.Render(schema));

        await WriteJsonAsync(options.JsonPath, schema, output).ConfigureAwait(false);
        await WriteExportAsync(options, schema, output).ConfigureAwait(false);
    }

    private static async Task<DatabaseSchema> ReadAsync(DumpOptions options)
    {
        var read = options.ToReadOptions();

        switch (options.Source)
        {
            case DumpSource.SqlServer:
                return await new SqlServerSchemaSource(options.Connection!)
                    .ReadAsync(read)
                    .ConfigureAwait(false);

            case DumpSource.Sqlite:
                return await new SqliteSchemaSource(
                        CommandLine.AsSqliteConnectionString(options.Connection!))
                    .ReadAsync(read)
                    .ConfigureAwait(false);

            default:
                await using (var context = ShopContextFactory.CreateSqlite())
                {
                    return await new EfCoreModelSchemaSource(context)
                        .ReadAsync(read with { IncludeMigrations = false })
                        .ConfigureAwait(false);
                }
        }
    }

    private static async Task<(DatabaseSchema Model, DatabaseSchema Database)> ReadBothAsync(
        DumpOptions options)
    {
        var read = options.ToReadOptions();
        var connection = options.Connection!;
        var isSqlite = options.Source == DumpSource.Sqlite;
        var sqliteConnection = CommandLine.AsSqliteConnectionString(connection);

        await using var context = isSqlite
            ? ShopContextFactory.CreateSqliteRaw(sqliteConnection)
            : ShopContextFactory.CreateSqlServer(connection);

        var model = await new EfCoreModelSchemaSource(context).ReadAsync(read).ConfigureAwait(false);

        ISchemaSource live = isSqlite
            ? new SqliteSchemaSource(sqliteConnection)
            : new SqlServerSchemaSource(connection);

        return (model, await live.ReadAsync(read).ConfigureAwait(false));
    }

    private static async Task WriteJsonAsync<T>(string? path, T value, TextWriter output)
    {
        if (path is null)
        {
            return;
        }

        var json = JsonSerializer.Serialize(value, DbsViewerJson.Readable);

        await File.WriteAllTextAsync(path, json, Encoding.UTF8).ConfigureAwait(false);
        output.WriteLine($"JSON zapsán do {Path.GetFullPath(path)} ({json.Length:N0} znaků)");
    }

    private static async Task WriteExportAsync(
        DumpOptions options,
        DatabaseSchema schema,
        TextWriter output)
    {
        if (options.ExportPath is not { } path)
        {
            return;
        }

        // Formát byl ověřený při rozboru argumentů.
        var format = CommandLine.ParseFormat(options.ExportFormat)!.Value;
        var content = SchemaExporter.Export(schema, format);

        await File.WriteAllTextAsync(path, content, Encoding.UTF8).ConfigureAwait(false);
        output.WriteLine($"Export ({format}) zapsán do {Path.GetFullPath(path)}");
    }
}
