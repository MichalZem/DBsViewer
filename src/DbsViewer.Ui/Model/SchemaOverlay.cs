namespace DbsViewer.Ui.Model;

/// <summary>Jak se objekt změnil oproti porovnávané verzi.</summary>
public enum ZmenaStav
{
    /// <summary>Beze změny.</summary>
    Beze,

    /// <summary>Objekt oproti porovnávané verzi přibyl.</summary>
    Pribylo,

    /// <summary>Objekt v porovnávané verzi byl, teď už není. Kreslí se jako duch.</summary>
    Ubylo,

    /// <summary>Objekt zůstal, ale změnil se — typ sloupce, unikátnost indexu a podobně.</summary>
    Zmeneno,
}

/// <summary>
/// Vrstva změn nad schématem: ke každé tabulce, sloupci, vazbě a indexu říká,
/// jak se liší od porovnávané verze.
/// </summary>
/// <remarks>
/// Nejde o <see cref="Analysis.SchemaDiff"/>, ten dává seznam nálezů ke čtení. Tohle
/// je podklad pro vykreslení: schéma se zobrazuje normálně a překryv jen obarvuje,
/// co se změnilo.
///
/// Součástí je i **sloučené schéma**, do kterého se přimíchají objekty, jež ve zobrazené
/// verzi už nejsou. Bez nich by nešlo ukázat, co ubylo — a právě to člověk při
/// porovnávání verzí hledá nejčastěji.
///
/// Počítá se v prohlížečce ze dvou načtených schémat, takže server nepotřebuje nic navíc.
/// </remarks>
public sealed class SchemaOverlay
{
    private readonly Dictionary<DbObjectName, ZmenaStav> _tabulky = [];
    private readonly Dictionary<(DbObjectName Tabulka, string Sloupec), ZmenaStav> _sloupce = new();
    private readonly Dictionary<(DbObjectName Tabulka, string Index), ZmenaStav> _indexy = new();
    private readonly Dictionary<string, ZmenaStav> _vazby = new(StringComparer.Ordinal);

    private SchemaOverlay(DatabaseSchema merged)
    {
        Schema = merged;
    }

    /// <summary>
    /// Schéma k vykreslení: zobrazená verze plus objekty, které v ní už nejsou.
    /// </summary>
    public DatabaseSchema Schema { get; private set; }

    /// <summary>Prázdný překryv — nic se neporovnává.</summary>
    public static SchemaOverlay None(DatabaseSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        return new SchemaOverlay(schema);
    }

    /// <summary>Kolik objektů se oproti porovnávané verzi liší.</summary>
    public int PocetZmen =>
        _tabulky.Count(static z => z.Value != ZmenaStav.Beze)
        + _sloupce.Count(static z => z.Value != ZmenaStav.Beze)
        + _indexy.Count(static z => z.Value != ZmenaStav.Beze)
        + _vazby.Count(static z => z.Value != ZmenaStav.Beze);

    /// <summary>Porovnává se vůbec něco?</summary>
    public bool JeAktivni => _tabulky.Count > 0 || _sloupce.Count > 0;

