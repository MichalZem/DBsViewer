using DbsViewer.TestKit;

namespace DbsViewer.Tests.Abstractions;

/// <summary>
/// Odvození cesty z nadřazeného řádku na jeho podřízené záznamy. Na tomhle stojí
/// šipka v mřížce dat — nabídne se právě tam, kde z řádku jde poskládat podmínka.
/// </summary>
public class ChildLinkTests
{
    private static DatabaseSchema Schema(params DbTable[] tables) => new() { Tables = tables };

    private static DbObjectName Jmeno(string name) => new(null, name);

    /// <summary>Zákazník, na kterého se odkazují objednávky.</summary>
    private static DatabaseSchema Eshop() => Schema(
        Build.Table("Customers", ["Id", "Email"], primaryKey: ["Id"]),
        Build.Table(
            "Orders",
            ["Id", "CustomerId"],
            primaryKey: ["Id"],
            foreignKeys: [Build.ForeignKey("FK_Orders_Customers", ["CustomerId"], "Customers")]));

    [Fact]
    public void Cizi_klic_do_tabulky_je_odkaz_na_podrizene_zaznamy()
    {
        var odkazy = ChildLinks.For(Eshop(), Jmeno("Customers"));

        var odkaz = Assert.Single(odkazy);

        Assert.Equal(Jmeno("Orders"), odkaz.Table);
        Assert.Equal("FK_Orders_Customers", odkaz.ForeignKeyName);
        Seq.Equal(["CustomerId"], odkaz.Columns);
        Seq.Equal(["Id"], odkaz.ParentColumns);
    }

    [Fact]
    public void Tabulka_bez_prichozich_klicu_nema_odkazy()
    {
        Assert.Empty(ChildLinks.For(Eshop(), Jmeno("Orders")));
    }

    [Fact]
    public void Odkaz_visi_u_sloupce_na_ktery_klic_ukazuje()
    {
        var odkaz = ChildLinks.For(Eshop(), Jmeno("Customers"))[0];

        Assert.Equal("Id", odkaz.AnchorColumn);
    }

    [Fact]
    public void Slozeny_klic_se_kotvi_u_prvniho_sloupce()
    {
        var schema = Schema(
            Build.Table("Sklady", ["Region", "Kod"], primaryKey: ["Region", "Kod"]),
            Build.Table(
                "Zasoby",
                ["Region", "Kod", "Kus"],
                foreignKeys:
                [
                    Build.ForeignKey("FK_Zasoby", ["Region", "Kod"], "Sklady", ["Region", "Kod"]),
                ]));

        var odkaz = ChildLinks.For(schema, Jmeno("Sklady"))[0];

        Assert.Equal("Region", odkaz.AnchorColumn);
        Seq.Equal(["Region", "Kod"], odkaz.Columns);
    }

    [Fact]
    public void Dve_vazby_mezi_temiz_tabulkami_jsou_dva_odkazy()
    {
        var schema = Schema(
            Build.Table("Lide", ["Id"], primaryKey: ["Id"]),
            Build.Table(
                "Zpravy",
                ["Id", "OdesilatelId", "PrijemceId"],
                foreignKeys:
                [
                    Build.ForeignKey("FK_Odesilatel", ["OdesilatelId"], "Lide"),
                    Build.ForeignKey("FK_Prijemce", ["PrijemceId"], "Lide"),
                ]));

        var odkazy = ChildLinks.For(schema, Jmeno("Lide"));

        Assert.Equal(2, odkazy.Count);
        Seq.Equal(["FK_Odesilatel", "FK_Prijemce"], [.. odkazy.Select(static o => o.ForeignKeyName)]);
    }

    [Fact]
    public void Odkaz_na_sebe_sama_se_nabizi_taky()
    {
        var schema = Schema(
            Build.Table(
                "Kategorie",
                ["Id", "RodicId"],
                primaryKey: ["Id"],
                foreignKeys: [Build.ForeignKey("FK_Rodic", ["RodicId"], "Kategorie")]));

        var odkaz = Assert.Single(ChildLinks.For(schema, Jmeno("Kategorie")));

        Assert.Equal(Jmeno("Kategorie"), odkaz.Table);
    }

