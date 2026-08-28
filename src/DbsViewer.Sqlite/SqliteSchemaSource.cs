using System.Data.Common;
using DbsViewer.Relational;
using Microsoft.Data.Sqlite;

namespace DbsViewer.Sqlite;

/// <summary>
/// Čte schéma z živé SQLite databáze přes <c>sqlite_master</c> a příkazy <c>PRAGMA</c>.
/// </summary>
/// <remarks>
/// SQLite nemá systémové pohledy jako SQL Server. Metadata se získávají po jedné tabulce
/// příkazy <c>PRAGMA table_info</c>, <c>index_list</c>, <c>index_info</c>
/// a <c>foreign_key_list</c>, takže počet dotazů roste s počtem tabulek.
/// </remarks>
public sealed class SqliteSchemaSource : RelationalSchemaSource
{
    private readonly string _databaseName;

    /// <summary>Nad vlastním připojením vytvořeným z connection stringu.</summary>
    public SqliteSchemaSource(string connectionString, string? key = null)
        : base(() => new SqliteConnection(connectionString), ownsConnection: true, key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _databaseName = new SqliteConnectionStringBuilder(connectionString).DataSource;
    }

    /// <summary>Nad cizím připojením. Otevře se, pokud otevřené není, ale nikdy se nezavírá.</summary>
    public SqliteSchemaSource(DbConnection connection, string? key = null)
        : base(() => connection, ownsConnection: false, key)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _databaseName = connection.DataSource;
    }

    public override string DisplayName => $"SQLite ({_databaseName})";

    protected override DbProviderKind Provider => DbProviderKind.Sqlite;

    protected override string ProviderName => "Microsoft.Data.Sqlite";

    protected override async Task<RawSchema> ReadRawAsync(
        DbConnection connection,
        SchemaReadOptions options,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var reader = new SqliteRawReader(connection, cancellationToken);
        return await reader.ReadAsync(options, warnings).ConfigureAwait(false);
    }
}

/// <summary>Čtení metadat SQLite. Mapování řádků je oddělené, aby šlo testovat bez databáze.</summary>
internal sealed class SqliteRawReader(DbConnection connection, CancellationToken cancellationToken)
{
    public async Task<RawSchema> ReadAsync(SchemaReadOptions options, List<string> warnings)
    {
        var objects = await QueryRunner
            .ReadAllAsync(connection, SqliteQueries.Objects, MapObject, cancellationToken)
            .ConfigureAwait(false);

        var tables = new List<RawTable>();
        var columns = new List<RawColumn>();
        var keyColumns = new List<RawKeyColumn>();
        var indexes = new List<RawIndex>();
        var indexColumns = new List<RawIndexColumn>();
        var foreignKeys = new List<RawForeignKey>();
        var foreignKeyColumns = new List<RawForeignKeyColumn>();
        var rowCounts = new List<RawRowCount>();

        foreach (var (name, isView, createSql) in objects)
        {
            tables.Add(new RawTable(Schema: null, Name: name, IsView: isView));

            columns.AddRange(await ReadColumnsAsync(name, createSql).ConfigureAwait(false));
            keyColumns.AddRange(await ReadKeyColumnsAsync(name).ConfigureAwait(false));

            if (isView)
            {
                // Pohled v SQLite nemá indexy, klíče ani cizí klíče.
                continue;
            }

            var (tableIndexes, tableIndexColumns) = await ReadIndexesAsync(name).ConfigureAwait(false);
            indexes.AddRange(tableIndexes);
            indexColumns.AddRange(tableIndexColumns);

            var (tableForeignKeys, tableForeignKeyColumns) =
                await ReadForeignKeysAsync(name).ConfigureAwait(false);
            foreignKeys.AddRange(tableForeignKeys);
            foreignKeyColumns.AddRange(tableForeignKeyColumns);

            if (options.IncludeRowCounts)
            {
                rowCounts.Add(await ReadRowCountAsync(name).ConfigureAwait(false));
            }
        }

        return new RawSchema
        {
            DatabaseName = connection.DataSource,
            Tables = tables,
            Columns = columns,
            KeyColumns = keyColumns,
            Indexes = indexes,
            IndexColumns = indexColumns,
            ForeignKeys = foreignKeys,
            ForeignKeyColumns = foreignKeyColumns,
            RowCounts = rowCounts,
            AppliedMigrations = options.IncludeMigrations
                ? await ReadMigrationsAsync(warnings).ConfigureAwait(false)
                : [],
        };
    }

