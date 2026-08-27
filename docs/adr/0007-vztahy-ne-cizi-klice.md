# 0007. Diagram kreslí vztahy, ne cizí klíče

- **Stav:** Přijato
- **Datum:** 2026-08-27

## Kontext

Naivní ER diagram kreslí jednu hranu na každý cizí klíč. U reálného modelu to vede
k nečitelnému výsledku: vztah N:M se zobrazí jako dvě hrany a vazební tabulka uprostřed,
vztah 1:1 se nedá odlišit od 1:N a identifikující vztahy nejsou vidět vůbec.

## Rozhodnutí

`DatabaseSchema` nese vedle `ForeignKeys` samostatnou kolekci `Relationships`, která je
odvozená a určená pro vykreslení. Kardinalita se odvozuje takto:

- **1:1** — cizí klíč, jehož sloupce pokrývá unikátní index.
- **1:N** — běžný cizí klíč. Nepovinná strana se kreslí přerušovaně.
- **N:M** — ze skip-navigací EF. Vazební tabulka se sbalí do jediné hrany a její dva
  cizí klíče se jako samostatné vztahy už nekreslí.

Vazební tabulka se navíc označuje příznakem `IsJoinTable`. U živé databáze, kde skip-navigace
neexistují, se použije heuristika: primární klíč složený výhradně ze sloupců právě dvou
cizích klíčů a žádný další datový sloupec.

Označení tabulky jako vazební a sbalení hrany jsou **dvě různé věci**. Ručně namodelovaná
vazební tabulka bez skip-navigací se označí, ale její hrany zůstanou dvě — sbalit se smí
jen to, co je jako N:M skutečně v modelu.

## Zvažované alternativy

**Kreslit cizí klíče přímo.** Jednodušší, ale u modelu se stovkou tabulek nečitelné.

**Sbalovat každou tabulku, kterou heuristika označí.** Skrylo by tabulky, které vazbu
sice připomínají, ale nesou vlastní data nebo je na ně navázaný kód.

**Odvozovat kardinalitu až v UI.** Rozdělilo by logiku mezi server a klienta a diff engine
by ji musel implementovat podruhé.

## Důsledky

- `DbRelationship` má stabilní `Id`, aby si UI mohlo pamatovat rozložení uzlů a hran.
- Skrytí tabulky odstraní i vztahy, které do ní vedou, ale ne cizí klíče v detailu
  ostatních tabulek — detail má zůstat pravdivý, i když se něco nekreslí.
- Heuristika pro vazební tabulky se dá vypnout přes `SchemaReadOptions.DetectJoinTables`.
