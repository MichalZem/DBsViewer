using System.Net;
using System.Net.Http.Json;
using DbsViewer.Server;

namespace DbsViewer.Tests.Server;

/// <summary>
/// Úprava a mazání řádků proti běžící aplikaci a skutečné databázi. Skládání SQL testuje
/// <see cref="DataWriteBuilderTests"/>; tady jde o to, že se zápis opravdu projeví,
/// a hlavně o to, kdy se **neprojeví**.
/// </summary>
public class DataEditTests
{
    private const string Rows = "/dbschema/api/tables/-/Customers/rows";
    private const string Update = Rows + "/update";
    private const string Delete = Rows + "/delete";

    private static async Task<DbsViewerApp> StartAsync(
        bool allowUpdate = true,
        bool allowDelete = true,
        Action<DataPreviewOptions>? configure = null)
    {
        var app = await DbsViewerApp.StartAsync(o =>
        {
            o.DataPreview.Enabled = true;
            o.DataPreview.AllowUpdate = allowUpdate;
            o.DataPreview.AllowDelete = allowDelete;
            configure?.Invoke(o.DataPreview);
        });

        await app.ExecuteAsync(
            "INSERT INTO Customers (Id, Email, DisplayName, CreatedAt) "
            + "VALUES (1, 'prvni@x.cz', 'První', '2026-01-01')");

        await app.ExecuteAsync(
            "INSERT INTO Customers (Id, Email, DisplayName, CreatedAt) "
            + "VALUES (2, 'druhy@x.cz', NULL, '2026-01-02')");

        return app;
    }

    private static DataUpdate Zmena(string column, string? value, string id = "1") => new()
    {
        Key = [new DataValue("Id", id)],
        Values = [new DataValue(column, value)],
    };

    private static DataDelete Smazani(string id = "1") => new()
    {
        Key = [new DataValue("Id", id)],
    };

    /// <summary>Zpráva, kterou server poslal v poli <c>chyba</c>.</summary>
    private static async Task<string> ChybaAsync(HttpResponseMessage response)
    {
        var telo = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();

        return telo?["chyba"] ?? "";
    }

    // ---------- co se povede ----------

