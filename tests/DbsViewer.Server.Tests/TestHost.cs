using DbsViewer.SampleShop;
using DbsViewer.Server;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DbsViewer.Tests.Server;

/// <summary>
/// Aplikace s DbsViewerem nad SQLite databází v paměti. Databáze se vytvoří podle
/// ukázkového modelu, takže EF model i živá databáze mají co porovnávat.
/// </summary>
public sealed class DbsViewerApp : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IHost _host;

    private DbsViewerApp(SqliteConnection connection, IHost host)
    {
        _connection = connection;
        _host = host;
    }

    public HttpClient Client { get; private init; } = null!;

    /// <summary>Postaví aplikaci se zadaným nastavením a prostředím.</summary>
    public static async Task<DbsViewerApp> StartAsync(
        Action<DbsViewerOptions>? configure = null,
        string environment = "Development",
        bool createDatabase = true,
        Action<string>? mutateDatabase = null,
        bool useFakeUi = false)
    {
        var connection = new SqliteConnection($"Data Source=DbsViewerServer_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await connection.OpenAsync();

        if (createDatabase)
        {
            await using var context = CreateContext(connection);
            await context.Database.EnsureCreatedAsync();

            // EnsureCreated pohledy nevytváří, ale model je zná — bez toho by diff
            // hlásil chybějící tabulku, která ve skutečnosti jen není z EF vytvořitelná.
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE VIEW OrderSummaries AS
                SELECT o.Id AS OrderId, o.Number, c.Email AS CustomerEmail, 0 AS Total
                FROM Orders o JOIN Customers c ON c.Id = o.CustomerId
                """;
            await command.ExecuteNonQueryAsync();
        }

        if (mutateDatabase is not null)
        {
            mutateDatabase(connection.ConnectionString);
        }

        var builder = new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.UseEnvironment(environment);

            web.ConfigureServices(services =>
            {
                services.AddLogging(static logging => logging.SetMinimumLevel(LogLevel.Warning));
                services.AddRouting();
                services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName, static _ => { });

                services.AddAuthorizationBuilder()
                    .AddPolicy("Vsichni", static policy => policy.RequireAssertion(static _ => true))
                    .AddPolicy("Nikdo", static policy => policy.RequireAssertion(static _ => false));

                services.AddDbContext<ShopContext>(options => options.UseSqlite(connection));
                services.AddDbsViewer<ShopContext>(options =>
                {
                    if (useFakeUi)
                    {
                        // Skutečné UI v testovací sestavě není; tohle podstrčí jeho náhradu.
                        options.UiAssembly = typeof(DbsViewerApp).Assembly;
                    }

                    configure?.Invoke(options);
                });
            });

            web.Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapDbsViewer());
            });
        });

        var host = await builder.StartAsync();

        return new DbsViewerApp(connection, host)
        {
            Client = host.GetTestClient(),
        };
    }

    private static ShopContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<ShopContext>().UseSqlite(connection).Options);

    /// <summary>Spustí SQL proti testovací databázi — kvůli vyrobení driftu.</summary>
    public async Task ExecuteAsync(string sql)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Přečte jednu hodnotu — kvůli ověření, že se tabulka nezměnila.</summary>
    public async Task<string?> ScalarAsync(string sql)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = sql;

        var value = await command.ExecuteScalarAsync();

        return value?.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        _host.Dispose();
        await _connection.DisposeAsync();
    }
}

/// <summary>
/// Přihlásí každý požadavek jako testovacího uživatele. Bez autentizačního schématu
/// by odmítnutá autorizace skončila výjimkou místo stavového kódu.
/// </summary>
internal sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "testovaci-uzivatel")], SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
