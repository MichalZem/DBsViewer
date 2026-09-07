using DbsViewer.TestKit;
using DbsViewer.Ui.Components;
using DbsViewer.Ui.Model;

namespace DbsViewer.Tests.Ui;

/// <summary>Rozebrání a složení query stringu.</summary>
public class QueryStringTests
{
    [Theory]
    [InlineData("http://localhost/dbschema/", "http://localhost/dbschema/")]
    [InlineData("http://localhost/dbschema/?a=1", "http://localhost/dbschema/")]
    [InlineData("http://localhost/dbschema/#kotva", "http://localhost/dbschema/")]
    [InlineData("http://localhost/dbschema/?a=1#kotva", "http://localhost/dbschema/")]
    public void Zaklad_adresy_je_bez_query_i_fragmentu(string uri, string expected) =>
        Assert.Equal(expected, QueryString.Path(uri));

    [Fact]
    public void Zaklad_bez_adresy_je_chyba_argumentu() =>
        Assert.Throws<ArgumentNullException>(() => QueryString.Path(null!));

    [Fact]
    public void Bez_query_nejsou_zadne_parametry()
    {
        Assert.Empty(QueryString.Parse(null));
        Assert.Empty(QueryString.Parse("http://localhost/dbschema/"));
        Assert.Empty(QueryString.Parse("http://localhost/dbschema/?"));
    }

    [Fact]
    public void Parametry_si_drzi_poradi_z_adresy()
    {
        var dvojice = QueryString.Parse("http://localhost/?b=2&a=1");

        Seq.Equal(["b", "a"], dvojice.Select(d => d.Key));
        Seq.Equal(["2", "1"], dvojice.Select(d => d.Value));
    }

    [Fact]
    public void Parametr_bez_rovnitka_ma_prazdnou_hodnotu()
    {
        var dvojice = QueryString.Parse("http://localhost/?vlajka");

        Assert.Equal("vlajka", dvojice[0].Key);
        Assert.Equal("", dvojice[0].Value);
    }

    [Fact]
    public void Fragment_za_mrizkou_do_query_nepatri()
    {
        var dvojice = QueryString.Parse("http://localhost/?a=1#b=2");

        Assert.Single(dvojice);
        Assert.Equal("1", dvojice[0].Value);
    }

    [Fact]
    public void Hodnoty_se_dekoduji()
    {
        // Jméno tabulky může obsahovat mezeru i čárku a musí projít adresou beze změny.
        Assert.Equal("Order Items", QueryString.Value("http://localhost/?table=Order%20Items", "table"));
        Assert.Equal("a,b", QueryString.Value("http://localhost/?x=a%2Cb", "x"));
    }

    [Fact]
    public void Jmeno_parametru_se_porovnava_bez_ohledu_na_velikost_pismen() =>
        Assert.Equal("1", QueryString.Value("http://localhost/?PANE=1", "pane"));

    [Fact]
    public void Chybejici_parametr_je_null() =>
        Assert.Null(QueryString.Value("http://localhost/?a=1", "b"));

    [Fact]
    public void Slouceni_nahradi_vlastni_a_cizi_necha_byt()
    {
        var cil = QueryString.Merge(
            "http://localhost/dbschema/?lang=cs&pane=diff",
            ["pane"],
            [new KeyValuePair<string, string>("pane", "diagram")]);

        Assert.Equal("http://localhost/dbschema/?lang=cs&pane=diagram", cil);
    }

    [Fact]
    public void Slouceni_bez_hodnot_necha_jen_zaklad()
    {
        // Adresa nedotčené prohlížečky musí zůstat čistá, ne skončit osamělým otazníkem.
        var cil = QueryString.Merge("http://localhost/dbschema/?pane=diff", ["pane"], []);

        Assert.Equal("http://localhost/dbschema/", cil);
    }

    [Fact]
    public void Slouceni_koduje_hodnoty()
    {
        var cil = QueryString.Merge(
            "http://localhost/",
            ["table"],
            [new KeyValuePair<string, string>("table", "Order Items")]);

        Assert.Equal("http://localhost/?table=Order%20Items", cil);
    }

