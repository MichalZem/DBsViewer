using System.Globalization;
using DbsViewer.Ui.Model;

namespace DbsViewer.Ui.Components;

/// <summary>
/// Stav prohlížečky zapsaný do adresy a zpátky.
/// </summary>
/// <remarks>
/// Prohlížečka je jedna stránka, ale uvnitř se naviguje: mezi panely, tabulkami
/// a verzemi schématu. Dokud to adresa neodrážela, chovala se prohlížečka jako slepá
/// ulička — tlačítko Zpět vyskočilo z aplikace ven, F5 hodilo člověka na začátek
/// a odkaz na konkrétní tabulku poslat nešlo.
///
/// Stav žije v query stringu, ne v cestě: prohlížečka se montuje pod libovolný prefix
/// hostitelské aplikace a k cestě se tedy nesmí vyjadřovat. Zapisují se jen hodnoty,
/// které se liší od výchozích — adresa nedotčené prohlížečky zůstane čistá.
///
/// Převod je čistá funkce nad řetězcem, aby se dal testovat bez prohlížeče.
/// </remarks>
internal sealed record ViewerRoute
{
    /// <summary>Sloučený zdroj — hodnota, se kterou <see cref="ViewerState.Source"/> startuje.</summary>
    private const string VychoziZdroj = "merged";

    /// <summary>
    /// Parametry, o které se stav stará. Ostatní v adrese zůstanou.
    /// </summary>
    /// <remarks>
    /// <c>lang</c> tu schválně není — patří <see cref="LanguageChoice"/> a klik
    /// v prohlížečce ho nesmí přepsat.
    /// </remarks>
    internal static readonly string[] QueryNames =
    [
        "pane", "table", "tab", "version", "base",
        "source", "group", "schema", "q", "focus", "hops", "expand",
    ];

    /// <summary>Zobrazený panel.</summary>
    public ViewerPane Pane { get; init; } = ViewerPane.Browser;

    /// <summary>Vybraná tabulka.</summary>
    public DbObjectName? Table { get; init; }

    /// <summary>Záložka detailu tabulky.</summary>
    public DetailTab Tab { get; init; } = DetailTab.Columns;

    /// <summary>Migrace, ke které se schéma zobrazuje, nebo <c>null</c> pro aktuální stav.</summary>
    public string? Version { get; init; }

    /// <summary>Migrace zvolená jako základ vizuálního porovnání.</summary>
    public string? Baseline { get; init; }

    /// <summary>
    /// Zdroj schématu: <c>ef</c>, <c>live</c> nebo <c>merged</c>.
    /// </summary>
    /// <remarks>
    /// Chybějící parametr znamená sloučený pohled, ne „nech, jak je". Bez toho by
    /// Zpět z pohledu na samotný EF model zdroj nevrátilo — v adrese, kam se člověk
    /// vrací, žádný zdroj není a nebylo by co nastavit. Když server sloučený pohled
    /// nenabízí, opraví si volbu sám při načtení schématu.
    /// </remarks>
    public string Source { get; init; } = VychoziZdroj;

    /// <summary>Vybraná skupina tabulek.</summary>
    public string? Group { get; init; }

    /// <summary>Vybrané schéma pro filtr.</summary>
    public string? Schema { get; init; }

    /// <summary>Text hledání.</summary>
    public string Search { get; init; } = "";

    /// <summary>Je zapnutý focus mode diagramu?</summary>
    public bool Focus { get; init; } = true;

    /// <summary>Vzdálenost sousedů ve focus modu.</summary>
    public int Hops { get; init; } = 1;

    /// <summary>Uzly diagramu zobrazené se všemi sloupci.</summary>
    public IReadOnlyList<DbObjectName> Expanded { get; init; } = [];

    /// <summary>Stav prohlížečky jako adresa.</summary>
    public static ViewerRoute From(ViewerState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new ViewerRoute
        {
            Pane = state.Pane,
            Table = state.SelectedTable,
            Tab = state.Tab,
            Version = state.SelectedMigration,
            Baseline = state.BaselineMigration,
            Source = state.Source,
            Group = state.Group,
            Schema = state.SchemaName,
            Search = state.Search,
            Focus = state.FocusEnabled,
            Hops = state.FocusHops,

            // Pořadí musí být stabilní, jinak by se adresa měnila jen tím, v jakém
            // pořadí se uzly rozbalovaly, a do historie by přibývaly stejné záznamy.
            Expanded = [.. state.ExpandedNodes.OrderBy(static n => n)],
        };
    }

    /// <summary>
    /// Adresa jako stav. Nesmyslná hodnota znamená výchozí — adresu píše i uživatel
    /// a překlep v ní nesmí prohlížečku shodit.
    /// </summary>
    public static ViewerRoute FromUri(string? uri)
    {
        var parametry = QueryString.Parse(uri);

        string? Hodnota(string jmeno)
        {
            foreach (var (klic, hodnota) in parametry)
            {
                if (klic.Equals(jmeno, StringComparison.OrdinalIgnoreCase))
                {
                    return hodnota is { Length: > 0 } ? hodnota : null;
                }
            }

            return null;
        }

        return new ViewerRoute
        {
            Pane = PanelZKodu(Hodnota("pane")),
            Table = JmenoZKodu(Hodnota("table")),
            Tab = ZalozkaZKodu(Hodnota("tab")),
            Version = Hodnota("version"),
            Baseline = Hodnota("base"),
            Source = Hodnota("source") ?? VychoziZdroj,
            Group = Hodnota("group"),
            Schema = Hodnota("schema"),
            Search = Hodnota("q") ?? "",
            Focus = Hodnota("focus") != "0",
            Hops = int.TryParse(Hodnota("hops"), out var hops) ? Math.Clamp(hops, 0, 3) : 1,
            Expanded = SeznamJmen(Hodnota("expand")),
        };
    }

