using DbsViewer.Analysis;

namespace DbsViewer.Ui.Components;

/// <summary>Záložka detailu tabulky.</summary>
public enum DetailTab
{
    Columns,
    Indexes,
    ForeignKeys,
    ReferencedBy,
    Data,
}

/// <summary>Hlavní pohled aplikace.</summary>
public enum ViewerPane
{
    /// <summary>Souhrn celé databáze.</summary>
    Overview,

    /// <summary>Seznam tabulek a detail.</summary>
    Browser,

    /// <summary>ER diagram.</summary>
    Diagram,

    /// <summary>Nálezy porovnání modelu s databází.</summary>
    Diff,

    /// <summary>Historie schématu podle migrací.</summary>
    History,
}

/// <summary>
/// Stav prohlížečky vytažený z komponenty ven, aby se dal testovat bez vykreslování.
/// Komponenta na něj jen sahá a překresluje se.
/// </summary>
public sealed class ViewerState
{
    private DatabaseSchema _schema = new();

    /// <summary>Načtené schéma. Změna přepočítá odvozený graf.</summary>
    public DatabaseSchema Schema
    {
        get => _schema;
        set
        {
            _schema = value ?? new DatabaseSchema();
            PrepocitejOdvozene();
            ZachovejKontext();
        }
    }

    /// <summary>
    /// Ponechá vybranou tabulku i rozbalené uzly, pokud v novém schématu existují.
    /// </summary>
    /// <remarks>
    /// Zásadní pro porovnávání verzí okem: přepnu se na starší migraci a chci vidět
    /// **tutéž** tabulku se stejně rozbalenými sloupci, abych poznal, co přibylo nebo
    /// zmizelo. Kdyby se výběr zahodil, musel bych ho po každém přepnutí hledat znovu
    /// a rozdíl by se ztratil.
    ///
    /// Co v novém schématu není, se zahodí — jinak by detail ukazoval tabulku,
    /// která tehdy neexistovala.
    /// </remarks>
    private void ZachovejKontext()
    {
        var znameTabulky = new HashSet<DbObjectName>(
            Overlay.Schema.Tables.Select(static t => t.Name));

        if (SelectedTable is { } vybrana && !znameTabulky.Contains(vybrana))
        {
            SelectedTable = null;
        }

        ExpandedNodes.RemoveWhere(n => !znameTabulky.Contains(n));
    }

    public Model.SchemaGraph Graph { get; private set; } = new(new DatabaseSchema());

    /// <summary>Souhrn schématu pro úvodní přehled. Přepočítá se s načtením schématu.</summary>
    public Model.SchemaSummary Summary { get; private set; } = Model.SchemaSummary.From(new DatabaseSchema());

    private DatabaseSchema? _baseline;

    /// <summary>
    /// Verze, vůči které se schéma vizuálně porovnává, nebo <c>null</c>, když se
    /// neporovnává nic.
    /// </summary>
    public DatabaseSchema? Baseline
    {
        get => _baseline;
        set
        {
            _baseline = value;
            PrepocitejOdvozene();
            ZachovejKontext();
        }
    }

    /// <summary>Migrace zvolená jako základ porovnání.</summary>
    public string? BaselineMigration { get; set; }

    /// <summary>
    /// Vrstva změn nad schématem. Bez porovnání označuje všechno jako beze změny.
    /// </summary>
    public Model.SchemaOverlay Overlay { get; private set; } =
        Model.SchemaOverlay.None(new DatabaseSchema());

    /// <summary>
    /// Schéma k zobrazení. Při porovnávání obsahuje i objekty, které zmizely —
    /// jinak by nešlo ukázat, co ubylo.
    /// </summary>
    public DatabaseSchema DisplaySchema => Overlay.Schema;

    /// <summary>Porovnává se schéma vůči jiné verzi?</summary>
    public bool JeVizualniPorovnani => _baseline is not null;

    /// <summary>
    /// Přepočítá vše, co ze schématu vychází. Graf i souhrn se počítají ze zobrazeného
    /// schématu, aby seznam tabulek i diagram viděly i duchy.
    /// </summary>
    private void PrepocitejOdvozene()
    {
        Overlay = _baseline is { } baseline
            ? Model.SchemaOverlay.Build(baseline, _schema)
            : Model.SchemaOverlay.None(_schema);

        Graph = new Model.SchemaGraph(Overlay.Schema);
        Summary = Model.SchemaSummary.From(Overlay.Schema);
    }

    public Model.ViewerMeta Meta { get; set; } = new();

    public SchemaDiff? Diff { get; set; }

    public ViewerPane Pane { get; set; } = ViewerPane.Browser;

    /// <summary>Porovnávaná starší verze v pohledu historie.</summary>
    public string? CompareFrom { get; set; }

    /// <summary>Porovnávaná novější verze v pohledu historie.</summary>
    public string? CompareTo { get; set; }

