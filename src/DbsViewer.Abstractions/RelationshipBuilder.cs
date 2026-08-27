namespace DbsViewer;

/// <summary>
/// Odvodí z cizích klíčů vztahy určené k vykreslení. Používá ho živá introspekce;
/// zdroj z EF modelu si navíc přidává N:M ze skip-navigací, které v databázi vidět nejsou.
/// </summary>
public static class RelationshipBuilder
{
    /// <summary>Sestaví vztahy z cizích klíčů zadaných tabulek.</summary>
    /// <param name="tables">Tabulky, ze kterých se čtou cizí klíče.</param>
    /// <param name="visible">Tabulky, které se smějí objevit v diagramu.</param>
    /// <param name="joinTables">
    /// Vazební tabulky. Dvojice jejich cizích klíčů se sbalí do jediné hrany N:M.
    /// </param>
    public static IReadOnlyList<DbRelationship> Build(
        IReadOnlyList<DbTable> tables,
        IReadOnlySet<DbObjectName> visible,
        IReadOnlySet<DbObjectName> joinTables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(visible);
        ArgumentNullException.ThrowIfNull(joinTables);

        var relationships = new List<DbRelationship>();
        var uniqueColumnSets = BuildUniqueColumnSets(tables);

        foreach (var table in tables)
        {
            if (joinTables.Contains(table.Name))
            {
                var collapsed = TryCollapseJoinTable(table, visible);
                if (collapsed is not null)
                {
                    relationships.Add(collapsed);
                    continue;
                }
            }

            relationships.AddRange(BuildDirect(table, visible, uniqueColumnSets));
        }

        relationships.Sort(static (a, b) => string.CompareOrdinal(a.Id, b.Id));
        return relationships;
    }

    private static IEnumerable<DbRelationship> BuildDirect(
        DbTable table,
        IReadOnlySet<DbObjectName> visible,
        IReadOnlyDictionary<DbObjectName, HashSet<string>> uniqueColumnSets)
    {
        var primaryKeyColumns = new HashSet<string>(
            table.PrimaryKey?.Columns ?? [],
            StringComparer.OrdinalIgnoreCase);

        foreach (var foreignKey in table.ForeignKeys)
        {
            if (!visible.Contains(table.Name) || !visible.Contains(foreignKey.PrincipalTable))
            {
                continue;
            }

            var isUnique = foreignKey.IsUnique
                || (uniqueColumnSets.TryGetValue(table.Name, out var sets)
                    && sets.Contains(ColumnSetKey(foreignKey.Columns)));

            yield return new DbRelationship
            {
                Id = ForeignKeyId(table.Name, foreignKey.Name),
                From = table.Name,
                To = foreignKey.PrincipalTable,
                Cardinality = isUnique ? DbCardinality.OneToOne : DbCardinality.OneToMany,
                ForeignKeyName = foreignKey.Name,
                DeleteBehavior = foreignKey.DeleteBehavior,
                IsRequired = IsRequired(table, foreignKey),
                IsIdentifying = foreignKey.Columns.Count > 0
                    && foreignKey.Columns.All(primaryKeyColumns.Contains),
                FromColumns = foreignKey.Columns,
                ToColumns = foreignKey.PrincipalColumns,
                FromNavigation = foreignKey.NavigationName,
                ToNavigation = foreignKey.InverseNavigationName,
            };
        }
    }

    private static DbRelationship? TryCollapseJoinTable(DbTable table, IReadOnlySet<DbObjectName> visible)
    {
        var left = table.ForeignKeys[0].PrincipalTable;
        var right = table.ForeignKeys[1].PrincipalTable;

        if (!visible.Contains(left) || !visible.Contains(right))
        {
            return null;
        }

        // Pořadí stran je dané abecedou, aby se stejný vztah neuložil dvakrát pod jiným Id.
        var (from, to, fromKey, toKey) = left.CompareTo(right) <= 0
            ? (left, right, table.ForeignKeys[0], table.ForeignKeys[1])
            : (right, left, table.ForeignKeys[1], table.ForeignKeys[0]);

        return new DbRelationship
        {
            Id = ManyToManyId(from, to, table.Name),
            From = from,
            To = to,
            Cardinality = DbCardinality.ManyToMany,
            ViaJoinTable = table.Name,
            DeleteBehavior = fromKey.DeleteBehavior,
            IsRequired = IsRequired(table, fromKey) && IsRequired(table, toKey),
            FromColumns = fromKey.PrincipalColumns,
            ToColumns = toKey.PrincipalColumns,
        };
    }

    /// <summary>Vazba je povinná, když žádný z jejích sloupců nepřipouští NULL.</summary>
    public static bool IsRequired(DbTable table, DbForeignKey foreignKey)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(foreignKey);

        if (foreignKey.IsRequired)
        {
            return true;
        }

        if (foreignKey.Columns.Count == 0)
        {
            return false;
        }

        foreach (var columnName in foreignKey.Columns)
        {
            if (table.FindColumn(columnName) is not { IsNullable: false })
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Stabilní identifikátor vztahu odvozeného z cizího klíče.</summary>
    public static string ForeignKeyId(DbObjectName table, string? foreignKeyName) =>
        $"fk:{table}|{foreignKeyName}";

    /// <summary>Stabilní identifikátor vztahu N:M.</summary>
    public static string ManyToManyId(DbObjectName from, DbObjectName to, DbObjectName joinTable) =>
        from.CompareTo(to) <= 0
            ? $"m2m:{from}<->{to}|{joinTable}"
            : $"m2m:{to}<->{from}|{joinTable}";

    /// <summary>
    /// Sady sloupců pokryté unikátním indexem. Cizí klíč nad takovou sadou je vztah 1:1.
    /// </summary>
    private static Dictionary<DbObjectName, HashSet<string>> BuildUniqueColumnSets(
        IReadOnlyList<DbTable> tables)
    {
        var map = new Dictionary<DbObjectName, HashSet<string>>();

        foreach (var table in tables)
        {
            var sets = new HashSet<string>(StringComparer.Ordinal);

            foreach (var index in table.Indexes)
            {
                if (index.IsUnique)
                {
                    sets.Add(ColumnSetKey(index.Columns));
                }
            }

            if (table.PrimaryKey is { } primaryKey)
            {
                sets.Add(ColumnSetKey(primaryKey.Columns));
            }

            map[table.Name] = sets;
        }

        return map;
    }

    private static string ColumnSetKey(IReadOnlyList<string> columns) =>
        string.Join('', columns.Select(static c => c.ToUpperInvariant()).Order(StringComparer.Ordinal));
}
