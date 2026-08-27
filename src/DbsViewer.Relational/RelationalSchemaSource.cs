using System.Data.Common;

namespace DbsViewer.Relational;

/// <summary>
/// Zdroj, který umí půjčit své připojení. Implementuje ho živá introspekce,
/// aby náhled dat nemusel navazovat druhé spojení.
/// </summary>
public interface IDbConnectionProvider
{
    /// <summary>Připojení k databázi. Volající ho zavírá jen tehdy, když ho sám otevřel.</summary>
    DbConnection GetConnection();
}

/// <summary>
/// Zdroj schématu čtoucí z živé databáze. Providerové čtečky doplňují jen dotazy
/// a mapování řádků; otevírání připojení, ošetření chyb a sestavení výsledku je společné.
/// </summary>
public abstract class RelationalSchemaSource : ISchemaSource, IDbConnectionProvider
{
    private readonly Func<DbConnection> _connectionFactory;
    private readonly bool _ownsConnection;

    /// <param name="connectionFactory">
    /// Vytvoří připojení. Když ho vytváří tento zdroj, taky ho po sobě uklidí.
    /// </param>
    /// <param name="ownsConnection">
    /// <c>false</c>, když připojení patří někomu jinému — pak se jen otevře, ale nezavře.
    /// </param>
    /// <param name="key">Klíč zdroje, když je registrovaných víc databází.</param>
    protected RelationalSchemaSource(
        Func<DbConnection> connectionFactory,
        bool ownsConnection = true,
        string? key = null)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);

        _connectionFactory = connectionFactory;
        _ownsConnection = ownsConnection;
        Key = key ?? ISchemaSource.DefaultKey;
    }

    public string Key { get; }

    public abstract string DisplayName { get; }

    public SchemaSourceKind Kind => SchemaSourceKind.LiveDatabase;

    /// <summary>Provider, pro který je čtečka napsaná.</summary>
    protected abstract DbProviderKind Provider { get; }

    /// <summary>Jméno providera do hlavičky schématu.</summary>
    protected abstract string ProviderName { get; }

    /// <summary>Přečte surová data z otevřeného připojení.</summary>
    protected abstract Task<RawSchema> ReadRawAsync(
        DbConnection connection,
        SchemaReadOptions options,
        List<string> warnings,
        CancellationToken cancellationToken);

    /// <summary>
    /// Připojení, se kterým zdroj pracuje. Umožňuje náhledu dat použít stejné spojení
    /// místo navazování druhého.
    /// </summary>
    public DbConnection GetConnection() => _connectionFactory();

    public async Task<DatabaseSchema> ReadAsync(
        SchemaReadOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var warnings = new List<string>();
        var connection = _connectionFactory();
        var openedHere = false;

        try
        {
            openedHere = await QueryRunner.EnsureOpenAsync(connection, cancellationToken).ConfigureAwait(false);
            var raw = await ReadRawAsync(connection, options, warnings, cancellationToken).ConfigureAwait(false);

            return LiveSchemaAssembler.Build(
                raw with { Warnings = [.. raw.Warnings, .. warnings] },
                options,
                Provider,
                ProviderName,
                DisplayName);
        }
        finally
        {
            if (openedHere && _ownsConnection)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }

            if (_ownsConnection)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
