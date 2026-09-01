namespace DbsViewer.Ui.Model;

/// <summary>Umístěný uzel diagramu.</summary>
public sealed record DiagramNode
{
    public required DbTable Table { get; init; }

    public double X { get; init; }

    public double Y { get; init; }

    public double Width { get; init; }

    public double Height { get; init; }

    public double CenterX => X + (Width / 2);

    public double CenterY => Y + (Height / 2);

    /// <summary>Pořadí vrstvy, ve které uzel leží. Nula je kořenová.</summary>
    public int Layer { get; init; }
}

/// <summary>Hrana diagramu s vypočtenou trasou.</summary>
public sealed record DiagramEdge
{
    public required DbRelationship Relationship { get; init; }

    /// <summary>Body lomené čáry, včetně krajních.</summary>
    public required IReadOnlyList<(double X, double Y)> Points { get; init; }

    /// <summary>Bod, kam se umístí popisek kardinality.</summary>
    public (double X, double Y) LabelAt { get; init; }

    /// <summary>Hrana z tabulky do sebe sama — kreslí se jako smyčka.</summary>
    public bool IsSelfLoop { get; init; }
}

/// <summary>Hotové rozložení diagramu.</summary>
public sealed record DiagramLayoutResult
{
    public required IReadOnlyList<DiagramNode> Nodes { get; init; }

    public required IReadOnlyList<DiagramEdge> Edges { get; init; }

    public double Width { get; init; }

    public double Height { get; init; }

    public DiagramNode? Find(DbObjectName table) =>
        Nodes.FirstOrDefault(n => n.Table.Name == table);
}

/// <summary>
/// Rozmístění tabulek do vrstev podle vazeb.
/// </summary>
/// <remarks>
/// Vrstvený layout je pro ER diagram přirozený: tabulky, na které se odkazuje, jdou
/// doleva, závislé doprava. Algoritmus je vlastní a v C#, aby diagram fungoval i bez
/// JavaScriptu; kvalitnější rozvržení přes elkjs se dá doplnit později, aniž by se měnil
/// tvar výsledku.
/// </remarks>
public static class DiagramLayout
{
    /// <summary>Šířka uzlu ve sbaleném stavu.</summary>
    public const double CollapsedWidth = 200;

    /// <summary>Výška hlavičky uzlu.</summary>
    public const double HeaderHeight = 34;

    /// <summary>Výška jednoho řádku se sloupcem.</summary>
    public const double RowHeight = 20;

    /// <summary>Vodorovná mezera mezi vrstvami.</summary>
    public const double LayerGap = 120;

    /// <summary>Svislá mezera mezi uzly ve vrstvě.</summary>
    public const double NodeGap = 36;

    private const double Margin = 40;

    /// <summary>Volný pruh u horního a dolního okraje uzlu, kam kotvy nesahají.</summary>
    private const double AnchorInset = 10;

    /// <summary>
    /// Nejmenší svislý rozestup dvou kotev. Musí se mezi ně vejít popisek kardinality,
    /// jinak se u tabulky s několika vazbami popisky slijí.
    /// </summary>
    private const double MinAnchorGap = 24;

    /// <summary>Spočítá rozložení pro zadané tabulky a vazby.</summary>
    /// <param name="tables">Tabulky k vykreslení.</param>
    /// <param name="relationships">Vazby mezi nimi.</param>
    /// <param name="expanded">Tabulky zobrazené se všemi sloupci; ostatní jen s klíči.</param>
    public static DiagramLayoutResult Compute(
        IReadOnlyList<DbTable> tables,
        IReadOnlyList<DbRelationship> relationships,
        IReadOnlySet<DbObjectName>? expanded = null)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(relationships);

        if (tables.Count == 0)
        {
            return new DiagramLayoutResult { Nodes = [], Edges = [], Width = 0, Height = 0 };
        }

        var layers = AssignLayers(tables, relationships);
        var kotvy = AnchorCounts(tables, relationships, layers);
        var nodes = PlaceNodes(tables, layers, expanded, kotvy, relationships);
        var edges = RouteEdges(nodes, relationships);

