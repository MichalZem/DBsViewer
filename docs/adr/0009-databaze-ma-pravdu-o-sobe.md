# 0009. Při slučování má databáze pravdu o sobě, model o záměru

- **Stav:** Přijato
- **Datum:** 2026-08-27

## Kontext

Sloučený pohled (`SchemaSourceKind.Merged`) spojuje EF model se skutečností v databázi.
U každého údaje, který znají oba zdroje, je potřeba rozhodnout, který vyhraje — a rozhodnout
to jednou a konzistentně, ne případ od případu.

Typ sloupce je jiný v modelu a jiný v databázi. Který ukázat? A co komentář, který má model,
ale databáze ne?

## Rozhodnutí

Pravidlo má dvě věty:

> **Databáze má pravdu o tom, co v ní je.** Typy, nullabilita, indexy, defaulty, identity,
> collation, počty řádků.
>
> **Model má pravdu o záměru.** Navigace, CLR typy, komentáře, dědičnost, concurrency tokeny,
> shadow properties, sbalené vztahy N:M.

Kde jeden zdroj mlčí, doplní ho druhý. Objekty z obou stran zůstávají zachované — sloučení
nikdy nic nezahazuje, protože právě to, co je jen na jedné straně, je zajímavé.

Vztahy jsou výjimka se svým vlastním pravidlem: **přednost má model**, protože jako jediný
zná navigace a umí N:M přes skip-navigace. Z databáze se doplní jen ty, které model nemá.
Sbalený vztah N:M navíc potlačí hrany vazební tabulky, které databáze hlásí zvlášť.

## Zvažované alternativy

**Vždy databáze.** Jednodušší, ale zahodí navigace a CLR typy — tedy to, kvůli čemu se
EF model vůbec čte.

**Vždy model.** Sloučený pohled by pak byl k ničemu: ukazoval by přesně to, co model, jen
pomaleji.

**Ukazovat obojí vedle sebe.** To je pravý účel diffu, ne sloučeného pohledu. Sloučený pohled
má odpovídat na otázku „jak to vypadá", diff na otázku „kde se to liší".

## Důsledky

- Sloučený pohled je vždy nadmnožina obou zdrojů, takže se v něm nedá nic ztratit.
- Diff a sloučení jsou dvě různé operace nad stejnými vstupy a nesmí se zaměňovat.
- Kde model a databáze říkají něco jiného, sloučený pohled ukáže databázi — a diff to
  zároveň označí jako nález. Uživatel tedy vidí skutečnost i to, že nesedí se záměrem.
