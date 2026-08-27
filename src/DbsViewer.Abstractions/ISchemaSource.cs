namespace DbsViewer;

/// <summary>
/// Zdroj databázového schématu. Implementace čte z EF modelu nebo z živé databáze,
/// ale vrací vždy stejný <see cref="DatabaseSchema"/>, takže diff engine ani UI
/// nemusí vědět, odkud data pocházejí.
/// </summary>
public interface ISchemaSource
{
    /// <summary>
    /// Klíč pro adresaci zdroje, když je v aplikaci registrovaných víc databází.
    /// Výchozí zdroj používá <see cref="DefaultKey"/>.
    /// </summary>
    string Key { get; }

    /// <summary>Popisek pro UI, např. „EF model (ShopContext)".</summary>
    string DisplayName { get; }

    SchemaSourceKind Kind { get; }

    Task<DatabaseSchema> ReadAsync(SchemaReadOptions options, CancellationToken cancellationToken = default);

    /// <summary>Klíč výchozího, nepojmenovaného zdroje.</summary>
    public const string DefaultKey = "default";
}

/// <summary>Co všechno se má při čtení schématu zjišťovat.</summary>
public sealed record SchemaReadOptions
{
    /// <summary>Sdílená instance s výchozím nastavením.</summary>
    public static SchemaReadOptions Default { get; } = new();

    /// <summary>
    /// Zjišťovat odhad počtu řádků. Jen ze statistik databáze, nikdy přes <c>COUNT(*)</c>.
    /// Zdroj z EF modelu tuto volbu ignoruje — z modelu se počet řádků zjistit nedá.
    /// </summary>
    public bool IncludeRowCounts { get; init; }

    /// <summary>
    /// Načíst seznam migrací. Vyžaduje dotaz do databáze i u zdroje z EF modelu;
    /// selhání se nikdy nepropaguje ven, jen se zapíše do <see cref="DatabaseSchema.Warnings"/>.
    /// </summary>
    public bool IncludeMigrations { get; init; } = true;

    /// <summary>Detekovat vazební tabulky N:M a sbalovat je do jediného vztahu.</summary>
    public bool DetectJoinTables { get; init; } = true;

    /// <summary>
    /// Tabulky, které se do výsledku nedostanou. Podporuje zástupný znak <c>*</c>,
    /// např. <c>AspNetUser*</c> nebo <c>audit.*</c>.
    /// </summary>
    public IReadOnlyList<string> HideTables { get; init; } = [];

    /// <summary>Když je neprázdné, načtou se jen tabulky z uvedených schémat.</summary>
    public IReadOnlyList<string> IncludeSchemas { get; init; } = [];

    /// <summary>Rozhodne, jestli se tabulka má do výsledku dostat.</summary>
    public bool IsVisible(DbObjectName table)
    {
        if (IncludeSchemas.Count > 0)
        {
            var schema = table.Schema ?? string.Empty;
            var matched = false;
            foreach (var included in IncludeSchemas)
            {
                if (string.Equals(schema, included, StringComparison.OrdinalIgnoreCase))
                {
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                return false;
            }
        }

        foreach (var pattern in HideTables)
        {
            if (GlobPattern.IsMatch(table.Name, pattern) || GlobPattern.IsMatch(table.Qualified, pattern))
            {
                return false;
            }
        }

        return true;
    }
}
