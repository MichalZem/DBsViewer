using DbsViewer.TestKit;

namespace DbsViewer.Tests.Abstractions;

public class JoinTableDetectorTests
{
    private static DbTable JoinTable() => Build.Table(
        "ProductTags",
        columns: ["ProductId", "TagId"],
        primaryKey: ["ProductId", "TagId"],
        foreignKeys:
        [
            Build.ForeignKey("FK_A", ["ProductId"], "Products"),
            Build.ForeignKey("FK_B", ["TagId"], "Tags"),
        ]);

    [Fact]
    public void Vazebni_tabulka_se_pozna() => Assert.True(JoinTableDetector.IsJoinTable(JoinTable()));

    [Fact]
    public void Tabulka_s_vlastnim_sloupcem_neni_vazebni()
    {
        var table = Build.Table(
            "Assignments",
            columns: ["TeamId", "PersonId", "Role"],
            primaryKey: ["TeamId", "PersonId"],
            foreignKeys:
            [
                Build.ForeignKey("FK_A", ["TeamId"], "Teams"),
                Build.ForeignKey("FK_B", ["PersonId"], "People"),
            ]);

        Assert.False(JoinTableDetector.IsJoinTable(table));
    }

    [Fact]
    public void Tabulka_s_jednim_cizim_klicem_neni_vazebni()
    {
        var table = Build.Table(
            "Orders",
            columns: ["CustomerId"],
            primaryKey: ["CustomerId"],
            foreignKeys: [Build.ForeignKey("FK_A", ["CustomerId"], "Customers")]);

        Assert.False(JoinTableDetector.IsJoinTable(table));
    }

    [Fact]
    public void Tabulka_bez_klice_neni_vazebni()
    {
        var table = Build.Table(
            "Log",
            columns: ["A", "B"],
            foreignKeys:
            [
                Build.ForeignKey("FK_A", ["A"], "X"),
                Build.ForeignKey("FK_B", ["B"], "Y"),
            ]);

        Assert.False(JoinTableDetector.IsJoinTable(table));
    }

    [Fact]
    public void Klic_z_jedineho_sloupce_neni_vazba()
    {
        var table = Build.Table(
            "T",
            columns: ["A", "B"],
            primaryKey: ["A"],
            foreignKeys:
            [
                Build.ForeignKey("FK_A", ["A"], "X"),
                Build.ForeignKey("FK_B", ["B"], "Y"),
            ]);

        Assert.False(JoinTableDetector.IsJoinTable(table));
    }

    [Fact]
    public void Klic_mimo_cizi_klice_neni_vazba()
    {
        var table = Build.Table(
            "T",
            columns: ["A", "B", "C"],
            primaryKey: ["A", "C"],
            foreignKeys:
            [
                Build.ForeignKey("FK_A", ["A"], "X"),
                Build.ForeignKey("FK_B", ["B"], "Y"),
            ]);

        Assert.False(JoinTableDetector.IsJoinTable(table));
    }

    [Fact]
    public void Pohled_neni_vazebni_tabulka()
    {
        var view = Build.Table(
            "V",
            columns: ["A", "B"],
            primaryKey: ["A", "B"],
            isView: true,
            foreignKeys:
            [
                Build.ForeignKey("FK_A", ["A"], "X"),
                Build.ForeignKey("FK_B", ["B"], "Y"),
            ]);

        Assert.False(JoinTableDetector.IsJoinTable(view));
    }

    [Fact]
    public void Detekce_najde_vsechny_vazebni_tabulky()
    {
        var detected = JoinTableDetector.Detect([JoinTable(), Build.Table("Products", ["Id"], ["Id"])]);

        Assert.Equal(Build.Names("ProductTags"), detected);
    }

    [Fact]
    public void Chybejici_vstup_je_chyba()
    {
        Assert.Throws<ArgumentNullException>(() => JoinTableDetector.Detect(null!));
        Assert.Throws<ArgumentNullException>(() => JoinTableDetector.IsJoinTable(null!));
    }
}

