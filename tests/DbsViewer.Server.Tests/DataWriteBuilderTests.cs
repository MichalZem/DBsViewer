using DbsViewer.Server;
using DbsViewer.TestKit;

namespace DbsViewer.Tests.Server;

/// <summary>
/// Skládání UPDATE a DELETE. Zápis je jediné místo, kde prohlížečka mění obsah databáze,
/// takže testy hlídají hlavně to, co se **nesmí** stát: dotaz bez klíče, dotaz nad
/// sloupcem, který se měnit nemá, a hodnota, která do sloupce nepatří.
/// </summary>
public class DataWriteBuilderTests
{
    private static DbColumn Column(
        string name,
        string storeType = "nvarchar(200)",
        bool isPrimaryKey = false,
        bool isNullable = false,
        bool isComputed = false,
        bool isIdentity = false) => new()
        {
            Name = name,
            StoreType = storeType,
            IsPrimaryKey = isPrimaryKey,
            IsNullable = isNullable,
            IsComputed = isComputed,
            IsIdentity = isIdentity,
        };

    /// <summary>Zákazníci: klíč <c>Id</c>, povinný <c>Email</c>, nepovinné <c>Jmeno</c>.</summary>
    private static DbTable Table(params DbColumn[] columns) => new()
    {
        Name = new DbObjectName(null, "Zakaznici"),
        Columns = columns.Length > 0
            ? columns
            :
            [
                Column("Id", "int", isPrimaryKey: true),
                Column("Email"),
                Column("Jmeno", isNullable: true),
            ],
        PrimaryKey = new DbPrimaryKey { Columns = columns.Length > 0 ? [columns[0].Name] : ["Id"] },
    };

    private static DataUpdate Update(params DataValue[] values) => new()
    {
        Key = [new DataValue("Id", "7")],
        Values = values,
    };

    // ---------- UPDATE ----------

    [Fact]
    public void Update_meni_jen_poslane_sloupce()
    {
        var query = DataQueryBuilder.BuildUpdate(
            Table(),
            Update(new DataValue("Email", "novy@x.cz")),
            [],
            isSqlite: true);

        Assert.Equal("UPDATE \"Zakaznici\" SET \"Email\" = @p0 WHERE \"Id\" = @p1", query.Sql);
        Seq.Equal(["novy@x.cz", 7L], query.Parameters);
    }

    [Fact]
    public void Update_pro_sql_server_escapuje_hranatymi_zavorkami()
    {
        var query = DataQueryBuilder.BuildUpdate(
            Table(),
            Update(new DataValue("Email", "novy@x.cz")),
            [],
            isSqlite: false);

        Assert.Equal("UPDATE [Zakaznici] SET [Email] = @p0 WHERE [Id] = @p1", query.Sql);
    }

