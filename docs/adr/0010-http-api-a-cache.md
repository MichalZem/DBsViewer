# 0010. Cache je singleton, zdroje jsou scoped

- **Stav:** Přijato
- **Datum:** 2026-08-27

## Kontext

`SchemaProvider` závisí na `ISchemaSource`, a ten u zdroje z EF modelu závisí na `DbContext`.
Kontext je v ASP.NET Core registrovaný jako scoped, takže scoped musí být i všechno nad ním.

První verze držela cache uvnitř `SchemaProvider`. Fungovalo to v jednotkových testech,
ale integrační test odhalil, že cache zaniká s každým požadavkem — introspekce databáze
tedy běžela při každém načtení stránky znovu.

## Rozhodnutí

Cache se vytáhla do samostatné třídy `SchemaCache` registrované jako **singleton**.
`SchemaProvider` zůstává scoped a cache si nechává předat.

`SchemaCache` drží zámek, takže souběžné požadavky na totéž schéma spustí introspekci jednou
a ostatní počkají na výsledek.

## Zvažované alternativy

**`IMemoryCache`.** Přidává závislost a vlastní politiku vypršení, kterou by stejně bylo
potřeba přenastavit. Vlastní třída je kratší a testuje se posunem `TimeProvider`.

**Cache ve statickém poli.** Znemožnilo by běh dvou různě nakonfigurovaných instancí
v jedné aplikaci a rozbilo by testy, které běží paralelně.

**Bez cache.** Introspekce SQLite dělá dotaz na tabulku, takže u většího schématu jsou
to stovky dotazů. Při každém načtení stránky je to neúnosné.

## Důsledky

- Čas se čte přes `TimeProvider`, takže vypršení cache jde otestovat bez čekání.
- Cache je společná pro všechny uživatele. To je v pořádku — schéma není závislé na tom,
  kdo se ptá, a autorizace se řeší na úrovni endpointu.
- `POST /api/refresh` a parametr `?refresh=true` dávají uživateli způsob, jak si vynutit
  aktuální stav, aniž by se musela snižovat doba platnosti.
