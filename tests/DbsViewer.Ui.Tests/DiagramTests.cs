using DbsViewer.TestKit;
using DbsViewer.Ui.Model;

namespace DbsViewer.Tests.Ui;

public class DiagramLayoutTests
{
    private static DbObjectName N(string name) => new(null, name);

    private static DbRelationship Rel(string from, string to) => new()
    {
        Id = $"fk:{from}->{to}",
        From = N(from),
        To = N(to),
    };

    [Fact]
    public void Prazdny_diagram_nema_rozmery()
    {
        var layout = DiagramLayout.Compute([], []);

        Assert.Empty(layout.Nodes);
        Assert.Empty(layout.Edges);
        Assert.Equal(0, layout.Width);
        Assert.Equal(0, layout.Height);
    }

    [Fact]
    public void Jedina_tabulka_dostane_misto()
    {
        var layout = DiagramLayout.Compute([Build.Table("T", ["Id"], ["Id"])], []);

        var node = Assert.Single(layout.Nodes);
        Assert.Equal(0, node.Layer);
        Assert.True(node.Width > 0);
        Assert.True(node.Height > 0);
        Assert.True(layout.Width > node.Width);
    }

    [Fact]
    public void Zavisla_tabulka_je_o_vrstvu_vpravo()
    {
        var tables = new[]
        {
            Build.Table("Customers", ["Id"], ["Id"]),
            Build.Table("Orders", ["Id", "CustomerId"], ["Id"]),
        };

        var layout = DiagramLayout.Compute(tables, [Rel("Orders", "Customers")]);

        var customers = layout.Find(N("Customers"))!;
        var orders = layout.Find(N("Orders"))!;

        Assert.Equal(0, customers.Layer);
        Assert.Equal(1, orders.Layer);
        Assert.True(orders.X > customers.X);
    }

    [Fact]
    public void Retez_zavislosti_da_tri_vrstvy()
    {
        var tables = new[]
        {
            Build.Table("A", ["Id"], ["Id"]),
            Build.Table("B", ["Id"], ["Id"]),
            Build.Table("C", ["Id"], ["Id"]),
        };

        var layers = DiagramLayout.AssignLayers(tables, [Rel("B", "A"), Rel("C", "B")]);

        Assert.Equal(0, layers[N("A")]);
        Assert.Equal(1, layers[N("B")]);
        Assert.Equal(2, layers[N("C")]);
    }

    [Fact]
    public void Cyklus_nenafoukne_pocet_vrstev()
    {
        // Vzájemně se odkazující tabulky jsou běžné: zaměstnanec má oddělení
        // a oddělení má vedoucího. Diagram kvůli tomu nesmí zbytnět do šířky.
        var tables = new[]
        {
            Build.Table("Employees", ["Id"], ["Id"]),
            Build.Table("Departments", ["Id"], ["Id"]),
        };

        var layers = DiagramLayout.AssignLayers(
            tables,
            [Rel("Employees", "Departments"), Rel("Departments", "Employees")]);

        Assert.True(
            layers.Values.Max() <= 1,
            $"Cyklus roztáhl diagram na {layers.Values.Max() + 1} vrstev.");
    }

    [Fact]
    public void Delsi_cyklus_take_nenafoukne_vrstvy()
    {
        var tables = new[]
        {
            Build.Table("A", ["Id"], ["Id"]),
            Build.Table("B", ["Id"], ["Id"]),
            Build.Table("C", ["Id"], ["Id"]),
            Build.Table("D", ["Id"], ["Id"]),
        };

        var layers = DiagramLayout.AssignLayers(
            tables,
            [Rel("A", "B"), Rel("B", "C"), Rel("C", "D"), Rel("D", "A")]);

        Assert.True(
            layers.Values.Max() <= 3,
            $"Cyklus roztáhl diagram na {layers.Values.Max() + 1} vrstev.");
    }

    [Fact]
    public void Cyklus_nezacykli_vypocet()
    {
        // Vzájemně se odkazující tabulky by rekurzi shodily přetečením zásobníku.
        var tables = new[]
        {
            Build.Table("A", ["Id"], ["Id"]),
            Build.Table("B", ["Id"], ["Id"]),
        };

        var layers = DiagramLayout.AssignLayers(tables, [Rel("A", "B"), Rel("B", "A")]);

        Assert.Equal(2, layers.Count);
    }

