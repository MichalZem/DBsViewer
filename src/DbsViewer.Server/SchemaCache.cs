namespace DbsViewer.Server;

/// <summary>
/// Sdílená cache načtených schémat.
/// </summary>
/// <remarks>
/// Je to singleton, zatímco <see cref="SchemaProvider"/> je scoped — a je to podstatný
/// rozdíl. Kdyby cache žila ve scoped službě, zanikla by s každým požadavkem a introspekce
/// databáze by běžela při každém načtení stránky znovu.
/// </remarks>
public sealed class SchemaCache(DbsViewerOptions options, TimeProvider timeProvider) : IDisposable
{
    // Klíčem je řetězec, ne SchemaView: kromě tří pohledů se cachují i snapshoty
    // jednotlivých migrací, kterých je libovolně mnoho.
    private readonly Dictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Vrátí schéma z cache, nebo ho načte zadanou funkcí a uloží.
    /// Zámek zajistí, že souběžné požadavky nespustí introspekci vícekrát.
    /// </summary>
    public async Task<DatabaseSchema> GetOrLoadAsync(
        SchemaView view,
        Func<CancellationToken, Task<DatabaseSchema>> load,
        bool refresh,
        CancellationToken cancellationToken) =>
        await GetOrLoadAsync(view.ToString(), load, refresh, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Totéž pod libovolným klíčem — používá se pro snapshoty migrací, kde samotný
    /// pohled nestačí a je potřeba rozlišit i konkrétní verzi.
    /// </summary>
    public async Task<DatabaseSchema> GetOrLoadAsync(
        string key,
        Func<CancellationToken, Task<DatabaseSchema>> load,
        bool refresh,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(load);
        ArgumentException.ThrowIfNullOrEmpty(key);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!refresh
                && _entries.TryGetValue(key, out var entry)
                && entry.ExpiresAt > timeProvider.GetUtcNow())
            {
                return entry.Schema;
            }

            var schema = await load(cancellationToken).ConfigureAwait(false);

            if (options.CacheFor > TimeSpan.Zero)
            {
                _entries[key] = new CacheEntry(schema, timeProvider.GetUtcNow() + options.CacheFor);
            }

            return schema;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Zahodí uložená schémata. Volá se po ručním obnovení z UI.</summary>
    public async Task InvalidateAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _entries.Clear();
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();

    private sealed record CacheEntry(DatabaseSchema Schema, DateTimeOffset ExpiresAt);
}
