namespace DbsViewer.Tests.Abstractions;

public class DbObjectNameTests
{
    [Fact]
    public void Prazdne_schema_se_normalizuje_na_null()
    {
        Assert.Null(new DbObjectName("", "Orders").Schema);
        Assert.Null(new DbObjectName(null, "Orders").Schema);
    }

    [Fact]
    public void Prazdne_jmeno_je_chyba()
    {
        Assert.Throws<ArgumentException>(() => new DbObjectName("dbo", ""));
        Assert.Throws<ArgumentNullException>(() => new DbObjectName("dbo", null!));
    }

    [Theory]
    [InlineData(null, "Orders", "Orders")]
    [InlineData("dbo", "Orders", "dbo.Orders")]
    public void Qualified_sklada_schema_a_jmeno(string? schema, string name, string expected)
    {
        var objectName = new DbObjectName(schema, name);

        Assert.Equal(expected, objectName.Qualified);
        Assert.Equal(expected, objectName.ToString());
    }

    [Fact]
    public void Rovnost_ignoruje_velikost_pismen()
    {
        var lower = new DbObjectName("dbo", "orders");
        var upper = new DbObjectName("DBO", "ORDERS");

        Assert.Equal(lower, upper);
        Assert.True(lower == upper);
        Assert.False(lower != upper);
        Assert.Equal(lower.GetHashCode(), upper.GetHashCode());
    }

    [Fact]
    public void Rovnost_rozlisuje_schema_i_jmeno()
    {
        var orders = new DbObjectName("dbo", "Orders");

        Assert.NotEqual(orders, new DbObjectName("sales", "Orders"));
        Assert.NotEqual(orders, new DbObjectName("dbo", "Customers"));
        Assert.NotEqual(orders, new DbObjectName(null, "Orders"));
    }

    [Fact]
    public void Hashkod_bez_schematu_je_stabilni()
    {
        Assert.Equal(
            new DbObjectName(null, "Orders").GetHashCode(),
            new DbObjectName("", "ORDERS").GetHashCode());
    }

    [Fact]
    public void Porovnani_radi_nejdriv_podle_schematu()
    {
        var a = new DbObjectName("alpha", "Zebra");
        var b = new DbObjectName("beta", "Apple");

        Assert.True(a.CompareTo(b) < 0);
        Assert.True(b.CompareTo(a) > 0);
    }

    [Fact]
    public void Porovnani_pri_shodnem_schematu_radi_podle_jmena()
    {
        var a = new DbObjectName("dbo", "Apple");
        var b = new DbObjectName("dbo", "Zebra");

        Assert.True(a.CompareTo(b) < 0);
        Assert.Equal(0, a.CompareTo(new DbObjectName("DBO", "APPLE")));
    }

    [Fact]
    public void Razeni_pouziva_porovnani()
    {
        DbObjectName[] names =
        [
            new("dbo", "Orders"),
            new(null, "Audit"),
            new("dbo", "Customers"),
        ];

        Array.Sort(names);

        Assert.Equal("Audit", names[0].Name);
        Assert.Equal("Customers", names[1].Name);
        Assert.Equal("Orders", names[2].Name);
    }
}
