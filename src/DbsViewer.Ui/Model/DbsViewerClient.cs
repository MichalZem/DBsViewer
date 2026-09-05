using System.Net.Http.Json;
using System.Text.Json;
using DbsViewer.Analysis;

namespace DbsViewer.Ui.Model;

/// <summary>Co server v této konfiguraci nabízí. Odpovídá <c>/api/meta</c>.</summary>
public sealed record ViewerMeta
{
    public string Title { get; init; } = "Schéma databáze";

    public string RoutePrefix { get; init; } = "/dbschema";

    /// <summary>Dá se procházet historie schématu podle migrací?</summary>
    public bool CanBrowseHistory { get; init; }

    public IReadOnlyList<string> Views { get; init; } = [];

    public bool CanDiff { get; init; }

    public bool CanPreviewData { get; init; }

    /// <summary>Smí se v mřížce upravovat hodnoty?</summary>
    public bool CanEditData { get; init; }

    /// <summary>Smí se v mřížce mazat řádky?</summary>
    public bool CanDeleteData { get; init; }

    /// <summary>Smí se v mřížce zakládat nové řádky?</summary>
    public bool CanInsertData { get; init; }

    public bool ShowRowCounts { get; init; }

    public IReadOnlyDictionary<string, string> Groups { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public int DataPreviewMaxRows { get; init; } = 100;
}

/// <summary>Porovnání použité při filtrování sloupce.</summary>
public enum FilterOperator
{
    /// <summary>Hodnota obsahuje zadaný text.</summary>
    Contains,

    /// <summary>Hodnota se rovná zadané.</summary>
    Equals,

    /// <summary>Hodnota začíná zadaným textem.</summary>
    StartsWith,

    /// <summary>Hodnota končí zadaným textem.</summary>
    EndsWith,

    /// <summary>Hodnota je větší než zadaná.</summary>
    GreaterThan,

    /// <summary>Hodnota je menší než zadaná.</summary>
    LessThan,

    /// <summary>Hodnota je NULL.</summary>
    IsNull,

    /// <summary>Hodnota není NULL.</summary>
    IsNotNull,
}

/// <summary>Filtr nad jedním sloupcem.</summary>
/// <param name="Column">Jméno sloupce.</param>
/// <param name="Operator">Porovnání.</param>
/// <param name="Value">Hledaná hodnota.</param>
public sealed record DataFilter(string Column, FilterOperator Operator, string? Value);

/// <summary>Požadavek na stránku dat. Odpovídá serverovému <c>DataQuery</c>.</summary>
public sealed record DataQuery
{
    /// <summary>Stránka počítaná od nuly.</summary>
    public int Page { get; init; }

    /// <summary>Počet řádků na stránku.</summary>
    public int PageSize { get; init; } = 50;

    /// <summary>Sloupec, podle kterého se řadí.</summary>
    public string? SortColumn { get; init; }

    /// <summary>Řadit sestupně.</summary>
    public bool SortDescending { get; init; }

    /// <summary>Filtry nad sloupci. Spojují se přes AND.</summary>
    public IReadOnlyList<DataFilter> Filters { get; init; } = [];
}

/// <summary>Hodnota jednoho sloupce v požadavku na zápis.</summary>
/// <param name="Column">Jméno sloupce.</param>
/// <param name="Value">Hodnota jako text. <c>null</c> znamená SQL NULL.</param>
public sealed record DataValue(string Column, string? Value);

/// <summary>Požadavek na úpravu řádku. Odpovídá serverovému <c>DataUpdate</c>.</summary>
public sealed record DataUpdate
{
    /// <summary>Hodnoty primárního klíče, které řádek identifikují.</summary>
    public IReadOnlyList<DataValue> Key { get; init; } = [];

    /// <summary>Nové hodnoty měněných sloupců.</summary>
    public IReadOnlyList<DataValue> Values { get; init; } = [];
}

/// <summary>Požadavek na vložení řádku. Odpovídá serverovému <c>DataInsert</c>.</summary>
public sealed record DataInsert
{
    /// <summary>Hodnoty vyplněných sloupců. Nevyplněné se nechají na databázi.</summary>
    public IReadOnlyList<DataValue> Values { get; init; } = [];
}

/// <summary>Požadavek na smazání řádku. Odpovídá serverovému <c>DataDelete</c>.</summary>
public sealed record DataDelete
{
    /// <summary>Hodnoty primárního klíče, které řádek identifikují.</summary>
    public IReadOnlyList<DataValue> Key { get; init; } = [];
}

/// <summary>Výsledek zápisu. Odpovídá serverovému <c>DataChangeResult</c>.</summary>
public sealed record RowChange
{
    /// <summary>Kolik řádků se změnilo.</summary>
    public int Affected { get; init; }
}

/// <summary>Stránka dat tabulky. Odpovídá <c>/api/tables/…/rows</c>.</summary>
public sealed record RowPreview
{
    public IReadOnlyList<string> Columns { get; init; } = [];

