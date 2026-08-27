using System.Data;
using System.Data.Common;
using DbsViewer.Relational;
using Microsoft.Extensions.Logging;

namespace DbsViewer.Server;

/// <summary>Výsledek náhledu dat tabulky.</summary>
public sealed record DataPreview
{
    public required DbObjectName Table { get; init; }

    /// <summary>Jména sloupců v pořadí, ve kterém jsou hodnoty v řádcích.</summary>
    public required IReadOnlyList<string> Columns { get; init; }

    /// <summary>Sloupce, jejichž hodnoty jsou zamaskované.</summary>
    public IReadOnlyList<string> MaskedColumns { get; init; } = [];

    /// <summary>Řádky. Hodnota <c>null</c> znamená NULL v databázi.</summary>
    public required IReadOnlyList<IReadOnlyList<string?>> Rows { get; init; }

    /// <summary>Limit, který se použil.</summary>
    public int Limit { get; init; }

    /// <summary>Vrátilo se přesně tolik řádků, kolik je limit — další data mohou existovat.</summary>
    public bool IsTruncated => Rows.Count >= Limit;
}

/// <summary>
/// Read-only náhled řádků tabulky.
/// </summary>
/// <remarks>
/// Tahle třída jako jediná v celém DbsVieweru čte obsah, ne strukturu, a je tedy
/// nejcitlivější částí komponenty. Platí pro ni pravidla z
/// <see href="../../docs/adr/0006-bezpecnostni-defaulty.md">ADR-0006</see>:
/// vypnuto ve výchozím stavu, whitelist tabulek, maskování sloupců, tvrdý strop řádků
/// a povinný audit log. Uživatelské SQL se nikdy nepřijímá — jméno tabulky se ověřuje
/// proti načtenému schématu a teprve pak escapuje.
/// </remarks>
public sealed class DataPreviewService(
    SchemaProvider schemaProvider,
    DbsViewerOptions options,
    IEnumerable<ISchemaSource> sources,
    ILogger<DataPreviewService> logger)
{
    /// <summary>Načte prvních několik řádků tabulky.</summary>
    /// <param name="table">Tabulka, ověřuje se proti načtenému schématu.</param>
    /// <param name="limit">Požadovaný počet řádků; ořízne se na povolené maximum.</param>
    /// <param name="user">Kdo se ptá — zapíše se do audit logu.</param>
    /// <param name="cancellationToken">Zrušení operace.</param>
    public async Task<DataPreview> GetAsync(
        DbObjectName table,
        int? limit = null,
        string? user = null,
        CancellationToken cancellationToken = default)
    {
        if (!options.DataPreview.Enabled)
        {
            throw new InvalidOperationException(
                "Náhled dat je vypnutý. Zapíná se přes DataPreview.Enabled a je to vědomé "
                + "rozhodnutí zpřístupnit obsah databáze, ne jen její strukturu.");
        }

        if (!options.DataPreview.IsAllowed(table))
        {
            throw new InvalidOperationException(
                $"Náhled dat pro tabulku {table} není povolený. Zkontroluj DataPreview.AllowedTables.");
        }

        var view = schemaProvider.LiveSource is not null ? SchemaView.Live : SchemaView.Ef;
        var schema = await schemaProvider.GetAsync(view, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // Jméno tabulky se nikdy nebere z požadavku přímo — musí sedět na načtené schéma.
        var known = schema.FindTable(table)
            ?? throw new InvalidOperationException($"Tabulka {table} ve schématu není.");

        var connection = GetConnection();
        var effectiveLimit = Math.Clamp(limit ?? options.DataPreview.MaxRows, 1, options.DataPreview.MaxRows);

        logger.LogInformation(
            "DbsViewer: náhled dat tabulky {Table}, limit {Limit}, uživatel {User}.",
            known.Qualified,
            effectiveLimit,
            user ?? "(neznámý)");

        return await ReadAsync(connection, known, effectiveLimit, cancellationToken).ConfigureAwait(false);
    }

    private async Task<DataPreview> ReadAsync(
        DbConnection connection,
        DbTable table,
        int limit,
        CancellationToken cancellationToken)
    {
        var masked = table.Columns
            .Where(c => options.DataPreview.IsMasked(c.Name))
            .Select(static c => c.Name)
            .ToList();

        var maskedSet = new HashSet<string>(masked, StringComparer.OrdinalIgnoreCase);

        await using var scope = await ConnectionScope.OpenAsync(connection, cancellationToken)
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = BuildQuery(table, limit, connection);
        command.CommandType = CommandType.Text;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var columns = new List<string>(reader.FieldCount);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            columns.Add(reader.GetName(i));
        }

        var rows = new List<IReadOnlyList<string?>>();

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = new string?[reader.FieldCount];

            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[i] = maskedSet.Contains(columns[i]) ? "••••••" : Format(reader, i);
            }

            rows.Add(row);
        }

        return new DataPreview
        {
            Table = table.Name,
            Columns = columns,
            MaskedColumns = masked,
            Rows = rows,
            Limit = limit,
        };
    }

    /// <summary>
    /// Sestaví dotaz. Limit je celé číslo z konfigurace, ne z požadavku, a jméno tabulky
    /// pochází z načteného schématu — do textu se tedy nedostane nic uživatelského.
    /// </summary>
    internal static string BuildQuery(DbTable table, int limit, DbConnection connection)
    {
        var isSqlite = connection.GetType().Name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        var name = QuoteName(table.Name, isSqlite);

        return isSqlite
            ? $"SELECT * FROM {name} LIMIT {limit}"
            : $"SELECT TOP ({limit}) * FROM {name}";
    }

    /// <summary>Escapování identifikátoru podle providera.</summary>
    internal static string QuoteName(DbObjectName name, bool isSqlite)
    {
        if (isSqlite)
        {
            return $"\"{name.Name.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }

        var table = $"[{name.Name.Replace("]", "]]", StringComparison.Ordinal)}]";

        return name.Schema is { } schema
            ? $"[{schema.Replace("]", "]]", StringComparison.Ordinal)}].{table}"
            : table;
    }

    /// <summary>Hodnota se do UI posílá jako text — binární data se nikdy nepřenášejí.</summary>
    internal static string? Format(DbDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetValue(ordinal);

        return value switch
        {
            byte[] bytes => $"0x… ({bytes.Length} B)",
            DateTime date => date.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset offset => offset.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            IFormattable formattable =>
                formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
    }

    /// <summary>
    /// Připojení, ze kterého se čte. Bere se ze zdroje živé databáze — náhled dat
    /// nad samotným EF modelem nedává smysl, protože model data nemá.
    /// </summary>
    private DbConnection GetConnection()
    {
        foreach (var source in sources)
        {
            if (source is IDbConnectionProvider provider)
            {
                return provider.GetConnection();
            }
        }

        throw new InvalidOperationException(
            "Náhled dat vyžaduje připojení k databázi. Zapni IncludeLiveDatabase.");
    }
}
