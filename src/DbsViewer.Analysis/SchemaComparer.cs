namespace DbsViewer.Analysis;

/// <summary>
/// Porovná EF model proti živé databázi a vrátí seznam rozdílů.
/// </summary>
/// <remarks>
/// Pracuje nad dvěma instancemi <see cref="DatabaseSchema"/> a nezná ani EF, ani SQL.
/// Porovnává se podle kvalifikovaných jmen, bez ohledu na velikost písmen — obojí
/// se v casingu běžně liší, aniž by šlo o rozdíl.
/// </remarks>
public static class SchemaComparer
{
    /// <summary>Porovná model s databází.</summary>
    /// <param name="model">Schéma z EF modelu.</param>
    /// <param name="database">Schéma z živé databáze.</param>
    /// <param name="options">Co se má a nemá hlásit.</param>
    public static SchemaDiff Compare(
        DatabaseSchema model,
        DatabaseSchema database,
        DiffOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(database);

        options ??= DiffOptions.Default;
        var findings = new List<DiffFinding>();

        CompareTables(model, database, options, findings);
        CompareMigrations(model, findings);

        findings.Sort(static (a, b) =>
        {
            var bySeverity = a.Severity.CompareTo(b.Severity);
            if (bySeverity != 0)
            {
                return bySeverity;
            }

            var byTable = Nullable.Compare(a.Table, b.Table);
            return byTable != 0 ? byTable : string.CompareOrdinal(a.Object, b.Object);
        });

        return new SchemaDiff { Findings = findings };
    }

    private static void CompareTables(
        DatabaseSchema model,
        DatabaseSchema database,
        DiffOptions options,
        List<DiffFinding> findings)
    {
        var databaseTables = database.Tables.ToDictionary(static t => t.Name);

        foreach (var modelTable in model.Tables)
        {
            if (!databaseTables.TryGetValue(modelTable.Name, out var databaseTable))
            {
                findings.Add(new DiffFinding
                {
                    Kind = DiffKind.TableMissingInDatabase,
                    Severity = DiffSeverity.Error,
                    Table = modelTable.Name,
                    Message = "Tabulka je v modelu, ale v databázi chybí. Nejspíš neaplikovaná migrace.",
                });
                continue;
            }

            CompareColumns(modelTable, databaseTable, options, findings);

            if (modelTable.IsView || databaseTable.IsView)
            {
                // Pohled nemá klíče, indexy ani cizí klíče.
                continue;
            }

            ComparePrimaryKey(modelTable, databaseTable, findings);
            CompareIndexes(modelTable, databaseTable, findings);
            CompareForeignKeys(modelTable, databaseTable, findings);
        }

        var modelTables = new HashSet<DbObjectName>(model.Tables.Select(static t => t.Name));

        foreach (var databaseTable in database.Tables)
        {
            if (modelTables.Contains(databaseTable.Name) || options.IsIgnored(databaseTable.Name))
            {
                continue;
            }

            findings.Add(new DiffFinding
            {
                Kind = DiffKind.TableMissingInModel,
                Severity = DiffSeverity.Warning,
                Table = databaseTable.Name,
                Message = "Tabulka je v databázi, ale v modelu není. Legacy objekt nebo cizí schéma.",
            });
        }
    }

    private static void CompareColumns(
        DbTable model,
        DbTable database,
        DiffOptions options,
        List<DiffFinding> findings)
    {
        var isView = model.IsView || database.IsView;

        foreach (var modelColumn in model.Columns)
        {
            var databaseColumn = database.FindColumn(modelColumn.Name);

            if (databaseColumn is null)
            {
                findings.Add(new DiffFinding
                {
                    Kind = DiffKind.ColumnMissingInDatabase,
                    Severity = DiffSeverity.Error,
                    Table = model.Name,
                    Object = modelColumn.Name,
                    Message = "Sloupec je v modelu, ale v databázi chybí.",
                });
                continue;
            }

            if (isView)
            {
                // Pohled atributy sloupců nedeklaruje — plynou z dotazu a databáze
                // je často vůbec nevystavuje. Porovnává se tedy jen jejich existence.
                continue;
            }

            CompareColumn(model.Name, modelColumn, databaseColumn, options, findings);
        }

        foreach (var databaseColumn in database.Columns)
        {
            if (model.FindColumn(databaseColumn.Name) is not null)
            {
                continue;
            }

            findings.Add(new DiffFinding
            {
                Kind = DiffKind.ColumnMissingInModel,
                Severity = DiffSeverity.Warning,
                Table = model.Name,
                Object = databaseColumn.Name,
                Message = "Sloupec je v databázi, ale v modelu není. EF s ním nepracuje.",
                DatabaseValue = databaseColumn.StoreType,
            });
        }
    }

