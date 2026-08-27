using DbsViewer.EfCore;
using DbsViewer.SampleShop;

namespace DbsViewer.Tests.EfCore;

/// <summary>
/// Načte schéma ukázkového e-shopu jednou pro celou třídu testů. Čtení modelu nesahá
/// do databáze, ale stavba EF modelu není zadarmo.
/// </summary>
public sealed class ShopSchemaFixture : IDisposable
{
    private readonly ShopContext _context = ShopContextFactory.CreateSqlite();

    public ShopSchemaFixture()
    {
        var source = new EfCoreModelSchemaSource(_context);
        Schema = source.ReadAsync(new SchemaReadOptions { IncludeMigrations = false })
            .GetAwaiter()
            .GetResult();
    }

    public DatabaseSchema Schema { get; }

    public DbTable Table(string name) =>
        Schema.FindTable(new DbObjectName(null, name))
        ?? throw new InvalidOperationException($"Tabulka {name} ve schématu není.");

    public DbColumn Column(string table, string column) =>
        Table(table).FindColumn(column)
        ?? throw new InvalidOperationException($"Sloupec {table}.{column} ve schématu není.");

    public void Dispose() => _context.Dispose();
}
