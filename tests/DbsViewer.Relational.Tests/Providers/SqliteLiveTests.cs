using DbsViewer.Sqlite;
using Microsoft.Data.Sqlite;

namespace DbsViewer.Tests.Relational;

/// <summary>
/// Čtení schématu ze skutečné SQLite databáze. Databáze se vytvoří v paměti,
/// takže testy nepotřebují nic nainstalovaného a běží všude.
/// </summary>
public sealed class SqliteLiveFixture : IDisposable
{
    private const string Ddl = """
        CREATE TABLE Customers (
            Id       INTEGER NOT NULL CONSTRAINT PK_Customers PRIMARY KEY AUTOINCREMENT,
            Email    TEXT    NOT NULL,
            Nickname TEXT    NULL,
            Created  TEXT    NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

        CREATE UNIQUE INDEX UX_Customers_Email ON Customers (Email);

        CREATE TABLE Orders (
            Id         INTEGER NOT NULL PRIMARY KEY,
            Number     TEXT    NOT NULL,
            CustomerId INTEGER NOT NULL,
            Quantity   INTEGER NOT NULL,
            UnitPrice  TEXT    NOT NULL,
            Total      TEXT    GENERATED ALWAYS AS (Quantity * UnitPrice) STORED,
            CONSTRAINT FK_Orders_Customers FOREIGN KEY (CustomerId)
                REFERENCES Customers (Id) ON DELETE CASCADE
        );

        CREATE INDEX IX_Orders_Customer ON Orders (CustomerId, Number);
        CREATE INDEX IX_Orders_Partial ON Orders (Number) WHERE Number IS NOT NULL;

        CREATE TABLE OrderLines (
            OrderId    INTEGER NOT NULL,
            LineNumber INTEGER NOT NULL,
            Note       TEXT,
            PRIMARY KEY (OrderId, LineNumber),
            FOREIGN KEY (OrderId) REFERENCES Orders (Id) ON DELETE CASCADE
        );

        CREATE TABLE Tags (Id INTEGER NOT NULL PRIMARY KEY, Name TEXT NOT NULL);

        CREATE TABLE OrderTags (
            OrderId INTEGER NOT NULL,
            TagId   INTEGER NOT NULL,
            PRIMARY KEY (OrderId, TagId),
            FOREIGN KEY (OrderId) REFERENCES Orders (Id),
            FOREIGN KEY (TagId)   REFERENCES Tags (Id)
        );

        CREATE VIEW OrderSummary AS SELECT Id, Number FROM Orders;

        CREATE TABLE __EFMigrationsHistory (
            MigrationId    TEXT NOT NULL PRIMARY KEY,
            ProductVersion TEXT NOT NULL
        );

        INSERT INTO __EFMigrationsHistory VALUES ('20260101_Init', '10.0.0');
        INSERT INTO __EFMigrationsHistory VALUES ('20260201_AddTags', '10.0.0');

        INSERT INTO Customers (Email, Nickname) VALUES ('a@b.cz', NULL);
        INSERT INTO Customers (Email, Nickname) VALUES ('c@d.cz', 'céčko');
        """;

    public SqliteLiveFixture()
    {
        // Sdílené připojení drží databázi v paměti naživu po celou dobu testů.
        // Jméno musí být unikátní: xUnit spouští testovací třídy paralelně a dvě
        // databáze se stejným jménem by si navzájem přepisovaly obsah.
        Connection = new SqliteConnection(
            $"Data Source=DbsViewerTests_{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        Connection.Open();

        using var command = Connection.CreateCommand();
        command.CommandText = Ddl;
        command.ExecuteNonQuery();

        Schema = new SqliteSchemaSource(Connection)
            .ReadAsync(new SchemaReadOptions { IncludeRowCounts = true })
            .GetAwaiter()
            .GetResult();
    }

    public SqliteConnection Connection { get; }

    public DatabaseSchema Schema { get; }

    public DbTable Table(string name) =>
        Schema.FindTable(new DbObjectName(null, name))
        ?? throw new InvalidOperationException($"Tabulka {name} ve schématu není.");

    public void Dispose() => Connection.Dispose();
}

public class SqliteLiveTests(SqliteLiveFixture fixture) : IClassFixture<SqliteLiveFixture>
{
    [Fact]
    public void Hlavicka_popisuje_zdroj()
    {
        Assert.Equal(SchemaSourceKind.LiveDatabase, fixture.Schema.SourceKind);
        Assert.Equal(DbProviderKind.Sqlite, fixture.Schema.Provider);
        Assert.Equal("Microsoft.Data.Sqlite", fixture.Schema.ProviderName);
        Assert.Empty(fixture.Schema.Warnings);
    }

