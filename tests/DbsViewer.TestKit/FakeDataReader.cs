using System.Collections;
using System.Data.Common;

namespace DbsViewer.TestKit;

/// <summary>
/// Čtečka nad polem hodnot v paměti. Umožňuje testovat mapování řádků providerů
/// bez databáze — a tedy i pro SQL Server, který na testovacím stroji být nemusí.
/// </summary>
public sealed class FakeDataReader(params object?[][] rows) : DbDataReader
{
    private int _index = -1;

    private object?[] Current => rows[_index];

    public override bool Read()
    {
        _index++;
        return _index < rows.Length;
    }

    public override Task<bool> ReadAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Read());

    public override object GetValue(int ordinal) => Current[ordinal] ?? DBNull.Value;

    public override bool IsDBNull(int ordinal) => Current[ordinal] is null;

    public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken) =>
        Task.FromResult(IsDBNull(ordinal));

    public override int FieldCount => rows.Length > 0 ? rows[0].Length : 0;

    public override bool HasRows => rows.Length > 0;

    public override bool IsClosed => false;

    public override int Depth => 0;

    public override int RecordsAffected => 0;

    public override object this[int ordinal] => GetValue(ordinal);

    public override object this[string name] => throw new NotSupportedException();

    public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);

    public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);

    public override char GetChar(int ordinal) => (char)GetValue(ordinal);

    public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);

    public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);

    public override double GetDouble(int ordinal) => (double)GetValue(ordinal);

    public override float GetFloat(int ordinal) => (float)GetValue(ordinal);

    public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);

    public override short GetInt16(int ordinal) => (short)GetValue(ordinal);

    public override int GetInt32(int ordinal) => (int)GetValue(ordinal);

    public override long GetInt64(int ordinal) => (long)GetValue(ordinal);

    public override string GetString(int ordinal) => (string)GetValue(ordinal);

    public override Type GetFieldType(int ordinal) => GetValue(ordinal).GetType();

    public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;

    public override string GetName(int ordinal) => $"Column{ordinal}";

    public override int GetOrdinal(string name) => throw new NotSupportedException();

    public override int GetValues(object?[] values)
    {
        Array.Copy(Current, values, Current.Length);
        return Current.Length;
    }

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) =>
        throw new NotSupportedException();

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) =>
        throw new NotSupportedException();

    public override bool NextResult() => false;

    public override IEnumerator GetEnumerator() => rows.GetEnumerator();
}
