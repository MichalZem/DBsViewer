using DbsViewer.EfCore.Internal;
using Microsoft.EntityFrameworkCore;

namespace DbsViewer.EfCore;

/// <summary>
/// Čte schéma z EF Core modelu. Funguje s libovolným relačním providerem, protože se drží
/// veřejného <c>IRelationalModel</c> API a nic providerově specifického nevyžaduje.
/// </summary>
/// <remarks>
/// Samotné čtení modelu nesahá do databáze. Jediná výjimka je seznam aplikovaných migrací,
/// který se dá vypnout přes <see cref="SchemaReadOptions.IncludeMigrations"/>; jeho selhání
/// se nikdy nepropaguje ven, jen skončí ve <see cref="DatabaseSchema.Warnings"/>.
/// </remarks>
public sealed class EfCoreModelSchemaSource : ISchemaSource
{
    private readonly DbContext _context;
    private readonly IMigrationsReader _migrations;

    /// <param name="context">Kontext, ze kterého se čte model. Nemusí být připojený k databázi.</param>
    /// <param name="key">Klíč zdroje, když je registrovaných víc databází.</param>
    public EfCoreModelSchemaSource(DbContext context, string? key = null)
        : this(context, new EfMigrationsReader(context), key)
    {
    }

    internal EfCoreModelSchemaSource(DbContext context, IMigrationsReader migrations, string? key = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(migrations);

        _context = context;
        _migrations = migrations;
        Key = key ?? ISchemaSource.DefaultKey;
    }

    public string Key { get; }

    public string DisplayName => $"EF model ({_context.GetType().Name})";

    public SchemaSourceKind Kind => SchemaSourceKind.EfModel;

    public async Task<DatabaseSchema> ReadAsync(
        SchemaReadOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var warnings = new List<string>();
        var migrations = options.IncludeMigrations
            ? await ReadMigrationsAsync(warnings, cancellationToken).ConfigureAwait(false)
            : [];

        return new EfModelReader(_context, options, warnings).Read(migrations);
    }

    private async Task<IReadOnlyList<DbMigration>> ReadMigrationsAsync(
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var inAssembly = SafeRead.Value(
            () => _migrations.GetInAssembly().ToArray(),
            [],
            static ex => $"Seznam migrací v assembly se nepodařilo načíst: {ex.Message}",
            warnings);

        IEnumerable<string> appliedMigrations;
        try
        {
            appliedMigrations = await _migrations.GetAppliedAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Databáze nemusí existovat nebo být dostupná — schéma z modelu je i tak platné.
            warnings.Add($"Aplikované migrace se nepodařilo načíst z databáze: {ex.Message}");
            appliedMigrations = [];
        }

        var applied = new HashSet<string>(appliedMigrations, StringComparer.Ordinal);
        var assemblySet = new HashSet<string>(inAssembly, StringComparer.Ordinal);

        var all = new SortedSet<string>(assemblySet, StringComparer.Ordinal);
        all.UnionWith(applied);

        return [.. all.Select(id => new DbMigration
        {
            Id = id,
            PresentInAssembly = assemblySet.Contains(id),
            AppliedInDatabase = applied.Contains(id),
        })];
    }
}
