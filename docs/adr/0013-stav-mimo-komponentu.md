# 0013. Stav prohlížečky žije mimo komponentu

- **Stav:** Přijato
- **Datum:** 2026-08-28

## Kontext

Prohlížečka má hodně stavu: vybraná tabulka, hledaný text, filtr skupiny a schématu,
zvolený pohled, aktivní záložka, zapnutý focus, jeho vzdálenost, rozbalené uzly diagramu,
načtený diff, chybová hláška, příznak načítání.

Přirozené místo pro něj je pole v komponentě. Jenže test každé vlastnosti pak vyžaduje
vykreslení celé obrazovky, nalezení správného tlačítka a kliknutí — a když se změní
rozvržení, rozbijí se testy, které s rozvržením nemají nic společného.

## Rozhodnutí

Stav je vytažený do samostatné třídy `ViewerState`, kterou komponenta dostává jako parametr.
Komponenta na něj jen sahá, volá server a překresluje se; veškerá odvozená logika
(filtrování, výřez diagramu, nálezy pro tabulku) je metodami stavu.

Stejný přístup platí pro odvozovací funkce: `SchemaFilter`, `SchemaGraph`, `DiagramLayout`
a `SchemaExporter` jsou čisté třídy bez vazby na Blazor.

## Zvažované alternativy

**Stav v komponentě.** Míň typů, ale test každé vlastnosti vyžaduje vykreslení a testy
jsou křehké vůči změnám v HTML.

**Plnohodnotný state management (Fluxor a podobné).** Přidává závislost a hodně ceremonie
za užitek, který je u jedné obrazovky sporný.

**Stav ve službě v DI.** Znemožnilo by dvě nezávislé instance prohlížečky a v testech
by se musel resetovat mezi případy.

## Důsledky

- Většina chování se testuje bez vykreslování; bUnit se používá jen tam, kde jde
  opravdu o vykreslení nebo o obsluhu události.
- `ViewerState` je vystavený jako parametr komponenty, takže si ho test může nastavit
  přímo do potřebného stavu místo klikací sekvence.
- Cena je jeden typ navíc a nutnost pamatovat, že komponenta stav nevlastní.