    private static void CompareColumn(
        DbObjectName table,
        DbColumn model,
        DbColumn database,
        DiffOptions options,
        List<DiffFinding> findings)
    {
        if (options.CompareStoreTypes && !StoreTypesMatch(model.StoreType, database.StoreType))
        {
            findings.Add(new DiffFinding
            {
                Kind = DiffKind.ColumnTypeMismatch,
                Severity = DiffSeverity.Error,
                Table = table,
                Object = model.Name,
                Message = "Typ sloupce se liší. Hrozí tichý ořez hodnoty nebo výjimka za běhu.",
                ModelValue = model.StoreType,
                DatabaseValue = database.StoreType,
            });
        }

        if (model.IsNullable != database.IsNullable)
        {
            findings.Add(new DiffFinding
            {
                Kind = DiffKind.ColumnNullabilityMismatch,
                Severity = DiffSeverity.Error,
                Table = table,
                Object = model.Name,
                Message = model.IsNullable
                    ? "Model sloupec připouští NULL, databáze ne. Uložení prázdné hodnoty selže."
                    : "Databáze sloupec připouští NULL, model ne. Načtení NULL vyhodí výjimku.",
                ModelValue = model.IsNullable ? "NULL" : "NOT NULL",
                DatabaseValue = database.IsNullable ? "NULL" : "NOT NULL",
            });
        }

        if (options.CompareLengths && model.MaxLength is { } modelLength
            && database.MaxLength is { } databaseLength
            && modelLength != databaseLength)
        {
            findings.Add(new DiffFinding
            {
                Kind = DiffKind.ColumnLengthMismatch,
                Severity = databaseLength < modelLength ? DiffSeverity.Error : DiffSeverity.Warning,
                Table = table,
                Object = model.Name,
                Message = databaseLength < modelLength
                    ? "Sloupec je v databázi kratší než v modelu. Delší hodnota se neuloží."
                    : "Sloupec je v databázi delší než v modelu.",
                ModelValue = modelLength.ToString(),
                DatabaseValue = databaseLength.ToString(),
            });
        }

        if (options.CompareDefaults && !DefaultsMatch(model.DefaultValueSql, database.DefaultValueSql))
        {
            findings.Add(new DiffFinding
            {
                Kind = DiffKind.ColumnDefaultMismatch,
                Severity = DiffSeverity.Warning,
                Table = table,
                Object = model.Name,
                Message = "Defaultní hodnota se liší.",
                ModelValue = model.DefaultValueSql ?? "(žádná)",
                DatabaseValue = database.DefaultValueSql ?? "(žádná)",
            });
        }
    }

    private static void ComparePrimaryKey(DbTable model, DbTable database, List<DiffFinding> findings)
    {
        var modelColumns = model.PrimaryKey?.Columns ?? [];
        var databaseColumns = database.PrimaryKey?.Columns ?? [];

        if (ColumnsMatch(modelColumns, databaseColumns))
        {
            return;
        }

        findings.Add(new DiffFinding
        {
            Kind = DiffKind.PrimaryKeyMismatch,
            Severity = DiffSeverity.Error,
            Table = model.Name,
            Message = "Primární klíč se liší.",
            ModelValue = Describe(modelColumns),
            DatabaseValue = Describe(databaseColumns),
        });
    }

