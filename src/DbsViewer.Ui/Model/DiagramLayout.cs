namespace DbsViewer.Ui.Model;

/// <summary>Umístěný uzel diagramu.</summary>
public sealed record DiagramNode
{
    public required DbTable Table { get; init; }

    public double X { get; init; }

    public double Y { get; init; }

    public double Width { get; init; }

    public double Height { get; init; }

    public double CenterX => X + (Width / 2);

    public double CenterY => Y + (Height / 2);

    /// <summary>Pořadí vrstvy, ve které uzel leží. Nula je kořenová.</summary>
    public int Layer { get; init; }
}

/// <summary>Hrana diagramu s vypočtenou trasou.</summary>
public sealed record DiagramEdge
{
    public required DbRelationship Relationship { get; init; }

    /// <summary>Body lomené čáry, včetně krajních.</summary>
    public required IReadOnlyList<(double X, double Y)> Points { get; init; }

    /// <summary>Bod, kam se umístí popisek kardinality.</summary>
    public (double X, double Y) LabelAt { get; init; }

    /// <summary>Hrana z tabulky do sebe sama — kreslí se jako smyčka.</summary>
    public bool IsSelfLoop { get; init; }
}

/// <summary>Hotové rozložení diagramu.</summary>
public sealed record DiagramLayoutResult
{
    public required IReadOnlyList<DiagramNode> Nodes { get; init; }

    public required IReadOnlyList<DiagramEdge> Edges { get; init; }

    public double Width { get; init; }

    public double Height { get; init; }

    public DiagramNode? Find(DbObjectName table) =>
        Nodes.FirstOrDefault(n => n.Table.Name == table);
}

/// <summary>
/// Rozmístění tabulek do vrstev podle vazeb.
/// </summary>
/// <remarks>
/// Vrstvený layout je pro ER diagram přirozený: tabulky, na které se odkazuje, jdou
/// doleva, závislé doprava. Algoritmus je vlastní a v C#, aby diagram fungoval i bez
/// JavaScriptu; kvalitnější rozvržení přes elkjs se dá doplnit později, aniž by se měnil
/// tvar výsledku.
///
/// Samotné vrstvení ale dá sloupců jen tolik, jak dlouhý je nejdelší řetěz cizích klíčů.
/// U velkého schématu s mělkými vazbami z toho vznikne pruh tří sloupců a desítek řádků,
/// který se nedá přehlédnout. Rozměry proto srovnávají tři kroky navíc — nesouvislé části
/// schématu vedle sebe, zalomení přeplněné vrstvy a mřížka tabulek bez vazeb.
/// </remarks>
public static class DiagramLayout
{
    /// <summary>Šířka uzlu ve sbaleném stavu.</summary>
    public const double CollapsedWidth = 200;

    /// <summary>Výška hlavičky uzlu.</summary>
    public const double HeaderHeight = 34;

    /// <summary>Výška jednoho řádku se sloupcem.</summary>
    public const double RowHeight = 20;

    /// <summary>Vodorovná mezera mezi vrstvami.</summary>
    public const double LayerGap = 120;

    /// <summary>Svislá mezera mezi uzly ve vrstvě.</summary>
    public const double NodeGap = 36;

    /// <summary>Vodorovná mezera mezi podsloupci téže vrstvy.</summary>
    public const double ColumnGap = 56;

    /// <summary>Mezera mezi bloky, na které se schéma rozpadlo.</summary>
    public const double BlockGap = 90;

    /// <summary>
    /// Cílový poměr stran plochy, šířka ku výšce. Diagram se čte na obrazovce, takže
    /// mírně na šířku — ne čtverec a hlavně ne pruh.
    /// </summary>
    private const double TargetAspect = 1.6;

    private const double Margin = 40;

    /// <summary>Volný pruh u horního a dolního okraje uzlu, kam kotvy nesahají.</summary>
    private const double AnchorInset = 10;

    /// <summary>
    /// Nejmenší svislý rozestup dvou kotev. Musí se mezi ně vejít popisek kardinality,
    /// jinak se u tabulky s několika vazbami popisky slijí.
    /// </summary>
    private const double MinAnchorGap = 24;

    /// <summary>Spočítá rozložení pro zadané tabulky a vazby.</summary>
    /// <param name="tables">Tabulky k vykreslení.</param>
    /// <param name="relationships">Vazby mezi nimi.</param>
    /// <param name="expanded">Tabulky zobrazené se všemi sloupci; ostatní jen s klíči.</param>
    public static DiagramLayoutResult Compute(
        IReadOnlyList<DbTable> tables,
        IReadOnlyList<DbRelationship> relationships,
        IReadOnlySet<DbObjectName>? expanded = null)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(relationships);

        if (tables.Count == 0)
        {
            return new DiagramLayoutResult { Nodes = [], Edges = [], Width = 0, Height = 0 };
        }

        var layers = AssignLayers(tables, relationships);
        var kotvy = AnchorCounts(tables, relationships, layers);