    [Fact]
    public void Self_reference_neposouva_vrstvu()
    {
        var tables = new[] { Build.Table("Categories", ["Id"], ["Id"]) };

        var layers = DiagramLayout.AssignLayers(tables, [Rel("Categories", "Categories")]);

        Assert.Equal(0, layers[N("Categories")]);
    }

    [Fact]
    public void Vazba_mimo_zobrazene_tabulky_se_ignoruje()
    {
        var tables = new[] { Build.Table("A", ["Id"], ["Id"]) };

        var layout = DiagramLayout.Compute(tables, [Rel("A", "Neexistuje")]);

        Assert.Empty(layout.Edges);
    }

    [Fact]
    public void Tabulky_ve_vrstve_nekoliduji()
    {
        var tables = new[]
        {
            Build.Table("A", ["Id"], ["Id"]),
            Build.Table("B", ["Id"], ["Id"]),
            Build.Table("C", ["Id"], ["Id"]),
        };

        var layout = DiagramLayout.Compute(tables, []);
        var ordered = layout.Nodes.OrderBy(n => n.Y).ToList();

        for (var i = 1; i < ordered.Count; i++)
        {
            Assert.True(ordered[i].Y >= ordered[i - 1].Y + ordered[i - 1].Height);
        }
    }

    [Fact]
    public void Tabulky_ze_stejneho_schematu_jsou_pohromade()
    {
        // V databázi s víc schématy se čtou tabulky po schématech, ne napřeskáčku.
        var tables = new[]
        {
            new DbTable { Name = new DbObjectName("sales", "Orders") },
            new DbTable { Name = new DbObjectName("hr", "Employees") },
            new DbTable { Name = new DbObjectName("sales", "Invoices") },
            new DbTable { Name = new DbObjectName("hr", "Departments") },
        };

        var poradi = DiagramLayout.Compute(tables, []).Nodes
            .OrderBy(n => n.Y)
            .Select(n => n.Table.Name.Schema)
            .ToList();

        // Nejdřív všechna hr, pak všechna sales — ne střídavě.
        Assert.Equal(["hr", "hr", "sales", "sales"], poradi);
    }

    [Fact]
    public void Hrana_ma_ortogonalni_trasu_i_popisek()
    {
        var tables = new[]
        {
            Build.Table("Customers", ["Id"], ["Id"]),
            Build.Table("Orders", ["Id", "CustomerId"], ["Id"]),
        };

        var edge = Assert.Single(DiagramLayout.Compute(tables, [Rel("Orders", "Customers")]).Edges);

        Assert.False(edge.IsSelfLoop);
        Assert.True(edge.Points.Count >= 2);
        Assert.True(edge.LabelAt.X > 0);

        // Každý úsek je vodorovný nebo svislý — šikmá čára by v ER diagramu byla cizí.
        for (var i = 1; i < edge.Points.Count; i++)
        {
            var a = edge.Points[i - 1];
            var b = edge.Points[i];

            Assert.True(
                Math.Abs(a.X - b.X) < 0.5 || Math.Abs(a.Y - b.Y) < 0.5,
                $"Úsek {i} není osově zarovnaný: {a} → {b}");
        }
    }

    [Fact]
    public void Self_reference_se_kresli_jako_smycka()
    {
        var tables = new[] { Build.Table("Categories", ["Id"], ["Id"]) };

        var edge = Assert.Single(DiagramLayout.Compute(tables, [Rel("Categories", "Categories")]).Edges);

        Assert.True(edge.IsSelfLoop);
        Assert.Equal(4, edge.Points.Count);

        // Smyčka vede ven doprava a zase zpět.
        Assert.True(edge.Points[1].X > edge.Points[0].X);
        Assert.Equal(edge.Points[0].X, edge.Points[3].X);
    }

    [Fact]
    public void Rozbaleny_uzel_je_vyssi()
    {
        var table = Build.Table("T", ["Id", "A", "B", "C"], ["Id"]);

        var collapsed = DiagramLayout.NodeHeight(table, isExpanded: false);
        var expanded = DiagramLayout.NodeHeight(table, isExpanded: true);

        Assert.True(expanded > collapsed);
    }

