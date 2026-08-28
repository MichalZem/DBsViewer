using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DbsViewer.Server;

/// <summary>Namapování prohlížečky na cestu v aplikaci.</summary>
public static class DbsViewerEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Zpřístupní prohlížečku na nakonfigurované cestě.
    /// </summary>
    /// <remarks>
    /// Když je prohlížečka povolená mimo Development a nemá nastavenou autorizační policy,
    /// tato metoda **vyhodí výjimku při startu aplikace**. Je to záměr —
    /// viz <see href="../../docs/adr/0006-bezpecnostni-defaulty.md">ADR-0006</see>.
    /// Varování do logu by nikdo nepřečetl včas; pád nastane v nasazovacím pipeline.
    /// </remarks>
    /// <returns>
    /// Skupina namapovaných endpointů, nebo <c>null</c>, když je prohlížečka
    /// v aktuálním prostředí vypnutá.
    /// </returns>
    public static RouteGroupBuilder? MapDbsViewer(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider.GetService<DbsViewerOptions>()
            ?? throw new InvalidOperationException(
                "DbsViewer není zaregistrovaný. Zavolej AddDbsViewer<TContext>() v Program.cs.");

        var environment = endpoints.ServiceProvider.GetRequiredService<IHostEnvironment>();

        GuardSecurity(options, environment.EnvironmentName);

        if (!options.IsEnabledIn(environment.EnvironmentName))
        {
            return null;
        }

        var group = endpoints.MapGroup(options.RoutePrefix);

        if (options.AuthorizationPolicy is { } policy)
        {
            group.RequireAuthorization(policy);
        }

        MapApi(group.MapGroup("/api"));

        // UI se mapuje jako poslední, protože jeho catch-all cesta by jinak
        // přebila endpointy API.
        UiHosting.MapUi(group, options, options.UiAssembly);

        return group;
    }

    /// <summary>
    /// Bezpečnostní kontrola při startu. Mimo Development je autorizační policy povinná.
    /// </summary>
    internal static void GuardSecurity(DbsViewerOptions options, string environmentName)
    {
        var current = DbsViewerOptions.MapEnvironment(environmentName);

        if (current == HostEnv.Development || !options.EnabledIn.HasFlag(current))
        {
            return;
        }

        if (options.AuthorizationPolicy is null)
        {
            throw new InvalidOperationException(
                $"DbsViewer je povolený v prostředí {environmentName}, ale nemá nastavenou "
                + "autorizační policy. Schéma databáze je citlivá informace, takže mimo "
                + "Development je autorizace povinná.\n\n"
                + "Buď zavolej options.RequireAuthorization(\"NázevPolicy\"), nebo omez "
                + "options.EnabledIn na HostEnv.Development.");
        }

        if (options.DataPreview is { Enabled: true, AllowInProduction: false } && current == HostEnv.Production)
        {
            throw new InvalidOperationException(
                "DbsViewer má v produkci zapnutý náhled dat. To zpřístupňuje obsah databáze, "
                + "ne jen její strukturu.\n\n"
                + "Pokud je to opravdu záměr, vypni tuhle pojistku nastavením "
                + "DataPreview.AllowInProduction = true a ujisti se, že máš nastavené "
                + "maskování sloupců a whitelist tabulek.");
        }
    }

    private static void MapApi(RouteGroupBuilder api)
    {
        api.MapGet("/meta", (SchemaProvider provider, DbsViewerOptions options) =>
            Results.Ok(DbsViewerMeta.From(provider, options)));

        api.MapGet("/schema", async (
            SchemaProvider provider,
            string? source,
            bool? refresh,
            CancellationToken cancellationToken) =>
        {
            if (!TryParseView(source, out var view))
            {
                return Results.BadRequest(new { chyba = $"Neznámý pohled '{source}'. Použij ef, live nebo merged." });
            }

            var schema = await provider.GetAsync(view ?? provider.DefaultView, refresh ?? false, cancellationToken)
                .ConfigureAwait(false);

            return Json(schema);
        });

        api.MapGet("/schema/diff", async (
            SchemaProvider provider,
            bool? refresh,
            CancellationToken cancellationToken) =>
        {
            if (!provider.CanDiff)
            {
                return Results.BadRequest(new
                {
                    chyba = "Porovnání vyžaduje EF model i živou databázi. Zkontroluj IncludeLiveDatabase.",
                });
            }

            var diff = await provider.GetDiffAsync(refresh ?? false, cancellationToken).ConfigureAwait(false);
            return Json(diff);
        });

        api.MapGet("/tables/{schema}/{name}", async (
            SchemaProvider provider,
            string schema,
            string name,
            string? source,
            CancellationToken cancellationToken) =>
        {
            if (!TryParseView(source, out var view))
            {
                return Results.BadRequest(new { chyba = $"Neznámý pohled '{source}'." });
            }

            var loaded = await provider.GetAsync(view ?? provider.DefaultView, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var table = loaded.FindTable(new DbObjectName(NormalizeSchema(schema), name));

            return table is null
                ? Results.NotFound(new { chyba = $"Tabulka {schema}.{name} ve schématu není." })
                : Json(table);
        });

        api.MapGet("/tables/{schema}/{name}/rows", async (
            DataPreviewService preview,
            HttpContext context,
            string schema,
            string name,
            int? limit,
            CancellationToken cancellationToken) =>
        {
            var table = new DbObjectName(NormalizeSchema(schema), name);

            try
            {
                var result = await preview
                    .GetAsync(table, limit, context.User.Identity?.Name, cancellationToken)
                    .ConfigureAwait(false);

                return Json(result);
            }
            catch (InvalidOperationException ex)
            {
                // Vypnutý nebo nepovolený náhled není chyba serveru, ale odmítnutí požadavku.
                return Results.Json(new { chyba = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
            }
        });

        api.MapPost("/refresh", async (SchemaProvider provider, CancellationToken cancellationToken) =>
        {
            await provider.InvalidateAsync(cancellationToken).ConfigureAwait(false);
            return Results.NoContent();
        });
    }

    /// <summary>V cestě se prázdné schéma zapisuje pomlčkou, protože segment nesmí být prázdný.</summary>
    internal static string? NormalizeSchema(string schema) =>
        schema is "-" or "" ? null : schema;

    /// <summary>
    /// Přeloží textový název pohledu. Bez zadání vrátí <c>null</c>, což znamená
    /// „použij výchozí pohled podle toho, co je k dispozici".
    /// </summary>
    internal static bool TryParseView(string? source, out SchemaView? view)
    {
        view = null;

        switch (source?.ToLowerInvariant())
        {
            case null or "":
                return true;
            case "merged":
                view = SchemaView.Merged;
                return true;
            case "ef" or "model":
                view = SchemaView.Ef;
                return true;
            case "live" or "db" or "database":
                view = SchemaView.Live;
                return true;
            default:
                return false;
        }
    }

    private static IResult Json<T>(T value) =>
        Results.Json(value, DbsViewerJson.Compact);
}

/// <summary>
/// Co je v této konfiguraci k dispozici. UI si to načte jako první a podle toho skryje
/// funkce, které by mu server stejně odmítl.
/// </summary>
public sealed record DbsViewerMeta
{
    public required string Title { get; init; }

    public required string RoutePrefix { get; init; }

    /// <summary>Pohledy, které jde načíst: <c>ef</c>, <c>live</c>, <c>merged</c>.</summary>
    public required IReadOnlyList<string> Views { get; init; }

    public required bool CanDiff { get; init; }

    public required bool CanPreviewData { get; init; }

    public required bool ShowRowCounts { get; init; }

    /// <summary>Pojmenované skupiny tabulek pro filtr v UI.</summary>
    public IReadOnlyDictionary<string, string> Groups { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public required int DataPreviewMaxRows { get; init; }

    internal static DbsViewerMeta From(SchemaProvider provider, DbsViewerOptions options) => new()
    {
        Title = options.Title ?? "Schéma databáze",
        RoutePrefix = options.RoutePrefix,
        Views = [.. provider.AvailableViews.Select(static v => v.ToString().ToLowerInvariant())],
        CanDiff = provider.CanDiff,
        CanPreviewData = options.DataPreview.Enabled,
        ShowRowCounts = options.ShowRowCounts,
        Groups = new Dictionary<string, string>(options.Groups, StringComparer.Ordinal),
        DataPreviewMaxRows = options.DataPreview.MaxRows,
    };
}
