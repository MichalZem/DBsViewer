using System.Text;

namespace DbsViewer.Ui.Model;

/// <summary>Formát exportu schématu.</summary>
public enum ExportFormat
{
    /// <summary>Mermaid <c>erDiagram</c> — vykreslí se v GitHubu i v dokumentaci.</summary>
    Mermaid,

    /// <summary>DBML pro dbdiagram.io.</summary>
    Dbml,

    /// <summary>Markdown dokumentace k zapsání do repozitáře.</summary>
    Markdown,
}

/// <summary>
/// Převod schématu do textových formátů. Umožňuje verzovat popis databáze v repozitáři
/// a nechat si ho zobrazit v nástrojích, které DbsViewer neznají.
/// </summary>
public static class SchemaExporter
{
    /// <summary>Vyexportuje schéma do zvoleného formátu.</summary>
    public static string Export(DatabaseSchema schema, ExportFormat format)
    {
        ArgumentNullException.ThrowIfNull(schema);

        return format switch
        {
            ExportFormat.Mermaid => ToMermaid(schema),
            ExportFormat.Dbml => ToDbml(schema),
            _ => ToMarkdown(schema),
        };
    }

    /// <summary>Přípona souboru pro daný formát.</summary>
    public static string FileExtension(ExportFormat format) => format switch
    {
        ExportFormat.Mermaid => "mmd",
        ExportFormat.Dbml => "dbml",
        _ => "md",
    };

    // ---------- Mermaid ----------

    private static string ToMermaid(DatabaseSchema schema)
    {
        var output = new StringBuilder();
        output.AppendLine("erDiagram");

        foreach (var table in schema.Tables)
        {
            output.AppendLine($"    {Identifier(table.Name)} {{");

            foreach (var column in table.Columns)
            {
                var key = column switch
                {
                    { IsPrimaryKey: true } => " PK",
                    { IsForeignKey: true } => " FK",
                    _ => "",
                };

                output.AppendLine($"        {MermaidType(column.StoreType)} {Identifier(column.Name)}{key}");
            }

            output.AppendLine("    }");
        }

        foreach (var relationship in schema.Relationships)
        {
            var notation = relationship.Cardinality switch
            {
                DbCardinality.ManyToMany => "}o--o{",
                DbCardinality.OneToOne => relationship.IsRequired ? "||--||" : "||--o|",
                _ => relationship.IsRequired ? "||--|{" : "||--o{",
            };

            var label = relationship.FromNavigation ?? relationship.ForeignKeyName ?? "vazba";

            output.AppendLine(
                $"    {Identifier(relationship.To)} {notation} {Identifier(relationship.From)} : \"{label}\"");
        }

        return output.ToString();
    }

