using DbsViewer.TestKit;

namespace DbsViewer.Tests.Abstractions;

/// <summary>
/// Pravidla o tom, co se v řádku smí měnit. Stejnou odpověď používá server i UI,
/// takže na nich stojí jak nabídka v mřížce, tak odmítnutí zápisu.
/// </summary>
public class RowEditingTests
{
    private static DbColumn Column(
        string name = "Email",
        string storeType = "nvarchar(200)",
        bool isPrimaryKey = false,
        bool isIdentity = false,
        bool isComputed = false,
        string? clrType = null,
        bool isNullable = false) => new()
        {
            Name = name,
            StoreType = storeType,
            IsPrimaryKey = isPrimaryKey,
            IsIdentity = isIdentity,
            IsComputed = isComputed,
            ClrType = clrType,
            IsNullable = isNullable,
        };

    [Fact]
    public void Obycejny_sloupec_se_upravit_da()
    {
        Assert.Null(RowEditing.ReadOnlyReason(Column()));
        Assert.True(RowEditing.IsEditable(Column()));
    }

    [Fact]
    public void Primarni_klic_je_identita_radku_ne_hodnota()
    {
        var duvod = RowEditing.ReadOnlyReason(Column("Id", isPrimaryKey: true));

        Assert.Equal("je součástí primárního klíče", duvod);
    }

    [Fact]
    public void Sloupec_generovany_databazi_se_neprepisuje()
    {
        Assert.Equal(
            "hodnotu generuje databáze",
            RowEditing.ReadOnlyReason(Column("Poradi", isIdentity: true)));
    }

    [Fact]
    public void Pocitany_sloupec_se_neprepisuje()
    {
        Assert.Equal(
            "je počítaný",
            RowEditing.ReadOnlyReason(Column("Celkem", isComputed: true)));
    }

    [Fact]
    public void Binarni_sloupec_neni_v_mrizce_videt_cely()
    {
        Assert.Equal(
            "je binární a v mřížce se zobrazuje jen jeho velikost",
            RowEditing.ReadOnlyReason(Column("Foto", "varbinary(max)")));
    }

    [Fact]
    public void Zamaskovany_sloupec_by_uzivatel_prepisoval_naslepo()
    {
        Assert.Equal(
            "je zamaskovaný",
            RowEditing.ReadOnlyReason(Column("Heslo"), ["heslo"]));
    }

    [Fact]
    public void Bez_seznamu_maskovanych_se_nemaskuje_nic()
    {
        Assert.True(RowEditing.IsEditable(Column("Heslo"), null));
        Assert.True(RowEditing.IsEditable(Column("Heslo"), []));
    }

    [Fact]
    public void Maskovani_jineho_sloupce_tenhle_neomezi()
    {
        Assert.True(RowEditing.IsEditable(Column("Email"), ["Heslo", "Token"]));
    }

    [Fact]
    public void Sloupec_je_povinny_argument()
    {
        Assert.Throws<ArgumentNullException>(() => RowEditing.ReadOnlyReason(null!));
        Assert.Throws<ArgumentNullException>(() => RowEditing.IsBinary(null!));
        Assert.Throws<ArgumentNullException>(() => RowEditing.CanIdentifyRows(null!));
    }

    [Theory]
    [InlineData("varbinary(max)", null, true)]
    [InlineData("binary", null, true)]
    [InlineData("image", null, true)]
    [InlineData("blob", null, true)]
    [InlineData("rowversion", null, true)]
    [InlineData("timestamp", null, true)]
    [InlineData("nvarchar(200)", null, false)]
    [InlineData("TEXT", "System.Byte[]", true)]
    public void Binarni_sloupec_se_pozna_z_typu(string storeType, string? clrType, bool expected)
    {
        Assert.Equal(expected, RowEditing.IsBinary(Column(storeType: storeType, clrType: clrType)));
    }

