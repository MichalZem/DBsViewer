using DbsViewer.TestKit;
namespace DbsViewer.Tests.EfCore;

/// <summary>
/// Ověření čtení EF modelu proti ukázkovému e-shopu. Každý test drží jednu vlastnost modelu,
/// aby při rozbití bylo hned vidět co.
/// </summary>
public class ShopSchemaTests(ShopSchemaFixture fixture) : IClassFixture<ShopSchemaFixture>
{
    private DatabaseSchema Schema => fixture.Schema;

    [Fact]
    public void Hlavicka_popisuje_zdroj_i_providera()
    {
        Assert.Equal(SchemaSourceKind.EfModel, Schema.SourceKind);
        Assert.Equal(DbProviderKind.Sqlite, Schema.Provider);
        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", Schema.ProviderName);
        Assert.Equal("EF model (ShopContext)", Schema.SourceName);
        Assert.Equal("main", Schema.DatabaseName);
        Assert.Empty(Schema.Warnings);
    }

    [Fact]
    public void Nacetly_se_vsechny_tabulky_i_pohled()
    {
        string[] expected =
        [
            "Categories", "CustomerProfiles", "Customers", "OrderLines", "Orders",
            "OrderSummaries", "Payments", "Products", "ProductTags", "Tags",
        ];

        Seq.Equal(expected, Schema.Tables.Select(t => t.Name.Name));
    }

    [Fact]
    public void Tabulky_jsou_serazene_podle_jmena() =>
        Seq.Equal(
            Schema.Tables.Select(t => t.Qualified).Order(StringComparer.OrdinalIgnoreCase),
            Schema.Tables.Select(t => t.Qualified));

    [Fact]
    public void Komentar_tabulky_se_precte() =>
        Assert.Equal("Zákazníci e-shopu", fixture.Table("Customers").Comment);

    [Fact]
    public void Komentar_sloupce_se_precte() =>
        Assert.Equal("Obchodní název produktu", fixture.Column("Products", "Name").Comment);

    [Fact]
    public void Sloupce_primarniho_klice_jsou_prvni_a_v_poradi_klice()
    {
        var lines = fixture.Table("OrderLines");

        Seq.Equal(["OrderId", "LineNumber"], lines.Columns.Take(2).Select(c => c.Name));
        Seq.Equal(["OrderId", "LineNumber"], lines.PrimaryKey!.Columns);
        Assert.All(lines.Columns.Take(2), c => Assert.True(c.IsPrimaryKey));
    }

    [Fact]
    public void Ostatni_sloupce_jsou_abecedne_a_maji_rostouci_poradi()
    {
        var lines = fixture.Table("OrderLines");
        var rest = lines.Columns.Skip(2).Select(c => c.Name).ToArray();

        Seq.Equal(rest.Order(StringComparer.OrdinalIgnoreCase), rest);
        Seq.Equal(Enumerable.Range(1, lines.Columns.Count), lines.Columns.Select(c => c.Ordinal));
    }

    [Fact]
    public void Owned_type_se_mapuje_do_stejne_tabulky()
    {
        var customers = fixture.Table("Customers");

        Seq.Equal(["Address", "Customer"], customers.EntityClrNames);
        Assert.Contains(customers.Columns, c => c.Name == "BillingStreet");
        Assert.Contains(customers.Columns, c => c.Name == "BillingCity");
    }

    [Fact]
    public void TPH_hierarchie_sdili_tabulku_a_ma_diskriminator()
    {
        var payments = fixture.Table("Payments");

        Seq.Equal(["BankTransfer", "CardPayment", "Payment"], payments.EntityClrNames);
        Assert.Equal("PaymentType", payments.DiscriminatorColumn);
        Assert.Contains(payments.Columns, c => c.Name == "CardLast4" && c.IsNullable);
        Assert.Contains(payments.Columns, c => c.Name == "Iban" && c.IsNullable);
    }

    [Fact]
    public void Tabulka_bez_dedicnosti_nema_diskriminator() =>
        Assert.Null(fixture.Table("Products").DiscriminatorColumn);

    [Fact]
    public void Pohled_se_pozna_a_nema_klice_ani_indexy()
    {
        var view = fixture.Table("OrderSummaries");

        Assert.True(view.IsView);
        Seq.Equal(["OrderSummary"], view.EntityClrNames);
        Assert.Null(view.PrimaryKey);
        Assert.Empty(view.Indexes);
        Assert.Empty(view.ForeignKeys);
        Assert.Equal(4, view.Columns.Count);
        Assert.All(view.Columns, c => Assert.NotNull(c.ClrType));
        Seq.Equal([1, 2, 3, 4], view.Columns.Select(c => c.Ordinal));
    }

    [Fact]
    public void Sloupec_pohledu_zna_svou_vlastnost() =>
        Seq.Equal(["Number"], fixture.Table("OrderSummaries").FindColumn("Number")!.PropertyNames);