        // Výšky se počítají dřív než pozice: odvozuje se z nich cílová plocha i to,
        // kdy je vrstva tak vysoká, že se musí zalomit.
        var vysky = tables.ToDictionary(
            static t => t.Name,
            t => NodeHeight(
                t,
                expanded?.Contains(t.Name) ?? false,
                kotvy.GetValueOrDefault(t.Name)));

        var nodes = PlaceNodes(tables, layers, relationships, vysky);
        var edges = RouteEdges(nodes, relationships);

        var width = nodes.Max(static n => n.X + n.Width) + Margin;
        var height = nodes.Max(static n => n.Y + n.Height) + Margin;

        return new DiagramLayoutResult
        {
            Nodes = nodes,
            Edges = edges,
            Width = width,
            Height = height,
        };
    }

    /// <summary>
    /// Přiřadí tabulky do vrstev. Tabulka je o vrstvu vpravo od všech, na které odkazuje,
    /// takže vazby přirozeně směřují doleva.
    /// </summary>
    /// <remarks>
    /// Cyklické vazby jsou v databázích běžné — zaměstnanec má oddělení a oddělení má
    /// vedoucího. Kdyby se braly v úvahu všechny, vrstvy by se u cyklu donekonečna
    /// posouvaly a diagram by zbytněl do šířky. Zpětné hrany se proto před výpočtem
    /// odstraní; v diagramu se stejně vykreslí, jen neurčují pořadí vrstev.
    /// </remarks>
    internal static Dictionary<DbObjectName, int> AssignLayers(
        IReadOnlyList<DbTable> tables,
        IReadOnlyList<DbRelationship> relationships)
    {
        var layers = new Dictionary<DbObjectName, int>();
        var dependencies = new Dictionary<DbObjectName, List<DbObjectName>>();

        foreach (var table in tables)
        {
            layers[table.Name] = 0;
            dependencies[table.Name] = [];
        }

        foreach (var relationship in relationships)
        {
            if (relationship.From != relationship.To
                && dependencies.TryGetValue(relationship.From, out var list)
                && layers.ContainsKey(relationship.To)
                && !list.Contains(relationship.To))
            {
                list.Add(relationship.To);
            }
        }

        RemoveBackEdges(tables, dependencies);

        // Bez cyklů stačí tolik průchodů, kolik je nejdelší řetěz závislostí.
        for (var pass = 0; pass < tables.Count; pass++)
        {
            var changed = false;

            foreach (var table in tables)
            {
                var wanted = 0;

                foreach (var dependency in dependencies[table.Name])
                {
                    wanted = Math.Max(wanted, layers[dependency] + 1);
                }

                if (wanted > layers[table.Name])
                {
                    layers[table.Name] = wanted;
                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }

        return layers;
    }

    /// <summary>
    /// Odstraní hrany uzavírající cyklus. Prochází se do hloubky a hrana vedoucí na uzel,
    /// který je právě na zásobníku, se zahodí — tím z grafu vznikne acyklický, ve kterém
    /// mají vrstvy konečnou hodnotu.
    /// </summary>
    private static void RemoveBackEdges(
        IReadOnlyList<DbTable> tables,
        Dictionary<DbObjectName, List<DbObjectName>> dependencies)
    {
        var hotovo = new HashSet<DbObjectName>();
        var naZasobniku = new HashSet<DbObjectName>();

        foreach (var table in tables)
        {
            Visit(table.Name);
        }

        void Visit(DbObjectName node)
        {
            if (!hotovo.Add(node))
            {
                return;
            }

            naZasobniku.Add(node);

            var sousede = dependencies[node];

            for (var i = sousede.Count - 1; i >= 0; i--)
            {
                var next = sousede[i];

                if (naZasobniku.Contains(next))
                {
                    // Zpětná hrana: kdyby zůstala, vrstvy by se posouvaly donekonečna.
                    sousede.RemoveAt(i);
                    continue;
                }

                Visit(next);
            }

            naZasobniku.Remove(node);
        }
    }

    /// <summary>
    /// Kolik kotev bude mít každá tabulka na té hustší ze svých dvou stran.
    /// </summary>
    /// <remarks>
    /// Počítá se před umístěním, protože z toho plyne minimální výška uzlu. Strana se
    /// odvodí z vrstev: tabulka odkazující doprava vystupuje pravým okrajem. Po zalomení
    /// vrstvy do podsloupců se odhad může od skutečné strany lišit — plyne z něj ale jen
    /// výška uzlu, ne trasa, takže nejhůř zbude pár pixelů místa navíc.
    /// </remarks>
    private static Dictionary<DbObjectName, int> AnchorCounts(
        IReadOnlyList<DbTable> tables,
        IReadOnlyList<DbRelationship> relationships,
        Dictionary<DbObjectName, int> layers)
    {
        var vlevo = new Dictionary<DbObjectName, int>();
        var vpravo = new Dictionary<DbObjectName, int>();

        foreach (var r in relationships)
        {
            if (r.From == r.To
                || !layers.TryGetValue(r.From, out var fromLayer)
                || !layers.TryGetValue(r.To, out var toLayer))
            {
                continue;
            }

            var fromRight = fromLayer <= toLayer;

            Zvys(fromRight ? vpravo : vlevo, r.From);
            Zvys(fromRight ? vlevo : vpravo, r.To);
        }

        return tables.ToDictionary(
            static t => t.Name,
            t => Math.Max(Pocet(vlevo, t.Name), Pocet(vpravo, t.Name)));

        static void Zvys(Dictionary<DbObjectName, int> kam, DbObjectName name) =>
            kam[name] = Pocet(kam, name) + 1;

        static int Pocet(Dictionary<DbObjectName, int> kde, DbObjectName name) =>
            kde.TryGetValue(name, out var value) ? value : 0;
    }

    /// <summary>Kolikrát se pořadí ve vrstvách přepočítá.</summary>
    private const int OrderingPasses = 4;

    /// <summary>
    /// Seřadí tabulky uvnitř vrstev tak, aby se vazby co nejméně křížily.
    /// </summary>
    /// <remarks>
    /// Abecední pořadí je předvídatelné, ale o vazby se nestará: tabulka odkazovaná
    /// shora skončí dole a její čára se protne přes celou vrstvu. Používá se proto
    /// barycentrum — uzel se posune na průměrnou pozici svých sousedů v sousední vrstvě.
    /// Několik průchodů tam a zpět pořadí ustálí. Není to optimální řešení (to je
    /// NP-těžké), ale křížení ubere podstatnou část.
    ///
    /// Abecední pořadí zůstává výchozím stavem i rozhodčím při shodě, takže výsledek
    /// je pokaždé stejný.
    /// </remarks>
    internal static Dictionary<DbObjectName, int> OrderWithinLayers(
        IReadOnlyList<DbTable> tables,
        Dictionary<DbObjectName, int> layers,
        IReadOnlyList<DbRelationship> relationships)
    {
        var sousede = tables.ToDictionary(static t => t.Name, static _ => new List<DbObjectName>());

        foreach (var r in relationships)
        {
            if (r.From == r.To
                || !sousede.TryGetValue(r.From, out var odFrom)
                || !sousede.TryGetValue(r.To, out var odTo))
            {
                continue;
            }

            odFrom.Add(r.To);
            odTo.Add(r.From);
        }

        var vrstvy = tables
            .GroupBy(t => layers[t.Name])
            .OrderBy(static g => g.Key)
            .Select(static g => g
                .OrderBy(static t => t.Name.Schema ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static t => t.Qualified, StringComparer.OrdinalIgnoreCase)
                .Select(static t => t.Name)
                .ToList())
            .ToList();

        var pozice = new Dictionary<DbObjectName, int>();
        Preznac();

        for (var pass = 0; pass < OrderingPasses; pass++)
        {
            // Střídavě zleva a zprava: každý směr srovnává vrstvu podle té sousední,
            // která už je usazená.
            var zleva = pass % 2 == 0;

            for (var i = 0; i < vrstvy.Count; i++)
            {
                var index = zleva ? i : vrstvy.Count - 1 - i;
                var sousedni = zleva ? index - 1 : index + 1;

                if (sousedni < 0 || sousedni >= vrstvy.Count)
                {
                    continue;
                }

                // Schéma je první kritérium: tabulky z jednoho schématu patří k sobě
                // i za cenu pár křížení navíc, protože tak se v databázi čtou.
                vrstvy[index] = vrstvy[index]
                    .OrderBy(n => n.Schema ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(n => Barycentrum(n, vrstvy[sousedni]))
                    .ThenBy(n => n.Qualified, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                Preznac();
            }
        }

        return pozice;

        void Preznac()
        {
            var poradi = 0;

            foreach (var vrstva in vrstvy)
            {
                foreach (var name in vrstva)
                {
                    pozice[name] = poradi++;
                }
            }
        }

        double Barycentrum(DbObjectName node, List<DbObjectName> sousedniVrstva)
        {
            var indexy = sousede[node]
                .Select(soused => sousedniVrstva.IndexOf(soused))
                .Where(static i => i >= 0)
                .ToList();

            // Tabulka bez souseda v té vrstvě nemá co srovnávat; zůstane, kde byla.
            return indexy.Count == 0 ? pozice[node] : indexy.Average();
        }
    }

    /// <summary>Umístění uzlu uvnitř bloku, ještě v jeho vlastních souřadnicích.</summary>
    private readonly record struct Umisteni(DbTable Table, double X, double Y, double Height, int Layer);

    /// <summary>Samostatně rozvržená část schématu se souřadnicemi od nuly.</summary>
    private sealed record Blok(IReadOnlyList<Umisteni> Nodes)
    {
        public double Width { get; } = Nodes.Max(static n => n.X + CollapsedWidth);

        public double Height { get; } = Nodes.Max(static n => n.Y + n.Height);

        /// <summary>Jméno první tabulky v abecedě. Rozhoduje při shodě velikosti bloků.</summary>
        public string Key { get; } = Nodes
            .Select(static n => n.Table.Qualified)
            .OrderBy(static q => q, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    /// <summary>
    /// Rozmístí tabulky do plochy s rozumným poměrem stran.
    /// </summary>
    /// <remarks>
    /// Části schématu, které spolu nesdílejí jedinou vazbu, se rozvrhnou zvlášť a výsledné
    /// bloky se poskládají vedle sebe. Ve společných vrstvách by se jen prokládaly a jejich
    /// vazby by se táhly přes celý obrázek, přestože spolu nemají co dělat — typicky
    /// tabulky přihlašování vedle tabulek objednávek.
    /// </remarks>
    private static List<DiagramNode> PlaceNodes(
        IReadOnlyList<DbTable> tables,
        Dictionary<DbObjectName, int> layers,
        IReadOnlyList<DbRelationship> relationships,
        Dictionary<DbObjectName, double> heights)
    {
        var (targetWidth, targetHeight) = TargetBox(tables, heights);
        var (komponenty, samostatne) = SplitComponents(tables, relationships);

        // Největší blok jde první: kolem něj se ty menší poskládají líp než naopak.
        var bloky = komponenty
            .Select(k => PlaceComponent(k, layers, relationships, heights, targetHeight))
            .OrderByDescending(static b => b.Height)
            .ThenBy(static b => b.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (samostatne.Count > 0)
        {
            // Mřížka až nakonec — nemá se k čemu vázat, takže nesmí rozhánět zbytek.
            bloky.Add(PlaceGrid(samostatne, heights, targetWidth));
        }

        return PackBlocks(bloky);
    }

    /// <summary>
    /// Cílové rozměry plochy, odvozené z celkové plochy uzlů.
    /// </summary>
    /// <remarks>
    /// Šířka viewportu se schválně nepoužívá: layout musí vyjít pokaždé stejně, jinak
    /// by se diagram po zvětšení okna přeskládal a snímky v dokumentaci by neseděly.
    /// </remarks>
    private static (double Width, double Height) TargetBox(
        IReadOnlyList<DbTable> tables,
        Dictionary<DbObjectName, double> heights)
    {
        var plocha = tables.Sum(t => (CollapsedWidth + LayerGap) * (heights[t.Name] + NodeGap));
        var width = Math.Sqrt(plocha * TargetAspect);

        return (width, plocha / width);
    }

    /// <summary>
    /// Rozdělí tabulky na souvislé části a na ty úplně bez vazeb.
    /// </summary>
    /// <remarks>
    /// Vazba mimo zobrazené tabulky se nepočítá. Po zapnutí filtru je taková tabulka
    /// v diagramu opravdu osamocená, i když cizí klíč v databázi má.
    /// </remarks>
    internal static (List<List<DbTable>> Components, List<DbTable> Isolated) SplitComponents(
        IReadOnlyList<DbTable> tables,
        IReadOnlyList<DbRelationship> relationships)
    {
        var sousede = tables.ToDictionary(static t => t.Name, static _ => new List<DbObjectName>());
        var podleJmena = tables.ToDictionary(static t => t.Name);
        var vevazbe = new HashSet<DbObjectName>();

        foreach (var r in relationships)
        {
            if (!sousede.TryGetValue(r.From, out var odFrom)
                || !sousede.TryGetValue(r.To, out var odTo))
            {
                continue;
            }

            vevazbe.Add(r.From);
            vevazbe.Add(r.To);

            // Smyčka do sebe sama tabulku s nikým nespojuje, ale mezi tabulky bez vazeb
            // ji nepustí: vazbu má, jen vede zpátky do ní, a oblouk potřebuje místo vedle.
            if (r.From != r.To)
            {
                odFrom.Add(r.To);
                odTo.Add(r.From);
            }

            // Sbalená N:M vazba se kreslí mezi konci, takže vazební tabulka sama žádnou
            // hranu nemá. Do mřížky „bez vazeb" přesto nepatří — váže se na obě strany
            // a v mřížce by tvrdila, že s ničím nesouvisí.
            if (r.ViaJoinTable is { } via && sousede.TryGetValue(via, out var odVia))
            {
                vevazbe.Add(via);

                odVia.Add(r.From);
                odVia.Add(r.To);
                odFrom.Add(via);
                odTo.Add(via);
            }
        }

        var komponenty = new List<List<DbTable>>();
        var samostatne = new List<DbTable>();
        var videne = new HashSet<DbObjectName>();

        foreach (var table in tables)
        {
            if (!vevazbe.Contains(table.Name))
            {
                samostatne.Add(table);
                continue;
            }

            if (!videne.Add(table.Name))
            {
                continue;
            }

            var komponenta = new List<DbTable> { table };
            var fronta = new Queue<DbObjectName>();
            fronta.Enqueue(table.Name);

            while (fronta.Count > 0)
            {
                foreach (var soused in sousede[fronta.Dequeue()])
                {
                    if (videne.Add(soused))
                    {
                        komponenta.Add(podleJmena[soused]);
                        fronta.Enqueue(soused);
                    }
                }
            }

            komponenty.Add(komponenta);
        }

        return (komponenty, samostatne);
    }

    /// <summary>Rozvrhne jednu souvislou část schématu do vrstev.</summary>
    private static Blok PlaceComponent(
        List<DbTable> tables,
        Dictionary<DbObjectName, int> layers,
        IReadOnlyList<DbRelationship> relationships,
        Dictionary<DbObjectName, double> heights,
        double columnLimit)
    {
        var poradi = OrderWithinLayers(tables, layers, relationships);
        var umisteni = new List<Umisteni>(tables.Count);
        var x = 0.0;

        foreach (var layer in tables.GroupBy(t => layers[t.Name]).OrderBy(static g => g.Key))
        {
            var sloupce = WrapIntoColumns(
                layer.OrderBy(t => poradi[t.Name]).ToList(), heights, columnLimit);

            for (var i = 0; i < sloupce.Count; i++)
            {
                var y = 0.0;

                foreach (var table in sloupce[i])
                {
                    var height = heights[table.Name];

                    umisteni.Add(new Umisteni(table, x, y, height, layer.Key));
                    y += height + NodeGap;
                }

                // Mezi podsloupci jedné vrstvy nevede žádná hrana, takže smějí stát blíž
                // u sebe než dvě vrstvy, mezi které se čáry musí vejít.
                x += CollapsedWidth + (i == sloupce.Count - 1 ? LayerGap : ColumnGap);
            }
        }

        return new Blok(umisteni);
    }

    /// <summary>
    /// Rozdělí vrstvu na tolik podsloupců, aby se každý vešel do zadané výšky.
    /// </summary>
    /// <remarks>
    /// Vrstva s padesáti tabulkami je jinak sloupec dlouhý několik obrazovek. Zalomí se
    /// proto stejně jako text — pořadí zůstává, jen pokračuje o sloupec vedle.
    /// </remarks>
    /// <param name="ordered">Tabulky vrstvy v pořadí, ve kterém mají jít pod sebe.</param>
    /// <param name="heights">Výšky uzlů podle jména tabulky.</param>
    /// <param name="columnLimit">Nejvyšší přípustná výška jednoho sloupce.</param>
    internal static List<List<DbTable>> WrapIntoColumns(
        IReadOnlyList<DbTable> ordered,
        Dictionary<DbObjectName, double> heights,
        double columnLimit)
    {
        ArgumentNullException.ThrowIfNull(ordered);
        ArgumentNullException.ThrowIfNull(heights);

        var celkem = ordered.Sum(t => heights[t.Name] + NodeGap) - NodeGap;
        var pocet = (int)Math.Ceiling(celkem / columnLimit);

        if (pocet <= 1)
        {
            return [[.. ordered]];
        }

        // Plnit až po limit by nechalo poslední sloupec skoro prázdný; cílem je rovnoměr.
        var cil = celkem / pocet;
        var sloupce = new List<List<DbTable>>();
        var soucasny = new List<DbTable>();
        var vyska = 0.0;

        foreach (var table in ordered)
        {
            var height = heights[table.Name];

            if (soucasny.Count > 0 && sloupce.Count + 1 < pocet && vyska + NodeGap + height > cil)
            {
                sloupce.Add(soucasny);
                soucasny = [];
                vyska = 0;
            }

            vyska += (soucasny.Count > 0 ? NodeGap : 0) + height;
            soucasny.Add(table);
        }

        sloupce.Add(soucasny);

        return sloupce;
    }

    /// <summary>
    /// Poskládá tabulky bez jediné vazby do mřížky.
    /// </summary>
    /// <remarks>
    /// Ve vrstvě stojí pod sebou a každá ukrojí kus výšky, přestože o vazbách neříkají nic —
    /// v ukázkovém schématu tak samotné číselníky nastavení natáhly diagram o osm řádků.
    /// V mřížce zaberou pruh a čtou se po řádcích jako seznam.
    /// </remarks>
    private static Blok PlaceGrid(
        List<DbTable> tables,
        Dictionary<DbObjectName, double> heights,
        double targetWidth)
    {
        var sirkaSloupce = CollapsedWidth + NodeGap;
        var sloupcu = Math.Max(1, (int)Math.Floor((targetWidth + NodeGap) / sirkaSloupce));

        var serazene = tables
            .OrderBy(static t => t.Name.Schema ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static t => t.Qualified, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var umisteni = new List<Umisteni>(serazene.Count);
        var y = 0.0;

        for (var i = 0; i < serazene.Count; i += sloupcu)
        {
            var radek = serazene.Skip(i).Take(sloupcu).ToList();

            for (var j = 0; j < radek.Count; j++)
            {
                umisteni.Add(new Umisteni(
                    radek[j], j * sirkaSloupce, y, heights[radek[j].Name], 0));
            }

            y += radek.Max(t => heights[t.Name]) + NodeGap;
        }

        return new Blok(umisteni);
    }

    /// <summary>
    /// Poskládá bloky do řádků.
    /// </summary>
    /// <remarks>
    /// Šířka řádku se nevolí předem. Blok je nedělitelný, takže při šířce o kousek menší,
    /// než jsou dva bloky vedle sebe, zbude na řádku jeden a diagram se znovu protáhne
    /// do výšky — přesně tomu se má skládání vyhnout. Zkusí se proto všechny šířky, na
    /// kterých nějaký blok končí, a vybere se ta, po které je výsledek nejblíž cílovému
    /// poměru stran.
    /// </remarks>
    private static List<DiagramNode> PackBlocks(List<Blok> bloky)
    {
        var limity = new List<double>();
        var sirka = 0.0;

        foreach (var blok in bloky)
        {
            sirka += (limity.Count > 0 ? BlockGap : 0) + blok.Width;
            limity.Add(sirka);
        }

        // MinBy vrací první nejlepší, takže při shodě rozhodne pořadí limitů — méně
        // řádků před více. Rozvržení tím zůstává pokaždé stejné.
        return limity
            .Select(limit => Rozmisti(bloky, limit))
            .MinBy(static r => Math.Abs(Math.Log(r.Width / r.Height / TargetAspect)))
            .Nodes;
    }

    /// <summary>Rozmístí bloky do řádků nejvýš zadané šířky.</summary>
    private static (List<DiagramNode> Nodes, double Width, double Height) Rozmisti(
        List<Blok> bloky,
        double limit)
    {
        var nodes = new List<DiagramNode>();
        var x = 0.0;
        var y = 0.0;
        var vyskaRadku = 0.0;
        var sirka = 0.0;

        foreach (var blok in bloky)
        {
            // Blok širší než limit se nezalomí — na svém řádku stojí sám.
            if (x > 0 && x + blok.Width > limit)
            {
                x = 0;
                y += vyskaRadku + BlockGap;
                vyskaRadku = 0;
            }

            foreach (var umisteni in blok.Nodes)
            {
                nodes.Add(new DiagramNode
                {
                    Table = umisteni.Table,
                    X = Margin + x + umisteni.X,
                    Y = Margin + y + umisteni.Y,
                    Width = CollapsedWidth,
                    Height = umisteni.Height,
                    Layer = umisteni.Layer,
                });
            }

            x += blok.Width + BlockGap;
            vyskaRadku = Math.Max(vyskaRadku, blok.Height);
            sirka = Math.Max(sirka, x - BlockGap);
        }

        return (nodes, sirka, y + vyskaRadku);
    }

    /// <summary>
    /// Výška uzlu. Sbalený uzel ukazuje jen klíčové sloupce — u tabulky s třiceti sloupci
    /// je jinak diagram nečitelný.
    /// </summary>
    internal static double NodeHeight(DbTable table, bool isExpanded, int anchorCount = 0)
    {
        var rows = isExpanded
            ? table.Columns.Count
            : table.Columns.Count(static c => c.IsPrimaryKey || c.IsForeignKey);

        var podleSloupcu = HeaderHeight + (Math.Max(rows, 1) * RowHeight) + 8;

        // Tabulka se třemi vazbami a jedním sloupcem by měla kotvy na sobě. Uzel proto
        // povyroste tak, aby se mezi ně vešly popisky. Kotvy se roztahují přes celou
        // použitelnou výšku, takže n kotev potřebuje n−1 mezer.
        var podleKotev = (2 * AnchorInset) + (Math.Max(anchorCount - 1, 0) * MinAnchorGap);

        return Math.Max(podleSloupcu, podleKotev);
    }

    /// <summary>Sloupce zobrazené v uzlu podle toho, jestli je rozbalený.</summary>
    public static IReadOnlyList<DbColumn> VisibleColumns(DbTable table, bool isExpanded)
    {
        ArgumentNullException.ThrowIfNull(table);

        if (isExpanded)
        {
            return table.Columns;
        }

        var keys = table.Columns.Where(static c => c.IsPrimaryKey || c.IsForeignKey).ToList();

        return keys.Count > 0 ? keys : [];
    }

    /// <summary>Spoj mezi dvěma uzly, ještě bez trasy.</summary>
    private readonly record struct Spoj(
        DbRelationship Relationship,
        DiagramNode From,
        DiagramNode To,
        bool FromRight);

    private static List<DiagramEdge> RouteEdges(
        List<DiagramNode> nodes,
        IReadOnlyList<DbRelationship> relationships)
    {
        var byName = nodes.ToDictionary(static n => n.Table.Name);
        var obstacles = nodes
            .Select(static n => new RouteObstacle(n.X, n.Y, n.Width, n.Height))
            .ToList();

        var edges = new List<DiagramEdge>();
        var spoje = new List<Spoj>();

        foreach (var relationship in relationships)
        {
            if (!byName.TryGetValue(relationship.From, out var from)
                || !byName.TryGetValue(relationship.To, out var to))
            {
                continue;
            }

            if (relationship.From == relationship.To)
            {
                edges.Add(SelfLoop(relationship, from));
                continue;
            }

            spoje.Add(new Spoj(relationship, from, to, from.CenterX <= to.CenterX));
        }

        var kotvy = AssignAnchors(spoje);
        var hotove = new List<IReadOnlyList<(double X, double Y)>>();

        // Krátké vazby se vedou první. Mají nejmíň možností, jak se vyhnout, a ty dlouhé
        // se pak přizpůsobí jim — opačné pořadí zaplní úzká místa dlouhými čárami.
        foreach (var spoj in spoje.OrderBy(static s => Math.Abs(s.To.CenterX - s.From.CenterX)
                                                     + Math.Abs(s.To.CenterY - s.From.CenterY)))
        {
            var startSide = spoj.FromRight ? EdgeSide.Right : EdgeSide.Left;
            var endSide = spoj.FromRight ? EdgeSide.Left : EdgeSide.Right;

            var start = (SideX(spoj.From, startSide), kotvy[(spoj.Relationship, true)]);
            var end = (SideX(spoj.To, endSide), kotvy[(spoj.Relationship, false)]);

            var points = EdgeRouter.Route(start, startSide, end, endSide, obstacles, hotove);
            hotove.Add(points);

            edges.Add(new DiagramEdge
            {
                Relationship = spoj.Relationship,
                Points = points,
                LabelAt = LabelPosition(points),
            });
        }

        return SpreadLabels(edges);
    }

    /// <summary>Nejmenší vodorovný odstup dvou popisků, aby se nepřekrývaly.</summary>
    private const double LabelWidth = 44;

    /// <summary>Nejmenší svislý odstup dvou popisků.</summary>
    private const double LabelLineHeight = 13;

    /// <summary>
    /// O kolik se popisek posune podél trasy při hledání volného místa. Menší krok
    /// nemá smysl — kratší posun by pořád kolidoval a jen by se zkoušel naprázdno.
    /// </summary>
    private const double LabelStep = LabelWidth;

    /// <summary>Kolik míst podél trasy se zkusí, než se popisek uhne kolmo.</summary>
    private const int LabelAttempts = 6;

    /// <summary>
    /// Rozmístí popisky kardinality tak, aby se nepřekrývaly.
    /// </summary>
    /// <remarks>
    /// Do jedné tabulky můžou mířit vazby zleva i zprava a jejich popisky skončí na
    /// stejném místě — v diagramu se to projeví jako slitý shluk typu „1:N:M".
    ///
    /// Kolize se řeší posunem **podél vlastní trasy**, ne kolmo na ni: popisek tak
    /// zůstane na své hraně a je pořád jasné, ke které patří. Teprve když se podél
    /// trasy volné místo nenajde, uhne se kolmo — to je poslední možnost, protože
    /// odsazený popisek se hůř přiřazuje k čáře.
    /// </remarks>
    internal static List<DiagramEdge> SpreadLabels(List<DiagramEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(edges);

        var obsazene = new List<(double X, double Y)>();
        var result = new List<DiagramEdge>(edges.Count);

        // Pevné pořadí, aby se stejný diagram vykreslil pokaždé stejně.
        foreach (var edge in edges.OrderBy(static e => e.LabelAt.Y).ThenBy(static e => e.LabelAt.X))
        {
            result.Add(edge with { LabelAt = VolneMisto(edge, obsazene) });
        }

        return result;
    }

    /// <summary>Najde pro popisek místo, kde nekoliduje s už umístěnými.</summary>
    private static (double X, double Y) VolneMisto(
        DiagramEdge edge,
        List<(double X, double Y)> obsazene)
    {
        foreach (var at in Kandidati(edge.LabelAt, PopiskovyUsek(edge.Points)))
        {
            if (!Koliduje(at, obsazene))
            {
                obsazene.Add(at);
                return at;
            }
        }

        // Podél trasy je plno; uhneme kolmo, dokud se místo nenajde.
        var nouzove = edge.LabelAt;

        while (Koliduje(nouzove, obsazene))
        {
            nouzove = (nouzove.X, nouzove.Y - LabelLineHeight);
        }

        obsazene.Add(nouzove);
        return nouzove;
    }

    private static bool Koliduje((double X, double Y) at, List<(double X, double Y)> obsazene) =>
        obsazene.Exists(o => Math.Abs(o.X - at.X) < LabelWidth
                             && Math.Abs(o.Y - at.Y) < LabelLineHeight);

    /// <summary>
    /// Místa podél trasy, kam popisek smí. První je to původní, další postupně dál
    /// od šipky — směrem, kterým hrana přišla.
    /// </summary>
    private static IEnumerable<(double X, double Y)> Kandidati(
        (double X, double Y) puvodni,
        ((double X, double Y) Od, (double X, double Y) Do)? usek)
    {
        yield return puvodni;

        if (usek is not { } u)
        {
            yield break;
        }

        var vodorovny = Math.Abs(u.Od.Y - u.Do.Y) < 0.5;
        var delka = vodorovny ? Math.Abs(u.Do.X - u.Od.X) : Math.Abs(u.Do.Y - u.Od.Y);

        // Směr od šipky zpět po hraně.
        var smer = vodorovny
            ? (u.Do.X > u.Od.X ? -1 : 1)
            : (u.Do.Y > u.Od.Y ? -1 : 1);

        for (var i = 1; i <= LabelAttempts; i++)
        {
            var posun = i * LabelStep;

            // Za začátek úseku se popisek nedostane — patřil by pak k jiné části trasy.
            if (posun > delka)
            {
                yield break;
            }

            yield return vodorovny
                ? (puvodni.X + (smer * posun), puvodni.Y)
                : (puvodni.X, puvodni.Y + (smer * posun));
        }
    }

    /// <summary>Úsek trasy, na kterém popisek leží — poslední vodorovný před šipkou.</summary>
    private static ((double X, double Y) Od, (double X, double Y) Do)? PopiskovyUsek(
        IReadOnlyList<(double X, double Y)> points)
    {
        for (var i = points.Count - 1; i >= 1; i--)
        {
            if (Math.Abs(points[i - 1].Y - points[i].Y) < 0.5)
            {
                return (points[i - 1], points[i]);
            }
        }

        return points.Count >= 2 ? (points[^2], points[^1]) : null;
    }

    private static double SideX(DiagramNode node, EdgeSide side) =>
        side == EdgeSide.Right ? node.X + node.Width : node.X;

    /// <summary>
    /// Rozprostře kotevní body po okrajích uzlů.
    /// </summary>
    /// <remarks>
    /// Kdyby všechny hrany vycházely ze středu, u tabulky s několika vazbami by se šipky
    /// slily do jednoho bodu a nešlo by poznat, která kam vede. Kotvy se proto rozdělí
    /// po výšce okraje a seřadí podle toho, kde leží protější uzel — tím se hrany
    /// na okraji nekříží.
    /// </remarks>
    private static Dictionary<(DbRelationship Relationship, bool IsSource), double> AssignAnchors(
        IReadOnlyList<Spoj> spoje)
    {
        var poptavka = new Dictionary<(DbObjectName Table, EdgeSide Side), List<(DbRelationship R, bool Source, double Toward)>>();

        foreach (var spoj in spoje)
        {
            var startSide = spoj.FromRight ? EdgeSide.Right : EdgeSide.Left;
            var endSide = spoj.FromRight ? EdgeSide.Left : EdgeSide.Right;

            Pridej(spoj.From.Table.Name, startSide, spoj.Relationship, true, spoj.To.CenterY);
            Pridej(spoj.To.Table.Name, endSide, spoj.Relationship, false, spoj.From.CenterY);
        }

        var result = new Dictionary<(DbRelationship, bool), double>();

        foreach (var ((table, side), zadosti) in poptavka)
        {
            var node = spoje
                .SelectMany<Spoj, DiagramNode>(static s => [s.From, s.To])
                .First(n => n.Table.Name == table);

            var serazene = zadosti.OrderBy(static z => z.Toward).ToList();

            // Krajních pár pixelů se nechává volných, aby kotva neseděla přesně v rohu.
            var top = node.Y + AnchorInset;
            var usable = Math.Max(node.Height - (2 * AnchorInset), 0);

            for (var i = 0; i < serazene.Count; i++)
            {
                // Jediná kotva patří doprostřed; víc kotev se roztáhne přes celou
                // použitelnou výšku, ať mají co největší rozestup.
                var podil = serazene.Count == 1 ? 0.5 : i / (serazene.Count - 1.0);

                result[(serazene[i].R, serazene[i].Source)] = top + (usable * podil);
            }
        }

        return result;

        void Pridej(DbObjectName table, EdgeSide side, DbRelationship r, bool source, double toward)
        {
            var key = (table, side);

            if (!poptavka.TryGetValue(key, out var list))
            {
                list = [];
                poptavka[key] = list;
            }

            list.Add((r, source, toward));
        }
    }

    /// <summary>Jak daleko před šipkou popisek stojí.</summary>
    private const double LabelOffset = 26;

    /// <summary>
    /// Popisek kardinality jde na poslední vodorovný úsek, kousek před šipku.
    /// </summary>
    /// <remarks>
    /// Původně seděl uprostřed nejdelšího úseku, jenže tam se popisky několika vazeb
    /// mířících do jedné tabulky slily do jedné nečitelné hromádky („N:M1:N"). U cíle
    /// má každá vazba svou kotvu, takže se rozejdou samy — a kardinalita ke konci vazby
    /// patří i významem.
    /// </remarks>
    internal static (double X, double Y) LabelPosition(IReadOnlyList<(double X, double Y)> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        for (var i = points.Count - 1; i >= 1; i--)
        {
            var a = points[i - 1];
            var b = points[i];

            if (Math.Abs(a.Y - b.Y) > 0.5)
            {
                continue;
            }

            // Na krátkém úseku by odsazení popisek posunulo až za jeho začátek.
            var odsazeni = Math.Min(LabelOffset, Math.Abs(b.X - a.X) / 2);

            return (b.X > a.X ? b.X - odsazeni : b.X + odsazeni, b.Y);
        }

        return points[0];
    }

    private static DiagramEdge SelfLoop(DbRelationship relationship, DiagramNode node)
    {
        var right = node.X + node.Width;
        var top = node.Y + (node.Height / 3);
        var bottom = node.Y + (node.Height * 2 / 3);
        var out_ = right + 30;

        IReadOnlyList<(double X, double Y)> points =
        [
            (right, top),
            (out_, top),
            (out_, bottom),
            (right, bottom),
        ];

        return new DiagramEdge
        {
            Relationship = relationship,
            Points = points,
            LabelAt = (out_ + 6, (top + bottom) / 2),
            IsSelfLoop = true,
        };
    }
}