    [Fact]
    public void Sbaleny_uzel_ukazuje_jen_klicove_sloupce()
    {
        var table = Build.Table("T", ["Id", "Poznamka"], ["Id"]);

        var collapsed = DiagramLayout.VisibleColumns(table, isExpanded: false);
        var expanded = DiagramLayout.VisibleColumns(table, isExpanded: true);

        Assert.Equal(["Id"], collapsed.Select(c => c.Name).ToList());
        Assert.Equal(2, expanded.Count);
    }

    [Fact]
    public void Tabulka_bez_klicu_nema_ve_sbalenem_stavu_sloupce()
    {
        var table = Build.Table("Log", ["Zprava"]);

        Assert.Empty(DiagramLayout.VisibleColumns(table, isExpanded: false));
    }

    [Fact]
    public void Rozbaleni_ovlivni_vysku_uzlu()
    {
        var tables = new[] { Build.Table("T", ["Id", "A", "B"], ["Id"]) };
        var expanded = new HashSet<DbObjectName> { N("T") };

        var normal = DiagramLayout.Compute(tables, []).Nodes[0].Height;
        var bigger = DiagramLayout.Compute(tables, [], expanded).Nodes[0].Height;

        Assert.True(bigger > normal);
    }

    [Fact]
    public void Hledani_uzlu_vraci_null_pro_neznamou_tabulku()
    {
        var layout = DiagramLayout.Compute([Build.Table("A", ["Id"], ["Id"])], []);

        Assert.NotNull(layout.Find(N("A")));
        Assert.Null(layout.Find(N("B")));
    }

    [Fact]
    public void Stred_uzlu_odpovida_rozmerum()
    {
        var node = DiagramLayout.Compute([Build.Table("A", ["Id"], ["Id"])], []).Nodes[0];

        Assert.Equal(node.X + (node.Width / 2), node.CenterX);
        Assert.Equal(node.Y + (node.Height / 2), node.CenterY);
    }

    [Fact]
    public void Chybejici_vstupy_jsou_chyba()
    {
        Assert.Throws<ArgumentNullException>(() => DiagramLayout.Compute(null!, []));
        Assert.Throws<ArgumentNullException>(() => DiagramLayout.Compute([], null!));
        Assert.Throws<ArgumentNullException>(() => DiagramLayout.VisibleColumns(null!, false));
    }

    [Fact]
    public void Zadna_hrana_nevede_pres_tabulku()
    {
        // Schéma podobné tomu, na kterém se problém projevil: prostřední sloupec tabulek,
        // přes který vedly vazby ze sousedních vrstev.
        var tables = new[]
        {
            Build.Table("Locations", ["Id"], ["Id"]),
            Build.Table("Spaces", ["Id", "LocationId"], ["Id"]),
            Build.Table("Bookings", ["Id", "SpaceId"], ["Id"]),
            Build.Table("Photos", ["Id", "LocationId", "SpaceId"], ["Id"]),
            Build.Table("SpaceComponents", ["CompositeSpaceId", "PartSpaceId"], ["CompositeSpaceId"]),
        };

        var relationships = new[]
        {
            Rel("Spaces", "Locations"),
            Rel("Bookings", "Spaces"),
            Rel("Photos", "Locations"),
            Rel("Photos", "Spaces"),
            Rel("SpaceComponents", "Spaces"),
        };

        var layout = DiagramLayout.Compute(tables, relationships);

        var obstacles = layout.Nodes
            .Select(n => new RouteObstacle(n.X, n.Y, n.Width, n.Height))
            .ToList();

        foreach (var edge in layout.Edges.Where(e => !e.IsSelfLoop))
        {
            for (var i = 1; i < edge.Points.Count; i++)
            {
                var a = edge.Points[i - 1];
                var b = edge.Points[i];

                Assert.False(
                    EdgeRouter.Blocked(a, b, obstacles),
                    $"Vazba {edge.Relationship.Id} vede úsekem {a} → {b} přes tabulku.");
            }
        }
    }

