using System.Net.Http.Json;
using System.Text.Json;
using DbsViewer.Analysis;

namespace DbsViewer.Ui.Model;

/// <summary>Co server v této konfiguraci nabízí. Odpovídá <c>/api/meta</c>.</summary>
public sealed record ViewerMeta
{
    public string Title { get; init; } = "Schéma databáze";

    public string RoutePrefix { get; init; } = "/dbschema";

    public IReadOnlyList<string> Views { get; init; } = [];

    public bool CanDiff { get; init; }

    public bool CanPreviewData { get; init; }

    public bool ShowRowCounts { get; init; }

    public IReadOnlyDictionary<string, string> Groups { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public int DataPreviewMaxRows { get; init; } = 100;
}

/// <summary>Náhled řádků tabulky. Odpovídá <c>/api/tables/…/rows</c>.</summary>
public sealed record RowPreview
{
    public IReadOnlyList<string> Columns { get; init; } = [];

    public IReadOnlyList<string> MaskedColumns { get; init; } = [];

    public IReadOnlyList<IReadOnlyList<string?>> Rows { get; init; } = [];

    public int Limit { get; init; }

    public bool IsTruncated { get; init; }
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

    public Task<RowPreview> GetRowsAsync(
        DbObjectName table,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var url = $"api/tables/{SchemaSegment(table)}/{Uri.EscapeDataString(table.Name)}/rows";

        if (limit is { } value)
        {
            url += $"?limit={value}";
        }

        return GetAsync<RowPreview>(url, cancellationToken);
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

/// <summary>Chyba při komunikaci se serverem, se zprávou určenou uživateli.</summary>
public sealed class DbsViewerClientException(string message) : Exception(message);
