# 0014. Historie schématu ze snapshotů EF migrací

- **Stav:** Přijato
- **Datum:** 2026-09-02

## Kontext

Prohlížečka uměla ukázat schéma teď: z EF modelu, z databáze, nebo obojí sloučené.
Neuměla ale odpovědět na otázku „jak vypadalo schéma před půl rokem" ani „co přinesla
tahle migrace". Přitom o migracích věděla — jen z nich četla samotné identifikátory
a stav aplikace.

Přidávat vlastní verzování schématu do databáze nebo skladovat snapshoty někde stranou
by znamenalo psát a udržovat něco, co v projektu s EF migracemi už existuje.

## Rozhodnutí

Historie se čte z toho, co EF drží v assembly aplikace. Každá migrace nese dvě věci:

**Operace** (`UpOperations`) — typované objekty, ne text. `AddColumnOperation`,
`DropIndexOperation`, `AlterColumnOperation` (i se starým stavem sloupce) a další.
Převádí se na `DbSchemaChange`, tvar nezávislý na EF, aby s ním uměl pracovat
i WebAssembly klient.

**Snapshot modelu** (`TargetModel`) — celé schéma po provedení migrace. Před použitím
se musí doinicializovat (`IModelRuntimeInitializer.Initialize(model, designTime: true)`),
jinak `GetRelationalModel()` vyhodí výjimku; pak už je to `IModel` jako každý jiný.

Ze snapshotu se **stejným čtecím kódem** (`EfModelReader`) sestaví `DatabaseSchema`.
Tím se historie chová jako další zdroj schématu: přehled, seznam tabulek i ER diagram
fungují beze změny, protože pracují s `DatabaseSchema` bez ohledu na to, odkud je.

Porovnání dvou verzí používá **stejný porovnávač** jako drift proti živé databázi
(`SchemaComparer`). Starší verze vystupuje jako „model", novější jako „databáze",
takže se změny čtou ve směru času. Mění se jen slova v UI: „sloupec je v databázi,
ale v modelu není" dává smysl u driftu, kdežto v historii je to „sloupec přibyl".

## Zvažované alternativy

**Vlastní tabulka se snapshoty schématu.** Fungovala by i bez EF migrací, ale znamená
zápis do cizí databáze — a DbsViewer je zásadně read-only (ADR-0006). Navíc by
zaznamenávala historii až od svého nasazení, kdežto snapshoty migrací sahají do minulosti.

**Parsování souborů migrací.** Nezávislé na běhové assembly, ale rozbije se na každé
netriviální migraci a nedá snapshot celého modelu, jen jednotlivé operace.

**Jen výpis operací, bez snapshotů.** Levnější, ale neumí „ukaž mi schéma k této verzi"
ani porovnání dvou vzdálených bodů. Právě to je přitom nejužitečnější — operace samotné
odpovídají na „co se změnilo", ne na „jak to tehdy vypadalo".

## Důsledky

- Historie funguje jen s EF migracemi a jen pro migrace, jejichž **kód je v assembly**.
  Migrace, která je v `__EFMigrationsHistory`, ale její třída už v projektu není, se
  v seznamu ukáže se stavem „chybí v kódu" a schéma k ní nabídnout nejde.
- **Vlastní SQL v migraci se analyzovat nedá.** `SqlOperation` nese jen text příkazu;
  hlásí se jako `IsOpaque`, aby uživatel věděl, že výpis změn nemusí být úplný. Snapshot
  to ale zachytí, pokud autor migrace snapshot přegeneroval.
- **Snapshot je to, co si autor migrace myslel.** Ručně upravená migrace bez
  přegenerovaného snapshotu ukáže model, ne realitu. Na to je diff proti živé databázi.
- Sestavení schématu pro jednu migraci stojí zhruba **milisekundu**, takže i u projektu
  se stovkou migrací je to zanedbatelné. Přesto se cachuje — kvůli překlikávání v UI.
  Cache proto klíčuje řetězcem, ne `SchemaView`: verzí je libovolně mnoho.
- **Data z historické verze číst nejde** a UI to musí vynutit. Snapshot popisuje
  strukturu v minulosti, kdežto řádky existují jen v databázi tady a teď — dotaz na
  tabulku, která od té doby zmizela nebo změnila sloupce, nedává smysl. Záložka Data
  je v historickém pohledu nedostupná i s vysvětlením proč.
- Testy potřebují projekt se **skutečnými migracemi** (`samples/DbsViewer.SampleMigrations`).
  Snapshoty se nedají vyrobit v paměti — musí je vygenerovat `dotnet ef` do assembly.
