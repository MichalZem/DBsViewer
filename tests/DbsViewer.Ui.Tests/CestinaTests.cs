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

    [Fact]
    public void Velka_cisla_maji_oddelovac_tisicu() =>
        Assert.Equal("1 234 567 řádků", Cestina.Radky(1234567).Replace('\u00a0', ' '));

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
