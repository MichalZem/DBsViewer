using System.Globalization;

namespace DbsViewer.Server;

/// <summary>
/// Převod textu z požadavku na hodnotu parametru.
/// </summary>
/// <remarks>
/// Rozhoduje **typ v databázi**, ne CLR typ z modelu: zapisuje se přes ADO.NET přímo
/// do sloupce, ne přes EF, takže se hodnota musí trefit do úložiště. U SQLite je datum
/// uložené jako <c>TEXT</c> a posílat ho jako <see cref="DateTime"/> by změnilo formát;
/// u SQL Serveru je <c>bit</c> a text „True" by databáze odmítla.
///
/// Neznámý typ končí jako řetězec. Je to schválně — u SQLite se stejně uplatní afinita
/// sloupce a u SQL Serveru implicitní konverze; kdyby ani ta nešla, chybu vrátí databáze
/// a uživatel ji uvidí.
/// </remarks>
internal static class DataValueConverter
{
    /// <summary>Hodnota pro <c>DbParameter</c>. NULL se předává jako <see cref="DBNull"/>.</summary>
    /// <param name="column">Sloupec ze schématu — určuje cílový typ.</param>
    /// <param name="value">Text z požadavku, nebo <c>null</c> pro SQL NULL.</param>
    /// <exception cref="DataRequestException">Hodnota se do sloupce nedá uložit.</exception>
    public static object ToParameter(DbColumn column, string? value)
    {
        if (value is null)
        {
            return column.IsNullable
                ? DBNull.Value
                : throw new DataRequestException($"Sloupec {column.Name} nesmí být NULL.");
        }

        var text = value;
        var store = RowEditing.StoreTypeName(column.StoreType);

        object? prevedeno = store switch
        {
            // Oddělovač tisíců se schválně nepřipouští: mřížka ukazuje čísla bez něj
            // a „1,5" má v půlce Evropy znamenat jedna a půl, ne patnáct.
            "bit" or "boolean" or "bool" => ParseBoolean(text),
            "tinyint" or "smallint" or "int" or "integer" or "mediumint" or "bigint" =>
                long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cele)
                    ? cele
                    : null,
            "decimal" or "numeric" or "money" or "smallmoney" =>
                decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var desetinne)
                    ? desetinne
                    : null,
            "float" or "double" or "real" =>
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var plovouci)
                    ? plovouci
                    : null,
            "uniqueidentifier" or "guid" => Guid.TryParse(text, out var guid) ? guid : null,
            "date" or "datetime" or "datetime2" or "smalldatetime" =>
                DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var datum)
                    ? datum
                    : null,
            "datetimeoffset" =>
                DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var posun)
                    ? posun
                    : null,
            "time" => TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var cas) ? cas : null,
            _ => text,
        };

        return prevedeno ?? throw new DataRequestException(
            $"Hodnotu „{text}\" nejde uložit do sloupce {column.Name} typu {column.StoreType}.");
    }

    /// <summary>
    /// Pravdivostní hodnota. Bere i <c>0</c> a <c>1</c>, protože tak ji ukazuje mřížka
    /// nad SQLite — tam je <c>bool</c> uložený jako číslo.
    /// </summary>
    private static object? ParseBoolean(string text) => text.Trim().ToLowerInvariant() switch
    {
        "true" or "1" => true,
        "false" or "0" => false,
        _ => null,
    };
}
