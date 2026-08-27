using System.Text.Json.Serialization;

namespace DbsViewer;

/// <summary>
/// Kvalifikované jméno databázového objektu. Porovnává se bez ohledu na velikost písmen,
/// protože EF model a živá databáze se v casingu běžně liší.
/// </summary>
public readonly record struct DbObjectName : IComparable<DbObjectName>
{
    public DbObjectName(string? schema, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Schema = string.IsNullOrEmpty(schema) ? null : schema;
        Name = name;
    }

    /// <summary>Schéma objektu, nebo <c>null</c> u databází bez schémat (SQLite).</summary>
    public string? Schema { get; init; }

    /// <summary>Jméno objektu bez schématu.</summary>
    public string Name { get; init; }

    /// <summary>Jméno ve tvaru <c>schema.name</c>, případně jen <c>name</c>.</summary>
    [JsonIgnore]
    public string Qualified => Schema is null ? Name : $"{Schema}.{Name}";

    public bool Equals(DbObjectName other) =>
        string.Equals(Schema, other.Schema, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => HashCode.Combine(
        Schema is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Schema),
        StringComparer.OrdinalIgnoreCase.GetHashCode(Name));

    public int CompareTo(DbObjectName other)
    {
        var bySchema = string.Compare(Schema, other.Schema, StringComparison.OrdinalIgnoreCase);
        return bySchema != 0 ? bySchema : string.Compare(Name, other.Name, StringComparison.OrdinalIgnoreCase);
    }

    public override string ToString() => Qualified;
}
