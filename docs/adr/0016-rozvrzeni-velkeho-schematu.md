# 0016. Rozvržení velkého schématu: bloky, zalomení vrstev a mřížka

- **Stav:** Přijato
- **Datum:** 2026-09-05
- **Mění:** upřesňuje [ADR-0012](0012-vlastni-layout-diagramu.md), rozhodnutí o vrstveném layoutu platí dál

## Kontext

Vrstvený layout z [ADR-0012](0012-vlastni-layout-diagramu.md) dává tabulce vrstvu podle
toho, jak dlouhý řetěz cizích klíčů před ní stojí. Počet sloupců diagramu se tím odvíjí
od **hloubky** schématu, ne od jeho velikosti.

Skutečná aplikační schémata jsou přitom mělká a široká. U databáze s třiceti tabulkami,
kde nejdelší řetěz vazeb má tři patra, vyjde diagram jako pruh tří sloupců a dvaceti
řádků — zhruba 950 × 2700 px. Přehledová mapa, která se nedá přehlédnout, protože se
z ní vejde na obrazovku pětina.

Přispívají k tomu tři věci:

1. **Vrstva 0 je odkladiště.** Padne do ní všechno, co nemá odchozí cizí klíč — včetně
   tabulek nastavení a číselníků, které nemají vazbu vůbec žádnou. Každá ukrojí řádek
   výšky a o schématu neřekne nic.
2. **Nesouvislé části se prokládají.** Tabulky přihlašování nemají s fakturami společnou
   jedinou vazbu, ale sdílejí s nimi tytéž tři sloupce. Jejich čáry se pak vedou přes celý
   obrázek kolem cizích tabulek.
3. **Vrstva nemá strop.** Padesát tabulek v jedné vrstvě je sloupec dlouhý několik
   obrazovek, i kdyby vedle něj bylo prázdno.

## Rozhodnutí

Vrstvení zůstává jádrem — v souvislé části schématu pořád platí, že odkazovaná tabulka
stojí vlevo a závislá vpravo. Nad ním ale rozhodují tři kroky, které srovnávají rozměry:

**Nesouvislé části se rozvrhnou zvlášť a poskládají vedle sebe.** Graf se rozpadne na
souvislé komponenty, každá projde vrstvením samostatně a výsledné bloky se poskládají do
řádků. Mezi bloky z definice nevede hrana, takže se skládáním nic nerozbije a dlouhé
přeběhy přes celý diagram zmizí.

**Šířku řádku nevolí konstanta, ale nejlepší poměr stran.** Blok je nedělitelný: při pevné
šířce o kousek menší, než jsou dva bloky vedle sebe, zbude na řádku jeden a diagram se
protáhne do výšky — přesně to, čemu se má skládání vyhnout. Zkusí se proto všechny šířky,
na kterých nějaký blok končí, a vyhraje ta, po které je výsledek nejblíž poměru 1,6 : 1.

**Přeplněná vrstva se zalomí do podsloupců.** Když je vrstva vyšší než cílová výška plochy,
rozdělí se stejně jako text — pořadí zůstává, jen pokračuje o sloupec vedle. Podsloupce
stojí blíž u sebe než vrstvy, protože mezi nimi žádná čára nevede.

**Tabulky bez jediné vazby jdou do mřížky** na konec. Ve sloupci zabírají místo a nic
neříkají; v mřížce se čtou po řádcích jako seznam. Vazební tabulka sbalené N:M vazby do
mřížky **nepatří**, i když vlastní hranu nemá — vazba se jen nakreslila mezi jejími konci,
takže se zařadí ke komponentě, kterou spojuje.

Cílové rozměry plochy se odvozují z celkové plochy uzlů, ne z velikosti okna. Layout musí
vyjít pokaždé stejně; kdyby se odvíjel od viewportu, diagram by se po zvětšení okna
přeskládal a snímky v dokumentaci by neseděly.

**O tom, kam která tabulka patří, rozhodují sbalené výšky.** Rozbalený uzel je vyšší,
takže kdyby jeho výška vstupovala do pořadí bloků, do zalomení vrstvy nebo do volby šířky
řádku, kliknutí na „+" by přeskládalo celý diagram a uživatel by musel hledat, kam se mu
zbytek schématu odskákal. Skutečné výšky se proto uplatní až při skládání uzlů pod sebe:
rozbalený uzel roztlačí svůj sloupec a to, co je pod ním, ale nikdy nezmění, kdo kde stojí.

## Zvažované alternativy

**Nechat to na focus modu.** Focus nad deseti tabulkami funguje dobře a je hlavním
způsobem práce s diagramem. Celoschéma je ale přehledová mapa — když se nedá přehlédnout,
chybí místo, ze kterého se do focusu vstupuje.

**Rozvržení podle šířky okna.** Dalo by lepší využití plochy, ale layout by přestal být
funkcí schématu: jiné okno, jiný diagram. Rozpadly by se testy i snímky v README a uživatel
by přišel o to, že se diagram po návratu na kartu tváří stejně.

**Skládání bloků do obdélníků (bin packing).** Těsnější než skládání do řádků, ale bloky
by ztratily společnou horní hranu a diagram by se přestal číst po pásech. U schématu,
kde jsou bloky výškově podobné, je rozdíl malý.

**Vlastní vrstva pro tabulky bez vazeb místo mřížky.** Vypadalo by to spořádaněji, ale byl
by to jen užší pruh téhož problému — svisle by rostl stejně.

## Důsledky

- U schématu z třiceti tabulek vyjde diagram zhruba 1810 × 1130 px místo 950 × 2700 px,
  tedy se vejde na obrazovku i s okrajem na zoom.
- Sloupců přibylo: jejich počet se odvíjí od počtu tabulek, ne od hloubky vazeb.
- Bloky se čtou po tématech — přihlašování, faktury, náklady — protože je tak dělí sám graf.
- Řádek bloků je vysoký jako jeho nejvyšší blok, takže vedle nižších zůstane prázdno.
  Je to cena za to, že bloky drží společnou horní hranu.
- Rozbalení uzlu posouvá jen svisle, a jen to, co leží pod ním. Vodorovně se nehne nic.
- `DiagramNode.Layer` dál nese číslo vrstvy uvnitř své komponenty, ne globální sloupec.
  Dvě tabulky se stejnou vrstvou už nemusí mít stejné X — tabulky z různých bloků ho
  nesdílejí a zalomená vrstva má podsloupců několik.
- Odhad strany, na které tabulka vystupuje (počítá se z něj výška uzlu kvůli kotvám), se
  po zalomení vrstvy může od skutečnosti lišit. Plyne z něj jen výška, ne trasa, takže
  nejhůř zbude pár pixelů místa navíc.
