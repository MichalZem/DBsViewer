# 0003. Čtení z design-time modelu, ne z runtime modelu

- **Stav:** Přijato
- **Datum:** 2026-08-27

## Kontext

První verze čtečky sahala na `DbContext.Model`. Na ukázkovém modelu okamžitě spadla:

```
The requested configuration is not stored in the read-optimized model,
please use 'DbContext.GetService<IDesignTimeModel>().Model'.
```

`DbContext.Model` je runtime model optimalizovaný pro dotazování. EF z něj při stavbě
odstraňuje metadata, která za běhu nepotřebuje: collation, komentáře, defaultní hodnoty
i check constrainty. Přesně to, co prohlížečka schématu chce ukazovat.

## Rozhodnutí

Schéma se čte z `context.GetService<IDesignTimeModel>().Model` — ze stejného modelu,
ze kterého EF generuje migrace. Runtime model zůstává jako záloha pro případ, že by
služba nebyla dostupná; pak se do `DatabaseSchema.Warnings` zapíše, co bude chybět.

## Zvažované alternativy

**Runtime model a chybějící údaje ignorovat.** Znamenalo by to tiše zahodit komentáře
a defaulty — tedy velkou část hodnoty, kterou má prohlížečka přinést.

**Runtime model s dopočtem z živé databáze.** Fungovalo by, ale jen když je databáze
dostupná, a znemožnilo by čtení schématu z modelu bez připojení.

## Důsledky

- Čtení modelu nevyžaduje připojení k databázi. Jediná výjimka je seznam aplikovaných
  migrací a ten se dá vypnout.
- Fallback na runtime model je obranná větev, která se v testech nedá vyvolat.
  Testuje se proto přes `SafeRead` s podvrženou chybou.
