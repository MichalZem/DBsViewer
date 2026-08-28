using DbsViewer.TestKit;
using DbsViewer.Ui.Model;

namespace DbsViewer.Tests.Ui;

public class SchemaFilterTests
{
    private static DbTable Table(string name, string? schema = null, params string[] columns) => new()
    {
        Name = new DbObjectName(schema, name),
        Columns = [.. columns.Select((c, i) => new DbColumn { Name = c, Ordinal = i + 1, StoreType = "int" })],
    };

    private static readonly IReadOnlyList<DbTable> Tables =
    [
        Table("Customers", null, "Id", "Email"),
        Table("Orders", null, "Id", "CustomerId"),
        Table("Products", "sales", "Id", "Name"),
    ];

    [Fact]
    public void Prazdne_hledani_vrati_vse()
    {
        Assert.Same(Tables, SchemaFilter.Search(Tables, null));
        Assert.Same(Tables, SchemaFilter.Search(Tables, ""));
        Assert.Same(Tables, SchemaFilter.Search(Tables, "   "));
    }

    [Fact]
    public void Hleda_se_v_nazvu_tabulky()
    {
        var found = SchemaFilter.Search(Tables, "order");

        Assert.Equal("Orders", Assert.Single(found).Name.Name);
    }

    [Fact]
    public void Hleda_se_i_v_nazvech_sloupcu()
    {
        // Sloupec je často to jediné, co uživatel zná.
        var found = SchemaFilter.Search(Tables, "CustomerId");

        Assert.Equal("Orders", Assert.Single(found).Name.Name);
    }

    [Fact]
    public void Hleda_se_i_ve_schematu()
    {
        var found = SchemaFilter.Search(Tables, "sales");

        Assert.Equal("Products", Assert.Single(found).Name.Name);
    }

    [Fact]
    public void Hleda_se_i_v_CLR_jmenech_entit()
    {
        var table = new DbTable
        {
            Name = new DbObjectName(null, "T"),
            EntityClrNames = ["ObjednavkaEntity"],
        };

        Assert.Single(SchemaFilter.Search([table], "objednavka"));
    }

    [Fact]
    public void Hledani_ignoruje_velikost_pismen_i_okrajove_mezery() =>
        Assert.Single(SchemaFilter.Search(Tables, "  ORDERS  "));

    [Fact]
    public void Nic_nenalezeno_vrati_prazdny_seznam() =>
        Assert.Empty(SchemaFilter.Search(Tables, "neexistuje"));

    [Fact]
    public void Tabulka_bez_shody_v_zadnem_poli_neprojde()
    {
        var table = new DbTable
        {
            Name = new DbObjectName("dbo", "Alfa"),
            EntityClrNames = ["Beta"],
            Columns = [new DbColumn { Name = "Gama", Ordinal = 1, StoreType = "int" }],
        };

        Assert.False(SchemaFilter.Matches(table, "delta"));
        Assert.True(SchemaFilter.Matches(table, "gama"));
        Assert.True(SchemaFilter.Matches(table, "beta"));
    }

    [Fact]
    public void Zvyraznene_sloupce_odpovidaji_hledani()
    {
        var matching = SchemaFilter.MatchingColumns(Tables[1], "id");

        Assert.Equal(2, matching.Count);
        Assert.Contains("CustomerId", matching);
    }

    [Fact]
    public void Bez_hledani_se_nic_nezvyrazni()
    {
        Assert.Empty(SchemaFilter.MatchingColumns(Tables[0], null));
        Assert.Empty(SchemaFilter.MatchingColumns(Tables[0], "  "));
    }

    [Fact]
    public void Skupina_omezi_tabulky_podle_vzoru()
    {
        var found = SchemaFilter.InGroup(Tables, "Order*");

        Assert.Equal("Orders", Assert.Single(found).Name.Name);
    }

