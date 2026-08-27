using System.Data;
using System.Data.Common;

namespace DbsViewer.Relational;

/// <summary>
/// Spouštění introspekčních dotazů. Odděleno od providerů, aby se dotazy daly psát
/// jako dvojice „SQL + mapování řádku" a mapování šlo testovat bez připojení k databázi.
/// </summary>
public static class QueryRunner
{
    /// <summary>Přečte všechny řádky dotazu a namapuje je.</summary>
    public static async Task<List<T>> ReadAllAsync<T>(
        DbConnection connection,
        string sql,
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrEmpty(sql);
        ArgumentNullException.ThrowIfNull(map);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var rows = new List<T>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(map(reader));
        }

        return rows;
    }

    /// <summary>
    /// Otevře připojení, pokud ještě otevřené není, a vrátí příznak, jestli ho má volající zavřít.
    /// Cizí připojení se nikdy nezavírá — mohl by ho používat někdo jiný.
    /// </summary>
    public static async Task<bool> EnsureOpenAsync(
        DbConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (connection.State == ConnectionState.Open)
        {
            return false;
        }

        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}

/// <summary>
/// Držení otevřeného připojení po dobu operace. Zavře ho jen tehdy, když ho samo otevřelo —
/// cizí připojení patří volajícímu.
/// </summary>
public sealed class ConnectionScope : IAsyncDisposable
{
    private readonly DbConnection _connection;
    private readonly bool _closeOnDispose;

    private ConnectionScope(DbConnection connection, bool closeOnDispose)
    {
        _connection = connection;
        _closeOnDispose = closeOnDispose;
    }

    /// <summary>Otevře připojení, pokud otevřené není.</summary>
    public static async Task<ConnectionScope> OpenAsync(
        DbConnection connection,
        CancellationToken cancellationToken = default)
    {
        var openedHere = await QueryRunner.EnsureOpenAsync(connection, cancellationToken).ConfigureAwait(false);
        return new ConnectionScope(connection, openedHere);
    }

    public async ValueTask DisposeAsync()
    {
        if (_closeOnDispose)
        {
            await _connection.CloseAsync().ConfigureAwait(false);
        }
    }
}

/// <summary>Čtení hodnot z <see cref="DbDataReader"/> se zacházením s NULL.</summary>
public static class DataReaderExtensions
{
    public static string GetText(this DbDataReader reader, int ordinal) =>
        reader.GetValue(ordinal) as string ?? reader.GetValue(ordinal).ToString() ?? string.Empty;

    public static string? GetTextOrNull(this DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetText(ordinal);

    public static bool GetBool(this DbDataReader reader, int ordinal) =>
        !reader.IsDBNull(ordinal) && Convert.ToBoolean(reader.GetValue(ordinal), Culture.Invariant);

    public static bool? GetBoolOrNull(this DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToBoolean(reader.GetValue(ordinal), Culture.Invariant);

    public static int GetInt(this DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal), Culture.Invariant);

    public static int? GetIntOrNull(this DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal), Culture.Invariant);

    public static long GetLong(this DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0L : Convert.ToInt64(reader.GetValue(ordinal), Culture.Invariant);
}

/// <summary>Kultura pro převody hodnot z databáze. Vždy invariantní, nikdy uživatelská.</summary>
internal static class Culture
{
    public static IFormatProvider Invariant => System.Globalization.CultureInfo.InvariantCulture;
}
