# 0012. Vlastní layout diagramu v C# místo elkjs

- **Stav:** Přijato
- **Datum:** 2026-08-28
- **Mění:** upřesňuje [ADR-0004](0004-blazor-wasm-embedded.md), kde byl elkjs uvedený jako předpoklad

## Kontext

Původní návrh počítal s knihovnou **elkjs** pro rozvržení diagramu přes JS interop.
Je to nejlepší volně dostupný layout engine pro grafy a pro ER diagram dává hezčí výsledky
než jednoduchý algoritmus.

Při stavbě se ale ukázaly tři věci, se kterými návrh nepočítal:

1. elkjs je zhruba 1,5 MB JavaScriptu, který se musí přibalit vedle už tak nemalého
   WASM runtime.
2. Layout přes JS interop je asynchronní, takže komponenta musí řešit stav „počítá se"
   a překreslování — což je práce navíc v místě, kde už tak je nejvíc stavu.
3. **Layout se nedá otestovat**, protože v testech komponent žádný JavaScript neběží.
   Při vynuceném 100% pokrytí by to znamenalo buď výjimku z pravidla, nebo mockování
   celého layoutu — a testovaly by se pak jen mocky.

## Rozhodnutí

Layout se počítá v C#: vrstvený algoritmus, kde je tabulka o vrstvu vpravo od všech,
na které odkazuje. Vazby tak přirozeně směřují doleva, což u ER diagramu odpovídá tomu,
jak lidé schéma čtou.

Cykly se přerušují omezením počtu průchodů na počet tabulek — u vzájemně se odkazujících
tabulek by rekurze skončila přetečením zásobníku.

Výsledkem je `DiagramLayoutResult` s uzly a trasami hran. **Tvar výsledku je nezávislý
na tom, kdo ho spočítal**, takže se elkjs dá doplnit později jako alternativní
implementace, aniž by se měnily komponenty.

## Zvažované alternativy

**elkjs přes JS interop.** Hezčí rozvržení, ale netestovatelné a s vyšší cenou v balíčku.
U diagramu, kde je hlavním způsobem práce focus mode nad deseti tabulkami, není rozdíl
v kvalitě rozvržení zásadní.

**Force-directed layout v C#.** Vypadá dobře u malých grafů, ale je nedeterministický,
takže by se diagram při každém načtení lišil a testovat by se dal jen přes vlastnosti,
ne přes očekávaný výsledek.

**Bez layoutu, tabulky v mřížce.** Nejlevnější, ale hrany by se křížily napříč celým
diagramem a orientace v něm by byla horší než v seznamu.

## Důsledky

- Diagram funguje bez jediného řádku JavaScriptu. Jediné JS volání v celém UI je
  stahování souboru při exportu.
- Layout se testuje jako čistá funkce: vrstvy, kolize uzlů, trasy hran i smyčky
  u self-reference.
- Kvalita rozvržení je horší než u elkjs, hlavně u hustých grafů. Zmírňuje to focus mode,
  který je primárním způsobem práce s diagramem — celoschéma je přehledová mapa.
- Pan a zoom řeší jedna transformační matice na kořenové skupině SVG, tedy taky bez JS.
