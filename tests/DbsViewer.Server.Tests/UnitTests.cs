using System.Data.Common;
using DbsViewer.SampleShop;
using DbsViewer.Server;
using DbsViewer.Sqlite;
using DbsViewer.TestKit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DbsViewer.Tests.Server;

public class DataPreviewQueryTests
{
    private static DbTable Table(string? schema, string name, params string[] columns) => new()
    {
        Name = new DbObjectName(schema, name),
        Columns = [.. columns.Select((c, i) => new DbColumn { Name = c, Ordinal = i + 1, StoreType = "int" })],
    };

    [Fact]
    public void SQLite_dotaz_pouziva_LIMIT()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");

        Assert.Equal(
            "SELECT * FROM \"Orders\" LIMIT 50",
            DataPreviewService.BuildQuery(Table(null, "Orders"), 50, connection));
    }

    [Fact]
    public void SqlServer_dotaz_pouziva_TOP()
    {
        using var connection = new Microsoft.Data.SqlClient.SqlConnection();

        Assert.Equal(
            "SELECT TOP (25) * FROM [dbo].[Orders]",
            DataPreviewService.BuildQuery(Table("dbo", "Orders"), 25, connection));
    }

    [Theory]
    [InlineData("dbo", "Orders", false, "[dbo].[Orders]")]
    [InlineData(null, "Orders", false, "[Orders]")]
    [InlineData(null, "Order]s", false, "[Order]]s]")]
    [InlineData("od]d", "T", false, "[od]]d].[T]")]
    [InlineData(null, "Orders", true, "\"Orders\"")]
    [InlineData(null, "Order\"s", true, "\"Order\"\"s\"")]
    [InlineData("dbo", "Orders", true, "\"Orders\"")]
    public void Jmeno_tabulky_se_escapuje(string? schema, string name, bool isSqlite, string expected) =>
        Assert.Equal(expected, DataPreviewService.QuoteName(new DbObjectName(schema, name), isSqlite));

    [Fact]
    public void Hodnoty_se_prevedou_na_text()
    {
        using var reader = new FakeDataReader(
        [
            null,
            42,
            "text",
            new byte[] { 1, 2, 3 },
            new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            12.5m,
            true,
        ]);

        reader.Read();

        Assert.Null(DataPreviewService.Format(reader, 0));
        Assert.Equal("42", DataPreviewService.Format(reader, 1));
        Assert.Equal("text", DataPreviewService.Format(reader, 2));
        Assert.Equal("0x… (3 B)", DataPreviewService.Format(reader, 3));
        Assert.StartsWith("2026-01-02T03:04:05", DataPreviewService.Format(reader, 4)!, StringComparison.Ordinal);
        Assert.StartsWith("2026-01-02T03:04:05", DataPreviewService.Format(reader, 5)!, StringComparison.Ordinal);
        Assert.Equal("12.5", DataPreviewService.Format(reader, 6));
        Assert.Equal("True", DataPreviewService.Format(reader, 7));
    }

    [Fact]
    public async Task Maskovane_sloupce_se_nahradi_hvezdickami()
    {
        await using var app = await DbsViewerApp.StartAsync(o =>
        {
            o.DataPreview.Enabled = true;
            o.DataPreview.MaskColumns.Add("Email");
        });

        await app.ExecuteAsync(
            "INSERT INTO Customers (Email, DisplayName, CreatedAt) VALUES ('tajne@x.cz', 'Adam', '2026-01-01')");

        var response = await app.Client.GetAsync("/dbschema/api/tables/-/Customers/rows");
        var json = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("tajne@x.cz", json, StringComparison.Ordinal);
        Assert.Contains("Adam", json, StringComparison.Ordinal);
        Assert.Contains("Email", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nahled_si_otevre_vlastni_pripojeni_a_zase_ho_zavre()
    {
        var connectionString = $"Data Source=Preview_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

        // Držící připojení udrží databázi v paměti naživu.
        await using var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync();

        await using (var command = keepAlive.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE T (Id INTEGER PRIMARY KEY, Nazev TEXT);
                INSERT INTO T (Nazev) VALUES ('první'), ('druhý');
                """;
            await command.ExecuteNonQueryAsync();
        }

        // Zdroj si vytvoří vlastní zavřené připojení — náhled ho musí otevřít i zavřít.
        var live = new SqliteSchemaSource(connectionString);
        var options = new DbsViewerOptions();
        options.DataPreview.Enabled = true;

        ISchemaSource[] sources = [live];
        var cache = new SchemaCache(options, TimeProvider.System);
        var provider = new SchemaProvider(sources, options, cache, NullLogger<SchemaProvider>.Instance);
        var preview = new DataPreviewService(provider, options, sources, NullLogger<DataPreviewService>.Instance);

        var result = await preview.GetAsync(new DbObjectName(null, "T"), user: "test");

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(["Id", "Nazev"], result.Columns.ToList());
        Assert.Empty(result.MaskedColumns);
        Assert.False(result.IsTruncated);
    }

    [Fact]
    public async Task Nahled_bez_pripojeni_k_databazi_je_odmitnut()
    {
        // Registrovaný je jen EF model, který data neumí.
        await using var context = ShopContextFactory.CreateSqlite();
        var options = new DbsViewerOptions { IncludeLiveDatabase = false };
        options.DataPreview.Enabled = true;

        var sources = new ISchemaSource[] { new EfCore.EfCoreModelSchemaSource(context) };
        var cache = new SchemaCache(options, TimeProvider.System);
        var provider = new SchemaProvider(sources, options, cache, NullLogger<SchemaProvider>.Instance);
        var preview = new DataPreviewService(provider, options, sources, NullLogger<DataPreviewService>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            preview.GetAsync(new DbObjectName(null, "Customers")));

        Assert.Contains("IncludeLiveDatabase", exception.Message, StringComparison.Ordinal);
    }
}

public class SchemaProviderTests
{
    private static (SchemaProvider Provider, ShopContext Context) Create(
        DbsViewerOptions options,
        bool includeLive)
    {
        var connection = new SqliteConnection($"Data Source=Provider_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        connection.Open();

        var context = new ShopContext(
            new DbContextOptionsBuilder<ShopContext>().UseSqlite(connection).Options);

        context.Database.EnsureCreated();

        ISchemaSource[] sources = includeLive
            ? [new EfCore.EfCoreModelSchemaSource(context), new SqliteSchemaSource(connection)]
            : [new EfCore.EfCoreModelSchemaSource(context)];

        var cache = new SchemaCache(options, TimeProvider.System);

        return (new SchemaProvider(sources, options, cache, NullLogger<SchemaProvider>.Instance), context);
    }

    [Fact]
    public async Task Nedostupny_pohled_je_chyba()
    {
        var options = new DbsViewerOptions { IncludeLiveDatabase = false };
        var (provider, context) = Create(options, includeLive: false);

        await using var _ = context;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetAsync(SchemaView.Live));

        Assert.Contains("není v této konfiguraci k dispozici", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diff_bez_obou_zdroju_je_chyba()
    {
        var options = new DbsViewerOptions { IncludeLiveDatabase = false };
        var (provider, context) = Create(options, includeLive: false);

        await using var _ = context;

        Assert.False(provider.CanDiff);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetDiffAsync());

        Assert.Contains("IncludeLiveDatabase", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Vychozi_pohled_je_slouceny_kdyz_jsou_oba_zdroje()
    {
        var options = new DbsViewerOptions();
        var (provider, context) = Create(options, includeLive: true);

        await using var _ = context;

        Assert.Equal(SchemaView.Merged, provider.DefaultView);
        Assert.Equal([SchemaView.Ef, SchemaView.Live, SchemaView.Merged], provider.AvailableViews.ToList());
        Assert.NotNull(await provider.GetAsync(provider.DefaultView));
    }

    [Fact]
    public async Task Zive_schema_se_da_vypnout_i_kdyz_je_zdroj_registrovany()
    {
        var options = new DbsViewerOptions { IncludeLiveDatabase = false };
        var (provider, context) = Create(options, includeLive: true);

        await using var _ = context;

        Assert.Null(provider.LiveSource);
        Assert.Equal(SchemaView.Ef, provider.DefaultView);
        Assert.False(provider.CanDiff);
    }
}

public class SchemaCacheTests
{
    private static DatabaseSchema Schema(string name) => new() { DatabaseName = name };

    [Fact]
    public async Task Druhe_volani_vrati_ulozene_schema()
    {
        var time = new FakeTimeProvider();
        var cache = new SchemaCache(new DbsViewerOptions(), time);
        var loads = 0;

        Task<DatabaseSchema> Load(CancellationToken _)
        {
            loads++;
            return Task.FromResult(Schema($"načtení {loads}"));
        }

        var first = await cache.GetOrLoadAsync(SchemaView.Ef, Load, refresh: false, CancellationToken.None);
        var second = await cache.GetOrLoadAsync(SchemaView.Ef, Load, refresh: false, CancellationToken.None);

        Assert.Equal(1, loads);
        Assert.Same(first, second);
    }

    [Fact]
    public async Task Po_vyprseni_se_nacte_znovu()
    {
        var time = new FakeTimeProvider();
        var options = new DbsViewerOptions { CacheFor = TimeSpan.FromMinutes(5) };
        var cache = new SchemaCache(options, time);
        var loads = 0;

        Task<DatabaseSchema> Load(CancellationToken _)
        {
            loads++;
            return Task.FromResult(Schema($"načtení {loads}"));
        }

        await cache.GetOrLoadAsync(SchemaView.Ef, Load, refresh: false, CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(6));
        await cache.GetOrLoadAsync(SchemaView.Ef, Load, refresh: false, CancellationToken.None);

        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task Vynucene_obnoveni_cache_obejde()
    {
        var cache = new SchemaCache(new DbsViewerOptions(), new FakeTimeProvider());
        var loads = 0;

        Task<DatabaseSchema> Load(CancellationToken _)
        {
            loads++;
            return Task.FromResult(Schema("x"));
        }

        await cache.GetOrLoadAsync(SchemaView.Ef, Load, refresh: false, CancellationToken.None);
        await cache.GetOrLoadAsync(SchemaView.Ef, Load, refresh: true, CancellationToken.None);

        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task Zneplatneni_zahodi_ulozena_schemata()
    {
        var cache = new SchemaCache(new DbsViewerOptions(), new FakeTimeProvider());
        var loads = 0;

        Task<DatabaseSchema> Load(CancellationToken _)
        {
            loads++;
            return Task.FromResult(Schema("x"));
        }

        await cache.GetOrLoadAsync(SchemaView.Ef, Load, refresh: false, CancellationToken.None);
        await cache.InvalidateAsync();
        await cache.GetOrLoadAsync(SchemaView.Ef, Load, refresh: false, CancellationToken.None);

        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task Vypnuta_cache_nic_neuklada()
    {
        var options = new DbsViewerOptions { CacheFor = TimeSpan.Zero };
        var cache = new SchemaCache(options, new FakeTimeProvider());
        var loads = 0;

        Task<DatabaseSchema> Load(CancellationToken _)
        {
            loads++;
            return Task.FromResult(Schema("x"));
        }

        await cache.GetOrLoadAsync(SchemaView.Ef, Load, refresh: false, CancellationToken.None);
        await cache.GetOrLoadAsync(SchemaView.Ef, Load, refresh: false, CancellationToken.None);

        Assert.Equal(2, loads);
    }

    [Fact]
    public async Task Ruzne_pohledy_maji_vlastni_zaznam()
    {
        var cache = new SchemaCache(new DbsViewerOptions(), new FakeTimeProvider());

        var ef = await cache.GetOrLoadAsync(
            SchemaView.Ef, _ => Task.FromResult(Schema("ef")), false, CancellationToken.None);
        var live = await cache.GetOrLoadAsync(
            SchemaView.Live, _ => Task.FromResult(Schema("live")), false, CancellationToken.None);

        Assert.Equal("ef", ef.DatabaseName);
        Assert.Equal("live", live.DatabaseName);
    }

    [Fact]
    public async Task Chybejici_nacitaci_funkce_je_chyba()
    {
        var cache = new SchemaCache(new DbsViewerOptions(), new FakeTimeProvider());

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            cache.GetOrLoadAsync(SchemaView.Ef, null!, false, CancellationToken.None));
    }

    [Fact]
    public void Cache_jde_uvolnit()
    {
        var cache = new SchemaCache(new DbsViewerOptions(), new FakeTimeProvider());

        cache.Dispose();
    }
}

public class ServiceRegistrationTests
{
    [Fact]
    public void Registrace_bez_kontextu_prida_vlastni_zdroj()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var source = new StubSource();
        services.AddDbsViewer(_ => source, o => o.Title = "Legacy");

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.Equal("Legacy", scope.ServiceProvider.GetRequiredService<DbsViewerOptions>().Title);
        Assert.Same(source, scope.ServiceProvider.GetRequiredService<ISchemaSource>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<SchemaProvider>());
        Assert.NotNull(provider.GetRequiredService<SchemaCache>());
    }

    [Fact]
    public void Vlastni_TimeProvider_se_neprepise()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var time = new FakeTimeProvider();
        services.AddSingleton<TimeProvider>(time);
        services.AddDbsViewer(_ => new StubSource());

        using var provider = services.BuildServiceProvider();

        Assert.Same(time, provider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void Bez_vlastniho_TimeProvideru_se_pouzije_systemovy()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbsViewer(_ => new StubSource());

        using var provider = services.BuildServiceProvider();

        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void Chybejici_argumenty_jsou_chyba()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DbsViewerServiceCollectionExtensions.AddDbsViewer(null!, _ => new StubSource()));

        Assert.Throws<ArgumentNullException>(() =>
            new ServiceCollection().AddDbsViewer((Func<IServiceProvider, ISchemaSource>)null!));

        Assert.Throws<ArgumentNullException>(() =>
            DbsViewerServiceCollectionExtensions.AddDbsViewer<ShopContext>(null!));
    }

    private sealed class StubSource : ISchemaSource
    {
        public string Key => ISchemaSource.DefaultKey;

        public string DisplayName => "Stub";

        public SchemaSourceKind Kind => SchemaSourceKind.EfModel;

        public Task<DatabaseSchema> ReadAsync(SchemaReadOptions options, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DatabaseSchema());
    }
}

public class LiveSourceFactoryTests
{
    [Fact]
    public void SQLite_pripojeni_dostane_svou_ctecku()
    {
        var factory = new DbsViewerLiveSourceFactory();
        using var connection = new SqliteConnection("Data Source=:memory:");

        var source = factory.Create("Microsoft.EntityFrameworkCore.Sqlite", connection);

        Assert.IsType<SqliteSchemaSource>(source);
    }

    [Fact]
    public void SqlServer_pripojeni_dostane_svou_ctecku()
    {
        var factory = new DbsViewerLiveSourceFactory();
        using var connection = new Microsoft.Data.SqlClient.SqlConnection();

        var source = factory.Create("Microsoft.EntityFrameworkCore.SqlServer", connection);

        Assert.IsType<SqlServer.SqlServerSchemaSource>(source);
    }

    [Theory]
    [InlineData("Npgsql.EntityFrameworkCore.PostgreSQL")]
    [InlineData(null)]
    public void Nepodporovany_provider_je_chyba_s_navodem(string? providerName)
    {
        var factory = new DbsViewerLiveSourceFactory();
        using var connection = new SqliteConnection("Data Source=:memory:");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            factory.Create(providerName, connection));

        Assert.Contains("IncludeLiveDatabase", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Vlastni_ctecka_jde_zaregistrovat()
    {
        var factory = new DbsViewerLiveSourceFactory();
        using var connection = new SqliteConnection("Data Source=:memory:");

        var same = factory.Register("Vlastni", _ => new SqliteSchemaSource(connection));

        Assert.Same(factory, same);
        Assert.IsType<SqliteSchemaSource>(factory.Create("Muj.Vlastni.Provider", connection));
    }

    [Fact]
    public void Chybejici_argumenty_jsou_chyba()
    {
        var factory = new DbsViewerLiveSourceFactory();

        Assert.Throws<ArgumentNullException>(() => factory.Register(null!, static _ => null!));
        Assert.Throws<ArgumentException>(() => factory.Register("  ", static _ => null!));
        Assert.Throws<ArgumentNullException>(() => factory.Register("x", null!));
        Assert.Throws<ArgumentNullException>(() => factory.Create("Sqlite", null!));
    }
}

/// <summary>Čas, který se dá posunout. Bez něj by testy vypršení cache musely čekat.</summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
