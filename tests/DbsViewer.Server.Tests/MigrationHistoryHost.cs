using DbsViewer.SampleMigrations;
using DbsViewer.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DbsViewer.Tests.Server;

/// <summary>
/// Aplikace nad modelem se skutečnými EF migracemi.
/// </summary>
/// <remarks>
/// Existuje vedle <see cref="DbsViewerApp"/>, protože ukázkový obchod migrace nemá —
/// a historie schématu se dá číst jedině z assembly, která migrace opravdu obsahuje.
/// Databáze se zakládá migracemi, ne <c>EnsureCreated</c>, aby v ní byla i historie
/// v <c>__EFMigrationsHistory</c>.
/// </remarks>
public sealed class MigrationHost : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly SqliteConnection _connection;

    private MigrationHost(IHost host, SqliteConnection connection, HttpClient client)
    {
        _host = host;
        _connection = connection;
        Client = client;
    }

    public HttpClient Client { get; }

    /// <summary>Postaví aplikaci a aplikuje na databázi zadaný počet migrací.</summary>
    /// <param name="applyAll">
    /// Aplikovat všechny migrace. Když <c>false</c>, databáze zůstane prázdná a všechny
    /// migrace se hlásí jako čekající.
    /// </param>
    public static async Task<MigrationHost> StartAsync(bool applyAll = true)
    {
        var connection = new SqliteConnection(
            $"Data Source=DbsViewerMigrace_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");

        await connection.OpenAsync();

        if (applyAll)
        {
            await using var context = new BlogContext(
                new DbContextOptionsBuilder<BlogContext>().UseSqlite(connection).Options);

            await context.Database.MigrateAsync();
        }

        var builder = new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.UseEnvironment("Development");

            web.ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddDbContext<BlogContext>(o => o.UseSqlite(connection));
                services.AddDbsViewer<BlogContext>(o => o.IncludeLiveDatabase = false);
            });

            web.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapDbsViewer());
            });
        });

        var host = await builder.StartAsync();

        return new MigrationHost(host, connection, host.GetTestClient());
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        _host.Dispose();
        await _connection.DisposeAsync();
    }
}
