using DbsViewer.EfCore;
using DbsViewer.TestKit;

namespace DbsViewer.Tests.EfCore;

/// <summary>
/// Konstrukce, které ukázkový e-shop nemá: vlastní schéma, tabulka bez klíče,
/// neceločíselné klíče, filtrovaný index s <c>INCLUDE</c>, ručně namodelovaná vazební
/// tabulka a entita bez mapování na tabulku.
/// </summary>
public class EdgeCaseSchemaTests
{
    private static async Task<DatabaseSchema> ReadSqlServerAsync(SchemaReadOptions? options = null)
    {
        await using var context = EdgeCaseContextFactory.CreateSqlServer();
        return await new EfCoreModelSchemaSource(context)
            .ReadAsync(options ?? new SchemaReadOptions { IncludeMigrations = false });
    }

    private static DbTable Table(DatabaseSchema schema, string? schemaName, string name) =>
        schema.FindTable(new DbObjectName(schemaName, name))
        ?? throw new InvalidOperationException($"Tabulka {name} ve schématu není.");

    [Fact]
    public async Task Provider_SqlServer_se_rozpozna()
    {
        var schema = await ReadSqlServerAsync();

        Assert.Equal(DbProviderKind.SqlServer, schema.Provider);
        Assert.Equal("DbsViewerTests", schema.DatabaseName);

        // DefaultSchema nese jen to, co model explicitně nastavil přes HasDefaultSchema().
        // Providerový default (u SQL Serveru dbo) v modelu není a dopočítá se až v databázi.
        Assert.Null(schema.DefaultSchema);
    }

    [Fact]
    public async Task Tabulka_ve_vlastnim_schematu_si_ho_nese()
    {
        var schema = await ReadSqlServerAsync();
        var ledgers = Table(schema, "audit", "Ledgers");

        Assert.Equal("audit", ledgers.Name.Schema);
        Assert.Equal("audit.Ledgers", ledgers.Qualified);
    }

    [Fact]
    public async Task Guid_klic_se_neoznaci_jako_identity()
    {
        var schema = await ReadSqlServerAsync();
        var id = Table(schema, "audit", "Ledgers").FindColumn("Id")!;

        Assert.True(id.IsPrimaryKey);
        Assert.False(id.IsIdentity);
    }

    [Fact]
    public async Task Textovy_klic_se_neoznaci_jako_identity()
    {
        var schema = await ReadSqlServerAsync();
        var code = Table(schema, null, "Snapshots").FindColumn("Code")!;

        Assert.True(code.IsPrimaryKey);
        Assert.False(code.IsIdentity);
        Assert.Equal(40, code.MaxLength);
    }

    [Fact]
    public async Task ValueGeneratedNever_potlaci_identity()
    {
        var schema = await ReadSqlServerAsync();
        var id = Table(schema, "legacy", "Legacy").FindColumn("Id")!;

        Assert.False(id.IsIdentity);
        Assert.Equal(DbValueGenerated.Never, id.ValueGenerated);
    }

    [Fact]
    public async Task Celociselny_klic_na_SqlServeru_je_identity()
    {
        var schema = await ReadSqlServerAsync();

        Assert.True(Table(schema, null, "Teams").FindColumn("Id")!.IsIdentity);
    }

    [Fact]
    public async Task Collation_sloupce_se_precte()
    {
        var schema = await ReadSqlServerAsync();

        Assert.Equal("Czech_CI_AS", Table(schema, null, "Snapshots").FindColumn("Note")!.Collation);
        Assert.Null(Table(schema, null, "Snapshots").FindColumn("Code")!.Collation);
    }

    [Fact]
    public async Task Filtrovany_index_nese_podminku_include_i_smer()
    {
        var schema = await ReadSqlServerAsync();
        var index = Assert.Single(Table(schema, "audit", "Ledgers").Indexes);

        Assert.Equal("IX_Ledgers_PostedOn", index.Name);
        Assert.Equal("[PostedOn] IS NOT NULL", index.FilterSql);
        Seq.Equal(["Amount"], index.IncludedColumns);
        Seq.Equal([true], index.IsDescending);
        Assert.False(index.IsClustered);
    }

    [Fact]
    public async Task Tabulka_bez_klice_nema_primarni_klic()
    {
        var schema = await ReadSqlServerAsync();
        var log = Table(schema, null, "AuditLogs");

        Assert.Null(log.PrimaryKey);
        Assert.True(log.IsExcludedFromMigrations);
        Assert.All(log.Columns, c => Assert.False(c.IsPrimaryKey));
    }

    [Fact]
    public async Task Rucne_namodelovana_vazebni_tabulka_se_pozna_heuristikou()
    {
        var schema = await ReadSqlServerAsync();

        Assert.True(Table(schema, null, "TeamMembers").IsJoinTable);
    }

    [Fact]
    public async Task Vazebni_tabulka_s_vlastnimi_daty_se_neoznaci()
    {
        var schema = await ReadSqlServerAsync();

        Assert.False(Table(schema, null, "Assignments").IsJoinTable);
    }