    [Fact]
    public void Nacetly_se_vsechny_tabulky_i_pohled()
    {
        Assert.Equal(
            ["Customers", "OrderLines", "Orders", "OrderSummary", "OrderTags", "Tags", "__EFMigrationsHistory"],
            fixture.Schema.Tables.Select(t => t.Name.Name).ToList());
    }

    [Fact]
    public void Pohled_se_pozna_a_nema_klice()
    {
        var view = fixture.Table("OrderSummary");

        Assert.True(view.IsView);
        Assert.Empty(view.Indexes);
        Assert.Empty(view.ForeignKeys);
        Assert.Equal(["Id", "Number"], view.Columns.Select(c => c.Name).ToList());
    }

    [Fact]
    public void Sloupce_maji_typ_i_nullabilitu()
    {
        var customers = fixture.Table("Customers");

        Assert.Equal("TEXT", customers.FindColumn("Email")!.StoreType);
        Assert.False(customers.FindColumn("Email")!.IsNullable);
        Assert.True(customers.FindColumn("Nickname")!.IsNullable);
    }

    [Fact]
    public void Klicovy_sloupec_neni_nullable_ani_bez_NOT_NULL()
    {
        Assert.False(fixture.Table("Orders").FindColumn("Id")!.IsNullable);
    }

    [Fact]
    public void Defaultni_hodnota_se_precte() =>
        Assert.Equal("CURRENT_TIMESTAMP", fixture.Table("Customers").FindColumn("Created")!.DefaultValueSql);

    [Fact]
    public void Slozeny_primarni_klic_zachova_poradi()
    {
        Assert.Equal(["OrderId", "LineNumber"], fixture.Table("OrderLines").PrimaryKey!.Columns.ToList());
    }

    [Fact]
    public void Generovany_sloupec_se_precte_z_DDL()
    {
        var total = fixture.Table("Orders").FindColumn("Total")!;

        Assert.True(total.IsComputed);
        Assert.Equal("Quantity * UnitPrice", total.ComputedSql);
        Assert.True(total.IsStored);
    }

    [Fact]
    public void Bezny_sloupec_neni_generovany() =>
        Assert.False(fixture.Table("Orders").FindColumn("Quantity")!.IsComputed);

    [Fact]
    public void Unikatni_index_se_precte()
    {
        var index = Assert.Single(fixture.Table("Customers").Indexes);

        Assert.Equal("UX_Customers_Email", index.Name);
        Assert.True(index.IsUnique);
        Assert.Equal(["Email"], index.Columns.ToList());
    }

    [Fact]
    public void Slozeny_index_zachova_poradi_sloupcu()
    {
        var index = fixture.Table("Orders").Indexes.Single(i => i.Name == "IX_Orders_Customer");

        Assert.Equal(["CustomerId", "Number"], index.Columns.ToList());
        Assert.False(index.IsUnique);
    }

    [Fact]
    public void Castecny_index_se_oznaci()
    {
        var index = fixture.Table("Orders").Indexes.Single(i => i.Name == "IX_Orders_Partial");

        Assert.NotNull(index.FilterSql);
    }

