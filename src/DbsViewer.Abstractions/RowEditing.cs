namespace DbsViewer;

/// <summary>
/// Pravidla o tom, co se v řádku smí měnit a jestli se řádek dá vůbec adresovat.
/// </summary>
/// <remarks>
/// Sedí v <c>Abstractions</c>, protože stejnou odpověď potřebuje server (aby zápis odmítl)
/// i prohlížečka (aby políčko vůbec nenabídla). Dvě kopie pravidel by se rozešly a UI by
/// slibovalo něco, co server neudělá.
/// </remarks>
public static class RowEditing
{
    /// <summary>Proč se sloupec nedá měnit, nebo <c>null</c>, když se měnit dá.</summary>
    /// <param name="column">Sloupec ze schématu.</param>
    /// <param name="maskedColumns">Sloupce, jejichž hodnoty se maskují.</param>
    public static string? ReadOnlyReason(DbColumn column, IReadOnlyCollection<string>? maskedColumns = null)
    {
        ArgumentNullException.ThrowIfNull(column);

        // Primární klíč řádek identifikuje. Jeho změna není úprava hodnoty, ale výměna
        // identity — a WHERE by pak ukazovalo jinam než SET.
        if (column.IsPrimaryKey)
        {
            return "je součástí primárního klíče";
        }

        if (column.IsIdentity)
        {
            return "hodnotu generuje databáze";
        }

        if (column.IsComputed)
        {
            return "je počítaný";
        }

        if (IsBinary(column))
        {
            return "je binární a v mřížce se zobrazuje jen jeho velikost";
        }

        // Zamaskovanou hodnotu uživatel nevidí, takže by přepisoval něco, co nezná.
        return Contains(maskedColumns, column.Name) ? "je zamaskovaný" : null;
    }

    /// <summary>Dá se hodnota sloupce upravit?</summary>
    public static bool IsEditable(DbColumn column, IReadOnlyCollection<string>? maskedColumns = null) =>
        ReadOnlyReason(column, maskedColumns) is null;

    /// <summary>Proč se sloupec nedá vyplnit u nového řádku, nebo <c>null</c>, když jde.</summary>
    /// <param name="column">Sloupec ze schématu.</param>
    /// <param name="maskedColumns">Sloupce, jejichž hodnoty se maskují.</param>
    /// <remarks>
    /// Proti <see cref="ReadOnlyReason"/> se liší v primárním klíči. U existujícího řádku je
    /// klíč jeho identita a měnit se nesmí; u nového ho musí vyplnit uživatel, jinak by do
    /// tabulky s přirozeným klíčem nešlo vložit vůbec nic.
    /// </remarks>
    public static string? NewRowReadOnlyReason(
        DbColumn column,
        IReadOnlyCollection<string>? maskedColumns = null)
    {
        ArgumentNullException.ThrowIfNull(column);

        if (column.IsIdentity)
        {
            return "hodnotu generuje databáze";
        }

        if (column.IsComputed)
        {
            return "je počítaný";
        }

        if (IsBinary(column))
        {
            return "je binární a v mřížce se zadat nedá";
        }

        // Zamaskovanou hodnotu uživatel nevidí ani u nového řádku, takže by ji psal naslepo.
        // Sloupec zůstane na výchozí hodnotě z databáze, nebo vložení odmítne NOT NULL.
        return Contains(maskedColumns, column.Name) ? "je zamaskovaný" : null;
    }

    /// <summary>Dá se hodnota sloupce vyplnit u nového řádku?</summary>
    public static bool IsFillable(DbColumn column, IReadOnlyCollection<string>? maskedColumns = null) =>
        NewRowReadOnlyReason(column, maskedColumns) is null;

    /// <summary>
    /// Dá se do tabulky vložit řádek?
    /// </summary>
    /// <remarks>
    /// Primární klíč se — na rozdíl od úpravy a mazání — nevyžaduje. <c>INSERT</c> žádný
    /// existující řádek neadresuje, takže se nemá čím splést, a „právě jeden řádek" plyne
    /// z tvaru příkazu. Do tabulky bez klíče se tedy vložit dá, jen se pak řádek nedá
    /// upravit ani smazat. Pohled zůstává jen ke čtení.
    /// </remarks>
    public static bool CanInsertRows(DbTable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        return !table.IsView;
    }

    /// <summary>
    /// Je to binární sloupec? Pozná se z CLR typu, a když ten chybí (tabulka mimo model),
    /// z typu v databázi.
    /// </summary>
    public static bool IsBinary(DbColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);

        if (string.Equals(column.ClrType, "System.Byte[]", StringComparison.Ordinal))
        {
            return true;
        }

        var store = StoreTypeName(column.StoreType);

        return store is "binary" or "varbinary" or "image" or "blob" or "rowversion" or "timestamp";
    }

    /// <summary>
    /// Dá se v tabulce jednoznačně adresovat jeden řádek? Bez toho se nesmí zapisovat —
    /// <c>UPDATE</c> bez spolehlivého <c>WHERE</c> může sáhnout na víc řádků, než se čeká.
    /// </summary>
    public static bool CanIdentifyRows(DbTable table, IReadOnlyCollection<string>? maskedColumns = null)
    {
        ArgumentNullException.ThrowIfNull(table);

        // Pohled se nemapuje na jednu tabulku, takže se do něj nezapisuje ani tehdy,
        // když ho model klíčem popisuje.
        if (table.IsView)
        {
            return false;
        }

        var key = table.PrimaryKey?.Columns ?? [];

        if (key.Count == 0)
        {
            return false;
        }

        foreach (var name in key)
        {
            // Zamaskovaná hodnota klíče se do WHERE dát nedá — v mřížce jsou místo ní
            // hvězdičky a ty by našly úplně jiný řádek, nebo žádný.
            if (table.FindColumn(name) is null || Contains(maskedColumns, name))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Jméno typu bez délky a přesnosti: z <c>nvarchar(200)</c> zbyde <c>nvarchar</c>.</summary>
    public static string StoreTypeName(string? storeType)
    {
        if (string.IsNullOrWhiteSpace(storeType))
        {
            return "";
        }

        var text = storeType.Trim();
        var zavorka = text.IndexOf('(', StringComparison.Ordinal);

        if (zavorka >= 0)
        {
            text = text[..zavorka];
        }

        return text.Trim().ToLowerInvariant();
    }

    /// <summary>Hledání jména bez ohledu na velikost písmen; kolekce bývá malá.</summary>
    private static bool Contains(IReadOnlyCollection<string>? names, string name)
    {
        if (names is null)
        {
            return false;
        }

        foreach (var item in names)
        {
            if (string.Equals(item, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
