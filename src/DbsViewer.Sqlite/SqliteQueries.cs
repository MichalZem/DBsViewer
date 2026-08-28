namespace DbsViewer.Sqlite;

/// <summary>
/// Introspekční dotazy pro SQLite.
/// </summary>
/// <remarks>
/// Příkaz <c>PRAGMA</c> nepřijímá parametry, takže se jméno tabulky musí vložit do textu.
/// Nejde ale o uživatelský vstup — jména pocházejí z <c>sqlite_master</c>, tedy ze samotné
/// databáze, a přesto se pro jistotu escapují uvozovkami podle pravidel SQLite.
/// </remarks>
internal static class SqliteQueries
{
    /// <summary>Tabulky a pohledy. Interní objekty SQLite začínají na <c>sqlite_</c>.</summary>
    public const string Objects = """
        SELECT name, type, sql
        FROM sqlite_master
        WHERE type IN ('table', 'view')
          AND name NOT LIKE 'sqlite_%'
        ORDER BY name
        """;

    public const string MigrationsHistoryExists = """
        SELECT COUNT(*)
        FROM sqlite_master
        WHERE type = 'table' AND name = '__EFMigrationsHistory'
        """;

    public const string AppliedMigrations = """
        SELECT MigrationId
        FROM __EFMigrationsHistory
        ORDER BY MigrationId
        """;

    /// <summary>
    /// Sloupce tabulky. Použije se <c>table_xinfo</c>, ne <c>table_info</c> — to generované
    /// sloupce vůbec nevrací. Navíc vrací příznak <c>hidden</c>, ze kterého se pozná,
    /// jestli je generovaný sloupec ukládaný, nebo počítaný za běhu.
    /// </summary>
    public static string TableInfo(string table) => $"PRAGMA table_xinfo({Quote(table)})";

    public static string IndexList(string table) => $"PRAGMA index_list({Quote(table)})";

    public static string IndexInfo(string index) => $"PRAGMA index_info({Quote(index)})";

    public static string ForeignKeyList(string table) => $"PRAGMA foreign_key_list({Quote(table)})";

    /// <summary>
    /// Počet řádků. SQLite nevede statistiky jako SQL Server, takže se musí počítat —
    /// proto je to za volbou <see cref="SchemaReadOptions.IncludeRowCounts"/> a ne ve výchozím stavu.
    /// </summary>
    public static string RowCount(string table) => $"SELECT COUNT(*) FROM {Quote(table)}";

    /// <summary>
    /// Escapování identifikátoru podle SQLite: dvojité uvozovky, vnitřní uvozovka se zdvojí.
    /// </summary>
    public static string Quote(string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        return $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
