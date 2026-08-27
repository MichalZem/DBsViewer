using DbsViewer.EfCore;
using DbsViewer.EfCore.Internal;
using DbsViewer.SampleShop;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using DbsViewer.TestKit;

namespace DbsViewer.Tests.EfCore;

public class SafeReadTests
{
    [Fact]
    public void Uspesne_cteni_nezapisuje_upozorneni()
    {
        var warnings = new List<string>();

        var value = SafeRead.Value(() => "ok", "náhrada", static _ => "chyba", warnings);

        Assert.Equal("ok", value);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Selhani_vrati_nahradu_a_popise_duvod()
    {
        var warnings = new List<string>();

        var value = SafeRead.Value(
            () => throw new InvalidOperationException("rozbité"),
            "náhrada",
            ex => $"nešlo přečíst: {ex.Message}",
            warnings);

        Assert.Equal("náhrada", value);
        Assert.Equal("nešlo přečíst: rozbité", Assert.Single(warnings));
    }

    [Fact]
    public void Optional_vraci_hodnotu_kdyz_cteni_projde()
    {
        var warnings = new List<string>();

        Assert.Equal("ok", SafeRead.Optional(() => "ok", static _ => "chyba", warnings));
        Assert.Empty(warnings);
    }

    [Fact]
    public void Optional_vraci_null_kdyz_cteni_selze()
    {
        var warnings = new List<string>();

        var value = SafeRead.Optional<string>(
            () => throw new InvalidOperationException("rozbité"),
            ex => $"nešlo: {ex.Message}",
            warnings);

        Assert.Null(value);
        Assert.Equal("nešlo: rozbité", Assert.Single(warnings));
    }

    [Fact]
    public void Optional_propusti_i_null_bez_upozorneni()
    {
        var warnings = new List<string>();

        Assert.Null(SafeRead.Optional<string>(() => null, static _ => "chyba", warnings));
        Assert.Empty(warnings);
    }
}

public class ModelResolutionTests
{
    [Fact]
    public void Design_time_model_ma_prednost()
    {
        using var context = ShopContextFactory.CreateSqlite();
        var designTime = context.GetService<IDesignTimeModel>().Model;
        var warnings = new List<string>();

        var resolved = EfModelReader.ResolveModel(() => designTime, context.Model, warnings);

        Assert.Same(designTime, resolved);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Kontext_vraci_design_time_model()
    {
        using var context = ShopContextFactory.CreateSqlite();
        var warnings = new List<string>();

        var resolved = EfModelReader.ResolveModel(context, warnings);

        Assert.Same(context.GetService<IDesignTimeModel>().Model, resolved);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Bez_design_time_modelu_se_pouzije_runtime_model_a_upozorni_se()
    {
        using var context = ShopContextFactory.CreateSqlite();
        var warnings = new List<string>();

        var resolved = EfModelReader.ResolveModel(
            () => throw new InvalidOperationException("služba chybí"),
            context.Model,
            warnings);

        Assert.Same(context.Model, resolved);
        var warning = Assert.Single(warnings);
        Assert.Contains("Design-time model není dostupný", warning, StringComparison.Ordinal);
        Assert.Contains("služba chybí", warning, StringComparison.Ordinal);
    }
}

public class MappingHelperTests
{
    [Theory]
    [InlineData(null, DbProviderKind.Unknown)]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", DbProviderKind.SqlServer)]
    [InlineData("microsoft.entityframeworkcore.sqlserver", DbProviderKind.SqlServer)]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite", DbProviderKind.Sqlite)]
    [InlineData("Npgsql.EntityFrameworkCore.PostgreSQL", DbProviderKind.Unknown)]
    [InlineData("Microsoft.EntityFrameworkCore.InMemory", DbProviderKind.Unknown)]
    public void Provider_se_urcuje_podle_jmena(string? providerName, DbProviderKind expected) =>
        Assert.Equal(expected, EfModelReader.DetectProvider(providerName));

    [Theory]
    [InlineData(ReferentialAction.NoAction, DbDeleteBehavior.NoAction)]
    [InlineData(ReferentialAction.Restrict, DbDeleteBehavior.Restrict)]
    [InlineData(ReferentialAction.Cascade, DbDeleteBehavior.Cascade)]
    [InlineData(ReferentialAction.SetNull, DbDeleteBehavior.SetNull)]
    [InlineData(ReferentialAction.SetDefault, DbDeleteBehavior.SetDefault)]
    [InlineData((ReferentialAction)99, DbDeleteBehavior.Unknown)]
    public void Chovani_pri_mazani_se_mapuje(ReferentialAction action, DbDeleteBehavior expected) =>
        Assert.Equal(expected, EfModelReader.MapReferentialAction(action));

    [Theory]
    [InlineData(null, DbValueGenerated.Never)]
    [InlineData(ValueGenerated.Never, DbValueGenerated.Never)]
    [InlineData(ValueGenerated.OnAdd, DbValueGenerated.OnAdd)]
    [InlineData(ValueGenerated.OnUpdate, DbValueGenerated.OnUpdate)]
    [InlineData(ValueGenerated.OnAddOrUpdate, DbValueGenerated.OnAddOrUpdate)]
    public void Generovani_hodnoty_se_mapuje(ValueGenerated? generated, DbValueGenerated expected) =>
        Assert.Equal(expected, EfModelReader.MapValueGenerated(generated));

    private sealed class Annotations(params (string Name, object? Value)[] values) : IAnnotatable
    {
        private readonly Dictionary<string, IAnnotation> _annotations =
            values.ToDictionary(v => v.Name, v => (IAnnotation)new Annotation(v.Name, v.Value));

        public object? this[string name] => FindAnnotation(name)?.Value;

        public IAnnotation? FindAnnotation(string name) =>
            _annotations.TryGetValue(name, out var annotation) ? annotation : null;

        public IEnumerable<IAnnotation> GetAnnotations() => _annotations.Values;

        public IAnnotation GetAnnotation(string name) =>
            FindAnnotation(name) ?? throw new InvalidOperationException(name);

        public IReadOnlyDictionary<string, object?> ToDictionary()
            => _annotations.ToDictionary(a => a.Key, a => a.Value.Value);

        public object? FindRuntimeAnnotationValue(string name) => null;

        public IAnnotation? FindRuntimeAnnotation(string name) => null;

        public IEnumerable<IAnnotation> GetRuntimeAnnotations() => [];

        public IAnnotation AddRuntimeAnnotation(string name, object? value) =>
            throw new NotSupportedException();

        public IAnnotation SetRuntimeAnnotation(string name, object? value) =>
            throw new NotSupportedException();

        public IAnnotation? RemoveRuntimeAnnotation(string name) => null;

        public TValue GetOrAddRuntimeAnnotationValue<TValue, TArg>(
            string name,
            Func<TArg?, TValue> valueFactory,
            TArg? factoryArgument) => valueFactory(factoryArgument);
    }

    [Fact]
    public void Bool_anotace_se_precte()
    {
        var annotatable = new Annotations(("SqlServer:Clustered", true));

        Assert.True(EfModelReader.ReadBoolAnnotation(annotatable, "SqlServer:Clustered"));
        Assert.Null(EfModelReader.ReadBoolAnnotation(annotatable, "Chybi"));
        Assert.Null(EfModelReader.ReadBoolAnnotation(null, "SqlServer:Clustered"));
    }

    [Fact]
    public void Bool_anotace_jineho_typu_se_ignoruje()
    {
        var annotatable = new Annotations(("SqlServer:Clustered", "ano"));

        Assert.Null(EfModelReader.ReadBoolAnnotation(annotatable, "SqlServer:Clustered"));
    }

    [Fact]
    public void Anotace_se_seznamem_sloupcu_se_precte_z_pole()
    {
        var annotatable = new Annotations(("SqlServer:Include", new[] { "Amount", "Note" }));

        Seq.Equal(["Amount", "Note"], EfModelReader.ReadStringArrayAnnotation(annotatable, "SqlServer:Include"));
    }

    [Fact]
    public void Anotace_se_seznamem_sloupcu_se_precte_i_ze_sekvence()
    {
        var annotatable = new Annotations(("SqlServer:Include", new List<string> { "Amount" }));

        Seq.Equal(["Amount"], EfModelReader.ReadStringArrayAnnotation(annotatable, "SqlServer:Include"));
    }

    [Fact]
    public void Chybejici_nebo_nesmyslna_anotace_da_prazdny_seznam()
    {
        var annotatable = new Annotations(("SqlServer:Include", 42));

        Assert.Empty(EfModelReader.ReadStringArrayAnnotation(annotatable, "SqlServer:Include"));
        Assert.Empty(EfModelReader.ReadStringArrayAnnotation(annotatable, "Chybi"));
        Assert.Empty(EfModelReader.ReadStringArrayAnnotation(null, "SqlServer:Include"));
    }
}

public class MigrationsReaderTests
{
    [Fact]
    public void Cte_migrace_z_kontextu()
    {
        using var context = ShopContextFactory.CreateSqlite();
        var reader = new EfMigrationsReader(context);

        // Ukázkový kontext žádné migrace nemá — podstatné je, že se čte bez výjimky.
        Assert.Empty(reader.GetInAssembly());
    }

    [Fact]
    public async Task Neexistujici_databaze_nema_zadne_aplikovane_migrace()
    {
        using var context = ShopContextFactory.CreateSqlite("nonexistent-directory/db.sqlite");
        var reader = new EfMigrationsReader(context);

        // EF se nejdřív ptá, jestli databáze existuje — u neexistující tedy nepadá,
        // jen vrátí prázdný seznam. Chybovou cestu pokrývají testy s falešnou čtečkou.
        Assert.Empty(await reader.GetAppliedAsync(CancellationToken.None));
    }
}
