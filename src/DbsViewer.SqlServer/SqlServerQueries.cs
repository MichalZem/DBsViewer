namespace DbsViewer.SqlServer;

/// <summary>
/// Introspekční dotazy nad systémovými pohledy SQL Serveru.
/// </summary>
/// <remarks>
/// Dotazy jsou read-only a nikdy nepracují s uživatelským vstupem — filtrování tabulek
/// probíhá až nad výsledkem, v <see cref="SchemaReadOptions"/>. Systémová schémata
/// (<c>sys</c>, <c>INFORMATION_SCHEMA</c>) se vynechávají už v SQL.
/// </remarks>
internal static class SqlServerQueries
{
    private const string NotSystemSchema =
        "s.name NOT IN ('sys', 'INFORMATION_SCHEMA', 'guest', 'db_owner', 'db_accessadmin')";

    public const string Tables = $"""
        SELECT s.name AS SchemaName,
               t.name AS TableName,
               CAST(0 AS bit) AS IsView,
               CAST(ep.value AS nvarchar(max)) AS Comment
        FROM sys.tables t
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        LEFT JOIN sys.extended_properties ep
               ON ep.major_id = t.object_id
              AND ep.minor_id = 0
              AND ep.class = 1
              AND ep.name = 'MS_Description'
        WHERE t.is_ms_shipped = 0 AND {NotSystemSchema}
        UNION ALL
        SELECT s.name, v.name, CAST(1 AS bit), CAST(ep.value AS nvarchar(max))
        FROM sys.views v
        JOIN sys.schemas s ON s.schema_id = v.schema_id
        LEFT JOIN sys.extended_properties ep
               ON ep.major_id = v.object_id
              AND ep.minor_id = 0
              AND ep.class = 1
              AND ep.name = 'MS_Description'
        WHERE v.is_ms_shipped = 0 AND {NotSystemSchema}
        """;

    public const string Columns = $"""
        SELECT s.name AS SchemaName,
               o.name AS TableName,
               c.name AS ColumnName,
               c.column_id AS Ordinal,
               CASE
                   WHEN ty.name IN ('nvarchar', 'nchar')
                        THEN ty.name + '(' + CASE WHEN c.max_length = -1 THEN 'max'
                                                  ELSE CAST(c.max_length / 2 AS varchar(10)) END + ')'
                   WHEN ty.name IN ('varchar', 'char', 'varbinary', 'binary')
                        THEN ty.name + '(' + CASE WHEN c.max_length = -1 THEN 'max'
                                                  ELSE CAST(c.max_length AS varchar(10)) END + ')'
                   WHEN ty.name IN ('decimal', 'numeric')
                        THEN ty.name + '(' + CAST(c.precision AS varchar(10)) + ',' + CAST(c.scale AS varchar(10)) + ')'
                   WHEN ty.name IN ('datetime2', 'datetimeoffset', 'time')
                        THEN ty.name + '(' + CAST(c.scale AS varchar(10)) + ')'
                   ELSE ty.name
               END AS StoreType,
               c.is_nullable AS IsNullable,
               c.is_identity AS IsIdentity,
               c.is_computed AS IsComputed,
               cc.definition AS ComputedSql,
               cc.is_persisted AS IsStored,
               dc.definition AS DefaultSql,
               CASE WHEN ty.name IN ('nvarchar', 'nchar') AND c.max_length > 0 THEN c.max_length / 2
                    WHEN ty.name IN ('varchar', 'char', 'varbinary', 'binary') AND c.max_length > 0 THEN c.max_length
                    ELSE NULL END AS MaxLength,
               CASE WHEN ty.name IN ('decimal', 'numeric') THEN c.precision ELSE NULL END AS Precision,
               CASE WHEN ty.name IN ('decimal', 'numeric') THEN c.scale ELSE NULL END AS Scale,
               c.collation_name AS Collation,
               CAST(ep.value AS nvarchar(max)) AS Comment
        FROM sys.columns c
        JOIN sys.objects o ON o.object_id = c.object_id
        JOIN sys.schemas s ON s.schema_id = o.schema_id
        JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        LEFT JOIN sys.computed_columns cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
        LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id
        LEFT JOIN sys.extended_properties ep
               ON ep.major_id = c.object_id
              AND ep.minor_id = c.column_id
              AND ep.class = 1
              AND ep.name = 'MS_Description'
        WHERE o.type IN ('U', 'V') AND o.is_ms_shipped = 0 AND {NotSystemSchema}
        """;

