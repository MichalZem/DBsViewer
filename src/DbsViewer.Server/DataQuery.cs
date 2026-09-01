namespace DbsViewer.Server;

/// <summary>Porovnání použité při filtrování sloupce.</summary>
public enum FilterOperator
{
    /// <summary>Hodnota obsahuje zadaný text.</summary>
    Contains,

    /// <summary>Hodnota se rovná zadané.</summary>
    Equals,

    /// <summary>Hodnota začíná zadaným textem.</summary>
    StartsWith,

    /// <summary>Hodnota končí zadaným textem.</summary>
    EndsWith,

    /// <summary>Hodnota je větší než zadaná.</summary>
    GreaterThan,

    /// <summary>Hodnota je menší než zadaná.</summary>
    LessThan,

    /// <summary>Hodnota je NULL.</summary>
    IsNull,

    /// <summary>Hodnota není NULL.</summary>
    IsNotNull,
}

/// <summary>Filtr nad jedním sloupcem.</summary>
/// <param name="Column">Jméno sloupce; ověřuje se proti schématu.</param>
/// <param name="Operator">Porovnání.</param>
/// <param name="Value">Hledaná hodnota. U <c>IsNull</c> a <c>IsNotNull</c> se nepoužije.</param>
public sealed record DataFilter(string Column, FilterOperator Operator, string? Value)
{
    /// <summary>Potřebuje tenhle operátor hodnotu?</summary>
    public bool NeedsValue =>
        Operator is not (FilterOperator.IsNull or FilterOperator.IsNotNull);
}

/// <summary>
/// Požadavek na stránku dat.
/// </summary>
/// <remarks>
/// Stránkuje, řadí i filtruje **databáze**, ne prohlížečka. Načíst celou tabulku
/// do paměti a krájet ji až v UI by u milionů řádků neprošlo ani serveru, ani klientovi;
/// tady se přenáší vždy jen jedna stránka.
/// </remarks>
public sealed record DataQuery
{
    /// <summary>Stránka počítaná od nuly.</summary>
    public int Page { get; init; }

    /// <summary>Počet řádků na stránku. Ořízne se na povolené maximum.</summary>
    public int PageSize { get; init; } = 50;

    /// <summary>Sloupec, podle kterého se řadí. Ověřuje se proti schématu.</summary>
    public string? SortColumn { get; init; }

    /// <summary>Řadit sestupně.</summary>
    public bool SortDescending { get; init; }

    /// <summary>Filtry nad sloupci. Spojují se přes AND.</summary>
    public IReadOnlyList<DataFilter> Filters { get; init; } = [];
}
