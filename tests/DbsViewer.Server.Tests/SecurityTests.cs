using System.Net;
using System.Net.Http.Json;
using DbsViewer.Server;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace DbsViewer.Tests.Server;

/// <summary>
/// Odolnost proti pokusu propašovat SQL. Náhled dat je jediné místo, kde se do dotazu
/// dostává jméno tabulky, takže je i jediným místem, kde by injekce mohla vzniknout.
/// </summary>
public class SqlInjectionTests
{
    [Fact]
    public async Task Jmeno_tabulky_z_pozadavku_se_neda_pouzit_k_injekci()
    {
        await using var app = await DbsViewerApp.StartAsync(o => o.DataPreview.Enabled = true);

        // Kdyby se jméno dostalo do dotazu bez ověření, tabulka by zmizela.
        var response = await app.Client.PostAsJsonAsync(
            "/dbschema/api/tables/-/Customers%3B%20DROP%20TABLE%20Orders%3B--/rows",
            new DataQuery());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // Orders musí pořád existovat.
        var schema = await app.Client.GetAsync("/dbschema/api/tables/-/Orders?source=live");
        Assert.Equal(HttpStatusCode.OK, schema.StatusCode);
    }

    [Fact]
    public async Task Tabulka_s_uvozovkou_v_nazvu_se_precte_bez_rozbiti_dotazu()
    {
        var connectionString = $"Data Source=Injekce_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

        await using var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync();

        await using (var command = keepAlive.CreateCommand())
        {
            // Jméno se zdvojenou uvozovkou je legální identifikátor.
            command.CommandText = """
                CREATE TABLE "Divn""a" (Id INTEGER PRIMARY KEY, Hodnota TEXT);
                INSERT INTO "Divn""a" (Hodnota) VALUES ('x');
                """;
            await command.ExecuteNonQueryAsync();
        }

        var live = new Sqlite.SqliteSchemaSource(connectionString);
        var options = new DbsViewerOptions();
        options.DataPreview.Enabled = true;

        ISchemaSource[] sources = [live];
        var cache = new SchemaCache(options, TimeProvider.System);
        var provider = new SchemaProvider(sources, options, cache, NullLogger<SchemaProvider>.Instance);
        var preview = new DataPreviewService(provider, options, sources, NullLogger<DataPreviewService>.Instance);

        var result = await preview.GetAsync(new DbObjectName(null, "Divn\"a"));

        Assert.Single(result.Rows);
    }

    [Fact]
    public void Escapovani_odolava_pokusu_o_uzavreni_identifikatoru()
    {
        // SQL Server: uzavírací závorka se zdvojí, takže dotaz nejde předčasně ukončit.
        var sqlServer = DataQueryBuilder.QuoteName(
            new DbObjectName("dbo", "T]; DROP TABLE Orders;--"), isSqlite: false);

        Assert.Equal("[dbo].[T]]; DROP TABLE Orders;--]", sqlServer);
        Assert.Equal(2, sqlServer.Count(c => c == '['));

        // SQLite: totéž s dvojitou uvozovkou.
        var sqlite = DataQueryBuilder.QuoteName(
            new DbObjectName(null, "T\"; DROP TABLE Orders;--"), isSqlite: true);

        Assert.StartsWith("\"T\"\";", sqlite, StringComparison.Ordinal);
        Assert.EndsWith("\"", sqlite, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Limit_radku_se_neda_prekrocit_pres_pozadavek()
    {
        await using var app = await DbsViewerApp.StartAsync(o =>
        {
            o.DataPreview.Enabled = true;
            o.DataPreview.MaxRows = 3;
        });

        for (var i = 0; i < 10; i++)
        {
            await app.ExecuteAsync(
                $"INSERT INTO Customers (Email, CreatedAt) VALUES ('u{i}@x.cz', '2026-01-01')");
        }

        // Ani záporná stránka, ani obrovská velikost nesmí strop obejít.
        foreach (var pageSize in new[] { 999999, -1, 0 })
        {
            var response = await app.Client.PostAsJsonAsync(
                "/dbschema/api/tables/-/Customers/rows",
                new DataQuery { PageSize = pageSize, Page = -5 });

            var json = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("\"pageSize\":", json, StringComparison.Ordinal);
            Assert.DoesNotContain("u9@x.cz", json, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Zapisovy_pozadavek_na_API_neexistuje()
    {
        await using var app = await DbsViewerApp.StartAsync();

        // Read-only je vlastnost API: jediné POST je obnovení cache.
        foreach (var (method, path) in new[]
        {
            (HttpMethod.Delete, "/dbschema/api/tables/-/Orders"),
            (HttpMethod.Put, "/dbschema/api/schema"),
            (HttpMethod.Post, "/dbschema/api/schema"),
        })
        {
            var response = await app.Client.SendAsync(new HttpRequestMessage(method, path));

            Assert.True(
                response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
                $"{method} {path} vrátilo {response.StatusCode}");
        }
    }
}