    [Fact]
    public void Klic_s_ruznym_poctem_sloupcu_na_stranach_se_preskoci()
    {
        // Platné schéma takový klíč nemá, ale přijít může z rozbitého čtení živé databáze.
        var schema = Schema(
            Build.Table("Sklady", ["Region", "Kod"], primaryKey: ["Region", "Kod"]),
            Build.Table(
                "Zasoby",
                ["Region"],
                foreignKeys: [Build.ForeignKey("FK_Zasoby", ["Region"], "Sklady", ["Region", "Kod"])]));

        Assert.Empty(ChildLinks.For(schema, Jmeno("Sklady")));
    }

    [Fact]
    public void Klic_bez_sloupcu_se_preskoci()
    {
        var schema = Schema(
            Build.Table("Sklady", ["Id"], primaryKey: ["Id"]),
            Build.Table("Zasoby", ["Id"], foreignKeys: [Build.ForeignKey("FK", [], "Sklady", [])]));

        Assert.Empty(ChildLinks.For(schema, Jmeno("Sklady")));
    }

    [Fact]
    public void Odkaz_zna_inverzni_navigaci_z_modelu()
    {
        var schema = Schema(
            Build.Table("Customers", ["Id"], primaryKey: ["Id"]),
            new DbTable
            {
                Name = Jmeno("Orders"),
                ForeignKeys =
                [
                    new DbForeignKey
                    {
                        Name = "FK",
                        Columns = ["CustomerId"],
                        PrincipalTable = Jmeno("Customers"),
                        PrincipalColumns = ["Id"],
                        InverseNavigationName = "Orders",
                    },
                ],
            });

        Assert.Equal("Orders", ChildLinks.For(schema, Jmeno("Customers"))[0].NavigationName);
    }

    [Fact]
    public void Schema_je_povinne()
    {
        Assert.Throws<ArgumentNullException>(() => ChildLinks.For(null!, Jmeno("Customers")));
    }

    [Fact]
    public void At_vybere_odkazy_jednoho_sloupce_bez_ohledu_na_velikost_pismen()
    {
        var odkazy = ChildLinks.For(Eshop(), Jmeno("Customers"));

        Assert.Single(ChildLinks.At(odkazy, "id"));
        Assert.Empty(ChildLinks.At(odkazy, "Email"));
    }

    [Fact]
    public void At_potrebuje_seznam()
    {
        Assert.Throws<ArgumentNullException>(() => ChildLinks.At(null!, "Id"));
    }

    [Fact]
    public void Podminka_vezme_hodnotu_z_nadrazeneho_radku()
    {
        var odkaz = ChildLinks.For(Eshop(), Jmeno("Customers"))[0];

        var podminka = odkaz.FilterFor(sloupec => sloupec == "Id" ? "42" : null);

        var jedna = Assert.Single(podminka!);

        Assert.Equal("CustomerId", jedna.Column);
        Assert.Equal("42", jedna.Value);
    }

    [Fact]
    public void Podminka_ze_slozeneho_klice_ma_vsechny_sloupce()
    {
        var schema = Schema(
            Build.Table("Sklady", ["Region", "Kod"], primaryKey: ["Region", "Kod"]),
            Build.Table(
                "Zasoby",
                ["Region", "Kod"],
                foreignKeys:
                [
                    Build.ForeignKey("FK", ["SkladRegion", "SkladKod"], "Sklady", ["Region", "Kod"]),
                ]));

        var podminka = ChildLinks.For(schema, Jmeno("Sklady"))[0]
            .FilterFor(sloupec => sloupec == "Region" ? "CZ" : "A1");

        Assert.NotNull(podminka);
        Seq.Equal(["SkladRegion", "SkladKod"], [.. podminka.Select(static p => p.Column)]);
        Seq.Equal(["CZ", "A1"], [.. podminka.Select(static p => p.Value)]);
    }

    [Fact]
    public void Nadrazena_hodnota_NULL_odkaz_rusi()
    {
        // Cizí klíč se na NULL nenaváže, takže by výsledek byl vždycky prázdný.
        var odkaz = ChildLinks.For(Eshop(), Jmeno("Customers"))[0];

        Assert.Null(odkaz.FilterFor(static _ => null));
    }

    [Fact]
    public void Podminka_potrebuje_funkci()
    {
        var odkaz = ChildLinks.For(Eshop(), Jmeno("Customers"))[0];

        Assert.Throws<ArgumentNullException>(() => odkaz.FilterFor(null!));
    }

    [Fact]
    public void Odkaz_se_vypise_i_se_sloupci()
    {
        var odkaz = ChildLinks.For(Eshop(), Jmeno("Customers"))[0];

        Assert.Equal("Orders (CustomerId) → Id", odkaz.ToString());
    }
}
