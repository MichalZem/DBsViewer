using Microsoft.EntityFrameworkCore;

namespace DbsViewer.EfCore.Internal;

/// <summary>
/// Zdroj seznamu migrací. Oddělený od <see cref="EfCoreModelSchemaSource"/> proto,
/// že jako jediná část čtení EF modelu sahá do databáze, a její selhání má vlastní
/// definované chování.
/// </summary>
internal interface IMigrationsReader
{
    /// <summary>Migrace přítomné v assembly aplikace.</summary>
    IEnumerable<string> GetInAssembly();

    /// <summary>Migrace zapsané v <c>__EFMigrationsHistory</c>. Dotaz do databáze.</summary>
    Task<IEnumerable<string>> GetAppliedAsync(CancellationToken cancellationToken);
}

/// <summary>Výchozí implementace nad <see cref="DbContext.Database"/>.</summary>
internal sealed class EfMigrationsReader(DbContext context) : IMigrationsReader
{
    public IEnumerable<string> GetInAssembly() => context.Database.GetMigrations();

    public Task<IEnumerable<string>> GetAppliedAsync(CancellationToken cancellationToken) =>
        context.Database.GetAppliedMigrationsAsync(cancellationToken);
}
