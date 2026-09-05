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
            string? migration,
            bool? refresh,
            CancellationToken cancellationToken) =>
        {
            // Historická verze má přednost: zadaná migrace určuje, ke kterému okamžiku
            // se schéma čte, a zdroj pak nedává smysl — snapshot je vždycky z modelu.
            if (migration is { Length: > 0 })
            {
                try
                {
                    var snapshot = await provider
                        .GetAtMigrationAsync(migration, refresh ?? false, cancellationToken)
                        .ConfigureAwait(false);

                    return Json(snapshot);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.NotFound(new { chyba = ex.Message });
                }
            }

            if (!TryParseView(source, out var view))
            {
                return Results.BadRequest(new { chyba = $"Neznámý pohled '{source}'. Použij ef, live nebo merged." });
            }

            var schema = await provider.GetAsync(view ?? provider.DefaultView, refresh ?? false, cancellationToken)
                .ConfigureAwait(false);

            return Json(schema);
        });

        // ---------- historie schématu ----------

        api.MapGet("/migrations", async (
            SchemaProvider provider,
            CancellationToken cancellationToken) =>
        {
            var schema = await provider
                .GetAsync(provider.DefaultView, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var history = provider.History;

            // Seznam vychází ze schématu, protože jen to ví, které migrace jsou
            // v databázi skutečně aplikované. Změny a snapshot přidá historie.
            var migrations = schema.Migrations
                .Select(m => m with
                {
                    Changes = history?.GetChanges(m.Id) ?? [],
                    HasSnapshot = history?.Has(m.Id) ?? false,
                })
                .ToList();

            return Json(migrations);
        });

        api.MapGet("/migrations/diff", async (
            SchemaProvider provider,
            string? from,
            string to,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var diff = await provider
                    .CompareMigrationsAsync(from, to, cancellationToken)
                    .ConfigureAwait(false);

                return Json(diff);
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { chyba = ex.Message });
            }
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

        // Filtry chodí tělem POSTu, ne v URL: hledaná hodnota je obsah databáze
        // a v adrese by skončila v historii prohlížeče i v logu serveru.
        api.MapPost("/tables/{schema}/{name}/rows", async (
            DataPreviewService preview,
            HttpContext context,
            string schema,
            string name,
            CancellationToken cancellationToken) =>
        {
            var table = new DbObjectName(NormalizeSchema(schema), name);

            try
            {
                // Tělo se čte vlastním nastavením, ne tím z hostitelské aplikace:
                // enumy chodí jako text a globální JsonOptions by se sáhlo celé
                // aplikaci, do které je komponenta zabudovaná.
                var query = await context.Request
                    .ReadFromJsonAsync<DataQuery>(DbsViewerJson.Compact, cancellationToken)
                    .ConfigureAwait(false);

                var result = await preview
                    .GetAsync(table, query, context.User.Identity?.Name, cancellationToken)
                    .ConfigureAwait(false);

                return Json(result);
            }
            catch (InvalidOperationException ex)
            {
                // Vypnutý nebo nepovolený náhled není chyba serveru, ale odmítnutí požadavku.
                return Results.Json(new { chyba = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
            }
        });

        // Zápis má vlastní cesty, ne PUT a DELETE nad /rows: obě operace potřebují tělo
        // s hodnotami klíče a tělo u DELETE se přenáší nespolehlivě.
        api.MapPost("/tables/{schema}/{name}/rows/update", (
            DataPreviewService preview,
            HttpContext context,
            string schema,
            string name,
            CancellationToken cancellationToken) =>
            WriteAsync<DataUpdate>(
                context,
                new DbObjectName(NormalizeSchema(schema), name),
                (table, update, user, token) => preview.UpdateAsync(table, update ?? new DataUpdate(), user, token),
                cancellationToken));

        api.MapPost("/tables/{schema}/{name}/rows/insert", (
            DataPreviewService preview,
            HttpContext context,
            string schema,
            string name,
            CancellationToken cancellationToken) =>
            WriteAsync<DataInsert>(
                context,
                new DbObjectName(NormalizeSchema(schema), name),
                (table, insert, user, token) => preview.InsertAsync(table, insert ?? new DataInsert(), user, token),
                cancellationToken));

        api.MapPost("/tables/{schema}/{name}/rows/delete", (
            DataPreviewService preview,
            HttpContext context,
            string schema,
            string name,
            CancellationToken cancellationToken) =>
            WriteAsync<DataDelete>(
                context,
                new DbObjectName(NormalizeSchema(schema), name),
                (table, delete, user, token) => preview.DeleteAsync(table, delete ?? new DataDelete(), user, token),
                cancellationToken));

        api.MapPost("/refresh", async (SchemaProvider provider, CancellationToken cancellationToken) =>
        {
            await provider.InvalidateAsync(cancellationToken).ConfigureAwait(false);
            return Results.NoContent();
        });
    }

    /// <summary>
    /// Společný průběh zápisu: přečte tělo, zavolá službu a přeloží odmítnutí na stavový kód.
    /// </summary>
    /// <remarks>
    /// Rozlišují se dvě odmítnutí. <see cref="DataRequestException"/> je vadný požadavek —
    /// uživatel může hodnotu opravit a zkusit znovu, takže <c>400</c> a zpráva do mřížky.
    /// <see cref="InvalidOperationException"/> je zakázaná operace, na které se opakováním
    /// nic nezmění, takže <c>403</c>.
    /// </remarks>
    private static async Task<IResult> WriteAsync<TRequest>(
        HttpContext context,
        DbObjectName table,
        Func<DbObjectName, TRequest?, string?, CancellationToken, Task<DataChangeResult>> write,
        CancellationToken cancellationToken)
        where TRequest : class
    {
        try
        {
            var request = await context.Request
                .ReadFromJsonAsync<TRequest>(DbsViewerJson.Compact, cancellationToken)
                .ConfigureAwait(false);

            var result = await write(table, request, context.User.Identity?.Name, cancellationToken)
                .ConfigureAwait(false);

            return Json(result);
        }
        catch (DataRequestException ex)
        {
            return Results.BadRequest(new { chyba = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.Json(new { chyba = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
        }
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

    /// <summary>Smí se v mřížce upravovat hodnoty?</summary>
    public bool CanEditData { get; init; }

    /// <summary>Smí se v mřížce mazat řádky?</summary>
    public bool CanDeleteData { get; init; }

    /// <summary>Smí se v mřížce zakládat nové řádky?</summary>
    public bool CanInsertData { get; init; }

    public required bool ShowRowCounts { get; init; }

    /// <summary>Pojmenované skupiny tabulek pro filtr v UI.</summary>
    public IReadOnlyDictionary<string, string> Groups { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public required int DataPreviewMaxRows { get; init; }

    /// <summary>
    /// Dá se procházet historie schématu? Vyžaduje EF migrace v assembly aplikace.
    /// </summary>
    public bool CanBrowseHistory { get; init; }

    internal static DbsViewerMeta From(SchemaProvider provider, DbsViewerOptions options) => new()
    {
        Title = options.Title ?? "Schéma databáze",
        RoutePrefix = options.RoutePrefix,
        Views = [.. provider.AvailableViews.Select(static v => v.ToString().ToLowerInvariant())],
        CanDiff = provider.CanDiff,
        CanPreviewData = options.DataPreview.Enabled,
        CanEditData = options.DataPreview is { Enabled: true, AllowUpdate: true },
        CanDeleteData = options.DataPreview is { Enabled: true, AllowDelete: true },
        CanInsertData = options.DataPreview is { Enabled: true, AllowInsert: true },
        ShowRowCounts = options.ShowRowCounts,
        Groups = new Dictionary<string, string>(options.Groups, StringComparer.Ordinal),
        DataPreviewMaxRows = options.DataPreview.MaxRows,
        CanBrowseHistory = provider.CanBrowseHistory,
    };
}
