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
            Description = $"Created table {op.Name} "
                + $"({Cislovka(op.Columns.Count, "column", "columns")})",
            After = string.Join(", ", op.Columns.Select(static c => c.Name)),
        },

        DropTableOperation op => new DbSchemaChange
        {
            Kind = SchemaChangeKind.DropTable,
            Table = Name(op.Schema, op.Name),
            Description = $"Dropped table {op.Name}",
        },

        RenameTableOperation op => new DbSchemaChange
        {
            Kind = SchemaChangeKind.RenameTable,
            Table = Name(op.Schema, op.Name),
            Description = $"Table {op.Name} renamed to {op.NewName}",
            Before = op.Name,
            After = op.NewName,
        },

        AddColumnOperation op => new DbSchemaChange
        {
            Kind = SchemaChangeKind.AddColumn,
            Table = Name(op.Schema, op.Table),
            Object = op.Name,
            Description = $"Added column {op.Table}.{op.Name}",
            After = Sloupec(op.ColumnType, op.IsNullable, op.DefaultValueSql),
        },

        DropColumnOperation op => new DbSchemaChange
        {
            Kind = SchemaChangeKind.DropColumn,
            Table = Name(op.Schema, op.Table),
            Object = op.Name,
            Description = $"Dropped column {op.Table}.{op.Name}",
        },

        AlterColumnOperation op => new DbSchemaChange
        {
            Kind = SchemaChangeKind.AlterColumn,
            Table = Name(op.Schema, op.Table),
            Object = op.Name,
            Description = $"Changed column {op.Table}.{op.Name}",

            // OldColumn EF vždycky vyplní, i když v něm typ zůstane prázdný.
            Before = Sloupec(op.OldColumn.ColumnType, op.OldColumn.IsNullable, op.OldColumn.DefaultValueSql),
            After = Sloupec(op.ColumnType, op.IsNullable, op.DefaultValueSql),
        },

        RenameColumnOperation op => new DbSchemaChange
        {
            Kind = SchemaChangeKind.RenameColumn,
            Table = Name(op.Schema, op.Table),
            Object = op.Name,
            Description = $"Column {op.Table}.{op.Name} renamed to {op.NewName}",
            Before = op.Name,
            After = op.NewName,
        },

        CreateIndexOperation op => new DbSchemaChange
        {
            Kind = SchemaChangeKind.CreateIndex,
            Table = Name(op.Schema, op.Table),
            Object = op.Name,
            Description = $"Created {(op.IsUnique ? "unique index" : "index")} {op.Name}",
            After = string.Join(", ", op.Columns),
        },

        DropIndexOperation op => new DbSchemaChange
        {
            Kind = SchemaChangeKind.DropIndex,

            // Jako jediná operace nemusí tabulku znát: index se v některých
            // providerech ruší jen podle jména.
            Table = op.Table is { } tabulka ? Name(op.Schema, tabulka) : null,
            Object = op.Name,
            Description = $"Dropped index {op.Name}",
        },

        AddForeignKeyOperation op => new DbSchemaChange
        {
            Kind = SchemaChangeKind.AddForeignKey,
            Table = Name(op.Schema, op.Table),
            Object = op.Name,
            Description = $"Added foreign key {op.Table} → {op.PrincipalTable}",
            After = $"{string.Join(", ", op.Columns)} (ON DELETE {op.OnDelete})",
        },

        DropForeignKeyOperation op => new DbSchemaChange
        {
            Kind = SchemaChangeKind.DropForeignKey,
            Table = Name(op.Schema, op.Table),
            Object = op.Name,
            Description = $"Dropped foreign key {op.Name}",
        },

        AddPrimaryKeyOperation op => new DbSchemaChange
        {
            Kind = SchemaChangeKind.AddPrimaryKey,
            Table = Name(op.Schema, op.Table),
            Object = op.Name,
            Description = $"Added primary key of table {op.Table}",
            After = string.Join(", ", op.Columns),
        },

        DropPrimaryKeyOperation op => new DbSchemaChange
        {
            Kind = SchemaChangeKind.DropPrimaryKey,
            Table = Name(op.Schema, op.Table),
            Object = op.Name,
            Description = $"Dropped primary key of table {op.Table}",
        },

        // Vlastní SQL se analyzovat nedá; hlásí se, že v migraci je, aby uživatel věděl,
        // že popis změn nemusí být úplný.
        SqlOperation op => new DbSchemaChange
        {
            Kind = SchemaChangeKind.Sql,
            Description = "Custom SQL statement",
            After = Zkratit(op.Sql),
        },

        InsertDataOperation op => new DbSchemaChange
        {
            Kind = SchemaChangeKind.Data,
            Table = Name(op.Schema, op.Table),
            Description = $"Inserted data into {op.Table} "
                + $"({Cislovka(op.Values.GetLength(0), "row", "rows")})",
        },

        DeleteDataOperation op => new DbSchemaChange
        {
            Kind = SchemaChangeKind.Data,
            Table = Name(op.Schema, op.Table),
            Description = $"Deleted data from {op.Table} "
                + $"({Cislovka(op.KeyValues.GetLength(0), "row", "rows")})",
        },

        UpdateDataOperation op => new DbSchemaChange
        {
            Kind = SchemaChangeKind.Data,
            Table = Name(op.Schema, op.Table),
            Description = $"Changed data in {op.Table} "
                + $"({Cislovka(op.KeyValues.GetLength(0), "row", "rows")})",
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
    /// Počet s tvarem slova.
    /// </summary>
    /// <remarks>
    /// Popisy migrací skládá server a zůstávají anglické — slouží i HTTP API a nástroji
    /// <c>dbsview</c>, který jazyk prohlížečky nezná. Angličtina má dva tvary, takže tu
    /// stačí jednoduché pravidlo a nemusí se sem tahat <c>Plural</c> z UI, na kterém
    /// EfCore nezávisí.
    /// </remarks>
    private static string Cislovka(int pocet, string jedna, string vice) =>
        $"{pocet} {(pocet == 1 ? jedna : vice)}";
}
