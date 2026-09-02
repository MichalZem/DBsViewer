using System.Net;
using System.Net.Http.Json;
using DbsViewer.Analysis;
using DbsViewer.Server;

namespace DbsViewer.Tests.Server;

/// <summary>
/// Historie schématu přes HTTP API: seznam migrací, schéma k dané verzi a porovnání
/// dvou verzí. Běží proti aplikaci se skutečnými migracemi.
/// </summary>
public class MigrationHistoryEndpointTests
{
    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<T>(DbsViewerJson.Compact)
            ?? throw new InvalidOperationException("Prázdná odpověď.");
    }

    private static async Task<IReadOnlyList<DbMigration>> MigraceAsync(MigrationHost app) =>
        await ReadAsync<List<DbMigration>>(await app.Client.GetAsync("/dbschema/api/migrations"));

    // ---------- seznam migrací ----------

    [Fact]
    public async Task Seznam_migraci_nese_i_zmeny()
    {
        await using var app = await MigrationHost.StartAsync();

        var migrace = await MigraceAsync(app);

        Assert.Equal(3, migrace.Count);
        Assert.All(migrace, m => Assert.True(m.HasSnapshot));

        // Prostřední migrace přidává jediný sloupec.
        var druha = migrace[1];
        var zmena = Assert.Single(druha.Changes);

        Assert.Equal(SchemaChangeKind.AddColumn, zmena.Kind);
        Assert.Equal("Publikovano", zmena.Object);
    }

    [Fact]
    public async Task Aplikovane_migrace_se_poznaji_od_cekajicich()
    {
        await using var aplikovane = await MigrationHost.StartAsync(applyAll: true);
        await using var cekajici = await MigrationHost.StartAsync(applyAll: false);

        Assert.All(await MigraceAsync(aplikovane), m => Assert.True(m.AppliedInDatabase));
        Assert.All(await MigraceAsync(cekajici), m => Assert.True(m.IsPending));
    }

    // ---------- schéma k dané verzi ----------

    [Fact]
    public async Task Schema_k_migraci_ukazuje_stav_v_tom_okamziku()
    {
        await using var app = await MigrationHost.StartAsync();
        var migrace = await MigraceAsync(app);

        var prvni = await ReadAsync<DatabaseSchema>(
            await app.Client.GetAsync($"/dbschema/api/schema?migration={migrace[0].Id}"));

        var posledni = await ReadAsync<DatabaseSchema>(
            await app.Client.GetAsync($"/dbschema/api/schema?migration={migrace[2].Id}"));

        // Tabulka Komentare vzniká až třetí migrací.
        Assert.Equal(2, prvni.Tables.Count);
        Assert.Equal(3, posledni.Tables.Count);
        Assert.DoesNotContain(prvni.Tables, t => t.Name.Name == "Komentare");
    }

    [Fact]
    public async Task Historicke_schema_zna_vazby_pro_diagram()
    {
        await using var app = await MigrationHost.StartAsync();
        var migrace = await MigraceAsync(app);

        var schema = await ReadAsync<DatabaseSchema>(
            await app.Client.GetAsync($"/dbschema/api/schema?migration={migrace[2].Id}"));

        // Bez vazeb by z historické verze nešel nakreslit diagram.
        Assert.Contains(schema.Relationships, r => r.From.Name == "Komentare");
        Assert.Equal(SchemaSourceKind.MigrationSnapshot, schema.SourceKind);
    }

    [Fact]
    public async Task Neznama_migrace_vrati_404_s_vysvetlenim()
    {
        await using var app = await MigrationHost.StartAsync();

        var response = await app.Client.GetAsync("/dbschema/api/schema?migration=20990101_Neexistuje");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("není v assembly", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bez_zadane_migrace_se_vraci_aktualni_schema()
    {
        await using var app = await MigrationHost.StartAsync();

        var schema = await ReadAsync<DatabaseSchema>(
            await app.Client.GetAsync("/dbschema/api/schema"));

        Assert.NotEqual(SchemaSourceKind.MigrationSnapshot, schema.SourceKind);
    }

    // ---------- porovnání dvou verzí ----------

    [Fact]
    public async Task Porovnani_sousednich_verzi_najde_pribyly_sloupec()
    {
        await using var app = await MigrationHost.StartAsync();
        var migrace = await MigraceAsync(app);

        var diff = await ReadAsync<SchemaDiff>(await app.Client.GetAsync(
            $"/dbschema/api/migrations/diff?from={migrace[0].Id}&to={migrace[1].Id}"));

        Assert.Contains(diff.Findings, f => f.Message.Contains("Publikovano", StringComparison.Ordinal)
                                            || f.Object == "Publikovano");
    }

    [Fact]
    public async Task Porovnani_vzdalenych_verzi_najde_i_novou_tabulku()
    {
        await using var app = await MigrationHost.StartAsync();
        var migrace = await MigraceAsync(app);

        var diff = await ReadAsync<SchemaDiff>(await app.Client.GetAsync(
            $"/dbschema/api/migrations/diff?from={migrace[0].Id}&to={migrace[2].Id}"));

        Assert.Contains(diff.Findings, f => f.Table?.Name == "Komentare");
    }

    [Fact]
    public async Task Porovnani_bez_vychozi_verze_ukaze_cely_vznik_schematu()
    {
        await using var app = await MigrationHost.StartAsync();
        var migrace = await MigraceAsync(app);

        var diff = await ReadAsync<SchemaDiff>(
            await app.Client.GetAsync($"/dbschema/api/migrations/diff?to={migrace[2].Id}"));

        // Proti prázdnému schématu je novotou úplně všechno.
        Assert.Contains(diff.Findings, f => f.Table?.Name == "Autori");
        Assert.Contains(diff.Findings, f => f.Table?.Name == "Komentare");
    }

    [Fact]
    public async Task Porovnani_stejne_verze_nenajde_nic()
    {
        await using var app = await MigrationHost.StartAsync();
        var migrace = await MigraceAsync(app);

        var diff = await ReadAsync<SchemaDiff>(await app.Client.GetAsync(
            $"/dbschema/api/migrations/diff?from={migrace[1].Id}&to={migrace[1].Id}"));

        Assert.Empty(diff.Findings);
    }

    [Fact]
    public async Task Porovnani_s_neznamou_verzi_vrati_404()
    {
        await using var app = await MigrationHost.StartAsync();

        var response = await app.Client.GetAsync(
            "/dbschema/api/migrations/diff?from=20990101_Neexistuje&to=20990102_Take");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- dostupnost ----------

    [Fact]
    public async Task Meta_hlasi_ze_historie_je_k_dispozici()
    {
        await using var app = await MigrationHost.StartAsync();

        var meta = await ReadAsync<DbsViewerMeta>(await app.Client.GetAsync("/dbschema/api/meta"));

        Assert.True(meta.CanBrowseHistory);
    }

    [Fact]
    public async Task Bez_migraci_se_historie_nenabizi()
    {
        // Ukázkový obchod migrace nemá, takže historie procházet nejde.
        await using var app = await DbsViewerApp.StartAsync();

        var response = await app.Client.GetAsync("/dbschema/api/meta");
        var meta = await response.Content.ReadFromJsonAsync<DbsViewerMeta>(DbsViewerJson.Compact);

        Assert.False(meta!.CanBrowseHistory);
    }
}
