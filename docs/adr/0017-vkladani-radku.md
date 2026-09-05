# 0017. Zápis do dat včetně vkládání řádků

- **Stav:** Přijato
- **Datum:** 2026-09-05
- **Nahrazuje:** [ADR-0015](0015-editace-radku.md), který vkládání řádků vylučoval

## Kontext

[ADR-0015](0015-editace-radku.md) rozhodl, že prohlížečka umí **změnit hodnoty v existujícím
řádku a řádek smazat** — a výslovně nic víc: „nevkládá nové řádky". Důvodem byla opatrnost
u funkce, kterou se z nástroje „jen se dívám" stane nástroj, kterým jde databázi rozbít.

Používání ukázalo, že ta hranice je vedená na špatném místě. Kdo si při ladění opravuje
příznak v existujícím řádku, potřebuje stejně často založit řádek nový — číselník, testovací
záznam, chybějící vazbu. Bez toho se stejně musí ven do jiného nástroje, takže opatrnost
nikoho neochránila, jen ho obtěžovala.

Zároveň platí, že **`INSERT` je bezpečnější operace než `UPDATE`**. Neadresuje žádný
existující řádek, takže se nemá čím splést; nemůže přepsat cizí data ani sáhnout na víc
řádků, než uživatel čekal. Pravidlo o kompletním primárním klíči, kterým ADR-0015 chrání
úpravy, u vkládání nemá co chránit.

## Rozhodnutí

Prohlížečka umí **změnit hodnoty v existujícím řádku, řádek smazat a nový řádek vložit**.
Pořád nemění strukturu a nespouští hromadné operace.

Pravidla z ADR-0015 platí beze změny pro úpravu a mazání:

**Existující řádek se adresuje výhradně kompletním primárním klíčem.** Tabulka bez klíče,
pohled ani zamaskovaný klíč se neupravují ani nemažou.

**Zápis se musí dotknout právě jednoho řádku.** Nula je chyba, ne úspěch.

**Nemění se sloupce, kterým uživatel nemůže rozumět nebo je nevlastní:** primární klíč,
sloupce generované databází, počítané, binární a zamaskované.

Pro vkládání platí tři pravidla navíc:

**Primární klíč se nevyžaduje, ale smí se vyplnit.** Do tabulky bez klíče se vložit dá —
`INSERT` žádný existující řádek neadresuje. Naopak u tabulky s přirozeným klíčem musí klíč
vyplnit uživatel, takže je to jediný sloupec, který se u nového řádku chová jinak než
u úpravy: tam je zakázaný, tady povinný. Do pohledu se nevkládá.

**Nevyplněný sloupec se do příkazu vůbec nedostane.** Prázdné políčko není prázdný řetězec
ani NULL — je to „ať si databáze doplní své". Vypsat ho jako NULL by přebilo výchozí hodnotu
a u `NOT NULL` sloupce by vložení zbytečně selhalo. Aby to bylo v mřížce vidět, políčko
ukazuje v nápovědě, co se stane, když zůstane prázdné: výchozí hodnotu, nebo `NULL`.

**Zapíná se zvlášť.** `DataPreview.AllowInsert` je vypnutý stejně jako `AllowUpdate`
a `AllowDelete` a bez `DataPreview.Enabled` neznamená nic. Zakládat data a opravovat překlep
v existujícím řádku jsou různě velká oprávnění; jeden společný přepínač by svedl k tomu
zapnout obojí bez přemýšlení. Whitelist `EditableTables` platí i pro vkládání a každé
vložení jde do audit logu se jménem uživatele, tabulkou a jmény sloupců — hodnoty ne.

Rozhodnutí, která ADR-0015 zdůvodnil a která platí dál: zapisuje se přes ADO.NET, ne přes
`DbContext`; hodnota se převádí podle typu v úložišti, ne podle CLR typu; hlášku databáze
uživatel uvidí.

## Zvažované alternativy

**Vkládat jen do tabulek s primárním klíčem**, tedy stejná podmínka jako u úpravy. Bylo by
to konzistentní na pohled, ale chránilo by to před něčím, co u `INSERT` nehrozí. Tabulka
bez klíče je navíc typicky log nebo číselník — přesně to, do čeho člověk potřebuje řádek
doplnit. Cena za zdánlivou konzistenci by byla vyšší než užitek.

**Vyplňovat nevyplněné sloupce jako NULL.** Jednodušší na implementaci a předvídatelnější
v tom, co přesně půjde do databáze. Jenže by to vyřadilo výchozí hodnoty, které jsou právě
u vkládání to nejužitečnější, co schéma nabízí — a `NOT NULL` sloupec s defaultem by nešlo
vložit vůbec.

**Řádek na konci mřížky.** Tam ho člověk čeká — nový záznam patří za stávající. Jenže
stránka má padesát řádků, takže by nový skončil hluboko pod okrajem okna a kliknutí na
*+ Nový řádek* by vypadalo, že se nic nestalo. Přilepit ho ke spodní hraně přes
`position: sticky` nejde: mřížka je v obalu s `overflow-x`, který je scrollportem bez
omezené výšky, takže se sticky svisle neuplatní. Řádek proto stojí nad daty.

**Formulář v samostatném dialogu** místo řádku v mřížce. Vešlo by se do něj víc
a šlo by lépe ukázat nápovědu ke sloupcům. Mřížka ale ukazuje, jak vypadají existující
řádky, a to je při zakládání nového ta nejlepší nápověda; dialog by ji zakryl.

**Společný přepínač pro veškerý zápis.** Míň konfigurace, ale zahodilo by rozlišení mezi
„smí opravit" a „smí zakládat", které se v praxi liší.

## Důsledky

- Mřížka má nad sebou tlačítko *+ Nový řádek*; otevře prázdný řádek nad daty a vloží se
  po potvrzení, stejně jako se ukládá úprava. Po vložení se stránka načte znovu, takže
  se řádek přesune tam, kam podle řazení patří.
- Vkládat jde i tam, kde se upravovat nedá — do tabulky bez primárního klíče. Řádek se pak
  ale už přes prohlížečku nedá opravit ani smazat.
- `RowEditing` zůstává jediným místem s pravidly, jen má napříště dvě sady: jednu pro
  existující řádek (`ReadOnlyReason`) a jednu pro nový (`NewRowReadOnlyReason`). Rozdíl
  mezi nimi je jediný sloupec — primární klíč.
- Zamaskovaný sloupec se u nového řádku nevyplňuje. Když je zároveň `NOT NULL` a bez
  výchozí hodnoty, vložení odmítne databáze a uživatel uvidí proč.
- Souběh se dál neřeší; u vkládání se projeví nanejvýš jako porušený unikátní index,
  což je hláška, které uživatel rozumí.
