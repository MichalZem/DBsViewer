using DbsViewer.Ui;
using DbsViewer.Ui.Model;
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

await builder.Build().RunAsync();
