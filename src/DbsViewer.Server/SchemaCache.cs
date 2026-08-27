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
    private readonly Dictionary<SchemaView, CacheEntry> _entries = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Vrátí schéma z cache, nebo ho načte zadanou funkcí a uloží.
    /// Zámek zajistí, že souběžné požadavky nespustí introspekci vícekrát.
    /// </summary>
    public async Task<DatabaseSchema> GetOrLoadAsync(
        SchemaView view,
        Func<CancellationToken, Task<DatabaseSchema>> load,
        bool refresh,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(load);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!refresh
                && _entries.TryGetValue(view, out var entry)
                && entry.ExpiresAt > timeProvider.GetUtcNow())
            {
                return entry.Schema;
            }

            var schema = await load(cancellationToken).ConfigureAwait(false);

            if (options.CacheFor > TimeSpan.Zero)
            {
                _entries[view] = new CacheEntry(schema, timeProvider.GetUtcNow() + options.CacheFor);
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
