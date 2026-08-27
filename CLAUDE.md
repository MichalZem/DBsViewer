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
  DbsViewer.Abstractions   Datový model, kontrakty a odvozovací funkce. Bez závislostí.
  DbsViewer.EfCore         Čtení schématu z EF modelu. Provider-agnostické.
  DbsViewer.Relational     Sdílená introspekce: surový model, assembler, spouštění dotazů.
  DbsViewer.SqlServer      Živá introspekce nad sys.*
  DbsViewer.Sqlite         Živá introspekce nad sqlite_master a PRAGMA
  DbsViewer.Analysis       Slučování a porovnání schémat (diff engine).
  DbsViewer.Server         AddDbsViewer / MapDbsViewer, HTTP API, cache, autorizace, náhled dat
  DbsViewer.Ui             (plánováno) Blazor WASM, embedovaný do DbsViewer.Server
samples/
  DbsViewer.SampleShop     Ukázkový model pro testy a ověření
tools/
  DbsViewer.Dump           Konzolový výpis schématu, diff a sloučený pohled
tests/
  DbsViewer.TestKit        Pomůcky sdílené testy. Neměří se pokrytí.
  DbsViewer.EfCore.Tests   Testy Abstractions a EfCore
  DbsViewer.Relational.Tests  Testy Relational, SqlServer, Sqlite a Analysis
  DbsViewer.Server.Tests   Testy Serveru proti běžící aplikaci
docs/adr/                  Architektonická rozhodnutí
```

Závislosti jdou vždy jedním směrem: `Abstractions` → zdroje → `Server` → `Ui`.
`Abstractions` nesmí záviset na ničem, protože ho používá i WASM klient.

**Testy patří k projektu, který testují** — jinak se pokrytí měří jinde, než se testuje,
a práh spadne bez skutečné příčiny. Sdílené pomůcky jdou do `DbsViewer.TestKit`.

---

## Jak to spustit

```bash
# ověřovací výpis schématu ukázkového modelu
dotnet run --project tools/DbsViewer.Dump

# živá databáze
dotnet run --project tools/DbsViewer.Dump -- --sqlite ./app.db --rows
dotnet run --project tools/DbsViewer.Dump -- --sqlserver "Server=.;Database=Eshop;Trusted_Connection=True"

# drift mezi EF modelem a databází (návratový kód 2 při nálezu chyby)
dotnet run --project tools/DbsViewer.Dump -- --diff ./app.db

# nápověda
dotnet run --project tools/DbsViewer.Dump -- --help

# testy včetně vynuceného pokrytí (spouštět z adresáře testovacího projektu)
cd tests/DbsViewer.EfCore.Tests && dotnet test
cd tests/DbsViewer.Relational.Tests && dotnet test
cd tests/DbsViewer.Server.Tests && dotnet test

# zabalení do NuGet balíčků (výstup do artifacts/packages/)
for p in Abstractions EfCore Relational SqlServer Sqlite Analysis Server; do
  dotnet pack src/DbsViewer.$p -c Release -o artifacts/packages
done
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

**Objekty se mezi zdroji párují podle struktury, ne podle jména.** SQLite jména cizích klíčů
nevystavuje a skládají se podle konvence, která se s EF nemusí trefit.
Viz [ADR-0011](docs/adr/0011-parovani-podle-sloupcu.md).

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

Hotové jsou **etapy 01 až 03**: datový model, čtení z EF modelu, živá introspekce obou
providerů, slučování, diff engine, HTTP API s autorizací, cache a náhledem dat. 542 testů.

Ověřeno end-to-end: balíček se dá zabalit, nainstalovat do čerstvé Web API aplikace
a dvěma řádky zapojit. Postup instalace je v [README](README.md).

Zbývá:

| # | Etapa |
|---|---|
| 04 | Blazor WASM UI bez diagramu, embedding do balíčku |
| 05 | ER diagram — SVG renderer, elkjs layout, focus mode, export |
| 06 | Diff a náhled dat v UI |
| 07 | Publikování na nuget.org, `dotnet tool`, statický snapshot do `docs/` |
