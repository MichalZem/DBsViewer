using System.Data.Common;
using DbsViewer.Relational;
using Microsoft.Data.SqlClient;

namespace DbsViewer.SqlServer;

/// <summary>
/// Čte schéma z živé Microsoft SQL Server databáze přes systémové pohledy <c>sys.*</c>.
/// </summary>
public sealed class SqlServerSchemaSource : RelationalSchemaSource
{
    private readonly string _databaseName;

    /// <summary>Nad vlastním připojením vytvořeným z connection stringu.</summary>
    public SqlServerSchemaSource(string connectionString, string? key = null)
        : base(() => new SqlConnection(connectionString), ownsConnection: true, key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _databaseName = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
    }

    /// <summary>
    /// Nad cizím připojením — typicky tím, které používá <c>DbContext</c>.
    /// Připojení se otevře, pokud otevřené není, ale nikdy se nezavírá.
    /// </summary>
    public SqlServerSchemaSource(DbConnection connection, string? key = null)
        : base(() => connection, ownsConnection: false, key)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _databaseName = connection.Database;
    }

    public override string DisplayName => $"SQL Server ({_databaseName})";

    protected override DbProviderKind Provider => DbProviderKind.SqlServer;

    protected override string ProviderName => "Microsoft.Data.SqlClient";

    protected override async Task<RawSchema> ReadRawAsync(
        DbConnection connection,
        SchemaReadOptions options,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var reader = new SqlServerRawReader(connection, cancellationToken);

        return new RawSchema
        {
            DatabaseName = connection.Database,
            DefaultSchema = "dbo",
            Tables = await reader.TablesAsync().ConfigureAwait(false),
            Columns = await reader.ColumnsAsync().ConfigureAwait(false),
            KeyColumns = await reader.PrimaryKeyColumnsAsync().ConfigureAwait(false),
            Indexes = await reader.IndexesAsync().ConfigureAwait(false),
            IndexColumns = await reader.IndexColumnsAsync().ConfigureAwait(false),
            ForeignKeys = await reader.ForeignKeysAsync().ConfigureAwait(false),
            ForeignKeyColumns = await reader.ForeignKeyColumnsAsync().ConfigureAwait(false),
            CheckConstraints = await reader.CheckConstraintsAsync().ConfigureAwait(false),
            RowCounts = options.IncludeRowCounts
                ? await reader.RowCountsAsync().ConfigureAwait(false)
                : [],
            AppliedMigrations = options.IncludeMigrations
                ? await reader.AppliedMigrationsAsync(warnings).ConfigureAwait(false)
                : [],
        };
    }
}

/// <summary>Spouštění dotazů a mapování řádků. Mapování je oddělené, aby šlo testovat bez databáze.</summary>
internal sealed class SqlServerRawReader(DbConnection connection, CancellationToken cancellationToken)
{
    public Task<List<RawTable>> TablesAsync() =>
        QueryRunner.ReadAllAsync(connection, SqlServerQueries.Tables, MapTable, cancellationToken);

    public Task<List<RawColumn>> ColumnsAsync() =>
        QueryRunner.ReadAllAsync(connection, SqlServerQueries.Columns, MapColumn, cancellationToken);

    public Task<List<RawKeyColumn>> PrimaryKeyColumnsAsync() =>
        QueryRunner.ReadAllAsync(connection, SqlServerQueries.PrimaryKeyColumns, MapKeyColumn, cancellationToken);

    public Task<List<RawIndex>> IndexesAsync() =>
        QueryRunner.ReadAllAsync(connection, SqlServerQueries.Indexes, MapIndex, cancellationToken);

    public Task<List<RawIndexColumn>> IndexColumnsAsync() =>
        QueryRunner.ReadAllAsync(connection, SqlServerQueries.IndexColumns, MapIndexColumn, cancellationToken);

    public Task<List<RawForeignKey>> ForeignKeysAsync() =>
        QueryRunner.ReadAllAsync(connection, SqlServerQueries.ForeignKeys, MapForeignKey, cancellationToken);

    public Task<List<RawForeignKeyColumn>> ForeignKeyColumnsAsync() =>
        QueryRunner.ReadAllAsync(
            connection, SqlServerQueries.ForeignKeyColumns, MapForeignKeyColumn, cancellationToken);

