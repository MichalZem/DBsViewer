namespace DbsViewer.Ui.Model;

/// <summary>Strana uzlu, ze které hrana vychází nebo do které vstupuje.</summary>
public enum EdgeSide
{
    /// <summary>Doleva od uzlu.</summary>
    Left,

    /// <summary>Doprava od uzlu.</summary>
    Right,
}

/// <summary>Obdélníková překážka, které se trasa vyhýbá.</summary>
/// <param name="X">Levý okraj.</param>
/// <param name="Y">Horní okraj.</param>
/// <param name="Width">Šířka.</param>
/// <param name="Height">Výška.</param>
public readonly record struct RouteObstacle(double X, double Y, double Width, double Height)
{
    /// <summary>Pravý okraj.</summary>
    public double Right => X + Width;

    /// <summary>Dolní okraj.</summary>
    public double Bottom => Y + Height;
}

/// <summary>
/// Ortogonální vedení hrany kolem tabulek.
/// </summary>
/// <remarks>
/// Naivní trasa „půl cesty vodorovně, pak svisle" vede přes tabulky, které jí stojí
/// v cestě, a u vazeb přes několik vrstev se schová pod uzly. Tenhle router místo toho
/// hledá cestu po mřížce vedené v odstupu kolem okrajů tabulek: každý segment se ověří
/// proti překážkám a zatáčky se penalizují, takže vyjde trasa s co nejmenším počtem
/// ohybů, která nikde neprochází uzlem.
///
/// Mřížka se staví jen z okolí obou konců, ne z celého diagramu — u stovek tabulek by
/// jinak hledání trvalo déle, než je pro překreslování únosné. Když ani tak nevyjde
/// (rozlehlý diagram, konce zavalené uzly), vrátí se jednoduchá trasa; diagram tím
/// nepřijde o hranu, jen nebude v tom jednom místě ideální.
/// </remarks>
public static class EdgeRouter
{
    /// <summary>Odstup trasy od okraje tabulky.</summary>
    public const double Padding = 16;

    /// <summary>Kolmý úsek, kterým hrana vystupuje z uzlu, než začne zatáčet.</summary>
    public const double StubLength = 18;

    /// <summary>
    /// Přirážka za zatáčku, v pixelech dráhy. Bez ní by vyhrávaly schodovité trasy
    /// stejné délky; s ní router raději zajede dál a zatočí jednou.
    /// </summary>
    private const double TurnPenalty = 60;

    /// <summary>
    /// Strop velikosti mřížky. Sto na sto je deset tisíc vrcholů — nad tím se hledání
    /// začne projevovat na plynulosti a raději se vrátí jednoduchá trasa.
    /// </summary>
    private const int MaxLines = 100;

    /// <summary>Tolerance na dotyk okraje. Trasa smí vést těsně kolem, ne skrz.</summary>
    private const double Epsilon = 0.5;

    /// <summary>
    /// Přirážka za souběh s už vedenou hranou. Je vysoká schválně: dvě čáry na téže
    /// lince splynou v jednu a diagram tím lže o počtu vazeb. Radši zajet jinam.
    /// </summary>
    private const double OverlapPenalty = 400;

    /// <summary>
    /// Přirážka za křížení s už vedenou hranou. Nižší než za souběh — křížení je
    /// čitelné a v hustším schématu se mu úplně vyhnout nedá.
    /// </summary>
    private const double CrossPenalty = 90;

    /// <summary>Jak daleko od sebe musí být rovnoběžné úseky, aby nešlo o souběh.</summary>
    private const double LaneWidth = 6;

