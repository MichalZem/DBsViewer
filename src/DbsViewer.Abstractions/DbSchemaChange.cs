namespace DbsViewer;

/// <summary>Druh změny, kterou migrace provádí.</summary>
public enum SchemaChangeKind
{
    /// <summary>Změna, kterou DbsViewer nezná jménem.</summary>
    Other,

    /// <summary>Vznikla tabulka.</summary>
    CreateTable,

    /// <summary>Tabulka zmizela.</summary>
    DropTable,

    /// <summary>Tabulka se přejmenovala.</summary>
    RenameTable,

    /// <summary>Přibyl sloupec.</summary>
    AddColumn,

    /// <summary>Sloupec zmizel.</summary>
    DropColumn,

    /// <summary>Sloupec změnil typ, nullability nebo default.</summary>
    AlterColumn,

    /// <summary>Sloupec se přejmenoval.</summary>
    RenameColumn,

    /// <summary>Vznikl index.</summary>
    CreateIndex,

    /// <summary>Index zmizel.</summary>
    DropIndex,

    /// <summary>Přibyl cizí klíč.</summary>
    AddForeignKey,

    /// <summary>Cizí klíč zmizel.</summary>
    DropForeignKey,

    /// <summary>Přibyl primární klíč.</summary>
    AddPrimaryKey,

    /// <summary>Primární klíč zmizel.</summary>
    DropPrimaryKey,

    /// <summary>Vlastní SQL příkaz.</summary>
    Sql,

    /// <summary>Vložení, změna nebo smazání dat.</summary>
    Data,
}

/// <summary>
/// Jedna změna schématu, kterou migrace provádí.
/// </summary>
/// <remarks>
/// Vzniká převodem operací migrace do podoby nezávislé na EF Core, aby s ní uměl
/// pracovat i WebAssembly klient, který EF nemá.
/// </remarks>
public sealed record DbSchemaChange
{
    /// <summary>Druh změny.</summary>
    public required SchemaChangeKind Kind { get; init; }

    /// <summary>Tabulka, které se změna týká. U vlastního SQL může chybět.</summary>
    public DbObjectName? Table { get; init; }

    /// <summary>Jméno měněného objektu — sloupce, indexu, klíče.</summary>
    public string? Object { get; init; }

    /// <summary>Popis změny v lidské řeči.</summary>
    public required string Description { get; init; }

    /// <summary>Stav před změnou, když ho migrace zaznamenává.</summary>
    public string? Before { get; init; }

    /// <summary>Stav po změně.</summary>
    public string? After { get; init; }

    /// <summary>
    /// Změna, jejíž dopad na schéma se nedá odvodit — vlastní SQL příkaz.
    /// </summary>
    /// <remarks>
    /// Hlásí se zvlášť, protože historie schématu je u takové migrace neúplná:
    /// snapshot ji zachytí jen tehdy, když ho autor migrace přegeneroval.
    /// </remarks>
    public bool IsOpaque => Kind == SchemaChangeKind.Sql;
}
