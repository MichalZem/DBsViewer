namespace DbsViewer;

/// <summary>
/// Vztah mezi dvěma tabulkami tak, jak se kreslí v diagramu. Není to totéž co cizí klíč:
/// vztah N:M vzniká ze dvou cizích klíčů přes vazební tabulku a kreslí se jako jedna hrana.
/// </summary>
public sealed class DbRelationship
{
    /// <summary>Stabilní identifikátor pro UI (výběr hrany, uložené rozložení).</summary>
    public required string Id { get; init; }

    /// <summary>Závislá strana — tabulka, která nese cizí klíč. U N:M jedna ze stran.</summary>
    public required DbObjectName From { get; init; }

    /// <summary>Principální strana.</summary>
    public required DbObjectName To { get; init; }

    public DbCardinality Cardinality { get; init; }

    /// <summary>Vazební tabulka u vztahu N:M.</summary>
    public DbObjectName? ViaJoinTable { get; init; }

    /// <summary>Jméno cizího klíče, ze kterého vztah vznikl. U N:M prázdné.</summary>
    public string? ForeignKeyName { get; init; }

    public DbDeleteBehavior DeleteBehavior { get; init; }

    public bool IsRequired { get; init; }

    /// <summary>Cizí klíč je zároveň součástí primárního klíče — identifikující vztah.</summary>
    public bool IsIdentifying { get; init; }

    /// <summary>Vztah tabulky na sebe samu.</summary>
    public bool IsSelfReference => From == To;

    public IReadOnlyList<string> FromColumns { get; init; } = [];

    public IReadOnlyList<string> ToColumns { get; init; } = [];

    public string? FromNavigation { get; init; }

    public string? ToNavigation { get; init; }

    public override string ToString() => Cardinality switch
    {
        DbCardinality.ManyToMany => $"{From} >--< {To} (via {ViaJoinTable})",
        DbCardinality.OneToOne => $"{From} --- {To}",
        _ => $"{From} >--- {To}",
    };
}

/// <summary>Jedna migrace a její stav.</summary>
public sealed class DbMigration
{
    public required string Id { get; init; }

    /// <summary>Migrace je zapsaná v <c>__EFMigrationsHistory</c>.</summary>
    public bool AppliedInDatabase { get; init; }

    /// <summary>Migrace existuje v assembly aplikace.</summary>
    public bool PresentInAssembly { get; init; }

    /// <summary>Migrace čeká na nasazení — je v kódu, ale ne v databázi.</summary>
    public bool IsPending => PresentInAssembly && !AppliedInDatabase;

    /// <summary>Databáze je napřed před kódem — migrace je aplikovaná, ale v assembly chybí.</summary>
    public bool IsOrphaned => AppliedInDatabase && !PresentInAssembly;
}