    /// <summary>
    /// Najde ortogonální trasu mezi dvěma kotevními body.
    /// </summary>
    /// <param name="start">Kotva na okraji zdrojového uzlu.</param>
    /// <param name="startSide">Strana, kterou hrana ze zdroje vystupuje.</param>
    /// <param name="end">Kotva na okraji cílového uzlu.</param>
    /// <param name="endSide">Strana, kterou hrana do cíle vstupuje.</param>
    /// <param name="obstacles">Tabulky, kterým se trasa vyhne.</param>
    /// <param name="routed">
    /// Už vedené hrany. Trasa se jim vyhýbá: souběh je zakázaný skoro úplně, křížení
    /// jen zdražené. Bez toho by se čáry mezi vrstvami slily do jedné.
    /// </param>
    public static IReadOnlyList<(double X, double Y)> Route(
        (double X, double Y) start,
        EdgeSide startSide,
        (double X, double Y) end,
        EdgeSide endSide,
        IReadOnlyList<RouteObstacle> obstacles,
        IReadOnlyList<IReadOnlyList<(double X, double Y)>>? routed = null)
    {
        ArgumentNullException.ThrowIfNull(obstacles);

        var fromStub = Stub(start, startSide);
        var toStub = Stub(end, endSide);
        var useky = Segments(routed);

        var path = FindPath(fromStub, toStub, obstacles, useky)
            ?? [fromStub, (fromStub.X, toStub.Y), toStub];

        return Simplify([start, .. path, end]);
    }

    /// <summary>Rozloží hotové trasy na jednotlivé úseky.</summary>
    private static List<((double X, double Y) A, (double X, double Y) B)> Segments(
        IReadOnlyList<IReadOnlyList<(double X, double Y)>>? routed)
    {
        var useky = new List<((double X, double Y) A, (double X, double Y) B)>();

        foreach (var trasa in routed ?? [])
        {
            for (var i = 1; i < trasa.Count; i++)
            {
                useky.Add((trasa[i - 1], trasa[i]));
            }
        }

        return useky;
    }

    /// <summary>Konec kolmého úseku vystupujícího z uzlu.</summary>
    internal static (double X, double Y) Stub((double X, double Y) anchor, EdgeSide side) =>
        side == EdgeSide.Right
            ? (anchor.X + StubLength, anchor.Y)
            : (anchor.X - StubLength, anchor.Y);

    /// <summary>
    /// Hledání nejlevnější ortogonální cesty po mřížce. Vrací <c>null</c>, když mřížka
    /// vyjde příliš velká nebo cesta neexistuje.
    /// </summary>
    private static List<(double X, double Y)>? FindPath(
        (double X, double Y) from,
        (double X, double Y) to,
        IReadOnlyList<RouteObstacle> obstacles,
        List<((double X, double Y) A, (double X, double Y) B)> routed)
    {
        var (xs, ys, relevant) = BuildGrid(from, to, obstacles, routed);

        if (xs.Count > MaxLines || ys.Count > MaxLines)
        {
            return null;
        }

        var startX = xs.IndexOf(from.X);
        var startY = ys.IndexOf(from.Y);
        var cilX = xs.IndexOf(to.X);
        var cilY = ys.IndexOf(to.Y);

        // Stav nese i směr příchodu, jinak by nešlo penalizovat zatáčku.
        var open = new PriorityQueue<(int Ix, int Iy, int Dir), double>();
        var cost = new Dictionary<(int, int, int), double>();
        var prev = new Dictionary<(int, int, int), (int, int, int)>();

        for (var dir = 0; dir < 4; dir++)
        {
            var stav = (startX, startY, dir);
            cost[stav] = 0;
            open.Enqueue(stav, Heuristic(startX, startY));
        }

        while (open.TryDequeue(out var current, out _))
        {
            var (ix, iy, dir) = current;

            if (ix == cilX && iy == cilY)
            {
                return Reconstruct(prev, current, xs, ys);
            }

            var currentCost = cost[current];

            for (var next = 0; next < 4; next++)
            {
                var (dx, dy) = Steps[next];
                var nx = ix + dx;
                var ny = iy + dy;

                if (nx < 0 || ny < 0 || nx >= xs.Count || ny >= ys.Count)
                {
                    continue;
                }

                var a = (xs[ix], ys[iy]);
                var b = (xs[nx], ys[ny]);

                if (Blocked(a, b, relevant))
                {
                    continue;
                }

                var delta = Math.Abs(b.Item1 - a.Item1) + Math.Abs(b.Item2 - a.Item2);
                var turn = next == dir ? 0 : TurnPenalty;
                var candidate = currentCost + delta + turn + Conflict(a, b, routed);
                var stav = (nx, ny, next);

                if (cost.TryGetValue(stav, out var known) && known <= candidate)
                {
                    continue;
                }

                cost[stav] = candidate;
                prev[stav] = current;
                open.Enqueue(stav, candidate + Heuristic(nx, ny));
            }
        }

        return null;

        double Heuristic(int ix, int iy) =>
            Math.Abs(xs[ix] - to.X) + Math.Abs(ys[iy] - to.Y);
    }