    [Fact]
    public void Slouceni_bez_povinnych_argumentu_je_chyba()
    {
        Assert.Throws<ArgumentNullException>(() => QueryString.Merge(null!, [], []));
        Assert.Throws<ArgumentNullException>(() => QueryString.Merge("http://localhost/", null!, []));
        Assert.Throws<ArgumentNullException>(() => QueryString.Merge("http://localhost/", [], null!));
    }
}

/// <summary>
/// Stav prohlížečky v adrese. Čistá funkce nad řetězcem, takže se dá otestovat
/// bez vykreslování i bez prohlížeče.
/// </summary>
public class ViewerRouteTests
{
    private const string Zaklad = "http://localhost/dbschema/";

    private static ViewerState State()
    {
        var state = new ViewerState { Meta = Vzorek.Meta() };
        state.Schema = Vzorek.Schema();
        return state;
    }

    [Fact]
    public void Nedotcena_prohlizecka_ma_cistou_adresu() =>
        // Výchozí hodnoty se nezapisují — jinak by adresa po prvním kliknutí
        // vypadala jako výpis konfigurace.
        Assert.Equal(Zaklad, ViewerRoute.From(State()).UrlFor(Zaklad));

    [Fact]
    public void Vybrana_tabulka_a_panel_jsou_v_adrese()
    {
        var state = State();
        state.Pane = ViewerPane.Diagram;
        state.Select(Vzorek.N("Orders"));

        Assert.Equal($"{Zaklad}?pane=diagram&table=Orders", ViewerRoute.From(state).UrlFor(Zaklad));
    }

    [Fact]
    public void Adresa_nese_i_zalozku_verzi_a_zaklad_porovnani()
    {
        var state = State();
        state.Select(Vzorek.N("Orders"));
        state.Tab = DetailTab.Data;
        state.SelectedMigration = "20260202_Sloupec";
        state.BaselineMigration = "20260101_Zaklad";

        var adresa = ViewerRoute.From(state).UrlFor(Zaklad);

        Assert.Contains("tab=data", adresa, StringComparison.Ordinal);
        Assert.Contains("version=20260202_Sloupec", adresa, StringComparison.Ordinal);
        Assert.Contains("base=20260101_Zaklad", adresa, StringComparison.Ordinal);
    }

    [Fact]
    public void Slouceny_zdroj_se_do_adresy_nepise()
    {
        // Bez parametru si zdroj vybere server podle své konfigurace, a to je
        // správnější než hodnota zapsaná do adresy.
        var state = State();

        Assert.DoesNotContain("source", ViewerRoute.From(state).UrlFor(Zaklad), StringComparison.Ordinal);

        state.Source = "live";

        Assert.Contains("source=live", ViewerRoute.From(state).UrlFor(Zaklad), StringComparison.Ordinal);
    }

    [Fact]
    public void Adresa_nese_filtry_i_hledani()
    {
        var state = State();
        state.Search = "ord";
        state.Group = "Prodej";
        state.SchemaName = "dbo";

        var adresa = ViewerRoute.From(state).UrlFor(Zaklad);

        Assert.Contains("q=ord", adresa, StringComparison.Ordinal);
        Assert.Contains("group=Prodej", adresa, StringComparison.Ordinal);
        Assert.Contains("schema=dbo", adresa, StringComparison.Ordinal);
    }

    [Fact]
    public void Adresa_nese_i_stav_diagramu()
    {
        var state = State();
        state.FocusEnabled = false;
        state.FocusHops = 3;
        state.ToggleExpanded(Vzorek.N("Orders"));
        state.ToggleExpanded(Vzorek.N("Customers"));

        var adresa = ViewerRoute.From(state).UrlFor(Zaklad);

        Assert.Contains("focus=0", adresa, StringComparison.Ordinal);
        Assert.Contains("hops=3", adresa, StringComparison.Ordinal);

        // Pořadí je abecední, ne podle toho, jak se uzly rozbalovaly — jinak by se
        // adresa měnila bez toho, že by se změnilo, co je vidět.
        Assert.Contains("expand=Customers%2COrders", adresa, StringComparison.Ordinal);
    }