    private async Task<List<RawColumn>> ReadColumnsAsync(string table, string? createSql)
    {
        var rows = await QueryRunner
            .ReadAllAsync(connection, SqliteQueries.TableInfo(table), MapColumnInfo, cancellationToken)
            .ConfigureAwait(false);

        // Výraz generovaného sloupce PRAGMA nevrací, musí se vyčíst z původního CREATE TABLE.
        var generated = SqliteTypeParser.FindGeneratedColumns(createSql);
        var columns = new List<RawColumn>(rows.Count);

        foreach (var row in rows.Where(static r => !r.IsHiddenColumn))
        {
            var (maxLength, precision, scale) = SqliteTypeParser.ParseFacets(row.DeclaredType);
            generated.TryGetValue(row.Name, out var generatedColumn);

            columns.Add(new RawColumn(
                Schema: null,
                Table: table,
                Name: row.Name,
                Ordinal: row.Ordinal + 1,
                // Sloupec bez deklarovaného typu má v SQLite afinitu BLOB, ale tvrdit
                // konkrétní typ by vyrobilo falešný nález v diffu. Neznámý typ zůstává prázdný.
                StoreType: row.DeclaredType,
                IsNullable: !row.NotNull && !row.IsPrimaryKey && !row.IsGenerated,
                IsIdentity: false,
                IsComputed: row.IsGenerated,
                ComputedSql: generatedColumn?.Expression,
                IsStored: row.IsGenerated ? row.IsStoredGenerated : null,
                DefaultValueSql: row.DefaultValue,
                MaxLength: maxLength,
                Precision: precision,
                Scale: scale));
        }

        return columns;
    }

    private async Task<List<RawKeyColumn>> ReadKeyColumnsAsync(string table)
    {
        var rows = await QueryRunner
            .ReadAllAsync(connection, SqliteQueries.TableInfo(table), MapColumnInfo, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows
                .Where(static r => r.KeyPosition > 0)
                .OrderBy(static r => r.KeyPosition)
                .Select(r => new RawKeyColumn(
                    Schema: null,
                    Table: table,
                    ConstraintName: null,
                    Column: r.Name,
                    Position: r.KeyPosition)),
        ];
    }

    private async Task<(List<RawIndex> Indexes, List<RawIndexColumn> Columns)> ReadIndexesAsync(string table)
    {
        var list = await QueryRunner
            .ReadAllAsync(connection, SqliteQueries.IndexList(table), MapIndexListRow, cancellationToken)
            .ConfigureAwait(false);

        var indexes = new List<RawIndex>();
        var columns = new List<RawIndexColumn>();

        foreach (var row in list)
        {
            // Indexy vytvořené kvůli UNIQUE constraintu mají původ 'u' nebo 'pk'
            // a v modelu už jsou jako klíč nebo unikátní index z DDL.
            if (row.Origin is "pk")
            {
                continue;
            }

            indexes.Add(new RawIndex(
                Schema: null,
                Table: table,
                Name: row.Name,
                IsUnique: row.IsUnique,
                FilterSql: row.IsPartial ? SqliteTypeParser.NotAvailable : null));

            var info = await QueryRunner
                .ReadAllAsync(connection, SqliteQueries.IndexInfo(row.Name), MapIndexInfoRow, cancellationToken)
                .ConfigureAwait(false);

            columns.AddRange(info
                .Where(static i => i.Column is not null)
                .Select(i => new RawIndexColumn(
                    Schema: null,
                    Table: table,
                    IndexName: row.Name,
                    Column: i.Column!,
                    Position: i.Position + 1)));
        }

        return (indexes, columns);
    }

    private async Task<(List<RawForeignKey> Keys, List<RawForeignKeyColumn> Columns)>
        ReadForeignKeysAsync(string table)
    {
        var rows = await QueryRunner
            .ReadAllAsync(connection, SqliteQueries.ForeignKeyList(table), MapForeignKeyRow, cancellationToken)
            .ConfigureAwait(false);

        var keys = new List<RawForeignKey>();
        var columns = new List<RawForeignKeyColumn>();

        // PRAGMA vrací jeden řádek na sloupec; složený klíč sdílí Id.
        foreach (var group in rows.GroupBy(static r => r.Id))
        {
            var first = group.First();
            var name = SqliteTypeParser.ForeignKeyName(table, first.PrincipalTable, first.Id);

            keys.Add(new RawForeignKey(
                Schema: null,
                Table: table,
                Name: name,
                PrincipalSchema: null,
                PrincipalTable: first.PrincipalTable,
                DeleteAction: first.OnDelete));

            columns.AddRange(group
                .OrderBy(static r => r.Sequence)
                .Select(r => new RawForeignKeyColumn(
                    Schema: null,
                    Table: table,
                    ForeignKeyName: name,
                    Column: r.Column,
                    PrincipalColumn: r.PrincipalColumn ?? r.Column,
                    Position: r.Sequence + 1)));
        }

        return (keys, columns);
    }

