namespace DbsViewer.Server;

/// <summary>Hodnota jednoho sloupce v požadavku na zápis.</summary>
/// <param name="Column">Jméno sloupce; ověřuje se proti schématu.</param>
/// <param name="Value">Hodnota jako text. <c>null</c> znamená SQL NULL.</param>
public sealed record DataValue(string Column, string? Value);

/// <summary>Požadavek na úpravu jednoho řádku.</summary>
/// <remarks>
/// Řádek se adresuje výhradně primárním klíčem. Posílají se jen sloupce, které se
/// opravdu mění — přepsat celý řádek by zbytečně rozhýbalo triggery i concurrency tokeny.
/// </remarks>
public sealed record DataUpdate
{
    /// <summary>Hodnoty primárního klíče. Musí být kompletní.</summary>
    public IReadOnlyList<DataValue> Key { get; init; } = [];

    /// <summary>Nové hodnoty měněných sloupců.</summary>
    public IReadOnlyList<DataValue> Values { get; init; } = [];
}

/// <summary>Požadavek na vložení jednoho řádku.</summary>
/// <remarks>
/// Klíč se neposílá: nový řádek žádný ještě nemá. Posílají se jen sloupce, které uživatel
/// vyplnil — u zbytku se nechá zapracovat výchozí hodnota z databáze.
/// </remarks>
public sealed record DataInsert
{
    /// <summary>Hodnoty vyplněných sloupců.</summary>
    public IReadOnlyList<DataValue> Values { get; init; } = [];
}

/// <summary>Požadavek na smazání jednoho řádku.</summary>
public sealed record DataDelete
{
    /// <summary>Hodnoty primárního klíče. Musí být kompletní.</summary>
    public IReadOnlyList<DataValue> Key { get; init; } = [];
}

/// <summary>Výsledek zápisu.</summary>
public sealed record DataChangeResult
{
    /// <summary>Kolik řádků se změnilo. Vždy jeden — jinak se zápis odmítne.</summary>
    public required int Affected { get; init; }
}

/// <summary>
/// Požadavek na zápis se nedá provést tak, jak přišel.
/// </summary>
/// <remarks>
/// Od <see cref="InvalidOperationException"/> se liší tím, kdo udělal chybu: tahle výjimka
/// znamená vadný požadavek (neznámý sloupec, nepřevoditelná hodnota, chybějící klíč)
/// a končí jako <c>400</c>, kdežto vypnutý nebo nepovolený zápis je odmítnutí a končí
/// jako <c>403</c>. Zpráva je česky, protože ji uživatel uvidí přímo v mřížce.
/// </remarks>
public sealed class DataRequestException : Exception
{
    /// <inheritdoc cref="DataRequestException"/>
    public DataRequestException(string message) : base(message)
    {
    }

    /// <inheritdoc cref="DataRequestException"/>
    public DataRequestException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
