using System.Globalization;
using System.Text;

namespace DbsViewer.Ui.Model;

/// <summary>
/// Převod lomené trasy na SVG path se zaoblenými rohy.
/// </summary>
/// <remarks>
/// Ostré pravoúhlé zlomy působí v diagramu tvrdě a při hustším rozvržení splývají
/// s okraji tabulek. Zaoblení je drobnost, která čitelnost i vzhled zvedne víc než
/// cokoli jiného na hraně — a stojí jen jeden oblouk v každém rohu.
/// </remarks>
public static class EdgePath
{
    /// <summary>Poloměr zaoblení rohu.</summary>
    public const double CornerRadius = 8;

    /// <summary>Sestaví data pro atribut <c>d</c> ze zadaných bodů.</summary>
    /// <param name="points">Body lomené trasy, včetně krajních.</param>
    public static string Build(IReadOnlyList<(double X, double Y)> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        if (points.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.Append('M').Append(Num(points[0].X)).Append(' ').Append(Num(points[0].Y));

        for (var i = 1; i < points.Count - 1; i++)
        {
            var prev = points[i - 1];
            var roh = points[i];
            var next = points[i + 1];

            // Oblouk si ukousne kus z obou sousedních úseků, takže nesmí být delší
            // než jejich polovina — jinak by se u krátkého úseku dva oblouky prolnuly.
            var r = Math.Min(
                CornerRadius,
                Math.Min(Delka(prev, roh), Delka(roh, next)) / 2);

            var vstup = Posun(roh, prev, r);
            var vystup = Posun(roh, next, r);

            sb.Append('L').Append(Num(vstup.X)).Append(' ').Append(Num(vstup.Y));
            sb.Append('Q').Append(Num(roh.X)).Append(' ').Append(Num(roh.Y))
              .Append(' ').Append(Num(vystup.X)).Append(' ').Append(Num(vystup.Y));
        }

        // Jediný bod je jen přesun; koncová úsečka by vedla sama do sebe.
        if (points.Count > 1)
        {
            var konec = points[^1];
            sb.Append('L').Append(Num(konec.X)).Append(' ').Append(Num(konec.Y));
        }

        return sb.ToString();
    }

    private static double Delka((double X, double Y) a, (double X, double Y) b) =>
        Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    /// <summary>Bod ve vzdálenosti <paramref name="r"/> od rohu směrem k sousedovi.</summary>
    private static (double X, double Y) Posun(
        (double X, double Y) roh,
        (double X, double Y) k,
        double r)
    {
        var delka = Delka(roh, k);

        // Nulová délka nastane u zdvojeného bodu; posouvat se pak není kam.
        if (delka < 0.001)
        {
            return roh;
        }

        var podil = r / delka;

        return (roh.X + ((k.X - roh.X) * podil), roh.Y + ((k.Y - roh.Y) * podil));
    }

    /// <summary>Číslo do SVG vždy s tečkou — desetinná čárka by atribut rozbila.</summary>
    private static string Num(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);
}