    [Fact]
    public void Update_bere_jmeno_sloupce_ze_schematu_ne_z_pozadavku()
    {
        // Do textu dotazu jde jméno ze schématu, i když se v požadavku liší velikostí písmen.
        var query = DataQueryBuilder.BuildUpdate(
            Table(),
            Update(new DataValue("eMaIl", "novy@x.cz")),
            [],
            isSqlite: true);

        Assert.Contains("\"Email\" = @p0", query.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Update_bez_zmen_nema_smysl()
    {
        var chyba = Assert.Throws<DataRequestException>(() =>
            DataQueryBuilder.BuildUpdate(Table(), Update(), [], isSqlite: true));

        Assert.Equal("Požadavek nemění žádný sloupec.", chyba.Message);
    }

    [Fact]
    public void Update_neznameho_sloupce_se_odmitne()
    {
        var chyba = Assert.Throws<DataRequestException>(() =>
            DataQueryBuilder.BuildUpdate(
                Table(),
                Update(new DataValue("Neexistuje", "x")),
                [],
                isSqlite: true));

        Assert.Contains("Neexistuje", chyba.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Update_primarniho_klice_se_odmitne()
    {
        var chyba = Assert.Throws<DataRequestException>(() =>
            DataQueryBuilder.BuildUpdate(Table(), Update(new DataValue("Id", "9")), [], isSqlite: true));

        Assert.Contains("primárního klíče", chyba.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Update_zamaskovaneho_sloupce_se_odmitne()
    {
        var chyba = Assert.Throws<DataRequestException>(() =>
            DataQueryBuilder.BuildUpdate(
                Table(),
                Update(new DataValue("Email", "x@x.cz")),
                ["Email"],
                isSqlite: true));

        Assert.Contains("zamaskovaný", chyba.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Tentyz_sloupec_dvakrat_se_odmitne()
    {
        var chyba = Assert.Throws<DataRequestException>(() =>
            DataQueryBuilder.BuildUpdate(
                Table(),
                Update(new DataValue("Email", "a@x.cz"), new DataValue("email", "b@x.cz")),
                [],
                isSqlite: true));

        Assert.Contains("dvakrát", chyba.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Null_do_nepovinneho_sloupce_projde()
    {
        var query = DataQueryBuilder.BuildUpdate(
            Table(),
            Update(new DataValue("Jmeno", null)),
            [],
            isSqlite: true);

        Seq.Equal([DBNull.Value, 7L], query.Parameters);
    }

    [Fact]
    public void Null_do_povinneho_sloupce_se_odmitne()
    {
        var chyba = Assert.Throws<DataRequestException>(() =>
            DataQueryBuilder.BuildUpdate(Table(), Update(new DataValue("Email", null)), [], isSqlite: true));

        Assert.Equal("Sloupec Email nesmí být NULL.", chyba.Message);
    }

    // ---------- DELETE ----------

    [Fact]
    public void Delete_se_ridi_klicem()
    {
        var query = DataQueryBuilder.BuildDelete(
            Table(),
            new DataDelete { Key = [new DataValue("Id", "7")] },
            [],
            isSqlite: true);

        Assert.Equal("DELETE FROM \"Zakaznici\" WHERE \"Id\" = @p0", query.Sql);
        Seq.Equal([7L], query.Parameters);
    }

    [Fact]
    public void Slozeny_klic_musi_byt_cely()
    {
        var table = new DbTable
        {
            Name = new DbObjectName(null, "Radky"),
            Columns = [Column("OrderId", "int", isPrimaryKey: true), Column("LineNumber", "int", isPrimaryKey: true)],
            PrimaryKey = new DbPrimaryKey { Columns = ["OrderId", "LineNumber"] },
        };

        var query = DataQueryBuilder.BuildDelete(
            table,
            new DataDelete { Key = [new DataValue("OrderId", "3"), new DataValue("LineNumber", "1")] },
            [],
            isSqlite: true);

        Assert.Equal("DELETE FROM \"Radky\" WHERE \"OrderId\" = @p0 AND \"LineNumber\" = @p1", query.Sql);

        var chyba = Assert.Throws<DataRequestException>(() =>
            DataQueryBuilder.BuildDelete(
                table,
                new DataDelete { Key = [new DataValue("OrderId", "3")] },
                [],
                isSqlite: true));

        Assert.Equal("V požadavku chybí hodnota klíče LineNumber.", chyba.Message);
    }

    [Fact]
    public void Prazdna_hodnota_klice_radek_neurci()
    {
        var chyba = Assert.Throws<DataRequestException>(() =>
            DataQueryBuilder.BuildDelete(
                Table(),
                new DataDelete { Key = [new DataValue("Id", null)] },
                [],
                isSqlite: true));

        Assert.Equal("Hodnota klíče Id je prázdná.", chyba.Message);
    }

    [Fact]
    public void Do_tabulky_bez_klice_se_nezapisuje()
    {
        var table = Table() with { PrimaryKey = null };

        var chyba = Assert.Throws<DataRequestException>(() =>
            DataQueryBuilder.BuildDelete(table, new DataDelete(), [], isSqlite: true));

        Assert.Contains("primárním klíčem", chyba.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Do_pohledu_se_nezapisuje()
    {
        var table = Table() with { IsView = true };

        Assert.Throws<DataRequestException>(() =>
            DataQueryBuilder.BuildDelete(
                table,
                new DataDelete { Key = [new DataValue("Id", "7")] },
                [],
                isSqlite: true));
    }

    // ---------- povinné argumenty ----------

    [Fact]
    public void Argumenty_jsou_povinne()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DataQueryBuilder.BuildUpdate(null!, Update(), [], isSqlite: true));
        Assert.Throws<ArgumentNullException>(() =>
            DataQueryBuilder.BuildUpdate(Table(), null!, [], isSqlite: true));
        Assert.Throws<ArgumentNullException>(() =>
            DataQueryBuilder.BuildUpdate(Table(), Update(), null!, isSqlite: true));

        Assert.Throws<ArgumentNullException>(() =>
            DataQueryBuilder.BuildDelete(null!, new DataDelete(), [], isSqlite: true));
        Assert.Throws<ArgumentNullException>(() =>
            DataQueryBuilder.BuildDelete(Table(), null!, [], isSqlite: true));
        Assert.Throws<ArgumentNullException>(() =>
            DataQueryBuilder.BuildDelete(Table(), new DataDelete(), null!, isSqlite: true));
    }

    // ---------- převod hodnot ----------

    [Theory]
    [InlineData("bit", "true", true)]
    [InlineData("bit", "1", true)]
    [InlineData("bit", "False", false)]
    [InlineData("bit", "0", false)]
    [InlineData("int", "42", 42L)]
    [InlineData("bigint", "-9", -9L)]
    [InlineData("INTEGER", "3", 3L)]
    [InlineData("float", "1.5", 1.5d)]
    [InlineData("nvarchar(200)", "text", "text")]
    [InlineData("neznamy_typ", "text", "text")]
    public void Hodnota_se_prevede_podle_typu_v_databazi(string storeType, string value, object expected)
    {
        var query = DataQueryBuilder.BuildUpdate(
            Table(Column("Id", "int", isPrimaryKey: true), Column("Hodnota", storeType, isNullable: true)),
            new DataUpdate { Key = [new DataValue("Id", "1")], Values = [new DataValue("Hodnota", value)] },
            [],
            isSqlite: true);

        Assert.Equal(expected, query.Parameters[0]);
    }

    [Fact]
    public void Desetinne_cislo_a_datum_se_ctou_invariantne()
    {
        var table = Table(
            Column("Id", "int", isPrimaryKey: true),
            Column("Castka", "decimal(18, 2)", isNullable: true),
            Column("Kdy", "datetime2", isNullable: true),
            Column("Posun", "datetimeoffset", isNullable: true),
            Column("Cas", "time", isNullable: true),
            Column("Klic", "uniqueidentifier", isNullable: true));

        var query = DataQueryBuilder.BuildUpdate(
            table,
            new DataUpdate
            {
                Key = [new DataValue("Id", "1")],
                Values =
                [
                    new DataValue("Castka", "1234.56"),
                    new DataValue("Kdy", "2026-09-04T10:11:12.0000000"),
                    new DataValue("Posun", "2026-09-04T10:11:12.0000000+02:00"),
                    new DataValue("Cas", "10:11:12"),
                    new DataValue("Klic", "8c2e2b62-8e2f-4a2f-9f0e-3f2b5c7a1d90"),
                ],
            },
            [],
            isSqlite: true);

        Assert.Equal(1234.56m, query.Parameters[0]);
        Assert.Equal(new DateTime(2026, 9, 4, 10, 11, 12, DateTimeKind.Unspecified), query.Parameters[1]);
        Assert.Equal(
            new DateTimeOffset(2026, 9, 4, 10, 11, 12, TimeSpan.FromHours(2)),
            query.Parameters[2]);
        Assert.Equal(new TimeSpan(10, 11, 12), query.Parameters[3]);
        Assert.Equal(Guid.Parse("8c2e2b62-8e2f-4a2f-9f0e-3f2b5c7a1d90"), query.Parameters[4]);
    }

    [Theory]
    [InlineData("bit", "mozna")]
    [InlineData("int", "sedm")]
    [InlineData("decimal(18, 2)", "1,5")]
    [InlineData("float", "spousta")]
    [InlineData("uniqueidentifier", "abc")]
    [InlineData("datetime2", "vcera")]
    [InlineData("datetimeoffset", "vcera")]
    [InlineData("time", "vecer")]
    public void Nepouzitelna_hodnota_se_odmitne_uz_na_serveru(string storeType, string value)
    {
        var chyba = Assert.Throws<DataRequestException>(() =>
            DataQueryBuilder.BuildUpdate(
                Table(Column("Id", "int", isPrimaryKey: true), Column("Hodnota", storeType, isNullable: true)),
                new DataUpdate { Key = [new DataValue("Id", "1")], Values = [new DataValue("Hodnota", value)] },
                [],
                isSqlite: true));

        Assert.Contains("Hodnotu", chyba.Message, StringComparison.Ordinal);
        Assert.Contains(storeType, chyba.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Pocitany_a_generovany_sloupec_se_neprepisuji()
    {
        var table = Table(
            Column("Id", "int", isPrimaryKey: true),
            Column("Celkem", "decimal(18, 2)", isComputed: true),
            Column("Poradi", "int", isIdentity: true),
            Column("Foto", "varbinary(max)", isNullable: true));

        foreach (var sloupec in new[] { "Celkem", "Poradi", "Foto" })
        {
            Assert.Throws<DataRequestException>(() =>
                DataQueryBuilder.BuildUpdate(
                    table,
                    new DataUpdate { Key = [new DataValue("Id", "1")], Values = [new DataValue(sloupec, "1")] },
                    [],
                    isSqlite: true));
        }
    }

    [Fact]
    public void Vyjimka_zapisu_umi_nest_i_puvodni_chybu()
    {
        var vnitrni = new InvalidOperationException("databáze");
        var chyba = new DataRequestException("nešlo to", vnitrni);

        Assert.Equal("nešlo to", chyba.Message);
        Assert.Same(vnitrni, chyba.InnerException);
    }
}
