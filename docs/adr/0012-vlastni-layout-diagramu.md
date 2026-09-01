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

Hrany vede vlastní ortogonální router (`EdgeRouter`). První verze kreslila trasu tvarem
„půl cesty vodorovně, pak svisle", což u vazeb přes několik vrstev vedlo čáru rovnou přes
tabulky, které jí stály v cestě. Router místo toho hledá cestu po mřížce vedené v odstupu
kolem okrajů tabulek: každý úsek se ověří proti překážkám a zatáčky se penalizují, takže
vyjde trasa s nejmenším počtem ohybů, která nikde neprochází uzlem. Mřížka se staví jen
z okolí obou konců — u stovek tabulek by celoplošná byla pomalejší, než je pro
překreslování únosné — a když cesta nevyjde, vrátí se náhradní trasa, aby hrana nezmizela.

Kotvy hran se rozprostírají po okrajích uzlů podle toho, kde leží protější tabulka.
Bez toho vycházely všechny vazby ze středu a u tabulky s několika vazbami se šipky slily
do jednoho bodu. Uzel kvůli tomu umí povyrůst, aby se mezi kotvy vešly popisky
kardinality; ty se navíc po výpočtu rozsunou, pokud by se přesto překrývaly.

Křížení a souběh čar se řeší dvěma způsoby. Pořadí tabulek uvnitř vrstvy se přepočítá
**barycentrem** — uzel se posune na průměrnou pozici svých sousedů v sousední vrstvě,
několik průchodů tam a zpět. Abecední pořadí je předvídatelné, ale o vazby se nestará
a čáry se pak kříží jen kvůli tomu, jak se tabulky jmenují. Optimální řešení je NP-těžké,
barycentrum ubere podstatnou část za jeden průchod.

Router navíc zná už vedené hrany a připočítává si za ně cenu: za **souběh** hodně (dvě
čáry na téže lince splynou v jednu a diagram tím lže o počtu vazeb), za **křížení** míň
(je čitelné a v hustším schématu se mu vyhnout nedá). Aby bylo kam uhnout, přidá se
do mřížky volný pruh vedle každé hotové hrany. Hrany se vedou od nejkratší — ty mají
nejmíň možností a dlouhé se jim pak přizpůsobí.

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
- Trasa se kreslí jako `path` se zaoblenými rohy, ne jako `polyline`. Ostré pravoúhlé
  zlomy působily tvrdě a splývaly s okraji tabulek. Hrot šipky má `markerUnits`
  na `userSpaceOnUse`, takže se nezvětšuje spolu s tloušťkou čáry — u zvýrazněné hrany
  jinak narostl do nepoměru.
- Layout se testuje jako čistá funkce: vrstvy, kolize uzlů, trasy hran i smyčky
  u self-reference.
- Kvalita rozvržení je horší než u elkjs, hlavně u hustých grafů. Zmírňuje to focus mode,
  který je primárním způsobem práce s diagramem — celoschéma je přehledová mapa.
- Router je nejdražší část vykreslení: hledá cestu pro každou hranu zvlášť. U diagramů,
  kde by se to projevilo, se mřížka omezí a trasa spadne na náhradní — diagram zůstane
  použitelný, jen v tom místě nebude ideální.
- Pan a zoom řeší jedna transformační matice na kořenové skupině SVG, tedy taky bez JS.
