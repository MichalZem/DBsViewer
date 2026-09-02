using System.Diagnostics.CodeAnalysis;
using DbsViewer.EfCore.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DbsViewer.EfCore;

/// <summary>
/// Převod <c>IRelationalModel</c> na <see cref="DatabaseSchema"/>. Čistě v paměti, bez dotazů do databáze.
/// </summary>
internal sealed class EfModelReader(
    DbContext context,
    SchemaReadOptions options,
    List<string> warnings,
    IModel? model = null)
{
    // Model se dá podstrčit zvenčí: snapshot migrace je taky IModel, jen nepochází
    // z kontextu, ale z historie. Bez zadání se vezme design-time model kontextu.
    private readonly IModel _model = model ?? ResolveModel(context, warnings);

    /// <summary>
    /// <c>DbContext.Model</c> je runtime model optimalizovaný pro čtení — EF z něj odstraňuje
    /// metadata, která za běhu nepotřebuje: collation, komentáře, defaultní hodnoty, check
    /// constrainty. Pro popis schématu je potřeba design-time model, tedy přesně to,
    /// z čeho EF generuje migrace. Runtime model je jen nouzová záloha.
    /// </summary>
    internal static IModel ResolveModel(DbContext context, List<string> warnings)
        => ResolveModel(() => context.GetService<IDesignTimeModel>().Model, context.Model, warnings);

    /// <inheritdoc cref="ResolveModel(DbContext, List{string})"/>
    internal static IModel ResolveModel(
        Func<IModel> designTimeModel,
        IModel runtimeModel,
        List<string> warnings)
        => SafeRead.Value(
            designTimeModel,
            runtimeModel,
            static ex => "Design-time model není dostupný, použil se runtime model. "
                + $"Chybět budou komentáře, collation a defaultní hodnoty. Důvod: {ex.Message}",
            warnings);

    public DatabaseSchema Read(IReadOnlyList<DbMigration> migrations)
    {
        var relational = _model.GetRelationalModel();
        var entitiesByTable = MapEntitiesToTables();
        var joinTables = DetectJoinTables();

        var tables = new List<DbTable>();

        foreach (var table in relational.Tables)
        {
            var name = new DbObjectName(table.Schema, table.Name);
            if (!options.IsVisible(name))
            {
                continue;
            }

            tables.Add(BuildTable(table, name, entitiesByTable, joinTables));
        }

        foreach (var view in relational.Views)
        {
            var name = new DbObjectName(view.Schema, view.Name);
            if (!options.IsVisible(name))
            {
                continue;
            }

            tables.Add(BuildView(view, name, entitiesByTable));
        }

        tables.Sort(static (a, b) => a.Name.CompareTo(b.Name));

        var visible = new HashSet<DbObjectName>(tables.Select(static t => t.Name));
        var relationships = BuildRelationships(relational, visible);

        return new DatabaseSchema
        {
            DatabaseName = TryGetDatabaseName(),
            ProviderName = context.Database.ProviderName,
            Provider = DetectProvider(context.Database.ProviderName),
            SourceKind = SchemaSourceKind.EfModel,
            SourceName = $"EF model ({context.GetType().Name})",
            DefaultSchema = _model.GetDefaultSchema(),
            Tables = tables,
            Relationships = relationships,
            Migrations = migrations,
            Warnings = warnings,
        };
    }

    // ---------- tabulky ----------

    private DbTable BuildTable(
        ITable table,
        DbObjectName name,
        IReadOnlyDictionary<DbObjectName, List<IEntityType>> entitiesByTable,
        IReadOnlySet<DbObjectName> joinTables)
    {
        var entities = entitiesByTable.TryGetValue(name, out var list) ? list : [];

        var primaryKey = table.PrimaryKey is { } pk
            ? new DbPrimaryKey
            {
                Name = pk.Name,
                Columns = [.. pk.Columns.Select(static c => c.Name)],
            }
            : null;

        var pkColumns = new HashSet<string>(
            primaryKey?.Columns ?? [],
            StringComparer.OrdinalIgnoreCase);

        var pkOrder = BuildKeyOrder(primaryKey);

        var fkColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var constraint in table.ForeignKeyConstraints)
        {
            foreach (var column in constraint.Columns)
            {
                fkColumns.Add(column.Name);
            }
        }

        var columns = BuildColumns(table, pkColumns, pkOrder, fkColumns);

        return new DbTable
        {
            Name = name,
            Comment = table.Comment,
            EntityClrNames = EntityNames(entities),
            IsJoinTable = joinTables.Contains(name),
            DiscriminatorColumn = entities.Select(static e => e.GetDiscriminatorPropertyName())
                .FirstOrDefault(static n => n is not null),
            IsExcludedFromMigrations = table.IsExcludedFromMigrations,
            Columns = columns,
            PrimaryKey = primaryKey,
            Indexes = BuildIndexes(table),
            ForeignKeys = BuildForeignKeys(table),
            CheckConstraints = BuildCheckConstraints(entities),
        };
    }

    private DbTable BuildView(
        IView view,
        DbObjectName name,
        IReadOnlyDictionary<DbObjectName, List<IEntityType>> entitiesByTable)
    {
        var entities = entitiesByTable.TryGetValue(name, out var list) ? list : [];

        var ordinal = 1;
        var columns = new List<DbColumn>();
        foreach (var column in view.Columns)
        {
            var property = column.PropertyMappings.Select(static m => m.Property).FirstOrDefault();
            columns.Add(new DbColumn
            {
                Name = column.Name,
                Ordinal = ordinal++,
                StoreType = column.StoreType,
                IsNullable = column.IsNullable,
                ClrType = property?.ClrType.FullName,
                PropertyNames = property is null ? [] : [property.Name],
            });
        }

        return new DbTable
        {
            Name = name,
            IsView = true,
            EntityClrNames = EntityNames(entities),
            Columns = columns,
        };
    }

    /// <summary>
    /// Pozice sloupců v primárním klíči. Sloupce mimo klíč dostanou <see cref="int.MaxValue"/>,
    /// takže se v jednom řazení propadnou za klíč.
    /// </summary>
    internal static IReadOnlyDictionary<string, int> BuildKeyOrder(DbPrimaryKey? primaryKey)
    {
        var order = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < (primaryKey?.Columns.Count ?? 0); i++)
        {
            order[primaryKey!.Columns[i]] = i;
        }

        return order;
    }

    private List<DbColumn> BuildColumns(
        ITable table,
        HashSet<string> pkColumns,
        IReadOnlyDictionary<string, int> pkOrder,
        HashSet<string> fkColumns)
    {
        // EF vrací sloupce abecedně. Pro čtení je užitečnější mít napřed primární klíč
        // v pořadí, ve kterém je definovaný, a teprve pak zbytek abecedně.
        var ordered = table.Columns
            .OrderBy(c => pkOrder.TryGetValue(c.Name, out var position) ? position : int.MaxValue)
            .ThenBy(static c => c.Name, StringComparer.OrdinalIgnoreCase);

        var storeObject = StoreObjectIdentifier.Table(table.Name, table.Schema);
        var ordinal = 1;
        var columns = new List<DbColumn>();

        foreach (var column in ordered)
        {
            var properties = column.PropertyMappings.Select(static m => m.Property).ToList();
            var property = properties.FirstOrDefault();

            var computedSql = property?.GetComputedColumnSql(storeObject);

            columns.Add(new DbColumn
            {
                Name = column.Name,
                Ordinal = ordinal++,
                StoreType = column.StoreType,
                ClrType = property?.ClrType.FullName,
                IsNullable = column.IsNullable,
                IsPrimaryKey = pkColumns.Contains(column.Name),
                IsForeignKey = fkColumns.Contains(column.Name),
                IsIdentity = property is not null && LooksLikeStoreGenerated(property),
                IsComputed = computedSql is not null,
                ComputedSql = computedSql,
                IsStored = computedSql is null ? null : property?.GetIsStored(storeObject),
                DefaultValueSql = property?.GetDefaultValueSql(storeObject),
                MaxLength = property?.GetMaxLength(),
                Precision = property?.GetPrecision(),
                Scale = property?.GetScale(),
                Collation = property?.GetCollation(storeObject),
                IsConcurrencyToken = property?.IsConcurrencyToken ?? false,
                IsShadowProperty = property?.IsShadowProperty() ?? false,
                ValueGenerated = MapValueGenerated(property?.ValueGenerated),
                PropertyNames = [.. properties.Select(static p => p.Name).Distinct().Order()],
                Comment = property?.GetComment(storeObject),
            });
        }

        return columns;
    }

    private static List<DbIndex> BuildIndexes(ITable table)
    {
        var indexes = new List<DbIndex>();

        foreach (var index in table.Indexes)
        {
            var mapped = index.MappedIndexes.FirstOrDefault();

            indexes.Add(new DbIndex
            {
                Name = index.Name,
                Columns = [.. index.Columns.Select(static c => c.Name)],
                IsUnique = index.IsUnique,
                FilterSql = mapped?.GetFilter(),
                IsDescending = NormalizeDescending(index.IsDescending, index.Columns.Count),
                // Clustered a INCLUDE jsou anotace SQL Serveru. Čteme je podle jména,
                // aby si tento balíček nemusel táhnout providerový balíček.
                IsClustered = ReadBoolAnnotation(mapped, "SqlServer:Clustered"),
                IncludedColumns = ReadStringArrayAnnotation(mapped, "SqlServer:Include"),
            });
        }

        indexes.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
        return indexes;
    }

    /// <summary>
    /// EF používá pro směr řazení tři stavy: <c>null</c> znamená všechny sloupce vzestupně,
    /// prázdný seznam všechny sestupně a jinak je to hodnota na sloupec. Prázdný seznam
    /// by se v našem modelu nedal odlišit od „nenastaveno“, takže se rozepíše.
    /// </summary>
    internal static IReadOnlyList<bool> NormalizeDescending(
        IReadOnlyList<bool>? isDescending,
        int columnCount) => isDescending switch
        {
            null => [],
            { Count: 0 } => [.. Enumerable.Repeat(true, columnCount)],
            _ => [.. isDescending],
        };

    private static List<DbForeignKey> BuildForeignKeys(ITable table)
    {
        var foreignKeys = new List<DbForeignKey>();

        foreach (var constraint in table.ForeignKeyConstraints)
        {
            var mapped = constraint.MappedForeignKeys.FirstOrDefault();

            foreignKeys.Add(new DbForeignKey
            {
                Name = constraint.Name,
                Columns = [.. constraint.Columns.Select(static c => c.Name)],
                PrincipalTable = new DbObjectName(
                    constraint.PrincipalTable.Schema,
                    constraint.PrincipalTable.Name),
                PrincipalColumns = [.. constraint.PrincipalColumns.Select(static c => c.Name)],
                DeleteBehavior = MapReferentialAction(constraint.OnDeleteAction),
                IsRequired = IsRequiredConstraint(constraint),
                IsUnique = mapped?.IsUnique ?? false,
                NavigationName = mapped?.DependentToPrincipal?.Name,
                InverseNavigationName = mapped?.PrincipalToDependent?.Name,
            });
        }

        foreignKeys.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
        return foreignKeys;
    }

    /// <summary>
    /// Vazba je povinná, když žádný ze sloupců cizího klíče nepřipouští NULL.
    /// Počítá se ze sloupců, ne z <c>IForeignKey.IsRequired</c> — u klíče sdíleného
    /// více entitami (TPH) se ta hodnota liší podle toho, kterou entitu se zeptáme.
    /// </summary>
    internal static bool IsRequiredConstraint(IForeignKeyConstraint constraint) =>
        constraint.Columns.Count > 0 && constraint.Columns.All(static c => !c.IsNullable);

    private static List<DbCheckConstraint> BuildCheckConstraints(List<IEntityType> entities)
    {
        var constraints = new List<DbCheckConstraint>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in entities)
        {
            foreach (var check in entity.GetCheckConstraints())
            {
                if (check.Name is { } name && seen.Add(name))
                {
                    constraints.Add(new DbCheckConstraint { Name = name, Sql = check.Sql });
                }
            }
        }

        return constraints;
    }

    // ---------- vztahy ----------

    private List<DbRelationship> BuildRelationships(
        IRelationalModel relational,
        IReadOnlySet<DbObjectName> visible)
    {
        var relationships = new List<DbRelationship>();
        var collapsedForeignKeys = new HashSet<string>(StringComparer.Ordinal);

        // 1) N:M ze skip-navigací. Vazební tabulka se sbalí do jediné hrany.
        if (options.DetectJoinTables)
        {
            relationships.AddRange(BuildManyToMany(visible, collapsedForeignKeys));
        }

        // 2) Zbylé cizí klíče jako 1:1 nebo 1:N.
        foreach (var table in relational.Tables)
        {
            var from = new DbObjectName(table.Schema, table.Name);
            if (!visible.Contains(from))
            {
                continue;
            }

            var pkColumns = new HashSet<string>(
                table.PrimaryKey?.Columns.Select(static c => c.Name) ?? [],
                StringComparer.OrdinalIgnoreCase);

            foreach (var constraint in table.ForeignKeyConstraints)
            {
                var to = new DbObjectName(
                    constraint.PrincipalTable.Schema,
                    constraint.PrincipalTable.Name);

                if (!visible.Contains(to))
                {
                    continue;
                }

                var id = RelationshipId(from, constraint.Name);
                if (collapsedForeignKeys.Contains(id))
                {
                    continue;
                }

                var mapped = constraint.MappedForeignKeys.FirstOrDefault();
                var columns = constraint.Columns.Select(static c => c.Name).ToArray();

                relationships.Add(new DbRelationship
                {
                    Id = id,
                    From = from,
                    To = to,
                    Cardinality = mapped?.IsUnique == true
                        ? DbCardinality.OneToOne
                        : DbCardinality.OneToMany,
                    ForeignKeyName = constraint.Name,
                    DeleteBehavior = MapReferentialAction(constraint.OnDeleteAction),
                    IsRequired = IsRequiredConstraint(constraint),
                    IsIdentifying = columns.Length > 0 && columns.All(pkColumns.Contains),
                    FromColumns = columns,
                    ToColumns = [.. constraint.PrincipalColumns.Select(static c => c.Name)],
                    FromNavigation = mapped?.DependentToPrincipal?.Name,
                    ToNavigation = mapped?.PrincipalToDependent?.Name,
                });
            }
        }

        relationships.Sort(static (a, b) => string.CompareOrdinal(a.Id, b.Id));
        return relationships;
    }

    private List<DbRelationship> BuildManyToMany(
        IReadOnlySet<DbObjectName> visible,
        HashSet<string> collapsedForeignKeys)
    {
        var result = new List<DbRelationship>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var navigations = _model.GetEntityTypes()
            .SelectMany(static entity => entity.GetSkipNavigations())
            .Select(static skip => (Skip: skip, Resolved: ResolveSkipNavigation(skip)))
            .Where(static candidate => candidate.Resolved is not null);

        foreach (var (skip, resolved) in navigations)
        {
            var (leftName, rightName, joinName, inverse) = resolved!.Value;

            // Každý vztah je v modelu dvakrát (navigace a její inverze) — bereme ho jednou.
            var key = leftName.CompareTo(rightName) <= 0
                ? $"{leftName}<->{rightName}|{joinName}"
                : $"{rightName}<->{leftName}|{joinName}";

            if (!seen.Add(key))
            {
                continue;
            }

            // Cizí klíče vazební tabulky už nekreslíme zvlášť.
            collapsedForeignKeys.Add(RelationshipId(joinName, skip.ForeignKey.GetConstraintName()));
            collapsedForeignKeys.Add(RelationshipId(joinName, inverse.ForeignKey.GetConstraintName()));

            if (!visible.Contains(leftName) || !visible.Contains(rightName))
            {
                continue;
            }

            result.Add(new DbRelationship
            {
                Id = $"m2m:{key}",
                From = leftName,
                To = rightName,
                Cardinality = DbCardinality.ManyToMany,
                ViaJoinTable = joinName,
                DeleteBehavior = MapReferentialAction(ReferentialAction.Cascade),
                IsRequired = true,
                FromNavigation = skip.Name,
                ToNavigation = inverse.Name,
            });
        }

        return result;
    }

    /// <summary>
    /// Rozbalí skip-navigaci na trojici tabulek a inverzní navigaci. Vrací <c>null</c>,
    /// když navigace nemá join entitu, inverzi nebo některá strana není mapovaná na tabulku.
    /// </summary>
    /// <remarks>
    /// Ty stavy EF u platného modelu negeneruje, takže se nedají vyvolat testem — jde
    /// o pojistku proti změně kontraktu v budoucí verzi EF. Vyloučeno z pokrytí právě proto,
    /// aby zbytek souboru mohl mít pokrytí vynucené na 100 %.
    /// </remarks>
    [ExcludeFromCodeCoverage(Justification = "Nedosažitelné u platného EF modelu; pojistka proti změně kontraktu EF.")]
    private static (DbObjectName Left, DbObjectName Right, DbObjectName Join, ISkipNavigation Inverse)?
        ResolveSkipNavigation(ISkipNavigation skip)
    {
        if (skip.JoinEntityType is not { } joinEntity || skip.Inverse is not { } inverse)
        {
            return null;
        }

        if (TableOf(skip.DeclaringEntityType) is not { } left
            || TableOf(skip.TargetEntityType) is not { } right
            || TableOf(joinEntity) is not { } join)
        {
            return null;
        }

        return (left, right, join, inverse);
    }

    /// <summary>
    /// Vazební tabulky N:M. Bere jednak join entity ze skip-navigací, jednak heuristiku
    /// pro explicitně namodelované vazební tabulky: primární klíč složený výhradně
    /// ze sloupců právě dvou cizích klíčů a žádný další datový sloupec.
    /// </summary>
    private HashSet<DbObjectName> DetectJoinTables()
    {
        var joinTables = new HashSet<DbObjectName>();
        if (!options.DetectJoinTables)
        {
            return joinTables;
        }

        foreach (var entity in _model.GetEntityTypes())
        {
            foreach (var skip in entity.GetSkipNavigations())
            {
                if (skip.JoinEntityType is { } joinEntity && TableOf(joinEntity) is { } name)
                {
                    joinTables.Add(name);
                }
            }
        }

        foreach (var table in _model.GetRelationalModel().Tables)
        {
            var name = new DbObjectName(table.Schema, table.Name);
            if (joinTables.Contains(name))
            {
                continue;
            }

            if (table.PrimaryKey is not { } pk || table.ForeignKeyConstraints.Count() != 2)
            {
                continue;
            }

            var fkColumns = new HashSet<string>(
                table.ForeignKeyConstraints.SelectMany(static fk => fk.Columns).Select(static c => c.Name),
                StringComparer.OrdinalIgnoreCase);

            var pkColumns = pk.Columns.Select(static c => c.Name).ToArray();
            if (pkColumns.Length < 2 || !pkColumns.All(fkColumns.Contains))
            {
                continue;
            }

            // Nesmí nést vlastní data — jinak je to plnohodnotná entita, ne jen vazba.
            var extraColumns = table.Columns.Count(c => !fkColumns.Contains(c.Name));
            if (extraColumns == 0)
            {
                joinTables.Add(name);
            }
        }

        return joinTables;
    }

    // ---------- pomocné ----------

    private Dictionary<DbObjectName, List<IEntityType>> MapEntitiesToTables()
    {
        var map = new Dictionary<DbObjectName, List<IEntityType>>();

        foreach (var entity in _model.GetEntityTypes())
        {
            if (TableOf(entity) is not { } name)
            {
                continue;
            }

            if (!map.TryGetValue(name, out var list))
            {
                map[name] = list = [];
            }

            list.Add(entity);
        }

        return map;
    }

    /// <summary>
    /// Jména entit namapovaných na tabulku. Implicitní vazební entita vztahu N:M nemá vlastní
    /// CLR typ — je to sdílený <c>Dictionary&lt;string, object&gt;</c> — takže se u ní použije
    /// jméno z modelu, jinak by ve výpisu svítilo nicneříkající <c>Dictionary`2</c>.
    /// </summary>
    private static IReadOnlyList<string> EntityNames(List<IEntityType> entities) =>
        [.. entities
            .Select(static e => e.HasSharedClrType ? e.ShortName() : e.ClrType.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

    private static DbObjectName? TableOf(IEntityType entity)
    {
        if (entity.GetTableName() is { } tableName)
        {
            return new DbObjectName(entity.GetSchema(), tableName);
        }

        return entity.GetViewName() is { } viewName
            ? new DbObjectName(entity.GetViewSchema(), viewName)
            : null;
    }

    private static string RelationshipId(DbObjectName from, string? constraintName) =>
        $"fk:{from}|{constraintName}";

    private string? TryGetDatabaseName() => SafeRead.Optional(
        () => context.Database.GetDbConnection().Database,
        static ex => $"Jméno databáze se nepodařilo zjistit: {ex.Message}",
        warnings);

    internal static DbProviderKind DetectProvider(string? providerName) => providerName switch
    {
        null => DbProviderKind.Unknown,
        var p when p.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) => DbProviderKind.SqlServer,
        var p when p.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) => DbProviderKind.Sqlite,
        _ => DbProviderKind.Unknown,
    };

    internal static DbDeleteBehavior MapReferentialAction(ReferentialAction action) => action switch
    {
        ReferentialAction.NoAction => DbDeleteBehavior.NoAction,
        ReferentialAction.Restrict => DbDeleteBehavior.Restrict,
        ReferentialAction.Cascade => DbDeleteBehavior.Cascade,
        ReferentialAction.SetNull => DbDeleteBehavior.SetNull,
        ReferentialAction.SetDefault => DbDeleteBehavior.SetDefault,
        _ => DbDeleteBehavior.Unknown,
    };

    internal static DbValueGenerated MapValueGenerated(ValueGenerated? generated) => generated switch
    {
        ValueGenerated.OnAdd => DbValueGenerated.OnAdd,
        ValueGenerated.OnUpdate => DbValueGenerated.OnUpdate,
        ValueGenerated.OnAddOrUpdate => DbValueGenerated.OnAddOrUpdate,
        _ => DbValueGenerated.Never,
    };

    /// <summary>
    /// Odhad, jestli hodnotu generuje databáze. U SQL Serveru se dá přečíst anotace strategie,
    /// jinde zbývá heuristika „celočíselný klíč generovaný při vložení".
    /// Živá introspekce tohle potvrdí nebo vyvrátí.
    /// </summary>
    private static bool LooksLikeStoreGenerated(IProperty property)
    {
        if (property.FindAnnotation("SqlServer:ValueGenerationStrategy")?.Value?.ToString() is { } strategy)
        {
            return strategy is not "None";
        }

        if (property.ValueGenerated != ValueGenerated.OnAdd)
        {
            return false;
        }

        var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
        return type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte);
    }

    internal static bool? ReadBoolAnnotation(IAnnotatable? annotatable, string name) =>
        annotatable?.FindAnnotation(name)?.Value as bool?;

    internal static IReadOnlyList<string> ReadStringArrayAnnotation(IAnnotatable? annotatable, string name) =>
        annotatable?.FindAnnotation(name)?.Value switch
        {
            string[] values => values,
            IEnumerable<string> values => [.. values],
            _ => [],
        };
}
