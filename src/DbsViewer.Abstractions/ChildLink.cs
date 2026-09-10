namespace DbsViewer;

/// <summary>Podmínka „sloupec se rovná hodnotě" navázaná na konkrétní nadřazený řádek.</summary>
/// <param name="Column">Jméno sloupce v podřízené tabulce.</param>
/// <param name="Value">Hodnota z nadřazeného řádku.</param>
public sealed record ChildFilter(string Column, string Value);

/// <summary>
/// Cesta z jednoho řádku nadřazené tabulky na jeho podřízené záznamy.
/// </summary>
/// <remarks>
/// Odpovídá na otázku, kterou si člověk nad daty klade nejčastěji: „a co k tomuhle
/// zákazníkovi patří". Bez toho se musela podřízená tabulka najít ručně, přepnout na
/// data a do filtru opsat cizí klíč — přitom všechno potřebné schéma už zná.
///
/// Vychází se z cizích klíčů, ne z <see cref="DbRelationship"/>: vztah N:M je tam
/// sloučený do jedné hrany přes vazební tabulku, kdežto pro skok na data je potřeba
/// právě ta vazební tabulka i konkrétní sloupce, které se mají porovnat.
/// </remarks>
public sealed record ChildLink
{
    /// <summary>Podřízená tabulka — ta, která nese cizí klíč.</summary>
    public required DbObjectName Table { get; init; }

    /// <summary>Jméno cizího klíče. Odlišuje dvě vazby mezi týmiž tabulkami.</summary>
    public required string ForeignKeyName { get; init; }

    /// <summary>Sloupce cizího klíče v podřízené tabulce.</summary>
    public required IReadOnlyList<string> Columns { get; init; }

    /// <summary>Odpovídající sloupce v nadřazené tabulce.</summary>
    public required IReadOnlyList<string> ParentColumns { get; init; }

    /// <summary>Navigace z nadřazené entity na podřízené, když ji model zná.</summary>
    public string? NavigationName { get; init; }

    /// <summary>
    /// Sloupec, u kterého se odkaz v mřížce nabízí.
    /// </summary>
    /// <remarks>
    /// U složeného klíče se nabízí jen u prvního sloupce, ne u všech — jinak by tentýž
    /// odkaz visel v řádku dvakrát a nebylo by poznat, že jde o jednu vazbu.
    /// </remarks>
    public string AnchorColumn => ParentColumns[0];

    /// <summary>
    /// Podmínka pro data podřízené tabulky, nebo <c>null</c>, když ji z řádku poskládat nejde.
    /// </summary>
    /// <remarks>
    /// Když je kterákoli hodnota nadřazeného sloupce NULL, odkaz nedává smysl: cizí klíč
    /// se na NULL nenaváže, takže by výsledek byl vždy prázdný. Stejně dopadne sloupec,
    /// který v načtené stránce není — třeba proto, že je zamaskovaný.
    /// </remarks>
    /// <param name="parentValue">Hodnota sloupce nadřazeného řádku, nebo <c>null</c>.</param>
    public IReadOnlyList<ChildFilter>? FilterFor(Func<string, string?> parentValue)
    {
        ArgumentNullException.ThrowIfNull(parentValue);

        var podminky = new List<ChildFilter>(Columns.Count);

        for (var i = 0; i < Columns.Count; i++)
        {
            if (parentValue(ParentColumns[i]) is not { } hodnota)
            {
                return null;
            }

            podminky.Add(new ChildFilter(Columns[i], hodnota));
        }

        return podminky;
    }

    public override string ToString() =>
        $"{Table} ({string.Join(", ", Columns)}) → {string.Join(", ", ParentColumns)}";
}

/// <summary>Odvození odkazů na podřízené záznamy ze schématu.</summary>
public static class ChildLinks
{
    /// <summary>
    /// Cizí klíče, které míří na zadanou tabulku, jako odkazy na podřízené záznamy.
    /// </summary>
    /// <remarks>
    /// Klíč s jiným počtem sloupců na obou stranách se přeskočí. Platné schéma takový
    /// klíč nemá, ale schéma sem přichází i z živé databáze a rozbité čtení nesmí
    /// prohlížečku shodit — párování po indexech by na něm spadlo.
    ///
    /// Pohledy se vynechávají: nemají cizí klíče, a i kdyby je čtení odněkud vyrobilo,
    /// odkaz na ně by z nadřazeného řádku nedával smysl.
    /// </remarks>
    public static IReadOnlyList<ChildLink> For(DatabaseSchema schema, DbObjectName parent)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var odkazy = new List<ChildLink>();

        foreach (var tabulka in schema.Tables)
        {
            foreach (var klic in tabulka.ForeignKeys)
            {
                if (klic.PrincipalTable != parent
                    || klic.Columns.Count == 0
                    || klic.Columns.Count != klic.PrincipalColumns.Count)
                {
                    continue;
                }

                odkazy.Add(new ChildLink
                {
                    Table = tabulka.Name,
                    ForeignKeyName = klic.Name,
                    Columns = klic.Columns,
                    ParentColumns = klic.PrincipalColumns,
                    NavigationName = klic.InverseNavigationName,
                });
            }
        }

        return odkazy;
    }

    /// <summary>Odkazy nabízené u konkrétního sloupce nadřazené tabulky.</summary>
    public static IReadOnlyList<ChildLink> At(IEnumerable<ChildLink> links, string column)
    {
        ArgumentNullException.ThrowIfNull(links);

        return [.. links.Where(o => string.Equals(o.AnchorColumn, column, StringComparison.OrdinalIgnoreCase))];
    }
}