    public IReadOnlyList<string> MaskedColumns { get; init; } = [];

    public IReadOnlyList<IReadOnlyList<string?>> Rows { get; init; } = [];

    public int Page { get; init; }

    public int PageSize { get; init; }

    /// <summary>Celkem řádků po filtrech, nebo <c>null</c>, když se ho nepodařilo zjistit.</summary>
    public long? TotalRows { get; init; }

    public string? SortColumn { get; init; }

    public bool SortDescending { get; init; }

    public long? PageCount { get; init; }

    public bool HasMore { get; init; }
}

/// <summary>
/// Volání serverového API. Chyby se překládají na české zprávy, protože je uživatel
/// uvidí přímo v UI.
/// </summary>
public sealed class DbsViewerClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = DbsViewerJson.Compact;

    public Task<ViewerMeta> GetMetaAsync(CancellationToken cancellationToken = default) =>
        GetAsync<ViewerMeta>("api/meta", cancellationToken);

    public Task<DatabaseSchema> GetSchemaAsync(
        string? source = null,
        bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>();

        if (!string.IsNullOrEmpty(source))
        {
            query.Add($"source={Uri.EscapeDataString(source)}");
        }

        if (refresh)
        {
            query.Add("refresh=true");
        }

        var url = query.Count > 0 ? $"api/schema?{string.Join('&', query)}" : "api/schema";

        return GetAsync<DatabaseSchema>(url, cancellationToken);
    }

    public Task<SchemaDiff> GetDiffAsync(bool refresh = false, CancellationToken cancellationToken = default) =>
        GetAsync<SchemaDiff>(refresh ? "api/schema/diff?refresh=true" : "api/schema/diff", cancellationToken);

    /// <summary>
    /// Načte jednu stránku dat. Posílá se POSTem, protože hledané hodnoty jsou obsah
    /// databáze a v adrese by skončily v historii prohlížeče i v logu serveru.
    /// </summary>
    public async Task<RowPreview> GetRowsAsync(
        DbObjectName table,
        DataQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        var url = $"api/tables/{SchemaSegment(table)}/{Uri.EscapeDataString(table.Name)}/rows";

        var response = await http
            .PostAsJsonAsync(url, query ?? new DataQuery(), Json, cancellationToken)
            .ConfigureAwait(false);

        EnsureSuccess(response);

        return await response.Content
            .ReadFromJsonAsync<RowPreview>(Json, cancellationToken)
            .ConfigureAwait(false)
            ?? new RowPreview();
    }

    /// <summary>Uloží nové hodnoty jednoho řádku.</summary>
    public Task<RowChange> UpdateRowAsync(
        DbObjectName table,
        DataUpdate update,
        CancellationToken cancellationToken = default) =>
        WriteAsync<DataUpdate, RowChange>(RowsUrl(table, "update"), update, cancellationToken);

    /// <summary>Vloží jeden řádek.</summary>
    public Task<RowChange> InsertRowAsync(
        DbObjectName table,
        DataInsert insert,
        CancellationToken cancellationToken = default) =>
        WriteAsync<DataInsert, RowChange>(RowsUrl(table, "insert"), insert, cancellationToken);

    /// <summary>Smaže jeden řádek.</summary>
    public Task<RowChange> DeleteRowAsync(
        DbObjectName table,
        DataDelete delete,
        CancellationToken cancellationToken = default) =>
        WriteAsync<DataDelete, RowChange>(RowsUrl(table, "delete"), delete, cancellationToken);