    private async Task<RawRowCount> ReadRowCountAsync(string table)
    {
        var rows = await QueryRunner
            .ReadAllAsync(connection, SqliteQueries.RowCount(table), static r => r.GetLong(0), cancellationToken)
            .ConfigureAwait(false);

        return new RawRowCount(Schema: null, Table: table, Rows: rows.Count > 0 ? rows[0] : 0);
    }

    private async Task<List<string>> ReadMigrationsAsync(List<string> warnings)
    {
        try
        {
            var exists = await QueryRunner
                .ReadAllAsync(connection, SqliteQueries.MigrationsHistoryExists,
                    static r => r.GetInt(0), cancellationToken)
                .ConfigureAwait(false);

            if (exists.Count == 0 || exists[0] == 0)
            {
                return [];
            }

            return await QueryRunner
                .ReadAllAsync(connection, SqliteQueries.AppliedMigrations,
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

    internal static (string Name, bool IsView, string? CreateSql) MapObject(DbDataReader r) =>
        (r.GetText(0), r.GetText(1) == "view", r.GetTextOrNull(2));

    internal static SqliteColumnInfo MapColumnInfo(DbDataReader r) => new(
        Ordinal: r.GetInt(0),
        Name: r.GetText(1),
        DeclaredType: r.GetTextOrNull(2) ?? string.Empty,
        NotNull: r.GetBool(3),
        DefaultValue: r.GetTextOrNull(4),
        KeyPosition: r.GetInt(5),
        Hidden: r.GetInt(6));

    internal static SqliteIndexListRow MapIndexListRow(DbDataReader r) => new(
        Name: r.GetText(1),
        IsUnique: r.GetBool(2),
        Origin: r.GetTextOrNull(3) ?? "c",
        IsPartial: r.GetBool(4));

    internal static SqliteIndexInfoRow MapIndexInfoRow(DbDataReader r) => new(
        Position: r.GetInt(0),
        Column: r.GetTextOrNull(2));

    internal static SqliteForeignKeyRow MapForeignKeyRow(DbDataReader r) => new(
        Id: r.GetInt(0),
        Sequence: r.GetInt(1),
        PrincipalTable: r.GetText(2),
        Column: r.GetText(3),
        PrincipalColumn: r.GetTextOrNull(4),
        OnDelete: r.GetTextOrNull(6));
}

/// <summary>
/// Řádek z <c>PRAGMA table_xinfo</c>. Sloupec <c>hidden</c> rozlišuje běžný sloupec (0),
/// skrytý sloupec virtuální tabulky (1) a generovaný sloupec — počítaný za běhu (2)
/// nebo ukládaný (3).
/// </summary>
internal sealed record SqliteColumnInfo(
    int Ordinal,
    string Name,
    string DeclaredType,
    bool NotNull,
    string? DefaultValue,
    int KeyPosition,
    int Hidden = 0)
{
    public bool IsPrimaryKey => KeyPosition > 0;

    /// <summary>Sloupec generovaný databází, ať už ukládaný, nebo počítaný za běhu.</summary>
    public bool IsGenerated => Hidden is 2 or 3;

    /// <summary>Generovaný sloupec, jehož hodnota se ukládá.</summary>
    public bool IsStoredGenerated => Hidden == 3;

    /// <summary>Skrytý sloupec virtuální tabulky — do schématu nepatří.</summary>
    public bool IsHiddenColumn => Hidden == 1;
}

/// <summary>Řádek z <c>PRAGMA index_list</c>.</summary>
internal sealed record SqliteIndexListRow(string Name, bool IsUnique, string Origin, bool IsPartial);

/// <summary>Řádek z <c>PRAGMA index_info</c>. Sloupec je <c>null</c> u indexu nad výrazem.</summary>
internal sealed record SqliteIndexInfoRow(int Position, string? Column);

/// <summary>Řádek z <c>PRAGMA foreign_key_list</c>.</summary>
internal sealed record SqliteForeignKeyRow(
    int Id,
    int Sequence,
    string PrincipalTable,
    string Column,
    string? PrincipalColumn,
    string? OnDelete);
