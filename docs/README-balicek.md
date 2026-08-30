# DbsViewer

Zabudovatelná prohlížečka databázového schématu pro EF Core aplikace. Přidáš balíček,
dva řádky do `Program.cs` a dostaneš prohlížeč tabulek, sloupců, indexů, klíčů
a grafický ER diagram vazeb.

## Instalace

```bash
dotnet add package DbsViewer.Server
```

```csharp
builder.Services.AddDbsViewer<AppDbContext>();   // registrace
app.MapDbsViewer();                              // prohlížečka na /dbsviewer
```

Ostatní balíčky `DbsViewer.*` se přitáhnou jako závislosti — instalovat je zvlášť
není potřeba.

## Co to umí

- **Schéma z EF modelu i z živé databáze**, včetně porovnání obou (drift mezi modelem
  a skutečností)
- **ER diagram** s pan &amp; zoom a focus modem, kreslený v Blazoru bez JavaScriptu
- **Tabulky, sloupce, indexy, klíče**, computed sloupce, defaulty, collation, komentáře
- **Náhled dat** a export dokumentace do Markdownu
- **`dotnet tool dbsview`** pro výpis a diff z příkazové řádky

Podporované databáze: **Microsoft SQL Server** a **SQLite**.

## Bezpečnost

Mimo `Development` se komponenta bez nastavené autorizace **vůbec nespustí** — schéma
databáze je citlivá informace. Náhled dat je ve výchozím stavu vypnutý.

## Dokumentace

Kompletní návod na instalaci, konfiguraci, zabezpečení a rozšíření je v
[README na GitHubu](https://github.com/MichalZem/DBsViewer#readme).
Architektonická rozhodnutí a jejich důvody v
[docs/adr](https://github.com/MichalZem/DBsViewer/tree/main/docs/adr).

## Licence

MIT
