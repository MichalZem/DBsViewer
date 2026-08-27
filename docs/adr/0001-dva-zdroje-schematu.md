# 0001. Dva nezávislé zdroje schématu za jedním rozhraním

- **Stav:** Přijato
- **Datum:** 2026-08-27

## Kontext

Popis databáze se dá získat ze dvou míst a každé ví něco, co to druhé neví.

EF Core model zná navigace, skip-navigace pro N:M, CLR typy, `DeleteBehavior`, owned types
a dědičnost. Nezná ale realitu v databázi — nevidí ručně přidaný index, sloupec navíc ani to,
že migrace nebyla nasazená.

Živá introspekce zná skutečné indexy včetně `INCLUDE` a filtrovaných, computed sloupce,
defaulty, triggery a odhad počtu řádků. Nezná navigace ani záměr modelu.

Cílem komponenty není jen kreslit ER diagram, ale ukázat **drift** mezi tím, co si EF myslí,
a tím, co v databázi opravdu je.

## Rozhodnutí

Zavádí se rozhraní `ISchemaSource`, které vrací `DatabaseSchema` bez ohledu na původ dat.
Implementace jsou nejméně dvě: `EfCoreModelSchemaSource` nad `DbContext.Model` a živá
introspekce pro každý provider. Diff engine pak porovnává dvě instance stejného typu
a o EF ani o SQL neví nic.

## Zvažované alternativy

**Jen EF model.** Nejlevnější a provider-agnostické, ale neumí diff — a právě diff je to,
co komponentu odlišuje od desítky existujících ER prohlížeček.

**Jen živá introspekce.** Funguje i pro databáze bez EF, ale ztrácí navigace a CLR typy,
takže diagram by kreslil cizí klíče místo vztahů (viz [0007](0007-vztahy-ne-cizi-klice.md)).

**Jeden zdroj, který obojí míchá uvnitř.** Zdánlivě jednodušší, ale znemožňuje porovnání —
sloučená data už nevědí, odkud který údaj přišel.

## Důsledky

- Živá introspekce je součástí v1, ne pozdější fáze — bez ní není diff.
- `DatabaseSchema` musí být plnitelný z obou stran. Pole, které umí jen jeden zdroj
  (například `RowCountEstimate`), je nullable a druhý zdroj ho nechá prázdné.
- Přibývá povinnost udržet oba zdroje ve shodě v pojmenování a normalizaci — proto
  `DbObjectName` porovnává bez ohledu na velikost písmen.
