# DbsViewer

Prohlížečka databázového schématu, kterou přidáš do jakékoli EF Core aplikace a bez další
konfigurace dostaneš přehled tabulek, sloupců, indexů, klíčů — a hlavně grafické schéma vazeb.

```csharp
builder.Services.AddDbsViewer<AppDbContext>();
app.MapDbsViewer();          // → /dbschema
```

> **Stav:** rozpracované. Hotové je čtení schématu z EF modelu (etapa 01 ze sedmi).
> Hostování, UI ani diagram zatím nejsou. Přehled etap je na konci.

---

## Co to řeší

Existující ER nástroje umí nakreslit diagram z databáze. DbsViewer čte **dva nezávislé
zdroje najednou** a umí je porovnat:

| Zdroj | Ví navíc |
|---|---|
| **EF Core model** | navigace, N:M přes skip-navigace, CLR typy, `DeleteBehavior`, owned types, TPH dědičnost |
| **Živá databáze** | skutečné indexy včetně `INCLUDE` a filtrovaných, computed sloupce, defaulty, odhad počtu řádků |

Z toho plyne funkce, kterou běžná prohlížečka nemá — **detekce driftu**. Na první pohled
uvidíš neaplikovanou migraci, ručně přidaný index mimo migrace, sloupec navíc v databázi
nebo `DeleteBehavior`, které se v praxi chová jinak, než model tvrdí.

## Vlastnosti

- **Tabulky a pohledy** včetně komentářů, computed sloupců, defaultů, collation a check constraintů
- **Vztahy, ne cizí klíče** — 1:1, 1:N i N:M se sbalenou vazební tabulkou, identifikující
  vztahy a self-reference ([proč](docs/adr/0007-vztahy-ne-cizi-klice.md))
- **Podpora celého EF modelu** — TPH dědičnost, owned types, shadow properties, vlastní schémata
- **Filtrování** tabulek podle jména i schématu pomocí glob vzorů
- **Migrace** rozdělené na nasazené, čekající a osiřelé
- Načtení schématu **nikdy nespadne** — dílčí selhání skončí jako upozornění ve výsledku

Plánované: ER diagram s focus modem, diff engine, read-only náhled dat, export do Mermaid,
DBML a Markdownu.

## Podporované prostředí

| | |
|---|---|
| .NET | 10 |
| EF Core | 10 |
| Databáze | Microsoft SQL Server, SQLite |
| Frontend | Blazor WebAssembly |

PostgreSQL zatím podporovaný není.

---

## Vyzkoušení

Repozitář obsahuje ukázkový model, na kterém jde čtení schématu hned vyzkoušet:

```bash
git clone https://github.com/MichalZem/DBsViewer.git
cd DBsViewer
dotnet run --project tools/DbsViewer.Dump
```

Výpis vypadá takhle:

```
Databáze  : main
Provider  : Sqlite (Microsoft.EntityFrameworkCore.Sqlite)
Zdroj     : EF model (ShopContext) [EfModel]
Tabulek   : 10, vztahů: 8

■ OrderLines   [OrderLine]
  PK FK OrderId                  INTEGER            NOT NULL
  PK    LineNumber               INTEGER            NOT NULL
     FK ProductId                INTEGER            NOT NULL
        Quantity                 INTEGER            NOT NULL
        Total                    TEXT               NOT NULL  computed: "Quantity" * "UnitPrice"
        UnitPrice                TEXT               NOT NULL
    ⌗ IX_OrderLines_ProductId (ProductId)

■ Payments   [BankTransfer+CardPayment+Payment · TPH:PaymentType]
  PK    Id                       INTEGER            NOT NULL  identity
        Amount                   TEXT               NOT NULL
        CardLast4                TEXT               NULL
        Iban                     TEXT               NULL
     FK OrderId                  INTEGER            NOT NULL

── Vztahy ──
  1:N  Categories ← Categories  onDelete=Restrict [self]
  1:1  Customers ← CustomerProfiles  onDelete=Cascade [identifying]
  1:N  Orders ← OrderLines  onDelete=Cascade [identifying]
  N:M  Tags ← Products via ProductTags  onDelete=Cascade
```

Schéma jde vypsat i jako JSON:

```bash
dotnet run --project tools/DbsViewer.Dump -- --json schema.json
```

Nebo proti vlastní SQL Server databázi — connection string nemusí ukazovat na existující
databázi, čtení modelu do ní nesahá:

```bash
dotnet run --project tools/DbsViewer.Dump -- --sqlserver "Server=.;Database=Eshop;Trusted_Connection=True;"
```

## Použití v kódu

```csharp
using DbsViewer.EfCore;

await using var context = new AppDbContext(options);

var source = new EfCoreModelSchemaSource(context);
var schema = await source.ReadAsync(new SchemaReadOptions
{
    HideTables = ["__EFMigrationsHistory", "AspNetUser*"],
    IncludeSchemas = ["dbo", "sales"],
    DetectJoinTables = true,
});

foreach (var table in schema.Tables)
{
    Console.WriteLine($"{table.Qualified} ({table.Columns.Count} sloupců)");
}

foreach (var pending in schema.Migrations.Where(m => m.IsPending))
{
    Console.WriteLine($"Čeká na nasazení: {pending.Id}");
}
```

Výsledek se serializuje připraveným nastavením, aby si server a klient nikdy nerozešly:

```csharp
var json = JsonSerializer.Serialize(schema, DbsViewerJson.Readable);
```

---

## Vývoj

```bash
dotnet build
cd tests/DbsViewer.EfCore.Tests && dotnet test
```

Testy vynucují **100% pokrytí řádků a metod** — build selže, když pokrytí klesne.
Odůvodnění v [ADR-0005](docs/adr/0005-stoprocentni-pokryti.md). Report končí
v `artifacts/coverage/`.

Architektonická rozhodnutí a jejich důvody jsou v [`docs/adr/`](docs/adr/README.md).
Pravidla pro práci na projektu v [`CLAUDE.md`](CLAUDE.md).

### Struktura

| Projekt | Účel |
|---|---|
| `src/DbsViewer.Abstractions` | Datový model a kontrakty, bez závislostí |
| `src/DbsViewer.EfCore` | Čtení schématu z EF modelu |
| `samples/DbsViewer.SampleShop` | Ukázkový model pro testy |
| `tools/DbsViewer.Dump` | Konzolový výpis schématu |
| `tests/DbsViewer.EfCore.Tests` | Testy s vynuceným pokrytím |

### Etapy

| # | Etapa | Stav |
|---|---|---|
| 01 | Datový model, čtení z EF modelu, ověřovací výpis | ✅ hotovo |
| 02 | Živá introspekce SQL Server a SQLite | ⬜ |
| 03 | `AddDbsViewer` / `MapDbsViewer`, endpointy, autorizace | ⬜ |
| 04 | Blazor WASM UI bez diagramu | ⬜ |
| 05 | ER diagram, focus mode, export | ⬜ |
| 06 | Diff engine, read-only náhled dat | ⬜ |
| 07 | NuGet balíčky, `dotnet tool` | ⬜ |

## Licence

MIT
