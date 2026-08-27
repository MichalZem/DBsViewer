namespace DbsViewer.Analysis;

/// <summary>
/// Sloučí schéma z EF modelu se schématem z živé databáze do jednoho pohledu.
/// </summary>
/// <remarks>
/// Pravidlo je jednoduché: <b>databáze má pravdu o tom, co v ní je</b> — typy, indexy,
/// defaulty, počty řádků. <b>Model má pravdu o záměru</b> — navigace, CLR typy, komentáře,
/// dědičnost. Kde jeden zdroj mlčí, doplní ho druhý.
/// </remarks>
public static class SchemaMerger
{
    /// <summary>Sloučí model a databázi. Objekty z obou stran zůstanou zachované.</summary>
    public static DatabaseSchema Merge(DatabaseSchema model, DatabaseSchema database)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(database);

        var modelTables = model.Tables.ToDictionary(static t => t.Name);
        var databaseTables = database.Tables.ToDictionary(static t => t.Name);

        var allNames = new SortedSet<DbObjectName>(modelTables.Keys);
        allNames.UnionWith(databaseTables.Keys);

        var tables = new List<DbTable>(allNames.Count);

        foreach (var name in allNames)
        {
            var hasModel = modelTables.TryGetValue(name, out var modelTable);
            var hasDatabase = databaseTables.TryGetValue(name, out var databaseTable);

            tables.Add((hasModel, hasDatabase) switch
            {
                (true, true) => MergeTable(modelTable!, databaseTable!),
                (true, false) => modelTable!,
                _ => databaseTable!,
            });
        }

        // Vztahy z modelu mají přednost — jen ony znají navigace a skip-navigace pro N:M.
        var relationships = MergeRelationships(model.Relationships, database.Relationships);

