namespace DbsViewer.Ui.Model;

/// <summary>
/// Graf tabulek a vazeb. Umí odpovědět na otázku „co sousedí s touhle tabulkou",
/// na které stojí focus mode.
/// </summary>
/// <remarks>
/// Focus mode je jediný způsob, jak udělat diagram se stovkou tabulek čitelný.
/// Celoschéma je přehledová mapa, ne hlavní pohled — viz
/// <see href="../../../docs/adr/0007-vztahy-ne-cizi-klice.md">ADR-0007</see>.
/// </remarks>
public sealed class SchemaGraph
{
    private readonly Dictionary<DbObjectName, HashSet<DbObjectName>> _neighbours = [];

    public SchemaGraph(DatabaseSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        Schema = schema;

        foreach (var table in schema.Tables)
        {
            _neighbours[table.Name] = [];
        }

        foreach (var relationship in schema.Relationships)
        {
            Link(relationship.From, relationship.To);

            // Vazební tabulka je sousedem obou stran, i když se hrana kreslí sbaleně.
            if (relationship.ViaJoinTable is { } join)
            {
                Link(relationship.From, join);
                Link(relationship.To, join);
            }
        }
    }

    public DatabaseSchema Schema { get; }

    /// <summary>Tabulky přímo spojené se zadanou. Self-reference se mezi sousedy nepočítá.</summary>
    public IReadOnlySet<DbObjectName> NeighboursOf(DbObjectName table) =>
        _neighbours.TryGetValue(table, out var found) ? found : new HashSet<DbObjectName>();

    /// <summary>Počet vazeb tabulky. Slouží k určení velikosti uzlu v přehledové mapě.</summary>
    public int DegreeOf(DbObjectName table) => NeighboursOf(table).Count;

    /// <summary>
    /// Tabulky do zadaného počtu kroků od výchozí. Nula vrátí jen výchozí tabulku,
    /// jedna přidá přímé sousedy a tak dál.
    /// </summary>
    public IReadOnlySet<DbObjectName> Focus(DbObjectName start, int hops)
    {
        var visited = new HashSet<DbObjectName>();

        if (!_neighbours.ContainsKey(start))
        {
            return visited;
        }

        visited.Add(start);

        var frontier = new List<DbObjectName> { start };

        for (var hop = 0; hop < hops && frontier.Count > 0; hop++)
        {
            var next = new List<DbObjectName>();

            foreach (var current in frontier)
            {
                foreach (var neighbour in NeighboursOf(current))
                {
                    if (visited.Add(neighbour))
                    {
                        next.Add(neighbour);
                    }
                }
            }

            frontier = next;
        }

        return visited;
    }

    /// <summary>Tabulky ve výřezu, ve stabilním pořadí.</summary>
    public IReadOnlyList<DbTable> TablesIn(IReadOnlySet<DbObjectName> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        return [.. Schema.Tables.Where(t => names.Contains(t.Name))];
    }

    /// <summary>Vazby, jejichž obě strany jsou ve výřezu.</summary>
    public IReadOnlyList<DbRelationship> RelationshipsIn(IReadOnlySet<DbObjectName> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        return [.. Schema.Relationships.Where(r => names.Contains(r.From) && names.Contains(r.To))];
    }

    /// <summary>
    /// Vazby, které do tabulky vedou zvenčí. Odpovídá na otázku „co se rozbije,
    /// když tuhle tabulku změním" — v běžných nástrojích tenhle pohled chybí.
    /// </summary>
    public IReadOnlyList<DbRelationship> IncomingTo(DbObjectName table) =>
        [.. Schema.Relationships.Where(r => r.To == table && r.From != table)];

    /// <summary>Vazby vedoucí z tabulky ven.</summary>
    public IReadOnlyList<DbRelationship> OutgoingFrom(DbObjectName table) =>
        [.. Schema.Relationships.Where(r => r.From == table && r.To != table)];

    private void Link(DbObjectName left, DbObjectName right)
    {
        if (left == right)
        {
            return;
        }

        if (_neighbours.TryGetValue(left, out var leftSet))
        {
            leftSet.Add(right);
        }

        if (_neighbours.TryGetValue(right, out var rightSet))
        {
            rightSet.Add(left);
        }
    }
}
