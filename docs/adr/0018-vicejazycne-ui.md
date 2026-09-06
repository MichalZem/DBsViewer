# 0018. Vícejazyčné UI: silně typované resx a jazyk v adrese

- **Stav:** Přijato
- **Datum:** 2026-09-06
- **Mění:** ruší pravidlo „texty v UI jsou česky" z [ADR-0012](0012-vlastni-layout-diagramu.md) a *Stylu* v `CLAUDE.md`

## Kontext

Prohlížečka mluvila jenom česky. Pro projekt psaný česky to bylo konzistentní, ale
NuGet je globální trh: balíček si nainstaluje kdokoli a české „Rozdíly" a „Odkazuje sem"
mu nepomůžou.

Rešerše konkurence to podpořila. Nejbližší analogie —
[EntityFrameworkCore.Diagrams](https://www.nuget.org/packages/EntityFrameworkCore.Diagrams),
tedy embedded diagram EF modelu v ASP.NET Core — nasbírala **57 000 stažení** s anglickým
UI, přestože poslední verzi vydala v roce 2017 a od té doby je mrtvá. Jazyk je tedy
prokazatelně jedna z mála věcí, které rozhodují o tom, jestli nástroj někdo použije.

Zvolil se **anglický výchozí jazyk s češtinou jako druhou možností**.

## Rozhodnutí

**Texty leží v `.resx` a silně typovanou třídu generuje MSBuild, ne Visual Studio.**
`ResXFileCodeGenerator` je custom tool VS, takže by třída na CI nevznikla; tentýž generátor
je ale vestavěný v MSBuildu a stačí ho zavolat metadaty položky:

```xml
<EmbeddedResource Update="Texts\Texts.resx">
  <Generator>MSBuild:Compile</Generator>
  <StronglyTypedFileName>$(IntermediateOutputPath)\Texts.Designer.cs</StronglyTypedFileName>
  <StronglyTypedLanguage>CSharp</StronglyTypedLanguage>
  <StronglyTypedClassName>Texts</StronglyTypedClassName>
  <PublicClass>true</PublicClass>
</EmbeddedResource>
```

Díky tomu je `@Texts.TabTables` v Razoru **kontrolované kompilátorem** — překlep je chyba
buildu, ne prázdný popisek za běhu. Vygenerovaná třída nese `[GeneratedCode]`, který
už tak coverlet v tomhle projektu vylučuje, takže se stoprocentní pokrytí nemuselo nijak
obcházet.

**Invariantní globalizace zůstává zapnutá.** Překlady se hledají přes satelitní assembly,
a ta se najde podle *jména* kultury — data ICU k tomu potřeba nejsou. Stačí povolit
kultury mimo předdefinované:

```xml
<InvariantGlobalization>true</InvariantGlobalization>
<PredefinedCulturesOnly>false</PredefinedCulturesOnly>
```

Měřeno na publikovaném výstupu: vypnutí invariantní globalizace by přidalo **242 kB**
komprimovaných dat do stahování a přes 600 kB do balíčku. Takhle je nárůst **nulový**;
platí se jen satelitní assembly s češtinou, která má pár kilobajtů.

**Angličtina je neutrální `.resx`, ne kultura `en`.** Pro ni se tedy žádná satelitní
assembly nehledá a `LanguageChoice.Culture` pro ni vrací invariantní kulturu.

**Jazyk žije v adrese jako `?lang=cs`.** Přežije to znovunačtení, které přepnutí stejně
vyžaduje; adresa se dá poslat kolegovi i s jazykem; a nepotřebuje to `localStorage`,
tedy ani JavaScript, kterému se UI jinde vyhýbá ([ADR-0012](0012-vlastni-layout-diagramu.md)).
Neznámý nebo chybějící kód znamená angličtinu — adresa přichází od uživatele a překlep
v ní nesmí prohlížečku shodit.

**Přepnutí jazyka znovu načte celou prohlížečku a uživatel to předem ví.** Ve WebAssembly
se satelitní assembly stahuje jen pro kulturu, se kterou aplikace nastartovala; přepnout
za běhu by šlo jedině přes `loadAllSatelliteResources`, což je JavaScript v `index.html`.
Reload je proto přijatý záměrně a přepínač se **ptá dřív, než se provede** — modálním
oknem, ne dialogem prohlížeče, který by stejně bez JavaScriptu nešel otevřít.

**Skloňování řeší kód, tvary slov resx.** Angličtina má dva tvary, čeština tři a resource
soubor pravidlo výběru vyjádřit neumí. Slova jsou proto v resx jako trojice
`Table_One` / `Table_Few` / `Table_Other` (v angličtině je `_Few` shodné s `_Other`)
a `Plural` vybírá podle jazyka. Formátování čísel se ze stejného důvodu neopírá o kulturu
stroje: oddělovač tisíců je nedělitelná mezera v češtině a čárka v angličtině.

**Nálezy diffu se skládají z `DiffKind`, ne ze serverové věty.** `DiffFinding` nese druh
nálezu už dnes, takže text vzniká až v prohlížečce a dá se přeložit. Serverová `Message`
zůstává anglická a slouží HTTP API a `dbsview`; když UI druh nálezu nezná, spadne na ni,
aby nový `DiffKind` neudělal prázdný řádek.

## Zvažované alternativy

**`IStringLocalizer` s klíči jako řetězci.** Standardní cesta, ale `L["TabTables"]` nikdo
nezkontroluje: překlep projde buildem a za běhu se vypíše klíč. To je přesně ta tichá
chyba, kterou projekt jinde odmítá.

**Typovaný katalog v C#** — rozhraní `ITexts` a jedna třída na jazyk. Vynutilo by úplnost
překladu kompilátorem, ne až testem, a nepotřebovalo by satelitní assembly ani kulturu.
Cenou by byl nestandardní formát, do kterého překladatel sahá jako do kódu. Při dvou
jazycích rozdíl nevyvážil ztrátu zavedeného formátu.

**Source generator nad resx**, který by vygeneroval rozhraní i implementace a chybějící
klíč ohlásil jako chybu buildu. Technicky nejlepší ze všech variant, ale devátý projekt
v řešení a vlastní údržba generátoru je na 220 řetězců ve dvou jazycích neúměrná.

**Přepnutí bez reloadu přes `loadAllSatelliteResources`.** Šlo by, ale za cenu druhého
místa s JavaScriptem v `index.html` a stahování všech jazyků při každém startu.

## Důsledky

- Výchozí jazyk je **angličtina**. Kdo chce češtinu, otevře `?lang=cs`.
- **Chybějící překlad je tichý**: neobsazený klíč v `Texts.cs.resx` se nahradí anglickou
  hodnotou bez chyby buildu i běhu. Hlídá to test, který projde všechny klíče v obou
  jazycích a ověří, že žádný není prázdný — compiler tuhle vlastnost ohlídat neumí.
- **Testy si připínají kulturu.** Bez toho by na českých Windows četly češtinu a na CI
  angličtinu; `Kultura.Pripni` je proto nastaví na invariantní a testy pro češtinu
  si ji přepnou samy.
- Hlášky, které vrací **databáze** (porušený cizí klíč, `NOT NULL`), zůstávají v jazyce
  databáze. Přeložit jde jen to, co skládá prohlížečka.
- **`dbsview` si formátuje čísla sám.** Prohlížečka vybírá tvar slova podle zvoleného
  jazyka, jenže nástroj žádný zvolený jazyk nemá — půjčené `Counts` by se řídilo kulturou
  stroje a výpis by se lišil podle toho, kde běží. Přesně na tom spadlo CI.
- **Nepřeloženo zatím zůstává:** výpis `dbsview`, konfigurační výjimky při startu (čte je
  vývojář, ne uživatel) a varování ze `SafeRead`, která v sobě nesou text výjimky
  z databázového driveru.
- Přidání jazyka je jeden nový `Texts.xx.resx` a jedna hodnota v `Language`. Plochý
  soubor s dvojicemi klíč–hodnota je dobrý vstup pro překladatele i pro strojový překlad.
