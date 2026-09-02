using Bunit;
using DbsViewer.Ui.Components;

namespace DbsViewer.Tests.Ui;

/// <summary>Časová osa migrací a přepínání na historickou verzi schématu.</summary>
public class HistorieTests : TestContext
{
    private static DbMigration Migrace(
        string id,
        bool applied = true,
        bool inAssembly = true,
        params DbSchemaChange[] changes) => new()
        {
            Id = id,
            AppliedInDatabase = applied,
            PresentInAssembly = inAssembly,
            HasSnapshot = inAssembly,
            Changes = changes,
        };

    private static DbSchemaChange Zmena(
        SchemaChangeKind kind,
        string description,
        string? before = null,
        string? after = null) => new()
        {
            Kind = kind,
            Description = description,
            Before = before,
            After = after,
        };

    private static IReadOnlyList<DbMigration> Ukazka() =>
    [
        Migrace("20260101_Zaklad", changes:
        [
            Zmena(SchemaChangeKind.CreateTable, "Vytvořena tabulka Autori"),
        ]),
        Migrace("20260202_Sloupec", changes:
        [
            Zmena(SchemaChangeKind.AddColumn, "Přidán sloupec Clanky.Publikovano", after: "TEXT, NULL"),
        ]),
    ];

    private IRenderedComponent<HistorieSchematu> Osa(
        IReadOnlyList<DbMigration>? migrace = null,
        string? vybrana = null) =>
        RenderComponent<HistorieSchematu>(p => p
            .Add(x => x.Migrations, migrace ?? Ukazka())
            .Add(x => x.SelectedMigration, vybrana));

