using System.Data.Common;
using DbsViewer.Relational;
using DbsViewer.SqlServer;
using DbsViewer.TestKit;

namespace DbsViewer.Tests.Relational;

/// <summary>
/// Mapování řádků SQL Serveru. Testuje se přes čtečku v paměti, takže testy běží
/// i tam, kde SQL Server nainstalovaný není.
/// </summary>
public class SqlServerMappingTests
{
    [Fact]
    public void Tabulka_se_namapuje()
    {
        using var reader = new FakeDataReader(["dbo", "Orders", false, "Objednávky"]);
        Assert.True(reader.Read());

        var table = SqlServerRawReader.MapTable(reader);

        Assert.Equal("dbo", table.Schema);
        Assert.Equal("Orders", table.Name);
        Assert.False(table.IsView);
        Assert.Equal("Objednávky", table.Comment);
    }

    [Fact]
    public void Pohled_se_namapuje_bez_komentare()
    {
        using var reader = new FakeDataReader(["dbo", "V", true, null]);
        reader.Read();

        var view = SqlServerRawReader.MapTable(reader);

        Assert.True(view.IsView);
        Assert.Null(view.Comment);
    }

    [Fact]
    public void Sloupec_se_namapuje_se_vsemi_udaji()
    {
        using var reader = new FakeDataReader(
        [
            "dbo", "Orders", "Total", 3, "decimal(18,2)", true, false, true,
            "[Quantity]*[Price]", true, "((0))", null, 18, 2, "Czech_CI_AS", "Celková částka",
        ]);
        reader.Read();

        var column = SqlServerRawReader.MapColumn(reader);

        Assert.Equal("Total", column.Name);
        Assert.Equal(3, column.Ordinal);
        Assert.Equal("decimal(18,2)", column.StoreType);
        Assert.True(column.IsNullable);
        Assert.False(column.IsIdentity);
        Assert.True(column.IsComputed);
        Assert.Equal("[Quantity]*[Price]", column.ComputedSql);
        Assert.True(column.IsStored);
        Assert.Equal("((0))", column.DefaultValueSql);
        Assert.Null(column.MaxLength);
        Assert.Equal(18, column.Precision);
        Assert.Equal(2, column.Scale);
        Assert.Equal("Czech_CI_AS", column.Collation);
        Assert.Equal("Celková částka", column.Comment);
    }

    [Fact]
    public void Sloupec_s_prazdnymi_hodnotami_se_namapuje()
    {
        using var reader = new FakeDataReader(
            ["dbo", "T", "Id", 1, "int", false, true, false, null, null, null, null, null, null, null, null]);
        reader.Read();

        var column = SqlServerRawReader.MapColumn(reader);

        Assert.True(column.IsIdentity);
        Assert.Null(column.ComputedSql);
        Assert.Null(column.IsStored);
        Assert.Null(column.Collation);
        Assert.Null(column.Comment);
    }

    [Fact]
    public void Sloupec_klice_se_namapuje()
    {
        using var reader = new FakeDataReader(["dbo", "T", "PK_T", "Id", 1, true]);
        reader.Read();

        var key = SqlServerRawReader.MapKeyColumn(reader);

        Assert.Equal("PK_T", key.ConstraintName);
        Assert.Equal("Id", key.Column);
        Assert.Equal(1, key.Position);
        Assert.True(key.IsClustered);
    }

    [Fact]
    public void Index_se_namapuje()
    {
        using var reader = new FakeDataReader(["dbo", "T", "IX_T", true, false, "[A] IS NOT NULL"]);
        reader.Read();

        var index = SqlServerRawReader.MapIndex(reader);

        Assert.Equal("IX_T", index.Name);
        Assert.True(index.IsUnique);
        Assert.False(index.IsClustered);
        Assert.Equal("[A] IS NOT NULL", index.FilterSql);
    }

    [Fact]
    public void Sloupec_indexu_se_namapuje()
    {
        using var reader = new FakeDataReader(["dbo", "T", "IX_T", "A", 2, true, false]);
        reader.Read();

        var column = SqlServerRawReader.MapIndexColumn(reader);

        Assert.Equal("IX_T", column.IndexName);
        Assert.Equal("A", column.Column);
        Assert.Equal(2, column.Position);
        Assert.True(column.IsDescending);
        Assert.False(column.IsIncluded);
    }

