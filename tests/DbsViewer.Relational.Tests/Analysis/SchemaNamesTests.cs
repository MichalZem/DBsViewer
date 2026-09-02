using DbsViewer.Analysis;
using DbsViewer.TestKit;

namespace DbsViewer.Tests.Relational;

/// <summary>
/// Srovnání jmen před párováním schémat. Bez něj se tabulka, kterou EF hlásí bez
/// schématu a databáze jako <c>dbo.Neco</c>, objevila dvakrát.
/// </summary>
public class SchemaNamesTests
{
    private static DatabaseSchema Model(params DbTable[] tables) => new()
    {
        Tables = tables,
        SourceKind = SchemaSourceKind.EfModel,
    };

    private static DatabaseSchema Database(string? defaultSchema, params DbTable[] tables) => new()
    {
        Tables = tables,
        DefaultSchema = defaultSchema,
        SourceKind = SchemaSourceKind.LiveDatabase,
    };

    private static DbTable Tabulka(string? schema, string name, params string[] columns) => new()
    {
        Name = new DbObjectName(schema, name),
        Columns = [.. columns.Select((c, i) => new DbColumn { Name = c, Ordinal = i + 1, StoreType = "int" })],
    };

    // ---------- samotné srovnání ----------

    [Fact]
    public void Tabulce_bez_schematu_se_doplni_vychozi()
    {
        var schema = SchemaNames.Normalize(Model(Tabulka(null, "Audit")), "dbo");

        Assert.Equal("dbo", Assert.Single(schema.Tables).Name.Schema);
    }

    [Fact]
    public void Tabulka_se_schematem_zustane()
    {
        var schema = SchemaNames.Normalize(Model(Tabulka("prodej", "Objednavky")), "dbo");

        Assert.Equal("prodej", Assert.Single(schema.Tables).Name.Schema);
    }

    [Fact]
    public void Bez_vychoziho_schematu_se_nemeni_nic()
    {
        // SQLite schémata nemá, takže doplňovat není co.
        var puvodni = Model(Tabulka(null, "Audit"));

        Assert.Same(puvodni, SchemaNames.Normalize(puvodni));
        Assert.Same(puvodni, SchemaNames.Normalize(puvodni, ""));
    }

    [Fact]
    public void Vychozi_schema_se_vezme_ze_schematu_kdyz_neni_zadane()
    {
        var puvodni = Model(Tabulka(null, "Audit")) with { DefaultSchema = "dbo" };

        Assert.Equal("dbo", Assert.Single(SchemaNames.Normalize(puvodni).Tables).Name.Schema);
    }

    [Fact]
    public void Srovnaji_se_i_cizi_klice()
    {
        var tabulka = Tabulka(null, "Objednavky", "Id", "ZakaznikId") with
        {
            ForeignKeys = [Build.ForeignKey("FK", ["ZakaznikId"], "Zakaznici")],
        };

        var schema = SchemaNames.Normalize(Model(tabulka), "dbo");
        var fk = Assert.Single(Assert.Single(schema.Tables).ForeignKeys);

        Assert.Equal("dbo", fk.PrincipalTable.Schema);
    }

    [Fact]
    public void Srovnaji_se_i_vazby()
    {
        var puvodni = Model(Tabulka(null, "Objednavky"), Tabulka(null, "Zakaznici")) with
        {
            Relationships =
            [
                new DbRelationship
                {
                    Id = "fk:1",
                    From = new DbObjectName(null, "Objednavky"),
                    To = new DbObjectName(null, "Zakaznici"),
                    ViaJoinTable = new DbObjectName(null, "Vazebni"),
                },
            ],
        };

        var vazba = Assert.Single(SchemaNames.Normalize(puvodni, "dbo").Relationships);

        Assert.Equal("dbo", vazba.From.Schema);
        Assert.Equal("dbo", vazba.To.Schema);
        Assert.Equal("dbo", vazba.ViaJoinTable?.Schema);
    }

    [Fact]
    public void Vazba_bez_vazebni_tabulky_se_nerozbije()
    {
        var puvodni = Model(Tabulka(null, "A")) with
        {
            Relationships =
            [
                new DbRelationship
                {
                    Id = "fk:1",
                    From = new DbObjectName(null, "A"),
                    To = new DbObjectName(null, "B"),
                },
            ],
        };

        Assert.Null(Assert.Single(SchemaNames.Normalize(puvodni, "dbo").Relationships).ViaJoinTable);
    }

    [Fact]
    public void Jmeno_se_srovna_i_samostatne()
    {
        Assert.Equal("dbo", SchemaNames.Normalize(new DbObjectName(null, "T"), "dbo").Schema);
        Assert.Equal("prodej", SchemaNames.Normalize(new DbObjectName("prodej", "T"), "dbo").Schema);
        Assert.Null(SchemaNames.Normalize(new DbObjectName(null, "T"), null).Schema);
    }

    [Fact]
    public void Null_schema_je_chyba_argumentu() =>
        Assert.Throws<ArgumentNullException>(() => SchemaNames.Normalize(null!, "dbo"));

    // ---------- dopad na slučování a porovnání ----------

    [Fact]
    public void Slouceni_neduplikuje_tabulku_bez_schematu()
    {
        // Přesně ten případ, který se projevil na SQL Serveru: EF hlásí Audit,
        // databáze dbo.Audit — a ve sloučeném schématu byly obě.
        var model = Model(Tabulka(null, "Audit", "Id"));
        var databaze = Database("dbo", Tabulka("dbo", "Audit", "Id"));

        var slouceno = SchemaMerger.Merge(model, databaze);

        Assert.Single(slouceno.Tables);
        Assert.Equal("dbo", slouceno.Tables[0].Name.Schema);
    }

    [Fact]
    public void Porovnani_nehlasi_rozdil_jen_kvuli_chybejicimu_schematu()
    {
        var model = Model(Tabulka(null, "Audit", "Id"));
        var databaze = Database("dbo", Tabulka("dbo", "Audit", "Id"));

        Assert.Empty(SchemaComparer.Compare(model, databaze).Findings);
    }

    [Fact]
    public void Tabulka_v_jinem_schematu_je_porad_rozdil()
    {
        // Srovnání se týká jen chybějícího schématu, ne odlišného.
        var model = Model(Tabulka("prodej", "Audit", "Id"));
        var databaze = Database("dbo", Tabulka("dbo", "Audit", "Id"));

        Assert.NotEmpty(SchemaComparer.Compare(model, databaze).Findings);
    }
}
