using Microsoft.EntityFrameworkCore;

namespace DbsViewer.SampleShop;

/// <summary>
/// Vytváření ukázkového kontextu pro nástroje a testy, aby se connection string
/// nemusel opisovat na deseti místech.
/// </summary>
public static class ShopContextFactory
{
    /// <summary>
    /// Kontext nad SQLite. Cesta <c>:memory:</c> vytvoří databázi v paměti,
    /// jinak se použije soubor.
    /// </summary>
    public static ShopContext CreateSqlite(string path = ":memory:")
    {
        var options = new DbContextOptionsBuilder<ShopContext>()
            .UseSqlite($"Data Source={path}")
            .Options;

        return new ShopContext(options);
    }

    /// <summary>Kontext nad SQLite z hotového connection stringu.</summary>
    public static ShopContext CreateSqliteRaw(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ShopContext>()
            .UseSqlite(connectionString)
            .Options;

        return new ShopContext(options);
    }

    /// <summary>
    /// Kontext nad SQL Serverem. Připojení se nikdy neotevírá jen kvůli čtení modelu,
    /// takže connection string nemusí ukazovat na existující databázi.
    /// </summary>
    public static ShopContext CreateSqlServer(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ShopContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new ShopContext(options);
    }
}