    [Fact]
    public async Task Heuristika_se_da_vypnout()
    {
        var schema = await ReadSqlServerAsync(new SchemaReadOptions
        {
            IncludeMigrations = false,
            DetectJoinTables = false,
        });

        Assert.False(Table(schema, null, "TeamMembers").IsJoinTable);
        Assert.All(schema.Tables, t => Assert.False(t.IsJoinTable));
    }

    [Fact]
    public async Task Entita_bez_mapovani_na_tabulku_se_do_schematu_nedostane()
    {
        var schema = await ReadSqlServerAsync();

        Assert.DoesNotContain(schema.Tables, t => t.Name.Name.Contains("PersonName", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rucni_vazebni_tabulka_zustava_dvema_vztahy_1_N()
    {
        var schema = await ReadSqlServerAsync();
        var fromTeamMembers = schema.Relationships.Where(r => r.From.Name == "TeamMembers").ToArray();

        // Heuristika tabulku označí, ale bez skip-navigací se hrany nesbalují —
        // sbalit se smí jen to, co je jako N:M skutečně v modelu.
        Assert.Equal(2, fromTeamMembers.Length);
        Assert.All(fromTeamMembers, r => Assert.Equal(DbCardinality.OneToMany, r.Cardinality));
        Assert.All(fromTeamMembers, r => Assert.True(r.IsIdentifying));
        Assert.DoesNotContain(
            schema.Relationships,
            r => r.Cardinality == DbCardinality.ManyToMany && r.ViaJoinTable?.Name == "TeamMembers");
    }

    [Fact]
    public async Task Jednosmerne_NM_se_sbali_i_kdyz_navigace_je_jen_na_jedne_strane()
    {
        var schema = await ReadSqlServerAsync();
        var manyToMany = Assert.Single(
            schema.Relationships,
            r => r.Cardinality == DbCardinality.ManyToMany);

        Assert.Equal("PersonSkill", manyToMany.ViaJoinTable!.Value.Name);
        Assert.Equal("People", manyToMany.From.Name);
        Assert.Equal("Skills", manyToMany.To.Name);

        // Navigace je jen na entitě Skill; protistrana je v modelu jako stínová.
        Assert.NotNull(manyToMany.FromNavigation ?? manyToMany.ToNavigation);
    }

    [Fact]
    public async Task Skryte_tabulky_se_nenactou_ani_jejich_vztahy()
    {
        var schema = await ReadSqlServerAsync(new SchemaReadOptions
        {
            IncludeMigrations = false,
            HideTables = ["Team*"],
        });

        Assert.DoesNotContain(schema.Tables, t => t.Name.Name.StartsWith("Team", StringComparison.Ordinal));
        Assert.DoesNotContain(schema.Relationships, r =>
            r.From.Name.StartsWith("Team", StringComparison.Ordinal)
            || r.To.Name.StartsWith("Team", StringComparison.Ordinal));
    }

    [Fact]
    public async Task IncludeSchemas_omezi_vysledek_na_jedno_schema()
    {
        var schema = await ReadSqlServerAsync(new SchemaReadOptions
        {
            IncludeMigrations = false,
            IncludeSchemas = ["audit"],
        });

        var only = Assert.Single(schema.Tables);
        Assert.Equal("audit.Ledgers", only.Qualified);
        Assert.Empty(schema.Relationships);
    }

    [Fact]
    public async Task Skryti_principalni_tabulky_odstrani_vztah_ale_ne_cizi_klic()
    {
        var schema = await ReadSqlServerAsync(new SchemaReadOptions
        {
            IncludeMigrations = false,
            HideTables = ["People"],
        });

        Assert.DoesNotContain(schema.Relationships, r => r.To.Name == "People");
        Assert.Contains(Table(schema, null, "TeamMembers").ForeignKeys, f => f.PrincipalTable.Name == "People");
    }

    [Fact]
    public async Task Sbalene_NM_se_zahodi_kdyz_je_jedna_strana_skryta()
    {
        await using var context = DbsViewer.SampleShop.ShopContextFactory.CreateSqlite();
        var schema = await new EfCoreModelSchemaSource(context).ReadAsync(new SchemaReadOptions
        {
            IncludeMigrations = false,
            HideTables = ["Tags"],
        });

        Assert.DoesNotContain(schema.Relationships, r => r.Cardinality == DbCardinality.ManyToMany);
        Assert.DoesNotContain(schema.Relationships, r => r.From.Name == "ProductTags");
    }

    [Fact]
    public async Task Stejny_model_na_SQLite_da_stejne_tabulky()
    {
        await using var context = EdgeCaseContextFactory.CreateSqlite();
        var schema = await new EfCoreModelSchemaSource(context)
            .ReadAsync(new SchemaReadOptions { IncludeMigrations = false });

        Assert.Equal(DbProviderKind.Sqlite, schema.Provider);
        Assert.Contains(schema.Tables, t => t.Qualified == "audit.Ledgers");
        // Guid klíč se nesmí označit jako identity ani bez anotace SQL Serveru.
        Assert.False(Table(schema, "audit", "Ledgers").FindColumn("Id")!.IsIdentity);
    }
}
