namespace DbsViewer.Dump;

/// <summary>Co má nástroj udělat.</summary>
public enum DumpAction
{
    /// <summary>Vypsat schéma.</summary>
    Show,

    /// <summary>Porovnat EF model s databází.</summary>
    Diff,

    /// <summary>Sloučit EF model s databází.</summary>
    Merged,

    /// <summary>Vypsat nápovědu.</summary>
    Help,
}

/// <summary>Odkud se čte schéma.</summary>
public enum DumpSource
{
    /// <summary>Ukázkový EF model z repozitáře.</summary>
    Sample,

    SqlServer,

    Sqlite,
}

/// <summary>
/// Rozebrané argumenty příkazové řádky. Oddělené od spouštění, aby se daly testovat
/// bez databáze i bez zápisu na disk.
/// </summary>
public sealed record DumpOptions
{
    public DumpAction Action { get; init; } = DumpAction.Show;

    public DumpSource Source { get; init; } = DumpSource.Sample;

    /// <summary>Connection string nebo cesta k souboru.</summary>
    public string? Connection { get; init; }

    /// <summary>Kam uložit JSON, nebo <c>null</c>.</summary>
    public string? JsonPath { get; init; }

    /// <summary>Kam uložit export, nebo <c>null</c>.</summary>
    public string? ExportPath { get; init; }

    /// <summary>Formát exportu: <c>mermaid</c>, <c>dbml</c> nebo <c>markdown</c>.</summary>
    public string ExportFormat { get; init; } = "markdown";

    public bool IncludeRowCounts { get; init; }

    public IReadOnlyList<string> HideTables { get; init; } = [];

    public IReadOnlyList<string> IncludeSchemas { get; init; } = [];

    /// <summary>Chyba v argumentech, nebo <c>null</c> když jsou v pořádku.</summary>
    public string? Error { get; init; }

    /// <summary>Volby čtení schématu odvozené z argumentů.</summary>
    public SchemaReadOptions ToReadOptions() => new()
    {
        IncludeRowCounts = IncludeRowCounts,
        IncludeMigrations = true,
        HideTables = HideTables,
        IncludeSchemas = IncludeSchemas,
    };
}

/// <summary>Rozbor argumentů příkazové řádky.</summary>
public static class CommandLine
{
    public const string Usage = """
        dbsview — výpis databázového schématu

        Zdroj (bez zadání se použije ukázkový EF model):
          --sqlserver <conn>         živá SQL Server databáze
          --sqlite <conn|cesta>      živá SQLite databáze

        Akce:
          --diff <conn>              porovná ukázkový EF model proti databázi
          --merged <conn>            sloučí ukázkový EF model s databází

        Výstup:
          --json <soubor>            uloží výsledek jako JSON
          --export <soubor>          uloží dokumentaci schématu
          --format <formát>          mermaid | dbml | markdown (výchozí markdown)

        Volby:
          --rows                     zjistí odhad počtu řádků
          --hide <vzor>[,<vzor>]     skryje tabulky (podporuje *)
          --schemas <a>[,<b>]        načte jen uvedená schémata
          --help                     tato nápověda

        Návratové kódy: 0 v pořádku, 1 chyba, 2 diff našel chybu.
        """;

    /// <summary>Rozebere argumenty. Chybu vrací v <see cref="DumpOptions.Error"/>, nevyhazuje.</summary>
    public static DumpOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Contains("--help") || args.Contains("-h"))
        {
            return new DumpOptions { Action = DumpAction.Help };
        }

        var options = new DumpOptions
        {
            IncludeRowCounts = args.Contains("--rows"),
            JsonPath = Value(args, "--json"),
            ExportPath = Value(args, "--export"),
            ExportFormat = Value(args, "--format") ?? "markdown",
            HideTables = List(Value(args, "--hide")),
            IncludeSchemas = List(Value(args, "--schemas")),
        };

        if (!IsKnownFormat(options.ExportFormat))
        {
            return options with
            {
                Error = $"Neznámý formát '{options.ExportFormat}'. Použij mermaid, dbml nebo markdown.",
            };
        }

        if (Value(args, "--diff") is { } diff)
        {
            return options with { Action = DumpAction.Diff, Connection = diff, Source = Detect(diff) };
        }

        if (Value(args, "--merged") is { } merged)
        {
            return options with { Action = DumpAction.Merged, Connection = merged, Source = Detect(merged) };
        }

        if (Value(args, "--sqlserver") is { } sqlServer)
        {
            return options with { Source = DumpSource.SqlServer, Connection = sqlServer };
        }

        if (Value(args, "--sqlite") is { } sqlite)
        {
            return options with { Source = DumpSource.Sqlite, Connection = sqlite };
        }

        return options;
    }

    /// <summary>Formát exportu z textu, nebo <c>null</c> u neznámého.</summary>
    public static Ui.Model.ExportFormat? ParseFormat(string? format) => format?.ToLowerInvariant() switch
    {
        "mermaid" or "mmd" => Ui.Model.ExportFormat.Mermaid,
        "dbml" => Ui.Model.ExportFormat.Dbml,
        "markdown" or "md" => Ui.Model.ExportFormat.Markdown,
        _ => null,
    };

    private static bool IsKnownFormat(string format) => ParseFormat(format) is not null;

    /// <summary>
    /// Rozpozná providera z connection stringu. SQL Server ho vždy uvádí přes
    /// <c>Server=</c> nebo <c>Initial Catalog</c>; cokoli jiného je cesta k SQLite souboru.
    /// </summary>
    public static DumpSource Detect(string connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var isSqlServer = connection.Contains("Server=", StringComparison.OrdinalIgnoreCase)
            || connection.Contains("Initial Catalog", StringComparison.OrdinalIgnoreCase);

        return isSqlServer ? DumpSource.SqlServer : DumpSource.Sqlite;
    }

    /// <summary>Holá cesta k souboru se doplní na connection string.</summary>
    public static string AsSqliteConnectionString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value.Contains('=', StringComparison.Ordinal) ? value : $"Data Source={value}";
    }

    internal static IReadOnlyList<string> List(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    internal static string? Value(string[] args, string name)
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