    /// <summary>Seznam migrací i s tím, co která změnila.</summary>
    public Task<IReadOnlyList<DbMigration>> GetMigrationsAsync(
        CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<DbMigration>>("api/migrations", cancellationToken);

    /// <summary>
    /// Schéma tak, jak vypadalo po zadané migraci. Vrací se stejný tvar jako u aktuálního
    /// schématu, takže přehled, seznam tabulek i diagram fungují beze změny.
    /// </summary>
    public Task<DatabaseSchema> GetSchemaAtMigrationAsync(
        string migrationId,
        CancellationToken cancellationToken = default) =>
        GetAsync<DatabaseSchema>(
            $"api/schema?migration={Uri.EscapeDataString(migrationId)}",
            cancellationToken);

    /// <summary>
    /// Porovná schéma ve dvou bodech historie.
    /// </summary>
    /// <param name="from">Starší verze, nebo <c>null</c> pro stav před první migrací.</param>
    /// <param name="to">Novější verze.</param>
    /// <param name="cancellationToken">Zrušení operace.</param>
    public Task<SchemaDiff> CompareMigrationsAsync(
        string? from,
        string to,
        CancellationToken cancellationToken = default)
    {
        var url = $"api/migrations/diff?to={Uri.EscapeDataString(to)}";

        if (from is { Length: > 0 })
        {
            url += $"&from={Uri.EscapeDataString(from)}";
        }

        return GetAsync<SchemaDiff>(url, cancellationToken);
    }

    /// <summary>Zahodí serverovou cache, aby se schéma načetlo znovu.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var response = await http.PostAsync("api/refresh", null, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response);
    }

    /// <summary>
    /// Prázdné schéma se v cestě zapisuje pomlčkou — segment URL nesmí být prázdný
    /// a SQLite schémata nemá.
    /// </summary>
    /// <summary>Cesta k zápisové operaci nad řádky tabulky.</summary>
    private static string RowsUrl(DbObjectName table, string operation) =>
        $"api/tables/{SchemaSegment(table)}/{Uri.EscapeDataString(table.Name)}/rows/{operation}";

    /// <summary>
    /// Zápis. Na rozdíl od čtení se z odmítnuté odpovědi vytáhne zpráva serveru.
    /// </summary>
    /// <remarks>
    /// U zápisu je důvod odmítnutí to jediné, co uživateli pomůže — „cizí klíč brání
    /// smazání" se z čísla 400 vyčíst nedá. Server ji posílá v poli <c>chyba</c>.
    /// </remarks>
    private async Task<TResult> WriteAsync<TRequest, TResult>(
        string url,
        TRequest request,
        CancellationToken cancellationToken)
        where TResult : new()
    {
        var response = await http
            .PostAsJsonAsync(url, request, Json, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new DbsViewerClientException(
                await ReadErrorAsync(response, cancellationToken).ConfigureAwait(false));
        }

        return await response.Content
            .ReadFromJsonAsync<TResult>(Json, cancellationToken)
            .ConfigureAwait(false)
            ?? new TResult();
    }

    /// <summary>Zpráva serveru, a když žádná nedorazila, popis stavového kódu.</summary>
    internal static async Task<string> ReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var chyba = await response.Content
                .ReadFromJsonAsync<ServerError>(Json, cancellationToken)
                .ConfigureAwait(false);

            if (chyba?.Chyba is { Length: > 0 } zprava)
            {
                return zprava;
            }
        }
        catch (JsonException)
        {
            // Tělo není JSON — pak zbývá jen stavový kód.
        }

        return DescribeFailure(response.StatusCode);
    }

    internal static string SchemaSegment(DbObjectName table) =>
        table.Schema is { Length: > 0 } schema ? Uri.EscapeDataString(schema) : "-";

    private async Task<T> GetAsync<T>(string url, CancellationToken cancellationToken)
    {
        var response = await http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response);

        var value = await response.Content
            .ReadFromJsonAsync<T>(Json, cancellationToken)
            .ConfigureAwait(false);

        return value ?? throw new DbsViewerClientException("Server vrátil prázdnou odpověď.");
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw new DbsViewerClientException(DescribeFailure(response.StatusCode));
    }

    /// <summary>Přeloží stavový kód na větu, která uživateli poradí, co dělat.</summary>
    internal static string DescribeFailure(System.Net.HttpStatusCode status) => status switch
    {
        System.Net.HttpStatusCode.Forbidden =>
            "Přístup odepřen. Funkce je v konfiguraci vypnutá nebo na ni nemáš oprávnění.",
        System.Net.HttpStatusCode.Unauthorized =>
            "Nejsi přihlášený. Přihlas se a zkus to znovu.",
        System.Net.HttpStatusCode.NotFound =>
            "Požadovaný objekt neexistuje.",
        System.Net.HttpStatusCode.BadRequest =>
            "Server požadavku nerozuměl. Zkontroluj zvolený pohled.",
        _ => $"Server odpověděl chybou {(int)status}.",
    };
}

/// <summary>Odmítnutá odpověď serveru. Zpráva je česky a míří rovnou do UI.</summary>
internal sealed record ServerError
{
    public string? Chyba { get; init; }
}

/// <summary>Chyba při komunikaci se serverem, se zprávou určenou uživateli.</summary>
public sealed class DbsViewerClientException(string message) : Exception(message);
