using DbsViewer.Analysis;

namespace DbsViewer.Server;

/// <summary>Prostředí, ve kterých smí být prohlížečka dostupná.</summary>
[Flags]
public enum HostEnv
{
    /// <summary>Nikde. Komponenta se nezaregistruje.</summary>
    None = 0,

    Development = 1,

    Staging = 2,

    Production = 4,

    /// <summary>Všude. Vyžaduje autorizační policy, jinak aplikace nenastartuje.</summary>
    All = Development | Staging | Production,
}

/// <summary>
/// Nastavení prohlížečky. Výchozí hodnoty jsou schválně restriktivní —
/// viz <see href="../../docs/adr/0006-bezpecnostni-defaulty.md">ADR-0006</see>.
/// </summary>
public sealed class DbsViewerOptions
{
    /// <summary>Cesta, na které prohlížečka běží. Vždy začíná lomítkem.</summary>
    public string RoutePrefix
    {
        get;
        set => field = Normalize(value);
    } = "/dbschema";

    /// <summary>Nadpis v UI. Bez zadání se použije jméno databáze.</summary>
    public string? Title { get; set; }

    /// <summary>
    /// Prostředí, ve kterých je prohlížečka dostupná. Mimo Development je nutná
    /// autorizační policy — jinak <c>MapDbsViewer()</c> vyhodí výjimku při startu.
    /// </summary>
    public HostEnv EnabledIn { get; set; } = HostEnv.Development;

    /// <summary>Autorizační policy, kterou musí volající splnit. <c>null</c> znamená bez autorizace.</summary>
    public string? AuthorizationPolicy { get; private set; }

    /// <summary>Číst i živou databázi, ne jen EF model. Zapíná diff a sloučený pohled.</summary>
    public bool IncludeLiveDatabase { get; set; } = true;

    /// <summary>Zjišťovat odhad počtu řádků.</summary>
    public bool ShowRowCounts { get; set; }

    /// <summary>Jak dlouho se schéma drží v cache. <see cref="TimeSpan.Zero"/> cache vypne.</summary>
    public TimeSpan CacheFor { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Tabulky, které se nezobrazí. Podporuje zástupný znak <c>*</c>.</summary>
    public IList<string> HideTables { get; } = new List<string>();

    /// <summary>Když je neprázdné, zobrazí se jen tabulky z uvedených schémat.</summary>
    public IList<string> IncludeSchemas { get; } = new List<string>();

    /// <summary>
    /// Pojmenované skupiny tabulek pro filtr v UI. Klíč je název skupiny,
    /// hodnota vzor se zástupným znakem <c>*</c>.
    /// </summary>
    public IDictionary<string, string> Groups { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Co se má hlásit při porovnání modelu s databází.</summary>
    public DiffOptions Diff { get; set; } = DiffOptions.Default;

    /// <summary>Nastavení náhledu dat. Ve výchozím stavu vypnuté.</summary>
    public DataPreviewOptions DataPreview { get; } = new();

    /// <summary>
    /// Sestava se soubory UI. Ve výchozím stavu je to <c>DbsViewer.Server</c> se
    /// zabudovaným Blazor UI; jde ji vyměnit za vlastní, když chceš prohlížečku
    /// s jiným frontendem.
    /// </summary>
    public System.Reflection.Assembly? UiAssembly { get; set; }

    /// <summary>
    /// Vyžaduje splnění autorizační policy. Bez ní je prohlížečka dostupná jen
    /// v Development a jinde odmítne nastartovat.
    /// </summary>
    public DbsViewerOptions RequireAuthorization(string policyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);
        AuthorizationPolicy = policyName;
        return this;
    }

    /// <summary>Převede nastavení na volby čtení schématu.</summary>
    public SchemaReadOptions ToReadOptions() => new()
    {
        IncludeRowCounts = ShowRowCounts,
        IncludeMigrations = true,
        HideTables = [.. HideTables],
        IncludeSchemas = [.. IncludeSchemas],
    };

    /// <summary>Je prohlížečka v daném prostředí povolená?</summary>
    public bool IsEnabledIn(string environmentName) =>
        EnabledIn.HasFlag(MapEnvironment(environmentName));

    /// <summary>
    /// Přiřadí jméno prostředí příznaku. Neznámé prostředí se považuje za produkční —
    /// bezpečnější je odmítnout než omylem zpřístupnit.
    /// </summary>
    public static HostEnv MapEnvironment(string environmentName) => environmentName switch
    {
        null => HostEnv.Production,
        var name when name.Equals("Development", StringComparison.OrdinalIgnoreCase) => HostEnv.Development,
        var name when name.Equals("Staging", StringComparison.OrdinalIgnoreCase) => HostEnv.Staging,
        _ => HostEnv.Production,
    };

    /// <summary>Cesta se normalizuje na tvar <c>/prefix</c> bez koncového lomítka.</summary>
    private static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var trimmed = value.Trim().TrimEnd('/');
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Prefix cesty nesmí být jen lomítko.", nameof(value));
        }

