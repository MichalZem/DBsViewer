using DbsViewer.EfCore;
using DbsViewer.SampleShop;
using DbsViewer.TestKit;

namespace DbsViewer.Tests.EfCore;

public class KeyOrderTests
{
    [Fact]
    public void Slozeny_klic_dostane_pozice_v_poradi_definice()
    {
        var order = EfModelReader.BuildKeyOrder(new DbPrimaryKey { Columns = ["OrderId", "LineNumber"] });

        Assert.Equal(0, order["OrderId"]);
        Assert.Equal(1, order["LineNumber"]);
        Assert.Equal(2, order.Count);
    }

    [Fact]
    public void Pozice_se_hledaji_bez_ohledu_na_velikost_pismen() =>
        Assert.Equal(0, EfModelReader.BuildKeyOrder(new DbPrimaryKey { Columns = ["OrderId"] })["ORDERID"]);

    [Fact]
    public void Tabulka_bez_klice_nema_zadne_pozice()
    {
        Assert.Empty(EfModelReader.BuildKeyOrder(null));
        Assert.Empty(EfModelReader.BuildKeyOrder(new DbPrimaryKey { Columns = [] }));
    }
}

public class DescendingNormalizationTests
{
    [Fact]
    public void Nenastaveny_smer_znamena_vse_vzestupne() =>
        Assert.Empty(EfModelReader.NormalizeDescending(null, 3));

    [Fact]
    public void Prazdny_seznam_znamena_vse_sestupne() =>
        Seq.Equal([true, true, true], EfModelReader.NormalizeDescending([], 3));

    [Fact]
    public void Smesany_smer_se_zachova_sloupec_po_sloupci() =>
        Seq.Equal([false, true], EfModelReader.NormalizeDescending([false, true], 2));

    [Fact]
    public void Prazdny_index_zustane_prazdny() =>
        Assert.Empty(EfModelReader.NormalizeDescending([], 0));
}

public class ViewFilteringTests
{
    [Fact]
    public async Task Skryty_pohled_se_do_schematu_nedostane()
    {
        await using var context = ShopContextFactory.CreateSqlite();
        var schema = await new EfCoreModelSchemaSource(context).ReadAsync(new SchemaReadOptions
        {
            IncludeMigrations = false,
            HideTables = ["OrderSummaries"],
        });

        Assert.DoesNotContain(schema.Tables, t => t.IsView);
        Assert.NotEmpty(schema.Tables);
    }

    [Fact]
    public async Task IncludeSchemas_odfiltruje_i_pohledy()
    {
        await using var context = ShopContextFactory.CreateSqlite();
        var schema = await new EfCoreModelSchemaSource(context).ReadAsync(new SchemaReadOptions
        {
            IncludeMigrations = false,
            IncludeSchemas = ["neexistujici"],
        });

        Assert.Empty(schema.Tables);
        Assert.Empty(schema.Relationships);
    }
}
