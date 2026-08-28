# 0008. Sdílená introspekční vrstva se surovým modelem

- **Stav:** Přijato
- **Datum:** 2026-08-27

## Kontext

Živou introspekci potřebují dva providery s velmi odlišným tvarem metadat. SQL Server
má systémové pohledy, ze kterých se dá vše přečíst osmi dotazy. SQLite systémové pohledy
nemá — metadata se získávají příkazy `PRAGMA` po jedné tabulce, takže počet dotazů roste
s velikostí schématu.

Přímočaré řešení, tedy dvě nezávislé implementace `ISchemaSource`, má dvě vady. Kód pro
sestavení `DatabaseSchema` by existoval dvakrát a časem by se rozešel — a diff by pak
hlásil rozdíly, které v databázi nejsou. A testovat by to šlo jen proti běžící databázi.

## Rozhodnutí

Mezi providery a `DatabaseSchema` se vkládá **surový model** (`RawSchema`): plochý,
bez vazeb, v podobě, kterou umí naplnit obojí. Vrstvy jsou tři:

1. **Provider** — SQL dotazy a mapování řádků na surové záznamy.
2. **`LiveSchemaAssembler`** — jedna sdílená čistá funkce, která ze surových dat poskládá
   `DatabaseSchema`. Nesahá do databáze a nezná providera.
3. **`RelationalSchemaSource`** — společná obálka: otevření připojení, úklid, ošetření chyb.

Odvození vztahů a detekce vazebních tabulek jsou v `DbsViewer.Abstractions`
(`RelationshipBuilder`, `JoinTableDetector`) a **sdílí je i zdroj z EF modelu**.

## Zvažované alternativy

**Dvě nezávislé implementace.** Méně vrstev, ale duplikovaná logika sestavení a nevyhnutelný
rozchod obou zdrojů. U komponenty, jejíž hlavní funkcí je porovnávat je, to je vada návrhu.

**Sdílet až na úrovni `DatabaseSchema`.** Assembler by odpadl, ale každý provider by musel
řešit párování sloupců s indexy a klíči sám — tedy přesně tu část, kde se chybuje.

**Surový model jako `DataTable`.** Ušetřilo by typy, ale ztratilo typovou kontrolu
a znečitelnilo mapování.

## Důsledky

- Mapování řádků je čistá funkce nad `DbDataReader`, takže se testuje čtečkou v paměti
  a nevyžaduje běžící databázi.
- **Spouštění dotazů ale otestovat bez databáze nejde.** Integrační testy proti reálnému
  SQL Serveru jsou proto pro dosažení prahu pokrytí nutné — bez nich klesne pokrytí
  `DbsViewer.SqlServer` na necelých 60 %. Na CI se server spouští v kontejneru;
  lokálně se testy bez něj přeskočí a práh pokrytí neprojde.
  Připojení se dá přesměrovat proměnnou `DBSVIEWER_TEST_SQLSERVER`.
- Přidání dalšího providera znamená napsat dotazy a mapování, nic víc.
- Cena je jeden mezityp navíc a jedno kopírování dat, které je proti dotazům do databáze
  zanedbatelné.
