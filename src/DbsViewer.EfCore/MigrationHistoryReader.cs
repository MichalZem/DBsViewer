using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace DbsViewer.EfCore;

/// <summary>
/// Historie schématu z EF migrací: co která migrace změnila a jak schéma vypadalo
/// po jejím provedení.
/// </summary>
/// <remarks>
/// Stojí na dvou věcech, které EF drží v assembly aplikace:
///
/// **Operace migrace** (<c>UpOperations</c>) říkají, co migrace deklaruje — přidání
/// sloupce, index, cizí klíč. Jsou to typované objekty, ne text, takže se dají přeložit
/// do čitelného popisu.
///
/// **Snapshot modelu** (<c>TargetModel</c>) nese celé schéma po provedení migrace.
/// Díky němu se dá zobrazit stav k libovolnému bodu historie a porovnat dvě verze
/// stejným porovnávačem, jaký se používá na drift proti živé databázi.
///
/// Obojí je čistě v paměti, bez dotazu do databáze. Které migrace jsou skutečně
/// aplikované, řeší <see cref="Internal.IMigrationsReader"/>.
/// </remarks>
public sealed class MigrationHistoryReader
{
    private readonly IMigrationsAssembly _assembly;
    private readonly IModelRuntimeInitializer _initializer;
    private readonly string _activeProvider;
    private readonly DbContext _context;

    /// <param name="context">Kontext aplikace. Migrace se hledají v jeho assembly.</param>
    public MigrationHistoryReader(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _assembly = context.GetService<IMigrationsAssembly>();
        _initializer = context.GetService<IModelRuntimeInitializer>();
        _activeProvider = context.GetService<IDatabaseProvider>().Name;
    }

    /// <summary>Identifikátory migrací v assembly, v pořadí, v jakém se aplikují.</summary>
    public IReadOnlyList<string> Ids => [.. _assembly.Migrations.Keys];

    /// <summary>Zná assembly tuhle migraci?</summary>
    public bool Has(string migrationId) =>
        migrationId is not null && _assembly.Migrations.ContainsKey(migrationId);

    /// <summary>
    /// Změny, které migrace provádí.
    /// </summary>
    /// <param name="migrationId">Identifikátor migrace.</param>
    /// <returns>Prázdný seznam, když migrace v assembly není.</returns>
    public IReadOnlyList<DbSchemaChange> GetChanges(string migrationId)
    {
        if (!TryCreate(migrationId, out var migration))
        {
            return [];
        }

        return [.. migration.UpOperations.Select(MigrationOperationDescriber.Describe)];
    }

    /// <summary>
    /// Schéma tak, jak vypadalo po provedení zadané migrace.
    /// </summary>
    /// <param name="migrationId">Identifikátor migrace.</param>
    /// <param name="options">Co se má číst a co skrýt.</param>
    /// <exception cref="InvalidOperationException">Migrace v assembly není.</exception>
    public DatabaseSchema ReadAt(string migrationId, SchemaReadOptions? options = null)
    {
        if (!TryCreate(migrationId, out var migration))
        {
            throw new InvalidOperationException(
                $"Migrace {migrationId} není v assembly aplikace, takže k ní schéma neexistuje. "
                + "Zobrazit jde jen historie migrací, jejichž kód je v projektu.");
        }

        options ??= SchemaReadOptions.Default;
        var warnings = new List<string>();

        // Snapshot je surový model — před čtením se musí doinicializovat, jinak
        // GetRelationalModel() vyhodí výjimku.
        var model = _initializer.Initialize(migration.TargetModel, designTime: true);

        var schema = new EfModelReader(_context, options, warnings, model).Read([]);

        return schema with
        {
            SourceKind = SchemaSourceKind.MigrationSnapshot,
            SourceName = $"Migrace {migrationId}",
            Warnings = [.. schema.Warnings, .. warnings],

            // Snapshot je stav v minulosti; seznam migrací k němu patřící sestavuje
            // volající, protože ví, které jsou aplikované.
            Migrations = [],
        };
    }

    private bool TryCreate(string migrationId, out Migration migration)
    {
        migration = null!;

        if (migrationId is null || !_assembly.Migrations.TryGetValue(migrationId, out var typeInfo))
        {
            return false;
        }

        migration = _assembly.CreateMigration(typeInfo, _activeProvider);
        return true;
    }
}
