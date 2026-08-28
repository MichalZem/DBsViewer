using System.Net;
using DbsViewer.Server;

namespace DbsViewer.Tests.Server;

/// <summary>
/// Servírování zabudovaného UI. Vlastní soubory se do testovací sestavy nevkládají,
/// takže se testuje překlad cest a chování bez UI; přítomnost souborů v balíčku
/// ověřuje build.
/// </summary>
public class UiHostingTests
{
    [Theory]
    [InlineData("dbsviewer.css", "DbsViewer.Server.ui.dbsviewer.css")]
    [InlineData("/dbsviewer.css", "DbsViewer.Server.ui.dbsviewer.css")]
    [InlineData("_framework/blazor.webassembly.js", "DbsViewer.Server.ui._framework.blazor.webassembly.js")]
    public void Cesta_se_prelozi_na_jmeno_prostredku(string path, string expected) =>
        Assert.Equal(expected, UiHosting.ResourceNameFor(path));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    [InlineData("../secrets.json")]
    [InlineData("_framework/../../secrets")]
    public void Nebezpecna_nebo_prazdna_cesta_se_odmitne(string path) =>
        Assert.Null(UiHosting.ResourceNameFor(path));

    [Theory]
    [InlineData("index.html", "text/html; charset=utf-8")]
    [InlineData("app.css", "text/css; charset=utf-8")]
    [InlineData("app.js", "text/javascript; charset=utf-8")]
    [InlineData("app.mjs", "text/javascript; charset=utf-8")]
    [InlineData("blazor.boot.json", "application/json; charset=utf-8")]
    [InlineData("dotnet.wasm", "application/wasm")]
    [InlineData("System.dll", "application/octet-stream")]
    [InlineData("app.pdb", "application/octet-stream")]
    [InlineData("icudt.dat", "application/octet-stream")]
    [InlineData("dotnet.blat", "application/octet-stream")]
    [InlineData("app.js.br", "application/octet-stream")]
    [InlineData("app.js.gz", "application/octet-stream")]
    [InlineData("font.woff", "font/woff")]
    [InlineData("font.woff2", "font/woff2")]
    [InlineData("logo.svg", "image/svg+xml")]
    [InlineData("icon.png", "image/png")]
    [InlineData("favicon.ico", "image/x-icon")]
    [InlineData("neznamy.xyz", "application/octet-stream")]
    [InlineData("bezpripony", "application/octet-stream")]
    public void Typ_obsahu_odpovida_pripone(string path, string expected) =>
        Assert.Equal(expected, UiHosting.ContentType(path));

    [Fact]
    public void Wasm_ma_spravny_typ_jinak_by_Blazor_nenabehl() =>
        Assert.Equal("application/wasm", UiHosting.ContentType("_framework/dotnet.wasm"));

    [Theory]
    [InlineData("/dbschema", "<base href=\"/dbschema/\" />")]
    [InlineData("/dbschema/", "<base href=\"/dbschema/\" />")]
    [InlineData("/_db", "<base href=\"/_db/\" />")]
    public void Zaklad_cesty_se_prepise_podle_prefixu(string prefix, string expected)
    {
        const string Html = "<html><head><base href=\"/\" /></head><body></body></html>";

        Assert.Contains(expected, UiHosting.RewriteBaseHref(Html, prefix), StringComparison.Ordinal);
    }

    [Fact]
    public void Html_bez_znacky_base_zustane_beze_zmeny()
    {
        const string Html = "<html><head></head></html>";

        Assert.Equal(Html, UiHosting.RewriteBaseHref(Html, "/dbschema"));
    }

    [Fact]
    public void Neuzavrena_znacka_base_html_nerozbije()
    {
        const string Html = "<html><head><base href=\"/\"";

        Assert.Equal(Html, UiHosting.RewriteBaseHref(Html, "/dbschema"));
    }

    [Fact]
    public void Chybejici_html_je_chyba() =>
        Assert.Throws<ArgumentNullException>(() => UiHosting.RewriteBaseHref(null!, "/x"));

    [Fact]
    public async Task Bez_zabudovaneho_UI_se_vrati_vysvetleni()
    {
        // Testovací sestava soubory UI nemá, takže server musí poradit, kde je API.
        await using var app = await DbsViewerApp.StartAsync();

        var response = await app.Client.GetAsync("/dbschema");
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("api/schema", text, StringComparison.Ordinal);
        Assert.Contains("HTTP API funguje", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Se_zabudovanym_UI_se_vrati_stranka()
    {
        await using var app = await DbsViewerApp.StartAsync(useFakeUi: true);

        var response = await app.Client.GetAsync("/dbschema");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("testovaci-ui", html, StringComparison.Ordinal);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);

        // Základ cesty se přepsal podle prefixu, jinak by se prohlížečka nenačetla.
        Assert.Contains("<base href=\"/dbschema/\" />", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Index_je_dostupny_i_pod_vlastnim_jmenem()
    {
        await using var app = await DbsViewerApp.StartAsync(useFakeUi: true);

        var response = await app.Client.GetAsync("/dbschema/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("testovaci-ui", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Zaklad_cesty_odpovida_vlastnimu_prefixu()
    {
        await using var app = await DbsViewerApp.StartAsync(
            o => o.RoutePrefix = "/_db",
            useFakeUi: true);

        var html = await app.Client.GetStringAsync("/_db");

        Assert.Contains("<base href=\"/_db/\" />", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Soubor_UI_se_vrati_se_spravnym_typem()
    {
        await using var app = await DbsViewerApp.StartAsync(useFakeUi: true);

        var css = await app.Client.GetAsync("/dbschema/dbsviewer.css");
        var js = await app.Client.GetAsync("/dbschema/_framework/blazor.webassembly.js");

        Assert.Equal(HttpStatusCode.OK, css.StatusCode);
        Assert.Equal("text/css", css.Content.Headers.ContentType!.MediaType);

        Assert.Equal(HttpStatusCode.OK, js.StatusCode);
        Assert.Equal("text/javascript", js.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Neznamy_soubor_vraci_404()
    {
        await using var app = await DbsViewerApp.StartAsync(useFakeUi: true);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await app.Client.GetAsync("/dbschema/neexistuje.js")).StatusCode);
    }

    [Fact]
    public async Task Pokus_o_vystup_z_adresare_se_odmitne()
    {
        await using var app = await DbsViewerApp.StartAsync(useFakeUi: true);

        var response = await app.Client.GetAsync("/dbschema/..%2f..%2fsecrets.json");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Endpointy_API_maji_prednost_pred_soubory_UI()
    {
        await using var app = await DbsViewerApp.StartAsync();

        var response = await app.Client.GetAsync("/dbschema/api/meta");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("routePrefix", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
