namespace DbsViewer.Ui.Model;

/// <summary>
/// Filtrování a hledání nad načteným schématem. Čistá logika bez vazby na komponenty,
/// aby se dala testovat bez vykreslování.
/// </summary>
public static class SchemaFilter
{
    /// <summary>
    /// Najde tabulky odpovídající hledanému textu. Hledá se v názvu tabulky, v názvech
    /// sloupců i v CLR jménech entit — sloupec je často to jediné, co uživatel zná.
    /// </summary>
    public static IReadOnlyList<DbTable> Search(IReadOnlyList<DbTable> tables, string? query)
    {
        ArgumentNullException.ThrowIfNull(tables);

        if (string.IsNullOrWhiteSpace(query))
        {
            return tables;
        }

        var needle = query.Trim();

        return [.. tables.Where(table => Matches(table, needle))];
    }

    /// <summary>Odpovídá tabulka hledanému textu?</summary>
    public static bool Matches(DbTable table, string needle)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentException.ThrowIfNullOrEmpty(needle);

        if (Contains(table.Name.Name, needle) || Contains(table.Name.Schema, needle))
        {
            return true;
        }

        foreach (var entity in table.EntityClrNames)
        {
            if (Contains(entity, needle))
            {
                return true;
            }
        }

        foreach (var column in table.Columns)
        {
            if (Contains(column.Name, needle))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Sloupce tabulky, které odpovídají hledanému textu. Pro zvýraznění v detailu.</summary>
    public static IReadOnlySet<string> MatchingColumns(DbTable table, string? query)
    {
        ArgumentNullException.ThrowIfNull(table);

        if (string.IsNullOrWhiteSpace(query))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var needle = query.Trim();

        return new HashSet<string>(
            table.Columns.Where(c => Contains(c.Name, needle)).Select(static c => c.Name),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Omezí tabulky na skupinu podle vzoru z konfigurace.</summary>
    public static IReadOnlyList<DbTable> InGroup(IReadOnlyList<DbTable> tables, string? pattern)
    {
        ArgumentNullException.ThrowIfNull(tables);

        if (string.IsNullOrWhiteSpace(pattern))
        {
            return tables;
        }

        return
        [
            .. tables.Where(t =>
                GlobPattern.IsMatch(t.Name.Name, pattern) || GlobPattern.IsMatch(t.Qualified, pattern)),
        ];
    }

    /// <summary>Schémata vyskytující se ve schématu, seřazená. Prázdné schéma se vynechá.</summary>
    public static IReadOnlyList<string> Schemas(IReadOnlyList<DbTable> tables)
    {
        ArgumentNullException.ThrowIfNull(tables);

        return
        [
            .. tables
                .Select(static t => t.Name.Schema)
                .Where(static s => s is not null)
                .Select(static s => s!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase),
        ];
    }

    private static bool Contains(string? value, string needle) =>
        value is not null && value.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
