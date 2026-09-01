namespace DbsViewer.Ui.Model;

/// <summary>Tabulka s jedním číslem — pro žebříčky v přehledu.</summary>
/// <param name="Table">Jméno tabulky.</param>
/// <param name="Value">Hodnota, podle které se řadí.</param>
public readonly record struct TableStat(DbObjectName Table, long Value);

/// <summary>Datový typ a kolikrát se ve schématu objevuje.</summary>
/// <param name="Type">Jméno typu tak, jak ho hlásí databáze.</param>
/// <param name="Count">Počet sloupců.</param>
public readonly record struct TypeStat(string Type, int Count);

/// <summary>Databázové schéma a kolik má tabulek.</summary>
/// <param name="Schema">Jméno schématu, nebo <c>null</c> pro výchozí.</param>
/// <param name="TableCount">Počet tabulek v něm.</param>
public readonly record struct SchemaStat(string? Schema, int TableCount);

/// <summary>
/// Souhrn celé databáze pro úvodní přehled.
/// </summary>
/// <remarks>
/// Počítá se v UI z už načteného schématu, ne na serveru: data jsou po ruce, výpočet je
/// průchod přes seznamy a server tím pádem nemusí mít další endpoint ani cache.
/// </remarks>
public sealed record SchemaSummary
{
    /// <summary>Počet tabulek bez pohledů.</summary>
    public int TableCount { get; init; }

    /// <summary>Počet pohledů.</summary>
    public int ViewCount { get; init; }

    /// <summary>Počet sloupců ve všech tabulkách.</summary>
    public int ColumnCount { get; init; }

    /// <summary>Počet indexů.</summary>
    public int IndexCount { get; init; }

    /// <summary>Počet vazeb.</summary>
    public int RelationshipCount { get; init; }

    /// <summary>Počet vazebních tabulek N:M.</summary>
    public int JoinTableCount { get; init; }

    /// <summary>Počet použitých databázových schémat.</summary>
    public int SchemaCount { get; init; }

    /// <summary>Počet nullable sloupců.</summary>
    public int NullableColumnCount { get; init; }

    /// <summary>Počet počítaných sloupců.</summary>
    public int ComputedColumnCount { get; init; }

    /// <summary>
    /// Odhad počtu řádků v celé databázi, nebo <c>null</c>, když ho nezná ani jedna
    /// tabulka — u schématu čteného jen z EF modelu to tak je vždy.
    /// </summary>
    public long? TotalRowEstimate { get; init; }

    /// <summary>Největší tabulky podle odhadu řádků.</summary>
    public IReadOnlyList<TableStat> LargestTables { get; init; } = [];

    /// <summary>Tabulky s nejvíce vazbami — uzly, kolem kterých se schéma točí.</summary>
    public IReadOnlyList<TableStat> MostConnected { get; init; } = [];

    /// <summary>Nejčastější datové typy.</summary>
    public IReadOnlyList<TypeStat> CommonTypes { get; init; } = [];

    /// <summary>Rozdělení tabulek podle databázových schémat.</summary>
    public IReadOnlyList<SchemaStat> BySchema { get; init; } = [];

    /// <summary>Tabulky bez primárního klíče.</summary>
    public IReadOnlyList<DbObjectName> WithoutPrimaryKey { get; init; } = [];

    /// <summary>Tabulky, které s ničím nesouvisí.</summary>
    public IReadOnlyList<DbObjectName> Isolated { get; init; } = [];

    /// <summary>
    /// Cizí klíče bez indexu na odkazující straně.
    /// </summary>
    /// <remarks>
    /// Klasický zdroj pomalých dotazů: bez indexu musí databáze při JOINu i při mazání
    /// rodiče projít celou tabulku. Hlásí se jako podnět, ne jako chyba — u malých
    /// číselníků index nemá smysl.
    /// </remarks>
    public IReadOnlyList<string> UnindexedForeignKeys { get; init; } = [];

    /// <summary>Kolik nejvýše se vypisuje v každém žebříčku.</summary>
    public const int TopCount = 5;

