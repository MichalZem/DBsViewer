# 0004. UI jako samostatný Blazor WASM projekt embedovaný do serveru

- **Stav:** Přijato
- **Datum:** 2026-08-27

## Kontext

Komponenta se má instalovat do libovolné aplikace dvěma řádky v `Program.cs`. Hostitel
přitom může být cokoli — čisté Web API, MVC, Blazor Server nebo Blazor WASM aplikace.

UI potřebuje interaktivní ER diagram s pan &amp; zoom, focus modem a klientským hledáním
napříč stovkami tabulek. Frontend se v těchto projektech běžně píše v Blazor WebAssembly.

## Rozhodnutí

UI je samostatný standalone Blazor WASM projekt `DbsViewer.Ui`. Jeho publikovaný `wwwroot`
se přibalí do `DbsViewer.Server` jako embedded resources a servíruje se přes
`ManifestEmbeddedFileProvider` pod konfigurovatelným prefixem s vlastním `index.html`
a `<base href>`.

## Zvažované alternativy

**Razor Class Library vložená do hostitelské Blazor aplikace.** Odpadl by druhý runtime,
ale komponenta by fungovala jen v Blazor hostiteli a kolidovala by s jeho routingem
i s jeho `blazor.boot.json`.

**Vanilla TypeScript a SVG.** Zhruba 200 kB místo 2–3 MB a okamžitý start, ale duplikuje
datový model v TypeScriptu a přidává do repozitáře npm build. Sdílení `DatabaseSchema`
v C# je největší výhoda zvolené varianty.

**Samostatně hostovaná aplikace.** Odpadá velikost balíčku, ale ztrácí se cíl „přidat
do projektu a nic nekonfigurovat".

## Důsledky

- Hostitelská aplikace nemusí o Blazoru vědět; funguje to i ve Web API.
- NuGet balíček naroste zhruba o 2–3 MB (brotli). Runtime se stahuje až při prvním
  otevření prohlížečky, ne při startu aplikace.
- `DbsViewer.Abstractions` musí zůstat bez závislostí a trimmovatelné, aby ho šlo
  použít na obou stranách. Proto je tam JSON source generator.
- Layout diagramu je jediné místo s JS interopem (elkjs). Zbytek se kreslí přímo v Blazoru.
- **UI se publikuje samostatným příkazem, ne z MSBuild targetu.** Před balením musí
  proběhnout `dotnet publish src/DbsViewer.Ui -c Release -o artifacts/ui`; build serveru
  jen vezme, co tam najde, a bez toho rovnou selže.

  Nabízí se to zautomatizovat targetem, a první verze to tak měla. Jenže `DbsViewer.Ui`
  je zároveň v řešení kvůli testům, takže se stavěl dvakrát současně — jednou jako projekt,
  podruhé z targetu — a oba běhy si sahaly na stejné mezisoubory. Padalo to na
  `IOException` v `GenerateDepsFile` a `GenerateBlazorBootExtensionJson`. Na dvoujádrovém
  stroji to prošlo, na CI ne. Izolace přes `BaseOutputPath` selhala na `NETSDK1047`
  (chybějící cíl pro `browser-wasm`), MSBuild task místo `Exec` na tomtéž souběhu.

  Samostatný krok stojí jeden řádek navíc v návodu a v CI, ale nezávisí na tom, kolik
  má stroj jader. Kdyby se to někdo chystal vrátit do targetu: tohle je ten důvod.
