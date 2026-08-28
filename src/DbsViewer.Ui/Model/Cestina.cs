namespace DbsViewer.Ui.Model;

/// <summary>
/// Skloňování podstatných jmen podle počtu. Bez něj by UI psalo „1 tabulek“.
/// </summary>
public static class Cestina
{
    /// <summary>
    /// Spojí počet se správným tvarem slova.
    /// </summary>
    /// <param name="pocet">Počet.</param>
    /// <param name="jedna">Tvar pro jedničku — „tabulka“.</param>
    /// <param name="dveAzCtyri">Tvar pro dvě až čtyři — „tabulky“.</param>
    /// <param name="petAVice">Tvar pro nulu a pět a víc — „tabulek“.</param>
    public static string Pocet(int pocet, string jedna, string dveAzCtyri, string petAVice) =>
        $"{pocet} {Tvar(pocet, jedna, dveAzCtyri, petAVice)}";

    /// <summary>Totéž pro dlouhá čísla, například odhad počtu řádků.</summary>
    public static string Pocet(long pocet, string jedna, string dveAzCtyri, string petAVice) =>
        $"{pocet:N0} {Tvar(pocet, jedna, dveAzCtyri, petAVice)}";

    /// <summary>Vybere správný tvar slova bez připojeného čísla.</summary>
    public static string Tvar(long pocet, string jedna, string dveAzCtyri, string petAVice)
    {
        // Záporná čísla se v UI neobjevují, ale kdyby ano, ať se chovají jako kladná.
        var absolutni = Math.Abs(pocet);

        return absolutni switch
        {
            1 => jedna,
            >= 2 and <= 4 => dveAzCtyri,
            _ => petAVice,
        };
    }

    /// <summary>Počet tabulek se správným tvarem.</summary>
    public static string Tabulky(int pocet) => Pocet(pocet, "tabulka", "tabulky", "tabulek");

    /// <summary>Počet vazeb se správným tvarem.</summary>
    public static string Vazby(int pocet) => Pocet(pocet, "vazba", "vazby", "vazeb");

    /// <summary>Počet řádků se správným tvarem.</summary>
    public static string Radky(long pocet) => Pocet(pocet, "řádek", "řádky", "řádků");

    /// <summary>Počet nálezů závažnosti chyba.</summary>
    public static string Chyby(int pocet) => Pocet(pocet, "chyba", "chyby", "chyb");

    /// <summary>Počet nálezů závažnosti varování.</summary>
    public static string Varovani(int pocet) => Pocet(pocet, "varování", "varování", "varování");
}