    [Fact]
    public void Adresa_zachova_volbu_jazyka()
    {
        // Klik v prohlížečce nesmí uživateli přepnout jazyk zpátky na výchozí.
        var state = State();
        state.Pane = ViewerPane.Overview;

        var adresa = ViewerRoute.From(state).UrlFor($"{Zaklad}?lang=cs");

        Assert.Contains("lang=cs", adresa, StringComparison.Ordinal);
        Assert.Contains("pane=overview", adresa, StringComparison.Ordinal);
    }

    [Fact]
    public void Adresa_bez_stavu_je_chyba_argumentu()
    {
        Assert.Throws<ArgumentNullException>(() => ViewerRoute.From(null!));
        Assert.Throws<ArgumentNullException>(() => new ViewerRoute().ApplyTo(null!));
    }

    [Theory]
    [InlineData("overview", ViewerPane.Overview)]
    [InlineData("browser", ViewerPane.Browser)]
    [InlineData("diagram", ViewerPane.Diagram)]
    [InlineData("diff", ViewerPane.Diff)]
    [InlineData("history", ViewerPane.History)]
    [InlineData("HISTORY", ViewerPane.History)]
    [InlineData("nesmysl", ViewerPane.Browser)]
    [InlineData(null, ViewerPane.Browser)]
    public void Panel_se_cte_z_adresy(string? kod, ViewerPane expected) =>
        Assert.Equal(expected, ViewerRoute.FromUri($"{Zaklad}?pane={kod}").Pane);

    [Theory]
    [InlineData("columns", DetailTab.Columns)]
    [InlineData("indexes", DetailTab.Indexes)]
    [InlineData("foreignkeys", DetailTab.ForeignKeys)]
    [InlineData("referencedby", DetailTab.ReferencedBy)]
    [InlineData("data", DetailTab.Data)]
    [InlineData("nesmysl", DetailTab.Columns)]
    public void Zalozka_se_cte_z_adresy(string kod, DetailTab expected) =>
        Assert.Equal(expected, ViewerRoute.FromUri($"{Zaklad}?tab={kod}").Tab);

    [Fact]
    public void Prazdna_adresa_da_vychozi_stav()
    {
        var adresa = ViewerRoute.FromUri(null);

        Assert.Equal(ViewerPane.Browser, adresa.Pane);
        Assert.Null(adresa.Table);
        Assert.Null(adresa.Version);
        Assert.Null(adresa.Baseline);
        Assert.Equal("merged", adresa.Source);
        Assert.Null(adresa.Group);
        Assert.Null(adresa.Schema);
        Assert.Equal("", adresa.Search);
        Assert.True(adresa.Focus);
        Assert.Equal(1, adresa.Hops);
        Assert.Empty(adresa.Expanded);
    }

    [Fact]
    public void Prazdna_hodnota_parametru_se_bere_jako_chybejici() =>
        Assert.Null(ViewerRoute.FromUri($"{Zaklad}?version=&base=&table=").Version);

    [Theory]
    [InlineData("0", 0)]
    [InlineData("2", 2)]
    [InlineData("9", 3)]
    [InlineData("-4", 0)]
    [InlineData("nic", 1)]
    public void Vzdalenost_sousedu_se_orizne_na_povoleny_rozsah(string kod, int expected) =>
        Assert.Equal(expected, ViewerRoute.FromUri($"{Zaklad}?hops={kod}").Hops);

    [Fact]
    public void Focus_vypina_jen_nula()
    {
        Assert.False(ViewerRoute.FromUri($"{Zaklad}?focus=0").Focus);
        Assert.True(ViewerRoute.FromUri($"{Zaklad}?focus=1").Focus);
        Assert.True(ViewerRoute.FromUri(Zaklad).Focus);
    }

    [Fact]
    public void Jmeno_se_schematem_se_rozdeli_po_prvni_tecce()
    {
        Assert.Equal(new DbObjectName("dbo", "Orders"), ViewerRoute.FromUri($"{Zaklad}?table=dbo.Orders").Table);
        Assert.Equal(new DbObjectName(null, "Orders"), ViewerRoute.FromUri($"{Zaklad}?table=Orders").Table);

        // Tečka na kraji schéma neurčuje — jinak by z ní vzniklo prázdné jméno.
        Assert.Equal(new DbObjectName(null, ".Orders"), ViewerRoute.FromUri($"{Zaklad}?table=.Orders").Table);
        Assert.Equal(new DbObjectName(null, "Orders."), ViewerRoute.FromUri($"{Zaklad}?table=Orders.").Table);
    }

