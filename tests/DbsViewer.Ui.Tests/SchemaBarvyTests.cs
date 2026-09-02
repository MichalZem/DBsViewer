using DbsViewer.Ui.Model;

namespace DbsViewer.Tests.Ui;

/// <summary>
/// Barva databázového schématu. Musí být stabilní: kdyby se měnila mezi načteními,
/// bylo by barevné odlišení k ničemu.
/// </summary>
public class SchemaBarvyTests
{
    [Fact]
    public void Stejne_schema_ma_vzdy_stejnou_barvu()
    {
        // String.GetHashCode se v .NET mezi spuštěními randomizuje, proto vlastní
        // hash — tenhle test je pojistka, že se na něj někdo nevrátí.
        Assert.Equal(SchemaBarvy.Odstin("prodej"), SchemaBarvy.Odstin("prodej"));
        Assert.Equal(SchemaBarvy.Odstin("dbo"), SchemaBarvy.Odstin("dbo"));
    }

    [Fact]
    public void Barva_nezavisi_na_velikosti_pismen()
    {
        // Jména objektů se v DbsVieweru porovnávají bez ohledu na casing;
        // barva se tomu musí přizpůsobit, jinak by „dbo" a „DBO" svítily jinak.
        Assert.Equal(SchemaBarvy.Odstin("dbo"), SchemaBarvy.Odstin("DBO"));
        Assert.Equal(SchemaBarvy.Odstin("Prodej"), SchemaBarvy.Odstin("prodej"));
    }

    [Fact]
    public void Ruzna_schemata_maji_ruzne_barvy()
    {
        var odstiny = new[] { "dbo", "prodej", "sklad", "hr", "audit", "archiv" }
            .Select(SchemaBarvy.Odstin)
            .ToList();

        Assert.Equal(odstiny.Count, odstiny.Distinct().Count());
    }

    [Fact]
    public void Odstin_je_v_platnem_rozsahu()
    {
        foreach (var schema in new[] { "a", "dbo", "velmi_dlouhe_jmeno_schematu", "X" })
        {
            var odstin = SchemaBarvy.Odstin(schema);

            Assert.InRange(odstin, 0, 359);
        }
    }

    [Fact]
    public void Prazdne_schema_barvu_nedostane()
    {
        Assert.Equal(0, SchemaBarvy.Odstin(null));
        Assert.Equal(0, SchemaBarvy.Odstin(""));

        Assert.Null(SchemaBarvy.Styl(null));
        Assert.Null(SchemaBarvy.Styl(""));
    }

    [Fact]
    public void Styl_nese_odstin_jako_promennou()
    {
        // Předává se jen odstín; sytost a světlost dopočítá stylopis podle tématu.
        var styl = SchemaBarvy.Styl("prodej");

        Assert.StartsWith("--schema-odstin: ", styl!, StringComparison.Ordinal);
        Assert.Contains(SchemaBarvy.Odstin("prodej").ToString(System.Globalization.CultureInfo.InvariantCulture), styl, StringComparison.Ordinal);
    }
}
