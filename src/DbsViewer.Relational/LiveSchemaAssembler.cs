namespace DbsViewer.Relational;

/// <summary>
/// Sestaví <see cref="DatabaseSchema"/> ze surových introspekčních dat. Čistá funkce —
/// nesahá do databáze a nezná providera, takže jde otestovat celá bez připojení.
/// </summary>
public static class LiveSchemaAssembler
{
    /// <summary>Poskládá surová data do schématu podle zadaného nastavení čtení.</summary>
    public static DatabaseSchema Build(
        RawSchema raw,
        SchemaReadOptions options,
        DbProviderKind provider,
        string? providerName,
        string sourceName)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(options);

        var visibleTables = raw.Tables
            .Select(static t => (Raw: t, Name: new DbObjectName(t.Schema, t.Name)))
            .Where(t => options.IsVisible(t.Name))
            .ToList();

        var visible = new HashSet<DbObjectName>(visibleTables.Select(static t => t.Name));

        var columns = GroupByTable(raw.Columns, static c => new DbObjectName(c.Schema, c.Table));
        var keyColumns = GroupByTable(raw.KeyColumns, static k => new DbObjectName(k.Schema, k.Table));
        var indexes = GroupByTable(raw.Indexes, static i => new DbObjectName(i.Schema, i.Table));
        var indexColumns = GroupByTable(raw.IndexColumns, static i => new DbObjectName(i.Schema, i.Table));
        var foreignKeys = GroupByTable(raw.ForeignKeys, static f => new DbObjectName(f.Schema, f.Table));
        var foreignKeyColumns = GroupByTable(raw.ForeignKeyColumns, static f => new DbObjectName(f.Schema, f.Table));
        var checks = GroupByTable(raw.CheckConstraints, static c => new DbObjectName(c.Schema, c.Table));

        var rowCounts = raw.RowCounts.ToDictionary(
            static r => new DbObjectName(r.Schema, r.Table),
            static r => r.Rows);

        var tables = new List<DbTable>(visibleTables.Count);

        foreach (var (rawTable, name) in visibleTables)
        {
            var primaryKey = BuildPrimaryKey(Get(keyColumns, name));
            var tableForeignKeys = BuildForeignKeys(Get(foreignKeys, name), Get(foreignKeyColumns, name));

            tables.Add(new DbTable
            {
                Name = name,
                Comment = rawTable.Comment,
                IsView = rawTable.IsView,
                RowCountEstimate = options.IncludeRowCounts && rowCounts.TryGetValue(name, out var rows)
                    ? rows
                    : null,
                Columns = BuildColumns(Get(columns, name), primaryKey, tableForeignKeys),
                PrimaryKey = primaryKey,
                Indexes = BuildIndexes(Get(indexes, name), Get(indexColumns, name)),
                ForeignKeys = tableForeignKeys,
                CheckConstraints = [.. Get(checks, name)
                    .Select(static c => new DbCheckConstraint { Name = c.Name, Sql = c.Sql })
                    .OrderBy(static c => c.Name, StringComparer.Ordinal)],
            });
        }

        tables.Sort(static (a, b) => a.Name.CompareTo(b.Name));

        IReadOnlySet<DbObjectName> joinTables = options.DetectJoinTables
            ? JoinTableDetector.Detect(tables)
            : new HashSet<DbObjectName>();
        if (joinTables.Count > 0)
        {
            for (var i = 0; i < tables.Count; i++)
            {
                if (joinTables.Contains(tables[i].Name))
                {
                    tables[i] = tables[i] with { IsJoinTable = true };
                }
            }
        }

