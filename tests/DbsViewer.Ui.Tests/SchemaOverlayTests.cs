using DbsViewer.TestKit;
using DbsViewer.Ui.Model;

namespace DbsViewer.Tests.Ui;

/// <summary>
/// Vrstva změn nad schématem. Kromě stavů jednotlivých objektů hlídá i to, že se
/// do schématu přimíchá, co zmizelo — bez toho by nešlo ukázat, co ubylo.
/// </summary>
public class SchemaOverlayTests
{
    private static DatabaseSchema Schema(params DbTable[] tables) => new() { Tables = tables };

    private static DbColumn Sloupec(
        string name,
        string typ = "int",
        bool nullable = false,
        bool pk = false) => new()
        {
            Name = name,
            Ordinal = 1,
            StoreType = typ,
            IsNullable = nullable,
            IsPrimaryKey = pk,
        };

    private static DbTable Tabulka(string name, params DbColumn[] columns) => new()
    {
        Name = new DbObjectName(null, name),
        Columns = columns,
    };

    private static DbObjectName N(string name) => new(null, name);

    // ---------- bez porovnání ----------

    [Fact]
    public void Prazdny_prekryv_nic_neoznaci()
    {
        var schema = Schema(Tabulka("Clanky", Sloupec("Id")));
        var overlay = SchemaOverlay.None(schema);

        Assert.False(overlay.JeAktivni);
        Assert.Equal(0, overlay.PocetZmen);
        Assert.Equal(ZmenaStav.Beze, overlay.Tabulka(N("Clanky")));
        Assert.Equal(ZmenaStav.Beze, overlay.Sloupec(N("Clanky"), "Id"));
        Assert.Same(schema, overlay.Schema);
    }

    [Fact]
    public void Null_je_chyba_argumentu()
    {
        Assert.Throws<ArgumentNullException>(() => SchemaOverlay.None(null!));
        Assert.Throws<ArgumentNullException>(() => SchemaOverlay.Build(null!, Schema()));
        Assert.Throws<ArgumentNullException>(() => SchemaOverlay.Build(Schema(), null!));
        Assert.Throws<ArgumentNullException>(() => SchemaOverlay.None(Schema()).Vazba(null!));
    }

    // ---------- sloupce ----------

    [Fact]
    public void Novy_sloupec_se_oznaci_jako_pribyly()
    {
        var stara = Schema(Tabulka("Clanky", Sloupec("Id")));
        var nova = Schema(Tabulka("Clanky", Sloupec("Id"), Sloupec("Publikovano", nullable: true)));

        var overlay = SchemaOverlay.Build(stara, nova);

        Assert.Equal(ZmenaStav.Pribylo, overlay.Sloupec(N("Clanky"), "Publikovano"));
        Assert.Equal(ZmenaStav.Beze, overlay.Sloupec(N("Clanky"), "Id"));
        Assert.Equal(ZmenaStav.Zmeneno, overlay.Tabulka(N("Clanky")));
    }

    [Fact]
    public void Zmizely_sloupec_zustane_v_seznamu_jako_duch()
    {
        // Právě tohle je smysl celého překryvu: co zmizelo, musí být vidět.
        var stara = Schema(Tabulka("Clanky", Sloupec("Id"), Sloupec("Stary")));
        var nova = Schema(Tabulka("Clanky", Sloupec("Id")));

        var overlay = SchemaOverlay.Build(stara, nova);
        var clanky = Assert.Single(overlay.Schema.Tables);

        Assert.Equal(ZmenaStav.Ubylo, overlay.Sloupec(N("Clanky"), "Stary"));
        Assert.Contains(clanky.Columns, c => c.Name == "Stary");
        Assert.Equal(2, clanky.Columns.Count);
    }

    [Fact]
    public void Zmena_typu_sloupce_se_oznaci()
    {
        var stara = Schema(Tabulka("Objednavky", Sloupec("Castka", "int")));
        var nova = Schema(Tabulka("Objednavky", Sloupec("Castka", "bigint")));

        Assert.Equal(
            ZmenaStav.Zmeneno,
            SchemaOverlay.Build(stara, nova).Sloupec(N("Objednavky"), "Castka"));
    }

