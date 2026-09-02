namespace DbsViewer.Analysis;

/// <summary>
/// Srovnání jmen objektů před párováním dvou schémat.
/// </summary>
/// <remarks>
/// EF model tabulku bez explicitního <c>ToTable(..., schema)</c> hlásí bez schématu,
/// kdežto databáze ji vrátí jako <c>dbo.Neco</c>. Bez srovnání by se taková tabulka
/// objevila dvakrát: jednou jako <c>Neco</c>, podruhé jako <c>dbo.Neco</c> — a diff by
/// hlásil, že chybí v modelu i v databázi zároveň.
///
/// Doplňuje se proto výchozí schéma, a to z libovolného ze zdrojů: model ho většinou
/// nezná, databáze ano.
/// </remarks>
public static class SchemaNames
{
    /// <summary>
    /// Doplní tabulkám bez schématu výchozí schéma.
    /// </summary>
    /// <param name="schema">Schéma ke srovnání.</param>
    /// <param name="defaultSchema">
    /// Výchozí schéma. Bez zadání se vezme z vlastního schématu; když ho nezná ani to,
    /// nemění se nic — u SQLite žádná schémata nejsou a doplňovat je nemá co.
    /// </param>
    public static DatabaseSchema Normalize(DatabaseSchema schema, string? defaultSchema = null)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var vychozi = defaultSchema ?? schema.DefaultSchema;

        if (vychozi is not { Length: > 0 })
        {
            return schema;
        }

        return schema with
        {
            Tables = [.. schema.Tables.Select(t => Normalize(t, vychozi))],
            Relationships = [.. schema.Relationships.Select(r => Normalize(r, vychozi))],
        };
    }

    /// <summary>Doplní jménu výchozí schéma, pokud žádné nemá.</summary>
    public static DbObjectName Normalize(DbObjectName name, string? defaultSchema) =>
        name.Schema is { Length: > 0 } || defaultSchema is not { Length: > 0 }
            ? name
            : new DbObjectName(defaultSchema, name.Name);

    private static DbTable Normalize(DbTable table, string defaultSchema) => table with
    {
        Name = Normalize(table.Name, defaultSchema),
        ForeignKeys =
        [
            .. table.ForeignKeys.Select(fk => fk with
            {
                PrincipalTable = Normalize(fk.PrincipalTable, defaultSchema),
            }),
        ],
    };

    private static DbRelationship Normalize(DbRelationship relationship, string defaultSchema) =>
        relationship with
        {
            From = Normalize(relationship.From, defaultSchema),
            To = Normalize(relationship.To, defaultSchema),
            ViaJoinTable = relationship.ViaJoinTable is { } via
                ? Normalize(via, defaultSchema)
                : null,
        };
}
