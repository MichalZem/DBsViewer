using System.Globalization;
using System.Runtime.CompilerServices;

namespace DbsViewer.Tests.Ui;

/// <summary>
/// Připne testům jazyk, ve kterém běží.
/// </summary>
/// <remarks>
/// Bez tohohle by texty záležely na tom, jaké jazykové nastavení má stroj: na českých
/// Windows by <c>ResourceManager</c> našel satelitní assembly a testy by četly češtinu,
/// zatímco na CI angličtinu. Přesně ta závislost na kultuře stroje, které se projekt
/// jinde vyhýbá.
///
/// Výchozí je invariantní kultura, tedy angličtina — ta je i výchozím jazykem prohlížečky.
/// Testy, které potřebují češtinu, si ji přepnou přes <c>VJazyce</c>.
/// </remarks>
internal static class Kultura
{
    [ModuleInitializer]
    internal static void Pripni()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
    }
}
