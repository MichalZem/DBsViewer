using DbsViewer.Ui.Model;

namespace DbsViewer.Tests.Ui;

/// <summary>
/// Vedení hran kolem tabulek. Naivní trasa „půl cesty a zalomit" procházela uzly,
/// které jí stály v cestě — tyhle testy hlídají, že se jim router vyhne.
/// </summary>
public class EdgeRouterTests
{
    [Fact]
    public void Volna_cesta_vede_primo()
    {
        var trasa = EdgeRouter.Route((100, 50), EdgeSide.Right, (300, 50), EdgeSide.Left, []);

        Assert.Equal((100, 50), trasa[0]);
        Assert.Equal((300, 50), trasa[^1]);

        // Nic nestojí v cestě a oba konce jsou ve stejné výšce, takže stačí úsečka.
        Assert.Equal(2, trasa.Count);
    }

    [Fact]
    public void Trasa_obejde_prekazku_v_ceste()
    {
        // Uzel přesně mezi konci — přímá čára by vedla skrz.
        var prekazka = new RouteObstacle(150, 20, 100, 60);

        var trasa = EdgeRouter.Route((100, 50), EdgeSide.Right, (350, 50), EdgeSide.Left, [prekazka]);

        Assert.All(Useky(trasa), usek =>
            Assert.False(
                EdgeRouter.Blocked(usek.A, usek.B, [prekazka]),
                $"Úsek {usek.A} → {usek.B} prochází tabulkou."));
    }

    [Fact]
    public void Trasa_je_vzdy_ortogonalni()
    {
        var prekazky = new[]
        {
            new RouteObstacle(150, 0, 80, 90),
            new RouteObstacle(150, 120, 80, 90),
        };

        var trasa = EdgeRouter.Route((100, 60), EdgeSide.Right, (400, 160), EdgeSide.Left, prekazky);

        Assert.All(Useky(trasa), usek =>
            Assert.True(
                Math.Abs(usek.A.X - usek.B.X) < 0.5 || Math.Abs(usek.A.Y - usek.B.Y) < 0.5,
                $"Úsek {usek.A} → {usek.B} není osově zarovnaný."));
    }

    [Fact]
    public void Trasa_zacina_i_konci_v_kotvach()
    {
        var trasa = EdgeRouter.Route(
            (100, 50), EdgeSide.Right, (400, 200), EdgeSide.Left, [new RouteObstacle(200, 30, 60, 60)]);

        Assert.Equal((100, 50), trasa[0]);
        Assert.Equal((400, 200), trasa[^1]);
    }

    [Fact]
    public void Zahlcena_mrizka_spadne_na_jednoduchou_trasu()
    {
        // Přes sto kandidátních linií se hledání vzdá; hrana ale nesmí zmizet.
        var prekazky = Enumerable
            .Range(0, 80)
            .Select(i => new RouteObstacle(120 + (i * 12), 40, 6, 6))
            .ToList();

        var trasa = EdgeRouter.Route((100, 50), EdgeSide.Right, (1200, 50), EdgeSide.Left, prekazky);

        Assert.Equal((100, 50), trasa[0]);
        Assert.Equal((1200, 50), trasa[^1]);
    }

    [Fact]
    public void Neprujezdna_situace_stale_vrati_trasu()
    {
        // Cíl uvnitř překážky: každý úsek z něj ven je zablokovaný, takže cesta
        // neexistuje. Hrana přesto nesmí zmizet — vykreslí se náhradní trasou.
        var prekazky = new[] { new RouteObstacle(200, 0, 200, 300) };

        var trasa = EdgeRouter.Route((100, 150), EdgeSide.Right, (300, 150), EdgeSide.Left, prekazky);

        Assert.Equal((100, 150), trasa[0]);
        Assert.Equal((300, 150), trasa[^1]);
    }

    [Fact]
    public void Stub_vede_kolmo_ven_z_uzlu()
    {
        Assert.Equal((118, 50), EdgeRouter.Stub((100, 50), EdgeSide.Right));
        Assert.Equal((82, 50), EdgeRouter.Stub((100, 50), EdgeSide.Left));
    }

    [Fact]
    public void Dotyk_okraje_neni_prujezd()
    {
        var prekazka = new RouteObstacle(100, 100, 50, 50);

        // Čára vedená přesně po okraji projde, čára vnitřkem ne.
        Assert.False(EdgeRouter.Blocked((0, 100), (200, 100), [prekazka]));
        Assert.True(EdgeRouter.Blocked((0, 125), (200, 125), [prekazka]));
    }

    [Fact]
    public void Svisly_usek_se_testuje_stejne_jako_vodorovny()
    {
        var prekazka = new RouteObstacle(100, 100, 50, 50);

        Assert.True(EdgeRouter.Blocked((125, 0), (125, 200), [prekazka]));
        Assert.False(EdgeRouter.Blocked((80, 0), (80, 200), [prekazka]));
    }

    [Fact]
    public void Prekazka_mimo_usek_neblokuje()
    {
        var prekazka = new RouteObstacle(500, 500, 50, 50);

        Assert.False(EdgeRouter.Blocked((0, 0), (100, 0), [prekazka]));
    }

