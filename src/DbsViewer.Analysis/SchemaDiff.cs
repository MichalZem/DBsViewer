namespace DbsViewer.Analysis;

/// <summary>Závažnost nálezu při porovnání dvou schémat.</summary>
public enum DiffSeverity
{
    /// <summary>Rozdíl, který se za běhu projeví chybou nebo tichým poškozením dat.</summary>
    Error,

    /// <summary>Rozdíl, který stojí za pozornost, ale sám o sobě nic nerozbíjí.</summary>
    Warning,

    /// <summary>Informace, ne problém.</summary>
    Info,
}

/// <summary>Čeho se nález týká.</summary>
public enum DiffKind
{
    TableMissingInDatabase,
    TableMissingInModel,
    ColumnMissingInDatabase,
    ColumnMissingInModel,
    ColumnTypeMismatch,
    ColumnNullabilityMismatch,
    ColumnLengthMismatch,
    ColumnDefaultMismatch,
    IndexMissingInDatabase,
    IndexMissingInModel,
    IndexUniquenessMismatch,
    IndexColumnsMismatch,
    PrimaryKeyMismatch,
    ForeignKeyMissingInDatabase,
    ForeignKeyMissingInModel,
    ForeignKeyDeleteBehaviorMismatch,
    ForeignKeyTargetMismatch,
    MigrationPending,
    MigrationOrphaned,
}

/// <summary>Jeden nález porovnání.</summary>
public sealed record DiffFinding
{
    public required DiffKind Kind { get; init; }

    public required DiffSeverity Severity { get; init; }

    /// <summary>Tabulka, ke které nález patří. U migrací <c>null</c>.</summary>
    public DbObjectName? Table { get; init; }

    /// <summary>Sloupec, index nebo cizí klíč, kterého se nález týká.</summary>
    public string? Object { get; init; }

    /// <summary>Věta pro uživatele, česky a konkrétně.</summary>
    public required string Message { get; init; }

    /// <summary>Co říká EF model.</summary>
    public string? ModelValue { get; init; }

    /// <summary>Co je skutečně v databázi.</summary>
    public string? DatabaseValue { get; init; }

    public override string ToString() => Table is { } table
        ? $"[{Severity}] {table}{(Object is null ? "" : "." + Object)}: {Message}"
        : $"[{Severity}] {Message}";
}

/// <summary>Výsledek porovnání EF modelu proti živé databázi.</summary>
public sealed record SchemaDiff
{
    public required IReadOnlyList<DiffFinding> Findings { get; init; }

    public DateTimeOffset ComparedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Schémata se shodují — žádný nález závažnosti chyba ani varování.</summary>
    public bool IsClean => !Findings.Any(static f => f.Severity != DiffSeverity.Info);

    public int ErrorCount => Findings.Count(static f => f.Severity == DiffSeverity.Error);

    public int WarningCount => Findings.Count(static f => f.Severity == DiffSeverity.Warning);

    /// <summary>Nálezy pro jednu tabulku, kvůli zvýraznění uzlu v diagramu.</summary>
    public IReadOnlyList<DiffFinding> ForTable(DbObjectName table) =>
        [.. Findings.Where(f => f.Table == table)];

    /// <summary>Nejvyšší závažnost nálezu u tabulky, nebo <c>null</c>, když je čistá.</summary>
    public DiffSeverity? SeverityOf(DbObjectName table)
    {
        DiffSeverity? worst = null;

        foreach (var finding in Findings)
        {
            if (finding.Table == table && (worst is null || finding.Severity < worst))
            {
                worst = finding.Severity;
            }
        }

        return worst;
    }
}