    [Fact]
    public void Vazby_do_jedne_tabulky_maji_kazda_svou_kotvu()
    {
        // Tři vazby mířící do Spaces. Kdyby všechny šly do středu, šipky by se slily
        // do jednoho bodu a nešlo by poznat, která odkud vede.
        var tables = new[]
        {
            Build.Table("Spaces", ["Id"], ["Id"]),
            Build.Table("Bookings", ["Id", "SpaceId"], ["Id"]),
            Build.Table("Photos", ["Id", "SpaceId"], ["Id"]),
            Build.Table("Reviews", ["Id", "SpaceId"], ["Id"]),
        };

        var relationships = new[]
        {
            Rel("Bookings", "Spaces"),
            Rel("Photos", "Spaces"),
            Rel("Reviews", "Spaces"),
        };

        var layout = DiagramLayout.Compute(tables, relationships);
        var cile = layout.Edges.Select(e => Math.Round(e.Points[^1].Y, 2)).ToList();

        Assert.Equal(3, cile.Count);
        Assert.Equal(cile.Count, cile.Distinct().Count());
    }

    [Fact]
    public void Popisek_stoji_kousek_pred_sipkou()
    {
        IReadOnlyList<(double X, double Y)> body = [(0, 0), (0, 40), (200, 40)];

        var at = DiagramLayout.LabelPosition(body);

        Assert.Equal(174, at.X);
        Assert.Equal(40, at.Y);
    }

    [Fact]
    public void Popisek_na_kratkem_useku_zustane_na_nem()
    {
        // Úsek je kratší než odsazení — popisek nesmí vyjet před jeho začátek.
        IReadOnlyList<(double X, double Y)> body = [(0, 0), (0, 40), (20, 40)];

        Assert.Equal(10, DiagramLayout.LabelPosition(body).X);
    }

    [Fact]
    public void Popisek_u_vazby_zprava_doleva_se_odsadi_opacne()
    {
        IReadOnlyList<(double X, double Y)> body = [(200, 0), (200, 40), (0, 40)];

        Assert.Equal(26, DiagramLayout.LabelPosition(body).X);
    }

    [Fact]
    public void Popisek_bez_vodorovneho_useku_padne_na_zacatek()
    {
        IReadOnlyList<(double X, double Y)> body = [(10, 0), (10, 40)];

        Assert.Equal((10, 0), DiagramLayout.LabelPosition(body));
    }

    [Fact]
    public void Popisek_z_null_trasy_je_chyba_argumentu() =>
        Assert.Throws<ArgumentNullException>(() => DiagramLayout.LabelPosition(null!));


    [Fact]
    public void Popisky_kardinality_se_neprekryvaji()
    {
        // Do Spaces míří vazby zleva i zprava; jejich popisky by jinak skončily
        // na stejném místě jako nečitelné „1:N:M".
        var tables = new[]
        {
            Build.Table("Spaces", ["Id"], ["Id"]),
            Build.Table("Bookings", ["Id", "SpaceId"], ["Id"]),
            Build.Table("Photos", ["Id", "SpaceId"], ["Id"]),
            Build.Table("Reviews", ["Id", "SpaceId"], ["Id"]),
            Build.Table("SpaceTag", ["SpacesId", "TagsId"], ["SpacesId", "TagsId"]),
        };

        var relationships = new[]
        {
            Rel("Bookings", "Spaces"),
            Rel("Photos", "Spaces"),
            Rel("Reviews", "Spaces"),
            Rel("SpaceTag", "Spaces"),
        };

        var popisky = DiagramLayout.Compute(tables, relationships).Edges
            .Select(e => e.LabelAt)
            .ToList();

        for (var i = 0; i < popisky.Count; i++)
        {
            for (var j = i + 1; j < popisky.Count; j++)
            {
                var blizko = Math.Abs(popisky[i].X - popisky[j].X) < 44
                    && Math.Abs(popisky[i].Y - popisky[j].Y) < 14;

                Assert.False(blizko, $"Popisky {popisky[i]} a {popisky[j]} se překrývají.");
            }
        }
    }


