using System.Globalization;
using Bunit;
using Bunit.TestDoubles;
using DbsViewer.Ui.Components;
using DbsViewer.Ui.Model;
using Microsoft.Extensions.DependencyInjection;

namespace DbsViewer.Tests.Ui;

/// <summary>
/// Pomůcka pro testy, které potřebují běžet v konkrétním jazyce. Kulturu vrátí zpátky,
/// i když test spadne — jinak by se rozbily testy, které poběží po něm.
/// </summary>
internal sealed class VJazyce : IDisposable
{
    private readonly CultureInfo _puvodni = CultureInfo.CurrentUICulture;

    public VJazyce(Language language) =>
        CultureInfo.CurrentUICulture = LanguageChoice.Culture(language);

    public void Dispose() => CultureInfo.CurrentUICulture = _puvodni;
}

/// <summary>Volba jazyka a její promítnutí do adresy.</summary>
public class LanguageChoiceTests
{
    [Fact]
    public void Bez_parametru_v_adrese_je_anglictina()
    {
        Assert.Equal(Language.English, LanguageChoice.FromUri("http://localhost/dbschema/"));
        Assert.Equal(Language.English, LanguageChoice.FromUri(null));
    }

    [Theory]
    [InlineData("http://localhost/dbschema/?lang=cs", Language.Czech)]
    [InlineData("http://localhost/dbschema/?lang=CS", Language.Czech)]
    [InlineData("http://localhost/dbschema/?lang=en", Language.English)]
    [InlineData("http://localhost/dbschema/?x=1&lang=cs&y=2", Language.Czech)]
    public void Jazyk_se_cte_z_adresy(string uri, Language expected) =>
        Assert.Equal(expected, LanguageChoice.FromUri(uri));

    [Theory]
    [InlineData("http://localhost/dbschema/?lang=klingon")]
    [InlineData("http://localhost/dbschema/?lang=")]
    [InlineData("http://localhost/dbschema/?lang")]
    [InlineData("http://localhost/dbschema/?jiny=cs")]
    public void Nesmyslny_jazyk_spadne_na_vychozi(string uri) =>
        // Adresa přichází od uživatele; překlep v ní nesmí prohlížečku shodit.
        Assert.Equal(Language.English, LanguageChoice.FromUri(uri));

    [Fact]
    public void Fragment_za_mrizkou_do_query_nepatri() =>
        Assert.Equal(Language.Czech, LanguageChoice.FromUri("http://localhost/?lang=cs#lang=en"));

    [Fact]
    public void Prepnuti_zachova_ostatni_parametry()
    {
        var cil = LanguageChoice.UrlFor("http://localhost/dbschema/?tabulka=Orders&lang=en", Language.Czech);

        Assert.Contains("tabulka=Orders", cil, StringComparison.Ordinal);
        Assert.Contains("lang=cs", cil, StringComparison.Ordinal);
        Assert.DoesNotContain("lang=en", cil, StringComparison.Ordinal);
    }

    [Fact]
    public void Prepnuti_bez_query_parametr_prida() =>
        Assert.Equal(
            "http://localhost/dbschema/?lang=cs",
            LanguageChoice.UrlFor("http://localhost/dbschema/", Language.Czech));

    [Fact]
    public void Prepnuti_z_prazdneho_query_neudela_dva_ampersandy() =>
        Assert.Equal(
            "http://localhost/dbschema/?lang=en",
            LanguageChoice.UrlFor("http://localhost/dbschema/?", Language.English));

    [Fact]
    public void Prepnuti_bez_adresy_je_chyba_argumentu() =>
        Assert.Throws<ArgumentNullException>(() => LanguageChoice.UrlFor(null!, Language.Czech));