    /// <summary>
    /// Sestaví překryv zobrazené verze proti starší.
    /// </summary>
    /// <param name="baseline">Verze, vůči které se porovnává — obvykle ta starší.</param>
    /// <param name="current">Zobrazená verze.</param>
    public static SchemaOverlay Build(DatabaseSchema baseline, DatabaseSchema current)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);

        var stareTabulky = baseline.Tables.ToDictionary(static t => t.Name);
        var noveTabulky = current.Tables.ToDictionary(static t => t.Name);

        var tabulky = new List<DbTable>(current.Tables.Count);
        var overlay = new SchemaOverlay(current with { Tables = tabulky });

        foreach (var tabulka in current.Tables)
        {
            if (stareTabulky.TryGetValue(tabulka.Name, out var stara))
            {
                tabulky.Add(overlay.SlucTabulku(stara, tabulka));
            }
            else
            {
                overlay.OznacCelou(tabulka, ZmenaStav.Pribylo);
                tabulky.Add(tabulka);
            }
        }

        // Tabulky, které zmizely, se přidají jako duchové — jinak by nebylo vidět,
        // co ubylo.
        foreach (var zmizela in baseline.Tables.Where(t => !noveTabulky.ContainsKey(t.Name)))
        {
            overlay.OznacCelou(zmizela, ZmenaStav.Ubylo);
            tabulky.Add(zmizela);
        }

        overlay.PorovnejVazby(baseline, current, tabulky);

        return overlay;
    }

    /// <summary>Stav tabulky.</summary>
    public ZmenaStav Tabulka(DbObjectName name) =>
        _tabulky.GetValueOrDefault(name, ZmenaStav.Beze);

    /// <summary>Stav sloupce.</summary>
    public ZmenaStav Sloupec(DbObjectName tabulka, string sloupec) =>
        _sloupce.GetValueOrDefault((tabulka, sloupec), ZmenaStav.Beze);

    /// <summary>Stav indexu.</summary>
    public ZmenaStav Index(DbObjectName tabulka, string index) =>
        _indexy.GetValueOrDefault((tabulka, index), ZmenaStav.Beze);

    /// <summary>Stav vazby.</summary>
    public ZmenaStav Vazba(DbRelationship relationship)
    {
        ArgumentNullException.ThrowIfNull(relationship);

        return _vazby.GetValueOrDefault(KlicVazby(relationship), ZmenaStav.Beze);
    }

    /// <summary>
    /// Sloučí tabulku ze dvou verzí: sloupce a indexy, které zmizely, zůstanou
    /// v seznamu jako duchové.
    /// </summary>
    private DbTable SlucTabulku(DbTable stara, DbTable nova)
    {
        var stareSloupce = stara.Columns.ToDictionary(
            static c => c.Name, StringComparer.OrdinalIgnoreCase);

        var noveJmena = new HashSet<string>(
            nova.Columns.Select(static c => c.Name), StringComparer.OrdinalIgnoreCase);

        var sloupce = new List<DbColumn>(nova.Columns.Count);
        var zmeneno = false;

        foreach (var sloupec in nova.Columns)
        {
            sloupce.Add(sloupec);

            if (!stareSloupce.TryGetValue(sloupec.Name, out var stary))
            {
                _sloupce[(nova.Name, sloupec.Name)] = ZmenaStav.Pribylo;
                zmeneno = true;
                continue;
            }

            if (SeLisi(stary, sloupec))
            {
                _sloupce[(nova.Name, sloupec.Name)] = ZmenaStav.Zmeneno;
                zmeneno = true;
            }
        }

        foreach (var zmizel in stara.Columns.Where(c => !noveJmena.Contains(c.Name)))
        {
            _sloupce[(nova.Name, zmizel.Name)] = ZmenaStav.Ubylo;
            sloupce.Add(zmizel);
            zmeneno = true;
        }

        var indexy = SlucIndexy(stara, nova, ref zmeneno);

        _tabulky[nova.Name] = zmeneno ? ZmenaStav.Zmeneno : ZmenaStav.Beze;

        return nova with { Columns = sloupce, Indexes = indexy };
    }

    private List<DbIndex> SlucIndexy(DbTable stara, DbTable nova, ref bool zmeneno)
    {
        var stareIndexy = stara.Indexes.ToDictionary(
            static i => i.Name, StringComparer.OrdinalIgnoreCase);

        var noveJmena = new HashSet<string>(
            nova.Indexes.Select(static i => i.Name), StringComparer.OrdinalIgnoreCase);

        var indexy = new List<DbIndex>(nova.Indexes.Count);

        foreach (var index in nova.Indexes)
        {
            indexy.Add(index);

            if (!stareIndexy.TryGetValue(index.Name, out var stary))
            {
                _indexy[(nova.Name, index.Name)] = ZmenaStav.Pribylo;
                zmeneno = true;
                continue;
            }

            if (stary.IsUnique != index.IsUnique
                || !stary.Columns.SequenceEqual(index.Columns, StringComparer.OrdinalIgnoreCase))
            {
                _indexy[(nova.Name, index.Name)] = ZmenaStav.Zmeneno;
                zmeneno = true;
            }
        }

        foreach (var zmizel in stara.Indexes.Where(i => !noveJmena.Contains(i.Name)))
        {
            _indexy[(nova.Name, zmizel.Name)] = ZmenaStav.Ubylo;
            indexy.Add(zmizel);
            zmeneno = true;
        }

        return indexy;
    }

    /// <summary>Označí celou tabulku i její obsah jedním stavem — přibyla, nebo zmizela.</summary>
    private void OznacCelou(DbTable table, ZmenaStav stav)
    {
        _tabulky[table.Name] = stav;

        foreach (var sloupec in table.Columns)
        {
            _sloupce[(table.Name, sloupec.Name)] = stav;
        }

        foreach (var index in table.Indexes)
        {
            _indexy[(table.Name, index.Name)] = stav;
        }
    }

    /// <summary>
    /// Porovná vazby a zmizelé přidá do schématu, aby v diagramu zůstala čára
    /// k tabulce, která už neexistuje.
    /// </summary>
    private void PorovnejVazby(DatabaseSchema baseline, DatabaseSchema current, List<DbTable> tabulky)
    {
        var stare = baseline.Relationships.ToDictionary(KlicVazby, StringComparer.Ordinal);
        var nove = new HashSet<string>(current.Relationships.Select(KlicVazby), StringComparer.Ordinal);
        var vazby = new List<DbRelationship>(current.Relationships);

        foreach (var vazba in current.Relationships)
        {
            var klic = KlicVazby(vazba);

            _vazby[klic] = stare.TryGetValue(klic, out var stara)
                ? stara.DeleteBehavior == vazba.DeleteBehavior && stara.IsRequired == vazba.IsRequired
                    ? ZmenaStav.Beze
                    : ZmenaStav.Zmeneno
                : ZmenaStav.Pribylo;
        }

        foreach (var zmizela in baseline.Relationships.Where(r => !nove.Contains(KlicVazby(r))))
        {
            // Vazba na tabulku, která ve sloučeném schématu není, by v diagramu
            // vedla nikam — kreslí se jen ty, jejichž oba konce existují.
            if (!tabulky.Exists(t => t.Name == zmizela.From) || !tabulky.Exists(t => t.Name == zmizela.To))
            {
                continue;
            }

            _vazby[KlicVazby(zmizela)] = ZmenaStav.Ubylo;
            vazby.Add(zmizela);
        }

        Schema = Schema with { Relationships = vazby };
    }

    /// <summary>
    /// Klíč vazby. Páruje se podle struktury, ne podle jména — jména cizích klíčů
    /// se mezi verzemi i zdroji liší (viz ADR-0011).
    /// </summary>
    private static string KlicVazby(DbRelationship r) =>
        $"{r.From.Qualified}|{r.To.Qualified}|{string.Join(",", r.FromColumns)}";

    /// <summary>Liší se sloupec natolik, že to stojí za zvýraznění?</summary>
    private static bool SeLisi(DbColumn stary, DbColumn novy) =>
        !string.Equals(stary.StoreType, novy.StoreType, StringComparison.OrdinalIgnoreCase)
        || stary.IsNullable != novy.IsNullable
        || stary.IsPrimaryKey != novy.IsPrimaryKey
        || !string.Equals(stary.DefaultValueSql, novy.DefaultValueSql, StringComparison.Ordinal);
}