    public DetailTab Tab { get; set; } = DetailTab.Columns;

    /// <summary>Vybraný pohled na schéma: <c>ef</c>, <c>live</c> nebo <c>merged</c>.</summary>
    public string Source { get; set; } = "merged";

    /// <summary>
    /// Migrace, ke které se schéma zobrazuje, nebo <c>null</c> pro aktuální stav.
    /// </summary>
    public string? SelectedMigration { get; set; }

    /// <summary>Seznam migrací i s tím, co která změnila.</summary>
    public IReadOnlyList<DbMigration> Migrations { get; set; } = [];

    /// <summary>
    /// Kouká se do historie, ne na aktuální schéma?
    /// </summary>
    /// <remarks>
    /// Rozhoduje o tom, co v UI zmizí: data z historické verze číst nejde, protože
    /// snapshot popisuje strukturu v minulosti, kdežto řádky existují jen tady a teď.
    /// </remarks>
    public bool IsHistorical => SelectedMigration is { Length: > 0 };

    public string Search { get; set; } = "";

    /// <summary>Vybraná skupina tabulek z konfigurace, nebo <c>null</c> pro všechny.</summary>
    public string? Group { get; set; }

    /// <summary>Vybrané schéma pro filtr, nebo <c>null</c> pro všechna.</summary>
    public string? SchemaName { get; set; }

    public DbObjectName? SelectedTable { get; set; }

    /// <summary>Vzdálenost sousedů v diagramu. Nula zobrazí jen vybranou tabulku.</summary>
    public int FocusHops
    {
        get;
        set => field = Math.Clamp(value, 0, 3);
    } = 1;

    /// <summary>Focus mode je zapnutý — diagram kreslí jen okolí vybrané tabulky.</summary>
    public bool FocusEnabled { get; set; } = true;

    /// <summary>Uzly zobrazené se všemi sloupci.</summary>
    public HashSet<DbObjectName> ExpandedNodes { get; } = [];

    public bool IsLoading { get; set; }

    /// <summary>Chybová zpráva k zobrazení, nebo <c>null</c>.</summary>
    public string? Error { get; set; }

    /// <summary>Tabulky po použití všech filtrů.</summary>
    public IReadOnlyList<DbTable> FilteredTables()
    {
        var tables = DisplaySchema.Tables;

        if (SchemaName is { } schemaName)
        {
            tables = [.. tables.Where(t =>
                string.Equals(t.Name.Schema, schemaName, StringComparison.OrdinalIgnoreCase))];
        }

        if (Group is { } group && Meta.Groups.TryGetValue(group, out var pattern))
        {
            tables = Model.SchemaFilter.InGroup(tables, pattern);
        }

        return Model.SchemaFilter.Search(tables, Search);
    }

    /// <summary>Tabulky vykreslené v diagramu podle nastavení focus modu.</summary>
    public IReadOnlyList<DbTable> DiagramTables()
    {
        var filtered = FilteredTables();

        if (!FocusEnabled || SelectedTable is not { } selected)
        {
            return filtered;
        }

        var focused = Graph.Focus(selected, FocusHops);
        var allowed = new HashSet<DbObjectName>(filtered.Select(static t => t.Name));

        // Vybraná tabulka zůstane vidět, i kdyby ji filtr vyřadil — jinak by focus
        // ukazoval prázdno a nebylo by jasné proč.
        allowed.Add(selected);

        return [.. DisplaySchema.Tables.Where(t => focused.Contains(t.Name) && allowed.Contains(t.Name))];
    }

    /// <summary>Vazby vykreslené v diagramu.</summary>
    public IReadOnlyList<DbRelationship> DiagramRelationships()
    {
        var visible = new HashSet<DbObjectName>(DiagramTables().Select(static t => t.Name));

        return Graph.RelationshipsIn(visible);
    }

    /// <summary>Detail vybrané tabulky, nebo <c>null</c>.</summary>
    public DbTable? SelectedDetail() =>
        SelectedTable is { } name ? DisplaySchema.FindTable(name) : null;

    /// <summary>Přepne rozbalení uzlu v diagramu.</summary>
    public void ToggleExpanded(DbObjectName table)
    {
        if (!ExpandedNodes.Add(table))
        {
            ExpandedNodes.Remove(table);
        }
    }

    /// <summary>Vybere tabulku a přepne detail na první záložku.</summary>
    public void Select(DbObjectName? table)
    {
        SelectedTable = table;
        Tab = DetailTab.Columns;
    }

    /// <summary>Nálezy diffu pro tabulku, když je diff načtený.</summary>
    public IReadOnlyList<DiffFinding> FindingsFor(DbObjectName table) =>
        Diff?.ForTable(table) ?? [];

    /// <summary>Nejvyšší závažnost nálezu u tabulky — pro barevné zvýraznění.</summary>
    public DiffSeverity? SeverityOf(DbObjectName table) => Diff?.SeverityOf(table);
}
