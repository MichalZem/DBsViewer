using DbsViewer.EfCore;
using DbsViewer.EfCore.Internal;
using DbsViewer.SampleShop;
using Microsoft.EntityFrameworkCore;
using DbsViewer.TestKit;

namespace DbsViewer.Tests.EfCore;

public class SchemaSourceTests
{
    private sealed class FakeMigrations : IMigrationsReader
    {
        public string[] InAssembly { get; init; } = [];

        public string[] Applied { get; init; } = [];

        public Exception? AssemblyFailure { get; init; }

        public Exception? DatabaseFailure { get; init; }

        public IEnumerable<string> GetInAssembly() =>
            AssemblyFailure is null ? InAssembly : throw AssemblyFailure;

        public Task<IEnumerable<string>> GetAppliedAsync(CancellationToken cancellationToken) =>
            DatabaseFailure is null
                ? Task.FromResult<IEnumerable<string>>(Applied)
                : Task.FromException<IEnumerable<string>>(DatabaseFailure);
    }

    private static async Task<DatabaseSchema> ReadAsync(IMigrationsReader migrations)
    {
        await using var context = ShopContextFactory.CreateSqlite();
        return await new EfCoreModelSchemaSource(context, migrations)
            .ReadAsync(new SchemaReadOptions { IncludeMigrations = true });
    }

    [Fact]
    public void Zdroj_ma_vychozi_klic_a_popisek()
    {
        using var context = ShopContextFactory.CreateSqlite();
        var source = new EfCoreModelSchemaSource(context);

        Assert.Equal(ISchemaSource.DefaultKey, source.Key);
        Assert.Equal("default", source.Key);
        Assert.Equal("EF model (ShopContext)", source.DisplayName);
        Assert.Equal(SchemaSourceKind.EfModel, source.Kind);
    }

    [Fact]
    public void Zdroj_prijme_vlastni_klic()
    {
        using var context = ShopContextFactory.CreateSqlite();

        Assert.Equal("reporting", new EfCoreModelSchemaSource(context, "reporting").Key);
    }

    [Fact]
    public void Chybejici_kontext_je_chyba() =>
        Assert.Throws<ArgumentNullException>(() => new EfCoreModelSchemaSource(null!));

    [Fact]
    public void Chybejici_ctecka_migraci_je_chyba()
    {
        using var context = ShopContextFactory.CreateSqlite();

        Assert.Throws<ArgumentNullException>(() => new EfCoreModelSchemaSource(context, null!, "x"));
    }

    [Fact]
    public async Task Chybejici_nastaveni_je_chyba()
    {
        await using var context = ShopContextFactory.CreateSqlite();
        var source = new EfCoreModelSchemaSource(context);

        await Assert.ThrowsAsync<ArgumentNullException>(() => source.ReadAsync(null!));
    }

    [Fact]
    public async Task Migrace_se_rozdeli_na_nasazene_cekajici_a_osirele()
    {
        var schema = await ReadAsync(new FakeMigrations
        {
            InAssembly = ["20260101_Init", "20260201_AddOrders", "20260301_AddTags"],
            Applied = ["20260101_Init", "20260201_AddOrders", "20259901_Legacy"],
        });

        Assert.Empty(schema.Warnings);
        Seq.Equal(
            ["20259901_Legacy", "20260101_Init", "20260201_AddOrders", "20260301_AddTags"],
            schema.Migrations.Select(m => m.Id));

        Assert.Equal("20260301_AddTags", schema.Migrations.Single(m => m.IsPending).Id);
        Assert.Equal("20259901_Legacy", schema.Migrations.Single(m => m.IsOrphaned).Id);
        Assert.Equal(2, schema.Migrations.Count(m => !m.IsPending && !m.IsOrphaned));
    }