    /// <summary>
    /// Adresa, na které tenhle stav stojí. Cizí parametry ze současné adresy zůstanou.
    /// </summary>
    /// <param name="uri">Současná adresa.</param>
    public string UrlFor(string uri) => QueryString.Merge(uri, QueryNames, Parametry());

    /// <summary>
    /// Přenese do stavu všechno kromě verze a základu porovnání — ty se musí dotáhnout
    /// ze serveru, takže je řeší komponenta.
    /// </summary>
    /// <remarks>
    /// Jména tabulek se nerozebírají na schéma a jméno, ale hledají se v načteném
    /// schématu. Tečka totiž může být i uvnitř jména a rozdělení podle ní by z tabulky
    /// <c>Order.Items</c> udělalo schéma <c>Order</c>. Co ve schématu není, se zahodí —
    /// adresa může ukazovat na tabulku, která mezitím zmizela.
    /// </remarks>
    public void ApplyTo(ViewerState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        state.Pane = Pane;
        state.SelectedTable = Najdi(state, Table);
        state.Tab = Tab;
        state.Group = Group;
        state.SchemaName = Schema;
        state.Search = Search;
        state.FocusEnabled = Focus;
        state.FocusHops = Hops;

        state.ExpandedNodes.Clear();

        foreach (var jmeno in Expanded)
        {
            if (Najdi(state, jmeno) is { } nalezene)
            {
                state.ExpandedNodes.Add(nalezene);
            }
        }
    }

    /// <summary>
    /// Dvojice do query stringu. Výchozí hodnoty se vynechávají, aby adresa nedotčené
    /// prohlížečky zůstala krátká a šla přečíst okem.
    /// </summary>
    private List<KeyValuePair<string, string>> Parametry()
    {
        var dvojice = new List<KeyValuePair<string, string>>();

        void Pridej(string jmeno, string? hodnota)
        {
            if (hodnota is { Length: > 0 })
            {
                dvojice.Add(new KeyValuePair<string, string>(jmeno, hodnota));
            }
        }

        if (Pane != ViewerPane.Browser)
        {
            Pridej("pane", Pane.ToString().ToLowerInvariant());
        }

        Pridej("table", Table?.Qualified);

        if (Tab != DetailTab.Columns)
        {
            Pridej("tab", Tab.ToString().ToLowerInvariant());
        }

        Pridej("version", Version);
        Pridej("base", Baseline);

        // Sloučený pohled se nezapisuje: bez parametru si zdroj vybere server podle
        // toho, co má nakonfigurované, a to je správnější než hodnota z adresy.
        if (!string.Equals(Source, VychoziZdroj, StringComparison.OrdinalIgnoreCase))
        {
            Pridej("source", Source);
        }

        Pridej("group", Group);
        Pridej("schema", Schema);
        Pridej("q", Search);

        if (!Focus)
        {
            Pridej("focus", "0");
        }

        if (Hops != 1)
        {
            Pridej("hops", Hops.ToString(CultureInfo.InvariantCulture));
        }

        if (Expanded.Count > 0)
        {
            Pridej("expand", string.Join(",", Expanded.Select(static n => n.Qualified)));
        }

        return dvojice;
    }

    /// <summary>Jméno tabulky tak, jak stojí ve schématu, nebo <c>null</c>, když tam není.</summary>
    private static DbObjectName? Najdi(ViewerState state, DbObjectName? jmeno)
    {
        if (jmeno is not { } hledane)
        {
            return null;
        }

        foreach (var tabulka in state.DisplaySchema.Tables)
        {
            if (tabulka.Name.Qualified.Equals(hledane.Qualified, StringComparison.OrdinalIgnoreCase))
            {
                return tabulka.Name;
            }
        }

        return null;
    }

    /// <summary>
    /// Jméno z adresy. Schéma se odděluje po první tečce; skutečné jméno pak dohledá
    /// <see cref="Najdi"/> ve schématu, takže na tomhle rozdělení nakonec nesejde.
    /// </summary>
    private static DbObjectName? JmenoZKodu(string? hodnota)
    {
        if (hodnota is not { Length: > 0 })
        {
            return null;
        }

        var tecka = hodnota.IndexOf('.', StringComparison.Ordinal);

        return tecka > 0 && tecka < hodnota.Length - 1
            ? new DbObjectName(hodnota[..tecka], hodnota[(tecka + 1)..])
            : new DbObjectName(null, hodnota);
    }

    /// <summary>Seznam jmen oddělených čárkou. Prázdné položky se zahazují.</summary>
    private static IReadOnlyList<DbObjectName> SeznamJmen(string? hodnota) =>
        hodnota is { Length: > 0 }
            ? [.. hodnota.Split(',').Select(JmenoZKodu).OfType<DbObjectName>()]
            : [];

    private static ViewerPane PanelZKodu(string? hodnota) => hodnota?.ToLowerInvariant() switch
    {
        "overview" => ViewerPane.Overview,
        "diagram" => ViewerPane.Diagram,
        "diff" => ViewerPane.Diff,
        "history" => ViewerPane.History,
        _ => ViewerPane.Browser,
    };

    private static DetailTab ZalozkaZKodu(string? hodnota) => hodnota?.ToLowerInvariant() switch
    {
        "indexes" => DetailTab.Indexes,
        "foreignkeys" => DetailTab.ForeignKeys,
        "referencedby" => DetailTab.ReferencedBy,
        "data" => DetailTab.Data,
        _ => DetailTab.Columns,
    };
}