    /// <summary>Posuny na mřížce: doprava, doleva, dolů, nahoru.</summary>
    private static readonly (int Dx, int Dy)[] Steps = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    /// <summary>
    /// Sestaví kandidátní vodorovné a svislé linie. Bere jen překážky v okolí obou konců —
    /// tabulka na druhém konci diagramu trasu ovlivnit nemůže, ale mřížku by zvětšila.
    /// </summary>
    private static (List<double> Xs, List<double> Ys, List<RouteObstacle> Relevant) BuildGrid(
        (double X, double Y) from,
        (double X, double Y) to,
        IReadOnlyList<RouteObstacle> obstacles,
        IReadOnlyList<((double X, double Y) A, (double X, double Y) B)> routed)
    {
        var minX = Math.Min(from.X, to.X) - Padding * 4;
        var maxX = Math.Max(from.X, to.X) + Padding * 4;
        var minY = Math.Min(from.Y, to.Y) - Padding * 4;
        var maxY = Math.Max(from.Y, to.Y) + Padding * 4;

        var relevant = new List<RouteObstacle>();
        var xs = new SortedSet<double> { from.X, to.X };
        var ys = new SortedSet<double> { from.Y, to.Y };

        foreach (var o in obstacles)
        {
            if (o.Right < minX || o.X > maxX || o.Bottom < minY || o.Y > maxY)
            {
                continue;
            }

            relevant.Add(o);

            xs.Add(o.X - Padding);
            xs.Add(o.Right + Padding);
            ys.Add(o.Y - Padding);
            ys.Add(o.Bottom + Padding);
        }

        // Vedle každé už vedené hrany se přidá volný pruh. Bez něj by mřížka nabízela
        // jen linku, na které ta hrana leží, a vyhnout se jí by nebylo kam.
        foreach (var (a, b) in routed)
        {
            if (Math.Abs(a.Y - b.Y) < Epsilon)
            {
                Pridej(ys, a.Y - LaneWidth, minY, maxY);
                Pridej(ys, a.Y + LaneWidth, minY, maxY);
            }
            else
            {
                Pridej(xs, a.X - LaneWidth, minX, maxX);
                Pridej(xs, a.X + LaneWidth, minX, maxX);
            }
        }

        return ([.. xs], [.. ys], relevant);

        static void Pridej(SortedSet<double> kam, double hodnota, double min, double max)
        {
            if (hodnota >= min && hodnota <= max)
            {
                kam.Add(hodnota);
            }
        }
    }

    /// <summary>Přirážka za konflikt s už vedenými hranami.</summary>
    internal static double Conflict(
        (double X, double Y) a,
        (double X, double Y) b,
        IReadOnlyList<((double X, double Y) A, (double X, double Y) B)> routed)
    {
        var penale = 0.0;

        foreach (var (c, d) in routed)
        {
            if (Overlaps(a, b, c, d))
            {
                penale += OverlapPenalty;
            }
            else if (Crosses(a, b, c, d))
            {
                penale += CrossPenalty;
            }
        }

        return penale;
    }

    /// <summary>Běží dva úseky souběžně tak blízko, že splynou?</summary>
    internal static bool Overlaps(
        (double X, double Y) a,
        (double X, double Y) b,
        (double X, double Y) c,
        (double X, double Y) d)
    {
        var prvniVodorovny = Math.Abs(a.Y - b.Y) < Epsilon;
        var druhyVodorovny = Math.Abs(c.Y - d.Y) < Epsilon;

        if (prvniVodorovny != druhyVodorovny)
        {
            return false;
        }

        return prvniVodorovny
            ? Math.Abs(a.Y - c.Y) < LaneWidth && Prekryv(a.X, b.X, c.X, d.X)
            : Math.Abs(a.X - c.X) < LaneWidth && Prekryv(a.Y, b.Y, c.Y, d.Y);
    }

