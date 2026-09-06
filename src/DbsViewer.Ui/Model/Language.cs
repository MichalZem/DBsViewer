using System.Globalization;

namespace DbsViewer.Ui.Model;

/// <summary>Jazyk prohlížečky.</summary>
public enum Language
{
    /// <summary>Angličtina. Výchozí — prohlížečka se instaluje po celém světě.</summary>
    English,

    /// <summary>Čeština.</summary>
    Czech,
}

/// <summary>
/// Volba jazyka a její promítnutí do adresy.
/// </summary>
/// <remarks>
/// Jazyk žije v query stringu, ne v úložišti prohlížeče. Důvody jsou tři: přežije to
/// znovunačtení, které přepnutí jazyka stejně vyžaduje; adresa se dá poslat kolegovi
/// i s jazykem; a nepotřebuje to <c>localStorage</c>, tedy ani JavaScript.
///
/// Čtení i skládání adresy je čistá funkce nad řetězcem, aby šlo otestovat bez prohlížeče.
/// </remarks>
public static class LanguageChoice
{
    /// <summary>Jméno parametru v adrese.</summary>
    public const string QueryName = "lang";

    /// <summary>Jazyk, ve kterém prohlížečka běží, když adresa neříká jinak.</summary>
    public const Language Default = Language.English;

    /// <summary>Dvoupísmenný kód jazyka. Používá se v adrese i pro hledání satelitní assembly.</summary>
    public static string Code(Language language) => language switch
    {
        Language.Czech => "cs",
        _ => "en",
    };

    /// <summary>Jméno jazyka v něm samotném — v přepínači se jazyky nepřekládají.</summary>
    public static string NativeName(Language language) => language switch
    {
        Language.Czech => "Čeština",
        _ => "English",
    };

    /// <summary>
    /// Kultura pro <see cref="System.Resources.ResourceManager"/>.
    /// </summary>
    /// <remarks>
    /// Angličtina je invariantní kultura, ne <c>en</c>: neutrální <c>.resx</c> je anglický,
    /// takže se pro něj žádná satelitní assembly nehledá.
    /// </remarks>
    public static CultureInfo Culture(Language language) => language switch
    {
        Language.Czech => new CultureInfo("cs"),
        _ => CultureInfo.InvariantCulture,
    };

    /// <summary>
    /// Jazyk z adresy. Neznámý nebo chybějící kód znamená výchozí jazyk — adresa
    /// přichází od uživatele a překlep v ní nesmí prohlížečku shodit.
    /// </summary>
    /// <param name="uri">Celá adresa včetně query stringu.</param>
    public static Language FromUri(string? uri)
    {
        if (uri is null)
        {
            return Default;
        }

        var otaznik = uri.IndexOf('?', StringComparison.Ordinal);

        if (otaznik < 0)
        {
            return Default;
        }

        // Fragment za # do query stringu nepatří.
        var query = uri[(otaznik + 1)..];
        var mrizka = query.IndexOf('#', StringComparison.Ordinal);

        if (mrizka >= 0)
        {
            query = query[..mrizka];
        }

        foreach (var dvojice in query.Split('&'))
        {
            var rovnitko = dvojice.IndexOf('=', StringComparison.Ordinal);

            if (rovnitko < 0
                || !dvojice[..rovnitko].Equals(QueryName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return FromCode(dvojice[(rovnitko + 1)..]);
        }

        return Default;
    }

    /// <summary>Jazyk podle dvoupísmenného kódu; neznámý kód dá výchozí jazyk.</summary>
    public static Language FromCode(string? code) =>
        string.Equals(code, "cs", StringComparison.OrdinalIgnoreCase) ? Language.Czech : Default;

    /// <summary>
    /// Adresa, na kterou se přejde při přepnutí jazyka. Ostatní parametry zůstávají,
    /// aby si člověk po přepnutí nemusel znovu nastavovat, co v adrese měl.
    /// </summary>
    /// <param name="uri">Současná adresa.</param>
    /// <param name="language">Jazyk, na který se přepíná.</param>
    public static string UrlFor(string uri, Language language)
    {
        ArgumentNullException.ThrowIfNull(uri);

        var otaznik = uri.IndexOf('?', StringComparison.Ordinal);
        var zaklad = otaznik < 0 ? uri : uri[..otaznik];
        var ostatni = new List<string>();

        if (otaznik >= 0)
        {
            foreach (var dvojice in uri[(otaznik + 1)..].Split('&'))
            {
                var rovnitko = dvojice.IndexOf('=', StringComparison.Ordinal);
                var jmeno = rovnitko < 0 ? dvojice : dvojice[..rovnitko];

                if (dvojice.Length > 0 && !jmeno.Equals(QueryName, StringComparison.OrdinalIgnoreCase))
                {
                    ostatni.Add(dvojice);
                }
            }
        }

        ostatni.Add($"{QueryName}={Code(language)}");

        return $"{zaklad}?{string.Join("&", ostatni)}";
    }
}
