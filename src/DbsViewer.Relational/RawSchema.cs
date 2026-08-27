namespace DbsViewer.Relational;

/// <summary>
/// Surový obraz databáze tak, jak vypadne z introspekčních dotazů — plochý, bez vazeb,
/// v podobě, kterou umí naplnit jak SQL Server, tak SQLite.
/// </summary>
/// <remarks>
/// Existuje proto, aby sestavení do <see cref="DatabaseSchema"/> bylo jedna sdílená čistá
/// funkce, a ne dvě mírně rozcházející se implementace na providera.
/// </remarks>
public sealed record RawSchema
{
    public string? DatabaseName { get; init; }

    public string? DefaultSchema { get; init; }

    public IReadOnlyList<RawTable> Tables { get; init; } = [];

    public IReadOnlyList<RawColumn> Columns { get; init; } = [];

    public IReadOnlyList<RawKeyColumn> KeyColumns { get; init; } = [];

    public IReadOnlyList<RawIndex> Indexes { get; init; } = [];

    public IReadOnlyList<RawIndexColumn> IndexColumns { get; init; } = [];

    public IReadOnlyList<RawForeignKey> ForeignKeys { get; init; } = [];

    public IReadOnlyList<RawForeignKeyColumn> ForeignKeyColumns { get; init; } = [];

    public IReadOnlyList<RawCheckConstraint> CheckConstraints { get; init; } = [];

    public IReadOnlyList<RawRowCount> RowCounts { get; init; } = [];

    public IReadOnlyList<string> AppliedMigrations { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>Tabulka nebo pohled.</summary>
public sealed record RawTable(
    string? Schema,
    string Name,
    bool IsView = false,
    string? Comment = null);

/// <summary>Sloupec tabulky.</summary>
public sealed record RawColumn(
    string? Schema,
    string Table,
    string Name,
    int Ordinal,
    string StoreType,
    bool IsNullable,
    bool IsIdentity = false,
    bool IsComputed = false,
    string? ComputedSql = null,
    bool? IsStored = null,
    string? DefaultValueSql = null,
    int? MaxLength = null,
    int? Precision = null,
    int? Scale = null,
    string? Collation = null,
    string? Comment = null);

/// <summary>Jeden sloupec primárního klíče.</summary>
public sealed record RawKeyColumn(
    string? Schema,
    string Table,
    string? ConstraintName,
    string Column,
    int Position,
    bool? IsClustered = null);

/// <summary>Index bez sloupců — ty jsou v <see cref="RawIndexColumn"/>.</summary>
public sealed record RawIndex(
    string? Schema,
    string Table,
    string Name,
    bool IsUnique,
    bool? IsClustered = null,
    string? FilterSql = null);

/// <summary>Sloupec indexu. <paramref name="IsIncluded"/> odlišuje klíčové sloupce od <c>INCLUDE</c>.</summary>
public sealed record RawIndexColumn(
    string? Schema,
    string Table,
    string IndexName,
    string Column,
    int Position,
    bool IsDescending = false,
    bool IsIncluded = false);

/// <summary>Cizí klíč bez sloupců — ty jsou v <see cref="RawForeignKeyColumn"/>.</summary>
public sealed record RawForeignKey(
    string? Schema,
    string Table,
    string Name,
    string? PrincipalSchema,
    string PrincipalTable,
    string? DeleteAction = null);

/// <summary>Dvojice sloupců cizího klíče.</summary>
public sealed record RawForeignKeyColumn(
    string? Schema,
    string Table,
    string ForeignKeyName,
    string Column,
    string PrincipalColumn,
    int Position);

/// <summary>Check constraint.</summary>
public sealed record RawCheckConstraint(
    string? Schema,
    string Table,
    string Name,
    string? Sql);

/// <summary>Odhad počtu řádků ze statistik databáze.</summary>
public sealed record RawRowCount(
    string? Schema,
    string Table,
    long Rows);
