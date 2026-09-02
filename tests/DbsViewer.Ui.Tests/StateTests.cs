using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DbsViewer.Analysis;
using DbsViewer.TestKit;
using DbsViewer.Ui.Components;
using DbsViewer.Ui.Model;

namespace DbsViewer.Tests.Ui;

/// <summary>Testovací data sdílená testy stavu i komponent.</summary>
internal static class Vzorek
{
    public static DbObjectName N(string name) => new(null, name);

    public static DatabaseSchema Schema() => new()
    {
        DatabaseName = "Shop",
        SourceKind = SchemaSourceKind.Merged,
        Tables =
        [
            Build.Table("Customers", ["Id", "Email"], ["Id"]),
            Build.Table("Orders", ["Id", "CustomerId"], ["Id"],
                [Build.ForeignKey("FK_Orders", ["CustomerId"], "Customers")]),
            Build.Table("Products", ["Id", "Nazev"], ["Id"]),
            Build.Table("AuditLog", ["Zprava"]),
        ],
        Relationships =
        [
            new DbRelationship
            {
                Id = "fk:Orders|FK_Orders",
                From = N("Orders"),
                To = N("Customers"),
                Cardinality = DbCardinality.OneToMany,
                FromColumns = ["CustomerId"],
                ToColumns = ["Id"],
                IsRequired = true,
            },
        ],
    };

    public static ViewerMeta Meta(bool canDiff = true, bool canPreview = false) => new()
    {
        Title = "Testovací schéma",
        Views = ["ef", "live", "merged"],
        CanDiff = canDiff,
        CanPreviewData = canPreview,
        Groups = new Dictionary<string, string>(StringComparer.Ordinal) { ["Prodej"] = "Order*" },
    };

    public static SchemaDiff Diff() => new()
    {
        Findings =
        [
            new DiffFinding
            {
                Kind = DiffKind.ColumnMissingInDatabase,
                Severity = DiffSeverity.Error,
                Table = N("Orders"),
                Object = "Poznamka",
                Message = "Sloupec je v modelu, ale v databázi chybí.",
                ModelValue = "nvarchar(200)",
            },
            new DiffFinding
            {
                Kind = DiffKind.IndexMissingInModel,
                Severity = DiffSeverity.Warning,
                Table = N("Customers"),
                Object = "IX_Rucni",
                Message = "Index je v databázi, ale v modelu není.",
            },
        ],
    };
}

public class ViewerStateTests
{
    private static ViewerState State()
    {
        var state = new ViewerState { Meta = Vzorek.Meta() };
        state.Schema = Vzorek.Schema();
        return state;
    }

    [Fact]
    public void Vychozi_stav_je_prazdny_ale_pouzitelny()
    {
        var state = new ViewerState();

        Assert.Empty(state.Schema.Tables);
        Assert.Equal(ViewerPane.Browser, state.Pane);
        Assert.Equal(DetailTab.Columns, state.Tab);
        Assert.Null(state.SelectedTable);
        Assert.Null(state.SelectedDetail());
        Assert.Empty(state.FilteredTables());
    }

    [Fact]
    public void Nastaveni_schematu_zachova_vyber_i_rozbaleni()
    {
        // Zásadní pro porovnávání verzí okem: po přepnutí na jinou verzi musí zůstat
        // tatáž tabulka vybraná a stejně rozbalená, jinak se rozdíl ztratí.
        var state = State();
        state.Select(Vzorek.N("Orders"));
        state.ToggleExpanded(Vzorek.N("Orders"));

        state.Schema = Vzorek.Schema();

        Assert.Equal(Vzorek.N("Orders"), state.SelectedTable);
        Assert.Contains(Vzorek.N("Orders"), state.ExpandedNodes);
        Assert.Equal(4, state.Schema.Tables.Count);
    }

    [Fact]
    public void Tabulka_ktera_v_nove_verzi_neni_se_odvybere()
    {
        // Detail tabulky, která tehdy neexistovala, by ukazoval nesmysl.
        var state = State();
        state.Select(Vzorek.N("Orders"));
        state.ToggleExpanded(Vzorek.N("Orders"));

        state.Schema = new DatabaseSchema
        {
            Tables = [Build.Table("Customers", ["Id"], ["Id"])],
        };

        Assert.Null(state.SelectedTable);
        Assert.Empty(state.ExpandedNodes);
    }

