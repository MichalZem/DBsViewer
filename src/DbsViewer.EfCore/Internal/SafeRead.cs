namespace DbsViewer.EfCore.Internal;

/// <summary>
/// Čtení metadat, které nesmí shodit načtení schématu. Selhání se nezahazuje potichu —
/// vždy skončí jako upozornění ve <see cref="DatabaseSchema.Warnings"/>.
/// </summary>
/// <remarks>
/// Existuje jako samostatný švy‑bod hlavně proto, aby se chybová cesta dala otestovat
/// bez rozbité databáze. Vlastní <c>try/catch</c> rozeseté po readeru by testovatelné nebylo.
/// </remarks>
internal static class SafeRead
{
    /// <summary>Přečte hodnotu, při výjimce vrátí náhradu a zapíše upozornění.</summary>
    /// <param name="read">Čtení, které může selhat.</param>
    /// <param name="fallback">Hodnota použitá při selhání. Vyhodnocuje se vždy, takže musí být levná.</param>
    /// <param name="describeFailure">Text upozornění pro uživatele.</param>
    /// <param name="warnings">Sběrné místo upozornění.</param>
    public static T Value<T>(
        Func<T> read,
        T fallback,
        Func<Exception, string> describeFailure,
        List<string> warnings)
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            warnings.Add(describeFailure(ex));
            return fallback;
        }
    }

    /// <summary>Varianta pro hodnoty, kde náhradou je <c>null</c>.</summary>
    public static T? Optional<T>(
        Func<T?> read,
        Func<Exception, string> describeFailure,
        List<string> warnings)
        where T : class
        => Value(read, null, describeFailure, warnings);
}
