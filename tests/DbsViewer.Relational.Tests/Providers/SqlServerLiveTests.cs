using DbsViewer.SqlServer;
using Microsoft.Data.SqlClient;

namespace DbsViewer.Tests.Relational;

/// <summary>
/// Vytvoří dočasnou databázi na lokálním SQL Serveru a zase ji uklidí.
/// Když server dostupný není, testy se přeskočí — mapování řádků pokrývají
/// testy s čtečkou v paměti, takže pokrytí na dostupnosti serveru nezávisí.
/// </summary>
public sealed class SqlServerLiveFixture : IAsyncLifetime
{
    /// <summary>
    /// Připojení k serveru, na kterém se vytvoří dočasná testovací databáze.
    /// Dá se přepsat proměnnou prostředí — na CI běží SQL Server v kontejneru
    /// s jinými přihlašovacími údaji než lokálně.
    /// </summary>
    private static string MasterConnectionString =>
        Environment.GetEnvironmentVariable("DBSVIEWER_TEST_SQLSERVER")
        ?? "Server=localhost;Database=master;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=5";

    private readonly string _databaseName = $"DbsViewerTest_{Guid.NewGuid():N}";

    private const string Ddl = """
        CREATE SCHEMA sales;
        GO

        CREATE TABLE dbo.Customers (
            Id      int           NOT NULL IDENTITY(1,1) CONSTRAINT PK_Customers PRIMARY KEY CLUSTERED,
            Email   nvarchar(256) NOT NULL,
            Note    nvarchar(max) NULL,
            Created datetime2(7)  NOT NULL CONSTRAINT DF_Customers_Created DEFAULT (GETUTCDATE())
        );

        CREATE UNIQUE INDEX UX_Customers_Email ON dbo.Customers (Email);

        EXEC sys.sp_addextendedproperty
            @name = N'MS_Description', @value = N'Zákazníci',
            @level0type = N'SCHEMA', @level0name = N'dbo',
            @level1type = N'TABLE',  @level1name = N'Customers';

        EXEC sys.sp_addextendedproperty
            @name = N'MS_Description', @value = N'Přihlašovací e-mail',
            @level0type = N'SCHEMA', @level0name = N'dbo',
            @level1type = N'TABLE',  @level1name = N'Customers',
            @level2type = N'COLUMN', @level2name = N'Email';

        CREATE TABLE sales.Orders (
            Id         int            NOT NULL IDENTITY(1,1) CONSTRAINT PK_Orders PRIMARY KEY,
            CustomerId int            NOT NULL,
            Quantity   int            NOT NULL,
            UnitPrice  decimal(18, 2) NOT NULL,
            Total      AS (Quantity * UnitPrice) PERSISTED,
            PlacedAt   datetime2(7)   NULL,
            CONSTRAINT FK_Orders_Customers FOREIGN KEY (CustomerId)
                REFERENCES dbo.Customers (Id) ON DELETE CASCADE,
            CONSTRAINT CK_Orders_Quantity CHECK (Quantity > 0)
        );

        CREATE INDEX IX_Orders_PlacedAt ON sales.Orders (PlacedAt DESC)
            INCLUDE (Quantity)
            WHERE PlacedAt IS NOT NULL;

        CREATE TABLE dbo.OrderLines (
            OrderId    int NOT NULL,
            LineNumber int NOT NULL,
            CONSTRAINT PK_OrderLines PRIMARY KEY (OrderId, LineNumber),
            CONSTRAINT FK_OrderLines_Orders FOREIGN KEY (OrderId) REFERENCES sales.Orders (Id)
        );

        CREATE TABLE dbo.Tags (Id int NOT NULL IDENTITY(1,1) CONSTRAINT PK_Tags PRIMARY KEY, Name nvarchar(60) NOT NULL);

        CREATE TABLE dbo.OrderTags (
            OrderId int NOT NULL,
            TagId   int NOT NULL,
            CONSTRAINT PK_OrderTags PRIMARY KEY (OrderId, TagId),
            CONSTRAINT FK_OrderTags_Orders FOREIGN KEY (OrderId) REFERENCES sales.Orders (Id),
            CONSTRAINT FK_OrderTags_Tags   FOREIGN KEY (TagId)   REFERENCES dbo.Tags (Id)
        );

        GO

        CREATE VIEW dbo.CustomerEmails AS SELECT Id, Email FROM dbo.Customers;
        GO

        CREATE TABLE dbo.__EFMigrationsHistory (
            MigrationId    nvarchar(150) NOT NULL CONSTRAINT PK___EFMigrationsHistory PRIMARY KEY,
            ProductVersion nvarchar(32)  NOT NULL
        );

        INSERT INTO dbo.__EFMigrationsHistory VALUES (N'20260101_Init', N'10.0.0');

        INSERT INTO dbo.Customers (Email) VALUES (N'a@b.cz'), (N'c@d.cz'), (N'e@f.cz');
        """;