    [Fact]
    public void Cizi_klic_zna_cil_i_chovani_pri_mazani()
    {
        var foreignKey = Assert.Single(fixture.Table("Orders").ForeignKeys);

        Assert.Equal(["CustomerId"], foreignKey.Columns.ToList());
        Assert.Equal("Customers", foreignKey.PrincipalTable.Name);
        Assert.Equal(["Id"], foreignKey.PrincipalColumns.ToList());
        Assert.Equal(DbDeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    [Fact]
    public void Vazebni_tabulka_se_pozna_a_hrana_se_sbali()
    {
        Assert.True(fixture.Table("OrderTags").IsJoinTable);

        var relationship = Assert.Single(
            fixture.Schema.Relationships,
            r => r.Cardinality == DbCardinality.ManyToMany);

        Assert.Equal("OrderTags", relationship.ViaJoinTable!.Value.Name);
    }

    [Fact]
    public void Identifikujici_vztah_se_pozna()
    {
        var relationship = fixture.Schema.Relationships.Single(r => r.From.Name == "OrderLines");

        Assert.True(relationship.IsIdentifying);
        Assert.Equal(DbCardinality.OneToMany, relationship.Cardinality);
    }

    [Fact]
    public void Pocty_radku_se_nactou()
    {
        Assert.Equal(2, fixture.Table("Customers").RowCountEstimate);
        Assert.Equal(0, fixture.Table("Orders").RowCountEstimate);
    }

    [Fact]
    public void Migrace_se_nactou_z_historie()
    {
        Assert.Equal(
            ["20260101_Init", "20260201_AddTags"],
            fixture.Schema.Migrations.Select(m => m.Id).ToList());

        Assert.All(fixture.Schema.Migrations, m => Assert.True(m.AppliedInDatabase));
    }

    [Fact]
    public async Task Databaze_bez_historie_migraci_vraci_prazdny_seznam()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE T (Id INTEGER PRIMARY KEY)";
            await command.ExecuteNonQueryAsync();
        }

        var schema = await new SqliteSchemaSource(connection).ReadAsync(SchemaReadOptions.Default);

        Assert.Empty(schema.Migrations);
        Assert.Empty(schema.Warnings);
        Assert.Single(schema.Tables);
    }

    [Fact]
    public async Task Vypnute_pocty_radku_se_nedotazuji()
    {
        var schema = await new SqliteSchemaSource(fixture.Connection)
            .ReadAsync(new SchemaReadOptions { IncludeRowCounts = false });

        Assert.All(schema.Tables, t => Assert.Null(t.RowCountEstimate));
    }

    [Fact]
    public async Task Zdroj_nad_connection_stringem_si_pripojeni_uklidi()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dbsviewer-{Guid.NewGuid():N}.db");

        try
        {
            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE T (Id INTEGER PRIMARY KEY)";
                await command.ExecuteNonQueryAsync();
            }

            SqliteConnection.ClearAllPools();

            var source = new SqliteSchemaSource($"Data Source={path}");
            var schema = await source.ReadAsync(SchemaReadOptions.Default);

            Assert.Equal("T", schema.Tables.Single().Name.Name);
            Assert.StartsWith("SQLite (", source.DisplayName, StringComparison.Ordinal);

            // Když se připojení nezavřelo, soubor by nešel smazat.
            SqliteConnection.ClearAllPools();
            File.Delete(path);
            Assert.False(File.Exists(path));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Prazdny_connection_string_je_chyba()
    {
        Assert.Throws<ArgumentException>(() => new SqliteSchemaSource(""));
        Assert.Throws<ArgumentNullException>(() => new SqliteSchemaSource((string)null!));
        Assert.Throws<ArgumentNullException>(() =>
            new SqliteSchemaSource((System.Data.Common.DbConnection)null!));
    }

    [Fact]
    public async Task Chybejici_nastaveni_je_chyba()
    {
        var source = new SqliteSchemaSource(fixture.Connection);

        await Assert.ThrowsAsync<ArgumentNullException>(() => source.ReadAsync(null!));
    }
}