public class RelationshipBuilderTests
{
    [Fact]
    public void Cizi_klic_bez_unikatnosti_je_vztah_1_N()
    {
        var tables = new[]
        {
            Build.Table("Customers", ["Id"], ["Id"]),
            Build.Table("Orders", ["Id", "CustomerId"], ["Id"],
                [Build.ForeignKey("FK", ["CustomerId"], "Customers", delete: DbDeleteBehavior.Cascade)]),
        };

        var relationship = Assert.Single(
            RelationshipBuilder.Build(tables, Build.Names("Customers", "Orders"), Build.Names()));

        Assert.Equal(DbCardinality.OneToMany, relationship.Cardinality);
        Assert.Equal("Orders", relationship.From.Name);
        Assert.Equal("Customers", relationship.To.Name);
        Assert.Equal(DbDeleteBehavior.Cascade, relationship.DeleteBehavior);
        Assert.True(relationship.IsRequired);
        Assert.False(relationship.IsIdentifying);
    }

    [Fact]
    public void Cizi_klic_pokryty_unikatnim_indexem_je_vztah_1_1()
    {
        var tables = new[]
        {
            Build.Table("Customers", ["Id"], ["Id"]),
            Build.Table("Profiles", ["Id", "CustomerId"], ["Id"],
                [Build.ForeignKey("FK", ["CustomerId"], "Customers")],
                [Build.Index("UX", ["CustomerId"], isUnique: true)]),
        };

        var relationship = Assert.Single(
            RelationshipBuilder.Build(tables, Build.Names("Customers", "Profiles"), Build.Names()));

        Assert.Equal(DbCardinality.OneToOne, relationship.Cardinality);
    }

    [Fact]
    public void Cizi_klic_shodny_s_primarnim_klicem_je_1_1_a_identifikujici()
    {
        var tables = new[]
        {
            Build.Table("Customers", ["Id"], ["Id"]),
            Build.Table("Profiles", ["CustomerId"], ["CustomerId"],
                [Build.ForeignKey("FK", ["CustomerId"], "Customers")]),
        };

        var relationship = Assert.Single(
            RelationshipBuilder.Build(tables, Build.Names("Customers", "Profiles"), Build.Names()));

        Assert.Equal(DbCardinality.OneToOne, relationship.Cardinality);
        Assert.True(relationship.IsIdentifying);
    }

    [Fact]
    public void Priznak_unikatnosti_na_klici_staci_i_bez_indexu()
    {
        var tables = new[]
        {
            Build.Table("A", ["Id"], ["Id"]),
            Build.Table("B", ["Id", "AId"], ["Id"],
                [Build.ForeignKey("FK", ["AId"], "A", isUnique: true)]),
        };

        var relationship = Assert.Single(
            RelationshipBuilder.Build(tables, Build.Names("A", "B"), Build.Names()));

        Assert.Equal(DbCardinality.OneToOne, relationship.Cardinality);
    }

    [Fact]
    public void Nepovinna_vazba_se_pozna_podle_nullable_sloupce()
    {
        var tables = new[]
        {
            Build.Table("Categories", ["Id"], ["Id"]),
            Build.Table("Sub", ["Id", "ParentId"], ["Id"],
                [Build.ForeignKey("FK", ["ParentId"], "Categories")],
                nullable: [false, true]),
        };

        var relationship = Assert.Single(
            RelationshipBuilder.Build(tables, Build.Names("Categories", "Sub"), Build.Names()));

        Assert.False(relationship.IsRequired);
    }

    [Fact]
    public void Skryta_tabulka_odstrani_vztah()
    {
        var tables = new[]
        {
            Build.Table("Customers", ["Id"], ["Id"]),
            Build.Table("Orders", ["Id", "CustomerId"], ["Id"],
                [Build.ForeignKey("FK", ["CustomerId"], "Customers")]),
        };

        Assert.Empty(RelationshipBuilder.Build(tables, Build.Names("Orders"), Build.Names()));
        Assert.Empty(RelationshipBuilder.Build(tables, Build.Names("Customers"), Build.Names()));
    }