        return new DatabaseSchema
        {
            DatabaseName = raw.DatabaseName,
            ProviderName = providerName,
            Provider = provider,
            SourceKind = SchemaSourceKind.LiveDatabase,
            SourceName = sourceName,
            DefaultSchema = raw.DefaultSchema,
            Tables = tables,
            Relationships = RelationshipBuilder.Build(tables, visible, joinTables),
            Migrations = options.IncludeMigrations ? BuildMigrations(raw.AppliedMigrations) : [],
            Warnings = raw.Warnings,
        };
    }

    private static List<DbColumn> BuildColumns(
        List<RawColumn> raw,
        DbPrimaryKey? primaryKey,
        List<DbForeignKey> foreignKeys)
    {
        var pkColumns = new HashSet<string>(
            primaryKey?.Columns ?? [],
            StringComparer.OrdinalIgnoreCase);

        var fkColumns = new HashSet<string>(
            foreignKeys.SelectMany(static f => f.Columns),
            StringComparer.OrdinalIgnoreCase);

        return
        [
            .. raw
                .OrderBy(static c => c.Ordinal)
                .Select(c => new DbColumn
                {
                    Name = c.Name,
                    Ordinal = c.Ordinal,
                    StoreType = c.StoreType,
                    IsNullable = c.IsNullable,
                    IsPrimaryKey = pkColumns.Contains(c.Name),
                    IsForeignKey = fkColumns.Contains(c.Name),
                    IsIdentity = c.IsIdentity,
                    IsComputed = c.IsComputed,
                    ComputedSql = c.ComputedSql,
                    IsStored = c.IsStored,
                    DefaultValueSql = c.DefaultValueSql,
                    MaxLength = c.MaxLength,
                    Precision = c.Precision,
                    Scale = c.Scale,
                    Collation = c.Collation,
                    Comment = c.Comment,
                    ValueGenerated = c.IsIdentity ? DbValueGenerated.OnAdd : DbValueGenerated.Never,
                }),
        ];
    }

    private static DbPrimaryKey? BuildPrimaryKey(List<RawKeyColumn> raw)
    {
        if (raw.Count == 0)
        {
            return null;
        }

        var ordered = raw.OrderBy(static k => k.Position).ToList();

        return new DbPrimaryKey
        {
            Name = ordered[0].ConstraintName,
            Columns = [.. ordered.Select(static k => k.Column)],
            IsClustered = ordered[0].IsClustered,
        };
    }

    private static List<DbIndex> BuildIndexes(List<RawIndex> raw, List<RawIndexColumn> columns)
    {
        var byIndex = columns
            .GroupBy(static c => c.IndexName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static g => g.Key, static g => g.OrderBy(static c => c.Position).ToList(),
                StringComparer.OrdinalIgnoreCase);

        var indexes = new List<DbIndex>(raw.Count);

        foreach (var index in raw)
        {
            var all = byIndex.TryGetValue(index.Name, out var found) ? found : [];
            var key = all.Where(static c => !c.IsIncluded).ToList();

            indexes.Add(new DbIndex
            {
                Name = index.Name,
                Columns = [.. key.Select(static c => c.Column)],
                IncludedColumns = [.. all.Where(static c => c.IsIncluded).Select(static c => c.Column)],
                IsUnique = index.IsUnique,
                IsClustered = index.IsClustered,
                FilterSql = index.FilterSql,
                IsDescending = key.Any(static c => c.IsDescending)
                    ? [.. key.Select(static c => c.IsDescending)]
                    : [],
            });
        }

        indexes.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
        return indexes;
    }

    private static List<DbForeignKey> BuildForeignKeys(
        List<RawForeignKey> raw,
        List<RawForeignKeyColumn> columns)
    {
        var byForeignKey = columns
            .GroupBy(static c => c.ForeignKeyName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static g => g.Key, static g => g.OrderBy(static c => c.Position).ToList(),
                StringComparer.OrdinalIgnoreCase);

        var foreignKeys = new List<DbForeignKey>(raw.Count);

        foreach (var foreignKey in raw)
        {
            var pairs = byForeignKey.TryGetValue(foreignKey.Name, out var found) ? found : [];

            foreignKeys.Add(new DbForeignKey
            {
                Name = foreignKey.Name,
                Columns = [.. pairs.Select(static c => c.Column)],
                PrincipalTable = new DbObjectName(foreignKey.PrincipalSchema, foreignKey.PrincipalTable),
                PrincipalColumns = [.. pairs.Select(static c => c.PrincipalColumn)],
                DeleteBehavior = ParseDeleteAction(foreignKey.DeleteAction),
            });
        }

        foreignKeys.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
        return foreignKeys;
    }

    /// <summary>
    /// Převod textového názvu akce na model. Přijímá tvary ze SQL Serveru
    /// (<c>NO_ACTION</c>) i ze SQLite (<c>NO ACTION</c>), bez ohledu na velikost písmen.
    /// </summary>
    public static DbDeleteBehavior ParseDeleteAction(string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return DbDeleteBehavior.Unknown;
        }

        return action.Replace('_', ' ').Trim().ToUpperInvariant() switch
        {
            "NO ACTION" => DbDeleteBehavior.NoAction,
            "RESTRICT" => DbDeleteBehavior.Restrict,
            "CASCADE" => DbDeleteBehavior.Cascade,
            "SET NULL" => DbDeleteBehavior.SetNull,
            "SET DEFAULT" => DbDeleteBehavior.SetDefault,
            _ => DbDeleteBehavior.Unknown,
        };
    }

    private static List<DbMigration> BuildMigrations(IReadOnlyList<string> applied) =>
    [
        .. applied
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(static id => new DbMigration
            {
                Id = id,
                AppliedInDatabase = true,
                PresentInAssembly = false,
            }),
    ];

    private static Dictionary<DbObjectName, List<T>> GroupByTable<T>(
        IReadOnlyList<T> items,
        Func<T, DbObjectName> keySelector)
    {
        var map = new Dictionary<DbObjectName, List<T>>();

        foreach (var item in items)
        {
            var key = keySelector(item);
            if (!map.TryGetValue(key, out var list))
            {
                map[key] = list = [];
            }

            list.Add(item);
        }

        return map;
    }

    private static List<T> Get<T>(Dictionary<DbObjectName, List<T>> map, DbObjectName key) =>
        map.TryGetValue(key, out var list) ? list : [];
}
