using DbsViewer.Server;

namespace DbsViewer.Tests.Server;

public class DbsViewerOptionsTests
{
    [Fact]
    public void Vychozi_nastaveni_je_restriktivni()
    {
        var options = new DbsViewerOptions();

        Assert.Equal(HostEnv.Development, options.EnabledIn);
        Assert.Null(options.AuthorizationPolicy);
        Assert.False(options.DataPreview.Enabled);
        Assert.False(options.DataPreview.AllowInProduction);
        Assert.False(options.ShowRowCounts);
        Assert.Equal("/dbschema", options.RoutePrefix);
    }

    [Theory]
    [InlineData("/db", "/db")]
    [InlineData("db", "/db")]
    [InlineData("/db/", "/db")]
    [InlineData("  /_db  ", "/_db")]
    [InlineData("a/b", "/a/b")]
    public void Prefix_cesty_se_normalizuje(string input, string expected) =>
        Assert.Equal(expected, new DbsViewerOptions { RoutePrefix = input }.RoutePrefix);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    public void Neplatny_prefix_je_chyba(string input) =>
        Assert.Throws<ArgumentException>(() => new DbsViewerOptions { RoutePrefix = input });

    [Fact]
    public void Chybejici_prefix_je_chyba() =>
        Assert.Throws<ArgumentNullException>(() => new DbsViewerOptions { RoutePrefix = null! });

    [Fact]
    public void RequireAuthorization_nastavi_policy_a_vrati_options()
    {
        var options = new DbsViewerOptions();

        var same = options.RequireAuthorization("DbAdmins");

        Assert.Same(options, same);
        Assert.Equal("DbAdmins", options.AuthorizationPolicy);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Prazdna_policy_je_chyba(string policy) =>
        Assert.Throws<ArgumentException>(() => new DbsViewerOptions().RequireAuthorization(policy));

    [Fact]
    public void Chybejici_policy_je_chyba() =>
        Assert.Throws<ArgumentNullException>(() => new DbsViewerOptions().RequireAuthorization(null!));

    [Theory]
    [InlineData("Development", HostEnv.Development)]
    [InlineData("development", HostEnv.Development)]
    [InlineData("Staging", HostEnv.Staging)]
    [InlineData("Production", HostEnv.Production)]
    public void Prostredi_se_prelozi_na_priznak(string name, HostEnv expected) =>
        Assert.Equal(expected, DbsViewerOptions.MapEnvironment(name));

    [Theory]
    [InlineData("QA")]
    [InlineData("Test")]
    [InlineData(null)]
    public void Nezname_prostredi_se_bere_jako_produkcni(string? name) =>
        Assert.Equal(HostEnv.Production, DbsViewerOptions.MapEnvironment(name!));

    [Fact]
    public void Dostupnost_se_ridi_priznakem()
    {
        var development = new DbsViewerOptions();

        Assert.True(development.IsEnabledIn("Development"));
        Assert.False(development.IsEnabledIn("Staging"));
        Assert.False(development.IsEnabledIn("Production"));

        var everywhere = new DbsViewerOptions { EnabledIn = HostEnv.All };

        Assert.True(everywhere.IsEnabledIn("Development"));
        Assert.True(everywhere.IsEnabledIn("Production"));

        var nowhere = new DbsViewerOptions { EnabledIn = HostEnv.None };

        Assert.False(nowhere.IsEnabledIn("Development"));
    }

    [Fact]
    public void Prevod_na_volby_cteni_prenasi_filtry()
    {
        var options = new DbsViewerOptions { ShowRowCounts = true };
        options.HideTables.Add("AspNet*");
        options.IncludeSchemas.Add("dbo");

        var read = options.ToReadOptions();

        Assert.True(read.IncludeRowCounts);
        Assert.True(read.IncludeMigrations);
        Assert.Equal(["AspNet*"], read.HideTables.ToList());
        Assert.Equal(["dbo"], read.IncludeSchemas.ToList());
    }
}

public class DataPreviewOptionsTests
{
    [Fact]
    public void Ve_vychozim_stavu_neni_povolena_zadna_tabulka()
    {
        var options = new DataPreviewOptions();

        Assert.False(options.IsAllowed(new DbObjectName("dbo", "Orders")));
    }

    [Fact]
    public void Zapnuty_nahled_bez_whitelistu_povoli_vse()
    {
        var options = new DataPreviewOptions { Enabled = true };

        Assert.True(options.IsAllowed(new DbObjectName("dbo", "Orders")));
    }

    [Fact]
    public void Whitelist_omezi_povolene_tabulky()
    {
        var options = new DataPreviewOptions { Enabled = true };
        options.AllowedTables.Add("Order*");

        Assert.True(options.IsAllowed(new DbObjectName("dbo", "Orders")));
        Assert.True(options.IsAllowed(new DbObjectName("dbo", "OrderLines")));
        Assert.False(options.IsAllowed(new DbObjectName("dbo", "Customers")));
    }

    [Fact]
    public void Whitelist_umi_i_kvalifikovane_jmeno()
    {
        var options = new DataPreviewOptions { Enabled = true };
        options.AllowedTables.Add("sales.*");

        Assert.True(options.IsAllowed(new DbObjectName("sales", "Orders")));
        Assert.False(options.IsAllowed(new DbObjectName("dbo", "Orders")));
    }

    [Fact]
    public void Vychozi_maskovani_chrani_hesla_a_tokeny()
    {
        var options = new DataPreviewOptions();

        Assert.True(options.IsMasked("PasswordHash"));
        Assert.True(options.IsMasked("password"));
        Assert.True(options.IsMasked("ApiToken"));
        Assert.True(options.IsMasked("ClientSecret"));
        Assert.False(options.IsMasked("Email"));
    }

    [Fact]
    public void Vlastni_vzor_maskovani_jde_pridat()
    {
        var options = new DataPreviewOptions();
        options.MaskColumns.Add("Email");

        Assert.True(options.IsMasked("Email"));
    }

    [Theory]
    [InlineData(50, 50)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(5000, DataPreviewOptions.HardRowLimit)]
    public void Pocet_radku_se_orizne_na_povolene_meze(int requested, int expected) =>
        Assert.Equal(expected, new DataPreviewOptions { MaxRows = requested }.MaxRows);

    [Fact]
    public void Vychozi_limit_je_sto_radku() =>
        Assert.Equal(100, new DataPreviewOptions().MaxRows);
}