    [Fact]
    public void Rozbalene_uzly_se_orezou_jen_o_ty_chybejici()
    {
        var state = State();
        state.ToggleExpanded(Vzorek.N("Orders"));
        state.ToggleExpanded(Vzorek.N("Customers"));

        state.Schema = new DatabaseSchema
        {
            Tables = [Build.Table("Customers", ["Id"], ["Id"])],
        };

        Assert.Contains(Vzorek.N("Customers"), state.ExpandedNodes);
        Assert.DoesNotContain(Vzorek.N("Orders"), state.ExpandedNodes);
    }

    [Fact]
    public void Nastaveni_zakladu_zapne_vizualni_porovnani()
    {
        var state = State();
        state.Schema = Vzorek.Schema();

        Assert.False(state.JeVizualniPorovnani);

        state.Baseline = new DatabaseSchema
        {
            Tables = [Build.Table("Customers", ["Id"], ["Id"])],
        };

        Assert.True(state.JeVizualniPorovnani);

        // Zobrazené schéma nese i to, co v základu bylo navíc, a stavy změn.
        Assert.Equal(ZmenaStav.Pribylo, state.Overlay.Tabulka(Vzorek.N("Orders")));
        Assert.True(state.Overlay.PocetZmen > 0);
    }

    [Fact]
    public void Zaklad_porovnani_jde_precist_zpet()
    {
        var state = State();
        var zaklad = new DatabaseSchema { Tables = [Build.Table("Customers", ["Id"], ["Id"])] };

        state.Baseline = zaklad;

        Assert.Same(zaklad, state.Baseline);
    }

    [Fact]
    public void Zruseni_zakladu_porovnani_vypne()
    {
        var state = State();
        state.Schema = Vzorek.Schema();
        state.Baseline = new DatabaseSchema { Tables = [] };

        state.Baseline = null;

        Assert.False(state.JeVizualniPorovnani);
        Assert.Equal(0, state.Overlay.PocetZmen);
        Assert.Equal(state.Schema.Tables.Count, state.DisplaySchema.Tables.Count);
    }

    [Fact]
    public void Prazdne_schema_nezpusobi_pad()
    {
        var state = State();

        state.Schema = null!;

        Assert.Empty(state.Schema.Tables);
    }

    [Fact]
    public void Hledani_omezi_seznam()
    {
        var state = State();
        state.Search = "order";

        Assert.Equal("Orders", Assert.Single(state.FilteredTables()).Name.Name);
    }

    [Fact]
    public void Skupina_omezi_seznam()
    {
        var state = State();
        state.Group = "Prodej";

        Assert.Equal("Orders", Assert.Single(state.FilteredTables()).Name.Name);
    }

    [Fact]
    public void Neznama_skupina_nefiltruje()
    {
        var state = State();
        state.Group = "Neexistuje";

        Assert.Equal(4, state.FilteredTables().Count);
    }

    [Fact]
    public void Filtr_schematu_omezi_seznam()
    {
        var state = new ViewerState
        {
            Schema = new DatabaseSchema
            {
                Tables =
                [
                    new DbTable { Name = new DbObjectName("dbo", "A") },
                    new DbTable { Name = new DbObjectName("sales", "B") },
                ],
            },
        };

        state.SchemaName = "sales";

        Assert.Equal("B", Assert.Single(state.FilteredTables()).Name.Name);
    }

    [Fact]
    public void Vyber_tabulky_prepne_na_prvni_zalozku()
    {
        var state = State();
        state.Tab = DetailTab.Data;

        state.Select(Vzorek.N("Orders"));

        Assert.Equal(DetailTab.Columns, state.Tab);
        Assert.Equal("Orders", state.SelectedDetail()!.Name.Name);
    }

    [Fact]
    public void Vyber_null_zrusi_detail()
    {
        var state = State();
        state.Select(Vzorek.N("Orders"));

        state.Select(null);

        Assert.Null(state.SelectedDetail());
    }