    [Fact]
    public void Seznam_rozbalenych_uzlu_prezije_prazdne_polozky()
    {
        var adresa = ViewerRoute.FromUri($"{Zaklad}?expand=Orders,,Customers");

        Seq.Equal([Vzorek.N("Orders"), Vzorek.N("Customers")], adresa.Expanded);
        Assert.Empty(ViewerRoute.FromUri($"{Zaklad}?expand=").Expanded);
    }

    [Fact]
    public void Adresa_se_promitne_do_stavu()
    {
        var state = State();

        ViewerRoute.FromUri($"{Zaklad}?pane=diagram&table=Orders&tab=data&q=or&group=Prodej"
            + "&schema=dbo&focus=0&hops=2&expand=Customers").ApplyTo(state);

        Assert.Equal(ViewerPane.Diagram, state.Pane);
        Assert.Equal(Vzorek.N("Orders"), state.SelectedTable);
        Assert.Equal(DetailTab.Data, state.Tab);
        Assert.Equal("or", state.Search);
        Assert.Equal("Prodej", state.Group);
        Assert.Equal("dbo", state.SchemaName);
        Assert.False(state.FocusEnabled);
        Assert.Equal(2, state.FocusHops);
        Seq.Equal([Vzorek.N("Customers")], state.ExpandedNodes);
    }

    [Fact]
    public void Tabulka_ktera_ve_schematu_neni_se_z_adresy_zahodi()
    {
        // Odkaz může být starý a ukazovat na tabulku, která mezitím zmizela.
        var state = State();

        ViewerRoute.FromUri($"{Zaklad}?table=Neexistuje&expand=Taky_ne,Orders").ApplyTo(state);

        Assert.Null(state.SelectedTable);
        Seq.Equal([Vzorek.N("Orders")], state.ExpandedNodes);
    }

    [Fact]
    public void Jmeno_z_adresy_se_dohleda_ve_schematu_i_pres_tecku_ve_jmenu()
    {
        // Rozdělení podle tečky by z tabulky „Order.Items" udělalo schéma „Order".
        // Proti tomu stojí dohledání celého jména ve schématu.
        var state = new ViewerState
        {
            Schema = new DatabaseSchema { Tables = [Build.Table("Order.Items", ["Id"], ["Id"])] },
        };

        ViewerRoute.FromUri($"{Zaklad}?table=order.items").ApplyTo(state);

        Assert.Equal(new DbObjectName(null, "Order.Items"), state.SelectedTable);
    }

    [Fact]
    public void Rozbalene_uzly_se_pri_promitnuti_nahradi()
    {
        // Zpět v prohlížeči musí uzly i zabalit, ne jen rozbalovat další.
        var state = State();
        state.ToggleExpanded(Vzorek.N("Orders"));

        ViewerRoute.FromUri($"{Zaklad}?expand=Customers").ApplyTo(state);

        Seq.Equal([Vzorek.N("Customers")], state.ExpandedNodes);
    }

    [Fact]
    public void Stav_a_adresa_prezijou_cestu_tam_i_zpatky()
    {
        var state = State();
        state.Pane = ViewerPane.Diagram;
        state.Select(Vzorek.N("Orders"));
        state.Tab = DetailTab.Indexes;
        state.Search = "or d";
        state.Group = "Prodej";
        state.SchemaName = "dbo";
        state.FocusEnabled = false;
        state.FocusHops = 0;
        state.ToggleExpanded(Vzorek.N("Customers"));

        var puvodni = ViewerRoute.From(state);
        var zpatky = ViewerRoute.FromUri(puvodni.UrlFor(Zaklad));

        // Seznamy se porovnávají zvlášť: záznam je srovnává podle reference, ne obsahu.
        Seq.Equal(puvodni.Expanded, zpatky.Expanded);
        Assert.Equal(puvodni with { Expanded = [] }, zpatky with { Expanded = [] });
    }
}
