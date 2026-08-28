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
    public void Hrana_ma_lomenou_trasu_i_popisek()
    {
        var tables = new[]
        {
            Build.Table("Customers", ["Id"], ["Id"]),
            Build.Table("Orders", ["Id", "CustomerId"], ["Id"]),
        };

        var edge = Assert.Single(DiagramLayout.Compute(tables, [Rel("Orders", "Customers")]).Edges);

        Assert.Equal(4, edge.Points.Count);
        Assert.False(edge.IsSelfLoop);
        Assert.True(edge.LabelAt.X > 0);
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