    /// <summary>Spočítá souhrn ze schématu.</summary>
    public static SchemaSummary From(DatabaseSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var tables = schema.Tables;
        var stupne = Degrees(schema);

        var odhady = tables
            .Where(static t => t.RowCountEstimate is not null)
            .ToList();

        return new SchemaSummary
        {
            TableCount = tables.Count(static t => !t.IsView),
            ViewCount = tables.Count(static t => t.IsView),
            ColumnCount = tables.Sum(static t => t.Columns.Count),
            IndexCount = tables.Sum(static t => t.Indexes.Count),
            RelationshipCount = schema.Relationships.Count,
            JoinTableCount = tables.Count(static t => t.IsJoinTable),
            SchemaCount = tables.Select(static t => t.Name.Schema).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            NullableColumnCount = tables.Sum(static t => t.Columns.Count(static c => c.IsNullable)),
            ComputedColumnCount = tables.Sum(static t => t.Columns.Count(static c => c.IsComputed)),

            TotalRowEstimate = odhady.Count > 0 ? odhady.Sum(static t => t.RowCountEstimate!.Value) : null,

            LargestTables = odhady
                .OrderByDescending(static t => t.RowCountEstimate!.Value)
                .ThenBy(static t => t.Qualified, StringComparer.OrdinalIgnoreCase)
                .Take(TopCount)
                .Select(static t => new TableStat(t.Name, t.RowCountEstimate!.Value))
                .ToList(),

            MostConnected = stupne
                .Where(static p => p.Value > 0)
                .OrderByDescending(static p => p.Value)
                .ThenBy(static p => p.Key.Qualified, StringComparer.OrdinalIgnoreCase)
                .Take(TopCount)
                .Select(static p => new TableStat(p.Key, p.Value))
                .ToList(),

            CommonTypes = tables
                .SelectMany(static t => t.Columns)
                .GroupBy(static c => c.StoreType, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(static g => g.Count())
                .ThenBy(static g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Take(TopCount)
                .Select(static g => new TypeStat(g.Key, g.Count()))
                .ToList(),

            BySchema = tables
                .GroupBy(static t => t.Name.Schema, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(static g => g.Count())
                .ThenBy(static g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static g => new SchemaStat(g.Key, g.Count()))
                .ToList(),

            WithoutPrimaryKey = tables
                .Where(static t => !t.IsView && t.PrimaryKey is null)
                .Select(static t => t.Name)
                .OrderBy(static n => n.Qualified, StringComparer.OrdinalIgnoreCase)
                .ToList(),

            Isolated = stupne
                .Where(static p => p.Value == 0)
                .Select(static p => p.Key)
                .OrderBy(static n => n.Qualified, StringComparer.OrdinalIgnoreCase)
                .ToList(),

            UnindexedForeignKeys = UnindexedFks(tables),
        };
    }

    /// <summary>
    /// S kolika jinými tabulkami každá tabulka souvisí.
    /// </summary>
    /// <remarks>
    /// Počítají se dvojice tabulek, ne hrany: tatáž dvojice spojená vztahem i cizím
    /// klíčem je pořád jedna vazba. Kromě odvozených vztahů se proto berou v potaz
    /// i cizí klíče samotné — vazební tabulka N:M se ve vztazích sbalí do přímé vazby
    /// mezi krajními tabulkami a sama v seznamu vůbec není, takže by bez nich vyšla
    /// jako osamocená, což je přesně naopak, než jak to je.
    /// </remarks>
    private static Dictionary<DbObjectName, int> Degrees(DatabaseSchema schema)
    {
        var dvojice = new HashSet<(DbObjectName A, DbObjectName B)>();

        foreach (var relationship in schema.Relationships)
        {
            Pridej(relationship.From, relationship.To);
        }

        foreach (var table in schema.Tables)
        {
            foreach (var fk in table.ForeignKeys)
            {
                Pridej(table.Name, fk.PrincipalTable);
            }
        }

        var stupne = schema.Tables.ToDictionary(static t => t.Name, static _ => 0);

        foreach (var (a, b) in dvojice)
        {
            Zvys(a);

            // Vazba do sebe sama je jedna vazba, ne dvě.
            if (b != a)
            {
                Zvys(b);
            }
        }

        return stupne;

        // Dvojice se ukládá v ustáleném pořadí, aby A→B a B→A byly totéž.
        void Pridej(DbObjectName from, DbObjectName to) =>
            dvojice.Add(string.Compare(from.Qualified, to.Qualified, StringComparison.OrdinalIgnoreCase) <= 0
                ? (from, to)
                : (to, from));

        // Druhá strana může být mimo načtené schéma — odfiltrovaná nebo v jiném schématu.
        // Vazba se tím nepřestává počítat té tabulce, kterou zobrazujeme; jinak by
        // vyšla jako osamocená, přestože odkaz má.
        void Zvys(DbObjectName name)
        {
            if (stupne.TryGetValue(name, out var current))
            {
                stupne[name] = current + 1;
            }
        }
    }

    /// <summary>Cizí klíče, jejichž sloupce nezačínají žádný index.</summary>
    private static List<string> UnindexedFks(IReadOnlyList<DbTable> tables)
    {
        var nalezy = new List<string>();

        foreach (var table in tables)
        {
            foreach (var fk in table.ForeignKeys)
            {
                // Stačí, když index sloupci FK začíná — pro vyhledání i mazání
                // se pak dá použít jeho prefix.
                var kryty = table.Indexes.Any(i => ZacinaSloupci(i.Columns, fk.Columns))
                    || ZacinaSloupci(table.PrimaryKey?.Columns ?? [], fk.Columns);

                if (!kryty)
                {
                    nalezy.Add($"{table.Qualified}.{string.Join(", ", fk.Columns)}");
                }
            }
        }

        return nalezy;
    }

    private static bool ZacinaSloupci(IReadOnlyList<string> index, IReadOnlyList<string> fk)
    {
        if (fk.Count == 0 || index.Count < fk.Count)
        {
            return false;
        }

        for (var i = 0; i < fk.Count; i++)
        {
            if (!string.Equals(index[i], fk[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