    [Fact]
    public void Bez_migraci_to_rekne()
    {
        var component = RenderComponent<HistorieSchematu>(p => p.Add(x => x.Migrations, []));

        Assert.Contains("nepoužívá EF migrace", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Migrace_se_vypisou_od_nejnovejsi()
    {
        // Nejnovější zajímá nejčastěji, takže patří nahoru.
        var jmena = Osa().FindAll(".casova-osa .jmeno").Select(e => e.TextContent).ToList();

        Assert.Equal(["Sloupec", "Zaklad"], jmena);
    }

    [Fact]
    public void Zmeny_migrace_jsou_videt()
    {
        var markup = Osa().Markup;

        Assert.Contains("Přidán sloupec Clanky.Publikovano", markup, StringComparison.Ordinal);
        Assert.Contains("Vytvořena tabulka Autori", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Casove_razitko_se_z_nazvu_odrizne()
    {
        Assert.Equal("Zaklad", HistorieSchematu.Kratce("20260101_Zaklad"));
        Assert.Equal("Bez razitka", HistorieSchematu.Kratce("Bez razitka"));
        Assert.Null(HistorieSchematu.Kratce(null));
        Assert.Null(HistorieSchematu.Kratce(""));

        // Podtržítko na konci není oddělovač, ale součást jména.
        Assert.Equal("2026_", HistorieSchematu.Kratce("2026_"));
    }

    // ---------- stav migrace ----------

    [Fact]
    public void Cekajici_migrace_se_odlisi()
    {
        var component = Osa([Migrace("20260303_Ceka", applied: false)]);

        Assert.Contains("čeká na nasazení", component.Markup, StringComparison.Ordinal);
        Assert.Single(component.FindAll(".casova-osa li.ceka"));
    }

    [Fact]
    public void Migrace_bez_kodu_nenabizi_prepnuti()
    {
        // Migrace proběhla, ale její kód už v projektu není — snapshot k ní neexistuje.
        var component = Osa([Migrace("20251212_Zmizela", inAssembly: false)]);

        Assert.Contains("chybí v kódu", component.Markup, StringComparison.Ordinal);
        Assert.Contains("schéma není k dispozici", component.Markup, StringComparison.Ordinal);
        Assert.Empty(component.FindAll(".prepnout"));
    }

    [Fact]
    public void Migrace_bez_zmen_to_rekne()
    {
        var component = Osa([Migrace("20260404_Prazdna")]);

        Assert.Contains("schéma nemění", component.Markup, StringComparison.Ordinal);
    }

    // ---------- přepnutí na verzi ----------

    [Fact]
    public void Klik_na_zobrazit_schema_ohlasi_migraci()
    {
        string? prepnuto = null;

        var component = RenderComponent<HistorieSchematu>(p => p
            .Add(x => x.Migrations, Ukazka())
            .Add(x => x.OnSwitch, (string id) => prepnuto = id));

        component.FindAll(".prepnout").ElementAt(0).Click();

        // První v seznamu je nejnovější migrace.
        Assert.Equal("20260202_Sloupec", prepnuto);
    }

    [Fact]
    public void Zobrazena_verze_je_oznacena()
    {
        var component = Osa(vybrana: "20260101_Zaklad");

        Assert.Single(component.FindAll(".casova-osa li.vybrana"));
        Assert.Contains("✓ zobrazeno", component.Markup, StringComparison.Ordinal);
    }

    // ---------- porovnání verzí ----------

    [Fact]
    public void Porovnani_ohlasi_obe_verze()
    {
        (string? From, string To)? porovnano = null;

        var component = RenderComponent<HistorieSchematu>(p => p
            .Add(x => x.Migrations, Ukazka())
            .Add(x => x.From, "20260101_Zaklad")
            .Add(x => x.To, "20260202_Sloupec")
            .Add(x => x.OnCompare, (( string? From, string To) r) => porovnano = r));

        component.Find(".porovnani button.hlavni").Click();

        Assert.Equal(("20260101_Zaklad", "20260202_Sloupec"), porovnano);
    }

    [Fact]
    public void Bez_cilove_verze_se_porovnat_neda()
    {
        var component = Osa();

        Assert.True(component.Find(".porovnani button.hlavni").HasAttribute("disabled"));
    }

    [Fact]
    public void Zmena_rozsahu_se_ohlasi()
    {
        (string? From, string? To)? rozsah = null;

        var component = RenderComponent<HistorieSchematu>(p => p
            .Add(x => x.Migrations, Ukazka())
            .Add(x => x.To, "20260202_Sloupec")
            .Add(x => x.OnRangeChange, ((string? From, string? To) r) => rozsah = r));

        component.FindAll(".porovnani select").ElementAt(0).Change("20260101_Zaklad");

        Assert.Equal("20260101_Zaklad", rozsah?.From);
    }

    [Fact]
    public void Zmena_cilove_verze_se_take_ohlasi()
    {
        (string? From, string? To)? rozsah = null;

        var component = RenderComponent<HistorieSchematu>(p => p
            .Add(x => x.Migrations, Ukazka())
            .Add(x => x.From, "20260101_Zaklad")
            .Add(x => x.OnRangeChange, ((string? From, string? To) r) => rozsah = r));

        component.FindAll(".porovnani select").ElementAt(1).Change("20260202_Sloupec");

        Assert.Equal("20260101_Zaklad", rozsah?.From);
        Assert.Equal("20260202_Sloupec", rozsah?.To);
    }

    [Fact]
    public void Vysledek_porovnani_se_vykresli()
    {
        var diff = new Analysis.SchemaDiff
        {
            Findings =
            [
                new Analysis.DiffFinding
                {
                    Kind = Analysis.DiffKind.ColumnMissingInModel,
                    Severity = Analysis.DiffSeverity.Warning,
                    Message = "Sloupec Publikovano přibyl",
                    Table = new DbObjectName(null, "Clanky"),
                },
            ],
        };

        var component = RenderComponent<HistorieSchematu>(p => p
            .Add(x => x.Migrations, Ukazka())
            .Add(x => x.From, "20260101_Zaklad")
            .Add(x => x.To, "20260202_Sloupec")
            .Add(x => x.Diff, diff));

        // Zpráva se v historii překládá do řeči času; tabulka a rozsah zůstávají.
        Assert.Contains("Sloupec přibyl", component.Markup, StringComparison.Ordinal);
        Assert.Contains("Clanky", component.Markup, StringComparison.Ordinal);
        // Nadpis říká směr porovnání, aby nešlo splést, která verze je výchozí.
        Assert.Contains("Co se změnilo od", component.Markup, StringComparison.Ordinal);
        Assert.Contains("ve směru času", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Porovnani_jde_zavrit()
    {
        var zavreno = false;

        var component = RenderComponent<HistorieSchematu>(p => p
            .Add(x => x.Migrations, Ukazka())
            .Add(x => x.To, "20260202_Sloupec")
            .Add(x => x.Diff, new Analysis.SchemaDiff { Findings = [] })
            .Add(x => x.OnCloseDiff, () => zavreno = true));

        component.Find(".porovnani button.odkaz").Click();

        Assert.True(zavreno);
    }

    // ---------- vlastní SQL ----------

    [Fact]
    public void Vlastni_SQL_se_oznaci_jako_neprohledne()
    {
        var component = Osa(
        [
            Migrace("20260505_Sql", changes:
            [
                Zmena(SchemaChangeKind.Sql, "Vlastní SQL příkaz", after: "UPDATE …"),
            ]),
        ]);

        Assert.Contains("obsahuje vlastní SQL", component.Markup, StringComparison.Ordinal);
        Assert.Single(component.FindAll(".zmeny li.neznama"));
    }

    // ---------- barvy změn ----------

    // ---------- slova podle kontextu ----------

    [Fact]
    public void V_historii_se_nalezy_ctou_ve_smeru_casu()
    {
        // „Sloupec je v databázi, ale v modelu není" dává smysl u driftu; v historii
        // je to prostě „sloupec přibyl".
        var diff = new Analysis.SchemaDiff
        {
            Findings =
            [
                new Analysis.DiffFinding
                {
                    Kind = Analysis.DiffKind.ColumnMissingInModel,
                    Severity = Analysis.DiffSeverity.Warning,
                    Message = "Sloupec je v databázi, ale v modelu není.",
                    Table = new DbObjectName(null, "Clanky"),
                },
            ],
        };

        var component = RenderComponent<HistorieSchematu>(p => p
            .Add(x => x.Migrations, Ukazka())
            .Add(x => x.To, "20260202_Sloupec")
            .Add(x => x.Diff, diff));

        Assert.Contains("Sloupec přibyl", component.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("v modelu není", component.Markup, StringComparison.Ordinal);
        Assert.Contains("Starší verze", component.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SchemaChangeKind.CreateTable, "pribylo", "+")]
    [InlineData(SchemaChangeKind.AddColumn, "pribylo", "+")]
    [InlineData(SchemaChangeKind.AddForeignKey, "pribylo", "+")]
    [InlineData(SchemaChangeKind.DropTable, "ubylo", "−")]
    [InlineData(SchemaChangeKind.DropIndex, "ubylo", "−")]
    [InlineData(SchemaChangeKind.AlterColumn, "zmeneno", "~")]
    [InlineData(SchemaChangeKind.RenameTable, "zmeneno", "~")]
    [InlineData(SchemaChangeKind.Sql, "neznama", "?")]
    public void Zmena_ma_barvu_i_znak_podle_druhu(SchemaChangeKind kind, string trida, string znak)
    {
        Assert.Equal(trida, HistorieSchematu.ZmenaTridy(kind));
        Assert.Equal(znak, HistorieSchematu.Znak(kind));
    }

    [Fact]
    public void Zmena_ukazuje_stary_i_novy_stav()
    {
        var component = Osa(
        [
            Migrace("20260606_Typ", changes:
            [
                Zmena(SchemaChangeKind.AlterColumn, "Změněn sloupec", before: "int, NULL", after: "bigint, NOT NULL"),
            ]),
        ]);

        Assert.Single(component.FindAll(".hodnota.pred"));
        Assert.Single(component.FindAll(".hodnota.po"));
        Assert.Contains("bigint", component.Markup, StringComparison.Ordinal);
    }
}