    [Fact]
    public void Popisky_na_stejnem_miste_se_rozsunou_podel_trasy()
    {
        // Tři popisky na jednom bodě. Uhýbá se podél hrany, ne kolmo — popisek tak
        // zůstane na své čáře a je pořád jasné, ke které patří.
        var edges = Enumerable
            .Range(0, 3)
            .Select(i => new DiagramEdge
            {
                Relationship = Rel($"A{i}", "B"),
                Points = [(0, 100), (300, 100)],
                LabelAt = (300, 100),
            })
            .ToList();

        var mista = DiagramLayout.SpreadLabels(edges).Select(e => e.LabelAt).ToList();

        Assert.Equal(3, mista.Distinct().Count());

        // Všechny zůstaly na trase, jen postupně dál od šipky.
        Assert.All(mista, m => Assert.Equal(100, m.Y));
        Assert.Equal([300, 256, 212], mista.Select(m => m.X));
    }

    [Fact]
    public void Kratka_hrana_uhne_popiskem_kolmo()
    {
        // Na krátké trase není kam couvat, takže se popisek posune o řádek —
        // poslední možnost, ale hrana o popisek nepřijde.
        var edges = Enumerable
            .Range(0, 2)
            .Select(i => new DiagramEdge
            {
                Relationship = Rel($"A{i}", "B"),
                Points = [(0, 100), (10, 100)],
                LabelAt = (10, 100),
            })
            .ToList();

        var mista = DiagramLayout.SpreadLabels(edges).Select(e => e.LabelAt).ToList();

        Assert.Equal(2, mista.Distinct().Count());
        Assert.Equal(10, mista[1].X);
        Assert.True(mista[1].Y < mista[0].Y);
    }

    [Fact]
    public void Svisla_hrana_uhyba_popiskem_svisle()
    {
        var edges = Enumerable
            .Range(0, 2)
            .Select(i => new DiagramEdge
            {
                Relationship = Rel($"A{i}", "B"),
                Points = [(50, 0), (50, 300)],
                LabelAt = (50, 300),
            })
            .ToList();

        var mista = DiagramLayout.SpreadLabels(edges).Select(e => e.LabelAt).ToList();

        Assert.Equal(50, mista[1].X);
        Assert.Equal(256, mista[1].Y);
    }

    [Fact]
    public void Rozsun_bez_hran_je_prazdny() =>
        Assert.Empty(DiagramLayout.SpreadLabels([]));

    [Fact]
    public void Rozsun_null_je_chyba_argumentu() =>
        Assert.Throws<ArgumentNullException>(() => DiagramLayout.SpreadLabels(null!));


    [Fact]
    public void Razeni_ve_vrstve_snizi_krizeni()
    {
        // Bez barycentra by se A1→B2 a A2→B1 překřížily jen kvůli abecednímu pořadí.
        var tables = new[]
        {
            Build.Table("Bcil", ["Id"], ["Id"]),
            Build.Table("Acil", ["Id"], ["Id"]),
            Build.Table("Zdroj1", ["Id", "CizId"], ["Id"]),
            Build.Table("Zdroj2", ["Id", "CizId"], ["Id"]),
        };

        var relationships = new[]
        {
            Rel("Zdroj1", "Bcil"),
            Rel("Zdroj2", "Acil"),
        };

        var layout = DiagramLayout.Compute(tables, relationships);

        // Vazby vedou vodorovně vedle sebe, ne přes sebe: cíl výš má i zdroj výš.
        var prvni = layout.Find(new DbObjectName(null, "Zdroj1"))!;
        var druha = layout.Find(new DbObjectName(null, "Zdroj2"))!;
        var cil1 = layout.Find(new DbObjectName(null, "Bcil"))!;
        var cil2 = layout.Find(new DbObjectName(null, "Acil"))!;

        Assert.Equal(prvni.Y < druha.Y, cil1.Y < cil2.Y);
    }

    [Fact]
    public void Poradi_ve_vrstve_je_pokazde_stejne()
    {
        var tables = new[]
        {
            Build.Table("Alfa", ["Id"], ["Id"]),
            Build.Table("Beta", ["Id"], ["Id"]),
            Build.Table("Gama", ["Id"], ["Id"]),
        };

        var layers = DiagramLayout.AssignLayers(tables, []);

        var prvni = DiagramLayout.OrderWithinLayers(tables, layers, []);
        var druhe = DiagramLayout.OrderWithinLayers(tables, layers, []);

        Assert.Equal(prvni, druhe);
    }

