using Bunit;
using DbsViewer.TestKit;
using DbsViewer.Ui.Components;
using DbsViewer.Ui.Model;

namespace DbsViewer.Tests.Ui;

/// <summary>Vykreslení přehledu celé databáze.</summary>
public class PrehledTests : TestContext
{
    [Fact]
    public void Prazdna_databaze_se_vykresli_bez_pádu()
    {
        var component = Render(new DatabaseSchema());

        Assert.Contains("tabulek", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Zakladni_cisla_se_ukazou()
    {
        var component = Render(Ukazka());

        Assert.Contains("Zdroj", component.Markup, StringComparison.Ordinal);
        Assert.Contains("Eshop", component.Markup, StringComparison.Ordinal);

        // Dvě tabulky, jeden pohled.
        Assert.Contains("2</span>", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Pohledy_se_ukazou_jen_kdyz_nejake_jsou()
    {
        var bez = Render(Schema(Build.Table("Zakaznici", ["Id"], ["Id"])));
        Assert.DoesNotContain("pohled", bez.Markup, StringComparison.OrdinalIgnoreCase);

        var s = Render(Schema(Build.Table("Prehled", ["X"], isView: true)));
        Assert.Contains("pohled", s.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Odhad_radku_se_ukaze_kdyz_ho_databaze_zna()
    {
        var schema = Schema(Build.Table("Objednavky", ["Id"], ["Id"]) with { RowCountEstimate = 12345 });

        // Nedělitelná mezera po tisících, nezávisle na kultuře stroje.
        Assert.Contains("12 345", Render(schema).Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Zebricek_nejvetsich_tabulek_odkazuje_na_detail()
    {
        DbObjectName? vybrano = null;

        var schema = Schema(Build.Table("Objednavky", ["Id"], ["Id"]) with { RowCountEstimate = 500 });

        var component = RenderComponent<PrehledDatabaze>(p => p
            .Add(c => c.Schema, schema)
            .Add(c => c.Summary, SchemaSummary.From(schema))
            .Add(c => c.OnSelect, (DbObjectName n) => vybrano = n));

        component.FindAll(".prehled-zebricek .odkaz").ElementAt(0).Click();

        Assert.Equal("Objednavky", vybrano?.Name);
    }

    [Fact]
    public void Varovani_z_nacteni_se_vypisou()
    {
        var schema = new DatabaseSchema { Warnings = ["Indexy se nepodařilo přečíst."] };

        Assert.Contains("nepodařilo přečíst", Render(schema).Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Bez_varovani_se_sekce_nevykresli()
    {
        Assert.DoesNotContain("prehled-varovani", Render(Ukazka()).Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Ciste_schema_hlasi_ze_neni_co_resit()
    {
        var schema = Schema(
            Build.Table("Zakaznici", ["Id"], ["Id"]),
            Build.Table("Objednavky", ["Id", "ZakaznikId"], ["Id"],
                foreignKeys: [Build.ForeignKey("FK", ["ZakaznikId"], "Zakaznici")],
                indexes: [Build.Index("IX", ["ZakaznikId"])]));

        schema = schema with
        {
            Relationships =
            [
                new DbRelationship
                {
                    Id = "fk:1",
                    From = new DbObjectName(null, "Objednavky"),
                    To = new DbObjectName(null, "Zakaznici"),
                },
            ],
        };

        Assert.Contains("Nic nápadného", Render(schema).Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Tabulka_bez_klice_se_ohlasi_jako_podnet()
    {
        var schema = Schema(Build.Table("ImportniDavka", ["Radek"]));

        var markup = Render(schema).Markup;

        Assert.Contains("Bez primárního klíče", markup, StringComparison.Ordinal);
        Assert.Contains("ImportniDavka", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Neindexovany_cizi_klic_se_ohlasi_jako_podnet()
    {
        var schema = Schema(
            Build.Table("Objednavky", ["Id", "ZakaznikId"], ["Id"],
                foreignKeys: [Build.ForeignKey("FK", ["ZakaznikId"], "Zakaznici")]));

        Assert.Contains("Cizí klíč bez indexu", Render(schema).Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Osamocena_tabulka_se_ohlasi_jako_podnet()
    {
        var schema = Schema(Build.Table("Ciselnik", ["Id"], ["Id"]));

        Assert.Contains("Bez vazeb", Render(schema).Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Vazebni_tabulky_se_ohlasi_jako_podnet()
    {
        var schema = Schema(
            Build.Table("SpaceComponents", ["A", "B"], ["A", "B"]) with { IsJoinTable = true });

        Assert.Contains("N:M", Render(schema).Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Klik_na_tabulku_v_podnetech_ji_vybere()
    {
        DbObjectName? vybrano = null;
        var schema = Schema(Build.Table("ImportniDavka", ["Radek"]));

        var component = RenderComponent<PrehledDatabaze>(p => p
            .Add(c => c.Schema, schema)
            .Add(c => c.Summary, SchemaSummary.From(schema))
            .Add(c => c.OnSelect, (DbObjectName n) => vybrano = n));

        component.FindAll(".podnety .odkaz").ElementAt(0).Click();

        Assert.Equal("ImportniDavka", vybrano?.Name);
    }

    [Fact]
    public void Dlouhy_seznam_se_orizne_a_zbytek_se_shrne()
    {
        // Deset tabulek bez klíče; vypíše se osm a zbytek se sečte.
        var tables = Enumerable
            .Range(1, 10)
            .Select(i => Build.Table($"Bez{i:00}", ["X"]))
            .ToArray();

        var markup = Render(Schema(tables)).Markup;

        Assert.Contains("a další 2 položky", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Bez10", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Dlouhy_seznam_cizich_klicu_se_take_orizne()
    {
        var tables = Enumerable
            .Range(1, 10)
            .Select(i => Build.Table($"T{i:00}", ["Id", "CizId"], ["Id"],
                foreignKeys: [Build.ForeignKey("FK", ["CizId"], "Jina")]))
            .ToArray();

        Assert.Contains("a další 2 položky", Render(Schema(tables)).Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Vice_schemat_se_vypise_v_puvodu()
    {
        var schema = new DatabaseSchema
        {
            Tables =
            [
                new DbTable { Name = new DbObjectName("sales", "Objednavky") },
                new DbTable { Name = new DbObjectName("hr", "Zamestnanci") },
            ],
        };

        var markup = Render(schema).Markup;

        Assert.Contains("Schémata", markup, StringComparison.Ordinal);
        Assert.Contains("sales", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Migrace_se_vypisou_kdyz_jsou()
    {
        var schema = new DatabaseSchema
        {
            Migrations = [new DbMigration { Id = "20260101_Init" }, new DbMigration { Id = "20260202_Ceny" }],
        };

        var markup = Render(schema).Markup;

        Assert.Contains("Migrace", markup, StringComparison.Ordinal);
        Assert.Contains("20260202_Ceny", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Nejcastejsi_typy_se_vypisou()
    {
        var schema = new DatabaseSchema
        {
            Tables =
            [
                new DbTable
                {
                    Name = new DbObjectName(null, "Zakaznici"),
                    Columns = [new DbColumn { Name = "Jmeno", Ordinal = 1, StoreType = "nvarchar" }],
                },
            ],
        };

        Assert.Contains("nvarchar", Render(schema).Markup, StringComparison.Ordinal);
    }

    private IRenderedComponent<PrehledDatabaze> Render(DatabaseSchema schema) =>
        RenderComponent<PrehledDatabaze>(p => p
            .Add(c => c.Schema, schema)
            .Add(c => c.Summary, SchemaSummary.From(schema)));

    private static DatabaseSchema Schema(params DbTable[] tables) => new() { Tables = tables };

    private static DatabaseSchema Ukazka()
    {
        var schema = Schema(
            Build.Table("Zakaznici", ["Id"], ["Id"]),
            Build.Table("Objednavky", ["Id", "ZakaznikId"], ["Id"],
                foreignKeys: [Build.ForeignKey("FK", ["ZakaznikId"], "Zakaznici")],
                indexes: [Build.Index("IX", ["ZakaznikId"])]));

        return schema with
        {
            DatabaseName = "Eshop",
            SourceName = "SQL Server (Eshop)",
            Relationships =
            [
                new DbRelationship
                {
                    Id = "fk:1",
                    From = new DbObjectName(null, "Objednavky"),
                    To = new DbObjectName(null, "Zakaznici"),
                },
            ],
        };
    }
}
