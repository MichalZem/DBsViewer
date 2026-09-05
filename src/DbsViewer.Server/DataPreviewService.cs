using System.Data;
using System.Globalization;
using System.Data.Common;
using DbsViewer.Relational;
using Microsoft.Extensions.Logging;

namespace DbsViewer.Server;

/// <summary>Výsledek náhledu dat tabulky — jedna stránka.</summary>
public sealed record DataPreview
{
    public required DbObjectName Table { get; init; }

    /// <summary>Jména sloupců v pořadí, ve kterém jsou hodnoty v řádcích.</summary>
    public required IReadOnlyList<string> Columns { get; init; }

    /// <summary>Sloupce, jejichž hodnoty jsou zamaskované.</summary>
    public IReadOnlyList<string> MaskedColumns { get; init; } = [];

    /// <summary>Řádky. Hodnota <c>null</c> znamená NULL v databázi.</summary>
    public required IReadOnlyList<IReadOnlyList<string?>> Rows { get; init; }

    /// <summary>Stránka počítaná od nuly.</summary>
    public int Page { get; init; }

    /// <summary>Počet řádků na stránku.</summary>
    public int PageSize { get; init; }

    /// <summary>
    /// Celkový počet řádků odpovídajících filtrům, nebo <c>null</c>, když se ho
    /// nepodařilo zjistit.
    /// </summary>
    public long? TotalRows { get; init; }

    /// <summary>Sloupec, podle kterého je stránka seřazená.</summary>
    public string? SortColumn { get; init; }

    /// <summary>Řazení je sestupné.</summary>
    public bool SortDescending { get; init; }

    /// <summary>Počet stránek, nebo <c>null</c>, když se počet řádků nezná.</summary>
    public long? PageCount => TotalRows is { } total && PageSize > 0
        ? Math.Max(1, (total + PageSize - 1) / PageSize)
        : null;

    /// <summary>
    /// Existuje další stránka? Bez celkového počtu se pozná podle toho, že se vrátila
    /// plná stránka.
    /// </summary>
    public bool HasMore => PageCount is { } stranek
        ? Page + 1 < stranek
        : Rows.Count >= PageSize;
}

