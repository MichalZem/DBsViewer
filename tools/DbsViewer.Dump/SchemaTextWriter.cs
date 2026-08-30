using System.Text;
using DbsViewer.Analysis;

namespace DbsViewer.Dump;

/// <summary>
/// Textový výpis schématu do konzole. Slouží k rychlé kontrole, že čtení modelu
/// vrací to, co se čeká — než bude existovat grafické UI.
/// </summary>
public static class SchemaTextWriter
{
    public static string Render(DatabaseSchema schema)
    {
        var output = new StringBuilder();

        output.AppendLine($"Databáze  : {schema.DatabaseName ?? "(neznámá)"}");
        output.AppendLine($"Provider  : {schema.Provider} ({schema.ProviderName})");
        output.AppendLine($"Zdroj     : {schema.SourceName} [{schema.SourceKind}]");
        output.AppendLine($"Načteno   : {schema.GeneratedAtUtc:u}");
        output.AppendLine($"Tabulek   : {schema.Tables.Count}, vztahů: {schema.Relationships.Count}");
        output.AppendLine();

        foreach (var table in schema.Tables)
        {
            RenderTable(output, table);
        }

        RenderRelationships(output, schema);
        RenderMigrations(output, schema);
        RenderWarnings(output, schema);

        return output.ToString();
    }

    /// <summary>Výpis nálezů porovnání modelu proti databázi.</summary>
    public static string RenderDiff(SchemaDiff diff, DatabaseSchema model, DatabaseSchema database)
    {
        var output = new StringBuilder();

        output.AppendLine($"Model    : {model.SourceName} — {model.Tables.Count} tabulek");
        output.AppendLine($"Databáze : {database.SourceName} — {database.Tables.Count} tabulek");
        output.AppendLine();

        if (diff.IsClean)
        {
            output.AppendLine("✓ Model a databáze se shodují.");
            return output.ToString();
        }

        output.AppendLine($"Nálezů: {diff.ErrorCount} chyb, {diff.WarningCount} varování");
        output.AppendLine();

        foreach (var group in diff.Findings.GroupBy(static f => f.Severity))
        {
            output.AppendLine(group.Key switch
            {
                DiffSeverity.Error => "── Chyby ──",
                DiffSeverity.Warning => "── Varování ──",
                _ => "── Informace ──",
            });

            foreach (var finding in group)
            {
                var where = finding.Table is { } table
                    ? $"{table}{(finding.Object is null ? "" : "." + finding.Object)}"
                    : finding.Object ?? "(schéma)";

                output.AppendLine($"  {where}");
                output.AppendLine($"    {finding.Message}");

                if (finding.ModelValue is not null || finding.DatabaseValue is not null)
                {
                    output.AppendLine(
                        $"    model: {finding.ModelValue ?? "—"}   databáze: {finding.DatabaseValue ?? "—"}");
                }
            }

            output.AppendLine();
        }

        return output.ToString();
    }

    private static void RenderTable(StringBuilder output, DbTable table)
    {
        var flags = new List<string>();
        if (table.IsView)
        {
            flags.Add("VIEW");
        }

        if (table.IsJoinTable)
        {
            flags.Add("JOIN");
        }

        if (table.EntityClrNames.Count > 0)
        {
            flags.Add(string.Join("+", table.EntityClrNames));
        }

        if (table.DiscriminatorColumn is { } discriminator)
        {
            flags.Add($"TPH:{discriminator}");
        }

        if (table.RowCountEstimate is { } rows)
        {
            flags.Add($"~{Ui.Model.Cestina.Radky(rows)}");
        }

        output.AppendLine($"■ {table.Qualified}{(flags.Count > 0 ? $"   [{string.Join(" · ", flags)}]" : "")}");

        if (table.Comment is { } comment)
        {
            output.AppendLine($"  „{comment}\"");
        }

        foreach (var column in table.Columns)
        {
            var marks = new StringBuilder();
            marks.Append(column.IsPrimaryKey ? "PK" : "  ");
            marks.Append(column.IsForeignKey ? " FK" : "   ");

            var extras = new List<string>();
            if (column.IsIdentity)
            {
                extras.Add("identity");
            }

            if (column.IsComputed)
            {
                extras.Add($"computed: {column.ComputedSql}");
            }

            if (column.DefaultValueSql is { } defaultSql)
            {
                extras.Add($"default: {defaultSql}");
            }

            if (column.IsConcurrencyToken)
            {
                extras.Add("concurrency");
            }

            output.AppendLine(
                $"  {marks} {column.Name,-24} {column.StoreType,-18} "
                + $"{(column.IsNullable ? "NULL" : "NOT NULL"),-8}"
                + $"{(extras.Count > 0 ? $"  {string.Join(", ", extras)}" : "")}");
        }

        foreach (var index in table.Indexes)
        {
            output.AppendLine(
                $"    ⌗ {(index.IsUnique ? "UNIQUE " : "")}{index.Name} ({string.Join(", ", index.Columns)})"
                + $"{(index.FilterSql is { } f ? $" WHERE {f}" : "")}");
        }

        foreach (var check in table.CheckConstraints)
        {
            output.AppendLine($"    ✓ {check.Name}: {check.Sql}");
        }

        output.AppendLine();
    }

    private static void RenderRelationships(StringBuilder output, DatabaseSchema schema)
    {
        if (schema.Relationships.Count == 0)
        {
            return;
        }

        output.AppendLine("── Vztahy ──");
        foreach (var relationship in schema.Relationships)
        {
            var arrow = relationship.Cardinality switch
            {
                DbCardinality.ManyToMany => "N:M",
                DbCardinality.OneToOne => "1:1",
                _ => "1:N",
            };

            var via = relationship.ViaJoinTable is { } join ? $" via {join}" : "";
            var identifying = relationship.IsIdentifying ? " [identifying]" : "";
            var self = relationship.IsSelfReference ? " [self]" : "";

            output.AppendLine(
                $"  {arrow}  {relationship.To} ← {relationship.From}{via}"
                + $"  onDelete={relationship.DeleteBehavior}{identifying}{self}");
        }

        output.AppendLine();
    }

    private static void RenderMigrations(StringBuilder output, DatabaseSchema schema)
    {
        if (schema.Migrations.Count == 0)
        {
            return;
        }

        output.AppendLine("── Migrace ──");
        foreach (var migration in schema.Migrations)
        {
            var state = migration switch
            {
                { IsPending: true } => "čeká na nasazení",
                { IsOrphaned: true } => "chybí v assembly",
                _ => "aplikovaná",
            };

            output.AppendLine($"  {migration.Id}  ({state})");
        }

        output.AppendLine();
    }

    private static void RenderWarnings(StringBuilder output, DatabaseSchema schema)
    {
        if (schema.Warnings.Count == 0)
        {
            return;
        }

        output.AppendLine("── Upozornění ──");
        foreach (var warning in schema.Warnings)
        {
            output.AppendLine($"  ! {warning}");
        }

        output.AppendLine();
    }
}
