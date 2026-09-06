using System.Globalization;
using DbsViewer.Ui;
using DbsViewer.Ui.Model;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Základní adresa je ta, ze které se aplikace načetla — tedy prefix, na kterém
// server prohlížečku vystavil. UI tak nemusí cestu nikde konfigurovat.
builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
});

builder.Services.AddScoped<DbsViewerClient>();

var host = builder.Build();

// Jazyk se bere z adresy (?lang=cs). Musí se nastavit před prvním vykreslením, protože
// ResourceManager čte kulturu až v okamžiku, kdy se text načítá — a satelitní assembly
// se v WebAssembly stahuje jen pro tu, se kterou aplikace nastartovala.
var language = LanguageChoice.FromUri(host.Services.GetRequiredService<NavigationManager>().Uri);
var culture = LanguageChoice.Culture(language);

CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

await host.RunAsync();