    [Fact]
    public void Zjednoduseni_vyhodi_body_na_primce()
    {
        IReadOnlyList<(double X, double Y)> body =
        [
            (0, 0),
            (10, 0),
            (20, 0),
            (20, 10),
            (20, 20),
        ];

        var zjednoduseno = EdgeRouter.Simplify(body);

        Assert.Equal([(0, 0), (20, 0), (20, 20)], zjednoduseno);
    }

    [Fact]
    public void Zjednoduseni_vyhodi_zdvojene_body()
    {
        IReadOnlyList<(double X, double Y)> body = [(0, 0), (0, 0), (10, 0)];

        Assert.Equal([(0, 0), (10, 0)], EdgeRouter.Simplify(body));
    }

    [Fact]
    public void Zjednoduseni_zachova_zatacky()
    {
        IReadOnlyList<(double X, double Y)> body = [(0, 0), (10, 0), (10, 10), (20, 10)];

        Assert.Equal(4, EdgeRouter.Simplify(body).Count);
    }

    [Fact]
    public void Obdelnik_zna_svoje_okraje()
    {
        var o = new RouteObstacle(10, 20, 100, 50);

        Assert.Equal(110, o.Right);
        Assert.Equal(70, o.Bottom);
    }

    [Fact]
    public void Null_prekazky_jsou_chyba_argumentu()
    {
        Assert.Throws<ArgumentNullException>(
            () => EdgeRouter.Route((0, 0), EdgeSide.Right, (10, 0), EdgeSide.Left, null!));

        Assert.Throws<ArgumentNullException>(() => EdgeRouter.Simplify(null!));
    }


    [Fact]
    public void Soubezne_useky_se_poznaji_jako_prekryv()
    {
        // Dvě vodorovné čáry ve stejné výšce přes stejný úsek by v diagramu splynuly.
        Assert.True(EdgeRouter.Overlaps((0, 50), (100, 50), (40, 50), (140, 50)));

        // Stejná výška, ale bez společného úseku.
        Assert.False(EdgeRouter.Overlaps((0, 50), (30, 50), (60, 50), (100, 50)));

        // Dost daleko od sebe, aby šly rozeznat.
        Assert.False(EdgeRouter.Overlaps((0, 50), (100, 50), (0, 70), (100, 70)));
    }

    [Fact]
    public void Svisly_prekryv_se_pozna_stejne()
    {
        Assert.True(EdgeRouter.Overlaps((50, 0), (50, 100), (50, 40), (50, 140)));
        Assert.False(EdgeRouter.Overlaps((50, 0), (50, 100), (80, 0), (80, 100)));
    }

    [Fact]
    public void Kolmy_usek_neni_prekryv() =>
        Assert.False(EdgeRouter.Overlaps((0, 50), (100, 50), (50, 0), (50, 100)));

    [Fact]
    public void Krizeni_se_pozna()
    {
        Assert.True(EdgeRouter.Crosses((0, 50), (100, 50), (50, 0), (50, 100)));

        // Svislý úsek končí nad vodorovným, takže se míjejí.
        Assert.False(EdgeRouter.Crosses((0, 50), (100, 50), (50, 0), (50, 20)));

        // Vodorovný úsek nedosáhne k svislému.
        Assert.False(EdgeRouter.Crosses((0, 50), (30, 50), (50, 0), (50, 100)));
    }

    [Fact]
    public void Rovnobezne_useky_se_nekrizi() =>
        Assert.False(EdgeRouter.Crosses((0, 50), (100, 50), (0, 80), (100, 80)));

    [Fact]
    public void Prirazka_roste_s_poctem_konfliktu()
    {
        var zadny = EdgeRouter.Conflict((0, 0), (100, 0), []);
        var krizeni = EdgeRouter.Conflict((0, 50), (100, 50), [((50, 0), (50, 100))]);
        var prekryv = EdgeRouter.Conflict((0, 50), (100, 50), [((0, 50), (100, 50))]);

        Assert.Equal(0, zadny);
        Assert.True(krizeni > 0);

        // Souběh je horší než křížení — dvě čáry na téže lince splynou v jednu.
        Assert.True(prekryv > krizeni);
    }

    [Fact]
    public void Trasa_se_vyhne_uz_vedene_hrane()
    {
        // Volná cesta vede přímo; s obsazenou linkou musí druhá hrana uhnout.
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> hotove =
            [[(118, 50), (282, 50)]];

        var trasa = EdgeRouter.Route(
            (100, 50), EdgeSide.Right, (300, 50), EdgeSide.Left, [], hotove);

        Assert.Equal((100, 50), trasa[0]);
        Assert.Equal((300, 50), trasa[^1]);
        Assert.True(trasa.Count > 2, "Druhá hrana zůstala na téže lince jako první.");
    }

    private static IEnumerable<((double X, double Y) A, (double X, double Y) B)> Useky(
        IReadOnlyList<(double X, double Y)> trasa)
    {
        for (var i = 1; i < trasa.Count; i++)
        {
            yield return (trasa[i - 1], trasa[i]);
        }
    }
}
