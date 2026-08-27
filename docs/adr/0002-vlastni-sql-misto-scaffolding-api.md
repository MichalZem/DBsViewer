# 0002. Vlastní SQL introspekce místo scaffolding API

- **Stav:** Přijato
- **Datum:** 2026-08-27

## Kontext

Živou introspekci databáze už EF Core umí — pohání jí `dotnet ef dbcontext scaffold`
přes `IDatabaseModelFactory`. Nabízí se ji použít a ušetřit si psaní SQL.

Implementace ale žijí v namespace končícím `.Scaffolding.Internal`, jsou označené jako
interní a jejich tvar se mezi verzemi EF mění. Balíček by si navíc musel táhnout
`Microsoft.EntityFrameworkCore.Design`.

## Rozhodnutí

Živá introspekce se píše vlastní: dotazy nad `sys.tables`, `sys.columns`, `sys.indexes`,
`sys.foreign_keys` a `sys.extended_properties` pro SQL Server, `PRAGMA table_info`,
`index_list`, `index_info` a `foreign_key_list` pro SQLite.

## Zvažované alternativy

**`IDatabaseModelFactory` ze scaffoldingu.** Ušetří zhruba 150 řádků SQL na providera,
ale zavazuje k internímu API. Upgrade EF by mohl komponentu rozbít bez varování,
a to je u knihovny, která má jít do každého projektu, nepřijatelné riziko.

**Hotová knihovna třetí strany.** Žádná nepokrývá oba providery způsobem, který by
odpovídal tvaru `DatabaseSchema`, a přidávala by závislost na cizím vydavatelském cyklu.

## Důsledky

- Přibývá vlastní SQL, které se musí testovat proti reálné databázi, ne jen proti modelu.
- Získává se plná kontrola nad tím, co se čte — například odhad řádků ze statistik
  místo drahého `COUNT(*)`.
- Přidání dalšího providera znamená napsat novou třídu, ne jen zaregistrovat balíček.
  PostgreSQL zatím záměrně není v plánu.
