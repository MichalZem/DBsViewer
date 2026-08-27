# 0011. Cizí klíče se mezi zdroji párují podle sloupců, ne podle jména

- **Stav:** Přijato
- **Datum:** 2026-08-27

## Kontext

Integrační test proti čerstvě nainstalovanému balíčku ukázal, že sloučený pohled hlásí
každou vazbu dvakrát a diff u shodných schémat vyrábí desítku falešných nálezů.

Příčina: **SQLite jména cizích klíčů vůbec nevystavuje.** `PRAGMA foreign_key_list` vrací
jen pořadové číslo, takže si je čtečka skládá podle konvence — `FK_Posts_Blogs`. EF ale
stejný klíč pojmenuje `FK_Posts_Blogs_BlogId`, protože do jména přidává sloupec.

Párování podle jména tedy považovalo jednu vazbu za dvě různé.

## Rozhodnutí

Cizí klíče se mezi zdroji párují podle **sloupců závislé tabulky**, ne podle jména.
Platí to na dvou místech:

- `SchemaComparer` — aby diff nehlásil tutéž vazbu jako „chybí v databázi" a zároveň
  „chybí v modelu",
- `SchemaMerger` — aby sloučený pohled měl jednu hranu a jeden cizí klíč.

Cílová tabulka součástí identity **není**. Kdyby byla, přesměrování vazby na jinou tabulku
by se nahlásilo jako dvojice „chybí" a „přebývá" místo jasného „cíl se liší".

Vztahy N:M se párují podle vazební tabulky, protože jejich `Id` se z téhož důvodu liší.

## Zvažované alternativy

**Sjednotit skládání jmen s EF.** Konvence EF není součástí veřejného kontraktu a mění se;
navíc uživatel může jméno klíče přejmenovat a shoda se rozpadne.

**Párovat podle jména s tolerancí na příponu.** Křehké a nefunguje u složených klíčů
ani u vlastních jmen.

**Hlásit rozdíl ve jménech jako nález.** U SQLite by to znamenalo nález u každého cizího
klíče v databázi — tedy šum, ne informace.

## Důsledky

- Dva cizí klíče mezi týmiž tabulkami nad různými sloupci zůstávají dvěma vztahy.
  To je správně: objednávka s fakturační a dodací adresou má dvě vazby.
- Jméno klíče se přebírá z databáze, protože popisuje skutečnost. Navigace se berou
  z modelu, protože je databáze nezná.
- Stejný přístup platí i pro indexy — tam ale jména fungují, protože je SQLite vystavuje.
