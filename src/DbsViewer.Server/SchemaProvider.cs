using DbsViewer.Analysis;
using Microsoft.Extensions.Logging;

namespace DbsViewer.Server;

/// <summary>Který pohled na schéma se má vrátit.</summary>
public enum SchemaView
{
    /// <summary>Z EF modelu.</summary>
    Ef,

    /// <summary>Z živé databáze.</summary>
    Live,

    /// <summary>Sloučení obojího.</summary>
    Merged,
}

/// <summary>
/// Načítání schématu s cache. Cache je nutná, protože introspekce živé databáze
/// není zadarmo a UI se na schéma ptá při každém načtení stránky.
/// </summary>
public sealed class SchemaProvider(
    IEnumerable<ISchemaSource> sources,
    DbsViewerOptions options,
    SchemaCache cache,
    ILogger<SchemaProvider> logger)
{
    private readonly List<ISchemaSource> _sources = [.. sources];

    /// <summary>Zdroj z EF modelu, pokud je registrovaný.</summary>
    public ISchemaSource? EfSource =>
        _sources.FirstOrDefault(static s => s.Kind == SchemaSourceKind.EfModel);

    /// <summary>Zdroj z živé databáze, pokud je registrovaný a povolený.</summary>
    public ISchemaSource? LiveSource => options.IncludeLiveDatabase
        ? _sources.FirstOrDefault(static s => s.Kind == SchemaSourceKind.LiveDatabase)
        : null;

    /// <summary>Pohledy, které jde v této konfiguraci sestavit.</summary>
    public IReadOnlyList<SchemaView> AvailableViews
    {
        get
        {
            var views = new List<SchemaView>();

            if (EfSource is not null)
            {
                views.Add(SchemaView.Ef);
            }

            if (LiveSource is not null)
            {
                views.Add(SchemaView.Live);
            }

            if (EfSource is not null && LiveSource is not null)
            {
                views.Add(SchemaView.Merged);
            }

            return views;
        }
    }

    /// <summary>Diff je k dispozici jen tehdy, když jsou oba zdroje.</summary>
    public bool CanDiff => EfSource is not null && LiveSource is not null;

    /// <summary>
    /// Pohled použitý, když si o něj volající neřekne. Sloučený má nejvíc informací,
    /// takže vyhrává, kdykoli je k dispozici.
    /// </summary>
    public SchemaView DefaultView => AvailableViews.Contains(SchemaView.Merged)
        ? SchemaView.Merged
        : AvailableViews.FirstOrDefault();

    /// <summary>Načte schéma v požadovaném pohledu, z cache nebo znovu.</summary>
    public async Task<DatabaseSchema> GetAsync(
        SchemaView view,
        bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        if (!AvailableViews.Contains(view))
        {
            throw new InvalidOperationException(
                $"Pohled {view} není v této konfiguraci k dispozici. Dostupné: "
                + $"{string.Join(", ", AvailableViews)}.");
        }

        return await cache
            .GetOrLoadAsync(view, token => LoadAsync(view, token), refresh, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Porovná EF model proti živé databázi.</summary>
    public async Task<SchemaDiff> GetDiffAsync(
        bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        if (!CanDiff)
        {
            throw new InvalidOperationException(
                "Porovnání vyžaduje EF model i živou databázi. Zkontroluj IncludeLiveDatabase "
                + "a registraci databázového zdroje.");
        }

        var model = await GetAsync(SchemaView.Ef, refresh, cancellationToken).ConfigureAwait(false);
        var database = await GetAsync(SchemaView.Live, refresh, cancellationToken).ConfigureAwait(false);

        return SchemaComparer.Compare(model, database, options.Diff);
    }

    /// <summary>Zahodí cache. Volá se po ručním obnovení z UI.</summary>
    public Task InvalidateAsync(CancellationToken cancellationToken = default) =>
        cache.InvalidateAsync(cancellationToken);

    private async Task<DatabaseSchema> LoadAsync(SchemaView view, CancellationToken cancellationToken)
    {
        var readOptions = options.ToReadOptions();

        switch (view)
        {
            case SchemaView.Ef:
                logger.LogInformation("DbsViewer načítá schéma z EF modelu.");
                return await EfSource!.ReadAsync(readOptions, cancellationToken).ConfigureAwait(false);

            case SchemaView.Live:
                logger.LogInformation("DbsViewer načítá schéma z živé databáze.");
                return await LiveSource!.ReadAsync(readOptions, cancellationToken).ConfigureAwait(false);

            default:
                logger.LogInformation("DbsViewer načítá sloučené schéma.");
                var model = await EfSource!.ReadAsync(readOptions, cancellationToken).ConfigureAwait(false);
                var database = await LiveSource!.ReadAsync(readOptions, cancellationToken).ConfigureAwait(false);
                return SchemaMerger.Merge(model, database);
        }
    }
}