    /// <summary>Mermaid nesnáší v typu závorky ani mezery, takže se z nich stanou podtržítka.</summary>
    private static string MermaidType(string storeType)
    {
        if (string.IsNullOrWhiteSpace(storeType))
        {
            return "unknown";
        }

        var buffer = new StringBuilder(storeType.Length);

        foreach (var character in storeType)
        {
            buffer.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        return buffer.ToString();
    }

    // ---------- DBML ----------

    private static string ToDbml(DatabaseSchema schema)
    {
        var output = new StringBuilder();

        if (schema.DatabaseName is { } name)
        {
            output.AppendLine($"// Databáze: {name}");
        }

        output.AppendLine($"// Vygenerováno DbsViewerem {schema.GeneratedAtUtc:yyyy-MM-dd HH:mm} UTC");
        output.AppendLine();

        foreach (var table in schema.Tables)
        {
            output.AppendLine($"Table \"{table.Qualified}\" {{");

            foreach (var column in table.Columns)
            {
                var settings = new List<string>();

                if (column.IsPrimaryKey)
                {
                    settings.Add("pk");
                }

                if (column.IsIdentity)
                {
                    settings.Add("increment");
                }

                if (!column.IsNullable)
                {
                    settings.Add("not null");
                }

                if (column.Comment is { } comment)
                {
                    settings.Add($"note: '{Escape(comment)}'");
                }

                var suffix = settings.Count > 0 ? $" [{string.Join(", ", settings)}]" : "";
                output.AppendLine($"  \"{column.Name}\" \"{column.StoreType}\"{suffix}");
            }

            if (table.Comment is { } tableComment)
            {
                output.AppendLine($"  Note: '{Escape(tableComment)}'");
            }

            output.AppendLine("}");
            output.AppendLine();
        }

        foreach (var relationship in schema.Relationships)
        {
            if (relationship.FromColumns.Count == 0 || relationship.ToColumns.Count == 0)
            {
                continue;
            }

            var notation = relationship.Cardinality switch
            {
                DbCardinality.ManyToMany => "<>",
                DbCardinality.OneToOne => "-",
                _ => ">",
            };

            output.AppendLine(
                $"Ref: \"{relationship.From.Qualified}\".\"{relationship.FromColumns[0]}\" "
                + $"{notation} \"{relationship.To.Qualified}\".\"{relationship.ToColumns[0]}\"");
        }

        return output.ToString();
    }

    // ---------- Markdown ----------

    private static string ToMarkdown(DatabaseSchema schema)
    {
        var output = new StringBuilder();

        output.AppendLine($"# Schéma databáze {schema.DatabaseName ?? ""}".TrimEnd());
        output.AppendLine();
        output.AppendLine($"Provider: {schema.Provider}  ");
        output.AppendLine($"Vygenerováno: {schema.GeneratedAtUtc:yyyy-MM-dd HH:mm} UTC  ");
        output.AppendLine($"Tabulek: {schema.Tables.Count}, vazeb: {schema.Relationships.Count}");
        output.AppendLine();

        foreach (var table in schema.Tables)
        {
            output.AppendLine($"## {table.Qualified}");
            output.AppendLine();

            if (table.Comment is { } comment)
            {
                output.AppendLine($"> {comment}");
                output.AppendLine();
            }

            output.AppendLine("| Sloupec | Typ | Null | Klíč | Poznámka |");
            output.AppendLine("|---|---|---|---|---|");

            foreach (var column in table.Columns)
            {
                var key = column switch
                {
                    { IsPrimaryKey: true, IsForeignKey: true } => "PK, FK",
                    { IsPrimaryKey: true } => "PK",
                    { IsForeignKey: true } => "FK",
                    _ => "",
                };

                var notes = new List<string>();

                if (column.IsIdentity)
                {
                    notes.Add("identity");
                }

                if (column.IsComputed)
                {
                    notes.Add("computed");
                }

                if (column.DefaultValueSql is { } defaultSql)
                {
                    notes.Add($"default `{defaultSql}`");
                }

                if (column.Comment is { } columnComment)
                {
                    notes.Add(columnComment);
                }

                output.AppendLine(
                    $"| {column.Name} | `{column.StoreType}` | {(column.IsNullable ? "ano" : "ne")} "
                    + $"| {key} | {string.Join(", ", notes)} |");
            }

            output.AppendLine();

            if (table.Indexes.Count > 0)
            {
                output.AppendLine("**Indexy**");
                output.AppendLine();

                foreach (var index in table.Indexes)
                {
                    var unique = index.IsUnique ? "UNIQUE " : "";
                    output.AppendLine($"- {unique}`{index.Name}` ({string.Join(", ", index.Columns)})");
                }

                output.AppendLine();
            }

            if (table.ForeignKeys.Count > 0)
            {
                output.AppendLine("**Cizí klíče**");
                output.AppendLine();

                foreach (var foreignKey in table.ForeignKeys)
                {
                    output.AppendLine(
                        $"- `{foreignKey.Name}`: ({string.Join(", ", foreignKey.Columns)}) → "
                        + $"{foreignKey.PrincipalTable.Qualified} "
                        + $"({string.Join(", ", foreignKey.PrincipalColumns)}), "
                        + $"onDelete {foreignKey.DeleteBehavior}");
                }

                output.AppendLine();
            }
        }

        return output.ToString();
    }

    /// <summary>Jméno bezpečné pro Mermaid — ten v identifikátorech tečky a mezery nesnáší.</summary>
    private static string Identifier(DbObjectName name) => Identifier(name.Qualified);

    private static string Identifier(string value)
    {
        var buffer = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            buffer.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
        }

        return buffer.Length == 0 ? "_" : buffer.ToString();
    }

    private static string Escape(string value) =>
        value.Replace("'", "\\'", StringComparison.Ordinal);
}