    [Fact]
    public void Rozbaleni_uzlu_se_prepina()
    {
        var state = State();
        var table = Vzorek.N("Orders");

        state.ToggleExpanded(table);
        Assert.Contains(table, state.ExpandedNodes);

        state.ToggleExpanded(table);
        Assert.DoesNotContain(table, state.ExpandedNodes);
    }

    [Fact]
    public void Bez_focusu_diagram_ukaze_vsechny_filtrovane()
    {
        var state = State();
        state.FocusEnabled = false;
        state.Select(Vzorek.N("Orders"));

        Assert.Equal(4, state.DiagramTables().Count);
    }

    [Fact]
    public void Bez_vybrane_tabulky_focus_nic_neomezi()
    {
        var state = State();

        Assert.Equal(4, state.DiagramTables().Count);
    }

    [Fact]
    public void Focus_omezi_diagram_na_okoli()
    {
        var state = State();
        state.Select(Vzorek.N("Orders"));

        var tables = state.DiagramTables();

        Assert.Equal(2, tables.Count);
        Assert.Contains(tables, t => t.Name.Name == "Customers");
        Assert.DoesNotContain(tables, t => t.Name.Name == "Products");
    }

    [Fact]
    public void Nulova_vzdalenost_ukaze_jen_vybranou()
    {
        var state = State();
        state.Select(Vzorek.N("Orders"));
        state.FocusHops = 0;

        Assert.Equal("Orders", Assert.Single(state.DiagramTables()).Name.Name);
    }

    [Fact]
    public void Vzdalenost_se_orizne_na_povolene_meze()
    {
        var state = State();

        state.FocusHops = 99;
        Assert.Equal(3, state.FocusHops);

        state.FocusHops = -5;
        Assert.Equal(0, state.FocusHops);
    }

    [Fact]
    public void Vybrana_tabulka_zustane_v_diagramu_i_kdyz_ji_filtr_vyradi()
    {
        // Jinak by focus ukazoval prázdno a nebylo by jasné proč.
        var state = State();
        state.Select(Vzorek.N("Orders"));
        state.Search = "Products";

        Assert.Contains(state.DiagramTables(), t => t.Name.Name == "Orders");
    }

    [Fact]
    public void Vazby_v_diagramu_odpovidaji_zobrazenym_tabulkam()
    {
        var state = State();
        state.Select(Vzorek.N("Orders"));

        Assert.Single(state.DiagramRelationships());

        state.FocusHops = 0;
        Assert.Empty(state.DiagramRelationships());
    }

    [Fact]
    public void Nalezy_diffu_se_vazou_na_tabulku()
    {
        var state = State();
        state.Diff = Vzorek.Diff();

        Assert.Single(state.FindingsFor(Vzorek.N("Orders")));
        Assert.Empty(state.FindingsFor(Vzorek.N("Products")));

        Assert.Equal(DiffSeverity.Error, state.SeverityOf(Vzorek.N("Orders")));
        Assert.Equal(DiffSeverity.Warning, state.SeverityOf(Vzorek.N("Customers")));
        Assert.Null(state.SeverityOf(Vzorek.N("Products")));
    }

    [Fact]
    public void Bez_nacteneho_diffu_nejsou_zadne_nalezy()
    {
        var state = State();

        Assert.Empty(state.FindingsFor(Vzorek.N("Orders")));
        Assert.Null(state.SeverityOf(Vzorek.N("Orders")));
    }
}

public class DbsViewerClientTests
{
    private static DbsViewerClient Client(HttpStatusCode status, string? json = null)
    {
        var handler = new StubHandler(status, json);
        return new DbsViewerClient(new HttpClient(handler) { BaseAddress = new Uri("http://test/dbschema/") });
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, DbsViewerJson.Compact);

    [Fact]
    public async Task Meta_se_nacte()
    {
        var client = Client(HttpStatusCode.OK, Json(Vzorek.Meta()));

        var meta = await client.GetMetaAsync();

        Assert.Equal("Testovací schéma", meta.Title);
        Assert.True(meta.CanDiff);
    }

