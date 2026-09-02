using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace DbsViewer.EfCore;

/// <summary>
/// Převod operací migrace na popis nezávislý na EF Core.
/// </summary>
/// <remarks>
/// EF má desítky typů operací; tenhle převod pokrývá ty, které mění strukturu schématu,
/// a zbytek shrne obecně. Popisy jsou české, protože je uživatel čte přímo v UI —
/// stejně jako u nálezů diffu.
/// </remarks>
internal static class MigrationOperationDescriber
{
    /// <summary>Popíše jednu operaci migrace.</summary>
    internal static DbSchemaChange Describe(MigrationOperation operation) => operation switch
    {
        CreateTableOperation op => new DbSchemaChange
        {
            Kind = SchemaChangeKind.CreateTable,
            Table = Name(op.Schema, op.Name),
            Description = $"Vytvořena tabulka {op.Name} "
                + $"({Cislovka(op.Columns.Count, "sloupec", "sloupce", "sloupců")})",
            After = string.Join(", ", op.Columns.Select(static c => c.Name)),
        },

        DropTableOperation op => new DbSchemaChange
        {
            Kind = SchemaChangeKind.DropTable,
            Table = Name(op.Schema, op.Name),
            Description = $"Odstraněna tabulka {op.Name}",
        },

        RenameTableOperation op => new DbSchemaChange
        {
            Kind = SchemaChangeKind.RenameTable,
            Table = Name(op.Schema, op.Name),
            Description = $"Tabulka {op.Name} přejmenována na {op.NewName}",
            Before = op.Name,
            After = op.NewName,
        },

        AddColumnOperation op => new DbSchemaChange
        {
            Kind = SchemaChangeKind.AddColumn,
            Table = Name(op.Schema, op.Table),
            Object = op.Name,
            Description = $"Přidán sloupec {op.Table}.{op.Name}",
            After = Sloupec(op.ColumnType, op.IsNullable, op.DefaultValueSql),
        },

        DropColumnOperation op => new DbSchemaChange
        {
            Kind = SchemaChangeKind.DropColumn,
            Table = Name(op.Schema, op.Table),
            Object = op.Name,
            Description = $"Odstraněn sloupec {op.Table}.{op.Name}",
        },

        AlterColumnOperation op => new DbSchemaChange
        {
            Kind = SchemaChangeKind.AlterColumn,
            Table = Name(op.Schema, op.Table),
            Object = op.Name,
            Description = $"Změněn sloupec {op.Table}.{op.Name}",

            // OldColumn EF vždycky vyplní, i když v něm typ zůstane prázdný.
            Before = Sloupec(op.OldColumn.ColumnType, op.OldColumn.IsNullable, op.OldColumn.DefaultValueSql),
            After = Sloupec(op.ColumnType, op.IsNullable, op.DefaultValueSql),
        },

        RenameColumnOperation op => new DbSchemaChange
        {
            Kind = SchemaChangeKind.RenameColumn,
            Table = Name(op.Schema, op.Table),
            Object = op.Name,
            Description = $"Sloupec {op.Table}.{op.Name} přejmenován na {op.NewName}",
            Before = op.Name,
            After = op.NewName,
        },

        CreateIndexOperation op => new DbSchemaChange
        {
            Kind = SchemaChangeKind.CreateIndex,
            Table = Name(op.Schema, op.Table),
            Object = op.Name,
            Description = $"Vytvořen {(op.IsUnique ? "unikátní index" : "index")} {op.Name}",
            After = string.Join(", ", op.Columns),
        },

        DropIndexOperation op => new DbSchemaChange
        {
            Kind = SchemaChangeKind.DropIndex,
            Table = Name(op.Schema, op.Table),
            Object = op.Name,
            Description = $"Odstraněn index {op.Name}",
        },

        AddForeignKeyOperation op => new DbSchemaChange
        {
            Kind = SchemaChangeKind.AddForeignKey,
            Table = Name(op.Schema, op.Table),
            Object = op.Name,
            Description = $"Přidán cizí klíč {op.Table} → {op.PrincipalTable}",
            After = $"{string.Join(", ", op.Columns)} (ON DELETE {op.OnDelete})",
        },

        DropForeignKeyOperation op => new DbSchemaChange
        {
            Kind = SchemaChangeKind.DropForeignKey,
            Table = Name(op.Schema, op.Table),
            Object = op.Name,
            Description = $"Odstraněn cizí klíč {op.Name}",
        },

        AddPrimaryKeyOperation op => new DbSchemaChange
        {
            Kind = SchemaChangeKind.AddPrimaryKey,
            Table = Name(op.Schema, op.Table),
            Object = op.Name,
            Description = $"Přidán primární klíč tabulky {op.Table}",
            After = string.Join(", ", op.Columns),
        },

        DropPrimaryKeyOperation op => new DbSchemaChange
        {
            Kind = SchemaChangeKind.DropPrimaryKey,
            Table = Name(op.Schema, op.Table),
            Object = op.Name,
            Description = $"Odstraněn primární klíč tabulky {op.Table}",
        },

        // Vlastní SQL se analyzovat nedá; hlásí se, že v migraci je, aby uživatel věděl,
        // že popis změn nemusí být úplný.
        SqlOperation op => new DbSchemaChange
        {
            Kind = SchemaChangeKind.Sql,
            Description = "Vlastní SQL příkaz",
            After = Zkratit(op.Sql),
        },

        InsertDataOperation op => new DbSchemaChange
        {
            Kind = SchemaChangeKind.Data,
            Table = Name(op.Schema, op.Table),
            Description = $"Vložena data do {op.Table} "
                + $"({Cislovka(op.Values.GetLength(0), "řádek", "řádky", "řádků")})",
        },

        DeleteDataOperation op => new DbSchemaChange
        {
            Kind = SchemaChangeKind.Data,
            Table = Name(op.Schema, op.Table),
            Description = $"Smazána data z {op.Table} "
                + $"({Cislovka(op.KeyValues.GetLength(0), "řádek", "řádky", "řádků")})",
        },

        UpdateDataOperation op => new DbSchemaChange
        {
            Kind = SchemaChangeKind.Data,
            Table = Name(op.Schema, op.Table),
            Description = $"Změněna data v {op.Table} "
                + $"({Cislovka(op.KeyValues.GetLength(0), "řádek", "řádky", "řádků")})",
        },

        _ => new DbSchemaChange
        {
            Kind = SchemaChangeKind.Other,

            // Jméno typu bez přípony „Operation" je pořád srozumitelnější než nic.
            Description = operation.GetType().Name.Replace("Operation", "", StringComparison.Ordinal),
        },
    };

    private static DbObjectName Name(string? schema, string table) => new(schema, table);

    private static string Sloupec(string? typ, bool nullable, string? defaultSql)
    {
        var popis = $"{typ}{(nullable ? ", NULL" : ", NOT NULL")}";

        return defaultSql is { Length: > 0 } ? $"{popis}, DEFAULT {defaultSql}" : popis;
    }

    /// <summary>Dlouhý SQL příkaz se do popisu nevejde; ukáže se začátek.</summary>
    private static string Zkratit(string sql)
    {
        var jednoradkove = string.Join(' ', sql.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(static r => r.Trim()));

        return jednoradkove.Length <= 120 ? jednoradkove : jednoradkove[..117] + "…";
    }

    /// <summary>
    /// Skloňování počtu. Duplikuje pravidlo z UI, protože Abstractions ani EfCore
    /// na UI nezávisí — a závislost opačným směrem by byla horší než tenhle převod.
    /// </summary>
    private static string Cislovka(int pocet, string jedna, string dveAzCtyri, string petAVice)
    {
        var tvar = pocet switch
        {
            1 => jedna,
            >= 2 and <= 4 => dveAzCtyri,
            _ => petAVice,
        };

        return $"{pocet} {tvar}";
    }
}
