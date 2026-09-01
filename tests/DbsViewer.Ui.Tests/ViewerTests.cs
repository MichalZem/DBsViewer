using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using DbsViewer.Analysis;
using DbsViewer.Ui.Components;
using DbsViewer.Ui.Model;
using Microsoft.Extensions.DependencyInjection;

namespace DbsViewer.Tests.Ui;

/// <summary>
/// Hlavní obrazovka. Server se nahrazuje obsluhou v paměti, takže testy pokrývají
/// i chybové cesty, které by proti skutečné aplikaci šlo vyvolat jen těžko.
/// </summary>
public class ViewerTests : TestContext
{
    private readonly FakeServer _server = new();

    private IRenderedComponent<Viewer> Render()
    {
        Services.AddSingleton(new DbsViewerClient(
            new HttpClient(_server) { BaseAddress = new Uri("http://test/dbschema/") }));

        JSInterop.Mode = JSRuntimeMode.Loose;

        return RenderComponent<Viewer>();
    }

    [Fact]
    public void Po_nacteni_se_ukaze_seznam_tabulek()
    {
        var component = Render();

        Assert.Contains("Testovací schéma", component.Markup, StringComparison.Ordinal);
        Assert.Contains("Customers", component.Markup, StringComparison.Ordinal);
        Assert.Contains("4 z 4 tabulek", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Behem_nacitani_se_ukaze_hlaska()
    {
        // Odpověď se pozdrží, aby šlo zachytit mezistav — jinak je render hotový
        // dřív, než se dá cokoli zkontrolovat.
        var brana = new TaskCompletionSource();
        _server.Wait = brana.Task;

        Services.AddSingleton(new DbsViewerClient(
            new HttpClient(_server) { BaseAddress = new Uri("http://test/dbschema/") }));
        JSInterop.Mode = JSRuntimeMode.Loose;

        var component = RenderComponent<Viewer>();

        Assert.Contains("Načítám schéma", component.Markup, StringComparison.Ordinal);

        brana.SetResult();
        component.WaitForAssertion(() =>
            Assert.Contains("Customers", component.Markup, StringComparison.Ordinal));

        await Task.CompletedTask;
    }

    [Fact]
    public void Nedostupny_server_ukaze_hlasku_misto_padu()
    {
        _server.Fail = HttpStatusCode.InternalServerError;

        var component = Render();

        Assert.Contains("chybou 500", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Vyber_tabulky_ukaze_detail()
    {
        var component = Render();

        component.FindAll(".seznam li button").ElementAt(1).Click();

        Assert.Contains("CustomerId", component.Markup, StringComparison.Ordinal);
        Assert.Single(component.FindAll(".seznam li button.vybrana"));
    }

    [Fact]
    public void Hledani_omezi_seznam()
    {
        var component = Render();

        component.Find(".seznam input").Input("order");

        Assert.Contains("1 z 4 tabulek", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Prepnuti_na_diagram_vykresli_uzly()
    {
        var component = Render();

        Zalozka(component, "Diagram");

        Assert.NotEmpty(component.FindAll(".uzel"));
        Assert.Contains("Focus na vybranou tabulku", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Focus_v_diagramu_jde_vypnout()
    {
        var component = Render();
        Zalozka(component, "Diagram");
        component.FindAll(".seznam li button").ElementAt(1).Click();

        var sFocusem = component.FindAll(".uzel").Count;

        component.Find(".diagram-nastroje input[type=checkbox]").Change(false);

        Assert.True(component.FindAll(".uzel").Count > sFocusem);
    }

    [Fact]
    public void Vzdalenost_focusu_jde_menit()
    {
        var component = Render();
        Zalozka(component, "Diagram");
        component.FindAll(".seznam li button").ElementAt(1).Click();

        component.Find("input[type=range]").Change("0");

        Assert.Contains("Vzdálenost 0", component.Markup, StringComparison.Ordinal);
        Assert.Single(component.FindAll(".uzel"));
    }

    [Fact]
    public void Neplatna_vzdalenost_se_ignoruje()
    {
        var component = Render();
        Zalozka(component, "Diagram");

        component.Find("input[type=range]").Change("nesmysl");

        Assert.Contains("Vzdálenost 1", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Rozbaleni_uzlu_v_diagramu_funguje()
    {
        var component = Render();
        Zalozka(component, "Diagram");

        component.FindAll(".uzel-prepinac").ElementAt(0).Click();

        Assert.Contains("−", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Rozdily_se_nactou_az_pri_prepnuti()
    {
        var component = Render();

        Assert.Equal(0, _server.DiffCalls);

        Zalozka(component, "Rozdíly");

        Assert.Equal(1, _server.DiffCalls);
        Assert.Contains("1 chyb", component.Markup, StringComparison.Ordinal);

        // Podruhé se už nenačítají.
        Zalozka(component, "Tabulky");
        Zalozka(component, "Rozdíly");
        Assert.Equal(1, _server.DiffCalls);
    }

    [Fact]
    public void Klik_v_rozdilech_prepne_na_detail()
    {
        var component = Render();
        Zalozka(component, "Rozdíly");

        component.FindAll("button.odkaz").ElementAt(0).Click();

        Assert.NotEmpty(component.FindAll(".seznam"));
        Assert.Single(component.FindAll(".seznam li button.vybrana"));
    }

    [Fact]
    public void Nalez_diffu_zvyrazni_tabulku_v_seznamu()
    {
        var component = Render();
        Zalozka(component, "Rozdíly");
        Zalozka(component, "Tabulky");

        Assert.Single(component.FindAll(".seznam li button.nalez-chyba"));
        Assert.Single(component.FindAll(".seznam li button.nalez-varovani"));
    }

    [Fact]
    public void Chyba_pri_nacitani_rozdilu_se_ukaze()
    {
        var component = Render();
        _server.FailDiff = true;

        Zalozka(component, "Rozdíly");

        Assert.Contains("Přístup odepřen", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Bez_podpory_rozdilu_se_zalozka_nezobrazi()
    {
        _server.Meta = Vzorek.Meta(canDiff: false) with { Views = ["ef"] };

        var component = Render();

        Assert.Equal(3, component.FindAll(".pohledy button").Count);
    }

    [Fact]
    public void Zmena_zdroje_nacte_schema_znovu()
    {
        var component = Render();
        var pred = _server.SchemaCalls;

        component.Find(".nastroje select").Change("ef");

        Assert.True(_server.SchemaCalls > pred);
        Assert.Contains("source=ef", _server.LastSchemaUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void Nepodporovany_zdroj_se_nahradi_dostupnym()
    {
        _server.Meta = Vzorek.Meta() with { Views = ["ef"] };

        var component = Render();

        Assert.Contains("source=ef", _server.LastSchemaUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void Skupina_omezi_seznam()
    {
        var component = Render();

        component.FindAll(".nastroje select").ElementAt(1).Change("Prodej");

        Assert.Contains("1 z 4 tabulek", component.Markup, StringComparison.Ordinal);

        component.FindAll(".nastroje select").ElementAt(1).Change("");

        Assert.Contains("4 z 4 tabulek", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Obnoveni_zahodi_cache_a_nacte_znovu()
    {
        var component = Render();
        var pred = _server.SchemaCalls;

        component.FindAll(".nastroje button").Last().Click();

        Assert.Equal(1, _server.RefreshCalls);
        Assert.True(_server.SchemaCalls > pred);
    }

    [Fact]
    public void Chyba_pri_obnoveni_se_ukaze()
    {
        var component = Render();
        _server.FailRefresh = true;

        component.FindAll(".nastroje button").Last().Click();

        Assert.Contains("Přístup odepřen", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Nahled_dat_se_nacte_az_na_vyzadani()
    {
        _server.Meta = Vzorek.Meta(canPreview: true);

        var component = Render();
        component.FindAll(".seznam li button").ElementAt(1).Click();
        component.FindAll(".zalozky button").ElementAt(4).Click();

        Assert.Equal(0, _server.RowCalls);

        component.Find("button.hlavni").Click();

        Assert.Equal(1, _server.RowCalls);
        Assert.Contains("a@b.cz", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Odepreny_nahled_dat_ukaze_hlasku()
    {
        _server.Meta = Vzorek.Meta(canPreview: true);
        _server.FailRows = true;

        var component = Render();
        component.FindAll(".seznam li button").ElementAt(1).Click();
        component.FindAll(".zalozky button").ElementAt(4).Click();
        component.Find("button.hlavni").Click();

        Assert.Contains("Přístup odepřen", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Vyber_jine_tabulky_zahodi_nactena_data()
    {
        _server.Meta = Vzorek.Meta(canPreview: true);

        var component = Render();
        component.FindAll(".seznam li button").ElementAt(1).Click();
        component.FindAll(".zalozky button").ElementAt(4).Click();
        component.Find("button.hlavni").Click();

        component.FindAll(".seznam li button").ElementAt(0).Click();
        component.FindAll(".zalozky button").ElementAt(4).Click();

        Assert.Contains("Načíst data", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Export_nabidne_formaty_a_stahne_soubor()
    {
        var component = Render();

        component.Find(".export button").Click();
        Assert.Equal(3, component.FindAll(".export-nabidka button").Count);

        component.FindAll(".export-nabidka button").ElementAt(0).Click();

        var volani = JSInterop.Invocations["dbsviewer.download"];
        Assert.Single(volani);
        Assert.Equal("schema.mmd", volani[0].Arguments[0]);
        Assert.Contains("erDiagram", (string)volani[0].Arguments[1]!, StringComparison.Ordinal);
    }

    [Fact]
    public void Nabidka_exportu_se_da_zase_zavrit()
    {
        var component = Render();

        component.Find(".export button").Click();
        component.Find(".export button").Click();

        Assert.Empty(component.FindAll(".export-nabidka"));
    }

    [Fact]
    public async Task Selhani_stahovani_ukaze_hlasku()
    {
        Services.AddSingleton(new DbsViewerClient(
            new HttpClient(_server) { BaseAddress = new Uri("http://test/dbschema/") }));

        // InvokeVoidAsync se v bUnit nastavuje přes SetupVoid, ne přes Setup<T>.
        JSInterop.Mode = JSRuntimeMode.Strict;
        JSInterop.SetupVoid("dbsviewer.download", _ => true)
            .SetException(new InvalidOperationException("stahování selhalo"));

        var component = RenderComponent<Viewer>();

        await component.InvokeAsync(() => component.Instance.ExportAsync(ExportFormat.Markdown));
        component.Render();

        Assert.Contains("stahování selhalo", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nacteni_dat_bez_vybrane_tabulky_nic_nedela()
    {
        var component = Render();

        await component.InvokeAsync(() => component.Instance.LoadRowsAsync());

        Assert.Equal(0, _server.RowCalls);
    }

    [Fact]
    public void Filtr_schematu_se_ukaze_jen_pri_vice_schematech()
    {
        var component = Render();

        // Vzorek má tabulky bez schématu, takže se přepínač nezobrazuje.
        Assert.Equal(2, component.FindAll(".nastroje select").Count);
    }

    [Fact]
    public void Pri_vice_schematech_jde_filtrovat()
    {
        _server.SchemaOverride = new DatabaseSchema
        {
            Tables =
            [
                new DbTable { Name = new DbObjectName("dbo", "A") },
                new DbTable { Name = new DbObjectName("sales", "B") },
                new DbTable { Name = new DbObjectName("audit", "C") },
            ],
        };

        var component = Render();

        Assert.Equal(3, component.FindAll(".nastroje select").Count);

        component.FindAll(".nastroje select").ElementAt(2).Change("sales");
        Assert.Contains("1 z 3 tabulek", component.Markup, StringComparison.Ordinal);

        component.FindAll(".nastroje select").ElementAt(2).Change("");
        Assert.Contains("3 z 3 tabulek", component.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ef", "EF model")]
    [InlineData("live", "Databáze")]
    [InlineData("merged", "Sloučeno")]
    public void Popisky_zdroju(string view, string expected) =>
        Assert.Equal(expected, Viewer.SourceLabel(view));

    [Fact]
    public void Prazdna_volba_znamena_bez_filtru()
    {
        Assert.Null(Viewer.Empty(""));
        Assert.Null(Viewer.Empty(null));
        Assert.Equal("x", Viewer.Empty("x"));
    }

    [Fact]
    public void Vyjimky_se_prekladaji_na_hlasky()
    {
        Assert.Equal("vlastní", Viewer.Describe(new DbsViewerClientException("vlastní")));
        Assert.Contains("neodpovídá", Viewer.Describe(new HttpRequestException()), StringComparison.Ordinal);
        Assert.Contains("nepodařilo", Viewer.Describe(new InvalidOperationException()), StringComparison.Ordinal);
    }

    /// <summary>Server v paměti. Umí odpovídat i selhávat, aby šly otestovat obě cesty.</summary>
    private sealed class FakeServer : HttpMessageHandler
    {
        public ViewerMeta Meta { get; set; } = Vzorek.Meta();

        /// <summary>Vlastní schéma místo vzorku, pro testy filtrů.</summary>
        public DatabaseSchema? SchemaOverride { get; set; }

        public HttpStatusCode? Fail { get; set; }

        public bool FailDiff { get; set; }

        public bool FailRows { get; set; }

        public bool FailRefresh { get; set; }

        public int SchemaCalls { get; private set; }

        public int DiffCalls { get; private set; }

        public int RowCalls { get; private set; }

        public int RefreshCalls { get; private set; }

        public string LastSchemaUrl { get; private set; } = "";

        /// <summary>Pozdrží odpověď, aby šel otestovat mezistav načítání.</summary>
        public Task? Wait { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (Wait is { } wait)
            {
                await wait;
            }

            return await Respond(request);
        }

        private Task<HttpResponseMessage> Respond(HttpRequestMessage request)
        {
            var url = request.RequestUri?.ToString() ?? "";

            if (Fail is { } status)
            {
                return Task.FromResult(new HttpResponseMessage(status));
            }

            if (url.Contains("api/meta", StringComparison.Ordinal))
            {
                return Json(Meta);
            }

            if (url.Contains("api/schema/diff", StringComparison.Ordinal))
            {
                DiffCalls++;
                return FailDiff ? Deny() : Json(Vzorek.Diff());
            }

            if (url.Contains("api/schema", StringComparison.Ordinal))
            {
                SchemaCalls++;
                LastSchemaUrl = url;
                return Json(SchemaOverride ?? Vzorek.Schema());
            }

            if (url.Contains("/rows", StringComparison.Ordinal))
            {
                RowCalls++;

                return FailRows
                    ? Deny()
                    : Json(new RowPreview
                    {
                        Columns = ["Id", "Email"],
                        Rows = [["1", "a@b.cz"]],
                        Limit = 100,
                    });
            }

            if (url.Contains("api/refresh", StringComparison.Ordinal))
            {
                RefreshCalls++;
                return FailRefresh
                    ? Deny()
                    : Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static Task<HttpResponseMessage> Deny() =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));

        private static Task<HttpResponseMessage> Json<T>(T value) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(value, DbsViewerJson.Compact),
                    Encoding.UTF8,
                    "application/json"),
            });
    }

    /// <summary>
    /// Klikne na záložku podle jejího názvu. Dřív se hledala podle pořadí, jenže to
    /// spadlo, jakmile mezi záložky přibyl Přehled.
    /// </summary>
    private static void Zalozka(IRenderedComponent<Viewer> component, string popisek) =>
        component
            .FindAll(".pohledy button")
            .First(b => b.TextContent.Contains(popisek, StringComparison.Ordinal))
            .Click();
}
