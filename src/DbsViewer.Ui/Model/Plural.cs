using System.Globalization;
using System.Text;

namespace DbsViewer.Ui.Model;

/// <summary>
/// Skloňování podstatných jmen podle počtu a formátování čísel.
/// </summary>
/// <remarks>
/// Tvary slov jsou v <c>.resx</c>, ale pravidlo, který z nich se pro dané číslo vybere,
/// v resource souboru vyjádřit nejde — angličtina má tvary dva, čeština tři a každý
/// jazyk je vybírá jinak. Pravidlo proto žije tady a resx dodává jen slova.
///
/// Formátování čísel se schválně neopírá o kulturu stroje: WebAssembly běží s invariantní
/// globalizací, server může mít jakoukoli a CI zase jinou, takže <c>{x:N0}</c> by pokaždé
/// dalo jiný výsledek. Oddělovač se proto vybírá podle jazyka prohlížečky, ne podle stroje.
/// </remarks>
public static class Plural
{
    /// <summary>Pevná mezera — v češtině odděluje tisíce a nesmí se zalomit.</summary>
    private const char CzechSeparator = ' ';

    private const char EnglishSeparator = ',';

    /// <summary>Běží prohlížečka česky?</summary>
    /// <remarks>
    /// Čte se z <see cref="CultureInfo.CurrentUICulture"/>, kterou nastaví <c>Program</c>
    /// podle adresy. Testy si ji přepnou stejným způsobem.
    /// </remarks>
    internal static bool IsCzech =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("cs", StringComparison.Ordinal);

    /// <summary>Spojí počet se správným tvarem slova.</summary>
    /// <param name="count">Počet.</param>
    /// <param name="one">Tvar pro jedničku — „tabulka", „table".</param>
    /// <param name="few">Tvar pro dvě až čtyři — „tabulky". V angličtině stejný jako <paramref name="other"/>.</param>
    /// <param name="other">Tvar pro nulu a pět a víc — „tabulek", „tables".</param>
    public static string Format(long count, string one, string few, string other) =>
        $"{Number(count)} {Form(count, one, few, other)}";

    /// <summary>Vybere tvar slova bez připojeného čísla.</summary>
    public static string Form(long count, string one, string few, string other)
    {
        // Záporná čísla se v UI neobjevují, ale kdyby ano, ať se chovají jako kladná.
        var absolute = Math.Abs(count);

        if (!IsCzech)
        {
            return absolute == 1 ? one : other;
        }

        return absolute switch
        {
            1 => one,
            >= 2 and <= 4 => few,
            _ => other,
        };
    }

    /// <summary>Číslo s oddělenými tisíci podle jazyka prohlížečky.</summary>
    public static string Number(long value)
    {
        var separator = IsCzech ? CzechSeparator : EnglishSeparator;
        var negative = value < 0;
        var digits = Math.Abs(value).ToString(CultureInfo.InvariantCulture);
        var buffer = new StringBuilder(digits.Length + (digits.Length / 3) + 1);

        for (var i = 0; i < digits.Length; i++)
        {
            // Oddělovač jde po trojicích zprava, takže první skupina může být kratší.
            if (i > 0 && (digits.Length - i) % 3 == 0)
            {
                buffer.Append(separator);
            }

            buffer.Append(digits[i]);
        }

        return negative ? "-" + buffer : buffer.ToString();
    }
}