    public const string PrimaryKeyColumns = $"""
        SELECT s.name AS SchemaName,
               t.name AS TableName,
               kc.name AS ConstraintName,
               c.name AS ColumnName,
               ic.key_ordinal AS Position,
               CASE WHEN i.type = 1 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS IsClustered
        FROM sys.key_constraints kc
        JOIN sys.tables t ON t.object_id = kc.parent_object_id
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        JOIN sys.indexes i ON i.object_id = kc.parent_object_id AND i.index_id = kc.unique_index_id
        JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE kc.type = 'PK' AND t.is_ms_shipped = 0 AND {NotSystemSchema}
        """;

    public const string Indexes = $"""
        SELECT s.name AS SchemaName,
               t.name AS TableName,
               i.name AS IndexName,
               i.is_unique AS IsUnique,
               CASE WHEN i.type = 1 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS IsClustered,
               i.filter_definition AS FilterSql
        FROM sys.indexes i
        JOIN sys.tables t ON t.object_id = i.object_id
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        WHERE i.name IS NOT NULL
          AND i.is_primary_key = 0
          AND i.is_hypothetical = 0
          AND t.is_ms_shipped = 0
          AND {NotSystemSchema}
        """;

    public const string IndexColumns = $"""
        SELECT s.name AS SchemaName,
               t.name AS TableName,
               i.name AS IndexName,
               c.name AS ColumnName,
               CASE WHEN ic.is_included_column = 1 THEN ic.index_column_id ELSE ic.key_ordinal END AS Position,
               ic.is_descending_key AS IsDescending,
               ic.is_included_column AS IsIncluded
        FROM sys.index_columns ic
        JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
        JOIN sys.tables t ON t.object_id = i.object_id
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE i.name IS NOT NULL
          AND i.is_primary_key = 0
          AND i.is_hypothetical = 0
          AND t.is_ms_shipped = 0
          AND {NotSystemSchema}
        """;

    public const string ForeignKeys = $"""
        SELECT s.name AS SchemaName,
               t.name AS TableName,
               fk.name AS ForeignKeyName,
               ps.name AS PrincipalSchema,
               pt.name AS PrincipalTable,
               fk.delete_referential_action_desc AS DeleteAction
        FROM sys.foreign_keys fk
        JOIN sys.tables t ON t.object_id = fk.parent_object_id
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        JOIN sys.tables pt ON pt.object_id = fk.referenced_object_id
        JOIN sys.schemas ps ON ps.schema_id = pt.schema_id
        WHERE t.is_ms_shipped = 0 AND {NotSystemSchema}
        """;

    public const string ForeignKeyColumns = $"""
        SELECT s.name AS SchemaName,
               t.name AS TableName,
               fk.name AS ForeignKeyName,
               c.name AS ColumnName,
               pc.name AS PrincipalColumn,
               fkc.constraint_column_id AS Position
        FROM sys.foreign_key_columns fkc
        JOIN sys.foreign_keys fk ON fk.object_id = fkc.constraint_object_id
        JOIN sys.tables t ON t.object_id = fkc.parent_object_id
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        JOIN sys.columns c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
        JOIN sys.columns pc ON pc.object_id = fkc.referenced_object_id AND pc.column_id = fkc.referenced_column_id
        WHERE t.is_ms_shipped = 0 AND {NotSystemSchema}
        """;

    public const string CheckConstraints = $"""
        SELECT s.name AS SchemaName,
               t.name AS TableName,
               cc.name AS ConstraintName,
               cc.definition AS Sql
        FROM sys.check_constraints cc
        JOIN sys.tables t ON t.object_id = cc.parent_object_id
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        WHERE t.is_ms_shipped = 0 AND {NotSystemSchema}
        """;

    /// <summary>
    /// Odhad počtu řádků ze statistik. Nikdy <c>COUNT(*)</c> — na velké tabulce
    /// by prohlížečka schématu zatížila produkční databázi.
    /// </summary>
    public const string RowCounts = $"""
        SELECT s.name AS SchemaName,
               t.name AS TableName,
               SUM(p.rows) AS Rows
        FROM sys.tables t
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
        WHERE t.is_ms_shipped = 0 AND {NotSystemSchema}
        GROUP BY s.name, t.name
        """;

    public const string AppliedMigrations = """
        SELECT MigrationId
        FROM __EFMigrationsHistory
        ORDER BY MigrationId
        """;

    public const string MigrationsHistoryExists = """
        SELECT CASE WHEN OBJECT_ID('__EFMigrationsHistory', 'U') IS NULL THEN 0 ELSE 1 END
        """;
}