    [Fact]
    public void Cizi_klic_se_namapuje()
    {
        using var reader = new FakeDataReader(["dbo", "Orders", "FK", "dbo", "Customers", "CASCADE"]);
        reader.Read();

        var foreignKey = SqlServerRawReader.MapForeignKey(reader);

        Assert.Equal("FK", foreignKey.Name);
        Assert.Equal("dbo", foreignKey.PrincipalSchema);
        Assert.Equal("Customers", foreignKey.PrincipalTable);
        Assert.Equal("CASCADE", foreignKey.DeleteAction);
    }

    [Fact]
    public void Sloupec_ciziho_klice_se_namapuje()
    {
        using var reader = new FakeDataReader(["dbo", "Orders", "FK", "CustomerId", "Id", 1]);
        reader.Read();

        var column = SqlServerRawReader.MapForeignKeyColumn(reader);

        Assert.Equal("CustomerId", column.Column);
        Assert.Equal("Id", column.PrincipalColumn);
        Assert.Equal(1, column.Position);
    }

    [Fact]
    public void Check_constraint_se_namapuje()
    {
        using var reader = new FakeDataReader(["dbo", "T", "CK_T", "([A]>(0))"]);
        reader.Read();

        var check = SqlServerRawReader.MapCheck(reader);

        Assert.Equal("CK_T", check.Name);
        Assert.Equal("([A]>(0))", check.Sql);
    }

    [Fact]
    public void Pocet_radku_se_namapuje()
    {
        using var reader = new FakeDataReader(["dbo", "Orders", 12345L]);
        reader.Read();

        Assert.Equal(12345L, SqlServerRawReader.MapRowCount(reader).Rows);
    }

    [Fact]
    public void Connection_string_bez_databaze_je_chyba()
    {
        Assert.Throws<ArgumentException>(() => new SqlServerSchemaSource(""));
        Assert.Throws<ArgumentException>(() => new SqlServerSchemaSource("   "));
        Assert.Throws<ArgumentNullException>(() => new SqlServerSchemaSource((string)null!));
    }

    [Fact]
    public void Chybejici_pripojeni_je_chyba() =>
        Assert.Throws<ArgumentNullException>(() =>
            new SqlServerSchemaSource((System.Data.Common.DbConnection)null!));

    [Fact]
    public void Zdroj_zna_svuj_klic_i_popisek()
    {
        var source = new SqlServerSchemaSource("Server=.;Database=Shop;Trusted_Connection=True;", "reporting");

        Assert.Equal("reporting", source.Key);
        Assert.Equal("SQL Server (Shop)", source.DisplayName);
        Assert.Equal(SchemaSourceKind.LiveDatabase, source.Kind);
    }

    [Fact]
    public void Vychozi_klic_se_pouzije_kdyz_neni_zadany() =>
        Assert.Equal(
            ISchemaSource.DefaultKey,
            new SqlServerSchemaSource("Server=.;Database=Shop;").Key);
}

/// <summary>Čtení hodnot z čtečky, včetně převodů typů a NULL.</summary>
public class DataReaderExtensionTests
{
    private static DbDataReader Row(params object?[] values)
    {
        var reader = new FakeDataReader(values);
        reader.Read();
        return reader;
    }

    [Fact]
    public void Text_se_precte_i_z_necetzcove_hodnoty()
    {
        using var reader = Row("text", 42, null);

        Assert.Equal("text", reader.GetText(0));
        Assert.Equal("42", reader.GetText(1));
        Assert.Null(reader.GetTextOrNull(2));
        Assert.Equal("text", reader.GetTextOrNull(0));
    }

    [Fact]
    public void Logicka_hodnota_se_precte_i_z_cisla()
    {
        using var reader = Row(true, false, 1, 0, null);

        Assert.True(reader.GetBool(0));
        Assert.False(reader.GetBool(1));
        Assert.True(reader.GetBool(2));
        Assert.False(reader.GetBool(3));
        Assert.False(reader.GetBool(4));
    }

    [Fact]
    public void Volitelna_logicka_hodnota_rozlisi_NULL()
    {
        using var reader = Row(true, null);

        Assert.True(reader.GetBoolOrNull(0));
        Assert.Null(reader.GetBoolOrNull(1));
    }

    [Fact]
    public void Cela_cisla_se_prectou_i_z_jinych_typu()
    {
        using var reader = Row(42, 42L, (short)42, null);

        Assert.Equal(42, reader.GetInt(0));
        Assert.Equal(42, reader.GetInt(1));
        Assert.Equal(42, reader.GetInt(2));
        Assert.Equal(0, reader.GetInt(3));

        Assert.Equal(42, reader.GetIntOrNull(0));
        Assert.Null(reader.GetIntOrNull(3));

        Assert.Equal(42L, reader.GetLong(0));
        Assert.Equal(0L, reader.GetLong(3));
    }
}
