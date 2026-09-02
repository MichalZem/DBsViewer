# Architektonická rozhodnutí (ADR)

Každé rozhodnutí, které je drahé změnit, tady má vlastní záznam. Cílem není dokumentovat
kód — ten se čte sám — ale zachytit **proč** je něco tak, jak to je, aby se za rok
nepřepisovalo dokola to samé.

## Kdy založit nový ADR

Když rozhodnutí splňuje aspoň jedno:

- mění veřejné API balíčku nebo tvar `DatabaseSchema`,
- přidává nebo odebírá závislost, projekt či cílový framework,
- volí mezi dvěma přístupy, kde ta druhá varianta byla vážně zvažovaná,
- zavádí bezpečnostní pravidlo nebo omezení,
- ruší nebo nahrazuje starší ADR.

Drobnosti (přejmenování metody, oprava chyby, doplnění testu) ADR nepotřebují.

## Jak

1. Zkopíruj [`template.md`](template.md) jako `NNNN-kratky-nazev.md` s dalším volným číslem.
2. Vyplň kontext, rozhodnutí a důsledky. Piš stručně — jedna stránka stačí.
3. Zapiš řádek do tabulky níže.
4. Nikdy needituj obsah přijatého ADR. Když rozhodnutí přestane platit, přepni jeho stav
   na `Nahrazeno` s odkazem na nový záznam a napiš nový.

## Stavy

| Stav | Význam |
|---|---|
| `Návrh` | Ještě se o tom rozhoduje. |
| `Přijato` | Platí a kód to odráží. |
| `Nahrazeno` | Neplatí, nahrazeno jiným ADR. Zůstává kvůli historii. |

## Seznam

| # | Rozhodnutí | Stav | Datum |
|---|---|---|---|
| [0001](0001-dva-zdroje-schematu.md) | Dva nezávislé zdroje schématu za jedním rozhraním | Přijato | 2026-08-27 |
| [0002](0002-vlastni-sql-misto-scaffolding-api.md) | Vlastní SQL introspekce místo scaffolding API | Přijato | 2026-08-27 |
| [0003](0003-design-time-model.md) | Čtení z design-time modelu, ne z runtime modelu | Přijato | 2026-08-27 |
| [0004](0004-blazor-wasm-embedded.md) | UI jako samostatný Blazor WASM projekt embedovaný do serveru | Přijato | 2026-08-27 |
| [0005](0005-stoprocentni-pokryti.md) | Vynucené 100% pokrytí řádků a metod | Přijato | 2026-08-27 |
| [0006](0006-bezpecnostni-defaulty.md) | Restriktivní bezpečnostní defaulty s pádem při startu | Přijato | 2026-08-27 |
| [0007](0007-vztahy-ne-cizi-klice.md) | Diagram kreslí vztahy, ne cizí klíče | Přijato | 2026-08-27 |
| [0008](0008-sdilena-introspekcni-vrstva.md) | Sdílená introspekční vrstva se surovým modelem | Přijato | 2026-08-27 |
| [0009](0009-databaze-ma-pravdu-o-sobe.md) | Při slučování má databáze pravdu o sobě, model o záměru | Přijato | 2026-08-27 |
| [0010](0010-http-api-a-cache.md) | Cache je singleton, zdroje jsou scoped | Přijato | 2026-08-27 |
| [0011](0011-parovani-podle-sloupcu.md) | Cizí klíče se párují podle sloupců, ne podle jména | Přijato | 2026-08-27 |
| [0012](0012-vlastni-layout-diagramu.md) | Vlastní layout diagramu v C# místo elkjs | Přijato | 2026-08-28 |
| [0013](0013-stav-mimo-komponentu.md) | Stav prohlížečky žije mimo komponentu | Přijato | 2026-08-28 |
| [0014](0014-historie-schematu-z-migraci.md) | Historie schématu ze snapshotů EF migrací | Přijato | 2026-09-02 |
