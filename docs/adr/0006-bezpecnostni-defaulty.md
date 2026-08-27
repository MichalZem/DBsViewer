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
   Zapnutý běží přes parametrizovaný `SELECT` s pevným limitem, nad whitelistem tabulek,
   s maskováním sloupců podle vzoru a nikdy nepřijímá SQL od uživatele.
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
- Testy musí pokrývat i to, že se komponenta odmítne spustit — chybějící pád je chyba.
- `SchemaReadOptions.HideTables` a maskování sloupců používají glob vzory, ne regulární
  výrazy: z konfigurace je regex zbytečně silný nástroj a snadno se v něm chybuje.
