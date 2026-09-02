namespace DbsViewer.Ui.Model;

/// <summary>
/// Barva databázového schématu, odvozená z jeho jména.
/// </summary>
/// <remarks>
/// Ve schématu s několika namespacy je barva rychlejší vodítko než čtení jména —
/// oko rozezná „všechno modré patří k prodeji" dřív, než přečte prefix.
///
/// Barva se **počítá z jména**, ne přiděluje z palety: schéma tak má stejnou barvu
/// v seznamu, v diagramu i po znovunačtení stránky, a nezávisí na tom, kolik schémat
/// se zrovna zobrazuje. Vrací se jen odstín; sytost a světlost řeší CSS, aby barva
/// seděla ve světlém i tmavém tématu.
/// </remarks>
public static class SchemaBarvy
{
    /// <summary>
    /// Odstín pro zadané schéma, ve stupních 0–359.
    /// </summary>
    /// <param name="schema">Jméno schématu. Prázdné vrací nulu, ale nepoužije se.</param>
    public static int Odstin(string? schema)
    {
        if (schema is not { Length: > 0 })
        {
            return 0;
        }

        // FNV-1a: krátký, stabilní napříč běhy i platformami. String.GetHashCode
        // se v .NET mezi spuštěními randomizuje, takže by barva pokaždé přeskočila.
        var hash = 2166136261u;

        foreach (var znak in schema)
        {
            hash ^= char.ToLowerInvariant(znak);
            hash *= 16777619u;
        }

        // Odstín se skládá ze dvou částí: hrubý sektor po 10 stupních a jemný posun
        // uvnitř něj. Prosté `hash % 360` dávalo u čtyř běžných jmen odstupy pod
        // 15 stupňů, což oko nerozliší; takhle vycházejí desítky stupňů.
        return (int)((hash % 36 * 10) + (hash / 36 % 10));
    }

    /// <summary>
    /// Inline styl s odstínem, nebo <c>null</c> pro tabulku bez schématu.
    /// </summary>
    /// <remarks>
    /// Předává se jen odstín jako CSS proměnná — sytost a světlost si dopočítá
    /// stylopis podle tématu, takže barva zůstane čitelná na světlém i tmavém pozadí.
    /// </remarks>
    public static string? Styl(string? schema) =>
        schema is { Length: > 0 } ? $"--schema-odstin: {Odstin(schema)}" : null;
}