    [Fact]
    public void Zmena_nullability_se_oznaci()
    {
        var stara = Schema(Tabulka("Clanky", Sloupec("Nadpis", nullable: true)));
        var nova = Schema(Tabulka("Clanky", Sloupec("Nadpis", nullable: false)));

        Assert.Equal(
            ZmenaStav.Zmeneno,
            SchemaOverlay.Build(stara, nova).Sloupec(N("Clanky"), "Nadpis"));
    }

    [Fact]
    public void Zmena_primarniho_klice_se_oznaci()
    {
        var stara = Schema(Tabulka("Clanky", Sloupec("Id", pk: false)));
        var nova = Schema(Tabulka("Clanky", Sloupec("Id", pk: true)));

        Assert.Equal(
            ZmenaStav.Zmeneno,
            SchemaOverlay.Build(stara, nova).Sloupec(N("Clanky"), "Id"));
    }

    [Fact]
    public void Zmena_defaultu_se_oznaci()
    {
        var stara = Schema(Tabulka("Clanky", Sloupec("Stav")));
        var nova = Schema(Tabulka("Clanky", Sloupec("Stav") with { DefaultValueSql = "0" }));

        Assert.Equal(
            ZmenaStav.Zmeneno,
            SchemaOverlay.Build(stara, nova).Sloupec(N("Clanky"), "Stav"));
    }

    [Fact]
    public void Shodna_tabulka_zustane_bez_oznaceni()
    {
        var schema = Schema(Tabulka("Clanky", Sloupec("Id"), Sloupec("Nadpis", "text")));

        var overlay = SchemaOverlay.Build(schema, schema);

        Assert.Equal(ZmenaStav.Beze, overlay.Tabulka(N("Clanky")));
        Assert.Equal(0, overlay.PocetZmen);
        Assert.True(overlay.JeAktivni);
    }

    // ---------- tabulky ----------

    [Fact]
    public void Nova_tabulka_se_oznaci_i_se_sloupci()
    {
        var stara = Schema(Tabulka("Clanky", Sloupec("Id")));
        var nova = Schema(Tabulka("Clanky", Sloupec("Id")), Tabulka("Komentare", Sloupec("Id")));

        var overlay = SchemaOverlay.Build(stara, nova);

        Assert.Equal(ZmenaStav.Pribylo, overlay.Tabulka(N("Komentare")));
        Assert.Equal(ZmenaStav.Pribylo, overlay.Sloupec(N("Komentare"), "Id"));
    }

    [Fact]
    public void Zmizela_tabulka_zustane_ve_schematu_jako_duch()
    {
        var stara = Schema(Tabulka("Clanky", Sloupec("Id")), Tabulka("Stara", Sloupec("Id")));
        var nova = Schema(Tabulka("Clanky", Sloupec("Id")));

        var overlay = SchemaOverlay.Build(stara, nova);

        Assert.Equal(ZmenaStav.Ubylo, overlay.Tabulka(N("Stara")));
        Assert.Equal(2, overlay.Schema.Tables.Count);
        Assert.Contains(overlay.Schema.Tables, t => t.Name.Name == "Stara");
    }

    // ---------- indexy ----------

    [Fact]
    public void Novy_index_se_oznaci()
    {
        var stara = Schema(Tabulka("Autori", Sloupec("Email")));
        var nova = Schema(Tabulka("Autori", Sloupec("Email")) with
        {
            Indexes = [Build.Index("IX_Email", ["Email"], isUnique: true)],
        });

        var overlay = SchemaOverlay.Build(stara, nova);

        Assert.Equal(ZmenaStav.Pribylo, overlay.Index(N("Autori"), "IX_Email"));
    }

