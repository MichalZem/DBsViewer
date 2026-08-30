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
| Verzování | Z git tagů přes MinVer. Číslo verze **není** v souboru. |

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
  DbsViewer.Ui             Blazor WASM prohlížečka. Publish se embeduje do Serveru.
samples/
  DbsViewer.SampleShop     Ukázkový model pro testy a ověření
tools/
  DbsViewer.Dump           dotnet tool `dbsview` — výpis, diff a export dokumentace
tests/
  DbsViewer.TestKit        Pomůcky sdílené testy. Neměří se pokrytí.
  DbsViewer.EfCore.Tests   Testy Abstractions a EfCore
  DbsViewer.Relational.Tests  Testy Relational, SqlServer, Sqlite a Analysis
  DbsViewer.Server.Tests   Testy Serveru proti běžící aplikaci
  DbsViewer.Ui.Tests       Testy UI, komponenty přes bUnit
  DbsViewer.Tool.Tests     Testy nástroje včetně zápisu souborů
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
cd tests/DbsViewer.Ui.Tests && dotnet test
cd tests/DbsViewer.Tool.Tests && dotnet test

# zabalení do NuGet balíčků (výstup do artifacts/packages/)
# Publikace UI musí předcházet — serverový balíček ho vkládá do assembly.
dotnet publish src/DbsViewer.Ui -c Release -o artifacts/ui
for p in Abstractions EfCore Relational SqlServer Sqlite Analysis Server; do
  dotnet pack src/DbsViewer.$p -c Release -o artifacts/packages
done
dotnet pack tools/DbsViewer.Dump -c Release -o artifacts/packages
```

**Blazor UI se do serverového balíčku vkládá jen v Release** a **publikuje se zvlášť**,
příkazem `dotnet publish src/DbsViewer.Ui -c Release -o artifacts/ui`. Nespouštěj ho
z MSBuild targetu — `DbsViewer.Ui` je i v řešení, takže by se stavěl dvakrát současně
a build by padal na zamčených mezisouborech. Když publikované UI chybí, build selže
s návodem, takže balíček bez UI nemůže vzniknout. V Debugu se embedding přeskakuje;
vynutit jde přes `-p:EmbedDbsViewerUi=true`.

**Verzi neurčuje soubor, ale git tag.** `Directory.Build.props` proto žádný `<Version>`
nemá — obstarává to MinVer. Tag `v1.2.3` vydá `1.2.3`, commit za ním `1.2.4-alpha.0.N`.
Publikaci na NuGet dělá [workflow](.github/workflows/build-a-publikace.yml), ne člověk.

**Integrační testy SQL Serveru jsou pro pokrytí nutné.** Bez běžící instance klesne
pokrytí `DbsViewer.SqlServer` na necelých 60 % a `dotnet test` selže. Připojení se dá
přesměrovat proměnnou `DBSVIEWER_TEST_SQLSERVER`; na CI běží server v kontejneru.

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

**Nic se nepáruje slovníkem tam, kde klíč nemusí být unikátní.** Tentýž sloupec smí mít
dva cizí klíče na různé tabulky, takže `ToDictionary` by shodilo diff u legálního schématu.

**Připojení zavírá ten, kdo ho otevřel; uvolňuje ten, kdo ho vytvořil.** Obojí řeší
`ConnectionScope` — cizí připojení se zavře, ale neuvolní.

**Bezpečnostní defaulty jsou restriktivní** a mimo Development komponenta bez autorizace
nenastartuje. Viz [ADR-0006](docs/adr/0006-bezpecnostni-defaulty.md).

**UI nepoužívá JavaScript na nic kromě stahování souboru.** Diagram i jeho layout jsou
v C#, protože JS interop se v testech komponent nedá spustit.
Viz [ADR-0012](docs/adr/0012-vlastni-layout-diagramu.md).

**Stav UI žije mimo komponentu** v `ViewerState`, aby se dal testovat bez vykreslování.
Viz [ADR-0013](docs/adr/0013-stav-mimo-komponentu.md).

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
- V Razoru se **string parametr komponenty musí předávat s `@`** (`Error="@Chyba"`).
  Bez něj se hodnota vezme jako literál a chyba je tichá.
- V testech komponent se nad `FindAll` **nepoužívá indexer, ale `ElementAt`** — novější
  AngleSharp změnil binární podpis, na který je bUnit zkompilovaný.
- Testovací databáze v paměti musí mít **unikátní jméno** (GUID). xUnit spouští třídy
  paralelně a dvě databáze stejného jména si přepisují obsah.
- CSS třídy a texty v UI jsou česky, stejně jako zbytek projektu.
- Počty v UI se **skloňují** přes `Cestina` — „1 tabulka“, ne „1 tabulek“.
  Výjimka je vazba po předložce (`2 z 5 tabulek`), kde je vždy genitiv plurálu.
- **Formátování čísel se nesmí opřít o kulturu stroje.** WebAssembly běží invariantně,
  server může mít jakoukoli a CI zase jinou — `{x:N0}` proto dá pokaždé jiný výsledek.
  Od toho je `Cestina.Cislo`.

---

## Stav a další kroky

**Všech sedm etap je hotových.** Datový model, čtení z EF modelu, živá introspekce obou
providerů, slučování, diff engine, HTTP API s autorizací a cache, Blazor WASM prohlížečka
s ER diagramem a focus modem, náhled dat, export a `dotnet tool`. **833 testů**, 100 %
pokrytí řádků a metod ve všech pěti sadách.

Ověřeno end-to-end: balíčky se zabalí, nainstalují do čerstvé Web API aplikace, dvěma
řádky zapojí a prohlížečka se všemi 47 soubory UI se servíruje z embedded resources.

Publikaci obstarává GitHub Actions: push na `main` vydá předběžnou verzi, tag `v*`
stabilní. Publikuje se přes **Trusted Publishing** (OIDC), takže v repozitáři neleží
žádný klíč — jen proměnná `NUGET_USER` se jménem účtu na nuget.org. Bez ní workflow
balíčky sestaví a nechá v artefaktech, ale nepublikuje.
