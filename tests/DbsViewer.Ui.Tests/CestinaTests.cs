using DbsViewer.Ui.Model;

namespace DbsViewer.Tests.Ui;

/// <summary>Skloňování podle počtu. Bez něj by UI psalo „1 tabulek“.</summary>
public class CestinaTests
{
    [Theory]
    [InlineData(0, "0 tabulek")]
    [InlineData(1, "1 tabulka")]
    [InlineData(2, "2 tabulky")]
    [InlineData(4, "4 tabulky")]
    [InlineData(5, "5 tabulek")]
    [InlineData(11, "11 tabulek")]
    [InlineData(21, "21 tabulek")]
    public void Tabulky_se_sklonuji(int pocet, string expected) =>
        Assert.Equal(expected, Cestina.Tabulky(pocet));

    [Theory]
    [InlineData(0, "0 vazeb")]
    [InlineData(1, "1 vazba")]
    [InlineData(3, "3 vazby")]
    [InlineData(9, "9 vazeb")]
    public void Vazby_se_sklonuji(int pocet, string expected) =>
        Assert.Equal(expected, Cestina.Vazby(pocet));

    [Theory]
    [InlineData(0, "0 řádků")]
    [InlineData(1, "1 řádek")]
    [InlineData(2, "2 řádky")]
    [InlineData(100, "100 řádků")]
    public void Radky_se_sklonuji(long pocet, string expected) =>
        Assert.Equal(expected, Cestina.Radky(pocet));

    [Theory]
    [InlineData(0, "0")]
    [InlineData(7, "7")]
    [InlineData(999, "999")]
    [InlineData(1000, "1 000")]
    [InlineData(12345, "12 345")]
    [InlineData(1234567, "1 234 567")]
    [InlineData(-4200, "-4 200")]
    public void Cislo_ma_nedelitelnou_mezeru_po_tisicich(long hodnota, string expected) =>
        Assert.Equal(expected, Cestina.Cislo(hodnota));

    [Fact]
    public void Formatovani_neobsahuje_kulturni_oddelovace()
    {
        // Kdyby se použil formát „N0“, výsledek by měl čárku v en-US a tečku v de-DE.
        // Přesně na tom selhalo první sestavení na CI, kde běží invariantní kultura.
        var vysledek = Cestina.Cislo(1234567);

        Assert.DoesNotContain(',', vysledek);
        Assert.DoesNotContain('.', vysledek);
        Assert.Equal(2, vysledek.Count(z => z == ' '));
    }

    [Fact]
    public void Formatovani_nezavisi_na_kulture_stroje()
    {
        // V invariantním režimu žádné jiné kultury neexistují, takže není co přepínat.
        if (!KulturyJsouKDispozici())
        {
            return;
        }

        var puvodni = System.Globalization.CultureInfo.CurrentCulture;

        try
        {
            foreach (var kultura in new[] { "en-US", "cs-CZ", "de-DE" })
            {
                System.Globalization.CultureInfo.CurrentCulture =
                    System.Globalization.CultureInfo.GetCultureInfo(kultura);

                Assert.Equal("1 234 567 řádků", Cestina.Radky(1234567));
            }
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = puvodni;
        }
    }

    /// <summary>
    /// Běží runtime s plnými daty o kulturách? V invariantním režimu <c>GetCultureInfo</c>
    /// pro cizí jméno vyhodí výjimku, takže se to nedá zjistit jinak než pokusem.
    /// </summary>
    private static bool KulturyJsouKDispozici()
    {
        try
        {
            return !System.Globalization.CultureInfo.GetCultureInfo("en-US").Name.Equals(
                System.Globalization.CultureInfo.InvariantCulture.Name,
                StringComparison.Ordinal);
        }
        catch (System.Globalization.CultureNotFoundException)
        {
            return false;
        }
    }

    [Theory]
    [InlineData(0, "0 chyb")]
    [InlineData(1, "1 chyba")]
    [InlineData(2, "2 chyby")]
    [InlineData(7, "7 chyb")]
    public void Chyby_se_sklonuji(int pocet, string expected) =>
        Assert.Equal(expected, Cestina.Chyby(pocet));

    [Theory]
    [InlineData(1, "1 varování")]
    [InlineData(5, "5 varování")]
    public void Varovani_je_ve_vsech_tvarech_stejne(int pocet, string expected) =>
        Assert.Equal(expected, Cestina.Varovani(pocet));

    [Fact]
    public void Zaporny_pocet_se_chova_jako_kladny() =>
        Assert.Equal("tabulka", Cestina.Tvar(-1, "tabulka", "tabulky", "tabulek"));

    [Fact]
    public void Samotny_tvar_jde_ziskat_bez_cisla()
    {
        Assert.Equal("tabulka", Cestina.Tvar(1, "tabulka", "tabulky", "tabulek"));
        Assert.Equal("tabulky", Cestina.Tvar(3, "tabulka", "tabulky", "tabulek"));
        Assert.Equal("tabulek", Cestina.Tvar(10, "tabulka", "tabulky", "tabulek"));
    }

    [Fact]
    public void Prevod_z_celeho_cisla_funguje_i_pro_int() =>
        Assert.Equal("2 tabulky", Cestina.Pocet(2, "tabulka", "tabulky", "tabulek"));
}