    [Fact]
    public void Vazba_mimo_schema_razeni_neovlivni()
    {
        var tables = new[] { Build.Table("Alfa", ["Id"], ["Id"]) };
        var layers = DiagramLayout.AssignLayers(tables, []);

        var poradi = DiagramLayout.OrderWithinLayers(
            tables, layers, [Rel("Alfa", "Neznama"), Rel("Neznama", "Alfa")]);

        Assert.Equal(0, poradi[new DbObjectName(null, "Alfa")]);
    }

    [Fact]
    public void Hrany_se_nepokryvaji_navzajem()
    {
        // Tři vazby do jedné tabulky ze sousední vrstvy. Kdyby se vedly po téže lince,
        // splynuly by v jednu čáru a diagram by lhal o počtu vazeb.
        var tables = new[]
        {
            Build.Table("Cil", ["Id"], ["Id"]),
            Build.Table("Zdroj1", ["Id", "CizId"], ["Id"]),
            Build.Table("Zdroj2", ["Id", "CizId"], ["Id"]),
            Build.Table("Zdroj3", ["Id", "CizId"], ["Id"]),
        };

        var relationships = new[]
        {
            Rel("Zdroj1", "Cil"),
            Rel("Zdroj2", "Cil"),
            Rel("Zdroj3", "Cil"),
        };

        var edges = DiagramLayout.Compute(tables, relationships).Edges;

        for (var i = 0; i < edges.Count; i++)
        {
            for (var j = i + 1; j < edges.Count; j++)
            {
                foreach (var (a, b) in Useky(edges[i].Points))
                {
                    foreach (var (c, d) in Useky(edges[j].Points))
                    {
                        Assert.False(
                            EdgeRouter.Overlaps(a, b, c, d),
                            $"Úseky {a}→{b} a {c}→{d} splývají.");
                    }
                }
            }
        }
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

public class SchemaExporterTests
{
    private static DatabaseSchema Schema() => new()
    {
        DatabaseName = "Shop",
        Provider = DbProviderKind.Sqlite,
        Tables =
        [
            new DbTable
            {
                Name = new DbObjectName(null, "Customers"),
                Comment = "Zákazníci",
                Columns =
                [
                    new DbColumn
                    {
                        Name = "Id",
                        Ordinal = 1,
                        StoreType = "int",
                        IsPrimaryKey = true,
                        IsIdentity = true,
                    },
                    new DbColumn
                    {
                        Name = "Email",
                        Ordinal = 2,
                        StoreType = "nvarchar(256)",
                        Comment = "Přihlašovací e-mail",
                    },
                ],
                Indexes = [new DbIndex { Name = "UX_Email", Columns = ["Email"], IsUnique = true }],
            },
            new DbTable
            {
                Name = new DbObjectName(null, "Orders"),
                Columns =
                [
                    new DbColumn { Name = "Id", Ordinal = 1, StoreType = "int", IsPrimaryKey = true },
                    new DbColumn
                    {
                        Name = "CustomerId",
                        Ordinal = 2,
                        StoreType = "int",
                        IsForeignKey = true,
                        IsNullable = true,
                    },
                    new DbColumn
                    {
                        Name = "Total",
                        Ordinal = 3,
                        StoreType = "decimal(18,2)",
                        IsComputed = true,
                        DefaultValueSql = "0",
                    },
                ],
                ForeignKeys =
                [
                    new DbForeignKey
                    {
                        Name = "FK_Orders_Customers",
                        Columns = ["CustomerId"],
                        PrincipalTable = new DbObjectName(null, "Customers"),
                        PrincipalColumns = ["Id"],
                        DeleteBehavior = DbDeleteBehavior.Cascade,
                    },
                ],
            },
        ],
        Relationships =
        [
            new DbRelationship
            {
                Id = "fk:1",
                From = new DbObjectName(null, "Orders"),
                To = new DbObjectName(null, "Customers"),
                Cardinality = DbCardinality.OneToMany,
                FromColumns = ["CustomerId"],
                ToColumns = ["Id"],
                FromNavigation = "Customer",
            },
        ],
    };

    [Fact]
    public void Mermaid_obsahuje_entity_i_vazby()
    {
        var output = SchemaExporter.Export(Schema(), ExportFormat.Mermaid);

        Assert.StartsWith("erDiagram", output, StringComparison.Ordinal);
        Assert.Contains("Customers {", output, StringComparison.Ordinal);
        Assert.Contains("Id PK", output, StringComparison.Ordinal);
        Assert.Contains("CustomerId FK", output, StringComparison.Ordinal);
        Assert.Contains("Customers ||--o{ Orders", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Mermaid_nepusti_do_typu_zavorky()
    {
        // Mermaid v typu závorky ani mezery nesnese — rozbily by diagram.
        var output = SchemaExporter.Export(Schema(), ExportFormat.Mermaid);

        Assert.Contains("nvarchar_256_ Email", output, StringComparison.Ordinal);
        Assert.DoesNotContain("nvarchar(256)", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Mermaid_rozlisuje_kardinality()
    {
        var schema = Schema() with
        {
            Relationships =
            [
                new DbRelationship
                {
                    Id = "a",
                    From = new DbObjectName(null, "Orders"),
                    To = new DbObjectName(null, "Customers"),
                    Cardinality = DbCardinality.ManyToMany,
                },
                new DbRelationship
                {
                    Id = "b",
                    From = new DbObjectName(null, "Orders"),
                    To = new DbObjectName(null, "Customers"),
                    Cardinality = DbCardinality.OneToOne,
                    IsRequired = true,
                },
                new DbRelationship
                {
                    Id = "c",
                    From = new DbObjectName(null, "Orders"),
                    To = new DbObjectName(null, "Customers"),
                    Cardinality = DbCardinality.OneToOne,
                },
                new DbRelationship
                {
                    Id = "d",
                    From = new DbObjectName(null, "Orders"),
                    To = new DbObjectName(null, "Customers"),
                    Cardinality = DbCardinality.OneToMany,
                    IsRequired = true,
                },
            ],
        };

        var output = SchemaExporter.Export(schema, ExportFormat.Mermaid);

        Assert.Contains("}o--o{", output, StringComparison.Ordinal);
        Assert.Contains("||--||", output, StringComparison.Ordinal);
        Assert.Contains("||--o|", output, StringComparison.Ordinal);
        Assert.Contains("||--|{", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Mermaid_zvlada_sloupec_bez_typu()
    {
        var schema = new DatabaseSchema
        {
            Tables =
            [
                new DbTable
                {
                    Name = new DbObjectName(null, "T"),
                    Columns = [new DbColumn { Name = "A", Ordinal = 1, StoreType = "" }],
                },
            ],
        };

        Assert.Contains("unknown A", SchemaExporter.Export(schema, ExportFormat.Mermaid), StringComparison.Ordinal);
    }

    [Fact]
    public void Dbml_nese_tabulky_klice_i_vazby()
    {
        var output = SchemaExporter.Export(Schema(), ExportFormat.Dbml);

        Assert.Contains("Table \"Customers\"", output, StringComparison.Ordinal);
        Assert.Contains("[pk, increment, not null]", output, StringComparison.Ordinal);
        Assert.Contains("Note: 'Zákazníci'", output, StringComparison.Ordinal);
        Assert.Contains("Ref: \"Orders\".\"CustomerId\" > \"Customers\".\"Id\"", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Dbml_escapuje_apostrofy()
    {
        var schema = new DatabaseSchema
        {
            Tables =
            [
                new DbTable
                {
                    Name = new DbObjectName(null, "T"),
                    Comment = "Ferda's tabulka",
                    Columns = [],
                },
            ],
        };

        Assert.Contains("Ferda\\'s", SchemaExporter.Export(schema, ExportFormat.Dbml), StringComparison.Ordinal);
    }

    [Fact]
    public void Dbml_rozlisuje_kardinality()
    {
        var schema = Schema() with
        {
            Relationships =
            [
                new DbRelationship
                {
                    Id = "m",
                    From = new DbObjectName(null, "Orders"),
                    To = new DbObjectName(null, "Customers"),
                    Cardinality = DbCardinality.ManyToMany,
                    FromColumns = ["CustomerId"],
                    ToColumns = ["Id"],
                },
                new DbRelationship
                {
                    Id = "o",
                    From = new DbObjectName(null, "Orders"),
                    To = new DbObjectName(null, "Customers"),
                    Cardinality = DbCardinality.OneToOne,
                    FromColumns = ["CustomerId"],
                    ToColumns = ["Id"],
                },
            ],
        };

        var output = SchemaExporter.Export(schema, ExportFormat.Dbml);

        Assert.Contains("\"CustomerId\" <> ", output, StringComparison.Ordinal);
        Assert.Contains("\"CustomerId\" - ", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Dbml_vynecha_vazbu_bez_sloupcu()
    {
        var schema = Schema() with
        {
            Relationships =
            [
                new DbRelationship
                {
                    Id = "x",
                    From = new DbObjectName(null, "Orders"),
                    To = new DbObjectName(null, "Customers"),
                },
            ],
        };

        Assert.DoesNotContain("Ref:", SchemaExporter.Export(schema, ExportFormat.Dbml), StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_ma_nadpisy_tabulky_indexy_i_klice()
    {
        var output = SchemaExporter.Export(Schema(), ExportFormat.Markdown);

        Assert.Contains("# Schéma databáze Shop", output, StringComparison.Ordinal);
        Assert.Contains("## Customers", output, StringComparison.Ordinal);
        Assert.Contains("> Zákazníci", output, StringComparison.Ordinal);
        Assert.Contains("**Indexy**", output, StringComparison.Ordinal);
        Assert.Contains("UNIQUE `UX_Email`", output, StringComparison.Ordinal);
        Assert.Contains("**Cizí klíče**", output, StringComparison.Ordinal);
        Assert.Contains("onDelete Cascade", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_popisuje_vlastnosti_sloupcu()
    {
        var output = SchemaExporter.Export(Schema(), ExportFormat.Markdown);

        Assert.Contains("identity", output, StringComparison.Ordinal);
        Assert.Contains("computed", output, StringComparison.Ordinal);
        Assert.Contains("default `0`", output, StringComparison.Ordinal);
        Assert.Contains("Přihlašovací e-mail", output, StringComparison.Ordinal);
        Assert.Contains("| PK |", output, StringComparison.Ordinal);
        Assert.Contains("| FK |", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_zvlada_schema_bez_jmena_i_bez_indexu()
    {
        var schema = new DatabaseSchema
        {
            Tables =
            [
                new DbTable
                {
                    Name = new DbObjectName(null, "T"),
                    Columns =
                    [
                        new DbColumn
                        {
                            Name = "A",
                            Ordinal = 1,
                            StoreType = "int",
                            IsPrimaryKey = true,
                            IsForeignKey = true,
                        },
                    ],
                },
            ],
        };

        var output = SchemaExporter.Export(schema, ExportFormat.Markdown);

        Assert.Contains("# Schéma databáze", output, StringComparison.Ordinal);
        Assert.DoesNotContain("**Indexy**", output, StringComparison.Ordinal);
        Assert.Contains("| PK, FK |", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ExportFormat.Mermaid, "mmd")]
    [InlineData(ExportFormat.Dbml, "dbml")]
    [InlineData(ExportFormat.Markdown, "md")]
    public void Pripona_odpovida_formatu(ExportFormat format, string expected) =>
        Assert.Equal(expected, SchemaExporter.FileExtension(format));

    [Fact]
    public void Chybejici_schema_je_chyba() =>
        Assert.Throws<ArgumentNullException>(() => SchemaExporter.Export(null!, ExportFormat.Mermaid));

    [Fact]
    public void Jmeno_se_zbavi_znaku_ktere_Mermaid_nesnasi()
    {
        var schema = new DatabaseSchema
        {
            Tables =
            [
                new DbTable
                {
                    Name = new DbObjectName("dbo", "Order Lines"),
                    Columns = [],
                },
            ],
        };

        var output = SchemaExporter.Export(schema, ExportFormat.Mermaid);

        Assert.Contains("dbo_Order_Lines {", output, StringComparison.Ordinal);
    }
}