    [Fact]
    public void Identity_se_pozna_u_celociselneho_klice()
    {
        Assert.True(fixture.Column("Customers", "Id").IsIdentity);
        Assert.Equal(DbValueGenerated.OnAdd, fixture.Column("Customers", "Id").ValueGenerated);
        Assert.False(fixture.Column("Orders", "Number").IsIdentity);
        Assert.Equal(DbValueGenerated.Never, fixture.Column("Orders", "Number").ValueGenerated);
    }

    [Fact]
    public void Klic_ktery_je_zaroven_cizim_klicem_neni_identity()
    {
        var customerId = fixture.Column("CustomerProfiles", "CustomerId");

        Assert.True(customerId.IsPrimaryKey);
        Assert.True(customerId.IsForeignKey);
        Assert.False(customerId.IsIdentity);
    }

    [Fact]
    public void Defaultni_hodnota_a_delka_se_prectou()
    {
        Assert.Equal("CURRENT_TIMESTAMP", fixture.Column("Customers", "CreatedAt").DefaultValueSql);
        Assert.Equal(256, fixture.Column("Customers", "Email").MaxLength);
        Assert.Null(fixture.Column("Customers", "Id").MaxLength);
    }

    [Fact]
    public void Presnost_a_meritko_se_prectou()
    {
        var price = fixture.Column("Products", "Price");

        Assert.Equal(18, price.Precision);
        Assert.Equal(2, price.Scale);
    }

    [Fact]
    public void Pocitany_sloupec_nese_svuj_vyraz()
    {
        var total = fixture.Column("OrderLines", "Total");

        Assert.True(total.IsComputed);
        Assert.Equal("\"Quantity\" * \"UnitPrice\"", total.ComputedSql);
        Assert.True(total.IsStored);
    }

    [Fact]
    public void Bezny_sloupec_nema_priznaky_pocitaneho()
    {
        var quantity = fixture.Column("OrderLines", "Quantity");

        Assert.False(quantity.IsComputed);
        Assert.Null(quantity.ComputedSql);
        Assert.Null(quantity.IsStored);
    }

    [Fact]
    public void Concurrency_token_se_pozna()
    {
        Assert.True(fixture.Column("Products", "Version").IsConcurrencyToken);
        Assert.False(fixture.Column("Products", "Name").IsConcurrencyToken);
    }

    [Fact]
    public void Sloupec_zna_svuj_CLR_typ_i_vlastnost()
    {
        var email = fixture.Column("Customers", "Email");

        Assert.Equal("System.String", email.ClrType);
        Seq.Equal(["Email"], email.PropertyNames);
        Assert.False(email.IsShadowProperty);
    }

    [Fact]
    public void Nullable_sloupec_nese_citelny_CLR_typ()
    {
        // Assembly-qualified jméno by prosáklo až do odpovědi HTTP API a měnilo by se
        // s verzí .NETu, na kterém server běží.
        Assert.Equal("System.Int32?", fixture.Column("Categories", "ParentCategoryId").ClrType);
    }

    [Fact]
    public void Nullabilita_odpovida_modelu()
    {
        Assert.False(fixture.Column("Customers", "Email").IsNullable);
        Assert.True(fixture.Column("Customers", "DisplayName").IsNullable);
    }

    [Fact]
    public void Indexy_jsou_serazene_a_znaji_unikatnost()
    {
        var products = fixture.Table("Products");

        Seq.Equal(
            ["IX_Products_Category_Name", "UX_Products_Sku"],
            products.Indexes.Select(i => i.Name));

        var unique = products.Indexes.Single(i => i.Name == "UX_Products_Sku");
        Assert.True(unique.IsUnique);
        Seq.Equal(["Sku"], unique.Columns);

        var composite = products.Indexes.Single(i => i.Name == "IX_Products_Category_Name");
        Assert.False(composite.IsUnique);
        Seq.Equal(["CategoryId", "Name"], composite.Columns);
        Assert.Null(composite.FilterSql);
        Assert.Empty(composite.IncludedColumns);
        Assert.Null(composite.IsClustered);
    }

    [Fact]
    public void Check_constraint_se_precte()
    {
        var check = Assert.Single(fixture.Table("Products").CheckConstraints);

        Assert.Equal("CK_Products_Price", check.Name);
        Assert.Equal("\"Price\" >= 0", check.Sql);
    }

    [Fact]
    public void Tabulka_bez_check_constraintu_ma_prazdny_seznam() =>
        Assert.Empty(fixture.Table("Orders").CheckConstraints);

