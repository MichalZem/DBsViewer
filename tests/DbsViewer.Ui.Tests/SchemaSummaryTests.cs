using DbsViewer.TestKit;
using DbsViewer.Ui.Model;

namespace DbsViewer.Tests.Ui;

/// <summary>Souhrn databáze pro úvodní přehled.</summary>
public class SchemaSummaryTests
{
    [Fact]
    public void Prazdne_schema_da_nulovy_souhrn()
    {
        var summary = SchemaSummary.From(new DatabaseSchema());

        Assert.Equal(0, summary.TableCount);
        Assert.Equal(0, summary.ColumnCount);
        Assert.Null(summary.TotalRowEstimate);
        Assert.Empty(summary.LargestTables);
        Assert.Empty(summary.MostConnected);
        Assert.Empty(summary.BySchema);
    }

    [Fact]
    public void Null_schema_je_chyba_argumentu() =>
        Assert.Throws<ArgumentNullException>(() => SchemaSummary.From(null!));

    [Fact]
    public void Pohledy_se_pocitaji_zvlast_od_tabulek()
    {
        var schema = Schema(
            Build.Table("Zakaznici", ["Id"], ["Id"]),
            Build.Table("Objednavky", ["Id"], ["Id"]),
            Build.Table("PrehledTrzeb", ["Castka"], isView: true));

        var summary = SchemaSummary.From(schema);

        Assert.Equal(2, summary.TableCount);
        Assert.Equal(1, summary.ViewCount);
    }

    [Fact]
    public void Scitaji_se_sloupce_indexy_a_vazby()
    {
        var schema = Schema(
            Build.Table("Zakaznici", ["Id", "Jmeno"], ["Id"],
                indexes: [Build.Index("IX_Jmeno", ["Jmeno"])]),
            Build.Table("Objednavky", ["Id", "ZakaznikId"], ["Id"],
                foreignKeys: [Build.ForeignKey("FK", ["ZakaznikId"], "Zakaznici")],
                indexes: [Build.Index("IX_Zakaznik", ["ZakaznikId"])]));

        schema = schema with { Relationships = [Rel("Objednavky", "Zakaznici")] };

        var summary = SchemaSummary.From(schema);

        Assert.Equal(4, summary.ColumnCount);
        Assert.Equal(2, summary.IndexCount);
        Assert.Equal(1, summary.RelationshipCount);
    }

    [Fact]
    public void Odhad_radku_se_secte_jen_z_tabulek_ktere_ho_znaji()
    {
        var schema = Schema(
            Build.Table("Zakaznici", ["Id"], ["Id"]) with { RowCountEstimate = 1200 },
            Build.Table("Objednavky", ["Id"], ["Id"]) with { RowCountEstimate = 45000 },
            Build.Table("Log", ["Id"], ["Id"]));

        var summary = SchemaSummary.From(schema);

        Assert.Equal(46200, summary.TotalRowEstimate);
        Assert.Equal("Objednavky", summary.LargestTables[0].Table.Name);
        Assert.Equal(45000, summary.LargestTables[0].Value);
    }

    [Fact]
    public void Bez_odhadu_radku_je_celkovy_pocet_neznamy()
    {
        // Schéma čtené jen z EF modelu počty řádků nezná — nesmí to vypadat jako nula.
        var summary = SchemaSummary.From(Schema(Build.Table("Zakaznici", ["Id"], ["Id"])));

        Assert.Null(summary.TotalRowEstimate);
        Assert.Empty(summary.LargestTables);
    }

    [Fact]
    public void Nejvic_propojena_tabulka_je_ta_s_nejvice_vazbami()
    {
        var schema = Schema(
            Build.Table("Spaces", ["Id"], ["Id"]),
            Build.Table("Bookings", ["Id"], ["Id"]),
            Build.Table("Photos", ["Id"], ["Id"]),
            Build.Table("Locations", ["Id"], ["Id"]));

        schema = schema with
        {
            Relationships =
            [
                Rel("Bookings", "Spaces"),
                Rel("Photos", "Spaces"),
                Rel("Spaces", "Locations"),
            ],
        };

        var summary = SchemaSummary.From(schema);

        Assert.Equal("Spaces", summary.MostConnected[0].Table.Name);
        Assert.Equal(3, summary.MostConnected[0].Value);
    }

    [Fact]
    public void Vazba_do_sebe_sama_se_pocita_jednou()
    {
        var schema = Schema(Build.Table("Kategorie", ["Id", "RodicId"], ["Id"]));
        schema = schema with { Relationships = [Rel("Kategorie", "Kategorie")] };

        var summary = SchemaSummary.From(schema);

        Assert.Equal(1, summary.MostConnected[0].Value);
    }

    [Fact]
    public void Vazba_na_tabulku_mimo_schema_se_ignoruje()
    {
        var schema = Schema(Build.Table("Objednavky", ["Id"], ["Id"]));
        schema = schema with { Relationships = [Rel("Objednavky", "NeexistujiciTabulka")] };

        var summary = SchemaSummary.From(schema);

        Assert.Equal(1, summary.MostConnected[0].Value);
    }

