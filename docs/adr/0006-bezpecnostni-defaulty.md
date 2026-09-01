# 0006. Restriktivní bezpečnostní defaulty s pádem při startu

- **Stav:** Přijato
- **Datum:** 2026-08-27

## Kontext

Komponenta zveřejňuje strukturu databáze a po zapnutí náhledu dat i její obsah.
Instaluje se do každého projektu a konfiguruje se dvěma řádky — tedy přesně ta situace,
kdy se snadno nasadí do produkce, aniž by na to někdo pomyslel.

Náhled dat byl do rozsahu v1 zařazen vědomě, což riziko zvyšuje.

## Rozhodnutí

Bezpečnostní pravidla nejsou konfigurace, ale vlastnost komponenty:

1. Prohlížečka je zapnutá **jen v Development**. Rozšíření na jiné prostředí vyžaduje
   explicitní `EnabledIn`.
2. Když je povolená mimo Development a **není nastavená autorizační policy**,
   `MapDbsViewer()` **vyhodí výjimku při startu aplikace**. Ne varování do logu — pád.
3. Žádný endpoint nespouští DDL ani DML. Read-only je vlastnost API, ne přepínač.
4. Náhled dat je vypnutý a je to samostatný opt-in nezávislý na zpřístupnění schématu.
   Zapnutý běží přes parametrizovaný `SELECT` s pevným stropem stránky, nad whitelistem
   tabulek, s maskováním sloupců podle vzoru a nikdy nepřijímá SQL od uživatele.
5. Každý přístup se loguje na úrovni `Information`: kdo, která tabulka, náhled dat ano/ne.

## Zvažované alternativy

**Varování do logu místo pádu.** Logy nikdo nečte včas. Pád při startu je jediná zpětná
vazba, kterou nelze přehlédnout, a nastane v nasazovacím pipeline, ne v provozu.

**Zapnuto všude, bezpečnost na hostiteli.** Přenáší odpovědnost na toho, kdo komponentu
jen „přidal a nic nekonfiguroval" — tedy na scénář, pro který je celá komponenta stavěná.

**Náhled dat vůbec nedělat.** Bezpečnější, ale zahazuje funkci, která je při ladění
nejužitečnější. Řeší se opt-inem a maskováním.

## Důsledky

- Nastavení náhledu dat patří do konfigurace prostředí, ne do kódu, aby se nedalo
  zapnout omylem při nasazení.
- **Stránkování, řazení a filtrování nesmí to pravidlo obejít.** Mřížka dat posílá jména
  sloupců a hledané hodnoty, takže se z požadavku skládá SQL — jediné takové místo
  v komponentě. Platí tam dvě pravidla bez výjimky:

  **Identifikátory se neescapují, ale ověřují.** Jméno sloupce z požadavku musí sedět
  na sloupec načteného schématu; do dotazu se vloží jméno ze schématu, ne to z požadavku.
  Escapování by stačilo taky, ale ověření drží i tehdy, když se v escapování najde chyba.
  Neznámý sloupec se tiše přeskočí — filtr může zůstat z jiné tabulky a odmítnutí by
  uživateli nic neřeklo.

  **Hodnoty se do textu dotazu nedostanou vůbec**, jdou přes `DbParameter`. Čísla stránek
  jsou celá čísla ověřená proti mezím. Zástupné znaky `%` a `_` se v hledané hodnotě
  escapují, aby hledání textu nebylo zadáváním vzoru.

  Skládá to `DataQueryBuilder`, oddělený od služby právě proto, aby šel testovat sám
  o sobě — na tom kusu kódu záleží nejvíc.
- `COUNT(*)` má časový limit a jeho selhání náhled neshodí: stránkuje se dál, jen bez
  celkového počtu. Dotaz nad velkou tabulkou s filtrem bez indexu běží klidně desítky
  sekund a držel by přitom připojení.
- Data chodí POSTem, ne GETem. Hledaná hodnota je obsah databáze a v adrese by skončila
  v historii prohlížeče i v logu serveru.
- Tělo požadavku se čte vlastním nastavením serializace, ne tím z hostitelské aplikace.
  Globální `JsonOptions` patří hostiteli; komponenta do nich nesahá.
- Testy musí pokrývat i to, že se komponenta odmítne spustit — chybějící pád je chyba.
- `SchemaReadOptions.HideTables` a maskování sloupců používají glob vzory, ne regulární
  výrazy: z konfigurace je regex zbytečně silný nástroj a snadno se v něm chybuje.