    [Fact]
    public async Task Schema_se_nacte_i_s_parametry()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Json(Vzorek.Schema()));
        var client = new DbsViewerClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://test/dbschema/") });

        await client.GetSchemaAsync("live", refresh: true);

        Assert.Contains("source=live", handler.LastUrl, StringComparison.Ordinal);
        Assert.Contains("refresh=true", handler.LastUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Schema_bez_parametru_nema_dotaz()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Json(Vzorek.Schema()));
        var client = new DbsViewerClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://test/dbschema/") });

        await client.GetSchemaAsync();

        Assert.EndsWith("api/schema", handler.LastUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diff_se_nacte()
    {
        var client = Client(HttpStatusCode.OK, Json(Vzorek.Diff()));

        var diff = await client.GetDiffAsync(refresh: true);

        Assert.Equal(1, diff.ErrorCount);
    }

    [Fact]
    public async Task Radky_se_nactou_POSTem()
    {
        // Hledané hodnoty jsou obsah databáze; v adrese by skončily v historii
        // prohlížeče i v logu serveru, proto chodí tělem požadavku.
        var handler = new StubHandler(HttpStatusCode.OK, Json(new RowPreview { PageSize = 10 }));
        var client = new DbsViewerClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://test/dbschema/") });

        var preview = await client.GetRowsAsync(
            Vzorek.N("Orders"),
            new DataQuery { Page = 2, PageSize = 10 });

        Assert.Contains("api/tables/-/Orders/rows", handler.LastUrl, StringComparison.Ordinal);
        Assert.DoesNotContain("?", handler.LastUrl, StringComparison.Ordinal);
        Assert.Equal(10, preview.PageSize);
    }

    [Fact]
    public async Task Radky_s_vlastnim_schematem_pouziji_jeho_jmeno()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Json(new RowPreview()));
        var client = new DbsViewerClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://test/dbschema/") });

        await client.GetRowsAsync(new DbObjectName("sales", "Orders"));

        Assert.Contains("api/tables/sales/Orders/rows", handler.LastUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Obnoveni_posle_POST()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent);
        var client = new DbsViewerClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://test/dbschema/") });

        await client.RefreshAsync();

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "Přístup odepřen")]
    [InlineData(HttpStatusCode.Unauthorized, "Nejsi přihlášený")]
    [InlineData(HttpStatusCode.NotFound, "neexistuje")]
    [InlineData(HttpStatusCode.BadRequest, "nerozuměl")]
    [InlineData(HttpStatusCode.InternalServerError, "chybou 500")]
    public async Task Chyby_maji_srozumitelne_hlasky(HttpStatusCode status, string expected)
    {
        var client = Client(status);

        var exception = await Assert.ThrowsAsync<DbsViewerClientException>(() => client.GetMetaAsync());

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chyba_pri_obnoveni_se_take_prelozi()
    {
        var client = Client(HttpStatusCode.Forbidden);

        await Assert.ThrowsAsync<DbsViewerClientException>(() => client.RefreshAsync());
    }

    [Fact]
    public async Task Prazdna_odpoved_je_chyba()
    {
        var client = Client(HttpStatusCode.OK, "null");

        var exception = await Assert.ThrowsAsync<DbsViewerClientException>(() => client.GetMetaAsync());

        Assert.Contains("prázdnou odpověď", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Prazdne_schema_se_v_ceste_zapise_pomlckou()
    {
        Assert.Equal("-", DbsViewerClient.SchemaSegment(new DbObjectName(null, "T")));
        Assert.Equal("dbo", DbsViewerClient.SchemaSegment(new DbObjectName("dbo", "T")));
    }

    private sealed class StubHandler(HttpStatusCode status, string? json = null) : HttpMessageHandler
    {
        public string LastUrl { get; private set; } = "";

        public HttpMethod LastMethod { get; private set; } = HttpMethod.Get;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastUrl = request.RequestUri?.ToString() ?? "";
            LastMethod = request.Method;

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json ?? "", Encoding.UTF8, "application/json"),
            });
        }
    }
}
