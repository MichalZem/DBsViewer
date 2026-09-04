# 0015. Zápis do dat jen po jednom řádku a jen podle primárního klíče

- **Stav:** Přijato
- **Datum:** 2026-09-04

## Kontext

Prohlížečka uměla data jenom číst. Při ladění nad vývojovou databází ale člověk potřebuje
drobnost opravit — přepnout příznak, smazat řádek, který vznikl omylem — a musel kvůli
tomu ven do jiného nástroje. Zároveň je zápis přesně ta funkce, kterou se z nástroje
„jen se dívám" stane nástroj, kterým jde databázi rozbít.

Platilo přitom, co už bylo rozhodnuté dřív: uživatelské SQL se nepřijímá nikdy
([ADR-0002](0002-vlastni-sql-misto-scaffolding-api.md) a `DataQueryBuilder`),
bezpečnostní defaulty jsou restriktivní a nesplněná podmínka shodí aplikaci při startu
([ADR-0006](0006-bezpecnostni-defaulty.md)).

## Rozhodnutí

Prohlížečka umí **změnit hodnoty v existujícím řádku a řádek smazat**. Nic víc: nevkládá
nové řádky, nemění strukturu a nespouští hromadné operace.

Platí čtyři pravidla, každé vynucené v kódu:

**Řádek se adresuje výhradně kompletním primárním klíčem.** `WHERE` se skládá jen z jeho
sloupců, a když tabulka klíč nemá, je to pohled, nebo je klíč zamaskovaný, zápis se odmítne.
Rozhoduje o tom `RowEditing` v `Abstractions` — stejný kód používá server i UI, aby
prohlížečka nenabízela to, co server odmítne.

**Zápis se musí dotknout právě jednoho řádku.** Nula znamená, že řádek mezitím zmizel nebo
se změnil jeho klíč; hlásí se to jako chyba, ne jako úspěch.

**Nemění se sloupce, kterým uživatel nemůže rozumět nebo je nevlastní:** primární klíč
(je to identita řádku, ne hodnota), sloupce generované databází, počítané sloupce, binární
sloupce (v mřížce je z nich vidět jen velikost) a zamaskované sloupce (uživatel jejich
hodnotu nevidí, takže by přepisoval naslepo).

**Zapíná se zvlášť od čtení.** `DataPreview.AllowUpdate` a `AllowDelete` jsou vypnuté a bez
zapnutého `DataPreview.Enabled` neznamenají nic. Nad rámec whitelistu tabulek pro čtení je
ještě `DataPreview.EditableTables` — dá se tak povolit prohlížení všeho a zápis do jediné
tabulky. Každý zápis jde do audit logu se jménem uživatele, tabulkou a jmény sloupců;
hodnoty se nelogují, protože obsah databáze do logu nepatří.

Hodnota se z požadavku převádí na parametr podle **typu v úložišti**, ne podle CLR typu
z modelu: zapisuje se přes ADO.NET přímo do sloupce, ne přes EF. Neznámý typ jde jako
řetězec a ať se s ním popere databáze.

## Zvažované alternativy

**Zápis přes `DbContext`.** Vypadá bezpečněji — EF zná typy, konverze i validace. Jenže
prohlížečka umí ukázat i tabulku, která v modelu vůbec není (drift, tabulka po ruce vedle
EF), a nad tou by `DbContext` neuměl nic. Model taky nemusí sedět na databázi; zápis by pak
selhal jinak, než by odpovídalo tomu, co je v mřížce vidět. A `SaveChanges` nad cizí entitou
umí rozjet kaskády, které uživatel v mřížce nevidí.

**Adresovat řádek podle všech jeho hodnot** (`WHERE` přes celý původní obsah). Zvládlo by
i tabulky bez klíče a rovnou by řešilo souběh. Jenže hodnoty jdou do mřížky jako text —
datum přeformátované, `float` zaokrouhlený, binární data zkrácená na velikost. `WHERE` nad
takovým textem by u půlky sloupců netrefil nic, a co hůř, u druhé půlky by mohl trefit víc
řádků, než uživatel označil.

**Optimistický zámek přes původní hodnoty měněných sloupců.** Chránilo by před přepsáním
cizí změny. Naráží ale na totéž přeformátování, a hlavně: nástroj se používá nad vývojovou
databází, kde je souběh výjimkou. Cena za jistotu (tichá selhání u dat, která se textem
neporovnají) by převážila užitek. Pokud se ukáže potřeba, přidá se to jako volba, ne jako
výchozí chování.

**Vlastní `AllowInProduction` pro zápis.** Pojistka proti produkci už existuje pro celý
náhled dat a zápis bez náhledu nejde zapnout. Druhý přepínač na totéž by jen svedl k tomu,
zapnout oba naráz bez přemýšlení.

## Důsledky

Prohlížečka přestává být čistě čtecí nástroj. Zavazujeme se tím k tomu, že:

- každá nová zápisová operace projde stejnou branou — ověření proti schématu, kompletní
  klíč, jeden řádek, audit log,
- `RowEditing` zůstane jediným místem, kde je napsané, co se smí měnit; kopie v UI ani
  v serveru nevznikne,
- tabulka bez primárního klíče a pohled zůstanou jen ke čtení, i kdyby to šlo obejít.

Uživatel uvidí zprávu databáze (porušený cizí klíč, `NOT NULL`, check constraint) přímo
v mřížce. Je to únik detailu o schématu, ale prohlížečka je za autorizací a bez té zprávy
by uživatel nevěděl, co dělá špatně.

Souběh se neřeší: kdo uloží druhý, přepíše prvního. Jediná pojistka je, že se řádek musí
pořád nacházet pod svým klíčem.