        var width = nodes.Max(static n => n.X + n.Width) + Margin;
        var height = nodes.Max(static n => n.Y + n.Height) + Margin;

        return new DiagramLayoutResult
        {
            Nodes = nodes,
            Edges = edges,
            Width = width,
            Height = height,
        };
    }

    /// <summary>
    /// Přiřadí tabulky do vrstev. Tabulka je o vrstvu vpravo od všech, na které odkazuje,
    /// takže vazby přirozeně směřují doleva.
    /// </summary>
    /// <remarks>
    /// Cyklické vazby jsou v databázích běžné — zaměstnanec má oddělení a oddělení má
    /// vedoucího. Kdyby se braly v úvahu všechny, vrstvy by se u cyklu donekonečna
    /// posouvaly a diagram by zbytněl do šířky. Zpětné hrany se proto před výpočtem
    /// odstraní; v diagramu se stejně vykreslí, jen neurčují pořadí vrstev.
    /// </remarks>
    internal static Dictionary<DbObjectName, int> AssignLayers(
        IReadOnlyList<DbTable> tables,
        IReadOnlyList<DbRelationship> relationships)
    {
        var layers = new Dictionary<DbObjectName, int>();
        var dependencies = new Dictionary<DbObjectName, List<DbObjectName>>();

        foreach (var table in tables)
        {
            layers[table.Name] = 0;
            dependencies[table.Name] = [];
        }

        foreach (var relationship in relationships)
        {
            if (relationship.From != relationship.To
                && dependencies.TryGetValue(relationship.From, out var list)
                && layers.ContainsKey(relationship.To)
                && !list.Contains(relationship.To))
            {
                list.Add(relationship.To);
            }
        }

        RemoveBackEdges(tables, dependencies);

        // Bez cyklů stačí tolik průchodů, kolik je nejdelší řetěz závislostí.
        for (var pass = 0; pass < tables.Count; pass++)
        {
            var changed = false;

            foreach (var table in tables)
            {
                var wanted = 0;

                foreach (var dependency in dependencies[table.Name])
                {
                    wanted = Math.Max(wanted, layers[dependency] + 1);
                }

                if (wanted > layers[table.Name])
                {
                    layers[table.Name] = wanted;
                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }

        return layers;
    }

    /// <summary>
    /// Odstraní hrany uzavírající cyklus. Prochází se do hloubky a hrana vedoucí na uzel,
    /// který je právě na zásobníku, se zahodí — tím z grafu vznikne acyklický, ve kterém
    /// mají vrstvy konečnou hodnotu.
    /// </summary>
    private static void RemoveBackEdges(
        IReadOnlyList<DbTable> tables,
        Dictionary<DbObjectName, List<DbObjectName>> dependencies)
    {
        var hotovo = new HashSet<DbObjectName>();
        var naZasobniku = new HashSet<DbObjectName>();

        foreach (var table in tables)
        {
            Visit(table.Name);
        }

        void Visit(DbObjectName node)
        {
            if (!hotovo.Add(node))
            {
                return;
            }

            naZasobniku.Add(node);

            var sousede = dependencies[node];

            for (var i = sousede.Count - 1; i >= 0; i--)
            {
                var next = sousede[i];

                if (naZasobniku.Contains(next))
                {
                    // Zpětná hrana: kdyby zůstala, vrstvy by se posouvaly donekonečna.
                    sousede.RemoveAt(i);
                    continue;
                }

                Visit(next);
            }

            naZasobniku.Remove(node);
        }
    }

    /// <summary>
    /// Kolik kotev bude mít každá tabulka na té hustší ze svých dvou stran.
    /// </summary>
    /// <remarks>
    /// Počítá se před umístěním, protože z toho plyne minimální výška uzlu. Strana se
    /// odvodí z vrstev: tabulka odkazující doprava vystupuje pravým okrajem. Uzly ve
    /// stejné vrstvě mají stejné X, takže u nich vychází pravá strana stejně jako
    /// při pozdějším porovnání středů.
    /// </remarks>
    private static Dictionary<DbObjectName, int> AnchorCounts(
        IReadOnlyList<DbTable> tables,
        IReadOnlyList<DbRelationship> relationships,
        Dictionary<DbObjectName, int> layers)
    {
        var vlevo = new Dictionary<DbObjectName, int>();
        var vpravo = new Dictionary<DbObjectName, int>();

        foreach (var r in relationships)
        {
            if (r.From == r.To
                || !layers.TryGetValue(r.From, out var fromLayer)
                || !layers.TryGetValue(r.To, out var toLayer))
            {
                continue;
            }

            var fromRight = fromLayer <= toLayer;

            Zvys(fromRight ? vpravo : vlevo, r.From);
            Zvys(fromRight ? vlevo : vpravo, r.To);
        }

        return tables.ToDictionary(
            static t => t.Name,
            t => Math.Max(Pocet(vlevo, t.Name), Pocet(vpravo, t.Name)));

        static void Zvys(Dictionary<DbObjectName, int> kam, DbObjectName name) =>
            kam[name] = Pocet(kam, name) + 1;

        static int Pocet(Dictionary<DbObjectName, int> kde, DbObjectName name) =>
            kde.TryGetValue(name, out var value) ? value : 0;
    }

    /// <summary>Kolikrát se pořadí ve vrstvách přepočítá.</summary>
    private const int OrderingPasses = 4;

    /// <summary>
    /// Seřadí tabulky uvnitř vrstev tak, aby se vazby co nejméně křížily.
    /// </summary>
    /// <remarks>
    /// Abecední pořadí je předvídatelné, ale o vazby se nestará: tabulka odkazovaná
    /// shora skončí dole a její čára se protne přes celou vrstvu. Používá se proto
    /// barycentrum — uzel se posune na průměrnou pozici svých sousedů v sousední vrstvě.
    /// Několik průchodů tam a zpět pořadí ustálí. Není to optimální řešení (to je
    /// NP-těžké), ale křížení ubere podstatnou část.
    ///
    /// Abecední pořadí zůstává výchozím stavem i rozhodčím při shodě, takže výsledek
    /// je pokaždé stejný.
    /// </remarks>
    internal static Dictionary<DbObjectName, int> OrderWithinLayers(
        IReadOnlyList<DbTable> tables,
        Dictionary<DbObjectName, int> layers,
        IReadOnlyList<DbRelationship> relationships)
    {
        var sousede = tables.ToDictionary(static t => t.Name, static _ => new List<DbObjectName>());

        foreach (var r in relationships)
        {
            if (r.From == r.To
                || !sousede.TryGetValue(r.From, out var odFrom)
                || !sousede.TryGetValue(r.To, out var odTo))
            {
                continue;
            }

            odFrom.Add(r.To);
            odTo.Add(r.From);
        }

        var vrstvy = tables
            .GroupBy(t => layers[t.Name])
            .OrderBy(static g => g.Key)
            .Select(static g => g.OrderBy(static t => t.Qualified, StringComparer.OrdinalIgnoreCase)
                                 .Select(static t => t.Name)
                                 .ToList())
            .ToList();

        var pozice = new Dictionary<DbObjectName, int>();
        Preznac();

        for (var pass = 0; pass < OrderingPasses; pass++)
        {
            // Střídavě zleva a zprava: každý směr srovnává vrstvu podle té sousední,
            // která už je usazená.
            var zleva = pass % 2 == 0;

            for (var i = 0; i < vrstvy.Count; i++)
            {
                var index = zleva ? i : vrstvy.Count - 1 - i;
                var sousedni = zleva ? index - 1 : index + 1;

                if (sousedni < 0 || sousedni >= vrstvy.Count)
                {
                    continue;
                }

                vrstvy[index] = vrstvy[index]
                    .OrderBy(n => Barycentrum(n, vrstvy[sousedni]))
                    .ThenBy(n => n.Qualified, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                Preznac();
            }
        }

        return pozice;

        void Preznac()
        {
            var poradi = 0;

            foreach (var vrstva in vrstvy)
            {
                foreach (var name in vrstva)
                {
                    pozice[name] = poradi++;
                }
            }
        }

        double Barycentrum(DbObjectName node, List<DbObjectName> sousedniVrstva)
        {
            var indexy = sousede[node]
                .Select(soused => sousedniVrstva.IndexOf(soused))
                .Where(static i => i >= 0)
                .ToList();

            // Tabulka bez souseda v té vrstvě nemá co srovnávat; zůstane, kde byla.
            return indexy.Count == 0 ? pozice[node] : indexy.Average();
        }
    }

    private static List<DiagramNode> PlaceNodes(
        IReadOnlyList<DbTable> tables,
        Dictionary<DbObjectName, int> layers,
        IReadOnlySet<DbObjectName>? expanded,
        Dictionary<DbObjectName, int> anchors,
        IReadOnlyList<DbRelationship> relationships)
    {
        var poradi = OrderWithinLayers(tables, layers, relationships);

        var byLayer = tables
            .GroupBy(t => layers[t.Name])
            .OrderBy(static g => g.Key)
            .ToList();

        var nodes = new List<DiagramNode>(tables.Count);
        var x = Margin;

        foreach (var layer in byLayer)
        {
            var ordered = layer.OrderBy(t => poradi[t.Name]).ToList();
            var y = Margin;

            foreach (var table in ordered)
            {
                var height = NodeHeight(
                    table,
                    expanded?.Contains(table.Name) ?? false,
                    anchors.GetValueOrDefault(table.Name));

                nodes.Add(new DiagramNode
                {
                    Table = table,
                    X = x,
                    Y = y,
                    Width = CollapsedWidth,
                    Height = height,
                    Layer = layer.Key,
                });

                y += height + NodeGap;
            }

            x += CollapsedWidth + LayerGap;
        }

        return nodes;
    }

    /// <summary>
    /// Výška uzlu. Sbalený uzel ukazuje jen klíčové sloupce — u tabulky s třiceti sloupci
    /// je jinak diagram nečitelný.
    /// </summary>
    internal static double NodeHeight(DbTable table, bool isExpanded, int anchorCount = 0)
    {
        var rows = isExpanded
            ? table.Columns.Count
            : table.Columns.Count(static c => c.IsPrimaryKey || c.IsForeignKey);

        var podleSloupcu = HeaderHeight + (Math.Max(rows, 1) * RowHeight) + 8;

        // Tabulka se třemi vazbami a jedním sloupcem by měla kotvy na sobě. Uzel proto
        // povyroste tak, aby se mezi ně vešly popisky. Kotvy se roztahují přes celou
        // použitelnou výšku, takže n kotev potřebuje n−1 mezer.
        var podleKotev = (2 * AnchorInset) + (Math.Max(anchorCount - 1, 0) * MinAnchorGap);

        return Math.Max(podleSloupcu, podleKotev);
    }

    /// <summary>Sloupce zobrazené v uzlu podle toho, jestli je rozbalený.</summary>
    public static IReadOnlyList<DbColumn> VisibleColumns(DbTable table, bool isExpanded)
    {
        ArgumentNullException.ThrowIfNull(table);

        if (isExpanded)
        {
            return table.Columns;
        }

        var keys = table.Columns.Where(static c => c.IsPrimaryKey || c.IsForeignKey).ToList();

        return keys.Count > 0 ? keys : [];
    }

    /// <summary>Spoj mezi dvěma uzly, ještě bez trasy.</summary>
    private readonly record struct Spoj(
        DbRelationship Relationship,
        DiagramNode From,
        DiagramNode To,
        bool FromRight);

    private static List<DiagramEdge> RouteEdges(
        List<DiagramNode> nodes,
        IReadOnlyList<DbRelationship> relationships)
    {
        var byName = nodes.ToDictionary(static n => n.Table.Name);
        var obstacles = nodes
            .Select(static n => new RouteObstacle(n.X, n.Y, n.Width, n.Height))
            .ToList();

        var edges = new List<DiagramEdge>();
        var spoje = new List<Spoj>();

        foreach (var relationship in relationships)
        {
            if (!byName.TryGetValue(relationship.From, out var from)
                || !byName.TryGetValue(relationship.To, out var to))
            {
                continue;
            }

            if (relationship.From == relationship.To)
            {
                edges.Add(SelfLoop(relationship, from));
                continue;
            }

            spoje.Add(new Spoj(relationship, from, to, from.CenterX <= to.CenterX));
        }

        var kotvy = AssignAnchors(spoje);
        var hotove = new List<IReadOnlyList<(double X, double Y)>>();

        // Krátké vazby se vedou první. Mají nejmíň možností, jak se vyhnout, a ty dlouhé
        // se pak přizpůsobí jim — opačné pořadí zaplní úzká místa dlouhými čárami.
        foreach (var spoj in spoje.OrderBy(static s => Math.Abs(s.To.CenterX - s.From.CenterX)
                                                     + Math.Abs(s.To.CenterY - s.From.CenterY)))
        {
            var startSide = spoj.FromRight ? EdgeSide.Right : EdgeSide.Left;
            var endSide = spoj.FromRight ? EdgeSide.Left : EdgeSide.Right;

            var start = (SideX(spoj.From, startSide), kotvy[(spoj.Relationship, true)]);
            var end = (SideX(spoj.To, endSide), kotvy[(spoj.Relationship, false)]);

            var points = EdgeRouter.Route(start, startSide, end, endSide, obstacles, hotove);
            hotove.Add(points);

            edges.Add(new DiagramEdge
            {
                Relationship = spoj.Relationship,
                Points = points,
                LabelAt = LabelPosition(points),
            });
        }

        return SpreadLabels(edges);
    }

    /// <summary>Nejmenší odstup dvou popisků, aby se nepřekrývaly.</summary>
    private const double LabelWidth = 44;

    /// <summary>Řádkový posun, o který se kolidující popisek uhne.</summary>
    private const double LabelLineHeight = 14;

    /// <summary>
    /// Odsune popisky, které by se překrývaly.
    /// </summary>
    /// <remarks>
    /// Umístění na trase samo o sobě nestačí: do jedné tabulky můžou mířit vazby zleva
    /// i zprava a jejich popisky skončí na stejném místě — v diagramu se to projeví jako
    /// slitý shluk typu „1:N:M". Kolize se proto po výpočtu dohledají a druhý popisek
    /// se posune o řádek výš.
    /// </remarks>
    internal static List<DiagramEdge> SpreadLabels(List<DiagramEdge> edges)
    {
        var obsazene = new List<(double X, double Y)>();
        var result = new List<DiagramEdge>(edges.Count);

        // Pevné pořadí, aby se stejný diagram vykreslil pokaždé stejně.
        foreach (var edge in edges.OrderBy(static e => e.LabelAt.Y).ThenBy(static e => e.LabelAt.X))
        {
            var at = edge.LabelAt;

            while (obsazene.Any(o => Math.Abs(o.X - at.X) < LabelWidth
                                     && Math.Abs(o.Y - at.Y) < LabelLineHeight))
            {
                at = (at.X, at.Y - LabelLineHeight);
            }

            obsazene.Add(at);
            result.Add(edge with { LabelAt = at });
        }

        return result;
    }

    private static double SideX(DiagramNode node, EdgeSide side) =>
        side == EdgeSide.Right ? node.X + node.Width : node.X;

    /// <summary>
    /// Rozprostře kotevní body po okrajích uzlů.
    /// </summary>
    /// <remarks>
    /// Kdyby všechny hrany vycházely ze středu, u tabulky s několika vazbami by se šipky
    /// slily do jednoho bodu a nešlo by poznat, která kam vede. Kotvy se proto rozdělí
    /// po výšce okraje a seřadí podle toho, kde leží protější uzel — tím se hrany
    /// na okraji nekříží.
    /// </remarks>
    private static Dictionary<(DbRelationship Relationship, bool IsSource), double> AssignAnchors(
        IReadOnlyList<Spoj> spoje)
    {
        var poptavka = new Dictionary<(DbObjectName Table, EdgeSide Side), List<(DbRelationship R, bool Source, double Toward)>>();

        foreach (var spoj in spoje)
        {
            var startSide = spoj.FromRight ? EdgeSide.Right : EdgeSide.Left;
            var endSide = spoj.FromRight ? EdgeSide.Left : EdgeSide.Right;

            Pridej(spoj.From.Table.Name, startSide, spoj.Relationship, true, spoj.To.CenterY);
            Pridej(spoj.To.Table.Name, endSide, spoj.Relationship, false, spoj.From.CenterY);
        }

        var result = new Dictionary<(DbRelationship, bool), double>();

        foreach (var ((table, side), zadosti) in poptavka)
        {
            var node = spoje
                .SelectMany<Spoj, DiagramNode>(static s => [s.From, s.To])
                .First(n => n.Table.Name == table);

            var serazene = zadosti.OrderBy(static z => z.Toward).ToList();

            // Krajních pár pixelů se nechává volných, aby kotva neseděla přesně v rohu.
            var top = node.Y + AnchorInset;
            var usable = Math.Max(node.Height - (2 * AnchorInset), 0);

            for (var i = 0; i < serazene.Count; i++)
            {
                // Jediná kotva patří doprostřed; víc kotev se roztáhne přes celou
                // použitelnou výšku, ať mají co největší rozestup.
                var podil = serazene.Count == 1 ? 0.5 : i / (serazene.Count - 1.0);

                result[(serazene[i].R, serazene[i].Source)] = top + (usable * podil);
            }
        }

        return result;

        void Pridej(DbObjectName table, EdgeSide side, DbRelationship r, bool source, double toward)
        {
            var key = (table, side);

            if (!poptavka.TryGetValue(key, out var list))
            {
                list = [];
                poptavka[key] = list;
            }

            list.Add((r, source, toward));
        }
    }

    /// <summary>Jak daleko před šipkou popisek stojí.</summary>
    private const double LabelOffset = 26;

    /// <summary>
    /// Popisek kardinality jde na poslední vodorovný úsek, kousek před šipku.
    /// </summary>
    /// <remarks>
    /// Původně seděl uprostřed nejdelšího úseku, jenže tam se popisky několika vazeb
    /// mířících do jedné tabulky slily do jedné nečitelné hromádky („N:M1:N"). U cíle
    /// má každá vazba svou kotvu, takže se rozejdou samy — a kardinalita ke konci vazby
    /// patří i významem.
    /// </remarks>
    internal static (double X, double Y) LabelPosition(IReadOnlyList<(double X, double Y)> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        for (var i = points.Count - 1; i >= 1; i--)
        {
            var a = points[i - 1];
            var b = points[i];

            if (Math.Abs(a.Y - b.Y) > 0.5)
            {
                continue;
            }

            // Na krátkém úseku by odsazení popisek posunulo až za jeho začátek.
            var odsazeni = Math.Min(LabelOffset, Math.Abs(b.X - a.X) / 2);

            return (b.X > a.X ? b.X - odsazeni : b.X + odsazeni, b.Y);
        }

        return points[0];
    }

    private static DiagramEdge SelfLoop(DbRelationship relationship, DiagramNode node)
    {
        var right = node.X + node.Width;
        var top = node.Y + (node.Height / 3);
        var bottom = node.Y + (node.Height * 2 / 3);
        var out_ = right + 30;

        IReadOnlyList<(double X, double Y)> points =
        [
            (right, top),
            (out_, top),
            (out_, bottom),
            (right, bottom),
        ];

        return new DiagramEdge
        {
            Relationship = relationship,
            Points = points,
            LabelAt = (out_ + 6, (top + bottom) / 2),
            IsSelfLoop = true,
        };
    }
}
