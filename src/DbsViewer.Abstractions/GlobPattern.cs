namespace DbsViewer;

/// <summary>
/// Porovnání jména proti vzoru se zástupnými znaky <c>*</c> a <c>?</c>.
/// Používá se pro skrývání tabulek a maskování sloupců, kde by regulární výraz
/// z konfigurace byl zbytečně silný nástroj.
/// </summary>
public static class GlobPattern
{
    /// <summary>Porovná jméno se vzorem, bez ohledu na velikost písmen.</summary>
    public static bool IsMatch(string? value, string? pattern)
    {
        if (string.IsNullOrEmpty(pattern) || value is null)
        {
            return false;
        }

        return IsMatch(value.AsSpan(), pattern.AsSpan());
    }

    private static bool IsMatch(ReadOnlySpan<char> value, ReadOnlySpan<char> pattern)
    {
        var v = 0;
        var p = 0;
        var starPattern = -1;
        var starValue = 0;

        while (v < value.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || Same(pattern[p], value[v])))
            {
                v++;
                p++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                starPattern = p++;
                starValue = v;
            }
            else if (starPattern >= 0)
            {
                p = starPattern + 1;
                v = ++starValue;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }

        return p == pattern.Length;
    }

    private static bool Same(char a, char b) =>
        a == b || char.ToUpperInvariant(a) == char.ToUpperInvariant(b);
}
