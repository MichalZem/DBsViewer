namespace DbsViewer.TestKit;

/// <summary>Pomůcky pro stavbu tabulek v testech, aby testy zůstaly čitelné.</summary>
public static class Build
{
    public static DbTable Table(
        string name,
        string[]? columns = null,
        string[]? primaryKey = null,
        DbForeignKey[]? foreignKeys = null,
        DbIndex[]? indexes = null,
        bool isView = false,
        bool[]? nullable = null)
    {
        columns ??= [];

        // Příznaky na sloupcích musí sedět s klíči, jinak by testy pracovaly se stavem,
        // jaký žádný skutečný zdroj schématu nevrátí.
        var keyColumns = new HashSet<string>(primaryKey ?? [], StringComparer.OrdinalIgnoreCase);
        var foreignKeyColumns = new HashSet<string>(
            (foreignKeys ?? []).SelectMany(static f => f.Columns),
            StringComparer.OrdinalIgnoreCase);

        return new DbTable
        {
            Name = new DbObjectName(null, name),
            IsView = isView,
            Columns =
            [
                .. columns.Select((c, i) => new DbColumn
                {
                    Name = c,
                    Ordinal = i + 1,
                    StoreType = "int",
                    IsNullable = nullable is not null && i < nullable.Length && nullable[i],
                    IsPrimaryKey = keyColumns.Contains(c),
                    IsForeignKey = foreignKeyColumns.Contains(c),
                }),
            ],
            PrimaryKey = primaryKey is null ? null : new DbPrimaryKey { Columns = primaryKey },
            ForeignKeys = foreignKeys ?? [],
            Indexes = indexes ?? [],
        };
    }

    public static DbForeignKey ForeignKey(
        string name,
        string[] columns,
        string principalTable,
        string[]? principalColumns = null,
        DbDeleteBehavior delete = DbDeleteBehavior.NoAction,
        bool isUnique = false) => new()
        {
            Name = name,
            Columns = columns,
            PrincipalTable = new DbObjectName(null, principalTable),
            PrincipalColumns = principalColumns ?? ["Id"],
            DeleteBehavior = delete,
            IsUnique = isUnique,
        };

    public static DbIndex Index(string name, string[] columns, bool isUnique = false) => new()
    {
        Name = name,
        Columns = columns,
        IsUnique = isUnique,
    };

    public static IReadOnlySet<DbObjectName> Names(params string[] names) =>
        new HashSet<DbObjectName>(names.Select(static n => new DbObjectName(null, n)));
}
