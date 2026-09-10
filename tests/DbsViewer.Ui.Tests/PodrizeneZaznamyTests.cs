using Bunit;
using DbsViewer.TestKit;
using DbsViewer.Ui.Components;
using DbsViewer.Ui.Model;

namespace DbsViewer.Tests.Ui;

/// <summary>
/// Proklik z řádku nadřazené tabulky na jeho podřízené záznamy. Šipka u hodnoty klíče
/// přepne mřížku na podřízenou tabulku omezenou právě na tenhle řádek.
/// </summary>
public class PodrizeneZaznamyTests : TestContext
{
    private static DbObjectName N(string name) => new(null, name);

    private static ChildLink Odkaz(
        string table = "Orders",
        string foreignKey = "FK_Orders",
        string[]? columns = null,
        string[]? parentColumns = null) => new()
        {
            Table = N(table),
            ForeignKeyName = foreignKey,
            Columns = columns ?? ["CustomerId"],
            ParentColumns = parentColumns ?? ["Id"],
        };

    private static DbTable Zakaznici() => new()
    {
        Name = N("Customers"),
        Columns =
        [
            new DbColumn { Name = "Id", Ordinal = 1, StoreType = "int", IsPrimaryKey = true },
            new DbColumn { Name = "Email", Ordinal = 2, StoreType = "nvarchar(200)" },
        ],
        PrimaryKey = new DbPrimaryKey { Columns = ["Id"] },
    };

    private static RowPreview Nahled(string? id = "1", params string[] masked) => new()
    {
        Columns = ["Id", "Email"],
        MaskedColumns = masked,
        Rows = [[id, "prvni@x.cz"]],
        PageSize = 50,
        TotalRows = 1,
    };

    private IRenderedComponent<DataNahled> Mrizka(
        IReadOnlyList<ChildLink>? links = null,
        IReadOnlyList<ChildFilter>? link = null,
        RowPreview? preview = null,
        List<ChildJump>? skoky = null,
        List<DataQuery>? loads = null,
        List<int>? zruseni = null,
        bool canEdit = false) =>
        RenderComponent<DataNahled>(p => p
            .Add(x => x.Table, Zakaznici())
            .Add(x => x.Preview, preview ?? Nahled())
            .Add(x => x.Links, links ?? [Odkaz()])
            .Add(x => x.Link, link ?? [])
            .Add(x => x.CanEdit, canEdit)
            .Add(x => x.OnUpdate, canEdit ? (DataUpdate _) => Task.FromResult<string?>(null) : null)
            .Add(x => x.OnLoad, (DataQuery q) => loads?.Add(q))
            .Add(x => x.OnShowChildren, (ChildJump j) => skoky?.Add(j))
            .Add(x => x.OnClearLink, () => zruseni?.Add(1)));

    // ---------- šipka v mřížce ----------