    [Fact]
    public void Zmizely_index_zustane_jako_duch()
    {
        var stara = Schema(Tabulka("Autori", Sloupec("Email")) with
        {
            Indexes = [Build.Index("IX_Stary", ["Email"])],
        });

        var nova = Schema(Tabulka("Autori", Sloupec("Email")));

        var overlay = SchemaOverlay.Build(stara, nova);
        var autori = Assert.Single(overlay.Schema.Tables);

        Assert.Equal(ZmenaStav.Ubylo, overlay.Index(N("Autori"), "IX_Stary"));
        Assert.Single(autori.Indexes);
    }

    [Fact]
    public void Zmena_unikatnosti_indexu_se_oznaci()
    {
        var stara = Schema(Tabulka("Autori", Sloupec("Email")) with
        {
            Indexes = [Build.Index("IX_Email", ["Email"])],
        });

        var nova = Schema(Tabulka("Autori", Sloupec("Email")) with
        {
            Indexes = [Build.Index("IX_Email", ["Email"], isUnique: true)],
        });

        Assert.Equal(
            ZmenaStav.Zmeneno,
            SchemaOverlay.Build(stara, nova).Index(N("Autori"), "IX_Email"));
    }

    [Fact]
    public void Zmena_sloupcu_indexu_se_oznaci()
    {
        var stara = Schema(Tabulka("Objednavky", Sloupec("A"), Sloupec("B")) with
        {
            Indexes = [Build.Index("IX", ["A"])],
        });

        var nova = Schema(Tabulka("Objednavky", Sloupec("A"), Sloupec("B")) with
        {
            Indexes = [Build.Index("IX", ["A", "B"])],
        });

        Assert.Equal(
            ZmenaStav.Zmeneno,
            SchemaOverlay.Build(stara, nova).Index(N("Objednavky"), "IX"));
    }

    // ---------- vazby ----------

    private static DbRelationship Vazba(
        string from,
        string to,
        DbDeleteBehavior delete = DbDeleteBehavior.NoAction,
        bool required = true) => new()
        {
            Id = $"fk:{from}->{to}",
            From = new DbObjectName(null, from),
            To = new DbObjectName(null, to),
            FromColumns = [$"{to}Id"],
            DeleteBehavior = delete,
            IsRequired = required,
        };

    [Fact]
    public void Nova_vazba_se_oznaci()
    {
        var tabulky = new[] { Tabulka("Clanky", Sloupec("Id")), Tabulka("Autori", Sloupec("Id")) };

        var stara = new DatabaseSchema { Tables = tabulky };
        var nova = new DatabaseSchema { Tables = tabulky, Relationships = [Vazba("Clanky", "Autori")] };

        var overlay = SchemaOverlay.Build(stara, nova);

        Assert.Equal(ZmenaStav.Pribylo, overlay.Vazba(Vazba("Clanky", "Autori")));
    }

    [Fact]
    public void Zmizela_vazba_zustane_v_diagramu_jako_duch()
    {
        var tabulky = new[] { Tabulka("Clanky", Sloupec("Id")), Tabulka("Autori", Sloupec("Id")) };

        var stara = new DatabaseSchema { Tables = tabulky, Relationships = [Vazba("Clanky", "Autori")] };
        var nova = new DatabaseSchema { Tables = tabulky };

        var overlay = SchemaOverlay.Build(stara, nova);

        Assert.Equal(ZmenaStav.Ubylo, overlay.Vazba(Vazba("Clanky", "Autori")));
        Assert.Single(overlay.Schema.Relationships);
    }

    [Fact]
    public void Vazba_na_neexistujici_tabulku_se_nekresli()
    {
        // Čára, která vede nikam, by v diagramu jen mátla.
        var stara = new DatabaseSchema
        {
            Tables = [Tabulka("Clanky", Sloupec("Id")), Tabulka("Zmizela", Sloupec("Id"))],
            Relationships = [Vazba("Clanky", "Zmizela")],
        };

        var nova = new DatabaseSchema { Tables = [Tabulka("Clanky", Sloupec("Id"))] };

        var overlay = SchemaOverlay.Build(stara, nova);

        // Tabulka Zmizela je duch, takže vazba na ni smysl dává a nakreslí se.
        Assert.Single(overlay.Schema.Relationships);
        Assert.Equal(2, overlay.Schema.Tables.Count);
    }

