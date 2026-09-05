using Bunit;
using DbsViewer.Ui.Components;
using DbsViewer.Ui.Model;

namespace DbsViewer.Tests.Ui;

/// <summary>
/// Úpravy a mazání v mřížce. Podstatné je, že prohlížečka nenabídne to, co by server
/// odmítl: řádek bez jednoznačného klíče, sloupec, který se měnit nesmí, ani zápis,
/// který konfigurace nepovolila.
/// </summary>
public class DataEditTests : TestContext
{
    private static DbTable Tabulka(bool sKlicem = true, bool isView = false) => new()
    {
        Name = new DbObjectName(null, "Zakaznici"),
        IsView = isView,
        Columns =
        [
            new DbColumn { Name = "Id", Ordinal = 1, StoreType = "int", IsPrimaryKey = sKlicem },
            new DbColumn { Name = "Email", Ordinal = 2, StoreType = "nvarchar(200)" },
            new DbColumn { Name = "Jmeno", Ordinal = 3, StoreType = "nvarchar(200)", IsNullable = true },
            new DbColumn { Name = "Celkem", Ordinal = 4, StoreType = "decimal(18, 2)", IsComputed = true },
        ],
        PrimaryKey = sKlicem ? new DbPrimaryKey { Columns = ["Id"] } : null,
    };

    private static RowPreview Nahled(params string[] masked) => new()
    {
        Columns = ["Id", "Email", "Jmeno", "Celkem"],
        MaskedColumns = masked,
        Rows =
        [
            ["1", "prvni@x.cz", "První", "10"],
            ["2", "druhy@x.cz", null, "20"],
        ],
        PageSize = 50,
        TotalRows = 2,
    };

    /// <summary>Mřížka se zapnutými úpravami. Zápisy se sbírají do seznamů.</summary>
    private IRenderedComponent<DataNahled> Mrizka(
        List<DataUpdate>? updates = null,
        List<DataDelete>? deletes = null,
        string? chyba = null,
        DbTable? table = null,
        RowPreview? preview = null,
        bool canEdit = true,
        bool canDelete = true,
        bool canInsert = false,
        List<DataInsert>? inserts = null,
        List<DataQuery>? loads = null) =>
        RenderComponent<DataNahled>(p => p
            .Add(x => x.Table, table ?? Tabulka())
            .Add(x => x.Preview, preview ?? Nahled())
            .Add(x => x.CanEdit, canEdit)
            .Add(x => x.CanDelete, canDelete)
            .Add(x => x.OnLoad, (DataQuery q) => loads?.Add(q))
            .Add(x => x.OnUpdate, (DataUpdate u) =>
            {
                updates?.Add(u);
                return Task.FromResult(chyba);
            })
            .Add(x => x.OnDelete, (DataDelete d) =>
            {
                deletes?.Add(d);
                return Task.FromResult(chyba);
            })
            .Add(x => x.CanInsert, canInsert)
            .Add(x => x.OnInsert, canInsert || inserts is not null
                ? (DataInsert i) =>
                {
                    inserts?.Add(i);
                    return Task.FromResult(chyba);
                }
                : null));

    /// <summary>Tlačítko v prvním řádku podle popisku.</summary>
    private static void Klikni(IRenderedComponent<DataNahled> component, string popisek, int radek = 0)
    {
        var bunka = component.FindAll("td.akce").ElementAt(radek);
        var tlacitko = bunka.QuerySelectorAll("button").First(b => b.TextContent.Trim() == popisek);

        tlacitko.Click();
    }

    // ---------- kdy se zapisovat dá ----------