    [Fact]
    public void Kody_a_jmena_jazyku_sedi()
    {
        Assert.Equal("cs", LanguageChoice.Code(Language.Czech));
        Assert.Equal("en", LanguageChoice.Code(Language.English));
        Assert.Equal("Čeština", LanguageChoice.NativeName(Language.Czech));
        Assert.Equal("English", LanguageChoice.NativeName(Language.English));
    }

    [Fact]
    public void Anglictina_pouziva_invariantni_kulturu()
    {
        // Neutrální .resx je anglický, takže se pro angličtinu žádná satelitní
        // assembly nehledá — a nemusí se kvůli ní stahovat data ICU.
        Assert.Equal(CultureInfo.InvariantCulture, LanguageChoice.Culture(Language.English));
        Assert.Equal("cs", LanguageChoice.Culture(Language.Czech).TwoLetterISOLanguageName);
    }
}

/// <summary>Skloňování a formátování čísel v obou jazycích.</summary>
public class PluralTests
{
    [Theory]
    [InlineData(0, "0 tabulek")]
    [InlineData(1, "1 tabulka")]
    [InlineData(2, "2 tabulky")]
    [InlineData(4, "4 tabulky")]
    [InlineData(5, "5 tabulek")]
    [InlineData(11, "11 tabulek")]
    public void Cestina_ma_tri_tvary(int pocet, string expected)
    {
        using var _ = new VJazyce(Language.Czech);

        Assert.Equal(expected, Counts.Tables(pocet));
    }

    [Theory]
    [InlineData(0, "0 tables")]
    [InlineData(1, "1 table")]
    [InlineData(2, "2 tables")]
    [InlineData(5, "5 tables")]
    public void Anglictina_ma_dva_tvary(int pocet, string expected)
    {
        using var _ = new VJazyce(Language.English);

        Assert.Equal(expected, Counts.Tables(pocet));
    }

    [Fact]
    public void Zaporny_pocet_se_chova_jako_kladny()
    {
        using var _ = new VJazyce(Language.Czech);

        Assert.Equal("tabulka", Plural.Form(-1, "tabulka", "tabulky", "tabulek"));
    }

    [Fact]
    public void Cestina_oddeluje_tisice_nedelitelnou_mezerou()
    {
        using var _ = new VJazyce(Language.Czech);

        Assert.Equal("1 234 567", Plural.Number(1234567));
        Assert.Equal("-4 200", Plural.Number(-4200));
        Assert.Equal("999", Plural.Number(999));
        Assert.Equal("0", Plural.Number(0));
    }

    [Fact]
    public void Anglictina_oddeluje_tisice_carkou()
    {
        using var _ = new VJazyce(Language.English);

        Assert.Equal("1,234,567", Plural.Number(1234567));
        Assert.Equal("999", Plural.Number(999));
    }

    [Fact]
    public void Formatovani_nezavisi_na_kulture_stroje()
    {
        // Kdyby se použil formát „N0", výsledek by měl čárku v en-US a tečku v de-DE.
        // Přesně na tom selhalo první sestavení na CI, kde běží invariantní kultura.
        using var _ = new VJazyce(Language.Czech);

        var puvodni = CultureInfo.CurrentCulture;

        try
        {
            foreach (var kultura in new[] { "en-US", "cs-CZ", "de-DE" })
            {
                CultureInfo.CurrentCulture = new CultureInfo(kultura);

                Assert.Equal("1 234 567", Plural.Number(1234567));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = puvodni;
        }
    }

    [Fact]
    public void Vsechny_pocitane_fraze_daji_neprazdny_text()
    {
        // Chybějící překlad v satelitní assembly se tiše nahradí angličtinou, takže
        // prázdný výsledek je jediné, co jde takhle hromadně odhalit.
        foreach (var jazyk in new[] { Language.English, Language.Czech })
        {
            using var _ = new VJazyce(jazyk);

            foreach (var fraze in new[]
            {
                Counts.Tables(2), Counts.TableForm(2), Counts.ViewForm(2),
                Counts.Columns(2), Counts.ColumnForm(2),
                Counts.Relationships(2), Counts.RelationshipForm(2), Counts.IndexForm(2),
                Counts.Rows(2), Counts.RowForm(2),
                Counts.Errors(2), Counts.Warnings(2),
                Counts.AppliedMigrations(2), Counts.JoinTables(2), Counts.Items(2),
            })
            {
                Assert.False(string.IsNullOrWhiteSpace(fraze));
            }
        }
    }
}

/// <summary>
/// Přepínač jazyka. Podstatné je, že samotná volba v nabídce ještě nic nepřepne —
/// mezi ní a znovunačtením stojí okno s varováním.
/// </summary>
public class PrepinacJazykaTests : TestContext
{
    private IRenderedComponent<PrepinacJazyka> Prepinac(string uri = "http://localhost/dbschema/")
    {
        Services.GetRequiredService<FakeNavigationManager>().NavigateTo(uri);

        return RenderComponent<PrepinacJazyka>();
    }

