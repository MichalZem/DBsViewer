namespace DbsViewer;

/// <summary>
/// Rozpozná vazební tabulky vztahu N:M podle jejich tvaru.
/// </summary>
/// <remarks>
/// Sdílené oběma zdroji schématu záměrně: kdyby EF model a živá databáze používaly každý
/// jinou heuristiku, diff by hlásil rozdíly, které v databázi nejsou.
/// </remarks>
public static class JoinTableDetector
{
    /// <summary>
    /// Vazební tabulka má primární klíč složený výhradně ze sloupců právě dvou cizích klíčů
    /// a nenese žádná vlastní data. Tabulka s vlastním sloupcem je plnohodnotná entita,
    /// i když vazbu tvarem připomíná.
    /// </summary>
    public static IReadOnlySet<DbObjectName> Detect(IReadOnlyList<DbTable> tables)
    {
        ArgumentNullException.ThrowIfNull(tables);

        var detected = new HashSet<DbObjectName>();

        foreach (var table in tables)
        {
            if (IsJoinTable(table))
            {
                detected.Add(table.Name);
            }
        }

        return detected;
    }

    /// <summary>Posoudí jedinou tabulku. Vystaveno kvůli testům i kvůli detailu v UI.</summary>
    public static bool IsJoinTable(DbTable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        if (table.IsView || table.ForeignKeys.Count != 2 || table.PrimaryKey is not { } primaryKey)
        {
            return false;
        }

        if (primaryKey.Columns.Count < 2)
        {
            return false;
        }

        var foreignKeyColumns = new HashSet<string>(
            table.ForeignKeys.SelectMany(static f => f.Columns),
            StringComparer.OrdinalIgnoreCase);

        foreach (var column in primaryKey.Columns)
        {
            if (!foreignKeyColumns.Contains(column))
            {
                return false;
            }
        }

        // Sloupec, který není v žádném cizím klíči, znamená vlastní data.
        foreach (var column in table.Columns)
        {
            if (!foreignKeyColumns.Contains(column.Name))
            {
                return false;
            }
        }

        return true;
    }
}
