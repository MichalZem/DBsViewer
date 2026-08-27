using System.Text;
using System.Text.Json;
using DbsViewer.EfCore;
using DbsViewer.SampleShop;

namespace DbsViewer.Dump;

/// <summary>
/// Ověřovací nástroj etapy 01: načte schéma z EF modelu ukázkového kontextu
/// a vypíše ho jako text, případně uloží jako JSON.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var jsonPath = GetOption(args, "--json");
        var sqlServer = GetOption(args, "--sqlserver");

        await using var context = sqlServer is null
            ? ShopContextFactory.CreateSqlite()
            : ShopContextFactory.CreateSqlServer(sqlServer);

        var source = new EfCoreModelSchemaSource(context);
        var options = new SchemaReadOptions
        {
            // Ukázkový kontext žádné migrace nemá a databáze nemusí existovat —
            // dotaz na migrace by jen vyrobil upozornění.
            IncludeMigrations = false,
        };

        var schema = await source.ReadAsync(options).ConfigureAwait(false);

        Console.WriteLine(SchemaTextWriter.Render(schema));

        if (jsonPath is not null)
        {
            var json = JsonSerializer.Serialize(schema, DbsViewerJson.Readable);
            await File.WriteAllTextAsync(jsonPath, json, Encoding.UTF8).ConfigureAwait(false);
            Console.WriteLine($"JSON zapsán do {Path.GetFullPath(jsonPath)} ({json.Length:N0} znaků)");
        }

        return 0;
    }

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