    [Fact]
    public async Task Upravena_hodnota_je_v_databazi()
    {
        await using var app = await StartAsync();

        var response = await app.Client.PostAsJsonAsync(Update, Zmena("DisplayName", "Opraveno"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var vysledek = await response.Content.ReadFromJsonAsync<DataChangeResult>();

        Assert.Equal(1, vysledek?.Affected);
        Assert.Equal("Opraveno", await app.ScalarAsync("SELECT DisplayName FROM Customers WHERE Id = 1"));
    }

    [Fact]
    public async Task Hodnotu_jde_vynulovat()
    {
        await using var app = await StartAsync();

        var response = await app.Client.PostAsJsonAsync(Update, Zmena("DisplayName", null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // ScalarAsync převádí hodnotu na text, takže se NULL ptáme přímo databáze.
        Assert.Equal("1", await app.ScalarAsync("SELECT DisplayName IS NULL FROM Customers WHERE Id = 1"));
    }

    [Fact]
    public async Task Smazany_radek_v_databazi_neni()
    {
        await using var app = await StartAsync();

        var response = await app.Client.PostAsJsonAsync(Delete, Smazani());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("1", await app.ScalarAsync("SELECT COUNT(*) FROM Customers"));
    }

    [Fact]
    public async Task Zapis_se_dotkne_jen_jednoho_radku()
    {
        await using var app = await StartAsync();

        await app.Client.PostAsJsonAsync(Update, Zmena("DisplayName", "Jen tenhle"));

        Assert.Equal("Jen tenhle", await app.ScalarAsync("SELECT DisplayName FROM Customers WHERE Id = 1"));
        Assert.Equal("1", await app.ScalarAsync("SELECT DisplayName IS NULL FROM Customers WHERE Id = 2"));
    }

    // ---------- co se nepovede ----------

    [Fact]
    public async Task Bez_zapnute_upravy_se_nezapisuje()
    {
        await using var app = await StartAsync(allowUpdate: false);

        var response = await app.Client.PostAsJsonAsync(Update, Zmena("DisplayName", "Nikdy"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("DataPreview.AllowUpdate", await ChybaAsync(response), StringComparison.Ordinal);
        Assert.Equal("První", await app.ScalarAsync("SELECT DisplayName FROM Customers WHERE Id = 1"));
    }

    [Fact]
    public async Task Bez_zapnuteho_mazani_se_nemaze()
    {
        await using var app = await StartAsync(allowDelete: false);

        var response = await app.Client.PostAsJsonAsync(Delete, Smazani());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("DataPreview.AllowDelete", await ChybaAsync(response), StringComparison.Ordinal);
        Assert.Equal("2", await app.ScalarAsync("SELECT COUNT(*) FROM Customers"));
    }

    [Fact]
    public async Task Vypnuty_nahled_dat_zapis_nepovoli()
    {
        await using var app = await DbsViewerApp.StartAsync(o =>
        {
            o.DataPreview.AllowUpdate = true;
            o.DataPreview.AllowDelete = true;
        });

        var response = await app.Client.PostAsJsonAsync(Update, Zmena("DisplayName", "Nikdy"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("Náhled dat je vypnutý", await ChybaAsync(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tabulka_mimo_whitelist_zapisu_se_odmitne()
    {
        await using var app = await StartAsync(configure: static p => p.EditableTables.Add("Orders"));

        var response = await app.Client.PostAsJsonAsync(Update, Zmena("DisplayName", "Nikdy"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("EditableTables", await ChybaAsync(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tabulka_na_whitelistu_zapisu_projde()
    {
        await using var app = await StartAsync(configure: static p => p.EditableTables.Add("Cust*"));

        var response = await app.Client.PostAsJsonAsync(Update, Zmena("DisplayName", "Smí se"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Neznama_tabulka_se_neupravuje()
    {
        await using var app = await StartAsync();

        var response = await app.Client.PostAsJsonAsync(
            "/dbschema/api/tables/-/Neexistuje/rows/update",
            Zmena("Cokoli", "x"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Zamaskovany_sloupec_se_neprepise()
    {
        await using var app = await StartAsync(configure: static p => p.MaskColumns.Add("DisplayName"));

        var response = await app.Client.PostAsJsonAsync(Update, Zmena("DisplayName", "Nikdy"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("zamaskovaný", await ChybaAsync(response), StringComparison.Ordinal);
        Assert.Equal("První", await app.ScalarAsync("SELECT DisplayName FROM Customers WHERE Id = 1"));
    }

    [Fact]
    public async Task Radek_ktery_uz_neexistuje_se_hlasi_jako_chyba()
    {
        await using var app = await StartAsync();

        var response = await app.Client.PostAsJsonAsync(Update, Zmena("DisplayName", "Pozdě", id: "999"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Řádek se nenašel", await ChybaAsync(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cizi_klic_smazani_zabrani_a_rekne_proc()
    {
        await using var app = await StartAsync();

        await app.ExecuteAsync("PRAGMA foreign_keys = ON");
        await app.ExecuteAsync(
            "INSERT INTO Orders (Id, Number, CustomerId, PlacedAt) VALUES (1, 'A-1', 1, '2026-01-03')");

        var response = await app.Client.PostAsJsonAsync(Delete, Smazani());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Databáze zápis odmítla", await ChybaAsync(response), StringComparison.Ordinal);
        Assert.Equal("2", await app.ScalarAsync("SELECT COUNT(*) FROM Customers"));
    }

    [Fact]
    public async Task Prazdne_telo_pozadavku_nic_nezmeni()
    {
        await using var app = await StartAsync();

        var response = await app.Client.PostAsJsonAsync<DataUpdate?>(Update, null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("nemění žádný sloupec", await ChybaAsync(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Prazdne_telo_mazani_radek_neurci()
    {
        await using var app = await StartAsync();

        var response = await app.Client.PostAsJsonAsync<DataDelete?>(Delete, null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("chybí hodnota klíče", await ChybaAsync(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Do_pohledu_se_nezapisuje()
    {
        await using var app = await StartAsync();

        var response = await app.Client.PostAsJsonAsync(
            "/dbschema/api/tables/-/OrderSummaries/rows/delete",
            new DataDelete { Key = [new DataValue("OrderId", "1")] });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("jednoznačně určit nedá", await ChybaAsync(response), StringComparison.Ordinal);
    }

    // ---------- metadata ----------

    [Fact]
    public async Task Meta_rekne_prohlizecce_co_se_smi()
    {
        await using var app = await StartAsync(allowDelete: false);

        var meta = await app.Client.GetFromJsonAsync<DbsViewerMeta>("/dbschema/api/meta");

        Assert.True(meta?.CanEditData);
        Assert.False(meta?.CanDeleteData);
    }

    [Fact]
    public async Task Bez_nahledu_dat_meta_zapis_nenabizi()
    {
        await using var app = await DbsViewerApp.StartAsync(static o =>
        {
            o.DataPreview.AllowUpdate = true;
            o.DataPreview.AllowDelete = true;
        });

        var meta = await app.Client.GetFromJsonAsync<DbsViewerMeta>("/dbschema/api/meta");

        Assert.False(meta?.CanEditData);
        Assert.False(meta?.CanDeleteData);
    }
}