        return trimmed.StartsWith('/') ? trimmed : '/' + trimmed;
    }
}

/// <summary>
/// Náhled dat tabulky. Vypnutý je nejen ve výchozím stavu — je to samostatné rozhodnutí
/// nezávislé na zpřístupnění schématu, protože zpřístupňuje obsah, ne strukturu.
/// </summary>
public sealed class DataPreviewOptions
{
    /// <summary>Povolit náhled dat. Vyžaduje vědomé zapnutí.</summary>
    public bool Enabled { get; set; }

    /// <summary>Maximální počet vrácených řádků. Tvrdý strop je 1000.</summary>
    public int MaxRows
    {
        get;
        set => field = Math.Clamp(value, 1, HardRowLimit);
    } = 100;

    /// <summary>
    /// Sloupce, jejichž hodnoty se nahradí hvězdičkami. Podporuje zástupný znak <c>*</c>,
    /// takže <c>*Password*</c> zamaskuje všechny sloupce s tím slovem v názvu.
    /// </summary>
    public IList<string> MaskColumns { get; } = new List<string> { "*Password*", "*Secret*", "*Token*" };

    /// <summary>
    /// Když je neprázdné, náhled je povolený jen pro uvedené tabulky.
    /// Podporuje zástupný znak <c>*</c>.
    /// </summary>
    public IList<string> AllowedTables { get; } = new List<string>();

    /// <summary>
    /// Vypne pojistku, která brání zapnout náhled dat v produkci. Vyžaduje vědomé
    /// rozhodnutí — bez ní aplikace v produkci se zapnutým náhledem nenastartuje.
    /// </summary>
    public bool AllowInProduction { get; set; }

    /// <summary>Tvrdý strop počtu řádků. Nedá se přenastavit.</summary>
    public const int HardRowLimit = 1000;

    /// <summary>
    /// Časový limit jednoho dotazu v sekundách.
    /// </summary>
    /// <remarks>
    /// Chrání hlavně <c>COUNT(*)</c>: nad velkou tabulkou s filtrem, který se nedá pokrýt
    /// indexem, běží klidně desítky sekund a držel by přitom připojení. Když nedoběhne,
    /// stránka se zobrazí i tak — jen bez celkového počtu.
    /// </remarks>
    public int CommandTimeoutSeconds
    {
        get;
        set => field = Math.Clamp(value, 1, 300);
    } = 30;

    /// <summary>Smí se z této tabulky číst?</summary>
    public bool IsAllowed(DbObjectName table)
    {
        if (!Enabled)
        {
            return false;
        }

        if (AllowedTables.Count == 0)
        {
            return true;
        }

        foreach (var pattern in AllowedTables)
        {
            if (GlobPattern.IsMatch(table.Name, pattern) || GlobPattern.IsMatch(table.Qualified, pattern))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Má se hodnota sloupce maskovat?</summary>
    public bool IsMasked(string columnName)
    {
        foreach (var pattern in MaskColumns)
        {
            if (GlobPattern.IsMatch(columnName, pattern))
            {
                return true;
            }
        }

        return false;
    }
}
