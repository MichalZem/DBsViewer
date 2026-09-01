using System.Data.Common;

namespace DbsViewer.Server;

/// <summary>Hotový dotaz: text a hodnoty, které se do něj navážou jako parametry.</summary>
/// <param name="Sql">Text dotazu.</param>
/// <param name="Parameters">Hodnoty parametrů v pořadí <c>@p0</c>, <c>@p1</c>, …</param>
public readonly record struct BuiltQuery(string Sql, IReadOnlyList<object> Parameters);

/// <summary>
/// Skládání SQL pro stránkovaný náhled dat.
/// </summary>
/// <remarks>
/// Nejcitlivější kód v celé komponentě: jako jediný staví SQL z něčeho, co přišlo
/// z požadavku. Platí tu proto dvě pravidla bez výjimky:
///
/// **Identifikátory se nikdy neescapují, ale ověřují.** Jméno sloupce z požadavku se musí
/// shodovat se sloupcem načteného schématu; do textu dotazu se pak vloží jméno ze schématu,
/// ne to z požadavku. Escapování by stačilo taky, ale ověření je bezpečné i tehdy, když
/// se v escapování někdy najde chyba.
///
/// **Hodnoty se do textu nedostanou vůbec** — jdou přes <see cref="DbParameter"/>.
/// Čísla stránek jsou celá čísla ověřená proti mezím, takže se vkládají přímo;
/// řetězec se do dotazu nedostane nikdy.
/// </remarks>
public static class DataQueryBuilder
{
    /// <summary>Sestaví dotaz na jednu stránku dat.</summary>
    /// <param name="table">Tabulka z načteného schématu.</param>
    /// <param name="query">Požadavek: stránka, řazení a filtry.</param>
    /// <param name="isSqlite">Skládá se dotaz pro SQLite, nebo pro SQL Server?</param>
    public static BuiltQuery BuildPage(DbTable table, DataQuery query, bool isSqlite)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(query);

        var parametry = new List<object>();
        var where = BuildWhere(table, query.Filters, parametry, isSqlite);
        var name = QuoteName(table.Name, isSqlite);
        var offset = (long)query.Page * query.PageSize;

        var sql = $"SELECT * FROM {name}{where}{BuildOrderBy(table, query, isSqlite)}";

        // SQL Server umí OFFSET/FETCH jen s ORDER BY; o to se stará BuildOrderBy,
        // které vždycky nějaké řazení vrátí.
        sql += isSqlite
            ? $" LIMIT {query.PageSize} OFFSET {offset}"
            : $" OFFSET {offset} ROWS FETCH NEXT {query.PageSize} ROWS ONLY";

