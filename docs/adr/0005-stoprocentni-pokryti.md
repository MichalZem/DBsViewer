# 0005. Vynucené 100% pokrytí řádků a metod

- **Stav:** Přijato
- **Datum:** 2026-08-27

## Kontext

DbsViewer má jít do každého projektu a číst produkční databáze. Chyba ve čtečce schématu
se projeví jako špatně nakreslený diagram nebo falešný nález v diffu — tedy tiše
a v nejhorší chvíli. Zároveň jde o knihovnu bez UI testů, kde je jednotkový test
jediná zpětná vazba.

## Rozhodnutí

`dotnet test` vynucuje pokrytí přes coverlet a při nedosažení prahu **selže**:

| Metrika | Práh | Proč |
|---|---|---|
| Řádky | 100 % | Každý řádek dodávaného kódu musí projít testem. |
| Metody | 100 % | Nedosažitelná metoda je buď mrtvý kód, nebo chybějící test. |
| Větve | 85 % | Regresní pojistka, ne cíl. Smí růst, ne klesat. |

Měří se jen `DbsViewer.Abstractions` a `DbsViewer.EfCore` — tedy to, co se publikuje.
Projekty `samples/` a `tools/` jsou pomůcky, ne dodávaný kód.

Vyloučeno je jen dvojí a vždy s odůvodněním v atributu:

- kód z generátorů (`GeneratedCodeAttribute`) — testuje se chování, které jím prochází,
  ne jeho vygenerované větve,
- pojmenované obranné helpery s `[ExcludeFromCodeCoverage(Justification = …)]`, které
  hlídají kontrakt EF a u platného modelu se nedají vyvolat.

## Zvažované alternativy

**Bez prahu, jen měření.** Pokrytí by klesalo tiše. Číslo, které nikoho nezastaví,
nikoho nezajímá.

**100 % včetně větví.** Znamenalo by buď mockovat metadata EF do absurdna, nebo mazat
obranné pojistky, které mají smysl. Za tu cenu to nestojí — proto je práh větví nižší
a explicitně označený jako pojistka.

**80 % na všechno.** Nechává prostor pro netestované cesty přesně tam, kde se schovávají
chyby v okrajových modelech.

## Důsledky

- Nedosažitelný kód se **nepíše**. Když vznikne, buď se odstraní, nebo se refaktoruje
  do testovatelného tvaru. Tak vznikly `SafeRead`, `IMigrationsReader` a `BuildKeyOrder`.
- Obranné `?? fallback` nad metadaty EF se nahrazují výpočtem, který je vždy platný —
  viz `IsRequiredConstraint`.
- Testy nejsou volitelné: kód bez testu neprojde buildem, ne code review.
