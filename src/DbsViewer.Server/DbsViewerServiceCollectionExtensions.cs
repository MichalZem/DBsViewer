using System.Data.Common;
using DbsViewer.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DbsViewer.Server;

/// <summary>Registrace prohlížečky do kontejneru služeb.</summary>
public static class DbsViewerServiceCollectionExtensions
{
    /// <summary>
    /// Zaregistruje prohlížečku nad zadaným <c>DbContext</c>. Zdroj z EF modelu se přidá
    /// vždy; živá introspekce jen tehdy, je-li povolená a je-li k dispozici čtečka
    /// pro daného providera.
    /// </summary>
    /// <typeparam name="TContext">Kontext, ze kterého se čte model.</typeparam>
    /// <param name="services">Kontejner služeb.</param>
    /// <param name="configure">Volitelné nastavení.</param>
    public static IServiceCollection AddDbsViewer<TContext>(
        this IServiceCollection services,
        Action<DbsViewerOptions>? configure = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = CreateOptions(configure);
        services.AddSingleton(options);

        services.AddScoped<ISchemaSource>(provider =>
            new EfCoreModelSchemaSource(provider.GetRequiredService<TContext>()));

        // Historie schématu je dostupná jen s DbContextem — snapshoty leží v assembly
        // aplikace, ne v databázi. U registrace přes vlastní ISchemaSource proto chybí.
        services.AddScoped(provider =>
            new MigrationHistoryReader(provider.GetRequiredService<TContext>()));

        if (options.IncludeLiveDatabase)
        {
            services.AddSingleton<DbsViewerLiveSourceFactory>();

            services.AddScoped(provider => CreateLiveSource(
                provider.GetRequiredService<TContext>(),
                provider.GetRequiredService<DbsViewerLiveSourceFactory>()));
        }

        return AddCommon(services);
    }

    /// <summary>
    /// Zaregistruje prohlížečku bez <c>DbContext</c>, jen nad zadaným zdrojem schématu.
    /// Hodí se pro cizí nebo legacy databázi.
    /// </summary>
    public static IServiceCollection AddDbsViewer(
        this IServiceCollection services,
        Func<IServiceProvider, ISchemaSource> sourceFactory,
        Action<DbsViewerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(sourceFactory);

        services.AddSingleton(CreateOptions(configure));
        services.AddScoped(sourceFactory);

        return AddCommon(services);
    }

    private static IServiceCollection AddCommon(IServiceCollection services)
    {
        services.TryAddTimeProvider();

        // Cache musí přežít požadavek, jinak by introspekce běžela pokaždé znovu.
        services.AddSingleton<SchemaCache>();
        services.AddScoped<SchemaProvider>();
        services.AddScoped<DataPreviewService>();

        return services;
    }

    private static DbsViewerOptions CreateOptions(Action<DbsViewerOptions>? configure)
    {
        var options = new DbsViewerOptions();
        configure?.Invoke(options);
        return options;
    }

    /// <summary>
    /// Vytvoří zdroj živé introspekce podle providera kontextu. Připojení se sdílí
    /// s kontextem, takže se nenavazuje druhé a platí pro něj stejná konfigurace.
    /// </summary>
    private static ISchemaSource CreateLiveSource(DbContext context, DbsViewerLiveSourceFactory factory) =>
        factory.Create(context.Database.ProviderName, context.Database.GetDbConnection());

    private static void TryAddTimeProvider(this IServiceCollection services)
    {
        if (!services.Any(static d => d.ServiceType == typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }
}

/// <summary>
/// Vytváří zdroj živé introspekce podle providera. Registruje se jako služba, aby se
/// dala v testech podvrhnout a aby <c>DbsViewer.Server</c> nemusel referencovat
/// balíčky obou providerů napevno.
/// </summary>
public class DbsViewerLiveSourceFactory
{
    private readonly Dictionary<string, Func<DbConnection, ISchemaSource>> _factories =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Vytvoří továrnu se čtečkami pro podporované providery. Registrace je předvyplněná
    /// schválně — cílem je, aby zapojení do aplikace nevyžadovalo víc než dva řádky.
    /// </summary>
    public DbsViewerLiveSourceFactory()
    {
        Register("SqlServer", static connection => new SqlServer.SqlServerSchemaSource(connection));
        Register("Sqlite", static connection => new Sqlite.SqliteSchemaSource(connection));
    }

    /// <summary>
    /// Zaregistruje čtečku pro providera. <paramref name="providerMarker"/> je podřetězec
    /// jména EF providera, například <c>SqlServer</c>.
    /// </summary>
    public DbsViewerLiveSourceFactory Register(
        string providerMarker,
        Func<DbConnection, ISchemaSource> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerMarker);
        ArgumentNullException.ThrowIfNull(factory);

        _factories[providerMarker] = factory;
        return this;
    }

    /// <summary>Vytvoří zdroj pro daného providera, nebo vyhodí výjimku s návodem.</summary>
    public ISchemaSource Create(string? providerName, DbConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        foreach (var (marker, factory) in _factories)
        {
            if (providerName is not null
                && providerName.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return factory(connection);
            }
        }

        throw new InvalidOperationException(
            $"Pro providera '{providerName ?? "(neznámý)"}' není zaregistrovaná čtečka živé databáze. "
            + "Přidej balíček DbsViewer.SqlServer nebo DbsViewer.Sqlite a zavolej jeho "
            + "registrační metodu, nebo vypni IncludeLiveDatabase.");
    }
}
