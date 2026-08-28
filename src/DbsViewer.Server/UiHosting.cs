using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DbsViewer.Server;

/// <summary>
/// Servírování zabudovaného Blazor WebAssembly UI.
/// </summary>
/// <remarks>
/// UI je samostatný projekt, jehož publikovaný <c>wwwroot</c> je v tomto balíčku jako
/// embedded resources — viz <see href="../../docs/adr/0004-blazor-wasm-embedded.md">ADR-0004</see>.
/// Hostitelská aplikace tedy nemusí o Blazoru vědět a funguje to i v čistém Web API.
/// </remarks>
public static class UiHosting
{
    /// <summary>Předpona, pod kterou jsou soubory UI uložené v assembly.</summary>
    private const string ResourcePrefix = "DbsViewer.Server.ui.";

    /// <summary>Vstupní stránka. Bez ní se UI nepovažuje za dostupné.</summary>
    private const string IndexResource = ResourcePrefix + "index.html";

    /// <summary>Namapuje soubory UI pod zadanou skupinu cest.</summary>
    /// <param name="group">Skupina cest, pod kterou prohlížečka běží.</param>
    /// <param name="options">Nastavení, ze kterého se bere prefix pro základ cesty.</param>
    /// <param name="assembly">
    /// Sestava se soubory UI. Testy sem podstrčí vlastní, aby šly ověřit i cesty
    /// servírování souborů — do testovací sestavy se celé UI nevkládá.
    /// </param>
    public static void MapUi(RouteGroupBuilder group, DbsViewerOptions options, Assembly? assembly = null)
    {
        assembly ??= typeof(UiHosting).Assembly;
        var files = ListFiles(assembly);

        // Bez index.html je UI nepoužitelné, i kdyby ostatní soubory existovaly.
        if (!files.Contains(IndexResource))
        {
            // UI se do balíčku nedostalo — API funguje dál, jen se místo stránky
            // vrátí vysvětlení, aby uživatel netápal nad prázdnou obrazovkou.
            group.MapGet("/", () => Results.Text(MissingUiMessage, "text/plain; charset=utf-8"));
            return;
        }

        group.MapGet("/", () => ServeIndex(assembly, options));
        group.MapGet("/index.html", () => ServeIndex(assembly, options));

        group.MapGet("/{**path}", (string path) =>
        {
            var resource = ResourceNameFor(path);

            if (resource is null || !files.Contains(resource))
            {
                return Results.NotFound();
            }

            var stream = assembly.GetManifestResourceStream(resource);

            return stream is null
                ? Results.NotFound()
                : Results.Stream(stream, ContentType(path));
        });
    }

    /// <summary>
    /// Vrátí <c>index.html</c> s upraveným <c>&lt;base href&gt;</c>. Bez toho by se
    /// prohlížečka na jiné cestě než v kořeni nenačetla.
    /// </summary>
    private static IResult ServeIndex(Assembly assembly, DbsViewerOptions options)
    {
        // Existenci index.html ověřilo MapUi, jinak by se sem řízení nedostalo.
        using var stream = assembly.GetManifestResourceStream(IndexResource)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var html = reader.ReadToEnd();

        return Results.Text(RewriteBaseHref(html, options.RoutePrefix), "text/html; charset=utf-8");
    }

    /// <summary>Nahradí základ cesty v HTML podle nakonfigurovaného prefixu.</summary>
    internal static string RewriteBaseHref(string html, string routePrefix)
    {
        ArgumentNullException.ThrowIfNull(html);

        var basePath = routePrefix.EndsWith('/') ? routePrefix : routePrefix + '/';
        var start = html.IndexOf("<base ", StringComparison.OrdinalIgnoreCase);

        if (start < 0)
        {
            return html;
        }

        var end = html.IndexOf('>', start);

        if (end < 0)
        {
            return html;
        }

        return string.Concat(html.AsSpan(0, start), $"<base href=\"{basePath}\" />", html.AsSpan(end + 1));
    }

    /// <summary>Převede cestu z URL na jméno prostředku v assembly.</summary>
    internal static string? ResourceNameFor(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        // Manifest resources nemají adresáře — lomítka jsou v názvu tečky.
        var normalized = path.Trim('/').Replace('/', '.');

        return normalized.Length == 0 ? null : ResourcePrefix + normalized;
    }

    /// <summary>Typ obsahu podle přípony. Blazor bez správných typů nenaběhne.</summary>
    internal static string ContentType(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();

        return extension switch
        {
            ".html" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".mjs" => "text/javascript; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".wasm" => "application/wasm",
            ".dll" => "application/octet-stream",
            ".pdb" => "application/octet-stream",
            ".dat" => "application/octet-stream",
            ".blat" => "application/octet-stream",
            ".br" => "application/octet-stream",
            ".gz" => "application/octet-stream",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".ico" => "image/x-icon",
            _ => "application/octet-stream",
        };
    }

    /// <summary>Soubory UI vložené do assembly.</summary>
    private static HashSet<string> ListFiles(Assembly assembly) =>
    [
        .. assembly.GetManifestResourceNames()
            .Where(static name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)),
    ];

    private const string MissingUiMessage =
        "Grafické UI není v tomto balíčku obsažené, ale HTTP API funguje.\n\n"
        + "Schéma najdeš na api/schema, porovnání na api/schema/diff.\n"
        + "Přehled dostupných funkcí vrací api/meta.";
}
