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
        var nodes = PlaceNodes(tables, layers, expanded);
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
    /// takže vazby přirozeně směřují doleva. Cykly se přeruší omezením hloubky.
    /// </summary>
    internal static Dictionary<DbObjectName, int> AssignLayers(
        IReadOnlyList<DbTable> tables,
        IReadOnlyList<DbRelationship> relationships)
    {
        var layers = tables.ToDictionary(static t => t.Name, static _ => 0);
        var dependencies = new Dictionary<DbObjectName, List<DbObjectName>>();

        foreach (var table in tables)
        {
            dependencies[table.Name] = [];
        }

        foreach (var relationship in relationships)
        {
            if (relationship.From != relationship.To
                && dependencies.TryGetValue(relationship.From, out var list)
                && layers.ContainsKey(relationship.To))
            {
                list.Add(relationship.To);
            }
        }

        // Iterace místo rekurze: u cyklického schématu by rekurze skončila přetečením.
        // Počet průchodů je omezený počtem tabulek, což je nejhorší možná délka řetězu.
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

    private static List<DiagramNode> PlaceNodes(
        IReadOnlyList<DbTable> tables,
        Dictionary<DbObjectName, int> layers,
        IReadOnlySet<DbObjectName>? expanded)
    {
        var byLayer = tables
            .GroupBy(t => layers[t.Name])
            .OrderBy(static g => g.Key)
            .ToList();

        var nodes = new List<DiagramNode>(tables.Count);
        var x = Margin;

        foreach (var layer in byLayer)
        {
            var ordered = layer.OrderBy(static t => t.Qualified, StringComparer.OrdinalIgnoreCase).ToList();
            var y = Margin;

            foreach (var table in ordered)
            {
                var height = NodeHeight(table, expanded?.Contains(table.Name) ?? false);

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
    internal static double NodeHeight(DbTable table, bool isExpanded)
    {
        var rows = isExpanded
            ? table.Columns.Count
            : table.Columns.Count(static c => c.IsPrimaryKey || c.IsForeignKey);

        return HeaderHeight + (Math.Max(rows, 1) * RowHeight) + 8;
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

    private static List<DiagramEdge> RouteEdges(
        List<DiagramNode> nodes,
        IReadOnlyList<DbRelationship> relationships)
    {
        var byName = nodes.ToDictionary(static n => n.Table.Name);
        var edges = new List<DiagramEdge>();

        foreach (var relationship in relationships)
        {
            if (!byName.TryGetValue(relationship.From, out var from)
                || !byName.TryGetValue(relationship.To, out var to))
            {
                continue;
            }

            edges.Add(relationship.From == relationship.To
                ? SelfLoop(relationship, from)
                : Connect(relationship, from, to));
        }

        return edges;
    }

    private static DiagramEdge Connect(DbRelationship relationship, DiagramNode from, DiagramNode to)
    {
        // Vazba vede z levého okraje závislé tabulky na pravý okraj principální,
        // pokud je principální vlevo. Jinak se strany prohodí.
        var fromRight = from.CenterX <= to.CenterX;

        var start = fromRight ? (from.X + from.Width, from.CenterY) : (from.X, from.CenterY);
        var end = fromRight ? (to.X, to.CenterY) : (to.X + to.Width, to.CenterY);

        var midX = (start.Item1 + end.Item1) / 2;

        IReadOnlyList<(double X, double Y)> points =
        [
            start,
            (midX, start.Item2),
            (midX, end.Item2),
            end,
        ];

        return new DiagramEdge
        {
            Relationship = relationship,
            Points = points,
            LabelAt = (midX, (start.Item2 + end.Item2) / 2),
        };
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
