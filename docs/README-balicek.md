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
app.MapDbsViewer();                              // prohlížečka na /dbschema
```

Ostatní balíčky `DbsViewer.*` se přitáhnou jako závislosti — instalovat je zvlášť
není potřeba.

![ER diagram ukázkového schématu](https://raw.githubusercontent.com/MichalZem/DBsViewer/main/docs/obrazky/diagram.png)

## Co to umí

- **Schéma z EF modelu i z živé databáze**, včetně porovnání obou (drift mezi modelem
  a skutečností)
- **ER diagram** s pan &amp; zoom a focus modem, kreslený v Blazoru bez JavaScriptu
- **Tabulky, sloupce, indexy, klíče**, computed sloupce, defaulty, collation, komentáře
- **Historie schématu** z EF migrací — pohled na to, jak schéma vypadalo dřív
- **Náhled dat** se stránkováním, řazením a filtrováním v databázi
- **Export dokumentace** do Markdownu, Mermaidu a DBML
- **`dotnet tool dbsview`** pro výpis a diff z příkazové řádky

Podporované databáze: **Microsoft SQL Server** a **SQLite**.

## Bezpečnost

Mimo `Development` se komponenta bez nastavené autorizace **vůbec nespustí** — schéma
databáze je citlivá informace. Náhled dat je ve výchozím stavu vypnutý.

## Dokumentace

Kompletní návod na instalaci, konfiguraci, zabezpečení a rozšíření je v
[README na GitHubu](https://github.com/MichalZem/DBsViewer/blob/main/README.cs.md).
Architektonická rozhodnutí a jejich důvody v
[docs/adr](https://github.com/MichalZem/DBsViewer/tree/main/docs/adr).

*English:* the full guide to installation, configuration and security is in the
[README on GitHub](https://github.com/MichalZem/DBsViewer#readme). The viewer's user
interface and the project documentation are in Czech.

## Licence

MIT