    private static void CompareIndexes(DbTable model, DbTable database, List<DiffFinding> findings)
    {
        var databaseIndexes = database.Indexes.ToDictionary(
            static i => i.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var modelIndex in model.Indexes)
        {
            if (!databaseIndexes.TryGetValue(modelIndex.Name, out var databaseIndex))
            {
                findings.Add(new DiffFinding
                {
                    Kind = DiffKind.IndexMissingInDatabase,
                    Severity = DiffSeverity.Warning,
                    Table = model.Name,
                    Object = modelIndex.Name,
                    Message = "Index je v modelu, ale v databázi chybí. Dotazy nad ním poběží pomalu.",
                    ModelValue = Describe(modelIndex.Columns),
                });
                continue;
            }

            if (modelIndex.IsUnique != databaseIndex.IsUnique)
            {
                findings.Add(new DiffFinding
                {
                    Kind = DiffKind.IndexUniquenessMismatch,
                    Severity = DiffSeverity.Error,
                    Table = model.Name,
                    Object = modelIndex.Name,
                    Message = modelIndex.IsUnique
                        ? "Model index považuje za unikátní, databáze ne. Duplicita projde."
                        : "Databáze index vynucuje jako unikátní, model ne. Uložení duplicity selže.",
                    ModelValue = modelIndex.IsUnique ? "UNIQUE" : "NEUNIKÁTNÍ",
                    DatabaseValue = databaseIndex.IsUnique ? "UNIQUE" : "NEUNIKÁTNÍ",
                });
            }

            if (!ColumnsMatch(modelIndex.Columns, databaseIndex.Columns))
            {
                findings.Add(new DiffFinding
                {
                    Kind = DiffKind.IndexColumnsMismatch,
                    Severity = DiffSeverity.Warning,
                    Table = model.Name,
                    Object = modelIndex.Name,
                    Message = "Sloupce indexu se liší.",
                    ModelValue = Describe(modelIndex.Columns),
                    DatabaseValue = Describe(databaseIndex.Columns),
                });
            }
        }

        var modelIndexes = new HashSet<string>(
            model.Indexes.Select(static i => i.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var databaseIndex in database.Indexes)
        {
            if (modelIndexes.Contains(databaseIndex.Name))
            {
                continue;
            }

            findings.Add(new DiffFinding
            {
                Kind = DiffKind.IndexMissingInModel,
                Severity = DiffSeverity.Warning,
                Table = model.Name,
                Object = databaseIndex.Name,
                Message = "Index je v databázi, ale v modelu není. Ručně doladěný výkon mimo migrace.",
                DatabaseValue = Describe(databaseIndex.Columns),
            });
        }
    }

    private static void CompareForeignKeys(DbTable model, DbTable database, List<DiffFinding> findings)
    {
        // Páruje se podle sloupců, ne podle jména: SQLite jména cizích klíčů vůbec
        // nevystavuje a skládají se podle konvence, která se s EF nemusí trefit.
        //
        // Nad stejnými sloupci ale může být víc klíčů — tentýž sloupec smí odkazovat
        // na dvě různé tabulky. Proto seznam kandidátů, ne slovník: první shodu
        // spotřebujeme a další klíč se spáruje s tou zbylou.
        var databaseKeys = new List<DbForeignKey>(database.ForeignKeys);

        foreach (var modelKey in model.ForeignKeys)
        {
            var databaseKey = TakeMatching(databaseKeys, modelKey);

            if (databaseKey is null)
            {
                findings.Add(new DiffFinding
                {
                    Kind = DiffKind.ForeignKeyMissingInDatabase,
                    Severity = DiffSeverity.Error,
                    Table = model.Name,
                    Object = modelKey.Name,
                    Message = "Cizí klíč je v modelu, ale v databázi chybí. Integrita se nevynucuje.",
                    ModelValue = modelKey.PrincipalTable.Qualified,
                });
                continue;
            }

            if (modelKey.PrincipalTable != databaseKey.PrincipalTable)
            {
                findings.Add(new DiffFinding
                {
                    Kind = DiffKind.ForeignKeyTargetMismatch,
                    Severity = DiffSeverity.Error,
                    Table = model.Name,
                    Object = modelKey.Name,
                    Message = "Cizí klíč ukazuje na jinou tabulku, než model předpokládá.",
                    ModelValue = modelKey.PrincipalTable.Qualified,
                    DatabaseValue = databaseKey.PrincipalTable.Qualified,
                });
            }

            if (DeleteBehaviorDiffers(modelKey.DeleteBehavior, databaseKey.DeleteBehavior))
            {
                findings.Add(new DiffFinding
                {
                    Kind = DiffKind.ForeignKeyDeleteBehaviorMismatch,
                    Severity = DiffSeverity.Error,
                    Table = model.Name,
                    Object = modelKey.Name,
                    Message = "Chování při mazání se liší. Kaskáda se v praxi chová jinak, než model tvrdí.",
                    ModelValue = modelKey.DeleteBehavior.ToString(),
                    DatabaseValue = databaseKey.DeleteBehavior.ToString(),
                });
            }
        }

        // Co v seznamu zbylo, model nemá.
        foreach (var databaseKey in databaseKeys)
        {
            findings.Add(new DiffFinding
            {
                Kind = DiffKind.ForeignKeyMissingInModel,
                Severity = DiffSeverity.Warning,
                Table = model.Name,
                Object = databaseKey.Name,
                Message = "Cizí klíč je v databázi, ale v modelu není.",
                DatabaseValue = databaseKey.PrincipalTable.Qualified,
            });
        }
    }

    private static void CompareMigrations(DatabaseSchema model, List<DiffFinding> findings)
    {
        foreach (var migration in model.Migrations)
        {
            if (migration.IsPending)
            {
                findings.Add(new DiffFinding
                {
                    Kind = DiffKind.MigrationPending,
                    Severity = DiffSeverity.Error,
                    Object = migration.Id,
                    Message = "Migrace je v kódu, ale není nasazená.",
                });
            }
            else if (migration.IsOrphaned)
            {
                findings.Add(new DiffFinding
                {
                    Kind = DiffKind.MigrationOrphaned,
                    Severity = DiffSeverity.Warning,
                    Object = migration.Id,
                    Message = "Migrace je nasazená, ale v kódu chybí. Databáze je novější než aplikace.",
                });
            }
        }
    }

    // ---------- porovnávací pravidla ----------

    /// <summary>
    /// Typy se považují za shodné i při jiném zápisu téhož — <c>nvarchar(200)</c> versus
    /// <c>NVARCHAR (200)</c>. Rozdíl v mezerách a velikosti písmen není rozdíl v databázi.
    /// </summary>
    public static bool StoreTypesMatch(string? model, string? database)
    {
        if (string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(database))
        {
            return true;
        }

        return string.Equals(Normalize(model), Normalize(database), StringComparison.OrdinalIgnoreCase);

        static string Normalize(string value) =>
            value.Replace(" ", "", StringComparison.Ordinal).Trim();
    }

    /// <summary>
    /// Defaulty se porovnávají po odstranění závorek, které SQL Server kolem výrazu přidává:
    /// model říká <c>GETDATE()</c>, databáze vrátí <c>(getdate())</c>.
    /// </summary>
    public static bool DefaultsMatch(string? model, string? database)
    {
        var left = StripParentheses(model);
        var right = StripParentheses(database);

        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        static string? StripParentheses(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            while (trimmed.Length > 2 && trimmed[0] == '(' && trimmed[^1] == ')')
            {
                trimmed = trimmed[1..^1].Trim();
            }

            return trimmed.Replace(" ", "", StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Sloupce se porovnávají včetně pořadí — u indexu i u klíče na pořadí záleží.
    /// </summary>
    public static bool ColumnsMatch(IReadOnlyList<string> model, IReadOnlyList<string> database)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(database);

        if (model.Count != database.Count)
        {
            return false;
        }

        for (var i = 0; i < model.Count; i++)
        {
            if (!string.Equals(model[i], database[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// <c>Restrict</c> a <c>NoAction</c> se v databázi chovají stejně — SQL Server obojí
    /// ukládá jako <c>NO_ACTION</c>. Hlásit to jako rozdíl by byl falešný poplach.
    /// </summary>
    public static bool DeleteBehaviorDiffers(DbDeleteBehavior model, DbDeleteBehavior database)
    {
        if (model == database)
        {
            return false;
        }

        if (model == DbDeleteBehavior.Unknown || database == DbDeleteBehavior.Unknown)
        {
            return false;
        }

        return !(IsNoOp(model) && IsNoOp(database));

        static bool IsNoOp(DbDeleteBehavior behavior) =>
            behavior is DbDeleteBehavior.Restrict or DbDeleteBehavior.NoAction;
    }

    /// <summary>
    /// Vybere ze seznamu klíč odpovídající zadanému a odebere ho, aby se nespároval
    /// podruhé. Přednost má shoda včetně cílové tabulky — teprve když žádná není,
    /// bere se shoda jen podle sloupců, protože to je nejspíš přesměrovaná vazba.
    /// </summary>
    private static DbForeignKey? TakeMatching(List<DbForeignKey> candidates, DbForeignKey wanted)
    {
        var identity = ForeignKeyIdentity(wanted);

        var index = candidates.FindIndex(c =>
            ForeignKeyIdentity(c) == identity && c.PrincipalTable == wanted.PrincipalTable);

        if (index < 0)
        {
            index = candidates.FindIndex(c => ForeignKeyIdentity(c) == identity);
        }

        if (index < 0)
        {
            return null;
        }

        var found = candidates[index];
        candidates.RemoveAt(index);

        return found;
    }

    /// <summary>
    /// Identita cizího klíče pro párování — sloupce závislé tabulky. Jméno v ní není,
    /// protože ho SQLite vůbec nevystavuje a skládá se uměle podle konvence.
    /// Cílová tabulka v ní taky není, aby se přesměrování vazby dalo nahlásit
    /// jako změna, ne jako dvojice „chybí" a „přebývá".
    /// </summary>
    internal static string ForeignKeyIdentity(DbForeignKey foreignKey)
    {
        ArgumentNullException.ThrowIfNull(foreignKey);

        return string.Join(',', foreignKey.Columns.Select(static c => c.ToUpperInvariant()));
    }

    private static string Describe(IReadOnlyList<string> columns) =>
        columns.Count == 0 ? "(žádné)" : string.Join(", ", columns);
}

/// <summary>Co se má při porovnání hlásit.</summary>
public sealed record DiffOptions
{
    public static DiffOptions Default { get; } = new();

    /// <summary>Porovnávat typy sloupců.</summary>
    public bool CompareStoreTypes { get; init; } = true;

    /// <summary>Porovnávat maximální délky.</summary>
    public bool CompareLengths { get; init; } = true;

    /// <summary>
    /// Porovnávat defaultní hodnoty. Ve výchozím stavu vypnuto — providerů je víc
    /// a jejich zápis defaultu se liší natolik, že falešných poplachů bývá víc než nálezů.
    /// </summary>
    public bool CompareDefaults { get; init; }

    /// <summary>
    /// Tabulky, které se nemají hlásit jako „v databázi navíc". Podporuje zástupný znak
    /// <c>*</c>. Historie migrací je vyloučená vždy.
    /// </summary>
    public IReadOnlyList<string> IgnoreTables { get; init; } = [];

    /// <summary>Má se tabulka vynechat z hlášení „chybí v modelu"?</summary>
    public bool IsIgnored(DbObjectName table)
    {
        if (string.Equals(table.Name, "__EFMigrationsHistory", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var pattern in IgnoreTables)
        {
            if (GlobPattern.IsMatch(table.Name, pattern) || GlobPattern.IsMatch(table.Qualified, pattern))
            {
                return true;
            }
        }

        return false;
    }
}