/// <summary>
/// Náhled řádků tabulky a jejich úprava.
/// </summary>
/// <remarks>
/// Tahle třída jako jediná v celém DbsVieweru sahá na obsah, ne na strukturu, a je tedy
/// nejcitlivější částí komponenty. Platí pro ni pravidla z
/// <see href="../../docs/adr/0006-bezpecnostni-defaulty.md">ADR-0006</see>:
/// vypnuto ve výchozím stavu, whitelist tabulek, maskování sloupců, tvrdý strop řádků
/// a povinný audit log. Uživatelské SQL se nikdy nepřijímá — jméno tabulky se ověřuje
/// proti načtenému schématu a teprve pak escapuje.
///
/// Zápis je nad rámec čtení vypnutý zvlášť a řídí se
/// <see href="../../docs/adr/0015-editace-radku.md">ADR-0015</see>: mění se jen hodnoty
/// existujícího řádku adresovaného primárním klíčem, nikdy víc řádků najednou.
/// </remarks>
public sealed class DataPreviewService(
    SchemaProvider schemaProvider,
    DbsViewerOptions options,
    IEnumerable<ISchemaSource> sources,
    ILogger<DataPreviewService> logger)
{
    /// <summary>Načte jednu stránku dat tabulky.</summary>
    /// <param name="table">Tabulka, ověřuje se proti načtenému schématu.</param>
    /// <param name="query">Stránka, řazení a filtry. Bez zadání se vezme první stránka.</param>
    /// <param name="user">Kdo se ptá — zapíše se do audit logu.</param>
    /// <param name="cancellationToken">Zrušení operace.</param>
    public async Task<DataPreview> GetAsync(
        DbObjectName table,
        DataQuery? query = null,
        string? user = null,
        CancellationToken cancellationToken = default)
    {
        var known = await ResolveAsync(table, cancellationToken).ConfigureAwait(false);
        var connection = GetConnection();
        var effective = Normalize(query ?? new DataQuery());

        logger.LogInformation(
            "DbsViewer: náhled dat tabulky {Table}, stránka {Page} po {PageSize}, "
            + "řazení {Sort}, filtrů {Filters}, uživatel {User}.",
            known.Qualified,
            effective.Page,
            effective.PageSize,
            effective.SortColumn ?? "(výchozí)",
            effective.Filters.Count,
            user ?? "(neznámý)");

        return await ReadAsync(connection, known, effective, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Upraví hodnoty v jednom řádku.</summary>
    /// <param name="table">Tabulka, ověřuje se proti načtenému schématu.</param>
    /// <param name="update">Klíč řádku a nové hodnoty měněných sloupců.</param>
    /// <param name="user">Kdo zapisuje — zapíše se do audit logu.</param>
    /// <param name="cancellationToken">Zrušení operace.</param>
    public async Task<DataChangeResult> UpdateAsync(
        DbObjectName table,
        DataUpdate update,
        string? user = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        var known = await ResolveAsync(table, cancellationToken).ConfigureAwait(false);

        GuardWrite(table, options.DataPreview.AllowUpdate, "Úprava dat", "DataPreview.AllowUpdate");

        var connection = GetConnection();
        var query = DataQueryBuilder.BuildUpdate(
            known,
            update,
            MaskedColumns(known),
            IsSqlite(connection));

        // Loguje se před zápisem a bez hodnot: co se měnilo, patří do auditu, ale obsah
        // databáze do logu nepatří.
        logger.LogInformation(
            "DbsViewer: úprava řádku tabulky {Table}, sloupce {Columns}, uživatel {User}.",
            known.Qualified,
            string.Join(", ", update.Values.Select(static v => v.Column)),
            user ?? "(neznámý)");

        return await WriteAsync(connection, query, ZadnyRadek, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Vloží jeden řádek.</summary>
    /// <param name="table">Tabulka, ověřuje se proti načtenému schématu.</param>
    /// <param name="insert">Vyplněné hodnoty nového řádku.</param>
    /// <param name="user">Kdo zapisuje — zapíše se do audit logu.</param>
    /// <param name="cancellationToken">Zrušení operace.</param>
    public async Task<DataChangeResult> InsertAsync(
        DbObjectName table,
        DataInsert insert,
        string? user = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(insert);

        var known = await ResolveAsync(table, cancellationToken).ConfigureAwait(false);

        GuardWrite(table, options.DataPreview.AllowInsert, "Vkládání dat", "DataPreview.AllowInsert");

        var connection = GetConnection();
        var query = DataQueryBuilder.BuildInsert(
            known,
            insert,
            MaskedColumns(known),
            IsSqlite(connection));

        logger.LogInformation(
            "DbsViewer: vložení řádku do tabulky {Table}, sloupce {Columns}, uživatel {User}.",
            known.Qualified,
            string.Join(", ", insert.Values.Select(static v => v.Column)),
            user ?? "(neznámý)");

        // Nulový počet řádků u INSERT znamená, že ho zahodil trigger nebo pravidlo —
        // hláška o nenalezeném řádku by tu lhala.
        return await WriteAsync(
            connection,
            query,
            "Řádek se nevložil. Databáze žádný řádek nepřidala.",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Smaže jeden řádek.</summary>
    /// <param name="table">Tabulka, ověřuje se proti načtenému schématu.</param>
    /// <param name="delete">Klíč mazaného řádku.</param>
    /// <param name="user">Kdo maže — zapíše se do audit logu.</param>
    /// <param name="cancellationToken">Zrušení operace.</param>
    public async Task<DataChangeResult> DeleteAsync(
        DbObjectName table,
        DataDelete delete,
        string? user = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delete);

        var known = await ResolveAsync(table, cancellationToken).ConfigureAwait(false);

        GuardWrite(table, options.DataPreview.AllowDelete, "Mazání dat", "DataPreview.AllowDelete");

        var connection = GetConnection();
        var query = DataQueryBuilder.BuildDelete(
            known,
            delete,
            MaskedColumns(known),
            IsSqlite(connection));

        logger.LogInformation(
            "DbsViewer: mazání řádku tabulky {Table}, uživatel {User}.",
            known.Qualified,
            user ?? "(neznámý)");

        return await WriteAsync(connection, query, ZadnyRadek, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Hláška, když se zápis nedotkl žádného řádku.</summary>
    private const string ZadnyRadek =
        "Řádek se nenašel. Nejspíš ho mezitím někdo smazal nebo změnil jeho klíč.";

    /// <summary>
    /// Ověří, že se z tabulky vůbec smí číst, a najde ji ve schématu.
    /// </summary>
    /// <remarks>
    /// Společné pro čtení i zápis: jméno tabulky se nikdy nebere z požadavku přímo,
    /// musí sedět na načtené schéma.
    /// </remarks>
    private async Task<DbTable> ResolveAsync(DbObjectName table, CancellationToken cancellationToken)
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

        return schema.FindTable(table)
            ?? throw new InvalidOperationException($"Tabulka {table} ve schématu není.");
    }

    /// <summary>Zápis je vypnutý, dokud ho někdo vědomě nezapne — a jen tam, kde smí.</summary>
    private void GuardWrite(DbObjectName table, bool allowed, string action, string option)
    {
        if (!allowed)
        {
            throw new InvalidOperationException(
                $"{action} je vypnutá. Zapíná se přes {option} a je to vědomé rozhodnutí "
                + "dovolit prohlížečce měnit obsah databáze.");
        }

        if (!options.DataPreview.IsEditable(table))
        {
            throw new InvalidOperationException(
                $"Zápis do tabulky {table} není povolený. Zkontroluj DataPreview.EditableTables.");
        }
    }

    /// <summary>Sloupce, jejichž hodnoty se maskují.</summary>
    private List<string> MaskedColumns(DbTable table) =>
    [
        .. table.Columns
            .Where(c => options.DataPreview.IsMasked(c.Name))
            .Select(static c => c.Name),
    ];

    /// <summary>
    /// Provede zápis a ověří, že se dotkl právě jednoho řádku.
    /// </summary>
    /// <remarks>
    /// Nula znamená, že řádek mezitím zmizel nebo se změnil jeho klíč. Mřížka tak
    /// nikdy netvrdí „uloženo", když se ve skutečnosti nic nestalo.
    /// </remarks>
    private async Task<DataChangeResult> WriteAsync(
        DbConnection connection,
        BuiltQuery query,
        string zadnyRadek,
        CancellationToken cancellationToken)
    {
        await using var scope = await ConnectionScope
            .OpenAsync(connection, ownsConnection: false, cancellationToken)
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = query.Sql;
        command.CommandType = CommandType.Text;
        command.CommandTimeout = options.DataPreview.CommandTimeoutSeconds;
        DataQueryBuilder.Bind(command, query.Parameters);

        int affected;

        try
        {
            affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbException ex)
        {
            // Zpráva databáze je to nejužitečnější, co se dá uživateli říct — cizí klíč,
            // NOT NULL, check constraint. Prohlížečka je za autorizací, takže ji ukážeme.
            throw new DataRequestException($"Databáze zápis odmítla: {ex.Message}", ex);
        }

        if (affected == 0)
        {
            throw new DataRequestException(zadnyRadek);
        }

        return new DataChangeResult { Affected = affected };
    }

    /// <summary>
    /// Ořízne požadavek na povolené meze. Číslo stránky ani její velikost se z požadavku
    /// nepřebírají bez kontroly — jdou přímo do textu dotazu.
    /// </summary>
    internal DataQuery Normalize(DataQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        return query with
        {
            Page = Math.Max(query.Page, 0),
            PageSize = Math.Clamp(query.PageSize, 1, options.DataPreview.MaxRows),
        };
    }

    private async Task<DataPreview> ReadAsync(
        DbConnection connection,
        DbTable table,
        DataQuery query,
        CancellationToken cancellationToken)
    {
        var masked = MaskedColumns(table);
        var maskedSet = new HashSet<string>(masked, StringComparer.OrdinalIgnoreCase);
        var isSqlite = IsSqlite(connection);

        // Připojení patří zdroji schématu, takže se jen otevře a zase zavře — neuvolňuje se.
        await using var scope = await ConnectionScope
            .OpenAsync(connection, ownsConnection: false, cancellationToken)
            .ConfigureAwait(false);

        var total = await CountAsync(connection, table, query, isSqlite, cancellationToken)
            .ConfigureAwait(false);

        var page = DataQueryBuilder.BuildPage(table, query, isSqlite);

        await using var command = connection.CreateCommand();
        command.CommandText = page.Sql;
        command.CommandType = CommandType.Text;
        command.CommandTimeout = options.DataPreview.CommandTimeoutSeconds;
        DataQueryBuilder.Bind(command, page.Parameters);

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
            Page = query.Page,
            PageSize = query.PageSize,
            TotalRows = total,
            SortColumn = DataQueryBuilder.FindColumn(table, query.SortColumn)?.Name,
            SortDescending = query.SortDescending,
        };
    }

    /// <summary>
    /// Spočítá řádky odpovídající filtrům.
    /// </summary>
    /// <remarks>
    /// Vrací <c>null</c>, když dotaz selže nebo nedoběhne do časového limitu. Přesný
    /// počet je nad velkou tabulkou drahý, ale stránkovat se dá i bez něj — mřížka pak
    /// nabídne jen další a předchozí stránku. Selhání počtu nesmí shodit celý náhled.
    /// </remarks>
    private async Task<long?> CountAsync(
        DbConnection connection,
        DbTable table,
        DataQuery query,
        bool isSqlite,
        CancellationToken cancellationToken)
    {
        var count = DataQueryBuilder.BuildCount(table, query, isSqlite);

        await using var command = connection.CreateCommand();
        command.CommandText = count.Sql;
        command.CommandType = CommandType.Text;
        command.CommandTimeout = options.DataPreview.CommandTimeoutSeconds;
        DataQueryBuilder.Bind(command, count.Parameters);

        return await TryCountAsync(command, table.Qualified, logger, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Spustí COUNT. Vrací <c>null</c>, když dotaz selže.
    /// </summary>
    /// <remarks>
    /// Vytaženo z <c>CountAsync</c>, aby šlo otestovat i selhání včetně zalogování:
    /// v integračním testu se nedá shodit počítání, aniž by se shodilo i čtení stránky,
    /// a chování při selhání je přitom to podstatné — náhled musí přežít i dotaz,
    /// který nedoběhne.
    /// </remarks>
    internal static async Task<long?> TryCountAsync(
        DbCommand command,
        string table,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            return value is null or DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        catch (DbException ex)
        {
            logger.LogWarning(
                ex,
                "DbsViewer: počet řádků tabulky {Table} se nepodařilo zjistit; "
                + "stránkuje se bez celkového počtu.",
                table);

            return null;
        }
    }

    /// <summary>Pozná providera podle typu připojení — jinak než podle jména to nejde.</summary>
    internal static bool IsSqlite(DbConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        return connection.GetType().Name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
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
