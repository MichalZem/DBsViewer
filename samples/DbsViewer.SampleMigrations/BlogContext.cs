using Microsoft.EntityFrameworkCore;

namespace DbsViewer.SampleMigrations;

/// <summary>
/// Malý model se skutečnou historií migrací.
/// </summary>
/// <remarks>
/// Existuje kvůli testům historie schématu: snapshoty migrací se dají číst jen
/// z assembly, která migrace opravdu obsahuje, takže je nejde vyrobit v paměti.
/// Migrace ve složce <c>Migrations</c> přidávají postupně sloupec a celou tabulku,
/// aby šlo ověřit, že se historie čte správně.
/// </remarks>
public sealed class BlogContext(DbContextOptions<BlogContext> options) : DbContext(options)
{
    public DbSet<Autor> Autori => Set<Autor>();

    public DbSet<Clanek> Clanky => Set<Clanek>();

    public DbSet<Komentar> Komentare => Set<Komentar>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Entity<Autor>().HasIndex(a => a.Email).IsUnique();
    }
}

public sealed class Autor
{
    public int Id { get; set; }

    public string Jmeno { get; set; } = "";

    public string Email { get; set; } = "";

    public List<Clanek> Clanky { get; set; } = [];
}

public sealed class Clanek
{
    public int Id { get; set; }

    public string Nadpis { get; set; } = "";

    public int AutorId { get; set; }

    public Autor? Autor { get; set; }

    /// <summary>Přidáno druhou migrací.</summary>
    public DateTime? Publikovano { get; set; }
}

/// <summary>Celá tabulka přidaná třetí migrací.</summary>
public sealed class Komentar
{
    public int Id { get; set; }

    public int ClanekId { get; set; }

    public Clanek? Clanek { get; set; }

    public string Text { get; set; } = "";
}
