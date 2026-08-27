namespace DbsViewer;

/// <summary>Index nad tabulkou.</summary>
public sealed record DbIndex
{
    public required string Name { get; init; }

    public required IReadOnlyList<string> Columns { get; init; }

    /// <summary>Sloupce v <c>INCLUDE</c>. Jen SQL Server; jinde prázdné.</summary>
    public IReadOnlyList<string> IncludedColumns { get; init; } = [];

    public bool IsUnique { get; init; }

    /// <summary>Clustered index. <c>null</c>, když to zdroj neumí zjistit.</summary>
    public bool? IsClustered { get; init; }

    /// <summary>Podmínka filtrovaného indexu (<c>WHERE …</c>).</summary>
    public string? FilterSql { get; init; }

    /// <summary>
    /// Směr řazení po jednotlivých sloupcích, vždy buď prázdné (všechny sloupce vzestupně),
    /// nebo stejně dlouhé jako <see cref="Columns"/>. Nikdy ne částečné.
    /// </summary>
    public IReadOnlyList<bool> IsDescending { get; init; } = [];

    public override string ToString() =>
        $"{(IsUnique ? "UNIQUE " : "")}INDEX {Name} ({string.Join(", ", Columns)})";
}

/// <summary>Cizí klíč z pohledu závislé tabulky.</summary>
public sealed record DbForeignKey
{
    public required string Name { get; init; }

    /// <summary>Sloupce v závislé tabulce.</summary>
    public required IReadOnlyList<string> Columns { get; init; }

    /// <summary>Tabulka, na kterou klíč ukazuje.</summary>
    public required DbObjectName PrincipalTable { get; init; }

    public required IReadOnlyList<string> PrincipalColumns { get; init; }

    public DbDeleteBehavior DeleteBehavior { get; init; }

    /// <summary>Vazba je povinná — všechny sloupce klíče jsou NOT NULL.</summary>
    public bool IsRequired { get; init; }

    /// <summary>Vazba je jednoznačná — sloupce klíče jsou pokryté unikátním indexem, tedy 1:1.</summary>
    public bool IsUnique { get; init; }

    /// <summary>Navigace ze závislé entity na principální, pokud v modelu existuje.</summary>
    public string? NavigationName { get; init; }

    /// <summary>Opačná navigace z principální entity na závislou.</summary>
    public string? InverseNavigationName { get; init; }

    public override string ToString() =>
        $"FK {Name}: ({string.Join(", ", Columns)}) -> {PrincipalTable}({string.Join(", ", PrincipalColumns)})";
}
