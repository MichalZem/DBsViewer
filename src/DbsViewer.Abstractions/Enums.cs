namespace DbsViewer;

/// <summary>Odkud pochází načtené schéma.</summary>
public enum SchemaSourceKind
{
    /// <summary>Z EF Core modelu (<c>DbContext.Model</c>) — zná navigace a CLR typy, nezná realitu v databázi.</summary>
    EfModel,

    /// <summary>Z živé databáze přes introspekci — zná skutečné indexy a defaulty, nezná navigace.</summary>
    LiveDatabase,

    /// <summary>Sloučení obou zdrojů.</summary>
    Merged,

    /// <summary>
    /// Ze snapshotu migrace — schéma tak, jak vypadalo po jejím provedení.
    /// </summary>
    /// <remarks>
    /// Historická verze. Data se z ní číst nedají: snapshot popisuje strukturu
    /// v minulosti, kdežto řádky existují jen v databázi tady a teď.
    /// </remarks>
    MigrationSnapshot,
}

/// <summary>Databázový provider, na který se schéma váže.</summary>
public enum DbProviderKind
{
    Unknown,
    SqlServer,
    Sqlite,
}

/// <summary>Chování při smazání principálního záznamu.</summary>
public enum DbDeleteBehavior
{
    Unknown,
    NoAction,
    Restrict,
    Cascade,
    SetNull,
    SetDefault,
}

/// <summary>Kardinalita vztahu mezi dvěma tabulkami tak, jak se kreslí v diagramu.</summary>
public enum DbCardinality
{
    OneToOne,
    OneToMany,
    ManyToMany,
}

/// <summary>Jak je hodnota sloupce generovaná.</summary>
public enum DbValueGenerated
{
    Never,
    OnAdd,
    OnUpdate,
    OnAddOrUpdate,
}
