using System.Net;
using System.Text;
using System.Text.Json;
using DbsViewer.TestKit;
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
    public void Prepnuti_na_prehled_ukaze_souhrn_databaze()
    {
        var component = Render();

        Zalozka(component, "Přehled");

        Assert.NotEmpty(component.FindAll(".prehled-cisla .karta"));
        Assert.Contains("Co stojí za pozornost", component.Markup, StringComparison.Ordinal);
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
    public void Nahled_dat_se_nacte_hned_po_otevreni()
    {
        // Dřív tu bylo tlačítko „Načíst data". Data si mřížka vyžádá sama, jakmile
        // se záložka otevře.
        _server.Meta = Vzorek.Meta(canPreview: true);

        var component = Render();
        component.FindAll(".seznam li button").ElementAt(1).Click();
        component.FindAll(".zalozky button").ElementAt(4).Click();

        Assert.Equal(1, _server.RowCalls);
        Assert.Contains("a@b.cz", component.Markup, StringComparison.Ordinal);
        Assert.Empty(component.FindAll("button.hlavni"));
    }

    [Fact]
    public void Odepreny_nahled_dat_ukaze_hlasku()
    {
        _server.Meta = Vzorek.Meta(canPreview: true);
        _server.FailRows = true;

        var component = Render();
        component.FindAll(".seznam li button").ElementAt(1).Click();
        component.FindAll(".zalozky button").ElementAt(4).Click();

        Assert.Contains("Přístup odepřen", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Vyber_jine_tabulky_nacte_jeji_data()
    {
        _server.Meta = Vzorek.Meta(canPreview: true);

        var component = Render();
        component.FindAll(".seznam li button").ElementAt(1).Click();
        component.FindAll(".zalozky button").ElementAt(4).Click();

        var poPrvni = _server.RowCalls;

        component.FindAll(".seznam li button").ElementAt(0).Click();
        component.FindAll(".zalozky button").ElementAt(4).Click();

        // Data se načtou znovu, protože jde o jinou tabulku.
        Assert.True(_server.RowCalls > poPrvni);
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
    public async Task Zapis_bez_vybrane_tabulky_se_neposila()
    {
        var component = Render();
        string? chyba = null;

        await component.InvokeAsync(async () =>
            chyba = await component.Instance.UpdateRowAsync(new DataUpdate()));

        Assert.Equal("Není vybraná žádná tabulka.", chyba);
        Assert.Equal(0, _server.WriteCalls);
    }

    [Fact]
    public async Task Uprava_radku_dojde_na_server()
    {
        var component = Render();
        component.FindAll(".seznam li button").ElementAt(0).Click();

        string? chyba = null;

        await component.InvokeAsync(async () => chyba = await component.Instance.UpdateRowAsync(
            new DataUpdate
            {
                Key = [new DataValue("Id", "1")],
                Values = [new DataValue("Email", "novy@x.cz")],
            }));

        Assert.Null(chyba);
        Assert.Equal(1, _server.WriteCalls);
    }

    [Fact]
    public async Task Smazani_radku_dojde_na_server()
    {
        var component = Render();
        component.FindAll(".seznam li button").ElementAt(0).Click();

        string? chyba = null;

        await component.InvokeAsync(async () => chyba = await component.Instance.DeleteRowAsync(
            new DataDelete { Key = [new DataValue("Id", "1")] }));

        Assert.Null(chyba);
        Assert.Equal(1, _server.WriteCalls);
    }

    [Fact]
    public async Task Odmitnuty_zapis_se_vrati_jako_hlaska_ne_jako_vyjimka()
    {
        // Mřížka podle hlášky pozná, že má nechat rozepsané hodnoty na místě.
        _server.FailWrite = true;

        var component = Render();
        component.FindAll(".seznam li button").ElementAt(0).Click();

        string? chyba = null;

        await component.InvokeAsync(async () => chyba = await component.Instance.DeleteRowAsync(
            new DataDelete { Key = [new DataValue("Id", "1")] }));

        Assert.Equal(DbsViewerClient.DescribeFailure(HttpStatusCode.Forbidden), chyba);
    }

    [Fact]
    public async Task Vlozeni_radku_dojde_na_server()
    {
        var component = Render();
        component.FindAll(".seznam li button").ElementAt(0).Click();

        string? chyba = null;

        await component.InvokeAsync(async () => chyba = await component.Instance.InsertRowAsync(
            new DataInsert { Values = [new DataValue("Email", "novy@x.cz")] }));

        Assert.Null(chyba);
        Assert.Equal(1, _server.WriteCalls);
    }

    [Fact]
    public async Task Odkaz_z_diagramu_otevre_rovnou_data_tabulky()
    {
        // Z diagramu je nejčastější další otázka „a co v té tabulce je" — bez tohohle
        // by se uživatel musel vracet do seznamu a přepínat záložku ručně.
        var component = Render();

        await component.InvokeAsync(() =>
            component.Instance.ShowDataAsync(new DbObjectName(null, "Customers")));

        Assert.Equal(new DbObjectName(null, "Customers"), component.Instance.State.SelectedTable);
        Assert.Equal(ViewerPane.Browser, component.Instance.State.Pane);
        Assert.Equal(DetailTab.Data, component.Instance.State.Tab);
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

        public int WriteCalls { get; private set; }

        public bool FailWrite { get; set; }

        public int RefreshCalls { get; private set; }

        public string LastSchemaUrl { get; private set; } = "";

        /// <summary>Schéma vracené pro historickou verzi, když má být jiné než výchozí.</summary>
        public DatabaseSchema? BaselineOverride { get; set; }

        public int MigrationDiffCalls { get; private set; }

        public string LastMigrationDiffUrl { get; private set; } = "";

        /// <summary>Migrace, které server vrátí. Prázdné znamená aplikaci bez migrací.</summary>
        public IReadOnlyList<DbMigration> Migrations { get; set; } =
        [
            new DbMigration
            {
                Id = "20260101_Zaklad",
                AppliedInDatabase = true,
                PresentInAssembly = true,
                HasSnapshot = true,
            },
            new DbMigration
            {
                Id = "20260202_Sloupec",
                AppliedInDatabase = true,
                PresentInAssembly = true,
                HasSnapshot = true,
            },
        ];

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

            if (url.Contains("api/migrations/diff", StringComparison.Ordinal))
            {
                MigrationDiffCalls++;
                LastMigrationDiffUrl = url;
                return Json(Vzorek.Diff());
            }

            if (url.Contains("api/migrations", StringComparison.Ordinal))
            {
                return Json(Migrations);
            }

            if (url.Contains("api/schema", StringComparison.Ordinal))
            {
                SchemaCalls++;
                LastSchemaUrl = url;

                // Schéma k migraci má míň tabulek — poznají se tak historické verze.
                return url.Contains("migration=", StringComparison.Ordinal)
                    ? Json(BaselineOverride ?? Vzorek.Schema() with
                    {
                        Tables = [Vzorek.Schema().Tables[0]],
                        SourceKind = SchemaSourceKind.MigrationSnapshot,
                    })
                    : Json(SchemaOverride ?? Vzorek.Schema());
            }

            if (url.Contains("/rows/update", StringComparison.Ordinal)
                || url.Contains("/rows/delete", StringComparison.Ordinal)
                || url.Contains("/rows/insert", StringComparison.Ordinal))
            {
                WriteCalls++;

                return FailWrite ? Deny() : Json(new RowChange { Affected = 1 });
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
                        PageSize = 50,
                        TotalRows = 1,
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

    [Fact]
    public void Ve_vyrezu_je_cesta_zpet_na_cele_schema()
    {
        // Ve výřezu není z diagramu poznat, že zbytek schématu existuje — cesta zpátky
        // proto musí být vidět, ne schovaná v zaškrtávátku.
        var component = Render();
        Zalozka(component, "Diagram");

        Assert.Empty(component.FindAll(".zpet-na-schema"));

        component.FindAll(".seznam li button").ElementAt(1).Click();

        Assert.Single(component.FindAll(".zpet-na-schema"));
        Assert.Contains("Výřez kolem", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Klik_na_cele_schema_zobrazi_vsechny_tabulky()
    {
        var component = Render();
        Zalozka(component, "Diagram");
        component.FindAll(".seznam li button").ElementAt(1).Click();

        var veVyrezu = component.FindAll(".uzel").Count;

        component.Find(".zpet-na-schema").Click();

        Assert.True(component.FindAll(".uzel").Count > veVyrezu);
        Assert.Empty(component.FindAll(".zpet-na-schema"));

        // Výběr zůstane, takže je vidět, odkud se člověk vrátil.
        Assert.Single(component.FindAll(".uzel.vybrany"));
    }

    // ---------- historie schématu ----------

    [Fact]
    public void Bez_migraci_se_zalozka_historie_nenabizi()
    {
        _server.Meta = Vzorek.Meta() with { CanBrowseHistory = false };

        Assert.DoesNotContain("Historie", Render().Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void S_migracemi_zalozka_historie_pribude()
    {
        _server.Meta = Vzorek.Meta() with { CanBrowseHistory = true };

        Assert.Contains("Historie", Render().Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Otevreni_historie_nacte_migrace()
    {
        _server.Meta = Vzorek.Meta() with { CanBrowseHistory = true };

        var component = Render();
        Zalozka(component, "Historie");

        Assert.Contains("Zaklad", component.Markup, StringComparison.Ordinal);
        Assert.Contains("Sloupec", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Prepnuti_na_migraci_nacte_historicke_schema()
    {
        _server.Meta = Vzorek.Meta() with { CanBrowseHistory = true };

        var component = Render();
        Zalozka(component, "Historie");
        component.FindAll(".prepnout").ElementAt(0).Click();

        // Schéma se načte s parametrem migrace, ne podle zdroje.
        Assert.Contains("migration=", _server.LastSchemaUrl, StringComparison.Ordinal);
        Assert.Contains("Historická verze", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void V_historicke_verzi_se_data_prohlizet_nedaji()
    {
        // Snapshot popisuje strukturu v minulosti; řádky existují jen tady a teď.
        _server.Meta = Vzorek.Meta(canPreview: true) with { CanBrowseHistory = true };

        var component = Render();
        Zalozka(component, "Historie");
        component.FindAll(".prepnout").ElementAt(0).Click();

        Zalozka(component, "Tabulky");
        component.FindAll(".seznam li button").ElementAt(0).Click();

        var dataZalozka = component.FindAll(".zalozky button")
            .First(b => b.TextContent.Contains("Data", StringComparison.Ordinal));

        Assert.True(dataZalozka.HasAttribute("disabled"));
    }

    [Fact]
    public void Navrat_na_aktualni_schema_historii_opusti()
    {
        _server.Meta = Vzorek.Meta() with { CanBrowseHistory = true };

        var component = Render();
        Zalozka(component, "Historie");
        component.FindAll(".prepnout").ElementAt(0).Click();

        component.Find(".zpet-na-aktualni").Click();

        Assert.DoesNotContain("Historická verze", component.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("migration=", _server.LastSchemaUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void Zmena_porovnavanych_verzi_se_promitne_do_dotazu()
    {
        _server.Meta = Vzorek.Meta() with { CanBrowseHistory = true };

        var component = Render();
        Zalozka(component, "Historie");

        // Přepnutí na porovnání od začátku historie.
        component.FindAll(".porovnani select").ElementAt(0).Change("");
        component.Find(".porovnani button.hlavni").Click();

        Assert.DoesNotContain("from=", _server.LastMigrationDiffUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void Porovnani_verzi_zavola_server()
    {
        _server.Meta = Vzorek.Meta() with { CanBrowseHistory = true };

        var component = Render();
        Zalozka(component, "Historie");
        component.Find(".porovnani button.hlavni").Click();

        Assert.Equal(1, _server.MigrationDiffCalls);
        Assert.Contains("to=", _server.LastMigrationDiffUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void Selhani_porovnani_verzi_skonci_hlaskou()
    {
        _server.Meta = Vzorek.Meta() with { CanBrowseHistory = true };

        var component = Render();
        Zalozka(component, "Historie");

        _server.Fail = HttpStatusCode.InternalServerError;
        component.Find(".porovnani button.hlavni").Click();

        Assert.Contains("chyba", component.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Prepinac_verze_je_videt_i_bez_vybrane_migrace()
    {
        // Bez stálého pruhu by nebylo poznat, že se schéma dá prohlížet i zpětně.
        _server.Meta = Vzorek.Meta() with { CanBrowseHistory = true };

        var component = Render();

        Assert.Single(component.FindAll(".verze-pruh"));
        Assert.Empty(component.FindAll(".verze-pruh.historicka"));
        Assert.Contains("Aktuální schéma", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Bez_historie_prepinac_verze_neni()
    {
        _server.Meta = Vzorek.Meta() with { CanBrowseHistory = false };

        Assert.Empty(Render().FindAll(".verze-pruh"));
    }

    [Fact]
    public void Verze_jsou_serazene_od_nejnovejsi()
    {
        _server.Meta = Vzorek.Meta() with { CanBrowseHistory = true };

        // První select je výběr zobrazené verze, druhý je základ porovnání.
        var volby = Render().FindAll(".verze-pruh select").ElementAt(0)
            .QuerySelectorAll("option").Select(o => o.TextContent.Trim()).ToList();

        // Popisky nesou i to, které migraci aktuální schéma odpovídá.
        Assert.Equal(3, volby.Count);
        Assert.StartsWith("Aktuální schéma", volby[0], StringComparison.Ordinal);
        Assert.Contains("Sloupec", volby[0], StringComparison.Ordinal);
        Assert.Contains("Sloupec", volby[1], StringComparison.Ordinal);
        Assert.Equal("Zaklad", volby[2]);
    }

    [Fact]
    public void Vyber_verze_v_pruhu_prepne_schema()
    {
        _server.Meta = Vzorek.Meta() with { CanBrowseHistory = true };

        var component = Render();
        component.Find(".verze-pruh select").Change("20260101_Zaklad");

        Assert.Contains("migration=20260101_Zaklad", _server.LastSchemaUrl, StringComparison.Ordinal);
        Assert.Single(component.FindAll(".verze-pruh.historicka"));
    }

    [Fact]
    public void Vyber_aktualniho_schematu_historii_opusti()
    {
        _server.Meta = Vzorek.Meta() with { CanBrowseHistory = true };

        var component = Render();
        component.Find(".verze-pruh select").Change("20260101_Zaklad");
        component.Find(".verze-pruh select").Change("");

        Assert.Empty(component.FindAll(".verze-pruh.historicka"));
        Assert.DoesNotContain("migration=", _server.LastSchemaUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void Verze_bez_snapshotu_se_vybrat_neda()
    {
        // Migrace, jejíž kód už v projektu není, schéma nemá.
        _server.Meta = Vzorek.Meta() with { CanBrowseHistory = true };
        _server.Migrations =
        [
            new DbMigration
            {
                Id = "20251111_Zmizela",
                AppliedInDatabase = true,
                PresentInAssembly = false,
                HasSnapshot = false,
            },
        ];

        var component = Render();
        var volba = component.FindAll(".verze-pruh option").ElementAt(1);

        Assert.True(volba.HasAttribute("disabled"));
        Assert.Contains("bez schématu", volba.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Prepnuti_verze_zachova_vybranou_tabulku()
    {
        // O tohle tu jde: vyberu tabulku, přepnu verzi a vidím tutéž tabulku znovu,
        // takže poznám, které sloupce přibyly nebo zmizely.
        _server.Meta = Vzorek.Meta() with { CanBrowseHistory = true };

        var component = Render();
        component.FindAll(".seznam li button").ElementAt(0).Click();

        var pred = component.Find(".detail-hlavicka h2").TextContent;

        component.Find(".verze-pruh select").Change("20260101_Zaklad");

        Assert.Single(component.FindAll(".detail-hlavicka"));
        Assert.Equal(pred, component.Find(".detail-hlavicka h2").TextContent);
    }

    [Fact]
    public void Prepnuti_verze_zachova_rozbaleny_uzel_v_diagramu()
    {
        _server.Meta = Vzorek.Meta() with { CanBrowseHistory = true };

        var component = Render();
        Zalozka(component, "Diagram");

        // Rozbalí se Customers — jediná tabulka, kterou má i starší verze.
        var uzly = component.FindAll(".uzel");
        var index = uzly.Select((u, i) => (u, i))
            .First(x => x.u.TextContent.Contains("Customers", StringComparison.Ordinal)).i;

        component.FindAll(".uzel-prepinac").ElementAt(index).Click();

        Assert.Contains("−", component.FindAll(".uzel-prepinac").ElementAt(index).TextContent, StringComparison.Ordinal);

        component.Find(".verze-pruh select").Change("20260101_Zaklad");

        // Uzel zůstal rozbalený, takže jdou porovnat sloupce mezi verzemi.
        Assert.Single(component.FindAll(".uzel-prepinac"));
        Assert.Contains("−", component.Find(".uzel-prepinac").TextContent, StringComparison.Ordinal);
    }

    // ---------- vizuální porovnání verzí ----------

    [Fact]
    public void Bez_porovnani_se_nic_neobarvi()
    {
        _server.Meta = Vzorek.Meta() with { CanBrowseHistory = true };

        var component = Render();
        component.FindAll(".seznam li button").ElementAt(0).Click();

        Assert.Empty(component.FindAll(".zmena-pribylo"));
        Assert.Empty(component.FindAll(".zmena-ubylo"));
        Assert.Empty(component.FindAll(".legenda-zmen"));
    }

    [Fact]
    public void Porovnani_obarvi_pribyle_i_zmizele_sloupce()
    {
        // Historická verze má jen Customers; porovnáním vůči ní se ukáže,
        // co v aktuálním schématu přibylo.
        _server.Meta = Vzorek.Meta() with { CanBrowseHistory = true };

        var component = Render();
        component.FindAll(".verze-pruh select").ElementAt(1).Change("20260101_Zaklad");

        Assert.Single(component.FindAll(".legenda-zmen"));

        // Tabulky, které v základu nebyly, jsou označené jako přibylé.
        Assert.NotEmpty(component.FindAll(".seznam li button.zmena-pribylo"));
    }

    [Fact]
    public void Zmizely_sloupec_zustane_videt_jako_duch()
    {
        _server.Meta = Vzorek.Meta() with { CanBrowseHistory = true };

        // Základ má sloupec navíc, který v aktuálním schématu není.
        _server.BaselineOverride = Vzorek.Schema() with
        {
            Tables =
            [
                Build.Table("Customers", ["Id", "Email", "Zruseny"], ["Id"]),
            ],
        };

        var component = Render();
        component.FindAll(".verze-pruh select").ElementAt(1).Change("20260101_Zaklad");
        component.FindAll(".seznam li button").First(b => b.TextContent.Contains("Customers", StringComparison.Ordinal)).Click();

        Assert.Contains("Zruseny", component.Markup, StringComparison.Ordinal);
        Assert.NotEmpty(component.FindAll("tr.zmena-ubylo"));
    }

    [Fact]
    public void Vypnuti_porovnani_obarveni_zrusi()
    {
        _server.Meta = Vzorek.Meta() with { CanBrowseHistory = true };

        var component = Render();
        component.FindAll(".verze-pruh select").ElementAt(1).Change("20260101_Zaklad");

        Assert.Single(component.FindAll(".legenda-zmen"));

        component.FindAll(".verze-pruh select").ElementAt(1).Change("");

        Assert.Empty(component.FindAll(".legenda-zmen"));
        Assert.Empty(component.FindAll(".zmena-pribylo"));
    }

    [Fact]
    public void Porovnani_obarvi_i_diagram()
    {
        _server.Meta = Vzorek.Meta() with { CanBrowseHistory = true };

        var component = Render();
        component.FindAll(".verze-pruh select").ElementAt(1).Change("20260101_Zaklad");
        Zalozka(component, "Diagram");

        Assert.NotEmpty(component.FindAll(".uzel.zmena-pribylo"));
    }

    [Fact]
    public void Selhani_nacteni_zakladu_skonci_hlaskou()
    {
        _server.Meta = Vzorek.Meta() with { CanBrowseHistory = true };

        var component = Render();
        _server.Fail = HttpStatusCode.InternalServerError;

        component.FindAll(".verze-pruh select").ElementAt(1).Change("20260101_Zaklad");

        Assert.Contains("chyba", component.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(component.FindAll(".legenda-zmen"));
    }

    [Fact]
    public void Pruh_ukaze_smer_porovnani()
    {
        // Z jednoho dropdownu nešlo poznat, která verze je výchozí a která cílová.
        _server.Meta = Vzorek.Meta() with { CanBrowseHistory = true };

        var component = Render();
        component.FindAll(".verze-pruh select").ElementAt(1).Change("20260101_Zaklad");

        var smer = component.Find(".smer-porovnani").TextContent;

        Assert.Contains("Zaklad", smer, StringComparison.Ordinal);
        Assert.Contains("→", smer, StringComparison.Ordinal);
        // Cíl porovnání nese i jméno migrace, které aktuální schéma odpovídá.
        Assert.Contains("aktuální", smer, StringComparison.Ordinal);
        Assert.Contains("Sloupec", smer, StringComparison.Ordinal);
    }

    [Fact]
    public void Smer_porovnani_nese_i_zobrazenou_historickou_verzi()
    {
        _server.Meta = Vzorek.Meta() with { CanBrowseHistory = true };

        var component = Render();
        component.FindAll(".verze-pruh select").ElementAt(0).Change("20260202_Sloupec");
        component.FindAll(".verze-pruh select").ElementAt(1).Change("20260101_Zaklad");

        var smer = component.Find(".smer-porovnani").TextContent;

        Assert.Contains("Zaklad", smer, StringComparison.Ordinal);
        Assert.Contains("Sloupec", smer, StringComparison.Ordinal);
    }

    [Fact]
    public void Popisek_vyberu_rika_smer()
    {
        _server.Meta = Vzorek.Meta() with { CanBrowseHistory = true };

        // „Porovnat vůči" nechávalo směr na domyšlení; „Co se změnilo od" ne.
        Assert.Contains("Co se změnilo od", Render().Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Aktualni_schema_nese_jmeno_odpovidajici_migrace()
    {
        // Samotné „Aktuální schéma" nestačilo: stálo v seznamu vedle jmen migrací
        // a nebylo poznat, které z nich odpovídá.
        _server.Meta = Vzorek.Meta() with { CanBrowseHistory = true };

        var volba = Render().FindAll(".verze-pruh select").ElementAt(0)
            .QuerySelectorAll("option").ElementAt(0).TextContent.Trim();

        Assert.Contains("Aktuální schéma", volba, StringComparison.Ordinal);
        Assert.Contains("Sloupec", volba, StringComparison.Ordinal);
    }

    [Fact]
    public void Cekajici_migrace_aktualnimu_schematu_neodpovida()
    {
        // Migrace v kódu, ale ne v databázi — aktuální schéma se řídí tou poslední
        // opravdu aplikovanou.
        _server.Meta = Vzorek.Meta() with { CanBrowseHistory = true };
        _server.Migrations =
        [
            new DbMigration
            {
                Id = "20260101_Nasazena",
                AppliedInDatabase = true,
                PresentInAssembly = true,
                HasSnapshot = true,
            },
            new DbMigration
            {
                Id = "20260202_Ceka",
                AppliedInDatabase = false,
                PresentInAssembly = true,
                HasSnapshot = true,
            },
        ];

        var volby = Render().FindAll(".verze-pruh select").ElementAt(0)
            .QuerySelectorAll("option").Select(o => o.TextContent.Trim()).ToList();

        Assert.Contains("Nasazena", volby[0], StringComparison.Ordinal);
        Assert.Contains("čeká na nasazení", volby[1], StringComparison.Ordinal);
        Assert.Contains("= aktuální schéma", volby[2], StringComparison.Ordinal);
    }

    [Fact]
    public void Bez_aplikovanych_migraci_se_jmeno_nedoplnuje()
    {
        _server.Meta = Vzorek.Meta() with { CanBrowseHistory = true };
        _server.Migrations =
        [
            new DbMigration
            {
                Id = "20260202_Ceka",
                AppliedInDatabase = false,
                PresentInAssembly = true,
                HasSnapshot = true,
            },
        ];

        var volba = Render().FindAll(".verze-pruh select").ElementAt(0)
            .QuerySelectorAll("option").ElementAt(0).TextContent.Trim();

        Assert.Equal("Aktuální schéma", volba);
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