    [Fact]
    public void Sipka_visi_u_sloupce_na_ktery_se_odkazuje()
    {
        var component = Mrizka();

        // Vazba míří na Id, takže šipka patří do buňky s Id — ne k e-mailu vedle.
        Assert.Single(component.FindAll("button.ikona-deti"));
        Assert.Single(component.FindAll("td:first-child button.ikona-deti"));
        Assert.Contains("1⤷", component.FindAll("td").ElementAt(0).TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Bez_prichozich_vazeb_zadna_sipka_neni()
    {
        Assert.Empty(Mrizka(links: []).FindAll("button.ikona-deti"));
    }

    [Fact]
    public void Hodnota_NULL_sipku_nenabidne()
    {
        // Cizí klíč se na NULL nenaváže, takže by odkaz vedl vždycky do prázdna.
        Assert.Empty(Mrizka(preview: Nahled(id: null)).FindAll("button.ikona-deti"));
    }

    [Fact]
    public void Zamaskovany_sloupec_sipku_nenabidne()
    {
        // V mřížce je místo hodnoty maska — filtrovalo by se podle hvězdiček.
        Assert.Empty(Mrizka(preview: Nahled("***", "Id")).FindAll("button.ikona-deti"));
    }

    [Fact]
    public void Sloupec_mimo_nactenou_stranku_sipku_nenabidne()
    {
        var odkaz = Odkaz(parentColumns: ["Kod"]);

        Assert.Empty(Mrizka(links: [odkaz]).FindAll("button.ikona-deti"));
    }

    [Fact]
    public void Rozepsana_uprava_sipku_schova()
    {
        // Kliknutí by odnavigovalo pryč a neuložené hodnoty by byly ztracené.
        var component = Mrizka(canEdit: true);

        Assert.Single(component.FindAll("button.ikona-deti"));

        component.FindAll("td.akce button").First(b => b.TextContent.Trim() == "Edit").Click();

        Assert.Empty(component.FindAll("button.ikona-deti"));
    }

    // ---------- skok ----------

    [Fact]
    public void Jediny_cil_se_otevre_rovnou_bez_nabidky()
    {
        var skoky = new List<ChildJump>();
        var component = Mrizka(skoky: skoky);

        component.Find("button.ikona-deti").Click();

        Assert.Empty(component.FindAll("ul.nabidka-deti"));

        var skok = Assert.Single(skoky);

        Assert.Equal(N("Orders"), skok.Table);

        var podminka = Assert.Single(skok.Filters);

        Assert.Equal("CustomerId", podminka.Column);
        Assert.Equal("1", podminka.Value);
    }

    [Fact]
    public void Vic_cilu_nejdriv_nabidne_vyber()
    {
        var skoky = new List<ChildJump>();
        var component = Mrizka(
            links: [Odkaz(), Odkaz("Reviews", "FK_Reviews", ["AutorId"])],
            skoky: skoky);

        component.Find("button.ikona-deti").Click();

        Assert.Empty(skoky);
        Assert.Equal(2, component.FindAll("ul.nabidka-deti li .odkaz").Count);
        Assert.Contains("Orders (CustomerId)", component.Markup, StringComparison.Ordinal);
        Assert.Contains("Reviews (AutorId)", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Vyber_z_nabidky_odskoci_na_zvolenou_tabulku()
    {
        var skoky = new List<ChildJump>();
        var component = Mrizka(
            links: [Odkaz(), Odkaz("Reviews", "FK_Reviews", ["AutorId"])],
            skoky: skoky);

        component.Find("button.ikona-deti").Click();
        component.FindAll("ul.nabidka-deti li .odkaz").ElementAt(1).Click();

        Assert.Equal(N("Reviews"), Assert.Single(skoky).Table);
        Assert.Empty(component.FindAll("ul.nabidka-deti"));
    }

    [Fact]
    public void Druhy_klik_na_sipku_nabidku_zavre()
    {
        var component = Mrizka(links: [Odkaz(), Odkaz("Reviews", "FK_Reviews", ["AutorId"])]);

        component.Find("button.ikona-deti").Click();
        Assert.Single(component.FindAll("ul.nabidka-deti"));

        component.Find("button.ikona-deti").Click();
        Assert.Empty(component.FindAll("ul.nabidka-deti"));
    }

    [Fact]
    public void Slozeny_klic_posle_vsechny_sloupce()
    {
        var skoky = new List<ChildJump>();

        RenderComponent<DataNahled>(p => p
            .Add(x => x.Table, Zakaznici())
            .Add(x => x.Preview, Nahled())
            .Add(x => x.Links, [Odkaz(columns: ["Kod", "Mail"], parentColumns: ["Id", "Email"])])
            .Add(x => x.OnShowChildren, (ChildJump j) => skoky.Add(j)))
            .Find("button.ikona-deti").Click();

        var skok = Assert.Single(skoky);

        Seq.Equal(["Kod", "Mail"], [.. skok.Filters.Select(static f => f.Column)]);
        Seq.Equal(["1", "prvni@x.cz"], [.. skok.Filters.Select(static f => f.Value)]);
    }

    // ---------- štítek vazby ----------

    [Fact]
    public void Vazba_na_nadrazeny_radek_je_videt_nad_mrizkou()
    {
        var component = Mrizka(link: [new ChildFilter("CustomerId", "7")]);

        var stitek = component.Find("p.vazba-na-radek").TextContent;

        Assert.Contains("Bound to a parent row", stitek, StringComparison.Ordinal);
        Assert.Contains("CustomerId = 7", stitek, StringComparison.Ordinal);
    }

    [Fact]
    public void Bez_vazby_zadny_stitek_neni()
    {
        Assert.Empty(Mrizka().FindAll("p.vazba-na-radek"));
    }

    [Fact]
    public void Zruseni_vazby_zavola_obsluhu()
    {
        var zruseni = new List<int>();
        var component = Mrizka(link: [new ChildFilter("CustomerId", "7")], zruseni: zruseni);

        component.Find("p.vazba-na-radek button").Click();

        Assert.Single(zruseni);
    }

    // ---------- dotaz na server ----------

    [Fact]
    public void Vazba_jde_do_dotazu_jako_presna_shoda()
    {
        var loads = new List<DataQuery>();

        Mrizka(link: [new ChildFilter("CustomerId", "7")], loads: loads);

        var filtr = Assert.Single(Assert.Single(loads).Filters);

        Assert.Equal("CustomerId", filtr.Column);
        Assert.Equal(FilterOperator.Equals, filtr.Operator);
        Assert.Equal("7", filtr.Value);
    }

    [Fact]
    public void Vazba_a_vlastni_filtr_plati_zaroven()
    {
        var loads = new List<DataQuery>();
        var component = Mrizka(link: [new ChildFilter("CustomerId", "7")], loads: loads);

        component.FindAll("tr.filtry input").ElementAt(1).Change("cz");

        var posledni = loads[^1].Filters;

        Assert.Equal(2, posledni.Count);
        Assert.Equal(FilterOperator.Equals, posledni[0].Operator);
        Assert.Equal(FilterOperator.Contains, posledni[1].Operator);
    }

    [Fact]
    public void Zmena_vazby_nacte_data_znovu()
    {
        var loads = new List<DataQuery>();
        var component = Mrizka(link: [new ChildFilter("CustomerId", "7")], loads: loads);

        Assert.Single(loads);

        component.SetParametersAndRender(p => p.Add(x => x.Link, [new ChildFilter("CustomerId", "9")]));

        Assert.Equal(2, loads.Count);
        Assert.Equal("9", loads[^1].Filters[0].Value);
    }

    [Fact]
    public void Stejna_vazba_data_znovu_nenacita()
    {
        var loads = new List<DataQuery>();
        var component = Mrizka(link: [new ChildFilter("CustomerId", "7")], loads: loads);

        component.SetParametersAndRender(p => p.Add(x => x.Link, [new ChildFilter("CustomerId", "7")]));

        Assert.Single(loads);
    }

    [Fact]
    public void Zmena_vazby_nechava_vlastni_filtry_byt()
    {
        // Skok na jiného rodiče téže tabulky nechává mřížku beze změny — zahodit
        // v ní rozepsané filtry by uživatele okradlo o práci.
        var loads = new List<DataQuery>();
        var component = Mrizka(link: [new ChildFilter("CustomerId", "7")], loads: loads);

        component.FindAll("tr.filtry input").ElementAt(1).Change("cz");
        component.SetParametersAndRender(p => p.Add(x => x.Link, [new ChildFilter("CustomerId", "9")]));

        Assert.Equal(2, loads[^1].Filters.Count);
    }

    [Fact]
    public void Prazdny_vysledek_s_vazbou_hlasi_omezeni_vypisu()
    {
        var component = Mrizka(
            link: [new ChildFilter("CustomerId", "7")],
            preview: new RowPreview { Columns = ["Id", "Email"], PageSize = 50, TotalRows = 0 });

        Assert.Contains("No row matches the filters", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Popis_odkazu_rekne_kam_vede_a_pres_co()
    {
        Assert.Equal("Orders (CustomerId)", DataNahled.Popis(Odkaz()));
    }

    [Fact]
    public void Popis_potrebuje_odkaz()
    {
        Assert.Throws<ArgumentNullException>(() => DataNahled.Popis(null!));
    }
}

/// <summary>Vazba na nadřazený řádek v adrese a ve stavu prohlížečky.</summary>
public class VazbaVAdreseTests
{
    private const string Zaklad = "http://localhost/dbschema/";

    private static ViewerState State()
    {
        var state = new ViewerState { Meta = Vzorek.Meta() };
        state.Schema = Vzorek.Schema();
        return state;
    }

    [Fact]
    public void Vazba_se_zapise_do_adresy()
    {
        var state = State();
        state.SelectChild(Vzorek.N("Orders"), [new ChildFilter("CustomerId", "7")]);

        var adresa = ViewerRoute.From(state).UrlFor(Zaklad);

        Assert.Contains("table=Orders", adresa, StringComparison.Ordinal);
        Assert.Contains("tab=data", adresa, StringComparison.Ordinal);
        Assert.Contains("link=CustomerId%3D7", adresa, StringComparison.Ordinal);
    }

    [Fact]
    public void Bez_vazby_v_adrese_nic_navic_neni()
    {
        Assert.DoesNotContain("link=", ViewerRoute.From(State()).UrlFor(Zaklad), StringComparison.Ordinal);
    }

    [Fact]
    public void Slozena_vazba_se_zapise_jako_dva_parametry()
    {
        var state = State();
        state.SelectChild(
            Vzorek.N("Orders"),
            [new ChildFilter("Region", "CZ"), new ChildFilter("Kod", "A1")]);

        var adresa = ViewerRoute.From(state).UrlFor(Zaklad);

        Assert.Contains("link=Region%3DCZ", adresa, StringComparison.Ordinal);
        Assert.Contains("link=Kod%3DA1", adresa, StringComparison.Ordinal);
    }

    [Fact]
    public void Vazba_se_z_adresy_precte_zpatky()
    {
        var adresa = ViewerRoute.FromUri($"{Zaklad}?table=Orders&tab=data&link=CustomerId%3D7");

        var podminka = Assert.Single(adresa.Link);

        Assert.Equal("CustomerId", podminka.Column);
        Assert.Equal("7", podminka.Value);
    }

    [Fact]
    public void Rovnitko_v_hodnote_dvojici_nerozdeli()
    {
        // Hodnota primárního klíče může být cokoli, i řetězec s rovnítkem.
        var state = State();
        state.SelectChild(Vzorek.N("Orders"), [new ChildFilter("Kod", "a=b")]);

        var zpet = ViewerRoute.FromUri(ViewerRoute.From(state).UrlFor(Zaklad));

        Assert.Equal("a=b", Assert.Single(zpet.Link).Value);
    }

    [Fact]
    public void Nesmyslna_vazba_v_adrese_se_zahodi()
    {
        // Adresu píše i uživatel a překlep v ní nesmí prohlížečku shodit.
        var adresa = ViewerRoute.FromUri($"{Zaklad}?link=bezrovnitka&link=%3Dbezjmena&link=");

        Assert.Empty(adresa.Link);
    }

    [Fact]
    public void Vazba_bez_tabulky_se_do_stavu_nepromitne()
    {
        var state = State();

        ViewerRoute.FromUri($"{Zaklad}?link=CustomerId%3D7").ApplyTo(state);

        Assert.Empty(state.DataLink);
    }

    [Fact]
    public void Vazba_se_do_stavu_promitne_i_s_tabulkou()
    {
        var state = State();

        ViewerRoute.FromUri($"{Zaklad}?table=Orders&tab=data&link=CustomerId%3D7").ApplyTo(state);

        Assert.Equal(Vzorek.N("Orders"), state.SelectedTable);
        Assert.Equal(DetailTab.Data, state.Tab);
        Assert.Equal("7", Assert.Single(state.DataLink).Value);
    }

    [Fact]
    public void Skok_na_deti_prepne_na_data()
    {
        var state = State();
        state.SelectChild(Vzorek.N("Orders"), [new ChildFilter("CustomerId", "7")]);

        Assert.Equal(Vzorek.N("Orders"), state.SelectedTable);
        Assert.Equal(DetailTab.Data, state.Tab);
        Assert.Single(state.DataLink);
    }

    [Fact]
    public void Skok_potrebuje_podminku()
    {
        Assert.Throws<ArgumentNullException>(() => State().SelectChild(Vzorek.N("Orders"), null!));
    }

    [Fact]
    public void Bezny_vyber_tabulky_vazbu_zahodi()
    {
        // Vazba platila pro předchozí tabulku; filtrovat podle ní jinou nedává smysl.
        var state = State();
        state.SelectChild(Vzorek.N("Orders"), [new ChildFilter("CustomerId", "7")]);

        state.Select(Vzorek.N("Products"));

        Assert.Empty(state.DataLink);
    }

    [Fact]
    public void Zmizeni_tabulky_ze_schematu_vazbu_zahodi()
    {
        var state = State();
        state.SelectChild(Vzorek.N("Orders"), [new ChildFilter("CustomerId", "7")]);

        state.Schema = new DatabaseSchema { Tables = [Build.Table("Products", ["Id"], ["Id"])] };

        Assert.Null(state.SelectedTable);
        Assert.Empty(state.DataLink);
    }
}