    private FakeNavigationManager Navigace => Services.GetRequiredService<FakeNavigationManager>();

    [Fact]
    public void Nabidka_ukazuje_oba_jazyky_v_nich_samotnych()
    {
        var volby = Prepinac().FindAll("select.jazyky option").Select(o => o.TextContent.Trim()).ToList();

        Assert.Equal(["English", "Čeština"], volby);
    }

    [Fact]
    public void Nabidka_ma_vybrany_jazyk_z_adresy()
    {
        var component = Prepinac("http://localhost/dbschema/?lang=cs");

        Assert.Equal("cs", component.Find("select.jazyky").GetAttribute("value"));
    }

    [Fact]
    public void Bez_volby_zadne_okno_neni() =>
        Assert.Empty(Prepinac().FindAll(".modal"));

    [Fact]
    public void Volba_jazyka_otevre_okno_ale_jeste_neprepne()
    {
        var component = Prepinac();
        var pred = Navigace.Uri;

        component.Find("select.jazyky").Change("cs");

        Assert.Single(component.FindAll(".modal"));
        Assert.Contains("Čeština", component.Markup, StringComparison.Ordinal);

        // Dokud uživatel nepotvrdí, adresa se nemění a o rozdělanou práci nepřijde.
        Assert.Equal(pred, Navigace.Uri);
    }

    [Fact]
    public void Okno_vysvetli_ze_se_prohlizecka_nacte_znovu()
    {
        var component = Prepinac();

        component.Find("select.jazyky").Change("cs");

        Assert.Contains("reload", component.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Volba_uz_beziciho_jazyka_okno_neotevre()
    {
        var component = Prepinac();

        component.Find("select.jazyky").Change("en");

        Assert.Empty(component.FindAll(".modal"));
    }

    [Fact]
    public void Potvrzeni_prepne_adresu_na_novy_jazyk()
    {
        var component = Prepinac();

        component.Find("select.jazyky").Change("cs");
        component.Find(".modal-tlacitka button.hlavni").Click();

        Assert.Contains("lang=cs", Navigace.Uri, StringComparison.Ordinal);
        Assert.Empty(component.FindAll(".modal"));
    }

    [Fact]
    public void Zruseni_okno_zavre_a_adresu_nechá_byt()
    {
        var component = Prepinac();
        var pred = Navigace.Uri;

        component.Find("select.jazyky").Change("cs");
        component.FindAll(".modal-tlacitka button").First(b => !b.ClassList.Contains("hlavni")).Click();

        Assert.Empty(component.FindAll(".modal"));
        Assert.Equal(pred, Navigace.Uri);
    }

    [Fact]
    public void Kliknuti_mimo_okno_ho_zavre()
    {
        var component = Prepinac();

        component.Find("select.jazyky").Change("cs");
        component.Find(".modal-pozadi").Click();

        Assert.Empty(component.FindAll(".modal"));
    }
}
