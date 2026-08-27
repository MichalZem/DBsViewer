# DbsViewer

Zabudovatelná prohlížečka databázového schématu pro EF Core aplikace. Přidá se do projektu
jako NuGet balíček, dvěma řádky v `Program.cs` se zapne a nabídne prohlížeč tabulek, sloupců,
indexů, klíčů a grafický ER diagram vazeb.

**Jazyk projektu je čeština.** Komentáře, XML dokumentace, jména testů, ADR i commit messages
se píšou česky. Identifikátory v kódu jsou anglicky.

---

## Nepřekročitelná pravidla

Tohle není doporučení. Když si nejsi jistý, zeptej se — neobcházej to.

### Nikdy necommituj ani nepushuj sám

Commit a push dělá **výhradně uživatel, nebo agent na výslovnou žádost v dané konverzaci**.

- Nikdy nespouštěj `git commit`, `git push`, `git tag` ani `git merge` z vlastní iniciativy —
  ani „na konec úkolu", ani „aby se práce neztratila", ani když je změna hotová a otestovaná.
- Souhlas platí jen pro ten jeden úkon, o který uživatel požádal. Nepřenáší se na další
  commity později v konverzaci.
- Když je práce hotová, řekni to a nech rozhodnutí na uživateli.
- Změny v pracovním stromu nech být. `git checkout --`, `git reset --hard`, `git stash` ani
  `git clean` nespouštěj bez výslovného pokynu.

### Pokrytí testy je 100 %

`dotnet test` **selže**, když pokrytí řádků nebo metod klesne pod 100 %. Není to metrika,
je to podmínka buildu. Podrobnosti a odůvodnění v [ADR-0005](docs/adr/0005-stoprocentni-pokryti.md).

Prakticky to znamená: nedosažitelný kód se nepíše. Když ho potřebuješ (obranná pojistka nad
kontraktem EF), vytáhni ho do pojmenovaného helperu a označ
`[ExcludeFromCodeCoverage(Justification = "…")]` s odůvodněním. Nikdy nesnižuj práh.

### Architektonická rozhodnutí patří do ADR

Než změníš něco, co je drahé vrátit, přečti si [`docs/adr/`](docs/adr/README.md).
Když rozhodnutí měníš, napiš nový ADR a starý přepni na `Nahrazeno` — nepřepisuj ho.
Kdy ADR založit, je popsané v [README ADR složky](docs/adr/README.md).

---

## Prostředí

| | |
|---|---|
| SDK | .NET 10 (`global.json` pinuje 10.0.300) |
| EF Core | 10.0.11, verze centrálně v `Directory.Packages.props` |
| Databáze | Microsoft SQL Server a SQLite. **PostgreSQL zatím ne.** |
| Frontend | Blazor WebAssembly |
| Repozitář | https://github.com/MichalZem/DBsViewer |

Balíčky se **nepojmenovávají** s prefixem organizace — je to holé `DbsViewer.*`.

---

## Struktura

```
src/
  DbsViewer.Abstractions   Datový model a kontrakty. Bez závislostí, trimmovatelné.
  DbsViewer.EfCore         Čtení schématu z EF modelu. Provider-agnostické.
  DbsViewer.SqlServer      (plánováno) Živá introspekce nad sys.*
  DbsViewer.Sqlite         (plánováno) Živá introspekce nad PRAGMA
  DbsViewer.Server         (plánováno) AddDbsViewer / MapDbsViewer, minimal API, hosting UI
  DbsViewer.Ui             (plánováno) Blazor WASM, embedovaný do DbsViewer.Server
samples/
  DbsViewer.SampleShop     Ukázkový model pro testy a ověření
tools/
  DbsViewer.Dump           Konzolový výpis schématu do textu a JSON
tests/
  DbsViewer.EfCore.Tests   Testy Abstractions i EfCore, vynucují pokrytí
docs/adr/                  Architektonická rozhodnutí
```

Závislosti jdou vždy jedním směrem: `Abstractions` → zdroje → `Server` → `Ui`.
`Abstractions` nesmí záviset na ničem, protože ho používá i WASM klient.

---

## Jak to spustit

```bash
# ověřovací výpis schématu ukázkového modelu
dotnet run --project tools/DbsViewer.Dump

# totéž plus JSON na disk
dotnet run --project tools/DbsViewer.Dump -- --json schema.json

# testy včetně vynuceného pokrytí (spouštět z adresáře testovacího projektu)
cd tests/DbsViewer.EfCore.Tests && dotnet test
```

Report pokrytí končí v `artifacts/coverage/` ve formátech cobertura, lcov a json.

---

## Zásady, které se projevují v celém kódu

**Načtení schématu nesmí spadnout.** Selhání dílčího čtení skončí jako text
v `DatabaseSchema.Warnings`, ne jako výjimka. Nikdy se nezahazuje potichu — od toho
je `SafeRead`. Jediná výjimka jsou chyby v argumentech (`ArgumentNullException`).

**Čtení EF modelu nesahá do databáze.** Jediná výjimka je seznam aplikovaných migrací
a ten se dá vypnout přes `SchemaReadOptions.IncludeMigrations`.

**Čte se z design-time modelu**, ne z `DbContext.Model` — runtime model nemá komentáře,
collation ani defaulty. Viz [ADR-0003](docs/adr/0003-design-time-model.md).

**Testovatelnost je součást návrhu.** Když se chybová cesta nedá vyvolat testem, není to
důvod ji netestovat, ale důvod vytáhnout ji za rozhraní — tak vznikly `SafeRead`
a `IMigrationsReader`.

**Jména objektů se porovnávají bez ohledu na velikost písmen.** EF model a živá databáze
se v casingu běžně liší; od toho je `DbObjectName`.

**Bezpečnostní defaulty jsou restriktivní** a mimo Development komponenta bez autorizace
nenastartuje. Viz [ADR-0006](docs/adr/0006-bezpecnostni-defaulty.md).

---

## Styl

- Soubor na typ, kromě těsně souvisejících (`DbTable` a `DbColumn` spolu).
- Veřejné typy mají XML dokumentaci. Komentář v těle vysvětluje **proč**, ne co.
- `IReadOnlyList<T>` v datovém modelu, nikdy `List<T>` navenek.
- Kolekční výrazy (`[]`, `[.. source]`) místo `new List<T>()`.
- Testy se jmenují česky a popisují chování: `Vazebni_tabulka_NM_se_oznaci`.
  Jeden test drží jednu vlastnost, aby při rozbití bylo hned vidět co.
- Sekvence v testech se porovnávají přes `Seq.Equal`, ne `Assert.Equal` —
  to má nejednoznačné přetížení mezi `ReadOnlySpan<T>` a `IEnumerable<T>`.

---

## Stav a další kroky

Hotová je **etapa 01**: datový model, čtení z EF modelu, ověřovací výpis, 166 testů.

Zbývá:

| # | Etapa |
|---|---|
| 02 | Živá introspekce SQL Server a SQLite, sloučený pohled |
| 03 | `DbsViewer.Server` — `AddDbsViewer` / `MapDbsViewer`, endpointy, cache, autorizace |
| 04 | Blazor WASM UI bez diagramu, embedding do balíčku |
| 05 | ER diagram — SVG renderer, elkjs layout, focus mode, export |
| 06 | Diff engine v UI, read-only náhled dat s maskováním |
| 07 | NuGet balíčky, `dotnet tool`, statický snapshot do `docs/` |