    [Fact]
    public void Se_zapnutymi_upravami_pribude_sloupec_s_tlacitky()
    {
        var component = Mrizka();

        Assert.Equal(2, component.FindAll("td.akce").Count);
        Assert.Contains("Upravit", component.Markup, StringComparison.Ordinal);
        Assert.Contains("Smazat", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Bez_povoleni_serveru_zadna_tlacitka_nejsou()
    {
        var component = Mrizka(canEdit: false, canDelete: false);

        Assert.Empty(component.FindAll("td.akce"));
    }

    [Fact]
    public void Bez_obsluhy_zapisu_se_tlacitka_nenabidnou()
    {
        var component = RenderComponent<DataNahled>(p => p
            .Add(x => x.Table, Tabulka())
            .Add(x => x.Preview, Nahled())
            .Add(x => x.CanEdit, true)
            .Add(x => x.CanDelete, true));

        Assert.Empty(component.FindAll("td.akce"));
    }

    [Fact]
    public void Tabulka_bez_klice_se_neupravuje_a_rekne_proc()
    {
        var component = Mrizka(table: Tabulka(sKlicem: false));

        Assert.Empty(component.FindAll("td.akce"));
        Assert.Contains("tabulka nemá primární klíč", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Zamaskovany_klic_zapis_znemozni()
    {
        var component = Mrizka(preview: Nahled("Id"));

        Assert.Empty(component.FindAll("td.akce"));
        Assert.Contains("není v mřížce celý čitelný", component.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, false, "pohled se přes prohlížečku neupravuje")]
    [InlineData(false, false, "tabulka nemá primární klíč")]
    [InlineData(false, true, "primární klíč není v mřížce celý čitelný")]
    public void Duvod_proc_se_nezapisuje(bool isView, bool sKlicem, string expected)
    {
        var table = Tabulka(sKlicem: sKlicem || isView, isView: isView);

        Assert.Equal(expected, DataNahled.DuvodBezKlice(table));
    }

    // ---------- úprava ----------

    [Fact]
    public void Upravit_prepne_radek_do_policek()
    {
        var component = Mrizka();

        Klikni(component, "Upravit");

        // Klíč ani počítaný sloupec políčko nedostanou.
        Assert.Equal(2, component.FindAll("tr.upravuje-se input.hodnota").Count);
        Assert.Equal(2, component.FindAll("tr.upravuje-se td.neupravitelna").Count);
    }

    [Fact]
    public void Nezmenitelny_sloupec_vysvetli_proc()
    {
        var component = Mrizka();

        Klikni(component, "Upravit");

        var bunky = component.FindAll("tr.upravuje-se td.neupravitelna");

        Assert.Equal("je součástí primárního klíče", bunky.ElementAt(0).GetAttribute("title"));
        Assert.Equal("je počítaný", bunky.ElementAt(1).GetAttribute("title"));
    }

    [Fact]
    public void Uklada_se_klic_a_jen_zmenene_sloupce()
    {
        var updates = new List<DataUpdate>();
        var component = Mrizka(updates);

        Klikni(component, "Upravit");
        component.FindAll("tr.upravuje-se input.hodnota").ElementAt(0).Change("novy@x.cz");
        Klikni(component, "Uložit");

        var update = Assert.Single(updates);

        Assert.Equal("Id", Assert.Single(update.Key).Column);
        Assert.Equal("1", update.Key[0].Value);

        var zmena = Assert.Single(update.Values);

        Assert.Equal("Email", zmena.Column);
        Assert.Equal("novy@x.cz", zmena.Value);
    }

    [Fact]
    public void Ulozeni_bez_zmeny_server_neobtezuje()
    {
        var updates = new List<DataUpdate>();
        var component = Mrizka(updates);

        Klikni(component, "Upravit");
        Klikni(component, "Uložit");

        Assert.Empty(updates);
        Assert.Empty(component.FindAll("tr.upravuje-se"));
    }

    [Fact]
    public void Zrusena_uprava_nic_neposle()
    {
        var updates = new List<DataUpdate>();
        var component = Mrizka(updates);

        Klikni(component, "Upravit");
        component.FindAll("input.hodnota").ElementAt(0).Change("jiny@x.cz");
        Klikni(component, "Zrušit");

        Assert.Empty(updates);
        Assert.Empty(component.FindAll("tr.upravuje-se"));
    }

    [Fact]
    public void Po_ulozeni_se_stranka_nacte_znovu()
    {
        var loads = new List<DataQuery>();
        var component = Mrizka(loads: loads);

        // První načtení proběhlo při vykreslení.
        Assert.Single(loads);

        Klikni(component, "Upravit");
        component.FindAll("input.hodnota").ElementAt(0).Change("novy@x.cz");
        Klikni(component, "Uložit");

        Assert.Equal(2, loads.Count);
    }

    [Fact]
    public void Hodnotu_jde_prepnout_na_NULL()
    {
        var updates = new List<DataUpdate>();
        var component = Mrizka(updates);

        Klikni(component, "Upravit");
        component.Find("input[type=checkbox]").Change(true);
        Klikni(component, "Uložit");

        var zmena = Assert.Single(Assert.Single(updates).Values);

        Assert.Equal("Jmeno", zmena.Column);
        Assert.Null(zmena.Value);
    }

    [Fact]
    public void Radek_s_NULL_zacina_zaskrtnuty_a_da_se_vratit()
    {
        var updates = new List<DataUpdate>();
        var component = Mrizka(updates);

        // Druhý řádek má ve sloupci Jmeno NULL.
        Klikni(component, "Upravit", radek: 1);

        Assert.True(component.Find("input[type=checkbox]").HasAttribute("checked"));

        component.Find("input[type=checkbox]").Change(false);
        Klikni(component, "Uložit", radek: 1);

        var zmena = Assert.Single(Assert.Single(updates).Values);

        Assert.Equal("", zmena.Value);
    }

    // ---------- mazání ----------

    [Fact]
    public void Mazani_se_potvrzuje_druhym_kliknutim()
    {
        var deletes = new List<DataDelete>();
        var component = Mrizka(deletes: deletes);

        Klikni(component, "Smazat");

        Assert.Empty(deletes);
        Assert.Contains("Smazat?", component.Markup, StringComparison.Ordinal);
        Assert.Single(component.FindAll("tr.maze-se"));

        Klikni(component, "Ano");

        Assert.Equal("1", Assert.Single(Assert.Single(deletes).Key).Value);
    }

    [Fact]
    public void Odmitnute_potvrzeni_radek_nechá_být()
    {
        var deletes = new List<DataDelete>();
        var component = Mrizka(deletes: deletes);

        Klikni(component, "Smazat");
        Klikni(component, "Ne");

        Assert.Empty(deletes);
        Assert.Empty(component.FindAll("tr.maze-se"));
    }

    [Fact]
    public void Rozepsana_uprava_a_cekajici_mazani_se_nepotkaji()
    {
        var component = Mrizka();

        Klikni(component, "Upravit");
        Klikni(component, "Smazat", radek: 1);

        Assert.Empty(component.FindAll("tr.upravuje-se"));
        Assert.Single(component.FindAll("tr.maze-se"));
    }

    // ---------- chyby ----------

    [Fact]
    public void Chyba_zapisu_nechá_policka_otevrena()
    {
        var component = Mrizka(chyba: "Databáze zápis odmítla: cizí klíč.");

        Klikni(component, "Upravit");
        component.FindAll("input.hodnota").ElementAt(0).Change("novy@x.cz");
        Klikni(component, "Uložit");

        Assert.Contains("cizí klíč", component.Markup, StringComparison.Ordinal);
        Assert.Single(component.FindAll("tr.upravuje-se"));
    }

    [Fact]
    public void Hlasku_o_chybe_jde_zavrit()
    {
        var component = Mrizka(chyba: "Nepovedlo se to.");

        Klikni(component, "Smazat");
        Klikni(component, "Ano");

        Assert.Single(component.FindAll("p.zapis-chyba"));

        component.Find("p.zapis-chyba button").Click();

        Assert.Empty(component.FindAll("p.zapis-chyba"));
    }

    [Fact]
    public void Radek_bez_hodnot_klice_se_nemaze()
    {
        // Stránka bez sloupce s klíčem: schéma klíč zná, ale v mřížce jeho hodnota není.
        var preview = new RowPreview
        {
            Columns = ["Email", "Jmeno"],
            Rows = [["prvni@x.cz", "První"]],
            PageSize = 50,
        };

        var deletes = new List<DataDelete>();
        var component = Mrizka(deletes: deletes, preview: preview);

        Klikni(component, "Smazat");
        Klikni(component, "Ano");

        Assert.Empty(deletes);
        Assert.Contains("jednoznačně určit", component.Markup, StringComparison.Ordinal);
    }

    // ---------- vkládání ----------

    /// <summary>Tlačítko nad mřížkou podle popisku.</summary>
    private static void KlikniNahore(IRenderedComponent<DataNahled> component, string popisek) =>
        component.FindAll("button").First(b => b.TextContent.Trim() == popisek).Click();

    [Fact]
    public void Bez_povoleneho_vkladani_se_novy_radek_nenabizi()
    {
        Assert.DoesNotContain("Nový řádek", Mrizka().Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Novy_radek_se_otevre_nad_daty()
    {
        // Na konci mřížky by se stránkou padesáti řádků skončil pod okrajem okna
        // a kliknutí by vypadalo, že se nic nestalo.
        var component = Mrizka(canInsert: true);

        KlikniNahore(component, "+ Nový řádek");

        var radky = component.FindAll("tbody tr");

        Assert.Equal(3, radky.Count);
        Assert.Contains("novy", radky.ElementAt(0).ClassName ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void Vlozeni_posle_jen_vyplnene_sloupce()
    {
        // Nevyplněný sloupec se neposílá, aby se uplatnila výchozí hodnota z databáze.
        var inserts = new List<DataInsert>();
        var component = Mrizka(canInsert: true, inserts: inserts);

        KlikniNahore(component, "+ Nový řádek");

        var radek = component.FindAll("tbody tr").ElementAt(0);
        radek.QuerySelectorAll("input.hodnota").ElementAt(1).Change("novy@x.cz");

        KlikniNahore(component, "Vložit");

        var insert = Assert.Single(inserts);
        var hodnota = Assert.Single(insert.Values);

        Assert.Equal("Email", hodnota.Column);
        Assert.Equal("novy@x.cz", hodnota.Value);
    }

    [Fact]
    public void Prazdny_novy_radek_se_neposila()
    {
        var inserts = new List<DataInsert>();
        var component = Mrizka(canInsert: true, inserts: inserts);

        KlikniNahore(component, "+ Nový řádek");
        KlikniNahore(component, "Vložit");

        Assert.Empty(inserts);
        Assert.Contains("ani jeden sloupec", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Do_pohledu_se_novy_radek_nenabizi()
    {
        var component = Mrizka(canInsert: true, table: Tabulka(isView: true));

        Assert.DoesNotContain("Nový řádek", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Vkladat_jde_i_do_tabulky_bez_klice()
    {
        // Bez klíče se řádek nedá upravit ani smazat, ale vložit se dá — INSERT žádný
        // existující řádek neadresuje.
        var component = Mrizka(canInsert: true, table: Tabulka(sKlicem: false));

        Assert.Contains("Nový řádek", component.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(">Upravit<", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Pocitany_sloupec_se_v_novem_radku_vyplnit_neda()
    {
        var component = Mrizka(canInsert: true);

        KlikniNahore(component, "+ Nový řádek");

        var radek = component.FindAll("tbody tr").ElementAt(0);

        // Id, Email a Jmeno mají políčko, Celkem je počítaný a má jen pomlčku.
        Assert.Equal(3, radek.QuerySelectorAll("input.hodnota").Length);
        Assert.Single(radek.QuerySelectorAll("td.neupravitelna"));
    }

    [Fact]
    public void Neuspesne_vlozeni_nechá_radek_otevreny()
    {
        var component = Mrizka(canInsert: true, inserts: [], chyba: "Databáze zápis odmítla");

        KlikniNahore(component, "+ Nový řádek");
        component.FindAll("tbody tr").ElementAt(0)
            .QuerySelectorAll("input.hodnota").ElementAt(1).Change("novy@x.cz");
        KlikniNahore(component, "Vložit");

        Assert.Contains("Databáze zápis odmítla", component.Markup, StringComparison.Ordinal);
        Assert.Equal(3, component.FindAll("tbody tr").Count);
    }

    [Fact]
    public void Policko_noveho_radku_ukaze_co_se_stane_bez_vyplneni()
    {
        // Prázdné políčko se do INSERT nedostane, takže se uplatní výchozí hodnota
        // z databáze — a ta musí být vidět, jinak uživatel netuší, co vloží.
        var tabulka = Tabulka();
        var sDefaultem = tabulka with
        {
            Columns =
            [
                tabulka.Columns[0],
                tabulka.Columns[1] with { DefaultValueSql = "'nikdo@x.cz'" },
                tabulka.Columns[2],
                tabulka.Columns[3],
            ],
        };

        var component = Mrizka(canInsert: true, table: sDefaultem);

        KlikniNahore(component, "+ Nový řádek");

        var policka = component.FindAll("tbody tr").ElementAt(0).QuerySelectorAll("input.hodnota");

        Assert.Equal("'nikdo@x.cz'", policka.ElementAt(1).GetAttribute("placeholder"));
        Assert.Equal("NULL", policka.ElementAt(2).GetAttribute("placeholder"));
        Assert.Equal("", policka.ElementAt(0).GetAttribute("placeholder"));
    }

    [Fact]
    public void Zruseni_noveho_radku_ho_zavre()
    {
        var component = Mrizka(canInsert: true);

        KlikniNahore(component, "+ Nový řádek");
        KlikniNahore(component, "Zrušit");

        Assert.Equal(2, component.FindAll("tbody tr").Count);
    }
}