    [Fact]
    public void Cizi_klic_zna_navigace_i_chovani_pri_mazani()
    {
        var foreignKey = Assert.Single(fixture.Table("Orders").ForeignKeys);

        Assert.Equal("FK_Orders_Customers_CustomerId", foreignKey.Name);
        Seq.Equal(["CustomerId"], foreignKey.Columns);
        Assert.Equal(new DbObjectName(null, "Customers"), foreignKey.PrincipalTable);
        Seq.Equal(["Id"], foreignKey.PrincipalColumns);
        Assert.Equal(DbDeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        Assert.True(foreignKey.IsRequired);
        Assert.False(foreignKey.IsUnique);
        Assert.Equal("Customer", foreignKey.NavigationName);
        Assert.Equal("Orders", foreignKey.InverseNavigationName);
    }

    [Fact]
    public void Cizi_klice_jsou_serazene_podle_jmena()
    {
        var names = fixture.Table("OrderLines").ForeignKeys.Select(f => f.Name).ToArray();

        Seq.Equal(names.Order(StringComparer.Ordinal), names);
    }

    [Fact]
    public void Vztah_1_1_ma_unikatni_cizi_klic()
    {
        var foreignKey = Assert.Single(fixture.Table("CustomerProfiles").ForeignKeys);

        Assert.True(foreignKey.IsUnique);
        Assert.Equal(DbDeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    [Fact]
    public void Vztahy_pokryvaji_vsechny_kardinality()
    {
        var byCardinality = Schema.Relationships
            .GroupBy(r => r.Cardinality)
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(1, byCardinality[DbCardinality.OneToOne]);
        Assert.Equal(1, byCardinality[DbCardinality.ManyToMany]);
        Assert.Equal(6, byCardinality[DbCardinality.OneToMany]);
        Assert.Equal(8, Schema.Relationships.Count);
    }

    [Fact]
    public void Self_reference_se_pozna()
    {
        var self = Schema.Relationships.Single(r => r.IsSelfReference);

        Assert.Equal("Categories", self.From.Name);
        Assert.Equal(DbCardinality.OneToMany, self.Cardinality);
        Assert.Equal(DbDeleteBehavior.Restrict, self.DeleteBehavior);
        Assert.False(self.IsRequired);
    }

    [Fact]
    public void Identifikujici_vztah_ma_cizi_klic_v_primarnim_klici()
    {
        var identifying = Schema.Relationships.Where(r => r.IsIdentifying).ToArray();

        Seq.Equal(
            ["CustomerProfiles", "OrderLines"],
            identifying.Select(r => r.From.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Neidentifikujici_vztah_se_neoznaci() =>
        Assert.False(Schema.Relationships.Single(r => r.From.Name == "Orders").IsIdentifying);

    [Fact]
    public void Vztah_nese_sloupce_i_navigace()
    {
        var orders = Schema.Relationships.Single(r => r.From.Name == "Orders");

        Seq.Equal(["CustomerId"], orders.FromColumns);
        Seq.Equal(["Id"], orders.ToColumns);
        Assert.Equal("Customer", orders.FromNavigation);
        Assert.Equal("Orders", orders.ToNavigation);
        Assert.Equal("FK_Orders_Customers_CustomerId", orders.ForeignKeyName);
        Assert.StartsWith("fk:", orders.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void Vztahy_jsou_serazene_podle_identifikatoru()
    {
        var ids = Schema.Relationships.Select(r => r.Id).ToArray();

        Seq.Equal(ids.Order(StringComparer.Ordinal), ids);
    }

    [Fact]
    public void Vazebni_tabulka_NM_se_oznaci()
    {
        var joinTable = fixture.Table("ProductTags");

        Assert.True(joinTable.IsJoinTable);
        Seq.Equal(["ProductTag"], joinTable.EntityClrNames);
        Assert.DoesNotContain("Dictionary", joinTable.EntityClrNames[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Bezna_tabulka_neni_oznacena_jako_vazebni() =>
        Assert.False(fixture.Table("Orders").IsJoinTable);

    [Fact]
    public void Vztah_NM_se_sbali_do_jedine_hrany()
    {
        var manyToMany = Schema.Relationships.Single(r => r.Cardinality == DbCardinality.ManyToMany);

        Assert.Equal(new DbObjectName(null, "ProductTags"), manyToMany.ViaJoinTable);
        Assert.True(manyToMany.IsRequired);
        Assert.Equal("Tags", manyToMany.FromNavigation);
        Assert.Equal("Products", manyToMany.ToNavigation);
        Assert.StartsWith("m2m:", manyToMany.Id, StringComparison.Ordinal);
        Assert.Null(manyToMany.ForeignKeyName);
    }

    [Fact]
    public void Cizi_klice_vazebni_tabulky_uz_nejsou_samostatnymi_vztahy()
    {
        Assert.Equal(2, fixture.Table("ProductTags").ForeignKeys.Count);
        Assert.DoesNotContain(Schema.Relationships, r => r.From.Name == "ProductTags");
    }

    [Fact]
    public void Zadna_tabulka_neni_vyloucena_z_migraci() =>
        Assert.All(Schema.Tables, t => Assert.False(t.IsExcludedFromMigrations));

    [Fact]
    public void Migrace_se_nenacitaly() => Assert.Empty(Schema.Migrations);
}
