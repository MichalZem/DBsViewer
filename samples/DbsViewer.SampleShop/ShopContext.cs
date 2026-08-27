using Microsoft.EntityFrameworkCore;

namespace DbsViewer.SampleShop;

/// <summary>
/// Ukázkový kontext, na kterém se ověřuje čtení schématu. Model záměrně obsahuje
/// všechny konstrukce, které musí DbsViewer umět: 1:1, 1:N, N:M, self-reference,
/// TPH dědičnost, owned type, počítaný sloupec, check constraint a pohled.
/// </summary>
public class ShopContext(DbContextOptions<ShopContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<CustomerProfile> CustomerProfiles => Set<CustomerProfile>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<OrderSummary> OrderSummaries => Set<OrderSummary>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.ToTable("Customers", t => t.HasComment("Zákazníci e-shopu"));
            entity.Property(c => c.Email).HasMaxLength(256).IsRequired();
            entity.Property(c => c.DisplayName).HasMaxLength(200);
            entity.Property(c => c.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(c => c.Email).IsUnique().HasDatabaseName("UX_Customers_Email");

            entity.OwnsOne(c => c.BillingAddress, address =>
            {
                address.Property(a => a.Street).HasMaxLength(200).HasColumnName("BillingStreet");
                address.Property(a => a.City).HasMaxLength(100).HasColumnName("BillingCity");
                address.Property(a => a.PostalCode).HasMaxLength(20).HasColumnName("BillingPostalCode");
            });
        });

        modelBuilder.Entity<CustomerProfile>(entity =>
        {
            entity.ToTable("CustomerProfiles");
            entity.HasKey(p => p.CustomerId);
            entity.Property(p => p.Bio).HasMaxLength(2000);
            entity.Property(p => p.PreferredLanguage).HasMaxLength(10);

            entity.HasOne(p => p.Customer)
                .WithOne(c => c.Profile)
                .HasForeignKey<CustomerProfile>(p => p.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("Categories");
            entity.Property(c => c.Name).HasMaxLength(120).IsRequired();

            entity.HasOne(c => c.ParentCategory)
                .WithMany(c => c.Children)
                .HasForeignKey(c => c.ParentCategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products", t => t.HasCheckConstraint("CK_Products_Price", "\"Price\" >= 0"));
            entity.Property(p => p.Sku).HasMaxLength(64).IsRequired();
            entity.Property(p => p.Name).HasMaxLength(300).IsRequired()
                .HasComment("Obchodní název produktu");
            entity.Property(p => p.Price).HasPrecision(18, 2);
            entity.Property(p => p.Version).IsConcurrencyToken();

            entity.HasIndex(p => p.Sku).IsUnique().HasDatabaseName("UX_Products_Sku");
            entity.HasIndex(p => new { p.CategoryId, p.Name }).HasDatabaseName("IX_Products_Category_Name");

            entity.HasOne(p => p.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(p => p.Tags)
                .WithMany(t => t.Products)
                .UsingEntity(join => join.ToTable("ProductTags"));
        });

        modelBuilder.Entity<Tag>(entity =>
        {
            entity.ToTable("Tags");
            entity.Property(t => t.Name).HasMaxLength(60).IsRequired();
            entity.HasIndex(t => t.Name).IsUnique().HasDatabaseName("UX_Tags_Name");
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("Orders");
            entity.Property(o => o.Number).HasMaxLength(32).IsRequired();
            entity.HasIndex(o => o.Number).IsUnique().HasDatabaseName("UX_Orders_Number");
            entity.HasIndex(o => new { o.CustomerId, o.PlacedAt }).HasDatabaseName("IX_Orders_Customer_PlacedAt");

            entity.HasOne(o => o.Customer)
                .WithMany(c => c.Orders)
                .HasForeignKey(o => o.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OrderLine>(entity =>
        {
            entity.ToTable("OrderLines");
            entity.HasKey(l => new { l.OrderId, l.LineNumber });
            entity.Property(l => l.UnitPrice).HasPrecision(18, 2);
            entity.Property(l => l.Total)
                .HasPrecision(18, 2)
                .HasComputedColumnSql("\"Quantity\" * \"UnitPrice\"", stored: true);

            entity.HasOne(l => l.Order)
                .WithMany(o => o.Lines)
                .HasForeignKey(l => l.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(l => l.Product)
                .WithMany(p => p.OrderLines)
                .HasForeignKey(l => l.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.ToTable("Payments");
            entity.HasDiscriminator<string>("PaymentType")
                .HasValue<CardPayment>("Card")
                .HasValue<BankTransfer>("Transfer");

            entity.Property(p => p.Amount).HasPrecision(18, 2);

            entity.HasOne(p => p.Order)
                .WithMany(o => o.Payments)
                .HasForeignKey(p => p.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CardPayment>().Property(p => p.CardLast4).HasMaxLength(4);
        modelBuilder.Entity<BankTransfer>().Property(p => p.Iban).HasMaxLength(34);

        modelBuilder.Entity<OrderSummary>(entity =>
        {
            entity.HasNoKey();
            entity.ToView("OrderSummaries");
        });
    }
}
