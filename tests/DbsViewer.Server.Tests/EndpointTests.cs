using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DbsViewer.Analysis;
using DbsViewer.Server;

namespace DbsViewer.Tests.Server;

/// <summary>Chování HTTP endpointů proti skutečně běžící aplikaci.</summary>
public class EndpointTests
{
    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(json, DbsViewerJson.Compact)!;
    }

    // ---------- meta ----------

    [Fact]
    public async Task Meta_popisuje_dostupne_funkce()
    {
        await using var app = await DbsViewerApp.StartAsync();

        var meta = await ReadAsync<DbsViewerMeta>(await app.Client.GetAsync("/dbschema/api/meta"));

        Assert.Equal("Schéma databáze", meta.Title);
        Assert.Equal("/dbschema", meta.RoutePrefix);
        Assert.Equal(["ef", "live", "merged"], meta.Views.ToList());
        Assert.True(meta.CanDiff);
        Assert.False(meta.CanPreviewData);
        Assert.Equal(100, meta.DataPreviewMaxRows);
    }

    [Fact]
    public async Task Meta_bez_zive_databaze_nabizi_jen_EF_model()
    {
        await using var app = await DbsViewerApp.StartAsync(o => o.IncludeLiveDatabase = false);

        var meta = await ReadAsync<DbsViewerMeta>(await app.Client.GetAsync("/dbschema/api/meta"));

        Assert.Equal(["ef"], meta.Views.ToList());
        Assert.False(meta.CanDiff);
    }

    [Fact]
    public async Task Meta_nese_vlastni_nadpis_i_skupiny()
    {
        await using var app = await DbsViewerApp.StartAsync(o =>
        {
            o.Title = "Eshop";
            o.Groups["Sklad"] = "Product*";
        });

        var meta = await ReadAsync<DbsViewerMeta>(await app.Client.GetAsync("/dbschema/api/meta"));

        Assert.Equal("Eshop", meta.Title);
        Assert.Equal("Product*", meta.Groups["Sklad"]);
    }

    // ---------- schéma ----------

    [Fact]
    public async Task Schema_se_vrati_ve_vychozim_pohledu()
    {
        await using var app = await DbsViewerApp.StartAsync();

        var schema = await ReadAsync<DatabaseSchema>(await app.Client.GetAsync("/dbschema/api/schema"));

        Assert.Equal(SchemaSourceKind.Merged, schema.SourceKind);
        Assert.NotEmpty(schema.Tables);
        Assert.Contains(schema.Tables, t => t.Name.Name == "Customers");
    }

    [Theory]
    [InlineData("ef", SchemaSourceKind.EfModel)]
    [InlineData("live", SchemaSourceKind.LiveDatabase)]
    [InlineData("merged", SchemaSourceKind.Merged)]
    public async Task Pohled_jde_vybrat(string source, SchemaSourceKind expected)
    {
        await using var app = await DbsViewerApp.StartAsync();

        var schema = await ReadAsync<DatabaseSchema>(
            await app.Client.GetAsync($"/dbschema/api/schema?source={source}"));

        Assert.Equal(expected, schema.SourceKind);
    }

    [Fact]
    public async Task Bez_zive_databaze_je_vychozi_pohled_EF_model()
    {
        await using var app = await DbsViewerApp.StartAsync(o => o.IncludeLiveDatabase = false);

        var schema = await ReadAsync<DatabaseSchema>(await app.Client.GetAsync("/dbschema/api/schema"));

        Assert.Equal(SchemaSourceKind.EfModel, schema.SourceKind);
    }

    [Fact]
    public async Task Neznamy_pohled_je_odmitnut()
    {
        await using var app = await DbsViewerApp.StartAsync();

        var response = await app.Client.GetAsync("/dbschema/api/schema?source=nesmysl");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Neznámý pohled", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Skryte_tabulky_se_do_odpovedi_nedostanou()
    {
        await using var app = await DbsViewerApp.StartAsync(o => o.HideTables.Add("Product*"));

        var schema = await ReadAsync<DatabaseSchema>(await app.Client.GetAsync("/dbschema/api/schema"));

        Assert.DoesNotContain(schema.Tables, t => t.Name.Name.StartsWith("Product", StringComparison.Ordinal));
    }

    // ---------- detail tabulky ----------

    [Fact]
    public async Task Detail_tabulky_se_vrati()
    {
        await using var app = await DbsViewerApp.StartAsync();

        var table = await ReadAsync<DbTable>(await app.Client.GetAsync("/dbschema/api/tables/-/Customers"));

        Assert.Equal("Customers", table.Name.Name);
        Assert.Contains(table.Columns, c => c.Name == "Email");
    }

    [Fact]
    public async Task Neexistujici_tabulka_vraci_404()
    {
        await using var app = await DbsViewerApp.StartAsync();

        var response = await app.Client.GetAsync("/dbschema/api/tables/-/Neexistuje");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Detail_tabulky_odmitne_neznamy_pohled()
    {
        await using var app = await DbsViewerApp.StartAsync();

        var response = await app.Client.GetAsync("/dbschema/api/tables/-/Customers?source=nesmysl");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- diff ----------

    [Fact]
    public async Task Diff_shodnych_schemat_je_cisty()
    {
        await using var app = await DbsViewerApp.StartAsync();

        var diff = await ReadAsync<SchemaDiff>(await app.Client.GetAsync("/dbschema/api/schema/diff"));

        // Databáze vznikla z téhož modelu, takže vážné nálezy být nemají.
        Assert.True(
            diff.ErrorCount == 0,
            string.Join(Environment.NewLine, diff.Findings.Select(f => $"{f} [model={f.ModelValue}, db={f.DatabaseValue}]")));
    }

    [Fact]
    public async Task Diff_najde_drift_v_databazi()
    {
        await using var app = await DbsViewerApp.StartAsync();
        await app.ExecuteAsync("CREATE INDEX IX_Rucne_Pridany ON Customers (DisplayName)");

        var diff = await ReadAsync<SchemaDiff>(
            await app.Client.GetAsync("/dbschema/api/schema/diff?refresh=true"));

        Assert.Contains(diff.Findings, f => f.Kind == DiffKind.IndexMissingInModel);
    }

    [Fact]
    public async Task Diff_bez_zive_databaze_je_odmitnut()
    {
        await using var app = await DbsViewerApp.StartAsync(o => o.IncludeLiveDatabase = false);

        var response = await app.Client.GetAsync("/dbschema/api/schema/diff");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("IncludeLiveDatabase", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    // ---------- náhled dat ----------

    [Fact]
    public async Task Nahled_dat_je_ve_vychozim_stavu_zakazany()
    {
        await using var app = await DbsViewerApp.StartAsync();

        var response = await PostRowsAsync(app, "/dbschema/api/tables/-/Customers/rows");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("vypnutý", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Zapnuty_nahled_dat_vrati_radky()
    {
        await using var app = await DbsViewerApp.StartAsync(o => o.DataPreview.Enabled = true);
        await app.ExecuteAsync(
            "INSERT INTO Customers (Email, DisplayName, CreatedAt) VALUES ('a@b.cz', 'Adam', '2026-01-01')");

        var preview = await ReadAsync<DataPreview>(
            await PostRowsAsync(app, "/dbschema/api/tables/-/Customers/rows"));

        Assert.Equal("Customers", preview.Table.Name);
        Assert.Contains("Email", preview.Columns);
        Assert.Single(preview.Rows);
        Assert.Contains("a@b.cz", preview.Rows[0]!);
    }

    [Fact]
    public async Task Nahled_dat_respektuje_whitelist()
    {
        await using var app = await DbsViewerApp.StartAsync(o =>
        {
            o.DataPreview.Enabled = true;
            o.DataPreview.AllowedTables.Add("Orders");
        });

        var allowed = await PostRowsAsync(app, "/dbschema/api/tables/-/Orders/rows");
        var denied = await PostRowsAsync(app, "/dbschema/api/tables/-/Customers/rows");

        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task Nahled_dat_odmitne_neexistujici_tabulku()
    {
        await using var app = await DbsViewerApp.StartAsync(o => o.DataPreview.Enabled = true);

        var response = await PostRowsAsync(app, "/dbschema/api/tables/-/Neexistuje/rows");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("ve schématu není", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Limit_radku_se_orizne_na_maximum()
    {
        await using var app = await DbsViewerApp.StartAsync(o =>
        {
            o.DataPreview.Enabled = true;
            o.DataPreview.MaxRows = 2;
        });

        for (var i = 0; i < 5; i++)
        {
            await app.ExecuteAsync(
                $"INSERT INTO Customers (Email, CreatedAt) VALUES ('u{i}@x.cz', '2026-01-01')");
        }

        var preview = await ReadAsync<DataPreview>(
            await PostRowsAsync(
                app,
                "/dbschema/api/tables/-/Customers/rows",
                new DataQuery { PageSize = 100 }));

        // Stránka se ořízne na MaxRows, takže z pěti řádků přijdou dva a další stránka
        // se nabídne.
        Assert.Equal(2, preview.Rows.Count);
        Assert.Equal(2, preview.PageSize);
        Assert.Equal(5, preview.TotalRows);
        Assert.True(preview.HasMore);
    }

    // ---------- obnovení ----------

    [Fact]
    public async Task Obnoveni_zahodi_cache()
    {
        await using var app = await DbsViewerApp.StartAsync();

        var before = await ReadAsync<DatabaseSchema>(
            await app.Client.GetAsync("/dbschema/api/schema?source=live"));
        Assert.DoesNotContain(before.Tables, t => t.Name.Name == "NovaTabulka");

        await app.ExecuteAsync("CREATE TABLE NovaTabulka (Id INTEGER PRIMARY KEY)");

        var response = await app.Client.PostAsync("/dbschema/api/refresh", null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var after = await ReadAsync<DatabaseSchema>(
            await app.Client.GetAsync("/dbschema/api/schema?source=live"));

        Assert.Contains(after.Tables, t => t.Name.Name == "NovaTabulka");
    }

    [Fact]
    public async Task Cache_vraci_stejny_snimek_dokud_nevyprsi()
    {
        await using var app = await DbsViewerApp.StartAsync();

        var first = await ReadAsync<DatabaseSchema>(
            await app.Client.GetAsync("/dbschema/api/schema?source=live"));

        await app.ExecuteAsync("CREATE TABLE JinaTabulka (Id INTEGER PRIMARY KEY)");

        var second = await ReadAsync<DatabaseSchema>(
            await app.Client.GetAsync("/dbschema/api/schema?source=live"));

        Assert.Equal(first.Tables.Count, second.Tables.Count);
        Assert.Equal(first.GeneratedAtUtc, second.GeneratedAtUtc);
    }

    [Fact]
    public async Task Parametr_refresh_obejde_cache()
    {
        await using var app = await DbsViewerApp.StartAsync();

        await ReadAsync<DatabaseSchema>(await app.Client.GetAsync("/dbschema/api/schema?source=live"));
        await app.ExecuteAsync("CREATE TABLE DalsiTabulka (Id INTEGER PRIMARY KEY)");

        var refreshed = await ReadAsync<DatabaseSchema>(
            await app.Client.GetAsync("/dbschema/api/schema?source=live&refresh=true"));

        Assert.Contains(refreshed.Tables, t => t.Name.Name == "DalsiTabulka");
    }

    [Fact]
    public async Task Vypnuta_cache_cte_pokazde_znovu()
    {
        await using var app = await DbsViewerApp.StartAsync(o => o.CacheFor = TimeSpan.Zero);

        await ReadAsync<DatabaseSchema>(await app.Client.GetAsync("/dbschema/api/schema?source=live"));
        await app.ExecuteAsync("CREATE TABLE BezCache (Id INTEGER PRIMARY KEY)");

        var second = await ReadAsync<DatabaseSchema>(
            await app.Client.GetAsync("/dbschema/api/schema?source=live"));

        Assert.Contains(second.Tables, t => t.Name.Name == "BezCache");
    }

    // ---------- cesty a prostředí ----------

    [Fact]
    public async Task Vlastni_prefix_cesty_funguje()
    {
        await using var app = await DbsViewerApp.StartAsync(o => o.RoutePrefix = "/_db");

        Assert.Equal(HttpStatusCode.OK, (await app.Client.GetAsync("/_db/api/meta")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.GetAsync("/dbschema/api/meta")).StatusCode);
    }

    [Fact]
    public async Task Ve_vypnutem_prostredi_endpointy_neexistuji()
    {
        await using var app = await DbsViewerApp.StartAsync(
            o => o.EnabledIn = HostEnv.Development,
            environment: "Staging");

        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.GetAsync("/dbschema/api/meta")).StatusCode);
    }

    [Fact]
    public async Task Ve_Staging_s_policy_endpointy_bezi()
    {
        await using var app = await DbsViewerApp.StartAsync(
            o =>
            {
                o.EnabledIn = HostEnv.All;
                o.RequireAuthorization("Vsichni");
            },
            environment: "Staging");

        var response = await app.Client.GetAsync("/dbschema/api/meta");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Nesplnena_policy_pristup_odmitne()
    {
        await using var app = await DbsViewerApp.StartAsync(
            o =>
            {
                o.EnabledIn = HostEnv.All;
                o.RequireAuthorization("Nikdo");
            },
            environment: "Staging");

        var response = await app.Client.GetAsync("/dbschema/api/meta");

        // Endpoint existuje, ale autorizace ho neproustí.
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Bez_policy_mimo_Development_aplikace_nenastartuje()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await DbsViewerApp.StartAsync(o => o.EnabledIn = HostEnv.All, environment: "Production"));

        Assert.Contains("autorizační policy", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Náhled dat chodí POSTem, protože hledané hodnoty jsou obsah databáze a v adrese
    /// by skončily v historii prohlížeče i v logu serveru.
    /// </summary>
    private static Task<HttpResponseMessage> PostRowsAsync(
        DbsViewerApp app,
        string url,
        DataQuery? query = null) =>
        app.Client.PostAsJsonAsync(url, query ?? new DataQuery());
}