    [Fact]
    public void Tabulka_bez_vazeb_je_osamocena()
    {
        var schema = Schema(
            Build.Table("Zakaznici", ["Id"], ["Id"]),
            Build.Table("Ciselnik", ["Id"], ["Id"]),
            Build.Table("Objednavky", ["Id"], ["Id"]));

        schema = schema with { Relationships = [Rel("Objednavky", "Zakaznici")] };

        var summary = SchemaSummary.From(schema);

        var jmeno = Assert.Single(summary.Isolated);
        Assert.Equal("Ciselnik", jmeno.Name);
    }

    [Fact]
    public void Tabulka_bez_primarniho_klice_se_ohlasi()
    {
        var schema = Schema(
            Build.Table("Zakaznici", ["Id"], ["Id"]),
            Build.Table("ImportniDavka", ["Radek"]));

        var jmeno = Assert.Single(SchemaSummary.From(schema).WithoutPrimaryKey);

        Assert.Equal("ImportniDavka", jmeno.Name);
    }

    [Fact]
    public void Pohled_bez_primarniho_klice_se_neohlasi()
    {
        // Pohled klíč mít nemusí, takže by to byl planý poplach.
        var schema = Schema(Build.Table("PrehledTrzeb", ["Castka"], isView: true));

        Assert.Empty(SchemaSummary.From(schema).WithoutPrimaryKey);
    }

    [Fact]
    public void Cizi_klic_bez_indexu_se_ohlasi()
    {
        var schema = Schema(
            Build.Table("Objednavky", ["Id", "ZakaznikId"], ["Id"],
                foreignKeys: [Build.ForeignKey("FK", ["ZakaznikId"], "Zakaznici")]));

        var nalez = Assert.Single(SchemaSummary.From(schema).UnindexedForeignKeys);

        Assert.Contains("ZakaznikId", nalez, StringComparison.Ordinal);
    }

    [Fact]
    public void Cizi_klic_pokryty_indexem_se_neohlasi()
    {
        var schema = Schema(
            Build.Table("Objednavky", ["Id", "ZakaznikId"], ["Id"],
                foreignKeys: [Build.ForeignKey("FK", ["ZakaznikId"], "Zakaznici")],
                indexes: [Build.Index("IX", ["ZakaznikId"])]));

        Assert.Empty(SchemaSummary.From(schema).UnindexedForeignKeys);
    }

    [Fact]
    public void Staci_kdyz_index_cizim_klicem_zacina()
    {
        // Složený index (ZakaznikId, Datum) se pro hledání podle ZakaznikId použít dá.
        var schema = Schema(
            Build.Table("Objednavky", ["Id", "ZakaznikId", "Datum"], ["Id"],
                foreignKeys: [Build.ForeignKey("FK", ["ZakaznikId"], "Zakaznici")],
                indexes: [Build.Index("IX", ["ZakaznikId", "Datum"])]));

        Assert.Empty(SchemaSummary.From(schema).UnindexedForeignKeys);
    }

    [Fact]
    public void Index_koncici_cizim_klicem_nestaci()
    {
        // Naopak (Datum, ZakaznikId) se pro hledání jen podle ZakaznikId použít nedá.
        var schema = Schema(
            Build.Table("Objednavky", ["Id", "ZakaznikId", "Datum"], ["Id"],
                foreignKeys: [Build.ForeignKey("FK", ["ZakaznikId"], "Zakaznici")],
                indexes: [Build.Index("IX", ["Datum", "ZakaznikId"])]));

        Assert.Single(SchemaSummary.From(schema).UnindexedForeignKeys);
    }

    [Fact]
    public void Cizi_klic_kryty_primarnim_klicem_se_neohlasi()
    {
        // U vazební tabulky je FK první částí složeného primárního klíče.
        var schema = Schema(
            Build.Table("SpaceComponents", ["CompositeSpaceId", "PartSpaceId"],
                ["CompositeSpaceId", "PartSpaceId"],
                foreignKeys: [Build.ForeignKey("FK", ["CompositeSpaceId"], "Spaces")]));

        Assert.Empty(SchemaSummary.From(schema).UnindexedForeignKeys);
    }

    [Fact]
    public void Cizi_klic_bez_sloupcu_se_neohlasi()
    {
        var fk = Build.ForeignKey("FK", [], "Zakaznici");
        var schema = Schema(Build.Table("Objednavky", ["Id"], ["Id"], foreignKeys: [fk]));

        Assert.Single(SchemaSummary.From(schema).UnindexedForeignKeys);
    }

    [Fact]
    public void Nejcastejsi_typy_se_seradi_podle_poctu()
    {
        var schema = new DatabaseSchema
        {
            Tables =
            [
                Sloupce("Zakaznici", ("Id", "int"), ("Jmeno", "nvarchar"), ("Email", "nvarchar")),
                Sloupce("Objednavky", ("Id", "int"), ("Poznamka", "nvarchar")),
            ],
        };

        var typy = SchemaSummary.From(schema).CommonTypes;

        Assert.Equal("nvarchar", typy[0].Type);
        Assert.Equal(3, typy[0].Count);
        Assert.Equal("int", typy[1].Type);
    }

