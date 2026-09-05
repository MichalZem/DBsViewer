using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using DbsViewer.Analysis;
using DbsViewer.TestKit;
using DbsViewer.Ui.Components;
using DbsViewer.Ui.Model;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace DbsViewer.Tests.Ui;

/// <summary>Vykreslení ER diagramu.</summary>
public class ErDiagramTests : TestContext
{
    private static DiagramLayoutResult Layout(IReadOnlySet<DbObjectName>? expanded = null)
    {
        var tables = new[]
        {
            Build.Table("Customers", ["Id", "Email"], ["Id"]),
            Build.Table("Orders", ["Id", "CustomerId"], ["Id"],
                [Build.ForeignKey("FK", ["CustomerId"], "Customers", delete: DbDeleteBehavior.Cascade)]),
        };

        var relationships = new[]
        {
            new DbRelationship
            {
                Id = "fk:1",
                From = new DbObjectName(null, "Orders"),
                To = new DbObjectName(null, "Customers"),
                Cardinality = DbCardinality.OneToMany,
                DeleteBehavior = DbDeleteBehavior.Cascade,
                IsRequired = true,
            },
        };

        return DiagramLayout.Compute(tables, relationships, expanded);
    }

    [Fact]
    public void Prazdny_diagram_ukaze_vysvetleni()
    {
        var component = RenderComponent<ErDiagram>(p => p.Add(
            x => x.Layout, new DiagramLayoutResult { Nodes = [], Edges = [] }));

        Assert.Contains("Žádné tabulky", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Uzly_i_hrany_se_vykresli()
    {
        var component = RenderComponent<ErDiagram>(p => p.Add(x => x.Layout, Layout()));

        Assert.Equal(2, component.FindAll(".uzel").Count);
        Assert.Single(component.FindAll(".hrana"));
        Assert.Contains("Customers", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Uzel_nabidne_odkaz_na_data_jen_kdyz_jsou_dostupna()
    {
        var bez = RenderComponent<ErDiagram>(p => p.Add(x => x.Layout, Layout()));

        Assert.Empty(bez.FindAll(".uzel-data"));

        var s = RenderComponent<ErDiagram>(p => p
            .Add(x => x.Layout, Layout())
            .Add(x => x.CanPreviewData, true));

        Assert.Equal(2, s.FindAll(".uzel-data").Count);
    }

    [Fact]
    public void Kliknuti_na_odkaz_na_data_hlasi_tabulku()
    {
        DbObjectName? vybrana = null;

        var component = RenderComponent<ErDiagram>(p => p
            .Add(x => x.Layout, Layout())
            .Add(x => x.CanPreviewData, true)
            .Add(x => x.OnShowData, (DbObjectName t) => vybrana = t));

        component.FindAll(".uzel-data").ElementAt(0).Click();

        Assert.NotNull(vybrana);
    }

    [Fact]
    public void Kaskada_ma_vlastni_tridu()
    {
        var component = RenderComponent<ErDiagram>(p => p.Add(x => x.Layout, Layout()));

        Assert.Contains("hrana kaskada", component.Markup, StringComparison.Ordinal);
        Assert.Contains("1:N", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Vybrany_uzel_se_zvyrazni()
    {
        var component = RenderComponent<ErDiagram>(p => p
            .Add(x => x.Layout, Layout())
            .Add(x => x.SelectedTable, new DbObjectName(null, "Orders")));

        Assert.Single(component.FindAll(".uzel.vybrany"));

        // Vazba vybrané tabulky zůstane plná; ostatní by zesvětlily. Tady je jediná,
        // takže přihlušená není žádná.
        Assert.Empty(component.FindAll(".hrana.prihlusena"));
    }

    [Fact]
    public void Cizi_hrany_pri_vyberu_zesvetli()
    {
        // Různá tloušťka čar by se četla jako různá důležitost vazby, ne jako
        // zvýraznění — proto se místo zesílení ostatní ztlumí.
        var component = RenderComponent<ErDiagram>(p => p
            .Add(x => x.Layout, Layout())
            .Add(x => x.SelectedTable, new DbObjectName(null, "Neexistuje")));

        Assert.Single(component.FindAll(".hrana.prihlusena"));
    }

    [Fact]
    public void Bez_vyberu_se_neztlumi_nic()
    {
        var component = RenderComponent<ErDiagram>(p => p.Add(x => x.Layout, Layout()));

        Assert.Empty(component.FindAll(".hrana.prihlusena"));
    }

    [Fact]
    public void Vysvetlivky_jdou_rozbalit()
    {
        var component = RenderComponent<ErDiagram>(p => p.Add(x => x.Layout, Layout()));

        Assert.DoesNotContain("CASCADE", component.Markup, StringComparison.Ordinal);

        component.Find(".diagram-legenda .prepinac").Click();

        Assert.Contains("CASCADE", component.Markup, StringComparison.Ordinal);
        Assert.Contains("nepovinná vazba", component.Markup, StringComparison.Ordinal);

        component.Find(".diagram-legenda .prepinac").Click();

        Assert.DoesNotContain("CASCADE", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Klik_na_uzel_ohlasi_vyber()
    {
        DbObjectName? vybrano = null;

        var component = RenderComponent<ErDiagram>(p => p
            .Add(x => x.Layout, Layout())
            .Add(x => x.OnSelect, (DbObjectName t) => vybrano = t));

        component.FindAll(".uzel").ElementAt(0).Click();

        Assert.NotNull(vybrano);
    }

    [Fact]
    public void Prepinac_uzlu_ohlasi_rozbaleni()
    {
        DbObjectName? prepnuto = null;

        var component = RenderComponent<ErDiagram>(p => p
            .Add(x => x.Layout, Layout())
            .Add(x => x.OnToggleExpand, (DbObjectName t) => prepnuto = t));

        component.FindAll(".uzel-prepinac").ElementAt(0).Click();

        Assert.NotNull(prepnuto);
    }

    [Fact]
    public void Rozbaleny_uzel_ukaze_vsechny_sloupce()
    {
        var expanded = new HashSet<DbObjectName> { new(null, "Customers") };

        var component = RenderComponent<ErDiagram>(p => p
            .Add(x => x.Layout, Layout(expanded))
            .Add(x => x.Expanded, expanded));

        Assert.Contains("Email", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Nalez_diffu_orámuje_uzel()
    {
        var component = RenderComponent<ErDiagram>(p => p
            .Add(x => x.Layout, Layout())
            .Add(x => x.SeverityOf, name =>
                name.Name == "Orders" ? DiffSeverity.Error : DiffSeverity.Warning));

        Assert.Single(component.FindAll(".uzel.nalez-chyba"));
        Assert.Single(component.FindAll(".uzel.nalez-varovani"));
    }

    [Fact]
    public void Zoom_se_meni_koleckem_i_tlacitky()
    {
        var component = RenderComponent<ErDiagram>(p => p.Add(x => x.Layout, Layout()));

        Assert.Contains("100 %", component.Markup, StringComparison.Ordinal);

        component.Find(".diagram-plocha").Wheel(new WheelEventArgs { DeltaY = -1 });
        Assert.Contains("110 %", component.Markup, StringComparison.Ordinal);

        component.FindAll(".diagram-ovladani button").ElementAt(1).Click();
        Assert.Contains("91 %", component.Markup, StringComparison.Ordinal);

        component.FindAll(".diagram-ovladani button").ElementAt(2).Click();
        Assert.Contains("100 %", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Kolecko_dolu_oddaluje()
    {
        var component = RenderComponent<ErDiagram>(p => p.Add(x => x.Layout, Layout()));

        component.Find(".diagram-plocha").Wheel(new WheelEventArgs { DeltaY = 1 });

        Assert.Contains("90 %", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Zoom_ma_meze()
    {
        var component = RenderComponent<ErDiagram>(p => p.Add(x => x.Layout, Layout()));
        var plus = component.FindAll(".diagram-ovladani button").ElementAt(0);

        for (var i = 0; i < 20; i++)
        {
            component.FindAll(".diagram-ovladani button").ElementAt(0).Click();
        }

        Assert.Contains("300 %", component.Markup, StringComparison.Ordinal);
        Assert.NotNull(plus);
    }

    [Fact]
    public void Tazeni_posune_plochu()
    {
        var component = RenderComponent<ErDiagram>(p => p.Add(x => x.Layout, Layout()));
        var plocha = component.Find(".diagram-plocha");

        plocha.PointerDown(new PointerEventArgs { ClientX = 100, ClientY = 100 });
        plocha.PointerMove(new PointerEventArgs { ClientX = 150, ClientY = 130 });

        Assert.Contains("translate(50 30)", component.Markup, StringComparison.Ordinal);

        plocha.PointerUp(new PointerEventArgs());
        plocha.PointerMove(new PointerEventArgs { ClientX = 300, ClientY = 300 });

        // Po puštění se plocha už nehýbe.
        Assert.Contains("translate(50 30)", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Pohyb_bez_stisku_nic_neposune()
    {
        var component = RenderComponent<ErDiagram>(p => p.Add(x => x.Layout, Layout()));

        component.Find(".diagram-plocha").PointerMove(new PointerEventArgs { ClientX = 50, ClientY = 50 });

        Assert.Contains("translate(0 0)", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Vazebni_tabulka_i_pohled_maji_odznak()
    {
        var tables = new[]
        {
            Build.Table("ProductTags", ["A", "B"], ["A", "B"]) with { IsJoinTable = true },
            Build.Table("V", ["Id"], isView: true),
        };

        var component = RenderComponent<ErDiagram>(p => p.Add(
            x => x.Layout, DiagramLayout.Compute(tables, [])));

        Assert.Contains("N:M", component.Markup, StringComparison.Ordinal);
        Assert.Contains("VIEW", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Nepovinna_vazba_se_kresli_prerusovane()
    {
        var tables = new[]
        {
            Build.Table("A", ["Id"], ["Id"]),
            Build.Table("B", ["Id", "AId"], ["Id"]),
        };

        var relationships = new[]
        {
            new DbRelationship
            {
                Id = "x",
                From = new DbObjectName(null, "B"),
                To = new DbObjectName(null, "A"),
                IsRequired = false,
            },
        };

        var component = RenderComponent<ErDiagram>(p => p.Add(
            x => x.Layout, DiagramLayout.Compute(tables, relationships)));

        Assert.Contains("stroke-dasharray", component.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DbCardinality.OneToOne, "1:1")]
    [InlineData(DbCardinality.OneToMany, "1:N")]
    [InlineData(DbCardinality.ManyToMany, "N:M")]
    public void Popisek_kardinality_odpovida(DbCardinality cardinality, string expected) =>
        Assert.Equal(expected, ErDiagram.CardinalityLabel(cardinality));

    [Theory]
    [InlineData(DbDeleteBehavior.Cascade, "kaskada")]
    [InlineData(DbDeleteBehavior.SetNull, "setnull")]
    [InlineData(DbDeleteBehavior.SetDefault, "setnull")]
    [InlineData(DbDeleteBehavior.Restrict, "restrict")]
    [InlineData(DbDeleteBehavior.NoAction, "restrict")]
    public void Trida_hrany_odpovida_chovani(DbDeleteBehavior behavior, string expected) =>
        Assert.Equal(expected, ErDiagram.DeleteClass(behavior));

    [Theory]
    [InlineData(DiffSeverity.Error, "nalez-chyba")]
    [InlineData(DiffSeverity.Warning, "nalez-varovani")]
    [InlineData(DiffSeverity.Info, "")]
    [InlineData(null, "")]
    public void Trida_nalezu_odpovida_zavaznosti(DiffSeverity? severity, string expected) =>
        Assert.Equal(expected, ErDiagram.SeverityClass(severity));

    [Fact]
    public void Znacka_klice_rozlisuje_role()
    {
        Assert.Equal("PK", ErDiagram.KeyMark(Column(pk: true)));
        Assert.Equal("FK", ErDiagram.KeyMark(Column(fk: true)));
        Assert.Equal("PF", ErDiagram.KeyMark(Column(pk: true, fk: true)));
        Assert.Equal("", ErDiagram.KeyMark(Column()));
    }

    [Fact]
    public void Cisla_do_SVG_maji_vzdy_tecku() =>
        Assert.Equal("12.5", ErDiagram.Fmt(12.5));

    private static DbColumn Column(bool pk = false, bool fk = false) => new()
    {
        Name = "X",
        Ordinal = 1,
        StoreType = "int",
        IsPrimaryKey = pk,
        IsForeignKey = fk,
    };
}

/// <summary>Vykreslení detailu tabulky.</summary>
public class TableDetailTests : TestContext
{
    private static DbTable Table() => new()
    {
        Name = new DbObjectName("dbo", "Orders"),
        Comment = "Objednávky",
        EntityClrNames = ["Order"],
        DiscriminatorColumn = "Typ",
        RowCountEstimate = 4200,
        Columns =
        [
            new DbColumn
            {
                Name = "Id",
                Ordinal = 1,
                StoreType = "int",
                IsPrimaryKey = true,
                IsIdentity = true,
                ClrType = "System.Int32",
            },
            new DbColumn
            {
                Name = "CustomerId",
                Ordinal = 2,
                StoreType = "int",
                IsForeignKey = true,
                IsNullable = true,
            },
            new DbColumn
            {
                Name = "Total",
                Ordinal = 3,
                StoreType = "decimal(18,2)",
                IsComputed = true,
                ComputedSql = "A*B",
                DefaultValueSql = "0",
                IsConcurrencyToken = true,
                Collation = "Czech_CI_AS",
                Comment = "Celkem",
            },
        ],
        Indexes =
        [
            new DbIndex
            {
                Name = "IX_Orders",
                Columns = ["CustomerId"],
                IncludedColumns = ["Total"],
                FilterSql = "[Total] > 0",
                IsClustered = true,
                IsDescending = [true],
            },
        ],
        ForeignKeys =
        [
            new DbForeignKey
            {
                Name = "FK_Orders_Customers",
                Columns = ["CustomerId"],
                PrincipalTable = new DbObjectName("dbo", "Customers"),
                PrincipalColumns = ["Id"],
                DeleteBehavior = DbDeleteBehavior.Cascade,
                NavigationName = "Customer",
            },
        ],
    };

    [Fact]
    public void Bez_vybrane_tabulky_je_vyzva()
    {
        var component = RenderComponent<TableDetail>();

        Assert.Contains("Vyber tabulku", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Hlavicka_ukaze_odznaky_i_komentar()
    {
        var component = RenderComponent<TableDetail>(p => p.Add(x => x.Table, Table()));

        // Schéma je barevný prefix ve vlastním elementu, takže se v markupu
        // se jménem nespojí — čtou se odděleně.
        Assert.Contains("dbo", component.Markup, StringComparison.Ordinal);
        Assert.Contains("Orders", component.Markup, StringComparison.Ordinal);
        Assert.Equal("dbo.Orders", component.Find(".detail-hlavicka h2").TextContent.Trim());
        Assert.Contains("Objednávky", component.Markup, StringComparison.Ordinal);
        Assert.Contains("Order", component.Markup, StringComparison.Ordinal);
        Assert.Contains("TPH: Typ", component.Markup, StringComparison.Ordinal);
        Assert.Contains("4", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Pohled_a_vazebni_tabulka_maji_odznak()
    {
        var table = Table() with { IsView = true, IsJoinTable = true };

        var component = RenderComponent<TableDetail>(p => p.Add(x => x.Table, table));

        Assert.Contains("pohled", component.Markup, StringComparison.Ordinal);
        Assert.Contains("vazební N:M", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Sloupce_ukazou_typ_klic_i_poznamky()
    {
        var component = RenderComponent<TableDetail>(p => p.Add(x => x.Table, Table()));

        Assert.Contains("identity", component.Markup, StringComparison.Ordinal);
        Assert.Contains("computed: A*B", component.Markup, StringComparison.Ordinal);
        Assert.Contains("concurrency", component.Markup, StringComparison.Ordinal);
        Assert.Contains("Czech_CI_AS", component.Markup, StringComparison.Ordinal);
        Assert.Contains("Int32", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Nalezena_slova_zvyrazni_radek()
    {
        var highlighted = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CustomerId" };

        var component = RenderComponent<TableDetail>(p => p
            .Add(x => x.Table, Table())
            .Add(x => x.Highlighted, highlighted));

        Assert.Single(component.FindAll("tr.nalezeno"));
    }

    [Fact]
    public void Zalozka_indexu_ukaze_podrobnosti()
    {
        var component = RenderComponent<TableDetail>(p => p
            .Add(x => x.Table, Table())
            .Add(x => x.ActiveTab, DetailTab.Indexes));

        Assert.Contains("IX_Orders", component.Markup, StringComparison.Ordinal);
        Assert.Contains("INCLUDE (Total)", component.Markup, StringComparison.Ordinal);
        Assert.Contains("WHERE [Total] &gt; 0", component.Markup, StringComparison.Ordinal);
        Assert.Contains("clustered", component.Markup, StringComparison.Ordinal);
        Assert.Contains("sestupně", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Zalozka_cizich_klicu_umi_prejit_na_cil()
    {
        DbObjectName? vybrano = null;

        var component = RenderComponent<TableDetail>(p => p
            .Add(x => x.Table, Table())
            .Add(x => x.ActiveTab, DetailTab.ForeignKeys)
            .Add(x => x.OnSelect, (DbObjectName t) => vybrano = t));

        component.Find("button.odkaz").Click();

        Assert.Equal("Customers", vybrano!.Value.Name);
    }

    [Fact]
    public void Zalozka_odkazuje_sem_ukaze_prichozi_vazby()
    {
        var incoming = new[]
        {
            new DbRelationship
            {
                Id = "x",
                From = new DbObjectName("dbo", "OrderLines"),
                To = new DbObjectName("dbo", "Orders"),
                Cardinality = DbCardinality.OneToMany,
                FromColumns = ["OrderId"],
            },
        };

        var component = RenderComponent<TableDetail>(p => p
            .Add(x => x.Table, Table())
            .Add(x => x.ActiveTab, DetailTab.ReferencedBy)
            .Add(x => x.Incoming, incoming));

        Assert.Contains("OrderLines", component.Markup, StringComparison.Ordinal);
        Assert.Contains("1:N", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Prazdne_zalozky_maji_vysvetleni()
    {
        var prazdna = new DbTable { Name = new DbObjectName(null, "T") };

        foreach (var tab in new[] { DetailTab.Indexes, DetailTab.ForeignKeys, DetailTab.ReferencedBy })
        {
            var component = RenderComponent<TableDetail>(p => p
                .Add(x => x.Table, prazdna)
                .Add(x => x.ActiveTab, tab));

            Assert.Contains("prazdno", component.Markup, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Prepnuti_zalozky_se_ohlasi()
    {
        DetailTab? zvolena = null;

        var component = RenderComponent<TableDetail>(p => p
            .Add(x => x.Table, Table())
            .Add(x => x.OnTabChange, (DetailTab t) => zvolena = t));

        component.FindAll(".zalozky button").ElementAt(1).Click();

        Assert.Equal(DetailTab.Indexes, zvolena);
    }

    [Fact]
    public void Zalozka_dat_je_bez_opravneni_zakazana()
    {
        var component = RenderComponent<TableDetail>(p => p.Add(x => x.Table, Table()));

        var dataTab = component.FindAll(".zalozky button").ElementAt(4);

        Assert.True(dataTab.HasAttribute("disabled"));
    }

    [Fact]
    public void Nalezy_diffu_se_vypisou()
    {
        var findings = new[]
        {
            new DiffFinding
            {
                Kind = DiffKind.ColumnMissingInDatabase,
                Severity = DiffSeverity.Error,
                Table = new DbObjectName("dbo", "Orders"),
                Object = "Poznamka",
                Message = "Sloupec chybí.",
                ModelValue = "nvarchar(200)",
            },
            new DiffFinding
            {
                Kind = DiffKind.IndexMissingInModel,
                Severity = DiffSeverity.Warning,
                Table = new DbObjectName("dbo", "Orders"),
                Message = "Index navíc.",
            },
        };

        var component = RenderComponent<TableDetail>(p => p
            .Add(x => x.Table, Table())
            .Add(x => x.Findings, findings));

        Assert.Single(component.FindAll(".nalezy li.chyba"));
        Assert.Single(component.FindAll(".nalezy li.varovani"));
        Assert.Contains("nvarchar(200)", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Zkraceni_CLR_typu_nechá_jen_jmeno()
    {
        Assert.Equal("Int32", TableDetail.ShortClrType("System.Int32"));
        Assert.Equal("Int32", TableDetail.ShortClrType("Int32"));
        Assert.Equal("", TableDetail.ShortClrType(null));
        Assert.Equal("", TableDetail.ShortClrType(""));

        // Useknutí za poslední tečkou by u generického jména utrhlo nesmysl:
        // z „List<System.String>" by zbylo „String>".
        Assert.Equal("Int32?", TableDetail.ShortClrType("System.Int32?"));
        Assert.Equal("Byte[]", TableDetail.ShortClrType("System.Byte[]"));
        Assert.Equal(
            "List<String>",
            TableDetail.ShortClrType("System.Collections.Generic.List<System.String>"));
        Assert.Equal(
            "Dictionary<String, Int32>",
            TableDetail.ShortClrType("System.Collections.Generic.Dictionary<System.String, System.Int32>"));
    }

    [Fact]
    public void Znacka_klice_rozlisuje_role()
    {
        Assert.Equal("PK, FK", TableDetail.KeyMark(new DbColumn
        {
            Name = "X",
            StoreType = "int",
            IsPrimaryKey = true,
            IsForeignKey = true,
        }));

        Assert.Equal("", TableDetail.KeyMark(new DbColumn { Name = "X", StoreType = "int" }));
    }

    [Theory]
    [InlineData(DetailTab.Columns, "Sloupce")]
    [InlineData(DetailTab.Indexes, "Indexy")]
    [InlineData(DetailTab.ForeignKeys, "Cizí klíče")]
    [InlineData(DetailTab.ReferencedBy, "Odkazuje sem")]
    [InlineData(DetailTab.Data, "Data")]
    public void Popisky_zalozek(DetailTab tab, string expected) =>
        Assert.Equal(expected, TableDetail.TabLabel(tab));

    [Fact]
    public void Poznamky_sloupce_bez_vlastnosti_jsou_prazdne() =>
        Assert.Equal("", TableDetail.ColumnNotes(new DbColumn { Name = "X", StoreType = "int" }));

    [Fact]
    public void Poznamka_pocitaneho_sloupce_bez_vyrazu()
    {
        var column = new DbColumn { Name = "X", StoreType = "int", IsComputed = true };

        Assert.Equal("computed", TableDetail.ColumnNotes(column));
    }

    [Fact]
    public void Poznamky_indexu_bez_zvlastnosti_jsou_prazdne() =>
        Assert.Equal("", TableDetail.IndexNotes(new DbIndex { Name = "IX", Columns = ["A"] }));
}

/// <summary>Náhled dat.</summary>
public class DataNahledTests : TestContext
{
    [Fact]
    public void Data_se_nactou_hned_bez_tlacitka()
    {
        // Dřív tu bylo tlačítko „Načíst data". Mřížka si o data řekne sama, jakmile
        // je vidět — kliknutí navíc jen zdržovalo.
        DataQuery? dotaz = null;

        var component = RenderComponent<DataNahled>(p => p
            .Add(x => x.Table, Tabulka())
            .Add(x => x.OnLoad, (DataQuery q) => dotaz = q));

        Assert.NotNull(dotaz);
        Assert.Equal(0, dotaz.Page);
        Assert.Empty(component.FindAll("button.hlavni"));
    }

    [Fact]
    public void Prepnuti_tabulky_nacte_data_znovu()
    {
        var dotazy = new List<DataQuery>();

        var component = RenderComponent<DataNahled>(p => p
            .Add(x => x.Table, Tabulka("Zakaznici"))
            .Add(x => x.OnLoad, (DataQuery q) => dotazy.Add(q)));

        component.SetParametersAndRender(p => p.Add(x => x.Table, Tabulka("Objednavky")));

        Assert.Equal(2, dotazy.Count);
    }

    [Fact]
    public void Stejna_tabulka_data_znovu_nenacita()
    {
        var dotazy = new List<DataQuery>();

        var component = RenderComponent<DataNahled>(p => p
            .Add(x => x.Table, Tabulka("Zakaznici"))
            .Add(x => x.OnLoad, (DataQuery q) => dotazy.Add(q)));

        component.SetParametersAndRender(p => p.Add(x => x.Error, null));

        Assert.Single(dotazy);
    }

    [Fact]
    public void Hlavicky_se_vykresli_ze_schematu_uz_pred_daty()
    {
        // Bez toho by mřížka po dorazení dat poskočila.
        var component = RenderComponent<DataNahled>(p => p.Add(x => x.Table, Tabulka()));

        Assert.Contains("Id", component.Markup, StringComparison.Ordinal);
        Assert.Contains("Načítám data", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Prazdna_tabulka_ma_vysvetleni()
    {
        var component = RenderComponent<DataNahled>(p => p
            .Add(x => x.Table, Tabulka())
            .Add(x => x.Preview, new RowPreview { Columns = ["Id"], Rows = [], PageSize = 10 }));

        Assert.Contains("Tabulka je prázdná", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Radky_se_vykresli_vcetne_NULL()
    {
        var preview = new RowPreview
        {
            Columns = ["Id", "Nazev"],
            Rows = [["1", null]],
            PageSize = 50,
        };

        var component = RenderComponent<DataNahled>(p => p
            .Add(x => x.Table, Tabulka())
            .Add(x => x.Preview, preview));

        Assert.Contains("NULL", component.Markup, StringComparison.Ordinal);
        Assert.Single(component.FindAll("td.nullova"));
    }

    [Fact]
    public void Maskovane_sloupce_se_oznaci()
    {
        var preview = new RowPreview
        {
            Columns = ["Id", "Heslo"],
            MaskedColumns = ["Heslo"],
            Rows = [["1", "••••••"]],
            PageSize = 50,
        };

        var component = RenderComponent<DataNahled>(p => p
            .Add(x => x.Table, Tabulka())
            .Add(x => x.Preview, preview));

        Assert.Contains("Zamaskované sloupce: Heslo", component.Markup, StringComparison.Ordinal);
        Assert.Single(component.FindAll("th.maskovany"));
    }

    [Fact]
    public void Chyba_se_zobrazi_misto_dat()
    {
        var component = RenderComponent<DataNahled>(p => p.Add(x => x.Error, "Přístup odepřen."));

        Assert.Contains("Přístup odepřen.", component.Markup, StringComparison.Ordinal);
        Assert.Empty(component.FindAll("table"));
    }

    // ---------- stránkování ----------

    [Fact]
    public void Celkovy_pocet_radku_se_ukaze()
    {
        var component = Mrizka(Stranka(0, celkem: 1234));

        // Cestina.Cislo odděluje tisíce nedělitelnou mezerou, ne obyčejnou.
        Assert.Contains("1\u00a0234 řádků", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Bez_celkoveho_poctu_se_ukaze_aspon_stranka()
    {
        var preview = Stranka(2, celkem: null) with { HasMore = true };
        var component = Mrizka(preview);

        Assert.Contains("bez celkového počtu", component.Markup, StringComparison.Ordinal);
        Assert.Contains("Stránka 3", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Na_prvni_strance_je_predchozi_nedostupne()
    {
        var component = Mrizka(Stranka(0, celkem: 200));

        var tlacitka = component.FindAll(".strankovani button");

        Assert.True(tlacitka.ElementAt(0).HasAttribute("disabled"));
        Assert.False(tlacitka.ElementAt(2).HasAttribute("disabled"));
    }

    [Fact]
    public void Na_posledni_strance_je_dalsi_nedostupne()
    {
        var preview = Stranka(3, celkem: 200) with { PageCount = 4, HasMore = false };
        var tlacitka = Mrizka(preview).FindAll(".strankovani button");

        Assert.False(tlacitka.ElementAt(0).HasAttribute("disabled"));
        Assert.True(tlacitka.ElementAt(2).HasAttribute("disabled"));
    }

    [Fact]
    public void Klik_na_dalsi_posle_dalsi_stranku()
    {
        var dotazy = new List<DataQuery>();
        var component = Mrizka(Stranka(1, celkem: 500), dotazy);

        dotazy.Clear();
        component.FindAll(".strankovani button").ElementAt(2).Click();

        Assert.Equal(2, dotazy[^1].Page);
    }

    [Fact]
    public void Klik_na_prvni_stranku_se_vrati_na_zacatek()
    {
        var dotazy = new List<DataQuery>();
        var component = Mrizka(Stranka(5, celkem: 500), dotazy);

        dotazy.Clear();
        component.FindAll(".strankovani button").ElementAt(0).Click();

        Assert.Equal(0, dotazy[^1].Page);
    }

    [Fact]
    public void Klik_na_posledni_stranku_skoci_na_konec()
    {
        var dotazy = new List<DataQuery>();
        var preview = Stranka(0, celkem: 500) with { PageCount = 10, HasMore = true };
        var component = Mrizka(preview, dotazy);

        dotazy.Clear();
        component.FindAll(".strankovani button").ElementAt(3).Click();

        Assert.Equal(9, dotazy[^1].Page);
    }

    [Fact]
    public void Jedina_stranka_strankovani_nezobrazi()
    {
        var component = Mrizka(Stranka(0, celkem: 3));

        Assert.Empty(component.FindAll(".strankovani"));
    }

    [Fact]
    public void Velikost_stranky_jde_zmenit()
    {
        var dotazy = new List<DataQuery>();
        var component = Mrizka(Stranka(2, celkem: 500), dotazy);

        dotazy.Clear();
        component.Find(".data-ovladani select").Change("100");

        Assert.Equal(100, dotazy[^1].PageSize);

        // Po změně velikosti se čísla stránek posunou, takže se začíná od první.
        Assert.Equal(0, dotazy[^1].Page);
    }

    [Fact]
    public void Nesmyslna_velikost_stranky_se_ignoruje()
    {
        var dotazy = new List<DataQuery>();
        var component = Mrizka(Stranka(0, celkem: 500), dotazy);

        dotazy.Clear();
        component.Find(".data-ovladani select").Change("nesmysl");

        Assert.Empty(dotazy);
    }

    // ---------- řazení ----------

    [Fact]
    public void Klik_na_hlavicku_seradi_vzestupne()
    {
        var dotazy = new List<DataQuery>();
        var component = Mrizka(Stranka(0, celkem: 10), dotazy);

        dotazy.Clear();
        component.FindAll("th .razeni").ElementAt(1).Click();

        Assert.Equal("Nazev", dotazy[^1].SortColumn);
        Assert.False(dotazy[^1].SortDescending);
    }

    [Fact]
    public void Druhy_klik_otoci_smer_a_treti_razeni_zrusi()
    {
        var dotazy = new List<DataQuery>();
        var component = Mrizka(Stranka(0, celkem: 10), dotazy);

        component.FindAll("th .razeni").ElementAt(1).Click();
        component.FindAll("th .razeni").ElementAt(1).Click();

        Assert.True(dotazy[^1].SortDescending);

        component.FindAll("th .razeni").ElementAt(1).Click();

        Assert.Null(dotazy[^1].SortColumn);
    }

    [Fact]
    public void Razeni_podle_jineho_sloupce_zacne_vzestupne()
    {
        var dotazy = new List<DataQuery>();
        var component = Mrizka(Stranka(0, celkem: 10), dotazy);

        component.FindAll("th .razeni").ElementAt(1).Click();
        component.FindAll("th .razeni").ElementAt(1).Click();
        component.FindAll("th .razeni").ElementAt(0).Click();

        Assert.Equal("Id", dotazy[^1].SortColumn);
        Assert.False(dotazy[^1].SortDescending);
    }

    [Fact]
    public void Razeni_se_v_hlavicce_oznaci_sipkou()
    {
        var component = Mrizka(Stranka(0, celkem: 10));

        component.FindAll("th .razeni").ElementAt(0).Click();

        Assert.Contains("▴", component.Markup, StringComparison.Ordinal);
    }

    // ---------- filtrování ----------

    [Fact]
    public void Zapsany_filtr_se_posle_serveru()
    {
        var dotazy = new List<DataQuery>();
        var component = Mrizka(Stranka(3, celkem: 500), dotazy);

        dotazy.Clear();
        component.FindAll("tr.filtry input").ElementAt(1).Change("Adam");

        var filtr = Assert.Single(dotazy[^1].Filters);

        Assert.Equal("Nazev", filtr.Column);
        Assert.Equal("Adam", filtr.Value);

        // Po zafiltrování se čísla stránek posunou, takže se začíná od první.
        Assert.Equal(0, dotazy[^1].Page);
    }

    [Fact]
    public void Prazdny_filtr_se_neposila()
    {
        var dotazy = new List<DataQuery>();
        var component = Mrizka(Stranka(0, celkem: 10), dotazy);

        component.FindAll("tr.filtry input").ElementAt(0).Change("x");
        component.FindAll("tr.filtry input").ElementAt(0).Change("   ");

        Assert.Empty(dotazy[^1].Filters);
    }

    [Fact]
    public void Filtry_se_daji_zrusit_najednou()
    {
        var dotazy = new List<DataQuery>();
        var component = Mrizka(Stranka(0, celkem: 10), dotazy);

        component.FindAll("tr.filtry input").ElementAt(0).Change("x");
        component.FindAll("tr.filtry input").ElementAt(1).Change("y");

        Assert.Equal(2, dotazy[^1].Filters.Count);

        component.Find(".data-ovladani button.odkaz").Click();

        Assert.Empty(dotazy[^1].Filters);
    }

    [Fact]
    public void Aktivni_filtr_je_vizualne_odlisen()
    {
        var component = Mrizka(Stranka(0, celkem: 10));

        component.FindAll("tr.filtry input").ElementAt(0).Change("x");

        Assert.Single(component.FindAll("tr.filtry input.aktivni"));
    }

    [Fact]
    public void Bez_shody_filtru_to_mrizka_rekne()
    {
        var dotazy = new List<DataQuery>();
        var component = Mrizka(Stranka(0, celkem: 10), dotazy);

        component.FindAll("tr.filtry input").ElementAt(0).Change("nikdo");

        component.SetParametersAndRender(p => p.Add(
            x => x.Preview,
            new RowPreview { Columns = ["Id", "Nazev"], Rows = [], PageSize = 50, TotalRows = 0 }));

        Assert.Contains("neodpovídá žádný řádek", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Prepnuti_tabulky_zahodi_filtry_i_razeni()
    {
        // Sloupce jsou v každé tabulce jiné, takže filtr z předchozí nedává smysl.
        var dotazy = new List<DataQuery>();

        var component = RenderComponent<DataNahled>(p => p
            .Add(x => x.Table, Tabulka("Zakaznici"))
            .Add(x => x.Preview, Stranka(0, celkem: 10))
            .Add(x => x.OnLoad, (DataQuery q) => dotazy.Add(q)));

        component.FindAll("tr.filtry input").ElementAt(0).Change("x");
        component.FindAll("th .razeni").ElementAt(0).Click();

        component.SetParametersAndRender(p => p.Add(x => x.Table, Tabulka("Objednavky")));

        Assert.Empty(dotazy[^1].Filters);
        Assert.Null(dotazy[^1].SortColumn);
    }

    private IRenderedComponent<DataNahled> Mrizka(
        RowPreview preview,
        List<DataQuery>? dotazy = null) =>
        RenderComponent<DataNahled>(p => p
            .Add(x => x.Table, Tabulka())
            .Add(x => x.Preview, preview)
            .Add(x => x.OnLoad, (DataQuery q) => dotazy?.Add(q)));

    private static RowPreview Stranka(int stranka, long? celkem) => new()
    {
        Columns = ["Id", "Nazev"],
        Rows = [["1", "Adam"], ["2", "Bára"]],
        Page = stranka,
        PageSize = 50,
        TotalRows = celkem,
        PageCount = celkem is { } c ? Math.Max(1, (c + 49) / 50) : null,
        HasMore = celkem is { } total && ((stranka + 1L) * 50) < total,
    };

    private static DbTable Tabulka(string name = "Zakaznici") => new()
    {
        Name = new DbObjectName(null, name),
        Columns =
        [
            new DbColumn { Name = "Id", Ordinal = 1, StoreType = "int", IsPrimaryKey = true },
            new DbColumn { Name = "Nazev", Ordinal = 2, StoreType = "nvarchar" },
        ],
    };

    // ---------- označení změn ----------

    [Theory]
    [InlineData(ZmenaStav.Beze, "", "")]
    [InlineData(ZmenaStav.Pribylo, "zmena-pribylo", "+")]
    [InlineData(ZmenaStav.Ubylo, "zmena-ubylo", "−")]
    [InlineData(ZmenaStav.Zmeneno, "zmena-zmeneno", "~")]
    public void Diagram_ma_pro_kazdy_stav_tridu_i_znak(ZmenaStav stav, string trida, string znak)
    {
        Assert.Equal(trida, ErDiagram.StavTridy(stav));
        Assert.Equal(znak, ErDiagram.StavZnak(stav));
    }

    [Theory]
    [InlineData(ZmenaStav.Pribylo, "Přibylo")]
    [InlineData(ZmenaStav.Ubylo, "teď už není")]
    [InlineData(ZmenaStav.Zmeneno, "Změnilo se")]
    public void Diagram_vysvetli_kazdy_stav(ZmenaStav stav, string cast) =>
        Assert.Contains(cast, ErDiagram.StavPopis(stav)!, StringComparison.Ordinal);

    [Fact]
    public void Nezmeneny_stav_nema_vysvetleni()
    {
        Assert.Null(ErDiagram.StavPopis(ZmenaStav.Beze));
        Assert.Null(TableDetail.StavPopis(ZmenaStav.Beze));
    }

    [Theory]
    [InlineData(ZmenaStav.Beze, "", "")]
    [InlineData(ZmenaStav.Pribylo, "zmena-pribylo", "+")]
    [InlineData(ZmenaStav.Ubylo, "zmena-ubylo", "−")]
    [InlineData(ZmenaStav.Zmeneno, "zmena-zmeneno", "~")]
    public void Detail_ma_pro_kazdy_stav_tridu_i_znak(ZmenaStav stav, string trida, string znak)
    {
        Assert.Equal(trida, TableDetail.StavTridy(stav));
        Assert.Equal(znak, TableDetail.StavZnak(stav));
    }

    [Theory]
    [InlineData(ZmenaStav.Pribylo, "Přibylo")]
    [InlineData(ZmenaStav.Ubylo, "teď už není")]
    [InlineData(ZmenaStav.Zmeneno, "Změnilo se")]
    public void Detail_vysvetli_kazdy_stav(ZmenaStav stav, string cast) =>
        Assert.Contains(cast, TableDetail.StavPopis(stav)!, StringComparison.Ordinal);

    // ---------- databázová schémata ----------

    [Fact]
    public void Schema_se_pise_jako_prefix_s_teckou()
    {
        var tables = new[] { new DbTable { Name = new DbObjectName("prodej", "Objednavky") } };

        var component = RenderComponent<ErDiagram>(p => p
            .Add(x => x.Layout, DiagramLayout.Compute(tables, []))
            .Add(x => x.ShowSchemas, true));

        // V uzlu stojí „prodej.Objednavky", ne odznak vedle jména.
        Assert.Equal("prodej.", component.Find(".uzel-schema").TextContent);
        Assert.Contains("--schema-odstin", component.Find(".uzel-schema").GetAttribute("style")!, StringComparison.Ordinal);
    }

    [Fact]
    public void Kazde_schema_ma_svou_barvu()
    {
        var tables = new[]
        {
            new DbTable { Name = new DbObjectName("prodej", "Objednavky") },
            new DbTable { Name = new DbObjectName("sklad", "Produkty") },
        };

        var component = RenderComponent<ErDiagram>(p => p
            .Add(x => x.Layout, DiagramLayout.Compute(tables, []))
            .Add(x => x.ShowSchemas, true));

        var styly = component.FindAll(".uzel-schema").Select(e => e.GetAttribute("style")).ToList();

        Assert.Equal(2, styly.Count);
        Assert.Equal(2, styly.Distinct().Count());
    }

    [Fact]
    public void Uzel_ukaze_schema_kdyz_je_jich_vic()
    {
        // V databázi s dbo.Orders i sales.Orders by bez štítku nešlo poznat,
        // který uzel je který.
        var tables = new[]
        {
            new DbTable { Name = new DbObjectName("sales", "Orders") },
            new DbTable { Name = new DbObjectName("dbo", "Orders") },
        };

        var component = RenderComponent<ErDiagram>(p => p
            .Add(x => x.Layout, DiagramLayout.Compute(tables, []))
            .Add(x => x.ShowSchemas, true));

        Assert.Equal(2, component.FindAll(".uzel-schema").Count);
        Assert.Contains("sales", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void S_jedinym_schematem_se_stitek_neukazuje()
    {
        var tables = new[] { new DbTable { Name = new DbObjectName("dbo", "Orders") } };

        var component = RenderComponent<ErDiagram>(p => p
            .Add(x => x.Layout, DiagramLayout.Compute(tables, []))
            .Add(x => x.ShowSchemas, false));

        Assert.Empty(component.FindAll(".uzel-schema"));
    }

    [Fact]
    public void Tabulky_bez_schematu_stitek_nemaji()
    {
        var tables = new[] { new DbTable { Name = new DbObjectName(null, "Orders") } };

        var component = RenderComponent<ErDiagram>(p => p
            .Add(x => x.Layout, DiagramLayout.Compute(tables, []))
            .Add(x => x.ShowSchemas, true));

        Assert.Empty(component.FindAll(".uzel-schema"));
    }
}

/// <summary>Přehled rozdílů.</summary>
public class DiffPrehledTests : TestContext
{
    [Fact]
    public void Bez_dat_se_nacita()
    {
        var component = RenderComponent<DiffPrehled>();

        Assert.Contains("Načítám porovnání", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Shodna_schemata_maji_potvrzeni()
    {
        var component = RenderComponent<DiffPrehled>(p => p.Add(
            x => x.Diff, new SchemaDiff { Findings = [] }));

        Assert.Contains("shodují", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Nalezy_se_seskupi_podle_zavaznosti()
    {
        var component = RenderComponent<DiffPrehled>(p => p.Add(x => x.Diff, Vzorek.Diff()));

        Assert.Contains("1 chyb", component.Markup, StringComparison.Ordinal);
        Assert.Contains("1 varování", component.Markup, StringComparison.Ordinal);
        Assert.Single(component.FindAll("section.chyba"));
        Assert.Single(component.FindAll("section.varovani"));
    }

    [Fact]
    public void Klik_na_tabulku_se_ohlasi()
    {
        DbObjectName? vybrano = null;

        var component = RenderComponent<DiffPrehled>(p => p
            .Add(x => x.Diff, Vzorek.Diff())
            .Add(x => x.OnSelect, (DbObjectName t) => vybrano = t));

        component.FindAll("button.odkaz").ElementAt(0).Click();

        Assert.NotNull(vybrano);
    }

    [Fact]
    public void Nalez_bez_tabulky_se_vypise_jako_text()
    {
        var diff = new SchemaDiff
        {
            Findings =
            [
                new DiffFinding
                {
                    Kind = DiffKind.MigrationPending,
                    Severity = DiffSeverity.Error,
                    Object = "20260101_Init",
                    Message = "Migrace čeká na nasazení.",
                },
            ],
        };

        var component = RenderComponent<DiffPrehled>(p => p.Add(x => x.Diff, diff));

        Assert.Contains("20260101_Init", component.Markup, StringComparison.Ordinal);
        Assert.Empty(component.FindAll("button.odkaz"));
    }

    [Fact]
    public void Nalez_bez_objektu_pojmenuje_schema()
    {
        var diff = new SchemaDiff
        {
            Findings =
            [
                new DiffFinding
                {
                    Kind = DiffKind.MigrationPending,
                    Severity = DiffSeverity.Info,
                    Message = "Informace.",
                },
            ],
        };

        var component = RenderComponent<DiffPrehled>(p => p.Add(x => x.Diff, diff));

        Assert.Contains("schéma", component.Markup, StringComparison.Ordinal);
        Assert.Contains("Informace", component.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DiffSeverity.Error, "Chyby", "chyba")]
    [InlineData(DiffSeverity.Warning, "Varování", "varovani")]
    [InlineData(DiffSeverity.Info, "Informace", "info")]
    public void Popisky_zavaznosti(DiffSeverity severity, string label, string css)
    {
        Assert.Equal(label, DiffPrehled.SeverityLabel(severity));
        Assert.Equal(css, DiffPrehled.SeverityClass(severity));
    }

    [Fact]
    public void Mimo_historii_zustavaji_puvodni_zpravy()
    {
        var diff = new SchemaDiff
        {
            Findings =
            [
                new DiffFinding
                {
                    Kind = DiffKind.ColumnMissingInModel,
                    Severity = DiffSeverity.Warning,
                    Message = "Sloupec je v databázi, ale v modelu není.",
                    Table = new DbObjectName(null, "Orders"),
                },
            ],
        };

        var component = RenderComponent<DiffPrehled>(p => p.Add(x => x.Diff, diff));

        Assert.Contains("v modelu není", component.Markup, StringComparison.Ordinal);
        Assert.Contains("Databáze", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Nalezy_o_migracich_se_neprekladaji()
    {
        // Stav migrací se mezi dvěma snapshoty neporovnává — původní zpráva sedí.
        Assert.Null(DiffPrehled.HistorickaZprava(DiffKind.MigrationPending));
        Assert.Null(DiffPrehled.HistorickaZprava(DiffKind.MigrationOrphaned));
    }

    [Theory]
    [InlineData(DiffKind.TableMissingInModel, "Tabulka přibyla.")]
    [InlineData(DiffKind.TableMissingInDatabase, "Tabulka zanikla.")]
    [InlineData(DiffKind.ColumnMissingInDatabase, "Sloupec zanikl.")]
    [InlineData(DiffKind.ColumnTypeMismatch, "Sloupec změnil typ.")]
    [InlineData(DiffKind.ColumnNullabilityMismatch, "Sloupec změnil povinnost.")]
    [InlineData(DiffKind.ColumnLengthMismatch, "Sloupec změnil délku.")]
    [InlineData(DiffKind.ColumnDefaultMismatch, "Sloupec změnil výchozí hodnotu.")]
    [InlineData(DiffKind.IndexMissingInModel, "Index přibyl.")]
    [InlineData(DiffKind.IndexMissingInDatabase, "Index zanikl.")]
    [InlineData(DiffKind.IndexUniquenessMismatch, "Index změnil unikátnost.")]
    [InlineData(DiffKind.IndexColumnsMismatch, "Index změnil sloupce.")]
    [InlineData(DiffKind.PrimaryKeyMismatch, "Primární klíč se změnil.")]
    [InlineData(DiffKind.ForeignKeyMissingInModel, "Cizí klíč přibyl.")]
    [InlineData(DiffKind.ForeignKeyMissingInDatabase, "Cizí klíč zanikl.")]
    [InlineData(DiffKind.ForeignKeyDeleteBehaviorMismatch, "Cizí klíč změnil chování při mazání.")]
    [InlineData(DiffKind.ForeignKeyTargetMismatch, "Cizí klíč změnil cíl.")]
    public void Kazdy_druh_nalezu_ma_historicke_zneni(DiffKind kind, string expected) =>
        Assert.Equal(expected, DiffPrehled.HistorickaZprava(kind));

    [Fact]
    public void Zprava_bez_nalezu_je_chyba_argumentu()
    {
        var component = RenderComponent<DiffPrehled>(p => p.Add(x => x.Diff, new SchemaDiff { Findings = [] }));

        Assert.Throws<ArgumentNullException>(() => component.Instance.Zprava(null!));
    }

}