    [Theory]
    [InlineData("nvarchar(200)", "nvarchar")]
    [InlineData("decimal(18, 2)", "decimal")]
    [InlineData("  INT  ", "int")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Z_typu_zbyde_jmeno_bez_delky(string? storeType, string expected)
    {
        Assert.Equal(expected, RowEditing.StoreTypeName(storeType));
    }

    [Fact]
    public void Tabulka_s_klicem_radek_urci()
    {
        var table = Build.Table("Customers", ["Id", "Email"], primaryKey: ["Id"]);

        Assert.True(RowEditing.CanIdentifyRows(table));
    }

    [Fact]
    public void Tabulka_bez_klice_radek_neurci()
    {
        var table = Build.Table("Log", ["Zprava"]);

        Assert.False(RowEditing.CanIdentifyRows(table));
    }

    [Fact]
    public void Prazdny_klic_radek_taky_neurci()
    {
        var table = Build.Table("Log", ["Zprava"]) with { PrimaryKey = new DbPrimaryKey { Columns = [] } };

        Assert.False(RowEditing.CanIdentifyRows(table));
    }

    [Fact]
    public void Pohled_se_neupravuje_ani_s_klicem()
    {
        var table = Build.Table("OrderSummaries", ["Id"], primaryKey: ["Id"], isView: true);

        Assert.False(RowEditing.CanIdentifyRows(table));
    }

    [Fact]
    public void Zamaskovany_klic_radek_neurci()
    {
        var table = Build.Table("Customers", ["Id", "Email"], primaryKey: ["Id"]);

        Assert.False(RowEditing.CanIdentifyRows(table, ["Id"]));
    }

    [Fact]
    public void Klic_mimo_sloupce_radek_neurci()
    {
        // Drift: klíč ukazuje na sloupec, který ve schématu tabulky není.
        var table = Build.Table("Customers", ["Email"]) with
        {
            PrimaryKey = new DbPrimaryKey { Columns = ["Id"] },
        };

        Assert.False(RowEditing.CanIdentifyRows(table));
    }
}

public class NoveRadkyTests
{
    private static DbColumn Column(
        string name,
        bool isPrimaryKey = false,
        bool isIdentity = false,
        bool isComputed = false,
        string storeType = "nvarchar(50)") => new()
        {
            Name = name,
            StoreType = storeType,
            IsPrimaryKey = isPrimaryKey,
            IsIdentity = isIdentity,
            IsComputed = isComputed,
        };

    [Fact]
    public void Primarni_klic_se_u_noveho_radku_vyplnit_smi()
    {
        // U existujícího řádku je klíč jeho identita, u nového ho musí zadat uživatel —
        // jinak by do tabulky s přirozeným klíčem nešlo vložit nic.
        var klic = Column("Kod", isPrimaryKey: true);

        Assert.NotNull(RowEditing.ReadOnlyReason(klic));
        Assert.Null(RowEditing.NewRowReadOnlyReason(klic));
        Assert.True(RowEditing.IsFillable(klic));
    }

    [Fact]
    public void Generovane_pocitane_a_binarni_sloupce_se_vyplnit_nedaji()
    {
        Assert.Equal(
            "hodnotu generuje databáze",
            RowEditing.NewRowReadOnlyReason(Column("Id", isIdentity: true)));

        Assert.Equal(
            "je počítaný",
            RowEditing.NewRowReadOnlyReason(Column("Celkem", isComputed: true)));

        Assert.Equal(
            "je binární a v mřížce se zadat nedá",
            RowEditing.NewRowReadOnlyReason(Column("Data", storeType: "varbinary(max)")));
    }

    [Fact]
    public void Zamaskovany_sloupec_se_naslepo_nevyplnuje()
    {
        var sloupec = Column("PasswordHash");

        Assert.Equal("je zamaskovaný", RowEditing.NewRowReadOnlyReason(sloupec, ["PasswordHash"]));
        Assert.False(RowEditing.IsFillable(sloupec, ["passwordhash"]));
    }

    [Fact]
    public void Vkladat_jde_i_do_tabulky_bez_klice()
    {
        // INSERT žádný existující řádek neadresuje, takže primární klíč nepotřebuje.
        var log = new DbTable { Name = new DbObjectName(null, "Log"), Columns = [Column("Zprava")] };

        Assert.False(RowEditing.CanIdentifyRows(log));
        Assert.True(RowEditing.CanInsertRows(log));
    }

    [Fact]
    public void Do_pohledu_se_nevklada()
    {
        var pohled = new DbTable
        {
            Name = new DbObjectName(null, "Prehled"),
            IsView = true,
            Columns = [Column("Id", isPrimaryKey: true)],
            PrimaryKey = new DbPrimaryKey { Columns = ["Id"] },
        };

        Assert.False(RowEditing.CanInsertRows(pohled));
    }

    [Fact]
    public void Chybejici_vstupy_jsou_chyba()
    {
        Assert.Throws<ArgumentNullException>(() => RowEditing.NewRowReadOnlyReason(null!));
        Assert.Throws<ArgumentNullException>(() => RowEditing.CanInsertRows(null!));
    }
}