        return new DatabaseSchema
        {
            DatabaseName = database.DatabaseName ?? model.DatabaseName,
            ProviderName = model.ProviderName ?? database.ProviderName,
            Provider = model.Provider != DbProviderKind.Unknown ? model.Provider : database.Provider,
            SourceKind = SchemaSourceKind.Merged,
            SourceName = $"{model.SourceName} + {database.SourceName}",
            DefaultSchema = model.DefaultSchema ?? database.DefaultSchema,
            GeneratedAtUtc = database.GeneratedAtUtc > model.GeneratedAtUtc
                ? database.GeneratedAtUtc
                : model.GeneratedAtUtc,
            Tables = tables,
            Relationships = relationships,
            Migrations = MergeMigrations(model.Migrations, database.Migrations),
            Warnings = [.. model.Warnings, .. database.Warnings],
        };
    }

    private static DbTable MergeTable(DbTable model, DbTable database) => new()
    {
        Name = model.Name,

        // Komentář může být v modelu i v databázi; model bývá aktuálnější.
        Comment = model.Comment ?? database.Comment,

        // Tohle ví jen model.
        EntityClrNames = model.EntityClrNames,
        DiscriminatorColumn = model.DiscriminatorColumn,
        IsExcludedFromMigrations = model.IsExcludedFromMigrations,
        IsJoinTable = model.IsJoinTable || database.IsJoinTable,
        IsView = model.IsView || database.IsView,

        // Tohle ví jen databáze.
        RowCountEstimate = database.RowCountEstimate,

        Columns = MergeColumns(model.Columns, database.Columns),

        // Klíče, indexy a cizí klíče bere skutečnost v databázi; co v ní není, doplní model.
        PrimaryKey = database.PrimaryKey ?? model.PrimaryKey,
        Indexes = MergeByName(model.Indexes, database.Indexes, static i => i.Name, MergeIndex),
        ForeignKeys = MergeByName(model.ForeignKeys, database.ForeignKeys, static f => f.Name, MergeForeignKey),
        CheckConstraints = database.CheckConstraints.Count > 0
            ? database.CheckConstraints
            : model.CheckConstraints,
    };

    private static IReadOnlyList<DbColumn> MergeColumns(
        IReadOnlyList<DbColumn> model,
        IReadOnlyList<DbColumn> database)
    {
        var modelByName = model.ToDictionary(static c => c.Name, StringComparer.OrdinalIgnoreCase);
        var merged = new List<DbColumn>(Math.Max(model.Count, database.Count));

        foreach (var databaseColumn in database)
        {
            merged.Add(modelByName.TryGetValue(databaseColumn.Name, out var modelColumn)
                ? MergeColumn(modelColumn, databaseColumn)
                : databaseColumn);
        }

        // Sloupce, které jsou jen v modelu, se přidají na konec — v databázi ještě nejsou.
        var databaseNames = new HashSet<string>(
            database.Select(static c => c.Name), StringComparer.OrdinalIgnoreCase);

        merged.AddRange(model.Where(c => !databaseNames.Contains(c.Name)));

        return merged;
    }

    private static DbColumn MergeColumn(DbColumn model, DbColumn database) => new()
    {
        Name = database.Name,
        Ordinal = database.Ordinal,

        // Skutečnost v databázi.
        StoreType = database.StoreType,
        IsNullable = database.IsNullable,
        IsIdentity = database.IsIdentity,
        IsComputed = database.IsComputed,
        ComputedSql = database.ComputedSql ?? model.ComputedSql,
        IsStored = database.IsStored ?? model.IsStored,
        DefaultValueSql = database.DefaultValueSql ?? model.DefaultValueSql,
        Collation = database.Collation ?? model.Collation,
        MaxLength = database.MaxLength ?? model.MaxLength,
        Precision = database.Precision ?? model.Precision,
        Scale = database.Scale ?? model.Scale,

        // Záměr modelu.
        ClrType = model.ClrType,
        PropertyNames = model.PropertyNames,
        IsConcurrencyToken = model.IsConcurrencyToken,
        IsShadowProperty = model.IsShadowProperty,
        ValueGenerated = model.ValueGenerated != DbValueGenerated.Never
            ? model.ValueGenerated
            : database.ValueGenerated,
        Comment = model.Comment ?? database.Comment,

        // Klíčovost plyne z databáze, ale model ji zná taky.
        IsPrimaryKey = database.IsPrimaryKey || model.IsPrimaryKey,
        IsForeignKey = database.IsForeignKey || model.IsForeignKey,
    };

    private static DbIndex MergeIndex(DbIndex model, DbIndex database) => new()
    {
        Name = database.Name,
        Columns = database.Columns.Count > 0 ? database.Columns : model.Columns,
        IncludedColumns = database.IncludedColumns.Count > 0
            ? database.IncludedColumns
            : model.IncludedColumns,
        IsUnique = database.IsUnique,
        IsClustered = database.IsClustered ?? model.IsClustered,
        FilterSql = database.FilterSql ?? model.FilterSql,
        IsDescending = database.IsDescending.Count > 0 ? database.IsDescending : model.IsDescending,
    };

    private static DbForeignKey MergeForeignKey(DbForeignKey model, DbForeignKey database) => new()
    {
        Name = database.Name,
        Columns = database.Columns.Count > 0 ? database.Columns : model.Columns,
        PrincipalTable = database.PrincipalTable,
        PrincipalColumns = database.PrincipalColumns.Count > 0
            ? database.PrincipalColumns
            : model.PrincipalColumns,
        DeleteBehavior = database.DeleteBehavior != DbDeleteBehavior.Unknown
            ? database.DeleteBehavior
            : model.DeleteBehavior,

        // Navigace zná jen model.
        IsRequired = model.IsRequired,
        IsUnique = model.IsUnique,
        NavigationName = model.NavigationName,
        InverseNavigationName = model.InverseNavigationName,
    };

    /// <summary>
    /// Vztahy z modelu mají přednost, protože jako jediné znají navigace a umí N:M.
    /// Z databáze se doplní jen ty, které model nemá — typicky vazby mimo model.
    /// </summary>
    private static IReadOnlyList<DbRelationship> MergeRelationships(
        IReadOnlyList<DbRelationship> model,
        IReadOnlyList<DbRelationship> database)
    {
        var merged = new List<DbRelationship>(model);
        var known = new HashSet<string>(model.Select(static r => r.Id), StringComparer.Ordinal);

        // Sbalené N:M v modelu skryjí i cizí klíče vazební tabulky, které databáze hlásí zvlášť.
        var collapsedJoinTables = new HashSet<DbObjectName>(
            model.Where(static r => r.ViaJoinTable is not null).Select(static r => r.ViaJoinTable!.Value));

        foreach (var relationship in database)
        {
            if (known.Contains(relationship.Id) || collapsedJoinTables.Contains(relationship.From))
            {
                continue;
            }

            merged.Add(relationship);
        }

        merged.Sort(static (a, b) => string.CompareOrdinal(a.Id, b.Id));
        return merged;
    }

    private static IReadOnlyList<DbMigration> MergeMigrations(
        IReadOnlyList<DbMigration> model,
        IReadOnlyList<DbMigration> database)
    {
        var byId = new SortedDictionary<string, DbMigration>(StringComparer.Ordinal);

        foreach (var migration in database)
        {
            byId[migration.Id] = migration;
        }

        foreach (var migration in model)
        {
            byId[migration.Id] = byId.TryGetValue(migration.Id, out var existing)
                ? new DbMigration
                {
                    Id = migration.Id,
                    PresentInAssembly = migration.PresentInAssembly,
                    AppliedInDatabase = migration.AppliedInDatabase || existing.AppliedInDatabase,
                }
                : migration;
        }

        return [.. byId.Values];
    }

    private static IReadOnlyList<T> MergeByName<T>(
        IReadOnlyList<T> model,
        IReadOnlyList<T> database,
        Func<T, string> nameSelector,
        Func<T, T, T> merge)
    {
        var modelByName = model.ToDictionary(nameSelector, StringComparer.OrdinalIgnoreCase);
        var merged = new List<T>(Math.Max(model.Count, database.Count));

        foreach (var item in database)
        {
            merged.Add(modelByName.TryGetValue(nameSelector(item), out var modelItem)
                ? merge(modelItem, item)
                : item);
        }

        var databaseNames = new HashSet<string>(
            database.Select(nameSelector), StringComparer.OrdinalIgnoreCase);

        merged.AddRange(model.Where(item => !databaseNames.Contains(nameSelector(item))));

        return merged;
    }
}
