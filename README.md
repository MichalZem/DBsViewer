# DbsViewer

[![Build a publikace](https://github.com/MichalZem/DBsViewer/actions/workflows/build-a-publikace.yml/badge.svg)](https://github.com/MichalZem/DBsViewer/actions/workflows/build-a-publikace.yml)

Prohlížečka databázového schématu, kterou přidáš do jakékoli EF Core aplikace. Dvěma řádky
v `Program.cs` dostaneš přehled tabulek, sloupců, indexů, klíčů a vazeb — plus detekci rozdílů
mezi tím, co si EF myslí, a tím, co v databázi opravdu je.

```csharp
builder.Services.AddDbsViewer<AppDbContext>();   // 1
app.MapDbsViewer();                              // 2 → /dbschema
```

> **Stav:** hotové a použitelné. Prohlížečka má grafické UI s ER diagramem, HTTP API,
> detekci rozdílů i náhled dat. Zbývá publikování na nuget.org — do té doby se balíčky
> sestavují ze zdrojů, viz [instalace](#instalace-do-vlastní-aplikace).

---

## Instalace do vlastní aplikace

Tahle kapitola je psaná tak, aby se podle ní dalo postupovat bez znalosti zbytku projektu.

### Předpoklady

| | |
|---|---|
| .NET SDK | 10.0 nebo novější |
| EF Core | 10.x |
| Databáze | Microsoft SQL Server nebo SQLite |
| Typ aplikace | jakákoli na ASP.NET Core — Web API, MVC, Blazor Server i Blazor WASM host |

PostgreSQL a jiné databáze podporované nejsou.

### Krok 1: získej balíčky

Až bude balíček na nuget.org, stačí:

```bash
dotnet add package DbsViewer.Server
```

Do té doby, nebo když chceš nejnovější vývojovou verzi, si balíčky sestav ze zdrojů:

```bash
git clone https://github.com/MichalZem/DBsViewer.git
cd DBsViewer

# Blazor UI se publikuje zvlášť — serverový balíček jeho výstup vkládá do sebe.
# Bez tohoto kroku balení rovnou selže a řekne ti to.
dotnet publish src/DbsViewer.Ui -c Release -o artifacts/ui

# vytvoří .nupkg soubory do artifacts/packages/
for p in Abstractions EfCore Relational SqlServer Sqlite Analysis Server; do
  dotnet pack src/DbsViewer.$p -c Release -o artifacts/packages
done
```

Ve Windows PowerShell:

```powershell
dotnet publish src/DbsViewer.Ui -c Release -o artifacts/ui

'Abstractions','EfCore','Relational','SqlServer','Sqlite','Analysis','Server' | ForEach-Object {
  dotnet pack "src/DbsViewer.$_" -c Release -o artifacts/packages
}
```

Vznikne sedm balíčků. Instalovat budeš **jen `DbsViewer.Server`** — ostatní se přitáhnou
jako jeho závislosti.

### Krok 2: přidej zdroj balíčků do své aplikace

Do složky své aplikace (vedle `.csproj` nebo `.sln`) přidej `NuGet.config`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="dbsviewer-local" value="C:\cesta\k\DBsViewer\artifacts\packages" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

Cestu k `artifacts/packages` uprav podle toho, kam jsi repozitář naklonoval. Musí být
absolutní, nebo relativní vůči umístění `NuGet.config`.

### Krok 3: nainstaluj balíček

```bash
dotnet add package DbsViewer.Server --version 0.1.0-alpha
```

Verze se musí uvést explicitně, protože jde o předběžné vydání.

### Krok 4: zapoj do `Program.cs`

```csharp
using DbsViewer.Server;              // ← přidej tento using

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite("Data Source=app.db"));
builder.Services.AddDbsViewer<AppDbContext>();     // ← 1. řádek

var app = builder.Build();

app.MapDbsViewer();                                // ← 2. řádek

app.Run();
```

Typový parametr `AddDbsViewer<T>` je tvůj `DbContext`. Nic dalšího konfigurovat nemusíš —
providera (SQL Server nebo SQLite) si komponenta zjistí sama a živou introspekci zapne
automaticky.

**Pořadí volání:** `MapDbsViewer()` musí být volané po `app.Build()`. Pokud aplikace používá
`UseAuthentication()` a `UseAuthorization()`, patří `MapDbsViewer()` až za ně.

### Krok 5: ověř

Spusť aplikaci a otevři v prohlížeči:

```
http://localhost:<port>/dbschema
```

Uvidíš prohlížečku: nahoře přepínač mezi přehledem, seznamem tabulek, ER diagramem
a rozdíly. V seznamu je vlevo hledání, vpravo detail se záložkami.

Když chceš ověřit jen API:

```
http://localhost:<port>/dbschema/api/meta
```

Odpověď vypadá takhle:

```json
{
  "title": "Schéma databáze",
  "routePrefix": "/dbschema",
  "views": ["ef", "live", "merged"],
  "canDiff": true,
  "canPreviewData": false,
  "showRowCounts": false,
  "groups": {},
  "dataPreviewMaxRows": 100
}
```

Když se vrátí `404`, aplikace nejspíš neběží ve vývojovém prostředí — ve výchozím nastavení
je prohlížečka dostupná **jen v Development**. Viz [Bezpečnost](#bezpečnost).

---

## Co uvidíš

**Prohlížeč tabulek.** Vlevo seznam s hledáním, které prochází názvy tabulek **i sloupců** —
sloupec je často to jediné, co člověk zná. Vpravo detail se záložkami *Sloupce, Indexy,
Cizí klíče, Odkazuje sem, Data*.

Záložka **Odkazuje sem** je inverzní pohled na cizí klíče a odpovídá na otázku „co se
rozbije, když tuhle tabulku změním". V běžných nástrojích chybí, přitom je při zásahu
do schématu nejužitečnější.

**Přehled** je vstupní obrazovka do cizí databáze: kolik je tabulek, sloupců, vazeb
a indexů, které tabulky jsou největší a nejvíc propojené, jaké typy se používají — a sekce
*Co stojí za pozornost* s tabulkami bez primárního klíče, cizími klíči bez indexu
a tabulkami, které s ničím nesouvisí. Každé jméno je odkaz do detailu.

**ER diagram.** Tabulky jako uzly, vazby jako hrany s popiskem kardinality. Kaskády mají
vlastní barvu — je to to, co člověk v cizím schématu hledá nejčastěji. Nepovinné vazby
jsou přerušované, vazba N:M je jedna hrana místo dvou přes vazební tabulku.

Hrany se vedou kolem tabulek, ne přes ně: trasa se hledá po mřížce v odstupu od okrajů
uzlů a zatáčky se penalizují, takže vyjde co nejpřímější čára, která nikde nemizí pod
tabulkou. Vazby mířící do jedné tabulky mají každá vlastní kotvu, aby se šipky neslily
do jednoho bodu.

Vysvětlivky k typům čar jsou v levém dolním rohu diagramu, sbalené do štítku.

**Focus mode** je zapnutý ve výchozím stavu a je to jediný způsob, jak udělat diagram
se stovkou tabulek čitelný: vyber tabulku a posuvníkem urči, jak daleko od ní se má
kreslit. Nula ukáže jen ji, tři už zachytí širší okolí. Zpátky na celé schéma vede
tlačítko **← Celé schéma** — ve výřezu totiž není z diagramu poznat, že zbytek
schématu vůbec existuje.

Uzly se dají rozbalit na všechny sloupce, plocha se posouvá tažením a přibližuje kolečkem.

**Rozdíly.** Nálezy porovnání modelu s databází, seskupené podle závažnosti. Tabulka
s nálezem se zvýrazní i v seznamu a v diagramu, takže je vidět, kde je problém.

**Data.** Záložka *Data* v detailu tabulky se načte sama, bez klikání. Mřížka umí
stránkovat, řadit kliknutím na hlavičku (vzestupně → sestupně → bez řazení) a filtrovat
políčkem pod každým sloupcem.

Všechno tohle dělá **databáze**, ne prohlížečka: `LIMIT`/`OFFSET` respektive
`OFFSET`/`FETCH`, `ORDER BY` a `WHERE` s parametry. Do paměti se nikdy nenačte víc než
jedna stránka, takže mřížka funguje stejně nad deseti řádky jako nad miliony. Celkový
počet se počítá `COUNT(*)`; když dotaz nedoběhne do časového limitu, stránkuje se dál,
jen bez čísel stránek.

Filtr hledá text kdekoli v hodnotě, i nad čísly a daty. Zástupné znaky `%` a `_` se
escapují — kdo hledá „100%", hledá opravdu „100%".

**Export.** Schéma jde stáhnout jako Mermaid, DBML nebo Markdown dokumentaci —
souborem, který se dá commitnout do repozitáře.

---

## HTTP API

UI všechno volá přes tohle API, takže se dá použít i samostatně.
Všechny cesty jsou relativní k `RoutePrefix` (výchozí `/dbschema`).

| Metoda | Cesta | Vrací |
|---|---|---|
| `GET` | `/` | Grafická prohlížečka (Blazor WebAssembly). |
| `GET` | `/api/meta` | Co je v této konfiguraci k dispozici. Volej jako první. |
| `GET` | `/api/schema` | Celé schéma. Parametr `source=ef\|live\|merged`, `refresh=true` obejde cache. |
| `GET` | `/api/schema/diff` | Rozdíly mezi EF modelem a databází. |
| `GET` | `/api/tables/{schema}/{name}` | Detail jedné tabulky. |
| `POST` | `/api/tables/{schema}/{name}/rows` | Stránka dat. Ve výchozím stavu vrací `403`. |
| `POST` | `/api/refresh` | Zahodí cache. |

**Prázdné schéma se v cestě zapisuje pomlčkou.** SQLite schémata nemá, takže detail tabulky
je `/api/tables/-/Customers`. U SQL Serveru `/api/tables/dbo/Customers`.

**Data chodí POSTem, ne GETem.** Hledaná hodnota je obsah databáze a v adrese by skončila
v historii prohlížeče i v logu serveru. Tělo požadavku:

```json
{
  "page": 0,
  "pageSize": 50,
  "sortColumn": "CreatedAt",
  "sortDescending": true,
  "filters": [
    { "column": "Email", "operator": "Contains", "value": "@firma.cz" }
  ]
}
```

Operátory: `Contains`, `Equals`, `StartsWith`, `EndsWith`, `GreaterThan`, `LessThan`,
`IsNull`, `IsNotNull`. Odpověď nese `rows`, `totalRows`, `pageCount` a `hasMore`.

### Pohledy na schéma

| `source` | Co vrací |
|---|---|
| `ef` | Jen EF model. Zná navigace, CLR typy, komentáře, dědičnost. Nesahá do databáze. |
| `live` | Jen skutečnost v databázi. Zná reálné indexy, defaulty, computed sloupce, počty řádků. |
| `merged` | Obojí sloučené. **Výchozí**, když jsou oba zdroje k dispozici. |

Pravidlo slučování: *databáze má pravdu o tom, co v ní je; model má pravdu o záměru.*
Podrobně v [ADR-0009](docs/adr/0009-databaze-ma-pravdu-o-sobe.md).

### Tvar odpovědi `/api/schema`

```json
{
  "databaseName": "app.db",
  "provider": "Sqlite",
  "sourceKind": "Merged",
  "generatedAtUtc": "2026-08-27T18:42:11+00:00",
  "tables": [
    {
      "name": { "name": "Posts" },
      "columns": [
        {
          "name": "Id",
          "ordinal": 1,
          "storeType": "INTEGER",
          "clrType": "System.Int32",
          "isNullable": false,
          "isPrimaryKey": true,
          "isIdentity": true,
          "propertyNames": ["Id"]
        },
        {
          "name": "BlogId",
          "ordinal": 3,
          "storeType": "INTEGER",
          "clrType": "System.Int32",
          "isNullable": false,
          "isForeignKey": true,
          "propertyNames": ["BlogId"]
        }
      ],
      "primaryKey": { "name": "PK_Posts", "columns": ["Id"] },
      "indexes": [{ "name": "IX_Posts_BlogId", "columns": ["BlogId"], "isUnique": false }],
      "foreignKeys": [
        {
          "name": "FK_Posts_Blogs",
          "columns": ["BlogId"],
          "principalTable": { "name": "Blogs" },
          "principalColumns": ["Id"],
          "deleteBehavior": "Cascade",
          "navigationName": "Blog",
          "inverseNavigationName": "Posts"
        }
      ]
    }
  ],
  "relationships": [
    {
      "id": "fk:Posts|FK_Posts_Blogs",
      "from": { "name": "Posts" },
      "to": { "name": "Blogs" },
      "cardinality": "OneToMany",
      "isRequired": true,
      "isIdentifying": false,
      "fromColumns": ["BlogId"],
      "toColumns": ["Id"],
      "fromNavigation": "Blog"
    }
  ],
  "migrations": [],
  "warnings": []
}
```

Klíčové vlastnosti tvaru:

- **`relationships` není totéž co `foreignKeys`.** Jsou odvozené pro vykreslení: vztah N:M
  přes vazební tabulku je jedna hrana s `cardinality: "ManyToMany"` a vyplněným
  `viaJoinTable`, ne dva cizí klíče. Podrobně v [ADR-0007](docs/adr/0007-vztahy-ne-cizi-klice.md).
- **`cardinality`** nabývá hodnot `OneToOne`, `OneToMany`, `ManyToMany`.
- **`isIdentifying`** znamená, že cizí klíč je součástí primárního klíče.
- **`warnings`** obsahuje texty toho, co se nepodařilo načíst. Načtení schématu nikdy
  nespadne — dílčí selhání skončí tady.
- Enumy se serializují **jako text**, ne jako číslo. Vlastnosti jsou v `camelCase`.
- Prázdné a `null` hodnoty se v odpovědi vynechávají.

### Tvar odpovědi `/api/schema/diff`

```json
{
  "findings": [
    {
      "kind": "IndexMissingInModel",
      "severity": "Warning",
      "table": { "name": "Customers" },
      "object": "IX_Rucne_Pridany",
      "message": "Index je v databázi, ale v modelu není. Ručně doladěný výkon mimo migrace.",
      "databaseValue": "DisplayName"
    }
  ],
  "comparedAtUtc": "2026-08-27T18:42:11+00:00",
  "isClean": false,
  "errorCount": 0,
  "warningCount": 1
}
```

Nálezy jsou seřazené od nejzávažnějších. `severity` je `Error`, `Warning` nebo `Info`.
Druhy nálezů (`kind`) pokrývají chybějící tabulky a sloupce na obou stranách, neshody typů,
nullability, délek, defaultů, klíčů, indexů, cizích klíčů a stavu migrací.

---

## Konfigurace

Vše volitelné. Bez konfigurace platí bezpečné výchozí hodnoty.

```csharp
builder.Services.AddDbsViewer<AppDbContext>(options =>
{
    // Kde prohlížečka běží
    options.RoutePrefix = "/_db";                  // výchozí "/dbschema"
    options.Title = "Eshop — schéma";

    // Kde je dostupná
    options.EnabledIn = HostEnv.Development | HostEnv.Staging;   // výchozí jen Development
    options.RequireAuthorization("DbSchemaAdmins");              // mimo Development povinné

    // Co se čte
    options.IncludeLiveDatabase = true;            // výchozí true
    options.ShowRowCounts = true;                  // výchozí false
    options.CacheFor = TimeSpan.FromMinutes(10);   // výchozí 5 minut, Zero cache vypne

    // Co se zobrazuje
    options.HideTables.Add("__EFMigrationsHistory");
    options.HideTables.Add("AspNetUser*");         // podporuje *
    options.IncludeSchemas.Add("dbo");             // prázdné = všechna schémata
    options.Groups["Sklad"] = "Warehouse*";        // skupiny pro filtr v UI

    // Náhled dat — viz Bezpečnost
    options.DataPreview.Enabled = true;            // výchozí false
    options.DataPreview.MaxRows = 50;              // strop velikosti stránky; výchozí 100, tvrdý strop 1000
    options.DataPreview.CommandTimeoutSeconds = 15; // výchozí 30; chrání hlavně COUNT(*)
    options.DataPreview.MaskColumns.Add("Email");  // výchozí *Password*, *Secret*, *Token*
    options.DataPreview.AllowedTables.Add("Order*");
});
```

### Bez `DbContext`

Pro cizí nebo legacy databázi, ke které nemáš EF model:

```csharp
using DbsViewer.SqlServer;

builder.Services.AddDbsViewer(
    _ => new SqlServerSchemaSource("Server=.;Database=Legacy;Trusted_Connection=True;TrustServerCertificate=True"),
    options => options.Title = "Legacy databáze");
```

Diff ani sloučený pohled v tomhle režimu nejsou — chybí model, se kterým by se porovnávalo.
`/api/meta` to hlásí v poli `views`.

---

## Bezpečnost

Schéma databáze je citlivá informace. Výchozí hodnoty jsou proto restriktivní a některá
pravidla se **nedají obejít konfigurací**. Odůvodnění v [ADR-0006](docs/adr/0006-bezpecnostni-defaulty.md).

| Pravidlo | Chování |
|---|---|
| Dostupnost | Jen v `Development`. Jinde je nutné explicitní `EnabledIn`. |
| Autorizace | Mimo Development je policy **povinná**. Bez ní aplikace **nenastartuje** — `MapDbsViewer()` vyhodí výjimku. |
| Zápis | Žádný endpoint nespouští DDL ani DML. Read-only je vlastnost API, ne přepínač. |
| Náhled dat | Vypnutý. Zapnutí je samostatné rozhodnutí nezávislé na zpřístupnění schématu. |
| Náhled dat v produkci | Aplikace nenastartuje, dokud nenastavíš `DataPreview.AllowInProduction = true`. |
| Uživatelské SQL | Nepřijímá se nikdy. Jméno tabulky se ověřuje proti načtenému schématu a teprve pak escapuje. |
| Audit | Každý náhled dat se loguje na úrovni `Information` — kdo, která tabulka, kolik řádků. |

Pád při startu je záměr. Varování v logu by nikdo nepřečetl včas; výjimka nastane
v nasazovacím pipeline, ne v provozu.

Doporučený provoz: v Development zapnuto úplně, ve Staging schéma a diff za autorizací,
v produkci buď vůbec, nebo jen schéma pro úzkou roli a náhled dat nikdy. Nastavení náhledu
dat drž v konfiguraci prostředí, ne v kódu, aby se nedalo zapnout omylem při nasazení.

---

## Řešení potíží

| Příznak | Příčina a náprava |
|---|---|
| `404` na `/dbschema/api/meta` | Aplikace neběží v `Development`. Nastav `EnabledIn` a autorizační policy, nebo spusť s `ASPNETCORE_ENVIRONMENT=Development`. |
| Výjimka při startu, „autorizační policy" | Prohlížečka je povolená mimo Development bez policy. Zavolej `RequireAuthorization("…")`, nebo omez `EnabledIn` na `HostEnv.Development`. |
| Výjimka při startu, „náhled dat" | Náhled dat v produkci. Buď ho vypni, nebo nastav `DataPreview.AllowInProduction = true`. |
| „není zaregistrovaná čtečka živé databáze" | Provider není SQL Server ani SQLite. Nastav `IncludeLiveDatabase = false`. |
| `views` obsahuje jen `["ef"]` | Živá introspekce se nezapnula. Zkontroluj `IncludeLiveDatabase` a providera. |
| `403` na `/rows` | Náhled dat je vypnutý nebo tabulka není ve whitelistu. |
| Změny v databázi se neprojeví | Cache. Zavolej `POST /api/refresh`, přidej `?refresh=true`, nebo sniž `CacheFor`. |
| Balíček se nenašel | Zkontroluj cestu v `NuGet.config` a že v `artifacts/packages` opravdu jsou `.nupkg` soubory. Po přebalení stejné verze vyčisti cache: `dotnet nuget locals global-packages --clear`. |

---

## Bez aplikace: nástroj z příkazové řádky

Schéma jde vypsat i bez zapojení do aplikace. Nástroj se instaluje jako `dotnet tool`:

```bash
dotnet pack tools/DbsViewer.Dump -c Release -o artifacts/packages
dotnet tool install -g DbsViewer.Tool --version 0.1.0-alpha --add-source ./artifacts/packages
```

Potom:

```bash
# ukázkový model z repozitáře
dbsview

# živá databáze
dbsview --sqlite ./app.db --rows
dbsview --sqlserver "Server=.;Database=Eshop;Trusted_Connection=True"

# drift mezi EF modelem a databází
dbsview --diff ./app.db

# dokumentace schématu do repozitáře
dbsview --sqlite ./app.db --export docs/schema.md --format markdown
dbsview --sqlite ./app.db --export docs/schema.mmd --format mermaid

# JSON na disk
dbsview --json schema.json

# nápověda
dbsview --help
```

Bez instalace jde spustit i přímo ze zdrojů:

```bash
dotnet run --project tools/DbsViewer.Dump -- --help
```

Výpis:

```
Databáze  : main
Provider  : Sqlite (Microsoft.EntityFrameworkCore.Sqlite)
Zdroj     : EF model (ShopContext) [EfModel]
Tabulek   : 10, vztahů: 8

■ OrderLines   [OrderLine]
  PK FK OrderId                  INTEGER            NOT NULL
  PK    LineNumber               INTEGER            NOT NULL
     FK ProductId                INTEGER            NOT NULL
        Total                    TEXT               NOT NULL  computed: "Quantity" * "UnitPrice"
    ⌗ IX_OrderLines_ProductId (ProductId)

■ Payments   [BankTransfer+CardPayment+Payment · TPH:PaymentType]
  PK    Id                       INTEGER            NOT NULL  identity

── Vztahy ──
  1:N  Categories ← Categories  onDelete=Restrict [self]
  1:1  Customers ← CustomerProfiles  onDelete=Cascade [identifying]
  N:M  Tags ← Products via ProductTags  onDelete=Cascade
```

Režim `--diff` vrací **kód 2**, když najde chybu, takže se dá použít jako kontrola v CI:

```yaml
- name: Kontrola driftu databáze
  run: dbsview --diff "${{ secrets.CONNECTION_STRING }}"
```

Ukázka vygenerované dokumentace je v [`docs/schema-ukazka.md`](docs/schema-ukazka.md).

---

## Co komponenta umí

- **Tabulky a pohledy** včetně komentářů, computed sloupců, defaultů, collation, check constraintů
- **Vztahy, ne cizí klíče** — 1:1, 1:N i N:M se sbalenou vazební tabulkou, identifikující
  vztahy, self-reference
- **Celý EF model** — TPH dědičnost, owned types, shadow properties, vlastní schémata
- **Detekci driftu** — neaplikovaná migrace, ručně přidaný index, sloupec navíc,
  `DeleteBehavior`, které se chová jinak než model tvrdí
- **Odolnost** — načtení schématu nikdy nespadne, dílčí selhání skončí ve `warnings`
- **Grafické UI** — přehled databáze, prohlížeč tabulek, ER diagram s focus modem,
  přehled rozdílů, stránkovaná mřížka dat, export do Mermaid, DBML a Markdownu

Zdroje dat:

| Zdroj | Ví navíc |
|---|---|
| **EF Core model** | navigace, N:M přes skip-navigace, CLR typy, `DeleteBehavior`, owned types, TPH |
| **Živá databáze** | skutečné indexy včetně `INCLUDE` a filtrovaných, computed sloupce, defaulty, odhad počtu řádků |

---

## Vývoj

```bash
# V Debugu stačí samotný build. Release potřebuje napřed publikované UI,
# protože serverový balíček ho vkládá do assembly.
dotnet build

cd tests/DbsViewer.EfCore.Tests && dotnet test
cd tests/DbsViewer.Relational.Tests && dotnet test
cd tests/DbsViewer.Server.Tests && dotnet test
cd tests/DbsViewer.Ui.Tests && dotnet test
cd tests/DbsViewer.Tool.Tests && dotnet test
```

Blazor UI se do serverového balíčku vkládá jen v `Release` — v `Debug` by publish UI
zdržoval každý build. Vynutit jde přes `-p:EmbedDbsViewerUi=true`, ale pak musí být UI
publikované do `artifacts/ui`:

```bash
dotnet publish src/DbsViewer.Ui -c Release -o artifacts/ui
dotnet build -c Release
```

Publikuje se samostatným příkazem schválně. Dokud to obstarával MSBuild target, stavěl
`DbsViewer.Ui` souběžně s tím, jak ho stavělo řešení (je v něm kvůli testům), a build
padal na zamčených mezisouborech — na CI spolehlivě, na slabším stroji jen občas.
Když publikované UI chybí, build to řekne rovnou; balíček bez UI nikdy nevznikne.

Testy vynucují **100% pokrytí řádků a metod** — build selže, když pokrytí klesne.
Odůvodnění v [ADR-0005](docs/adr/0005-stoprocentni-pokryti.md). Report končí
v `artifacts/coverage/`.

Architektonická rozhodnutí a jejich důvody jsou v [`docs/adr/`](docs/adr/README.md).
Pravidla pro práci na projektu v [`CLAUDE.md`](CLAUDE.md).

### Struktura

| Projekt | Účel |
|---|---|
| `src/DbsViewer.Abstractions` | Datový model, kontrakty, odvozovací funkce. Bez závislostí. |
| `src/DbsViewer.EfCore` | Čtení schématu z EF modelu |
| `src/DbsViewer.Relational` | Sdílená vrstva živé introspekce |
| `src/DbsViewer.SqlServer` | Introspekce SQL Serveru nad `sys.*` |
| `src/DbsViewer.Sqlite` | Introspekce SQLite nad `PRAGMA` |
| `src/DbsViewer.Analysis` | Slučování a porovnání schémat |
| `src/DbsViewer.Server` | `AddDbsViewer`, `MapDbsViewer`, HTTP API, hosting UI — **tohle se instaluje** |
| `src/DbsViewer.Ui` | Blazor WASM prohlížečka, embedovaná do serverového balíčku |
| `samples/DbsViewer.SampleShop` | Ukázkový model pro testy |
| `tools/DbsViewer.Dump` | `dotnet tool dbsview` — výpis schématu, diff a export |
| `tests/*` | Testy s vynuceným pokrytím |

### Etapy

| # | Etapa | Stav |
|---|---|---|
| 01 | Datový model, čtení z EF modelu | ✅ hotovo |
| 02 | Živá introspekce, slučování, diff engine | ✅ hotovo |
| 03 | `AddDbsViewer` / `MapDbsViewer`, HTTP API, autorizace, náhled dat | ✅ hotovo |
| 04 | Blazor WASM UI, embedding do balíčku | ✅ hotovo |
| 05 | ER diagram, focus mode, export | ✅ hotovo |
| 06 | Diff a náhled dat v UI | ✅ hotovo |
| 07 | `dotnet tool`, statický snapshot | ✅ hotovo |
| — | Publikování na nuget.org | ⬜ |

## Vydávání

Publikaci na NuGet obstarává [GitHub Actions](.github/workflows/build-a-publikace.yml).

| Co uděláš | Co se stane |
|---|---|
| Push na `main` | Kompilace, testy a vydání **předběžné** verze, například `0.2.1-alpha.0.7` |
| `git tag v1.2.3 && git push origin v1.2.3` | Totéž plus vydání **stabilní** verze `1.2.3` a release na GitHubu |
| Pull request | Jen kompilace a testy, nic se nepublikuje |

Verzi určuje **poslední git tag**, ne číslo v souboru — obstarává to
[MinVer](https://github.com/adamralph/minver). Po tagu `v1.2.3` dostane každý další
commit verzi `1.2.4-alpha.0.N`, takže se dvě sestavení nikdy neperou o stejné číslo.

### Co je potřeba nastavit

Publikuje se přes **Trusted Publishing**, takže v repozitáři neleží žádný klíč
k NuGetu. Běh si od GitHubu vyžádá podepsaný OIDC token a vymění ho na nuget.org
za dočasný klíč platný hodinu.

**1. Politika na nuget.org** — přihlaš se, klikni na své jméno → *Trusted Publishing*
a přidej politiku:

| Pole | Hodnota |
|---|---|
| Repository Owner | `MichalZem` |
| Repository | `DBsViewer` |
| Workflow File | `build-a-publikace.yml` |
| Environment | nechat prázdné |

**2. Proměnná v repozitáři** — *Settings → Secrets and variables → Actions → Variables*:

| Proměnná | K čemu | Povinné |
|---|---|---|
| `NUGET_USER` | Uživatelské jméno na nuget.org (**ne e-mail**) | Ano, jinak se publikace přeskočí |

Případně z příkazové řádky: `gh variable set NUGET_USER --body "<jméno>"`.

Volitelně ještě tajemství `TEST_SQL_PASSWORD` (heslo testovacího SQL Serveru
v kontejneru) — bez něj se použije výchozí hodnota.

Bez `NUGET_USER` workflow **neselže** — jen přeskočí publikaci a nechá balíčky
v artefaktech běhu, odkud se dají stáhnout ručně. Stejně se zachová i ve forku,
který na politiku nedosáhne.

### Proč běží na CI SQL Server

Integrační testy potřebují skutečnou databázi: mapování řádků se testuje čtečkou v paměti,
ale spouštění dotazů nad `sys.*` jinak ověřit nejde. Bez nich klesne pokrytí
`DbsViewer.SqlServer` pod práh a build právem selže. Workflow proto spouští
SQL Server v kontejneru; lokálně stačí jakákoli instance a připojení se dá přesměrovat
proměnnou `DBSVIEWER_TEST_SQLSERVER`.

## Licence

MIT
