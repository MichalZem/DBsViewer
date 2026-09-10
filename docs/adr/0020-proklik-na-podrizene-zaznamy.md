# 0020. Proklik z řádku na podřízené záznamy

- **Stav:** Přijato
- **Datum:** 2026-09-10
- **Navazuje na:** [ADR-0019](0019-stav-v-adrese.md) (stav v adrese),
  [ADR-0011](0011-parovani-podle-sloupcu.md) (párování podle sloupců)

## Kontext

Náhled dat uměl ukázat tabulku, stránkovat ji, řadit a filtrovat. Neuměl ale to, co si
nad daty člověk žádá nejčastěji: *„a co k tomuhle řádku patří?"*

Odpovědět na to šlo jen ručně — najít v seznamu podřízenou tabulku, přepnout na její
data, do políčka filtru opsat hodnotu cizího klíče a doufat, že se ve sloupci nespletl.
Přitom prohlížečka schéma zná: ví, které tabulky na tu současnou ukazují, kterými
sloupci a na které sloupce.

Záložka *Odkazuje sem* na tuhle otázku odpovídala jen na úrovni schématu — které tabulky
tabulku referencují. Skok na konkrétní **řádky** chyběl.

## Rozhodnutí

**U hodnoty, na kterou se odkazuje cizí klíč, visí v mřížce šipka na podřízené záznamy.**
Klik přepne prohlížečku na podřízenou tabulku omezenou právě na tenhle řádek.

**Vychází se z cizích klíčů, ne z `DbRelationship`.** Vztah z [ADR-0007](0007-vztahy-ne-cizi-klice.md)
slučuje N:M do jedné hrany přes vazební tabulku a ta se v něm ztratí — pro skok na data
je přitom potřeba právě ona, i konkrétní sloupce, které se mají porovnat. Odvození je
proto vlastní funkce `ChildLinks.For` v `Abstractions`, vedle `RelationshipBuilder`.

**Vazba na nadřazený řádek je vlastní pojem, ne předvyplněný filtr.** Ukazuje se nad
mřížkou jako štítek, jde zrušit zvlášť, jde na přesnou shodu (`Equals`) a nemíchá se
s filtry, které si uživatel naťuká do hlaviček — ty zůstávají na „obsahuje" a platí
navíc. Kdyby vazba přistála do políček filtrů, uživatel by ji nedopatřením smazal a pak
by nechápal, proč najednou vidí celou tabulku.

**Vazba patří do adresy, vlastní filtry ne.** Proklik je navigace ve smyslu
[ADR-0019](0019-stav-v-adrese.md): Zpět musí vrátit uživatele k nadřazenému řádku
a odkaz na „objednávky tohohle zákazníka" musí jít poslat. Naťukané filtry jsou naproti
tomu hodnoty rozepsané v políčkách, ne krok navigace.

**Skládat se smí i složený klíč.** Šipka se pak kotví u prvního sloupce vazby a do
podmínky jdou všechny sloupce; parametr `link` se v adrese opakuje.

## Zvažované alternativy

**Předvyplnit stávající políčka filtrů.** Nejmíň kódu, ale filtr v mřížce je „obsahuje",
takže `CustomerId` 1 by vytáhlo i 10, 11 a 100. Přepnout kvůli tomu všechna políčka na
přesnou shodu by rozbilo hledání v textových sloupcích, kde je „obsahuje" to správné.

**Ikona ve sloupci akcí místo v buňce.** Sloupec akcí je jeden na řádek, ale odkazů může
z řádku vést víc a z různých sloupců — u složeného klíče i z jiného než z primárního.
Ikona v buňce říká, *která hodnota* se do podmínky pošle; ikona v akcích by to zamlčela.

**Otevřít podřízené záznamy v rozbaleném podřádku pod rodičem.** Hezké u jedné úrovně,
ale mřížka by musela umět vnořené stránkování, řazení a filtrování zvlášť pro každý
rozbalený řádek — a stav toho všeho by se do adresy nevešel.

**Držet vazbu jen ve stavu komponenty.** Nejjednodušší, ale Zpět by z podřízené tabulky
vedlo na tutéž tabulku bez vazby, tedy na úplně jiná data, než jaká uživatel před
chvílí viděl. To je přesně ta slepá ulička, kterou ADR-0019 zrušil.

## Důsledky

- **Šipka se nenabídne, když by vedla do prázdna.** Hodnota `NULL` se cizím klíčem
  nenaváže, zamaskovaný sloupec v mřížce nese masku místo hodnoty a sloupec mimo
  načtenou stránku hodnotu nemá vůbec. Ve všech třech případech se odkaz vynechá,
  místo aby předstíral, že hodnotu zná.
- **Rozepsaný řádek šipku schová.** Klik by odnavigoval pryč a neuložené hodnoty by
  byly ztracené.
- **Víc cílů znamená nabídku.** Jediný cíl se otevře rovnou — nabídka s jednou položkou
  by byla klik navíc bez užitku. Nabídka se zavírá druhým klikem na šipku nebo výběrem;
  na klik mimo by byl potřeba JavaScript, a ten se podle
  [ADR-0012](0012-vlastni-layout-diagramu.md) používá jen na stahování souboru.
- **Změna vazby načte data znovu, ale nechá filtry být.** Skok na jiného rodiče téže
  tabulky nechává mřížku beze změny a zahodit v ní rozepsané filtry by uživatele
  okradlo o práci. Změna tabulky je jiný případ — tam se filtry i řazení ruší, protože
  sloupce jsou jinde.
- **Prázdná vazba musí být sdílená instance prázdného pole.** `ViewerRoute` je záznam
  a kolekce v něm se porovnávají referencí; nový prázdný seznam by způsobil, že dvě
  adresy bez vazby nikdy nevyjdou jako shodné.
- Do adresy přibyla hodnota z **dat**, ne jen ze schématu. Je to táž hodnota, kterou
  má uživatel před sebou v mřížce, ale na rozdíl od jmen tabulek se může objevit
  v historii prohlížeče a v odkazu, který někam pošle.
- Server se neměnil: `Equals` operátor v `DataQuery` existoval a jména sloupců se pořád
  ověřují proti načtenému schématu, takže i vazba z ručně upravené adresy projde
  stejnou kontrolou jako všechno ostatní.
