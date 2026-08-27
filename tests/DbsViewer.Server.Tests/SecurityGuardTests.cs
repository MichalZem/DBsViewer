using DbsViewer.Server;

namespace DbsViewer.Tests.Server;

/// <summary>
/// Bezpečnostní kontrola při startu. Tyhle testy hlídají pravidla z ADR-0006 —
/// když některý z nich přestane platit, znamená to, že se komponenta dá nasadit
/// do produkce bez autorizace.
/// </summary>
public class SecurityGuardTests
{
    private static void Guard(DbsViewerOptions options, string environment) =>
        DbsViewerEndpointRouteBuilderExtensions.GuardSecurity(options, environment);

    [Fact]
    public void V_Development_neni_autorizace_povinna()
    {
        var options = new DbsViewerOptions();

        Guard(options, "Development");
    }

    [Fact]
    public void Ve_Staging_bez_policy_aplikace_nenastartuje()
    {
        var options = new DbsViewerOptions { EnabledIn = HostEnv.Development | HostEnv.Staging };

        var exception = Assert.Throws<InvalidOperationException>(() => Guard(options, "Staging"));

        Assert.Contains("autorizační policy", exception.Message, StringComparison.Ordinal);
        Assert.Contains("RequireAuthorization", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void V_produkci_bez_policy_aplikace_nenastartuje()
    {
        var options = new DbsViewerOptions { EnabledIn = HostEnv.All };

        Assert.Throws<InvalidOperationException>(() => Guard(options, "Production"));
    }

    [Fact]
    public void Nezname_prostredi_se_chova_jako_produkce()
    {
        var options = new DbsViewerOptions { EnabledIn = HostEnv.All };

        Assert.Throws<InvalidOperationException>(() => Guard(options, "QA"));
    }

    [Fact]
    public void S_nastavenou_policy_start_projde()
    {
        var options = new DbsViewerOptions { EnabledIn = HostEnv.All }.RequireAuthorization("DbAdmins");

        Guard(options, "Production");
    }

    [Fact]
    public void Vypnuta_prohlizecka_v_danem_prostredi_kontrolu_neresi()
    {
        // Prohlížečka ve Staging povolená není, takže chybějící policy nevadí.
        var options = new DbsViewerOptions { EnabledIn = HostEnv.Development };

        Guard(options, "Staging");
    }

    [Fact]
    public void Nahled_dat_v_produkci_aplikaci_shodi()
    {
        var options = new DbsViewerOptions { EnabledIn = HostEnv.All }.RequireAuthorization("DbAdmins");
        options.DataPreview.Enabled = true;

        var exception = Assert.Throws<InvalidOperationException>(() => Guard(options, "Production"));

        Assert.Contains("náhled dat", exception.Message, StringComparison.Ordinal);
        Assert.Contains("AllowInProduction", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Nahled_dat_v_produkci_jde_povolit_vedome()
    {
        var options = new DbsViewerOptions { EnabledIn = HostEnv.All }.RequireAuthorization("DbAdmins");
        options.DataPreview.Enabled = true;
        options.DataPreview.AllowInProduction = true;

        Guard(options, "Production");
    }

    [Fact]
    public void Nahled_dat_ve_Staging_pojistku_nespousti()
    {
        var options = new DbsViewerOptions { EnabledIn = HostEnv.All }.RequireAuthorization("DbAdmins");
        options.DataPreview.Enabled = true;

        Guard(options, "Staging");
    }

    [Fact]
    public void Nahled_dat_v_Development_pojistku_nespousti()
    {
        var options = new DbsViewerOptions();
        options.DataPreview.Enabled = true;

        Guard(options, "Development");
    }
}

public class ViewParsingTests
{
    private static bool TryParse(string? source, out SchemaView? view) =>
        DbsViewerEndpointRouteBuilderExtensions.TryParseView(source, out view);

    [Theory]
    [InlineData("ef", SchemaView.Ef)]
    [InlineData("model", SchemaView.Ef)]
    [InlineData("EF", SchemaView.Ef)]
    [InlineData("live", SchemaView.Live)]
    [InlineData("db", SchemaView.Live)]
    [InlineData("database", SchemaView.Live)]
    [InlineData("merged", SchemaView.Merged)]
    [InlineData("MERGED", SchemaView.Merged)]
    public void Znamy_pohled_se_prelozi(string source, SchemaView expected)
    {
        Assert.True(TryParse(source, out var view));
        Assert.Equal(expected, view);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Bez_zadani_se_pohled_nevybira(string? source)
    {
        Assert.True(TryParse(source, out var view));
        Assert.Null(view);
    }

    [Theory]
    [InlineData("nesmysl")]
    [InlineData("postgres")]
    public void Neznamy_pohled_je_odmitnut(string source)
    {
        Assert.False(TryParse(source, out var view));
        Assert.Null(view);
    }

    [Theory]
    [InlineData("-", null)]
    [InlineData("", null)]
    [InlineData("dbo", "dbo")]
    public void Pomlcka_v_ceste_znamena_prazdne_schema(string input, string? expected) =>
        Assert.Equal(expected, DbsViewerEndpointRouteBuilderExtensions.NormalizeSchema(input));
}
