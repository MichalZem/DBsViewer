using System.Text.Json.Serialization;

namespace DbsViewer;

/// <summary>
/// Kompletní popis databázového schématu. Jedna instance je to, co jde po drátě do UI
/// a co porovnává diff engine — nezávisle na tom, jestli vznikla z EF modelu nebo z živé databáze.
/// </summary>
public sealed class DatabaseSchema
{
    /// <summary>Jméno databáze, pokud ho jde zjistit.</summary>
    public string? DatabaseName { get; init; }

    /// <summary>Plný název EF providera, např. <c>Microsoft.EntityFrameworkCore.SqlServer</c>.</summary>
    public string? ProviderName { get; init; }

    /// <summary>Rozpoznaný typ providera.</summary>
    public DbProviderKind Provider { get; init; }

    /// <summary>Odkud schéma pochází.</summary>
    public SchemaSourceKind SourceKind { get; init; }

    /// <summary>Popisek zdroje pro UI, např. „EF model (ShopContext)".</summary>
    public string? SourceName { get; init; }

    /// <summary>Okamžik načtení. Slouží k invalidaci cache a k zobrazení stáří snapshotu.</summary>
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Výchozí schéma modelu (u SQL Serveru typicky <c>dbo</c>).</summary>
    public string? DefaultSchema { get; init; }

    public IReadOnlyList<DbTable> Tables { get; init; } = [];

    /// <summary>Vztahy odvozené z cizích klíčů a skip-navigací. To, co se kreslí v diagramu.</summary>
    public IReadOnlyList<DbRelationship> Relationships { get; init; } = [];

    public IReadOnlyList<DbMigration> Migrations { get; init; } = [];

    /// <summary>Co se nepodařilo načíst nebo odvodit. Zobrazuje se v UI, nikdy se nezahazuje potichu.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    [JsonIgnore]
    public int TableCount => Tables.Count;

    /// <summary>Vyhledání tabulky podle kvalifikovaného jména, bez ohledu na casing.</summary>
    public DbTable? FindTable(DbObjectName name)
    {
        foreach (var table in Tables)
        {
            if (table.Name == name)
            {
                return table;
            }
        }

        return null;
    }
}