    /// <summary>Kříží se vodorovný úsek se svislým?</summary>
    internal static bool Crosses(
        (double X, double Y) a,
        (double X, double Y) b,
        (double X, double Y) c,
        (double X, double Y) d)
    {
        var prvniVodorovny = Math.Abs(a.Y - b.Y) < Epsilon;
        var druhyVodorovny = Math.Abs(c.Y - d.Y) < Epsilon;

        if (prvniVodorovny == druhyVodorovny)
        {
            return false;
        }

        var (vodorovnyA, vodorovnyB, svislyA, svislyB) = prvniVodorovny
            ? (a, b, c, d)
            : (c, d, a, b);

        return Mezi(svislyA.X, vodorovnyA.X, vodorovnyB.X)
            && Mezi(vodorovnyA.Y, svislyA.Y, svislyB.Y);
    }

    private static bool Prekryv(double a1, double a2, double b1, double b2) =>
        Math.Max(a1, a2) > Math.Min(b1, b2) + Epsilon
        && Math.Min(a1, a2) < Math.Max(b1, b2) - Epsilon;

    private static bool Mezi(double hodnota, double a, double b) =>
        hodnota > Math.Min(a, b) + Epsilon && hodnota < Math.Max(a, b) - Epsilon;

    /// <summary>Protíná osově zarovnaný segment některou překážku?</summary>
    internal static bool Blocked(
        (double X, double Y) a,
        (double X, double Y) b,
        IReadOnlyList<RouteObstacle> obstacles)
    {
        var loX = Math.Min(a.X, b.X);
        var hiX = Math.Max(a.X, b.X);
        var loY = Math.Min(a.Y, b.Y);
        var hiY = Math.Max(a.Y, b.Y);

        foreach (var o in obstacles)
        {
            // Dotyk okraje projde, průnik vnitřkem ne.
            if (hiX > o.X + Epsilon
                && loX < o.Right - Epsilon
                && hiY > o.Y + Epsilon
                && loY < o.Bottom - Epsilon)
            {
                return true;
            }
        }

        return false;
    }

    private static List<(double X, double Y)> Reconstruct(
        Dictionary<(int, int, int), (int, int, int)> prev,
        (int Ix, int Iy, int Dir) end,
        List<double> xs,
        List<double> ys)
    {
        var body = new List<(double X, double Y)>();
        var current = (end.Ix, end.Iy, end.Dir);

        while (true)
        {
            body.Add((xs[current.Item1], ys[current.Item2]));

            if (!prev.TryGetValue(current, out var parent))
            {
                break;
            }

            current = parent;
        }

        body.Reverse();

        return body;
    }

    /// <summary>
    /// Vyhodí body ležící na spojnici sousedů. Bez toho by trasa nesla desítky
    /// mezilehlých bodů z mřížky a SVG by zbytečně narostlo.
    /// </summary>
    internal static IReadOnlyList<(double X, double Y)> Simplify(
        IReadOnlyList<(double X, double Y)> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        var result = new List<(double X, double Y)>(points.Count);

        foreach (var point in points)
        {
            // Body na stejném místě jsou k ničemu — vznikají, když kotva padne na mřížku.
            if (result.Count > 0 && Same(result[^1], point))
            {
                continue;
            }

            if (result.Count >= 2 && Collinear(result[^2], result[^1], point))
            {
                result[^1] = point;
                continue;
            }

            result.Add(point);
        }

        return result;
    }

    private static bool Same((double X, double Y) a, (double X, double Y) b) =>
        Math.Abs(a.X - b.X) < Epsilon && Math.Abs(a.Y - b.Y) < Epsilon;

    private static bool Collinear((double X, double Y) a, (double X, double Y) b, (double X, double Y) c) =>
        (Math.Abs(a.X - b.X) < Epsilon && Math.Abs(b.X - c.X) < Epsilon)
        || (Math.Abs(a.Y - b.Y) < Epsilon && Math.Abs(b.Y - c.Y) < Epsilon);
}