    public bool IsAvailable { get; private set; }

    public DatabaseSchema Schema { get; private set; } = new();

    public string ConnectionString =>
        new SqlConnectionStringBuilder(MasterConnectionString)
        {
            InitialCatalog = _databaseName,
        }.ConnectionString;

    public async Task InitializeAsync()
    {
        try
        {
            await ExecuteOnMasterAsync($"CREATE DATABASE [{_databaseName}]");
        }
        catch (Exception)
        {
            // Server není dostupný nebo chybí oprávnění — testy se přeskočí.
            IsAvailable = false;
            return;
        }

        IsAvailable = true;

        await using (var connection = new SqlConnection(ConnectionString))
        {
            await connection.OpenAsync();

            foreach (var batch in SplitBatches(Ddl))
            {
                await using var command = connection.CreateCommand();
                command.CommandText = batch;
                await command.ExecuteNonQueryAsync();
            }
        }

        Schema = await new SqlServerSchemaSource(ConnectionString)
            .ReadAsync(new SchemaReadOptions { IncludeRowCounts = true });
    }

    public async Task DisposeAsync()
    {
        if (!IsAvailable)
        {
            return;
        }

        SqlConnection.ClearAllPools();

        try
        {
            await ExecuteOnMasterAsync(
                $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; "
                + $"DROP DATABASE [{_databaseName}]");
        }
        catch (Exception)
        {
            // Úklid se nepovedl — dočasná databáze zůstane, ale test kvůli tomu selhat nemá.
        }
    }

    public DbTable Table(string schema, string name) =>
        Schema.FindTable(new DbObjectName(schema, name))
        ?? throw new InvalidOperationException($"Tabulka {schema}.{name} ve schématu není.");

    /// <summary>
    /// Rozdělí skript na dávky podle <c>GO</c>. SQL Server vyžaduje, aby některé příkazy
    /// (<c>CREATE SCHEMA</c>, <c>CREATE VIEW</c>) byly v dávce první.
    /// </summary>
    private static IEnumerable<string> SplitBatches(string script) =>
        script
            .Split(Environment.NewLine + "GO", StringSplitOptions.RemoveEmptyEntries)
            .Select(static batch => batch.Trim())
            .Where(static batch => batch.Length > 0);