    [Fact]
    public void Zmena_chovani_pri_mazani_se_oznaci()
    {
        var tabulky = new[] { Tabulka("Clanky", Sloupec("Id")), Tabulka("Autori", Sloupec("Id")) };

        var stara = new DatabaseSchema
        {
            Tables = tabulky,
            Relationships = [Vazba("Clanky", "Autori", DbDeleteBehavior.NoAction)],
        };

        var nova = new DatabaseSchema
        {
            Tables = tabulky,
            Relationships = [Vazba("Clanky", "Autori", DbDeleteBehavior.Cascade)],
        };

        Assert.Equal(
            ZmenaStav.Zmeneno,
            SchemaOverlay.Build(stara, nova).Vazba(Vazba("Clanky", "Autori", DbDeleteBehavior.Cascade)));
    }

    [Fact]
    public void Zmena_povinnosti_vazby_se_oznaci()
    {
        var tabulky = new[] { Tabulka("Fotky", Sloupec("Id")), Tabulka("Mista", Sloupec("Id")) };

        var stara = new DatabaseSchema
        {
            Tables = tabulky,
            Relationships = [Vazba("Fotky", "Mista", required: true)],
        };

        var nova = new DatabaseSchema
        {
            Tables = tabulky,
            Relationships = [Vazba("Fotky", "Mista", required: false)],
        };

        Assert.Equal(
            ZmenaStav.Zmeneno,
            SchemaOverlay.Build(stara, nova).Vazba(Vazba("Fotky", "Mista", required: false)));
    }

    [Fact]
    public void Shodna_vazba_zustane_bez_oznaceni()
    {
        var tabulky = new[] { Tabulka("Clanky", Sloupec("Id")), Tabulka("Autori", Sloupec("Id")) };

        var schema = new DatabaseSchema { Tables = tabulky, Relationships = [Vazba("Clanky", "Autori")] };

        Assert.Equal(
            ZmenaStav.Beze,
            SchemaOverlay.Build(schema, schema).Vazba(Vazba("Clanky", "Autori")));
    }

    [Fact]
    public void Nova_tabulka_oznaci_i_svoje_indexy()
    {
        var stara = Schema(Tabulka("Clanky", Sloupec("Id")));

        var nova = Schema(
            Tabulka("Clanky", Sloupec("Id")),
            Tabulka("Autori", Sloupec("Email")) with
            {
                Indexes = [Build.Index("IX_Email", ["Email"], isUnique: true)],
            });

        var overlay = SchemaOverlay.Build(stara, nova);

        Assert.Equal(ZmenaStav.Pribylo, overlay.Index(N("Autori"), "IX_Email"));
    }

    [Fact]
    public void Zmizela_tabulka_oznaci_i_svoje_indexy()
    {
        var stara = Schema(
            Tabulka("Clanky", Sloupec("Id")),
            Tabulka("Stara", Sloupec("Email")) with
            {
                Indexes = [Build.Index("IX_Stary", ["Email"])],
            });

        var nova = Schema(Tabulka("Clanky", Sloupec("Id")));

        var overlay = SchemaOverlay.Build(stara, nova);

        Assert.Equal(ZmenaStav.Ubylo, overlay.Index(N("Stara"), "IX_Stary"));
        Assert.Equal(ZmenaStav.Ubylo, overlay.Sloupec(N("Stara"), "Email"));
    }

    // ---------- souhrn ----------

    [Fact]
    public void Pocet_zmen_secte_vsechny_druhy()
    {
        var stara = Schema(Tabulka("Clanky", Sloupec("Id"), Sloupec("Stary")));

        var nova = Schema(
            Tabulka("Clanky", Sloupec("Id"), Sloupec("Novy")),
            Tabulka("Komentare", Sloupec("Id")));

        var overlay = SchemaOverlay.Build(stara, nova);

        // Clanky změněné, Novy přibyl, Stary ubyl, Komentare přibyly i se sloupcem.
        Assert.Equal(5, overlay.PocetZmen);
    }
}
