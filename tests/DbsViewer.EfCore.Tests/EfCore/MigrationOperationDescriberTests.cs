using DbsViewer.EfCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace DbsViewer.Tests.EfCore;

/// <summary>
/// Převod operací migrace na popis změny. Testuje se přímo, protože skutečné migrace
/// pokrývají jen zlomek typů operací — zbytek by jinak zůstal neověřený.
/// </summary>
public class MigrationOperationDescriberTests
{
    // ---------- tabulky ----------

    [Fact]
    public void Vytvoreni_tabulky_uvede_pocet_sloupcu()
    {
        var operation = new CreateTableOperation { Name = "Objednavky", Schema = "prodej" };
        operation.Columns.Add(new AddColumnOperation { Name = "Id", Table = "Objednavky" });
        operation.Columns.Add(new AddColumnOperation { Name = "Castka", Table = "Objednavky" });

        var zmena = MigrationOperationDescriber.Describe(operation);

        Assert.Equal(SchemaChangeKind.CreateTable, zmena.Kind);
        Assert.Equal("prodej", zmena.Table?.Schema);
        Assert.Contains("2 sloupce", zmena.Description, StringComparison.Ordinal);
        Assert.Equal("Id, Castka", zmena.After);
    }

    [Fact]
    public void Odstraneni_tabulky()
    {
        var zmena = MigrationOperationDescriber.Describe(
            new DropTableOperation { Name = "Stara" });

        Assert.Equal(SchemaChangeKind.DropTable, zmena.Kind);
        Assert.Contains("Odstraněna tabulka Stara", zmena.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Prejmenovani_tabulky_nese_obe_jmena()
    {
        var zmena = MigrationOperationDescriber.Describe(
            new RenameTableOperation { Name = "Stara", NewName = "Nova" });

        Assert.Equal(SchemaChangeKind.RenameTable, zmena.Kind);
        Assert.Equal("Stara", zmena.Before);
        Assert.Equal("Nova", zmena.After);
    }

    // ---------- sloupce ----------

    [Fact]
    public void Pridani_sloupce_nese_typ_i_nullability()
    {
        var zmena = MigrationOperationDescriber.Describe(new AddColumnOperation
        {
            Name = "Poznamka",
            Table = "Clanky",
            ColumnType = "nvarchar(200)",
            IsNullable = true,
        });

        Assert.Equal(SchemaChangeKind.AddColumn, zmena.Kind);
        Assert.Equal("Poznamka", zmena.Object);
        Assert.Equal("nvarchar(200), NULL", zmena.After);
    }

    [Fact]
    public void Pridani_sloupce_s_defaultem_ho_uvede()
    {
        var zmena = MigrationOperationDescriber.Describe(new AddColumnOperation
        {
            Name = "Stav",
            Table = "Objednavky",
            ColumnType = "int",
            IsNullable = false,
            DefaultValueSql = "0",
        });

        Assert.Contains("NOT NULL", zmena.After!, StringComparison.Ordinal);
        Assert.Contains("DEFAULT 0", zmena.After!, StringComparison.Ordinal);
    }

    [Fact]
    public void Odstraneni_sloupce()
    {
        var zmena = MigrationOperationDescriber.Describe(
            new DropColumnOperation { Name = "Stary", Table = "Clanky" });

        Assert.Equal(SchemaChangeKind.DropColumn, zmena.Kind);
        Assert.Equal("Stary", zmena.Object);
    }

    [Fact]
    public void Zmena_sloupce_ukazuje_stav_pred_i_po()
    {
        var zmena = MigrationOperationDescriber.Describe(new AlterColumnOperation
        {
            Name = "Castka",
            Table = "Objednavky",
            ColumnType = "decimal(18,2)",
            IsNullable = false,
            OldColumn = new AddColumnOperation
            {
                Name = "Castka",
                Table = "Objednavky",
                ColumnType = "int",
                IsNullable = true,
            },
        });

        Assert.Equal(SchemaChangeKind.AlterColumn, zmena.Kind);
        Assert.Equal("int, NULL", zmena.Before);
        Assert.Equal("decimal(18,2), NOT NULL", zmena.After);
    }

    [Fact]
    public void Zmena_sloupce_bez_typu_v_puvodnim_stavu_nespadne()
    {
        // EF OldColumn vždycky vyplní, ale typ v něm zůstat prázdný může.
        var zmena = MigrationOperationDescriber.Describe(new AlterColumnOperation
        {
            Name = "Castka",
            Table = "Objednavky",
            ColumnType = "int",
        });

        Assert.NotNull(zmena.Before);
        Assert.NotNull(zmena.After);
    }

    [Fact]
    public void Prejmenovani_sloupce()
    {
        var zmena = MigrationOperationDescriber.Describe(new RenameColumnOperation
        {
            Name = "Stary",
            NewName = "Novy",
            Table = "Clanky",
        });

        Assert.Equal(SchemaChangeKind.RenameColumn, zmena.Kind);
        Assert.Equal("Stary", zmena.Before);
        Assert.Equal("Novy", zmena.After);
    }

    // ---------- indexy a klíče ----------

    [Fact]
    public void Vytvoreni_unikatniho_indexu_se_odlisi()
    {
        var zmena = MigrationOperationDescriber.Describe(new CreateIndexOperation
        {
            Name = "IX_Email",
            Table = "Autori",
            Columns = ["Email"],
            IsUnique = true,
        });

        Assert.Equal(SchemaChangeKind.CreateIndex, zmena.Kind);
        Assert.Contains("unikátní index", zmena.Description, StringComparison.Ordinal);
        Assert.Equal("Email", zmena.After);
    }

    [Fact]
    public void Vytvoreni_obycejneho_indexu()
    {
        var zmena = MigrationOperationDescriber.Describe(new CreateIndexOperation
        {
            Name = "IX_Datum",
            Table = "Objednavky",
            Columns = ["Datum", "Stav"],
        });

        Assert.DoesNotContain("unikátní", zmena.Description, StringComparison.Ordinal);
        Assert.Equal("Datum, Stav", zmena.After);
    }

    [Fact]
    public void Odstraneni_indexu()
    {
        var zmena = MigrationOperationDescriber.Describe(
            new DropIndexOperation { Name = "IX_Stary", Table = "Objednavky" });

        Assert.Equal(SchemaChangeKind.DropIndex, zmena.Kind);
    }

    [Fact]
    public void Odstraneni_indexu_bez_tabulky_nespadne()
    {
        // DropIndex je jediná operace, která tabulku znát nemusí — některé providery
        // ruší index jen podle jména.
        var zmena = MigrationOperationDescriber.Describe(
            new DropIndexOperation { Name = "IX_Stary" });

        Assert.Equal(SchemaChangeKind.DropIndex, zmena.Kind);
        Assert.Null(zmena.Table);
        Assert.Equal("IX_Stary", zmena.Object);
    }

    [Fact]
    public void Pridani_ciziho_klice_nese_smer_i_chovani_pri_mazani()
    {
        var zmena = MigrationOperationDescriber.Describe(new AddForeignKeyOperation
        {
            Name = "FK_Objednavky_Zakaznici",
            Table = "Objednavky",
            Columns = ["ZakaznikId"],
            PrincipalTable = "Zakaznici",
            OnDelete = Microsoft.EntityFrameworkCore.Migrations.ReferentialAction.Cascade,
        });

        Assert.Equal(SchemaChangeKind.AddForeignKey, zmena.Kind);
        Assert.Contains("Objednavky → Zakaznici", zmena.Description, StringComparison.Ordinal);
        Assert.Contains("Cascade", zmena.After!, StringComparison.Ordinal);
    }

    [Fact]
    public void Odstraneni_ciziho_klice()
    {
        var zmena = MigrationOperationDescriber.Describe(
            new DropForeignKeyOperation { Name = "FK_Stary", Table = "Objednavky" });

        Assert.Equal(SchemaChangeKind.DropForeignKey, zmena.Kind);
    }

    [Fact]
    public void Pridani_primarniho_klice()
    {
        var zmena = MigrationOperationDescriber.Describe(new AddPrimaryKeyOperation
        {
            Name = "PK_Objednavky",
            Table = "Objednavky",
            Columns = ["Id"],
        });

        Assert.Equal(SchemaChangeKind.AddPrimaryKey, zmena.Kind);
        Assert.Equal("Id", zmena.After);
    }

    [Fact]
    public void Odstraneni_primarniho_klice()
    {
        var zmena = MigrationOperationDescriber.Describe(
            new DropPrimaryKeyOperation { Name = "PK_Stary", Table = "Objednavky" });

        Assert.Equal(SchemaChangeKind.DropPrimaryKey, zmena.Kind);
    }

    // ---------- vlastní SQL a data ----------

    [Fact]
    public void Vlastni_SQL_se_oznaci_jako_nepruhledne()
    {
        var zmena = MigrationOperationDescriber.Describe(
            new SqlOperation { Sql = "UPDATE Objednavky SET Stav = 'nova'" });

        Assert.Equal(SchemaChangeKind.Sql, zmena.Kind);
        Assert.True(zmena.IsOpaque);
        Assert.Contains("UPDATE Objednavky", zmena.After!, StringComparison.Ordinal);
    }

    [Fact]
    public void Dlouhy_SQL_prikaz_se_zkrati()
    {
        var dlouhy = new string('x', 300);
        var zmena = MigrationOperationDescriber.Describe(new SqlOperation { Sql = dlouhy });

        Assert.True(zmena.After!.Length < 130);
        Assert.EndsWith("…", zmena.After, StringComparison.Ordinal);
    }

    [Fact]
    public void Viceradkovy_SQL_se_slozi_na_jeden_radek()
    {
        var zmena = MigrationOperationDescriber.Describe(
            new SqlOperation { Sql = "UPDATE T\n  SET A = 1\n  WHERE B = 2" });

        Assert.DoesNotContain('\n', zmena.After!);
        Assert.Contains("UPDATE T SET A = 1 WHERE B = 2", zmena.After!, StringComparison.Ordinal);
    }

    [Fact]
    public void Vlozeni_dat_uvede_pocet_radku()
    {
        var zmena = MigrationOperationDescriber.Describe(new InsertDataOperation
        {
            Table = "Ciselnik",
            Columns = ["Id", "Nazev"],
            Values = new object[,] { { 1, "A" }, { 2, "B" }, { 3, "C" } },
        });

        Assert.Equal(SchemaChangeKind.Data, zmena.Kind);
        Assert.Contains("3 řádky", zmena.Description, StringComparison.Ordinal);
        Assert.False(zmena.IsOpaque);
    }

    [Fact]
    public void Smazani_dat()
    {
        var zmena = MigrationOperationDescriber.Describe(new DeleteDataOperation
        {
            Table = "Ciselnik",
            KeyColumns = ["Id"],
            KeyValues = new object[,] { { 1 } },
        });

        Assert.Equal(SchemaChangeKind.Data, zmena.Kind);
        Assert.Contains("1 řádek", zmena.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Zmena_dat()
    {
        var zmena = MigrationOperationDescriber.Describe(new UpdateDataOperation
        {
            Table = "Ciselnik",
            KeyColumns = ["Id"],
            KeyValues = new object[,] { { 1 }, { 2 }, { 3 }, { 4 }, { 5 } },
            Columns = ["Nazev"],
            Values = new object[,] { { "A" }, { "B" }, { "C" }, { "D" }, { "E" } },
        });

        Assert.Equal(SchemaChangeKind.Data, zmena.Kind);
        Assert.Contains("5 řádků", zmena.Description, StringComparison.Ordinal);
    }

    // ---------- neznámé operace ----------

    [Fact]
    public void Nezname_operaci_se_aspon_pojmenuje()
    {
        // Typů operací má EF desítky; ty neznámé se popíšou aspoň jménem.
        var zmena = MigrationOperationDescriber.Describe(new EnsureSchemaOperation { Name = "prodej" });

        Assert.Equal(SchemaChangeKind.Other, zmena.Kind);
        Assert.Equal("EnsureSchema", zmena.Description);
    }
}
