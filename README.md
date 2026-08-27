# DbsViewer

Prohlížečka databázového schématu, kterou přidáš do jakékoli EF Core aplikace. Dvěma řádky
v `Program.cs` dostaneš přehled tabulek, sloupců, indexů, klíčů a vazeb — plus detekci rozdílů
mezi tím, co si EF myslí, a tím, co v databázi opravdu je.

```csharp
builder.Services.AddDbsViewer<AppDbContext>();   // 1
app.MapDbsViewer();                              // 2 → /dbschema
```

> **Stav:** HTTP API je hotové a funkční. Grafické UI a ER diagram zatím ne — komponenta
> teď vrací JSON. Přehled etap je [na konci](#etapy).

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

Balíčky **zatím nejsou na nuget.org**. Sestav je ze zdrojů:

```bash
git clone https://github.com/MichalZem/DBsViewer.git
cd DBsViewer

# vytvoří .nupkg soubory do artifacts/packages/
for p in Abstractions EfCore Relational SqlServer Sqlite Analysis Server; do
  dotnet pack src/DbsViewer.$p -c Release -o artifacts/packages
done
```

Ve Windows PowerShell:

```powershell
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

Spusť aplikaci a otevři:

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

## HTTP API

Všechny cesty jsou relativní k `RoutePrefix` (výchozí `/dbschema`).

| Metoda | Cesta | Vrací |
|---|---|---|
| `GET` | `/api/meta` | Co je v této konfiguraci k dispozici. Volej jako první. |
| `GET` | `/api/schema` | Celé schéma. Parametr `source=ef\|live\|merged`, `refresh=true` obejde cache. |
| `GET` | `/api/schema/diff` | Rozdíly mezi EF modelem a databází. |
| `GET` | `/api/tables/{schema}/{name}` | Detail jedné tabulky. |
| `GET` | `/api/tables/{schema}/{name}/rows` | Náhled dat. Ve výchozím stavu vrací `403`. |
| `POST` | `/api/refresh` | Zahodí cache. |

**Prázdné schéma se v cestě zapisuje pomlčkou.** SQLite schémata nemá, takže detail tabulky
je `/api/tables/-/Customers`. U SQL Serveru `/api/tables/dbo/Customers`.

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
    options.DataPreview.MaxRows = 50;              // výchozí 100, strop 1000
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

Schéma jde vypsat i bez zapojení do aplikace:

```bash
# ukázkový model z repozitáře
dotnet run --project tools/DbsViewer.Dump

# živá databáze
dotnet run --project tools/DbsViewer.Dump -- --sqlite ./app.db --rows
dotnet run --project tools/DbsViewer.Dump -- --sqlserver "Server=.;Database=Eshop;Trusted_Connection=True"

# drift mezi EF modelem a databází
dotnet run --project tools/DbsViewer.Dump -- --diff ./app.db

# JSON na disk
dotnet run --project tools/DbsViewer.Dump -- --json schema.json

# nápověda
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

Režim `--diff` vrací **kód 2**, když najde chybu, takže se dá použít jako kontrola v CI.

---

## Co komponenta umí

- **Tabulky a pohledy** včetně komentářů, computed sloupců, defaultů, collation, check constraintů
- **Vztahy, ne cizí klíče** — 1:1, 1:N i N:M se sbalenou vazební tabulkou, identifikující
  vztahy, self-reference
- **Celý EF model** — TPH dědičnost, owned types, shadow properties, vlastní schémata
- **Detekci driftu** — neaplikovaná migrace, ručně přidaný index, sloupec navíc,
  `DeleteBehavior`, které se chová jinak než model tvrdí
- **Odolnost** — načtení schématu nikdy nespadne, dílčí selhání skončí ve `warnings`

Zdroje dat:

| Zdroj | Ví navíc |
|---|---|
| **EF Core model** | navigace, N:M přes skip-navigace, CLR typy, `DeleteBehavior`, owned types, TPH |
| **Živá databáze** | skutečné indexy včetně `INCLUDE` a filtrovaných, computed sloupce, defaulty, odhad počtu řádků |

---

## Vývoj

```bash
dotnet build

cd tests/DbsViewer.EfCore.Tests && dotnet test
cd tests/DbsViewer.Relational.Tests && dotnet test
cd tests/DbsViewer.Server.Tests && dotnet test
```

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
| `src/DbsViewer.Server` | `AddDbsViewer`, `MapDbsViewer`, HTTP API — **tohle se instaluje** |
| `samples/DbsViewer.SampleShop` | Ukázkový model pro testy |
| `tools/DbsViewer.Dump` | Výpis schématu, diff a sloučený pohled |
| `tests/*` | Testy s vynuceným pokrytím |

### Etapy

| # | Etapa | Stav |
|---|---|---|
| 01 | Datový model, čtení z EF modelu | ✅ hotovo |
| 02 | Živá introspekce, slučování, diff engine | ✅ hotovo |
| 03 | `AddDbsViewer` / `MapDbsViewer`, HTTP API, autorizace, náhled dat | ✅ hotovo |
| 04 | Blazor WASM UI bez diagramu | ⬜ |
| 05 | ER diagram, focus mode, export | ⬜ |
| 06 | Diff a náhled dat v UI | ⬜ |
| 07 | Publikování na nuget.org, `dotnet tool` | ⬜ |

## Licence

MIT
