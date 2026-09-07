# 0019. Stav prohlížečky žije v adrese

- **Stav:** Přijato
- **Datum:** 2026-09-07
- **Mění:** ruší poznámku „přechod mezi tabulkami nemá smysl řešit adresou" z `App.razor`

## Kontext

Prohlížečka byla jedna stránka bez routeru: `App.razor` vykresloval `Viewer` a veškerý
stav — panel, vybraná tabulka, záložka, verze schématu, filtry — žil jenom
ve [`ViewerState`](0013-stav-mimo-komponentu.md). Adresy se dotýkala jediná věc, volba
jazyka z [ADR-0018](0018-vicejazycne-ui.md), a i tu četl `Program.cs` jen jednou při startu.

Chovalo se to jako slepá ulička a bolelo to ve třech místech:

- **Tlačítko Zpět vyskočilo z aplikace ven.** Žádný klik nepřidal záznam do historie
  prohlížeče, takže Zpět vedlo na to, co bylo *před* prohlížečkou. Uživatel, který se
  proklikal třemi tabulkami a chtěl se vrátit o krok, přišel o všechno.
- **Odkaz nešlo poslat.** „Koukni na tuhle tabulku" znamenalo popsat cestu slovy.
- **F5 zahodilo stav.** Znovunačtení, které si vynutí i přepnutí jazyka, hodilo člověka
  zpátky na výchozí obrazovku.

## Rozhodnutí

**Stav prohlížečky se promítá do query stringu a zpátky.** Skládá to `ViewerRoute` —
čistá funkce nad řetězcem, testovatelná bez prohlížeče, stejně jako `LanguageChoice`.
Komponenta po každé změně zapíše adresu přes `NavigationManager` a naslouchá
`LocationChanged`, aby Zpět a Vpřed stav obnovily.

**Stav žije v query stringu, ne v cestě.** Prohlížečka se montuje pod libovolný prefix
hostitelské aplikace (`MapDbsViewer("/dbschema")`) a k cestě se tedy nesmí vyjadřovat.
Router by navíc znamenal rozhodovat, co je „stránka", a přitom je celá prohlížečka jedna.

**Navigace přidává záznam do historie, filtry adresu přepisují.** Panel, tabulka, záložka,
verze, základ porovnání a stav diagramu jsou kroky, ke kterým se člověk vrací. Hledání,
skupina a schéma ne — jeden znak v hledání by jinak byl vlastní krok historie a Zpět
by se textem prokousávalo písmenko po písmenku.

**Zapisují se jen hodnoty odlišné od výchozích.** Adresa nedotčené prohlížečky zůstane
čistá; parametrů je dvanáct a vypsat je všechny by z adresy udělalo výpis konfigurace.

**Cizí parametry v adrese zůstávají.** Sdílí to `QueryString.Merge`, kterým jde i volba
jazyka: klik v prohlížečce nesmí uživateli přepnout jazyk zpátky na výchozí.

## Zvažované alternativy

**Blazor `Router` a cesty typu `/table/Orders`.** Vypadá to jako „správný" Blazor, ale
prohlížečka nezná svůj prefix — ten určuje hostitelská aplikace až za běhu. Router by
si navíc vynutil rozdělit jednu obrazovku na stránky, přestože panely sdílejí načtené
schéma i vybranou tabulku, a přepnutí panelu by pak znamenalo znovu složit celý stav.

**Fragment za `#` místo query stringu.** Nešlo by ho poslat serveru a hlavně by kolidoval
s `?lang=`, který v query stringu už je; míchat dvě místa pro stav by znamenalo dva
parsery a dvě pravidla, co má přednost.

**`localStorage`.** Přežije znovunačtení, ale nejde poslat odkazem, potřebuje JavaScript
(projekt ho podle [ADR-0012](0012-vlastni-layout-diagramu.md) používá jen na stahování
souboru) a s historií prohlížeče nemá nic společného — Zpět by pořád vyskakovalo ven.

**Nechat v adrese jen navigaci a filtry vynechat.** Kratší adresa, ale F5 by resetovalo
hledání a odkaz na „tabulky odpovídající *order* v diagramu" by poslat nešlo. Filtry
jsou v adrese, jen nezanášejí historii.

## Důsledky

- Jméno tabulky se z adresy **nerozebírá na schéma a jméno, ale dohledává ve schématu**.
  Tečka může být i uvnitř jména a rozdělení podle ní by z `Order.Items` udělalo schéma
  `Order`. Co ve schématu není, se zahodí — odkaz může být starý.
- **Vlastní zápis adresy vyvolá `LocationChanged` stejně jako tlačítko Zpět.** Komponenta
  si proto pamatuje adresu, kterou zapsala, a tu jednu událost přeskočí. Bez toho by na
  každý klik obnovovala stav z adresy, kterou právě sama složila, a zahodila by přitom
  rozdělanou práci, která v adrese není — načtený náhled dat.
- **Chybějící `source` znamená sloučený pohled, ne „nech, jak je".** Jinak by Zpět
  z pohledu na samotný EF model zdroj nevrátilo: v adrese, kam se člověk vrací, žádný
  zdroj není a nebylo by co nastavit. Když server sloučený pohled nenabízí, opraví si
  volbu sám při načtení schématu.
- **Rozbalené uzly diagramu se do adresy zapisují v abecedním pořadí.** Podle pořadí
  rozbalování by se adresa měnila, aniž by se změnilo, co je vidět, a do historie by
  přibývaly stejné záznamy.
- Změna verze schématu nebo zdroje z adresy znamená **další dotaz na server**. Zpět mezi
  verzemi tedy stojí kolo na síti; cache serveru z [ADR-0010](0010-http-api-a-cache.md)
  to drží v mezích.
- Adresa je veřejná plocha: přibyl v ní seznam jmen tabulek, který v ní dřív nebyl.
  Nic navíc tím ale neuniká — kdo prohlížečku otevře, jména tabulek vidí i tak.