    private static async Task ExecuteOnMasterAsync(string sql)
    {
        await using var connection = new SqlConnection(MasterConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>
/// Ověření, že introspekční dotazy nad <c>sys.*</c> skutečně vracejí, co mají.
/// </summary>
[Trait("Kategorie", "Integrační")]
public class SqlServerLiveTests(SqlServerLiveFixture fixture) : IClassFixture<SqlServerLiveFixture>
{
    /// <summary>Přeskočí test, když lokální SQL Server není k dispozici.</summary>
    private bool Skip => !fixture.IsAvailable;

    private const string Reason = "Lokální SQL Server není dostupný.";

    [SkippableFact]
    public void Hlavicka_popisuje_zdroj()
    {
        if (Skipper.SkipUnavailable(Skip, Reason))
        {
            return;
        }

        Assert.Equal(SchemaSourceKind.LiveDatabase, fixture.Schema.SourceKind);
        Assert.Equal(DbProviderKind.SqlServer, fixture.Schema.Provider);
        Assert.Equal("dbo", fixture.Schema.DefaultSchema);
        Assert.Empty(fixture.Schema.Warnings);
    }

    [SkippableFact]
    public void Tabulky_z_vice_schemat_se_nactou()
    {
        if (Skipper.SkipUnavailable(Skip, Reason))
        {
            return;
        }

        var names = fixture.Schema.Tables.Select(t => t.Qualified).ToList();

        Assert.Contains("dbo.Customers", names);
        Assert.Contains("sales.Orders", names);
        Assert.Contains("dbo.CustomerEmails", names);
    }

    [SkippableFact]
    public void Komentare_tabulky_i_sloupce_se_prectou()
    {
        if (Skipper.SkipUnavailable(Skip, Reason))
        {
            return;
        }

        var customers = fixture.Table("dbo", "Customers");

        Assert.Equal("Zákazníci", customers.Comment);
        Assert.Equal("Přihlašovací e-mail", customers.FindColumn("Email")!.Comment);
    }

    [SkippableFact]
    public void Typy_sloupcu_nesou_delku_i_presnost()
    {
        if (Skipper.SkipUnavailable(Skip, Reason))
        {
            return;
        }

        var customers = fixture.Table("dbo", "Customers");
        var orders = fixture.Table("sales", "Orders");

        Assert.Equal("nvarchar(256)", customers.FindColumn("Email")!.StoreType);
        Assert.Equal(256, customers.FindColumn("Email")!.MaxLength);
        Assert.Equal("nvarchar(max)", customers.FindColumn("Note")!.StoreType);
        Assert.Equal("decimal(18,2)", orders.FindColumn("UnitPrice")!.StoreType);
        Assert.Equal(18, orders.FindColumn("UnitPrice")!.Precision);
        Assert.Equal(2, orders.FindColumn("UnitPrice")!.Scale);
    }

    [SkippableFact]
    public void Identity_a_default_se_prectou()
    {
        if (Skipper.SkipUnavailable(Skip, Reason))
        {
            return;
        }

        var customers = fixture.Table("dbo", "Customers");

        Assert.True(customers.FindColumn("Id")!.IsIdentity);
        Assert.False(customers.FindColumn("Email")!.IsIdentity);
        Assert.Contains("getutcdate", customers.FindColumn("Created")!.DefaultValueSql!,
            StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public void Pocitany_sloupec_nese_vyraz()
    {
        if (Skipper.SkipUnavailable(Skip, Reason))
        {
            return;
        }

        var total = fixture.Table("sales", "Orders").FindColumn("Total")!;

        Assert.True(total.IsComputed);
        Assert.True(total.IsStored);
        Assert.Contains("Quantity", total.ComputedSql!, StringComparison.Ordinal);
    }

    [SkippableFact]
    public void Klic_nese_jmeno_i_priznak_clustered()
    {
        if (Skipper.SkipUnavailable(Skip, Reason))
        {
            return;
        }

        var primaryKey = fixture.Table("dbo", "Customers").PrimaryKey!;

        Assert.Equal("PK_Customers", primaryKey.Name);
        Assert.True(primaryKey.IsClustered);
        Assert.Equal(["Id"], primaryKey.Columns.ToList());
    }

    [SkippableFact]
    public void Slozeny_klic_zachova_poradi()
    {
        if (Skipper.SkipUnavailable(Skip, Reason))
        {
            return;
        }

        Assert.Equal(
            ["OrderId", "LineNumber"],
            fixture.Table("dbo", "OrderLines").PrimaryKey!.Columns.ToList());
    }

    [SkippableFact]
    public void Filtrovany_index_nese_INCLUDE_i_smer_razeni()
    {
        if (Skipper.SkipUnavailable(Skip, Reason))
        {
            return;
        }

        var index = fixture.Table("sales", "Orders").Indexes.Single(i => i.Name == "IX_Orders_PlacedAt");

        Assert.Equal(["PlacedAt"], index.Columns.ToList());
        Assert.Equal(["Quantity"], index.IncludedColumns.ToList());
        Assert.Equal([true], index.IsDescending.ToList());
        Assert.NotNull(index.FilterSql);
        Assert.False(index.IsClustered);
    }

    [SkippableFact]
    public void Cizi_klic_pres_schemata_zna_cil()
    {
        if (Skipper.SkipUnavailable(Skip, Reason))
        {
            return;
        }

        var foreignKey = Assert.Single(fixture.Table("sales", "Orders").ForeignKeys);

        Assert.Equal("FK_Orders_Customers", foreignKey.Name);
        Assert.Equal("dbo.Customers", foreignKey.PrincipalTable.Qualified);
        Assert.Equal(DbDeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    [SkippableFact]
    public void Check_constraint_se_precte()
    {
        if (Skipper.SkipUnavailable(Skip, Reason))
        {
            return;
        }

        var check = Assert.Single(fixture.Table("sales", "Orders").CheckConstraints);

        Assert.Equal("CK_Orders_Quantity", check.Name);
        Assert.Contains("Quantity", check.Sql!, StringComparison.Ordinal);
    }

    [SkippableFact]
    public void Pohled_se_pozna()
    {
        if (Skipper.SkipUnavailable(Skip, Reason))
        {
            return;
        }

        var view = fixture.Table("dbo", "CustomerEmails");

        Assert.True(view.IsView);
        Assert.Null(view.PrimaryKey);
        Assert.Empty(view.ForeignKeys);
    }

    [SkippableFact]
    public void Odhad_poctu_radku_se_precte_ze_statistik()
    {
        if (Skipper.SkipUnavailable(Skip, Reason))
        {
            return;
        }

        Assert.Equal(3, fixture.Table("dbo", "Customers").RowCountEstimate);
    }

    [SkippableFact]
    public void Vazebni_tabulka_se_pozna_a_hrana_se_sbali()
    {
        if (Skipper.SkipUnavailable(Skip, Reason))
        {
            return;
        }

        Assert.True(fixture.Table("dbo", "OrderTags").IsJoinTable);

        var relationship = Assert.Single(
            fixture.Schema.Relationships,
            r => r.Cardinality == DbCardinality.ManyToMany);

        Assert.Equal("dbo.OrderTags", relationship.ViaJoinTable!.Value.Qualified);
    }

    [SkippableFact]
    public void Migrace_se_nactou_z_historie()
    {
        if (Skipper.SkipUnavailable(Skip, Reason))
        {
            return;
        }

        var migration = Assert.Single(fixture.Schema.Migrations);

        Assert.Equal("20260101_Init", migration.Id);
        Assert.True(migration.AppliedInDatabase);
    }

    [SkippableFact]
    public async Task Zdroj_nad_cizim_pripojenim_ho_nezavira()
    {
        if (Skipper.SkipUnavailable(Skip, Reason))
        {
            return;
        }

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        var source = new SqlServerSchemaSource(connection);
        var schema = await source.ReadAsync(SchemaReadOptions.Default);

        Assert.NotEmpty(schema.Tables);
        Assert.StartsWith("SQL Server (", source.DisplayName, StringComparison.Ordinal);

        // Cizí připojení patří volajícímu — zdroj ho zavřít nesmí.
        Assert.Equal(System.Data.ConnectionState.Open, connection.State);
    }

    [SkippableFact]
    public async Task Poskozena_historie_migraci_jen_upozorni()
    {
        if (Skipper.SkipUnavailable(Skip, Reason))
        {
            return;
        }

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using (var command = connection.CreateCommand())
        {
            // Tabulka existuje, ale nemá očekávaný sloupec — dotaz na MigrationId selže.
            command.CommandText = """
                DROP TABLE dbo.__EFMigrationsHistory;
                CREATE TABLE dbo.__EFMigrationsHistory (NecoJineho nvarchar(50) NOT NULL);
                """;
            await command.ExecuteNonQueryAsync();
        }

        try
        {
            var schema = await new SqlServerSchemaSource(connection).ReadAsync(SchemaReadOptions.Default);

            Assert.Contains(schema.Warnings, w => w.Contains("Historii migrací", StringComparison.Ordinal));
            Assert.Empty(schema.Migrations);
            Assert.NotEmpty(schema.Tables);
        }
        finally
        {
            await using var restore = connection.CreateCommand();
            restore.CommandText = """
                DROP TABLE dbo.__EFMigrationsHistory;
                CREATE TABLE dbo.__EFMigrationsHistory (
                    MigrationId    nvarchar(150) NOT NULL PRIMARY KEY,
                    ProductVersion nvarchar(32)  NOT NULL
                );
                INSERT INTO dbo.__EFMigrationsHistory VALUES (N'20260101_Init', N'10.0.0');
                """;
            await restore.ExecuteNonQueryAsync();
        }
    }

    [SkippableFact]
    public async Task Databaze_bez_historie_migraci_vraci_prazdny_seznam()
    {
        if (Skipper.SkipUnavailable(Skip, Reason))
        {
            return;
        }

        // Databáze spravovaná jinak než přes EF historii migrací nemá — a to je normální stav.
        var master = new SqlServerSchemaSource(
            new SqlConnectionStringBuilder(fixture.ConnectionString) { InitialCatalog = "master" }.ConnectionString);

        var schema = await master.ReadAsync(SchemaReadOptions.Default);

        Assert.Empty(schema.Migrations);
        Assert.Empty(schema.Warnings);
    }

    [SkippableFact]
    public async Task Filtrovani_podle_schematu_funguje()
    {
        if (Skipper.SkipUnavailable(Skip, Reason))
        {
            return;
        }

        var schema = await new SqlServerSchemaSource(fixture.ConnectionString)
            .ReadAsync(new SchemaReadOptions { IncludeSchemas = ["sales"] });

        Assert.Equal("sales.Orders", schema.Tables.Single().Qualified);
    }
}

/// <summary>Označení testu, který se smí přeskočit, když prostředí není k dispozici.</summary>
public sealed class SkippableFactAttribute : FactAttribute;

/// <summary>
/// Přeskočení testu, když prostředí není k dispozici. xUnit 2 přeskočení za běhu neumí,
/// takže se test ukončí bez tvrzení — a fakt, že běžel naprázdno, hlásí zpráva v konzoli.
/// </summary>
internal static class Skipper
{
    public static bool SkipUnavailable(bool unavailable, string reason)
    {
        if (unavailable)
        {
            Console.WriteLine($"PŘESKOČENO: {reason}");
        }

        return unavailable;
    }
}
