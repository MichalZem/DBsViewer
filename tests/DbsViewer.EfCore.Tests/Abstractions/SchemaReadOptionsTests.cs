namespace DbsViewer.Tests.Abstractions;

public class SchemaReadOptionsTests
{
    [Fact]
    public void Vychozi_nastaveni_nic_neskryva()
    {
        var options = SchemaReadOptions.Default;

        Assert.True(options.IsVisible(new DbObjectName("dbo", "Orders")));
        Assert.True(options.IncludeMigrations);
        Assert.True(options.DetectJoinTables);
        Assert.False(options.IncludeRowCounts);
        Assert.Empty(options.HideTables);
        Assert.Empty(options.IncludeSchemas);
    }

    [Fact]
    public void HideTables_skryva_podle_jmena_tabulky()
    {
        var options = new SchemaReadOptions { HideTables = ["__EFMigrationsHistory", "AspNetUser*"] };

        Assert.False(options.IsVisible(new DbObjectName(null, "__EFMigrationsHistory")));
        Assert.False(options.IsVisible(new DbObjectName("dbo", "AspNetUserRoles")));
        Assert.True(options.IsVisible(new DbObjectName("dbo", "Orders")));
    }

    [Fact]
    public void HideTables_umi_i_kvalifikovane_jmeno()
    {
        var options = new SchemaReadOptions { HideTables = ["audit.*"] };

        Assert.False(options.IsVisible(new DbObjectName("audit", "Changes")));
        Assert.True(options.IsVisible(new DbObjectName("dbo", "Changes")));
    }

    [Fact]
    public void IncludeSchemas_propusti_jen_uvedena_schemata()
    {
        var options = new SchemaReadOptions { IncludeSchemas = ["sales", "dbo"] };

        Assert.True(options.IsVisible(new DbObjectName("sales", "Orders")));
        Assert.True(options.IsVisible(new DbObjectName("DBO", "Orders")));
        Assert.False(options.IsVisible(new DbObjectName("audit", "Changes")));
    }

    [Fact]
    public void IncludeSchemas_bere_tabulku_bez_schematu_jako_prazdne_schema()
    {
        var withEmpty = new SchemaReadOptions { IncludeSchemas = [""] };
        Assert.True(withEmpty.IsVisible(new DbObjectName(null, "Orders")));

        var withDbo = new SchemaReadOptions { IncludeSchemas = ["dbo"] };
        Assert.False(withDbo.IsVisible(new DbObjectName(null, "Orders")));
    }

    [Fact]
    public void HideTables_ma_prednost_pred_IncludeSchemas()
    {
        var options = new SchemaReadOptions
        {
            IncludeSchemas = ["dbo"],
            HideTables = ["Orders"],
        };

        Assert.False(options.IsVisible(new DbObjectName("dbo", "Orders")));
    }
}