    [Fact]
    public void Vazebni_tabulka_se_sbali_do_jedine_hrany()
    {
        var relationship = Assert.Single(RelationshipBuilder.Build(
            JoinTableModel(),
            Build.Names("Products", "Tags", "ProductTags"),
            Build.Names("ProductTags")));

        Assert.Equal(DbCardinality.ManyToMany, relationship.Cardinality);
        Assert.Equal("Products", relationship.From.Name);
        Assert.Equal("Tags", relationship.To.Name);
        Assert.Equal("ProductTags", relationship.ViaJoinTable!.Value.Name);
        Assert.True(relationship.IsRequired);
        Assert.StartsWith("m2m:", relationship.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void Sbaleni_se_zrusi_kdyz_je_jedna_strana_skryta()
    {
        var relationships = RelationshipBuilder.Build(
            JoinTableModel(),
            Build.Names("Products", "ProductTags"),
            Build.Names("ProductTags"));

        // Vazba se sbalit nedá, takže se vykreslí zbylý cizí klíč jako běžný vztah.
        var single = Assert.Single(relationships);
        Assert.Equal(DbCardinality.OneToMany, single.Cardinality);
        Assert.Equal("Products", single.To.Name);
    }

    [Fact]
    public void Identifikator_vztahu_NM_neni_zavisly_na_poradi_stran()
    {
        var products = new DbObjectName(null, "Products");
        var tags = new DbObjectName(null, "Tags");
        var join = new DbObjectName(null, "ProductTags");

        Assert.Equal(
            RelationshipBuilder.ManyToManyId(products, tags, join),
            RelationshipBuilder.ManyToManyId(tags, products, join));
    }

    [Fact]
    public void Vztahy_jsou_serazene_podle_identifikatoru()
    {
        var tables = new[]
        {
            Build.Table("A", ["Id"], ["Id"]),
            Build.Table("Z", ["Id", "AId"], ["Id"], [Build.ForeignKey("FK_Z", ["AId"], "A")]),
            Build.Table("B", ["Id", "AId"], ["Id"], [Build.ForeignKey("FK_B", ["AId"], "A")]),
        };

        var ids = RelationshipBuilder.Build(tables, Build.Names("A", "B", "Z"), Build.Names())
            .Select(r => r.Id)
            .ToList();

        Assert.Equal(ids.Order(StringComparer.Ordinal).ToList(), ids);
    }

    [Fact]
    public void Priznak_povinnosti_na_klici_staci_sam_o_sobe()
    {
        // Cizí klíč z EF modelu nese IsRequired přímo; sloupce se pak neprocházejí.
        var table = Build.Table("T", ["AId"], nullable: [true]);
        var foreignKey = Build.ForeignKey("FK", ["AId"], "A") with { IsRequired = true };

        Assert.True(RelationshipBuilder.IsRequired(table, foreignKey));
    }

    [Fact]
    public void Povinnost_vazby_bez_sloupcu_je_nepravda()
    {
        var table = Build.Table("T", ["A"], ["A"]);
        var foreignKey = Build.ForeignKey("FK", [], "X");

        Assert.False(RelationshipBuilder.IsRequired(table, foreignKey));
    }

    [Fact]
    public void Povinnost_vazby_na_nezname_sloupce_je_nepravda()
    {
        var table = Build.Table("T", ["A"], ["A"]);
        var foreignKey = Build.ForeignKey("FK", ["Chybi"], "X");

        Assert.False(RelationshipBuilder.IsRequired(table, foreignKey));
    }

    [Fact]
    public void Chybejici_vstupy_jsou_chyba()
    {
        Assert.Throws<ArgumentNullException>(() =>
            RelationshipBuilder.Build(null!, Build.Names(), Build.Names()));
        Assert.Throws<ArgumentNullException>(() =>
            RelationshipBuilder.Build([], null!, Build.Names()));
        Assert.Throws<ArgumentNullException>(() =>
            RelationshipBuilder.Build([], Build.Names(), null!));
        Assert.Throws<ArgumentNullException>(() =>
            RelationshipBuilder.IsRequired(null!, Build.ForeignKey("FK", [], "X")));
        Assert.Throws<ArgumentNullException>(() =>
            RelationshipBuilder.IsRequired(Build.Table("T"), null!));
    }

    private static DbTable[] JoinTableModel() =>
    [
        Build.Table("Products", ["Id"], ["Id"]),
        Build.Table("Tags", ["Id"], ["Id"]),
        Build.Table("ProductTags", ["ProductId", "TagId"], ["ProductId", "TagId"],
        [
            Build.ForeignKey("FK_P", ["ProductId"], "Products", delete: DbDeleteBehavior.Cascade),
            Build.ForeignKey("FK_T", ["TagId"], "Tags", delete: DbDeleteBehavior.Cascade),
        ]),
    ];
}
