using DbsViewer.EfCore;
using DbsViewer.SampleMigrations;

namespace DbsViewer.Tests.EfCore;

/// <summary>
/// Čtení historie schématu z EF migrací. Běží proti ukázkovému projektu se skutečnými
/// migracemi — snapshoty se dají číst jen z assembly, která je opravdu obsahuje.
/// </summary>
public class MigrationHistoryTests
{
    private static MigrationHistoryReader Reader() =>
        new(BlogContextFactory.Create());

    [Fact]
    public void Migrace_se_vypisou_v_poradi_aplikace()
    {
        var ids = Reader().Ids;

        Assert.Equal(3, ids.Count);
        Assert.EndsWith("ZakladniModel", ids[0], StringComparison.Ordinal);
        Assert.EndsWith("PridanoPublikovano", ids[1], StringComparison.Ordinal);
        Assert.EndsWith("PridanyKomentare", ids[2], StringComparison.Ordinal);
    }

    [Fact]
    public void Neznama_migrace_se_pozna()
    {
        var reader = Reader();

        Assert.True(reader.Has(reader.Ids[0]));
        Assert.False(reader.Has("20990101_Neexistuje"));
        Assert.False(reader.Has(null!));
    }

    // ---------- co migrace změnila ----------

    [Fact]
    public void Prvni_migrace_zaklada_tabulky()
    {
        var reader = Reader();
        var zmeny = reader.GetChanges(reader.Ids[0]);

        Assert.Contains(zmeny, z => z.Kind == SchemaChangeKind.CreateTable
                                    && z.Table?.Name == "Autori");

        Assert.Contains(zmeny, z => z.Kind == SchemaChangeKind.CreateTable
                                    && z.Table?.Name == "Clanky");

        // Unikátní index na e-mailu je součástí základu.
        Assert.Contains(zmeny, z => z.Kind == SchemaChangeKind.CreateIndex
                                    && z.Description.Contains("unikátní", StringComparison.Ordinal));
    }

    [Fact]
    public void Druha_migrace_pridava_jen_sloupec()
    {
        var reader = Reader();
        var zmena = Assert.Single(reader.GetChanges(reader.Ids[1]));

        Assert.Equal(SchemaChangeKind.AddColumn, zmena.Kind);
        Assert.Equal("Clanky", zmena.Table?.Name);
        Assert.Equal("Publikovano", zmena.Object);
        Assert.Contains("NULL", zmena.After!, StringComparison.Ordinal);
    }

    [Fact]
    public void Treti_migrace_pridava_tabulku_i_s_vazbou()
    {
        var reader = Reader();
        var zmeny = reader.GetChanges(reader.Ids[2]);

        Assert.Contains(zmeny, z => z.Kind == SchemaChangeKind.CreateTable
                                    && z.Table?.Name == "Komentare");
    }

    [Fact]
    public void Zmeny_neznamé_migrace_jsou_prazdne() =>
        Assert.Empty(Reader().GetChanges("20990101_Neexistuje"));

    // ---------- schéma v čase ----------

    [Fact]
    public void Schema_po_prvni_migraci_jeste_nezna_pozdejsi_sloupec()
    {
        var reader = Reader();
        var schema = reader.ReadAt(reader.Ids[0]);

        var clanky = Assert.Single(schema.Tables, t => t.Name.Name == "Clanky");

        Assert.DoesNotContain(clanky.Columns, c => c.Name == "Publikovano");
        Assert.Equal(2, schema.Tables.Count);
    }

    [Fact]
    public void Schema_po_druhe_migraci_uz_sloupec_ma()
    {
        var reader = Reader();
        var schema = reader.ReadAt(reader.Ids[1]);

        var clanky = Assert.Single(schema.Tables, t => t.Name.Name == "Clanky");
        var sloupec = Assert.Single(clanky.Columns, c => c.Name == "Publikovano");

        Assert.True(sloupec.IsNullable);
    }

    [Fact]
    public void Schema_po_treti_migraci_ma_novou_tabulku()
    {
        var reader = Reader();
        var schema = reader.ReadAt(reader.Ids[2]);

        Assert.Equal(3, schema.Tables.Count);
        Assert.Contains(schema.Tables, t => t.Name.Name == "Komentare");
    }

    [Fact]
    public void Historicke_schema_zna_i_vazby()
    {
        var reader = Reader();
        var schema = reader.ReadAt(reader.Ids[2]);

        // Vazby musí fungovat, jinak by z historického schématu nešel nakreslit diagram.
        Assert.Contains(schema.Relationships, r => r.From.Name == "Clanky" && r.To.Name == "Autori");
        Assert.Contains(schema.Relationships, r => r.From.Name == "Komentare" && r.To.Name == "Clanky");
    }

    [Fact]
    public void Historicke_schema_zna_indexy_i_klice()
    {
        var reader = Reader();
        var autori = Assert.Single(reader.ReadAt(reader.Ids[0]).Tables, t => t.Name.Name == "Autori");

        Assert.NotNull(autori.PrimaryKey);
        Assert.Contains(autori.Indexes, i => i.IsUnique && i.Columns.Contains("Email"));
    }

    [Fact]
    public void Historicke_schema_se_oznaci_jako_snapshot()
    {
        var reader = Reader();
        var schema = reader.ReadAt(reader.Ids[1]);

        Assert.Equal(SchemaSourceKind.MigrationSnapshot, schema.SourceKind);
        Assert.Contains(reader.Ids[1], schema.SourceName!, StringComparison.Ordinal);

        // Seznam migrací patří k živému pohledu, ne ke snapshotu v minulosti.
        Assert.Empty(schema.Migrations);
    }

    [Fact]
    public void Schema_neznamé_migrace_je_chyba_s_vysvetlenim()
    {
        var chyba = Assert.Throws<InvalidOperationException>(
            () => Reader().ReadAt("20990101_Neexistuje"));

        Assert.Contains("není v assembly", chyba.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cteni_bez_kontextu_je_chyba_argumentu() =>
        Assert.Throws<ArgumentNullException>(() => new MigrationHistoryReader(null!));
}
