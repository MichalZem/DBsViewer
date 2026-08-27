using Microsoft.EntityFrameworkCore;

namespace DbsViewer.Tests;

/// <summary>
/// Model s konstrukcemi, které ukázkový e-shop nemá: tabulka ve vlastním schématu,
/// tabulka bez klíče, klíč typu Guid i string, filtrovaný a sestupný index,
/// <c>INCLUDE</c> sloupce, explicitní vazební tabulka, tabulka vyloučená z migrací
/// a entita mapovaná na SQL dotaz.
/// </summary>
public class EdgeCaseContext(DbContextOptions<EdgeCaseContext> options) : DbContext(options)
{
    public DbSet<Ledger> Ledgers => Set<Ledger>();

    public DbSet<Snapshot> Snapshots => Set<Snapshot>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<Team> Teams => Set<Team>();

    public DbSet<Person> People => Set<Person>();

    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();

    public DbSet<Assignment> Assignments => Set<Assignment>();

    public DbSet<LegacyTable> LegacyTables => Set<LegacyTable>();

    public DbSet<PersonName> PersonNames => Set<PersonName>();

    public DbSet<Skill> Skills => Set<Skill>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Ledger>(entity =>
        {
            // Guid klíč generovaný na klientovi — nesmí se označit jako identity.
            entity.ToTable("Ledgers", "audit");
            entity.Property(l => l.Amount).HasPrecision(18, 4);
            entity.HasIndex(l => l.PostedOn)
                .HasDatabaseName("IX_Ledgers_PostedOn")
                .HasFilter("[PostedOn] IS NOT NULL")
                .IsDescending(true)
                .IncludeProperties(l => l.Amount)
                .IsClustered(false);
        });

        modelBuilder.Entity<Snapshot>(entity =>
        {
            // Textový klíč zadávaný ručně — také ne identity.
            entity.ToTable("Snapshots");
            entity.HasKey(s => s.Code);
            entity.Property(s => s.Code).HasMaxLength(40);
            entity.Property(s => s.Note).UseCollation("Czech_CI_AS");
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLogs", t => t.ExcludeFromMigrations());
            entity.HasNoKey();
        });

        modelBuilder.Entity<Team>().ToTable("Teams");
        modelBuilder.Entity<Person>().ToTable("People");

        modelBuilder.Entity<TeamMember>(entity =>
        {
            // Vazební tabulka namodelovaná ručně, tedy bez skip-navigací.
            // Musí ji odchytit heuristika, ne detekce přes model.
            entity.ToTable("TeamMembers");
            entity.HasKey(m => new { m.TeamId, m.PersonId });
            entity.HasOne(m => m.Team).WithMany(t => t.Members).HasForeignKey(m => m.TeamId);
            entity.HasOne(m => m.Person).WithMany(p => p.Teams).HasForeignKey(m => m.PersonId);
        });

        modelBuilder.Entity<Assignment>(entity =>
        {
            // Vypadá jako vazební tabulka, ale nese vlastní data — sbalit se nesmí.
            entity.ToTable("Assignments");
            entity.HasKey(a => new { a.TeamId, a.PersonId });
            entity.Property(a => a.Role).HasMaxLength(50);
            entity.HasOne<Team>().WithMany().HasForeignKey(a => a.TeamId);
            entity.HasOne<Person>().WithMany().HasForeignKey(a => a.PersonId);
        });

        modelBuilder.Entity<LegacyTable>(entity =>
        {
            entity.ToTable("Legacy", "legacy");
            entity.Property(l => l.Id).ValueGeneratedNever();
        });

        modelBuilder.Entity<Skill>(entity =>
        {
            // Jednosměrné N:M — navigace existuje jen na jedné straně,
            // takže druhá skip-navigace, a tím i inverze, chybí.
            entity.ToTable("Skills");
            entity.HasMany(s => s.People).WithMany();
        });

        modelBuilder.Entity<PersonName>(entity =>
        {
            // Není namapovaná na tabulku ani na pohled — nesmí se objevit ve výsledku.
            entity.HasNoKey();
            entity.ToTable((string?)null);
            entity.ToSqlQuery("SELECT 1 AS Value");
        });
    }
}

public class Ledger
{
    public Guid Id { get; set; }

    public decimal Amount { get; set; }

    public DateTime? PostedOn { get; set; }
}

public class Snapshot
{
    public string Code { get; set; } = "";

    public string? Note { get; set; }
}

public class AuditLog
{
    public string Message { get; set; } = "";
}

public class Team
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public List<TeamMember> Members { get; set; } = [];
}

public class Person
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public List<TeamMember> Teams { get; set; } = [];
}

public class TeamMember
{
    public int TeamId { get; set; }

    public Team Team { get; set; } = null!;

    public int PersonId { get; set; }

    public Person Person { get; set; } = null!;
}

public class Assignment
{
    public int TeamId { get; set; }

    public int PersonId { get; set; }

    public string? Role { get; set; }
}

public class LegacyTable
{
    public int Id { get; set; }

    public string? Payload { get; set; }
}

public class Skill
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public List<Person> People { get; set; } = [];
}

public class PersonName
{
    public int Value { get; set; }
}

/// <summary>Vytváření hraničního kontextu nad oběma podporovanými providery.</summary>
public static class EdgeCaseContextFactory
{
    public static EdgeCaseContext CreateSqlServer() => new(
        new DbContextOptionsBuilder<EdgeCaseContext>()
            .UseSqlServer("Server=(local);Database=DbsViewerTests;Trusted_Connection=True;")
            .Options);

    public static EdgeCaseContext CreateSqlite() => new(
        new DbContextOptionsBuilder<EdgeCaseContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);
}
