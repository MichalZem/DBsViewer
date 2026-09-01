using DbsViewer.Ui.Model;

namespace DbsViewer.Tests.Ui;

/// <summary>Převod trasy na SVG path se zaoblenými rohy.</summary>
public class EdgePathTests
{
    [Fact]
    public void Prazdna_trasa_da_prazdny_retezec() =>
        Assert.Equal(string.Empty, EdgePath.Build([]));

    [Fact]
    public void Null_trasa_je_chyba_argumentu() =>
        Assert.Throws<ArgumentNullException>(() => EdgePath.Build(null!));

    [Fact]
    public void Primka_nema_zadny_oblouk()
    {
        var d = EdgePath.Build([(0, 0), (100, 0)]);

        Assert.Equal("M0 0L100 0", d);
        Assert.DoesNotContain('Q', d);
    }

    [Fact]
    public void Roh_se_zaobli_kvadratickou_krivkou()
    {
        var d = EdgePath.Build([(0, 0), (100, 0), (100, 100)]);

        // Úsek se zkrátí o poloměr, roh je řídicím bodem oblouku.
        Assert.Equal("M0 0L92 0Q100 0 100 8L100 100", d);
    }

    [Fact]
    public void Kratky_usek_dostane_mensi_oblouk()
    {
        // Prostřední úsek je 10 px, takže oblouk může být nejvýš 5 — jinak by se
        // dva oblouky na jednom úseku prolnuly.
        var d = EdgePath.Build([(0, 0), (100, 0), (100, 10), (200, 10)]);

        Assert.Contains("L95 0", d, StringComparison.Ordinal);
        Assert.Contains("Q100 0 100 5", d, StringComparison.Ordinal);
    }

    [Fact]
    public void Zdvojeny_bod_nerozbije_trasu()
    {
        var d = EdgePath.Build([(0, 0), (50, 0), (50, 0), (50, 50)]);

        Assert.StartsWith("M0 0", d, StringComparison.Ordinal);
        Assert.EndsWith("50 50", d, StringComparison.Ordinal);
    }

    [Fact]
    public void Jediny_bod_da_jen_presun() =>
        Assert.Equal("M10 20", EdgePath.Build([(10, 20)]));

    [Fact]
    public void Cisla_maji_desetinnou_tecku()
    {
        // Ve WebAssembly běží invariantní kultura, ale server může mít jakoukoli —
        // desetinná čárka by atribut d rozbila.
        var d = EdgePath.Build([(0.5, 1.25), (10.75, 1.25)]);

        Assert.DoesNotContain(',', d);
        Assert.Contains("0.5", d, StringComparison.Ordinal);
    }
}