    [Fact]
    public void Tabulky_se_rozdeli_podle_schemat()
    {
        var schema = new DatabaseSchema
        {
            Tables =
            [
                VeSchematu("sales", "Objednavky"),
                VeSchematu("sales", "Faktury"),
                VeSchematu("hr", "Zamestnanci"),
            ],
        };

        var summary = SchemaSummary.From(schema);

        Assert.Equal(2, summary.SchemaCount);
        Assert.Equal("sales", summary.BySchema[0].Schema);
        Assert.Equal(2, summary.BySchema[0].TableCount);
    }

    [Fact]
    public void Pocitaji_se_nullable_i_pocitane_sloupce()
    {
        var schema = new DatabaseSchema
        {
            Tables =
            [
                Build.Table("Zakaznici", ["Id", "Poznamka"], ["Id"], nullable: [false, true]) with
                {
                    Columns =
                    [
                        new DbColumn { Name = "Id", Ordinal = 1, StoreType = "int", IsPrimaryKey = true },
                        new DbColumn { Name = "Poznamka", Ordinal = 2, StoreType = "nvarchar", IsNullable = true },
                        new DbColumn { Name = "CeleJmeno", Ordinal = 3, StoreType = "nvarchar", IsComputed = true },
                    ],
                },
            ],
        };

        var summary = SchemaSummary.From(schema);

        Assert.Equal(1, summary.NullableColumnCount);
        Assert.Equal(1, summary.ComputedColumnCount);
    }

    [Fact]
    public void Vazebni_tabulky_se_pocitaji()
    {
        var schema = Schema(
            Build.Table("Spaces", ["Id"], ["Id"]),
            Build.Table("SpaceComponents", ["A", "B"], ["A", "B"]) with { IsJoinTable = true });

        Assert.Equal(1, SchemaSummary.From(schema).JoinTableCount);
    }

    [Fact]
    public void Zebricky_se_orezou_na_pet_polozek()
    {
        var tables = Enumerable
            .Range(1, 12)
            .Select(i => Build.Table($"T{i:00}", ["Id"], ["Id"]) with { RowCountEstimate = i * 100 })
            .ToArray();

        Assert.Equal(SchemaSummary.TopCount, SchemaSummary.From(Schema(tables)).LargestTables.Count);
    }


    [Fact]
    public void Vazebni_tabulka_neni_osamocena()
    {
        // Vazba N:M se ve vztazích sbalí na přímou Spaces–Tags a samotná SpaceTag
        // v seznamu vůbec není. Osamocená ale rozhodně není — pozná se to z cizích klíčů.
        var schema = Schema(
            Build.Table("Spaces", ["Id"], ["Id"]),
            Build.Table("Tags", ["Id"], ["Id"]),
            Build.Table("SpaceTag", ["SpacesId", "TagsId"], ["SpacesId", "TagsId"],
                foreignKeys:
                [
                    Build.ForeignKey("FK_Spaces", ["SpacesId"], "Spaces"),
                    Build.ForeignKey("FK_Tags", ["TagsId"], "Tags"),
                ]) with { IsJoinTable = true });

        schema = schema with { Relationships = [Rel("Spaces", "Tags")] };

        Assert.Empty(SchemaSummary.From(schema).Isolated);
    }

    [Fact]
    public void Tabulka_bez_vztahu_i_cizich_klicu_zustava_osamocena()
    {
        var schema = Schema(
            Build.Table("AuditLogy", ["Id"], ["Id"]),
            Build.Table("Zakaznici", ["Id"], ["Id"]),
            Build.Table("Objednavky", ["Id", "ZakaznikId"], ["Id"],
                foreignKeys: [Build.ForeignKey("FK", ["ZakaznikId"], "Zakaznici")]));

        var jmeno = Assert.Single(SchemaSummary.From(schema).Isolated);

        Assert.Equal("AuditLogy", jmeno.Name);
    }

    [Fact]
    public void Cizi_klic_do_sebe_sama_se_pocita_jednou()
    {
        var schema = Schema(
            Build.Table("Kategorie", ["Id", "RodicId"], ["Id"],
                foreignKeys: [Build.ForeignKey("FK", ["RodicId"], "Kategorie")]));

        Assert.Equal(1, SchemaSummary.From(schema).MostConnected[0].Value);
    }

    private static DatabaseSchema Schema(params DbTable[] tables) => new() { Tables = tables };

    private static DbRelationship Rel(string from, string to) => new()
    {
        Id = $"fk:{from}->{to}",
        From = new DbObjectName(null, from),
        To = new DbObjectName(null, to),
    };

    private static DbTable Sloupce(string name, params (string Name, string Type)[] columns) => new()
    {
        Name = new DbObjectName(null, name),
        Columns =
        [
            .. columns.Select((c, i) => new DbColumn { Name = c.Name, Ordinal = i + 1, StoreType = c.Type }),
        ],
    };

    private static DbTable VeSchematu(string schema, string name) => new()
    {
        Name = new DbObjectName(schema, name),
    };
}