    public Task<List<RawCheckConstraint>> CheckConstraintsAsync() =>
        QueryRunner.ReadAllAsync(connection, SqlServerQueries.CheckConstraints, MapCheck, cancellationToken);

    public Task<List<RawRowCount>> RowCountsAsync() =>
        QueryRunner.ReadAllAsync(connection, SqlServerQueries.RowCounts, MapRowCount, cancellationToken);

    /// <summary>
    /// Historie migrací. Tabulka nemusí existovat — u databáze spravované jinak než přes EF
    /// je to normální stav, ne chyba.
    /// </summary>
    public async Task<List<string>> AppliedMigrationsAsync(List<string> warnings)
    {
        try
        {
            var exists = await QueryRunner
                .ReadAllAsync(connection, SqlServerQueries.MigrationsHistoryExists,
                    static r => r.GetInt(0) == 1, cancellationToken)
                .ConfigureAwait(false);

            if (exists.Count == 0 || !exists[0])
            {
                return [];
            }

            return await QueryRunner
                .ReadAllAsync(connection, SqlServerQueries.AppliedMigrations,
                    static r => r.GetText(0), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            warnings.Add($"Historii migrací se nepodařilo přečíst: {ex.Message}");
            return [];
        }
    }

    // ---------- mapování ----------

    internal static RawTable MapTable(DbDataReader r) => new(
        Schema: r.GetTextOrNull(0),
        Name: r.GetText(1),
        IsView: r.GetBool(2),
        Comment: r.GetTextOrNull(3));

    internal static RawColumn MapColumn(DbDataReader r) => new(
        Schema: r.GetTextOrNull(0),
        Table: r.GetText(1),
        Name: r.GetText(2),
        Ordinal: r.GetInt(3),
        StoreType: r.GetText(4),
        IsNullable: r.GetBool(5),
        IsIdentity: r.GetBool(6),
        IsComputed: r.GetBool(7),
        ComputedSql: r.GetTextOrNull(8),
        IsStored: r.GetBoolOrNull(9),
        DefaultValueSql: r.GetTextOrNull(10),
        MaxLength: r.GetIntOrNull(11),
        Precision: r.GetIntOrNull(12),
        Scale: r.GetIntOrNull(13),
        Collation: r.GetTextOrNull(14),
        Comment: r.GetTextOrNull(15));

    internal static RawKeyColumn MapKeyColumn(DbDataReader r) => new(
        Schema: r.GetTextOrNull(0),
        Table: r.GetText(1),
        ConstraintName: r.GetTextOrNull(2),
        Column: r.GetText(3),
        Position: r.GetInt(4),
        IsClustered: r.GetBoolOrNull(5));

    internal static RawIndex MapIndex(DbDataReader r) => new(
        Schema: r.GetTextOrNull(0),
        Table: r.GetText(1),
        Name: r.GetText(2),
        IsUnique: r.GetBool(3),
        IsClustered: r.GetBoolOrNull(4),
        FilterSql: r.GetTextOrNull(5));

    internal static RawIndexColumn MapIndexColumn(DbDataReader r) => new(
        Schema: r.GetTextOrNull(0),
        Table: r.GetText(1),
        IndexName: r.GetText(2),
        Column: r.GetText(3),
        Position: r.GetInt(4),
        IsDescending: r.GetBool(5),
        IsIncluded: r.GetBool(6));

    internal static RawForeignKey MapForeignKey(DbDataReader r) => new(
        Schema: r.GetTextOrNull(0),
        Table: r.GetText(1),
        Name: r.GetText(2),
        PrincipalSchema: r.GetTextOrNull(3),
        PrincipalTable: r.GetText(4),
        DeleteAction: r.GetTextOrNull(5));

    internal static RawForeignKeyColumn MapForeignKeyColumn(DbDataReader r) => new(
        Schema: r.GetTextOrNull(0),
        Table: r.GetText(1),
        ForeignKeyName: r.GetText(2),
        Column: r.GetText(3),
        PrincipalColumn: r.GetText(4),
        Position: r.GetInt(5));

    internal static RawCheckConstraint MapCheck(DbDataReader r) => new(
        Schema: r.GetTextOrNull(0),
        Table: r.GetText(1),
        Name: r.GetText(2),
        Sql: r.GetTextOrNull(3));

    internal static RawRowCount MapRowCount(DbDataReader r) => new(
        Schema: r.GetTextOrNull(0),
        Table: r.GetText(1),
        Rows: r.GetLong(2));
}