    [Fact]
    public void Skupina_umi_i_kvalifikovane_jmeno() =>
        Assert.Equal("Products", Assert.Single(SchemaFilter.InGroup(Tables, "sales.*")).Name.Name);

    [Fact]
    public void Prazdna_skupina_vrati_vse()
    {
        Assert.Same(Tables, SchemaFilter.InGroup(Tables, null));
        Assert.Same(Tables, SchemaFilter.InGroup(Tables, ""));
    }

    [Fact]
    public void Schemata_se_vypisou_serazene_a_bez_prazdneho()
    {
        var schemas = SchemaFilter.Schemas(Tables);

        Assert.Equal(["sales"], schemas.ToList());
    }

    [Fact]
    public void Schemata_se_neopakuji()
    {
        var tables = new[] { Table("A", "dbo"), Table("B", "dbo"), Table("C", "audit") };

        Assert.Equal(["audit", "dbo"], SchemaFilter.Schemas(tables).ToList());
    }

    [Fact]
    public void Chybejici_vstupy_jsou_chyba()
    {
        Assert.Throws<ArgumentNullException>(() => SchemaFilter.Search(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => SchemaFilter.InGroup(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => SchemaFilter.Schemas(null!));
        Assert.Throws<ArgumentNullException>(() => SchemaFilter.MatchingColumns(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => SchemaFilter.Matches(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => SchemaFilter.Matches(Tables[0], null!));
        Assert.Throws<ArgumentException>(() => SchemaFilter.Matches(Tables[0], ""));
    }
}

public class SchemaGraphTests
{
    private static DatabaseSchema Schema() => new()
    {
        Tables =
        [
            Build.Table("Customers", ["Id"], ["Id"]),
            Build.Table("Orders", ["Id", "CustomerId"], ["Id"]),
            Build.Table("OrderLines", ["OrderId", "ProductId"], ["OrderId", "ProductId"]),
            Build.Table("Products", ["Id"], ["Id"]),
            Build.Table("Tags", ["Id"], ["Id"]),
            Build.Table("Osamela", ["Id"], ["Id"]),
        ],
        Relationships =
        [
            Rel("fk:1", "Orders", "Customers"),
            Rel("fk:2", "OrderLines", "Orders"),
            Rel("fk:3", "OrderLines", "Products"),
            Rel("m2m:1", "Products", "Tags", join: "ProductTags"),
        ],
    };

    private static DbRelationship Rel(string id, string from, string to, string? join = null) => new()
    {
        Id = id,
        From = new DbObjectName(null, from),
        To = new DbObjectName(null, to),
        ViaJoinTable = join is null ? null : new DbObjectName(null, join),
        Cardinality = join is null ? DbCardinality.OneToMany : DbCardinality.ManyToMany,
    };

    private static DbObjectName N(string name) => new(null, name);

    [Fact]
    public void Sousedi_jsou_obousmerni()
    {
        var graph = new SchemaGraph(Schema());

        Assert.Contains(N("Customers"), graph.NeighboursOf(N("Orders")));
        Assert.Contains(N("Orders"), graph.NeighboursOf(N("Customers")));
    }

    [Fact]
    public void Neznama_tabulka_nema_sousedy() =>
        Assert.Empty(new SchemaGraph(Schema()).NeighboursOf(N("Neexistuje")));

    [Fact]
    public void Stupen_odpovida_poctu_vazeb()
    {
        var graph = new SchemaGraph(Schema());

        Assert.Equal(2, graph.DegreeOf(N("OrderLines")));
        Assert.Equal(0, graph.DegreeOf(N("Osamela")));
    }

    [Fact]
    public void Self_reference_se_mezi_sousedy_nepocita()
    {
        var schema = new DatabaseSchema
        {
            Tables = [Build.Table("Categories", ["Id", "ParentId"], ["Id"])],
            Relationships = [Rel("fk:self", "Categories", "Categories")],
        };

        Assert.Empty(new SchemaGraph(schema).NeighboursOf(N("Categories")));
    }

    [Fact]
    public void Nula_kroku_vrati_jen_vychozi_tabulku()
    {
        var focus = new SchemaGraph(Schema()).Focus(N("Orders"), 0);

        Assert.Equal([N("Orders")], focus.ToList());
    }

    [Fact]
    public void Jeden_krok_prida_prime_sousedy()
    {
        var focus = new SchemaGraph(Schema()).Focus(N("Orders"), 1);

        Assert.Equal(3, focus.Count);
        Assert.Contains(N("Customers"), focus);
        Assert.Contains(N("OrderLines"), focus);
    }

    [Fact]
    public void Dva_kroky_dosahnou_dal()
    {
        var focus = new SchemaGraph(Schema()).Focus(N("Customers"), 2);

        Assert.Contains(N("OrderLines"), focus);
        Assert.DoesNotContain(N("Osamela"), focus);
    }

    [Fact]
    public void Focus_z_nezname_tabulky_je_prazdny() =>
        Assert.Empty(new SchemaGraph(Schema()).Focus(N("Neexistuje"), 2));

    [Fact]
    public void Vazebni_tabulka_je_sousedem_obou_stran()
    {
        var schema = new DatabaseSchema
        {
            Tables =
            [
                Build.Table("Products", ["Id"], ["Id"]),
                Build.Table("Tags", ["Id"], ["Id"]),
                Build.Table("ProductTags", ["ProductId", "TagId"], ["ProductId", "TagId"]),
            ],
            Relationships = [Rel("m2m", "Products", "Tags", join: "ProductTags")],
        };

        var graph = new SchemaGraph(schema);

        Assert.Contains(N("ProductTags"), graph.NeighboursOf(N("Products")));
        Assert.Contains(N("ProductTags"), graph.NeighboursOf(N("Tags")));
    }

    [Fact]
    public void Vyrez_vrati_tabulky_i_vazby()
    {
        var graph = new SchemaGraph(Schema());
        var names = graph.Focus(N("Orders"), 1);

        Assert.Equal(3, graph.TablesIn(names).Count);
        Assert.Equal(2, graph.RelationshipsIn(names).Count);
    }

    [Fact]
    public void Vazba_s_jednou_stranou_mimo_vyrez_se_nekresli()
    {
        var graph = new SchemaGraph(Schema());
        var names = new HashSet<DbObjectName> { N("Orders") };

        Assert.Empty(graph.RelationshipsIn(names));
    }

    [Fact]
    public void Prichozi_a_odchozi_vazby_se_rozlisi()
    {
        var graph = new SchemaGraph(Schema());

        Assert.Single(graph.IncomingTo(N("Customers")));
        Assert.Empty(graph.OutgoingFrom(N("Customers")));

        Assert.Equal(2, graph.OutgoingFrom(N("OrderLines")).Count);
        Assert.Empty(graph.IncomingTo(N("OrderLines")));
    }

    [Fact]
    public void Self_reference_se_mezi_prichozi_nepocita()
    {
        var schema = new DatabaseSchema
        {
            Tables = [Build.Table("Categories", ["Id"], ["Id"])],
            Relationships = [Rel("fk:self", "Categories", "Categories")],
        };

        var graph = new SchemaGraph(schema);

        Assert.Empty(graph.IncomingTo(N("Categories")));
        Assert.Empty(graph.OutgoingFrom(N("Categories")));
    }

    [Fact]
    public void Chybejici_vstupy_jsou_chyba()
    {
        Assert.Throws<ArgumentNullException>(() => new SchemaGraph(null!));

        var graph = new SchemaGraph(Schema());
        Assert.Throws<ArgumentNullException>(() => graph.TablesIn(null!));
        Assert.Throws<ArgumentNullException>(() => graph.RelationshipsIn(null!));
    }
}
