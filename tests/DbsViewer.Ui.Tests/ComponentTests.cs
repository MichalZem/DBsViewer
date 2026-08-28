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
        Assert.Single(component.FindAll(".hrana.zvyraznena"));
    }

    [Fact]
    public void Klik_na_uzel_ohlasi_vyber()
    {
        DbObjectName? vybrano = null;

        var component = RenderComponent<ErDiagram>(p => p
            .Add(x => x.Layout, Layout())
            .Add(x => x.OnSelect, (DbObjectName t) => vybrano = t));

        component.FindAll(".uzel")[0].Click();

        Assert.NotNull(vybrano);
    }

    [Fact]
    public void Prepinac_uzlu_ohlasi_rozbaleni()
    {
        DbObjectName? prepnuto = null;

        var component = RenderComponent<ErDiagram>(p => p
            .Add(x => x.Layout, Layout())
            .Add(x => x.OnToggleExpand, (DbObjectName t) => prepnuto = t));

        component.FindAll(".uzel-prepinac")[0].Click();

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

        component.FindAll(".diagram-ovladani button")[1].Click();
        Assert.Contains("91 %", component.Markup, StringComparison.Ordinal);

        component.FindAll(".diagram-ovladani button")[2].Click();
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
        var plus = component.FindAll(".diagram-ovladani button")[0];

        for (var i = 0; i < 20; i++)
        {
            component.FindAll(".diagram-ovladani button")[0].Click();
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

        Assert.Contains("dbo.Orders", component.Markup, StringComparison.Ordinal);
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

        component.FindAll(".zalozky button")[1].Click();

        Assert.Equal(DetailTab.Indexes, zvolena);
    }

    [Fact]
    public void Zalozka_dat_je_bez_opravneni_zakazana()
    {
        var component = RenderComponent<TableDetail>(p => p.Add(x => x.Table, Table()));

        var dataTab = component.FindAll(".zalozky button")[4];

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
    public void Bez_dat_je_vyzva_k_nacteni()
    {
        var component = RenderComponent<DataNahled>();

        Assert.Contains("Načíst data", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Klik_na_nacteni_se_ohlasi()
    {
        var nacteno = false;

        var component = RenderComponent<DataNahled>(p => p.Add(x => x.OnLoad, () => nacteno = true));

        component.Find("button.hlavni").Click();

        Assert.True(nacteno);
    }

    [Fact]
    public void Chyba_se_zobrazi_misto_dat()
    {
        var component = RenderComponent<DataNahled>(p => p.Add(x => x.Error, "Přístup odepřen."));

        Assert.Contains("Přístup odepřen.", component.Markup, StringComparison.Ordinal);
        Assert.Empty(component.FindAll("button.hlavni"));
    }

    [Fact]
    public void Prazdna_tabulka_ma_vysvetleni()
    {
        var component = RenderComponent<DataNahled>(p => p.Add(
            x => x.Preview, new RowPreview { Columns = ["Id"], Rows = [], Limit = 10 }));

        Assert.Contains("Tabulka je prázdná", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Radky_se_vykresli_vcetne_NULL()
    {
        var preview = new RowPreview
        {
            Columns = ["Id", "Nazev"],
            Rows = [["1", null]],
            Limit = 100,
        };

        var component = RenderComponent<DataNahled>(p => p.Add(x => x.Preview, preview));

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
            Limit = 100,
        };

        var component = RenderComponent<DataNahled>(p => p.Add(x => x.Preview, preview));

        Assert.Contains("Zamaskované sloupce: Heslo", component.Markup, StringComparison.Ordinal);
        Assert.Single(component.FindAll("th.maskovany"));
    }

    [Fact]
    public void Oriznuty_vysledek_to_rekne()
    {
        var preview = new RowPreview
        {
            Columns = ["Id"],
            Rows = [["1"], ["2"]],
            Limit = 2,
            IsTruncated = true,
        };

        var component = RenderComponent<DataNahled>(p => p.Add(x => x.Preview, preview));

        Assert.Contains("omezeno na 2", component.Markup, StringComparison.Ordinal);
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

        component.FindAll("button.odkaz")[0].Click();

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
}
