using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DbsViewer.SampleMigrations;

/// <summary>
/// Kontext pro <c>dotnet ef</c> při generování migrací a pro testy.
/// </summary>
/// <remarks>
/// Migrace se generují proti SQLite, protože ta nepotřebuje běžící server —
/// snapshot modelu je stejně na provideru nezávislý.
/// </remarks>
public sealed class BlogContextFactory : IDesignTimeDbContextFactory<BlogContext>
{
    /// <summary>Kontext nad databází v paměti. Jméno je unikátní, aby si testy nešlapaly.</summary>
    public static BlogContext Create() =>
        new(new DbContextOptionsBuilder<BlogContext>()
            .UseSqlite($"Data Source=blog_{Guid.NewGuid():N};Mode=Memory;Cache=Shared")
            .Options);

    /// <inheritdoc />
    public BlogContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<BlogContext>()
            .UseSqlite("Data Source=blog-design.db")
            .Options);
}