        return new BuiltQuery(sql, parametry);
    }

    /// <summary>Sestaví dotaz na počet řádků odpovídajících filtrům.</summary>
    public static BuiltQuery BuildCount(DbTable table, DataQuery query, bool isSqlite)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(query);

        var parametry = new List<object>();
        var where = BuildWhere(table, query.Filters, parametry, isSqlite);

        return new BuiltQuery(
            $"SELECT COUNT(*) FROM {QuoteName(table.Name, isSqlite)}{where}",
            parametry);
    }

    /// <summary>
    /// Klauzule ORDER BY. Vrací ji vždy — bez řazení není stránkování stabilní
    /// a SQL Server bez něj OFFSET/FETCH ani nepovolí.
    /// </summary>
    internal static string BuildOrderBy(DbTable table, DataQuery query, bool isSqlite)
    {
        if (FindColumn(table, query.SortColumn) is { } sloupec)
        {
            return $" ORDER BY {QuoteColumn(sloupec.Name, isSqlite)}{(query.SortDescending ? " DESC" : "")}";
        }

        // Bez zvoleného řazení se řadí podle primárního klíče: databáze jinak může vrátit
        // řádky pokaždé v jiném pořadí a při listování by některé chyběly a jiné se
        // opakovaly.
        var klic = table.PrimaryKey?.Columns ?? [];

        if (klic.Count > 0)
        {
            var sloupce = klic
                .Select(c => FindColumn(table, c))
                .OfType<DbColumn>()
                .Select(c => QuoteColumn(c.Name, isSqlite))
                .ToList();

            if (sloupce.Count > 0)
            {
                return $" ORDER BY {string.Join(", ", sloupce)}";
            }
        }

        // Tabulka bez klíče: pořadí je libovolné, ale syntakticky musí něco stát.
        return isSqlite ? "" : " ORDER BY (SELECT NULL)";
    }

    /// <summary>Klauzule WHERE. Hodnoty přidává do <paramref name="parameters"/>.</summary>
    internal static string BuildWhere(
        DbTable table,
        IReadOnlyList<DataFilter> filters,
        List<object> parameters,
        bool isSqlite)
    {
        var podminky = new List<string>();

        foreach (var filter in filters)
        {
            // Neznámý sloupec se přeskočí, ne aby se dotaz odmítl: filtr může zůstat
            // z předchozí tabulky a odmítnutí by uživateli nic neřeklo.
            if (FindColumn(table, filter.Column) is not { } sloupec)
            {
                continue;
            }

            if (filter.NeedsValue && string.IsNullOrEmpty(filter.Value))
            {
                continue;
            }

            var jmeno = QuoteColumn(sloupec.Name, isSqlite);
            var index = parameters.Count;

            switch (filter.Operator)
            {
                case FilterOperator.IsNull:
                    podminky.Add($"{jmeno} IS NULL");
                    break;

                case FilterOperator.IsNotNull:
                    podminky.Add($"{jmeno} IS NOT NULL");
                    break;

                case FilterOperator.Equals:
                    podminky.Add($"{jmeno} = @p{index}");
                    parameters.Add(filter.Value!);
                    break;

                case FilterOperator.GreaterThan:
                    podminky.Add($"{jmeno} > @p{index}");
                    parameters.Add(filter.Value!);
                    break;

                case FilterOperator.LessThan:
                    podminky.Add($"{jmeno} < @p{index}");
                    parameters.Add(filter.Value!);
                    break;

                default:
                    // Textové hledání jde přes LIKE nad převedenou hodnotou, aby fungovalo
                    // i nad čísly a daty — uživatel v mřížce nerozlišuje typ sloupce.
                    podminky.Add($"{AsText(jmeno, isSqlite)} LIKE @p{index} ESCAPE '\\'");
                    parameters.Add(Wildcards(filter.Operator, filter.Value!));
                    break;
            }
        }

        return podminky.Count == 0 ? "" : " WHERE " + string.Join(" AND ", podminky);
    }

    /// <summary>Vzor pro LIKE podle operátoru. Zástupné znaky v hodnotě se ruší.</summary>
    /// <remarks>
    /// Escapuje se zpětným lomítkem a klauzulí <c>ESCAPE</c>, protože hranaté závorky
    /// zná jen SQL Server — SQLite by je vzal jako obyčejné znaky a hledal něco jiného.
    /// Bez escapování by uživatelské „%" hledalo cokoli a „_" jakýkoli znak; v mřížce
    /// se přitom čeká hledání textu, ne zadávání vzoru.
    /// </remarks>
    internal static string Wildcards(FilterOperator op, string value)
    {
        // Zpětné lomítko musí jít první, jinak by se escapovalo i to, co přidáme my.
        var escaped = value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

        return op switch
        {
            FilterOperator.StartsWith => escaped + "%",
            FilterOperator.EndsWith => "%" + escaped,
            _ => "%" + escaped + "%",
        };
    }

    /// <summary>Převod sloupce na text, aby LIKE fungoval nad libovolným typem.</summary>
    private static string AsText(string quotedColumn, bool isSqlite) =>
        isSqlite
            ? $"CAST({quotedColumn} AS TEXT)"
            : $"CAST({quotedColumn} AS NVARCHAR(MAX))";

    /// <summary>
    /// Najde sloupec ve schématu podle jména. Vrací sloupec ze schématu, takže se
    /// do dotazu dostane ověřené jméno, ne to z požadavku.
    /// </summary>
    internal static DbColumn? FindColumn(DbTable table, string? name) =>
        string.IsNullOrEmpty(name)
            ? null
            : table.Columns.FirstOrDefault(c =>
                string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Escapování jména sloupce podle providera.</summary>
    internal static string QuoteColumn(string name, bool isSqlite) =>
        isSqlite
            ? $"\"{name.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : $"[{name.Replace("]", "]]", StringComparison.Ordinal)}]";

    /// <summary>Escapování jména tabulky podle providera.</summary>
    internal static string QuoteName(DbObjectName name, bool isSqlite)
    {
        if (isSqlite)
        {
            return $"\"{name.Name.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }

        var table = $"[{name.Name.Replace("]", "]]", StringComparison.Ordinal)}]";

        return name.Schema is { } schema
            ? $"[{schema.Replace("]", "]]", StringComparison.Ordinal)}].{table}"
            : table;
    }

    /// <summary>Naváže hodnoty na příkaz jako parametry.</summary>
    public static void Bind(DbCommand command, IReadOnlyList<object> parameters)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(parameters);

        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = $"@p{i}";
            parameter.Value = parameters[i];
            command.Parameters.Add(parameter);
        }
    }
}
