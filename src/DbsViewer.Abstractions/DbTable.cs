using System.Text.Json.Serialization;

namespace DbsViewer;

/// <summary>Tabulka nebo pohled včetně všeho, co se o ní dá zjistit.</summary>
public sealed record DbTable
{
    public required DbObjectName Name { get; init; }

    /// <summary>Komentář z databáze nebo z <c>HasComment()</c> v modelu.</summary>
    public string? Comment { get; init; }

    /// <summary>
    /// CLR jména entit namapovaných na tuto tabulku. Při TPH dědičnosti nebo owned types
    /// jich je víc než jedno; u tabulky mimo model je seznam prázdný.
    /// </summary>
    public IReadOnlyList<string> EntityClrNames { get; init; } = [];

    /// <summary>Jde o pohled, ne o tabulku.</summary>
    public bool IsView { get; init; }

    /// <summary>Vazební tabulka vztahu N:M — v diagramu se dá sbalit do jedné hrany.</summary>
    public bool IsJoinTable { get; init; }

    /// <summary>Sloupec s diskriminátorem u TPH dědičnosti.</summary>
    public string? DiscriminatorColumn { get; init; }

    /// <summary>Tabulka je v modelu označená <c>ExcludeFromMigrations()</c>.</summary>
    public bool IsExcludedFromMigrations { get; init; }

    /// <summary>Odhad počtu řádků. Nikdy se nepočítá přes <c>COUNT(*)</c>, jen ze statistik.</summary>
    public long? RowCountEstimate { get; init; }

    public IReadOnlyList<DbColumn> Columns { get; init; } = [];

    public DbPrimaryKey? PrimaryKey { get; init; }

    public IReadOnlyList<DbIndex> Indexes { get; init; } = [];

    public IReadOnlyList<DbForeignKey> ForeignKeys { get; init; } = [];

    public IReadOnlyList<DbCheckConstraint> CheckConstraints { get; init; } = [];

    [JsonIgnore]
    public string Qualified => Name.Qualified;

    public DbColumn? FindColumn(string columnName)
    {
        foreach (var column in Columns)
        {
            if (string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase))
            {
                return column;
            }
        }

        return null;
    }

    public override string ToString() => Qualified;
}

/// <summary>Sloupec tabulky.</summary>
public sealed record DbColumn
{
    public required string Name { get; init; }

    /// <summary>Pořadí ve výpisu tabulky (1-based).</summary>
    public int Ordinal { get; init; }

    /// <summary>Typ v databázi včetně délky, např. <c>nvarchar(200)</c>.</summary>
    public required string StoreType { get; init; }

    /// <summary>CLR typ z modelu, např. <c>System.Guid</c>. U tabulky mimo model <c>null</c>.</summary>
    public string? ClrType { get; init; }

    public bool IsNullable { get; init; }

    public bool IsPrimaryKey { get; init; }

    /// <summary>Sloupec je součástí některého cizího klíče.</summary>
    public bool IsForeignKey { get; init; }

    /// <summary>Hodnotu generuje databáze při vložení (IDENTITY, AUTOINCREMENT, sekvence).</summary>
    public bool IsIdentity { get; init; }

    public bool IsComputed { get; init; }

    public string? ComputedSql { get; init; }

    /// <summary>Persistovaný computed sloupec.</summary>
    public bool? IsStored { get; init; }

    public string? DefaultValueSql { get; init; }

    public int? MaxLength { get; init; }

    public int? Precision { get; init; }

    public int? Scale { get; init; }

    public string? Collation { get; init; }

    public bool IsConcurrencyToken { get; init; }

    /// <summary>Vlastnost existuje jen v modelu, ne jako property na entitě (shadow property).</summary>
    public bool IsShadowProperty { get; init; }

    public DbValueGenerated ValueGenerated { get; init; }

    /// <summary>Jména CLR vlastností mapovaných na tento sloupec. Při TPH jich může být víc.</summary>
    public IReadOnlyList<string> PropertyNames { get; init; } = [];

    public string? Comment { get; init; }

    public override string ToString() => $"{Name} {StoreType}{(IsNullable ? " NULL" : " NOT NULL")}";
}

/// <summary>Primární klíč.</summary>
public sealed record DbPrimaryKey
{
    public string? Name { get; init; }

    public required IReadOnlyList<string> Columns { get; init; }

    public bool? IsClustered { get; init; }

    public override string ToString() => $"PK ({string.Join(", ", Columns)})";
}

/// <summary>Check constraint.</summary>
public sealed record DbCheckConstraint
{
    public required string Name { get; init; }

    public string? Sql { get; init; }
}