    [Fact]
    public async Task Nedostupna_databaze_migrace_neshodi_jen_upozorni()
    {
        var schema = await ReadAsync(new FakeMigrations
        {
            InAssembly = ["20260101_Init"],
            DatabaseFailure = new InvalidOperationException("databáze neexistuje"),
        });

        var warning = Assert.Single(schema.Warnings);
        Assert.Contains("databáze neexistuje", warning, StringComparison.Ordinal);
        Assert.True(schema.Migrations.Single().IsPending);
        Assert.NotEmpty(schema.Tables);
    }

    [Fact]
    public async Task Nedostupny_seznam_migraci_v_assembly_jen_upozorni()
    {
        var schema = await ReadAsync(new FakeMigrations
        {
            Applied = ["20260101_Init"],
            AssemblyFailure = new InvalidOperationException("chybí migrations assembly"),
        });

        var warning = Assert.Single(schema.Warnings);
        Assert.Contains("chybí migrations assembly", warning, StringComparison.Ordinal);
        Assert.True(schema.Migrations.Single().IsOrphaned);
    }

    [Fact]
    public async Task Selhani_obou_zdroju_migraci_da_dve_upozorneni()
    {
        var schema = await ReadAsync(new FakeMigrations
        {
            AssemblyFailure = new InvalidOperationException("assembly"),
            DatabaseFailure = new InvalidOperationException("databáze"),
        });

        Assert.Equal(2, schema.Warnings.Count);
        Assert.Empty(schema.Migrations);
    }

    [Fact]
    public async Task Vypnute_migrace_se_vubec_neptaji()
    {
        var failing = new FakeMigrations
        {
            AssemblyFailure = new InvalidOperationException("nemá se volat"),
            DatabaseFailure = new InvalidOperationException("nemá se volat"),
        };

        await using var context = ShopContextFactory.CreateSqlite();
        var schema = await new EfCoreModelSchemaSource(context, failing)
            .ReadAsync(new SchemaReadOptions { IncludeMigrations = false });

        Assert.Empty(schema.Migrations);
        Assert.Empty(schema.Warnings);
    }

    [Fact]
    public async Task Vychozi_ctecka_migraci_cte_z_kontextu()
    {
        // Ukázkový kontext žádné migrace nemá a databáze neexistuje —
        // obojí musí skončit prázdným seznamem a upozorněním, ne výjimkou.
        await using var context = ShopContextFactory.CreateSqlite("nonexistent-directory/db.sqlite");
        var schema = await new EfCoreModelSchemaSource(context)
            .ReadAsync(new SchemaReadOptions { IncludeMigrations = true });

        Assert.Empty(schema.Migrations);
        Assert.NotEmpty(schema.Tables);
    }

    [Fact]
    public async Task Nezjistitelne_jmeno_databaze_jen_upozorni()
    {
        // Neznámý parametr connection stringu shodí až vytvoření spojení,
        // stavbu modelu ale neovlivní.
        var options = new DbContextOptionsBuilder<ShopContext>()
            .UseSqlite("Data Source=:memory:;NeznamyParametr=1")
            .Options;

        await using var context = new ShopContext(options);
        var schema = await new EfCoreModelSchemaSource(context)
            .ReadAsync(new SchemaReadOptions { IncludeMigrations = false });

        Assert.Null(schema.DatabaseName);
        Assert.Contains(schema.Warnings, w => w.StartsWith("Jméno databáze", StringComparison.Ordinal));
        Assert.NotEmpty(schema.Tables);
    }

    [Fact]
    public async Task Zruseni_operace_se_propaguje()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var migrations = new FakeMigrations
        {
            DatabaseFailure = new OperationCanceledException(cancellation.Token),
        };

        await using var context = ShopContextFactory.CreateSqlite();
        var schema = await new EfCoreModelSchemaSource(context, migrations)
            .ReadAsync(new SchemaReadOptions { IncludeMigrations = true }, cancellation.Token);

        // Zrušení dotazu na migrace nesmí zahodit celé schéma.
        Assert.Single(schema.Warnings);
        Assert.NotEmpty(schema.Tables);
    }
}
