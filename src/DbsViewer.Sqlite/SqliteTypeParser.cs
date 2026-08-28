using System.Globalization;
using System.Text.RegularExpressions;

namespace DbsViewer.Sqlite;

/// <summary>
/// Čtení údajů, které SQLite nevystavuje přes <c>PRAGMA</c> a musí se dolovat
/// z deklarovaného typu nebo z původního <c>CREATE TABLE</c>.
/// </summary>
internal static partial class SqliteTypeParser
{
    /// <summary>Zástupný text tam, kde SQLite údaj má, ale nedá se přečíst.</summary>
    public const string NotAvailable = "(částečný index)";

    /// <summary>
    /// Délka, přesnost a měřítko z deklarovaného typu — <c>nvarchar(200)</c> nebo
    /// <c>decimal(18, 2)</c>. SQLite typ nevynucuje, ale EF ho do DDL zapisuje,
    /// takže se dá přečíst zpátky.
    /// </summary>
    public static (int? MaxLength, int? Precision, int? Scale) ParseFacets(string? declaredType)
    {
        if (string.IsNullOrWhiteSpace(declaredType))
        {
            return (null, null, null);
        }

        var match = FacetsPattern().Match(declaredType);
        if (!match.Success)
        {
            return (null, null, null);
        }

        var first = ParseInt(match.Groups["first"].Value);
        var second = match.Groups["second"].Success ? ParseInt(match.Groups["second"].Value) : null;

        // Dvě čísla znamenají přesnost a měřítko, jedno je délka.
        return second is null ? (first, null, null) : (null, first, second);
    }

    /// <summary>
    /// Generované sloupce z původního <c>CREATE TABLE</c>. <c>PRAGMA table_info</c> je
    /// nevrací jako generované a <c>table_xinfo</c> sice ano, ale bez výrazu.
    /// </summary>
    public static Dictionary<string, GeneratedColumn> FindGeneratedColumns(string? createSql)
    {
        var found = new Dictionary<string, GeneratedColumn>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(createSql))
        {
            return found;
        }

        foreach (Match match in GeneratedColumnPattern().Matches(createSql))
        {
            var name = match.Groups["name"].Value.Trim('"', '[', ']', '`', '\'');
            var expression = match.Groups["expr"].Value.Trim();
            var isStored = match.Groups["stored"].Success
                && match.Groups["stored"].Value.Equals("STORED", StringComparison.OrdinalIgnoreCase);

            found[name] = new GeneratedColumn(expression, isStored);
        }

        return found;
    }

    /// <summary>
    /// Jméno cizího klíče. SQLite ho nevystavuje, takže se skládá stejně, jako ho tvoří
    /// EF Core — aby diff proti EF modelu nehlásil rozdíl jen kvůli jménu.
    /// </summary>
    public static string ForeignKeyName(string table, string principalTable, int id) =>
        id == 0
            ? $"FK_{table}_{principalTable}"
            : $"FK_{table}_{principalTable}_{id}";

    private static int? ParseInt(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    [GeneratedRegex(@"\(\s*(?<first>\d+)\s*(?:,\s*(?<second>\d+)\s*)?\)", RegexOptions.ExplicitCapture)]
    private static partial Regex FacetsPattern();

    [GeneratedRegex(
        """(?<name>"[^"]+"|\[[^\]]+\]|`[^`]+`|\w+)\s+[^,()]*?\bGENERATED\s+ALWAYS\s+AS\s*\((?<expr>(?:[^()]|\([^()]*\))*)\)\s*(?<stored>STORED|VIRTUAL)?""",
        RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture)]
    private static partial Regex GeneratedColumnPattern();
}

/// <summary>Generovaný sloupec vyčtený z DDL.</summary>
internal sealed record GeneratedColumn(string Expression, bool IsStored);
