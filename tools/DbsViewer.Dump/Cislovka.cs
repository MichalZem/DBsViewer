using System.Globalization;
using System.Text;

namespace DbsViewer.Dump;

/// <summary>
/// Čísla a skloňování ve výpisu nástroje.
/// </summary>
/// <remarks>
/// Nástroj má vlastní formátování schválně, i když UI umí totéž. Prohlížečka od
/// <see href="../../docs/adr/0018-vicejazycne-ui.md">ADR-0018</see> mluví dvěma jazyky
/// a vybírá tvar podle toho, který jazyk uživatel zvolil — jenže `dbsview` žádný zvolený
/// jazyk nemá. Kdyby si půjčoval <c>Counts</c> z UI, vybral by se tvar podle kultury
/// stroje: na českých Windows „2 řádky", na CI „2 rows". Výpis nástroje je česky a nesmí
/// se měnit podle toho, kde běží.
/// </remarks>
internal static class Cislovka
{
    /// <summary>Počet se správným tvarem slova.</summary>
    /// <param name="pocet">Počet.</param>
    /// <param name="jedna">Tvar pro jedničku — „řádek".</param>
    /// <param name="dveAzCtyri">Tvar pro dvě až čtyři — „řádky".</param>
    /// <param name="petAVice">Tvar pro nulu a pět a víc — „řádků".</param>
    public static string Pocet(long pocet, string jedna, string dveAzCtyri, string petAVice)
    {
        var tvar = Math.Abs(pocet) switch
        {
            1 => jedna,
            >= 2 and <= 4 => dveAzCtyri,
            _ => petAVice,
        };

        return $"{Cislo(pocet)} {tvar}";
    }

    /// <summary>Číslo s nedělitelnou mezerou po tisících, nezávisle na kultuře stroje.</summary>
    public static string Cislo(long hodnota)
    {
        var zaporne = hodnota < 0;
        var cislice = Math.Abs(hodnota).ToString(CultureInfo.InvariantCulture);
        var buffer = new StringBuilder(cislice.Length + (cislice.Length / 3) + 1);

        for (var i = 0; i < cislice.Length; i++)
        {
            // Mezera odděluje trojice zprava, takže první skupina může být kratší.
            if (i > 0 && (cislice.Length - i) % 3 == 0)
            {
                buffer.Append(' ');
            }

            buffer.Append(cislice[i]);
        }

        return zaporne ? "-" + buffer : buffer.ToString();
    }
}
