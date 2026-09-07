namespace DbsViewer.Ui.Model;

/// <summary>
/// Čtení a skládání query stringu.
/// </summary>
/// <remarks>
/// Vlastní parser, ne <c>QueryHelpers</c> z ASP.NET Core: ten je v balíčku, který
/// WebAssembly klient jinak nepotřebuje. Navíc jde o čisté funkce nad řetězcem,
/// takže se dají testovat bez prohlížeče — a testuje se přes ně stav celé prohlížečky.
///
/// Hodnoty se kódují přes <see cref="Uri.EscapeDataString(string)"/>, tedy mezera jako
/// <c>%20</c>, ne jako <c>+</c>. Jména tabulek mezery i tečky obsahovat můžou a musí
/// projít adresou tam i zpátky beze změny.
/// </remarks>
internal static class QueryString
{
    /// <summary>Adresa bez query stringu a bez fragmentu.</summary>
    public static string Path(string uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        var konec = uri.IndexOfAny(['?', '#']);

        return konec < 0 ? uri : uri[..konec];
    }

    /// <summary>
    /// Rozebrané parametry v pořadí, ve kterém stojí v adrese. Dvojice bez <c>=</c>
    /// má prázdnou hodnotu, prázdné dvojice se přeskakují.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Parse(string? uri)
    {
        if (uri is null)
        {
            return [];
        }

        var otaznik = uri.IndexOf('?', StringComparison.Ordinal);

        if (otaznik < 0)
        {
            return [];
        }

        // Fragment za # do query stringu nepatří.
        var query = uri[(otaznik + 1)..];
        var mrizka = query.IndexOf('#', StringComparison.Ordinal);

        if (mrizka >= 0)
        {
            query = query[..mrizka];
        }

        var dvojice = new List<KeyValuePair<string, string>>();

        foreach (var cast in query.Split('&'))
        {
            if (cast.Length == 0)
            {
                continue;
            }

            var rovnitko = cast.IndexOf('=', StringComparison.Ordinal);

            var jmeno = rovnitko < 0 ? cast : cast[..rovnitko];
            var hodnota = rovnitko < 0 ? "" : cast[(rovnitko + 1)..];

            dvojice.Add(new KeyValuePair<string, string>(
                Uri.UnescapeDataString(jmeno), Uri.UnescapeDataString(hodnota)));
        }

        return dvojice;
    }

    /// <summary>
    /// Hodnota prvního parametru daného jména, nebo <c>null</c>, když v adrese není.
    /// Jméno se porovnává bez ohledu na velikost písmen — adresa přichází od uživatele.
    /// </summary>
    public static string? Value(string? uri, string name)
    {
        foreach (var (jmeno, hodnota) in Parse(uri))
        {
            if (jmeno.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return hodnota;
            }
        }

        return null;
    }

    /// <summary>
    /// Složí adresu ze stávající: parametry ze seznamu <paramref name="own"/> nahradí
    /// hodnotami z <paramref name="values"/>, ostatní nechá být.
    /// </summary>
    /// <remarks>
    /// Cizí parametry musí přežít, protože v adrese stojí i volba jazyka a případné
    /// parametry hostitelské aplikace. Zahodit je by znamenalo, že klik v prohlížečce
    /// přepne uživateli jazyk zpátky na výchozí.
    /// </remarks>
    /// <param name="uri">Současná adresa.</param>
    /// <param name="own">Jména parametrů, o která se volající stará.</param>
    /// <param name="values">Nové hodnoty. Co v seznamu není, se do adresy nedostane.</param>
    public static string Merge(
        string uri,
        IEnumerable<string> own,
        IEnumerable<KeyValuePair<string, string>> values)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(own);
        ArgumentNullException.ThrowIfNull(values);

        var vlastni = new HashSet<string>(own, StringComparer.OrdinalIgnoreCase);
        var casti = new List<string>();

        foreach (var (jmeno, hodnota) in Parse(uri))
        {
            if (!vlastni.Contains(jmeno))
            {
                casti.Add($"{Uri.EscapeDataString(jmeno)}={Uri.EscapeDataString(hodnota)}");
            }
        }

        foreach (var (jmeno, hodnota) in values)
        {
            casti.Add($"{Uri.EscapeDataString(jmeno)}={Uri.EscapeDataString(hodnota)}");
        }

        var zaklad = Path(uri);

        return casti.Count == 0 ? zaklad : $"{zaklad}?{string.Join("&", casti)}";
    }
}
