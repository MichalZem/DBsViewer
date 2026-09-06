using DbsViewer.Ui.Texts;

namespace DbsViewer.Ui.Model;

/// <summary>
/// Počty se správným tvarem podstatného jména.
/// </summary>
/// <remarks>
/// Spojuje dvě části, které musí zůstat oddělené: slova jsou v <c>.resx</c>, protože je
/// překládá překladatel, a pravidlo pro výběr tvaru je v <see cref="Plural"/>, protože
/// se v resource souboru vyjádřit nedá. Tahle třída je jen jejich stykové místo, aby se
/// v komponentách nepsalo pokaždé všech pět argumentů.
/// </remarks>
public static class Counts
{
    /// <summary>Počet tabulek — „5 tabulek", „5 tables".</summary>
    public static string Tables(long count) =>
        Plural.Format(count, Texts.Texts.Table_One, Texts.Texts.Table_Few, Texts.Texts.Table_Other);

    /// <summary>Tvar slova „tabulka" bez čísla; číslo se vypisuje zvlášť velkým písmem.</summary>
    public static string TableForm(long count) =>
        Plural.Form(count, Texts.Texts.Table_One, Texts.Texts.Table_Few, Texts.Texts.Table_Other);

    /// <summary>Tvar slova „pohled" bez čísla.</summary>
    public static string ViewForm(long count) =>
        Plural.Form(count, Texts.Texts.View_One, Texts.Texts.View_Few, Texts.Texts.View_Other);

    /// <summary>Počet sloupců.</summary>
    public static string Columns(long count) =>
        Plural.Format(count, Texts.Texts.Column_One, Texts.Texts.Column_Few, Texts.Texts.Column_Other);

    /// <summary>Tvar slova „sloupec" bez čísla.</summary>
    public static string ColumnForm(long count) =>
        Plural.Form(count, Texts.Texts.Column_One, Texts.Texts.Column_Few, Texts.Texts.Column_Other);

    /// <summary>Počet vazeb.</summary>
    public static string Relationships(long count) =>
        Plural.Format(count, Texts.Texts.Relationship_One, Texts.Texts.Relationship_Few, Texts.Texts.Relationship_Other);

    /// <summary>Tvar slova „vazba" bez čísla.</summary>
    public static string RelationshipForm(long count) =>
        Plural.Form(count, Texts.Texts.Relationship_One, Texts.Texts.Relationship_Few, Texts.Texts.Relationship_Other);

    /// <summary>Tvar slova „index" bez čísla.</summary>
    public static string IndexForm(long count) =>
        Plural.Form(count, Texts.Texts.Index_One, Texts.Texts.Index_Few, Texts.Texts.Index_Other);

    /// <summary>Počet řádků.</summary>
    public static string Rows(long count) =>
        Plural.Format(count, Texts.Texts.Row_One, Texts.Texts.Row_Few, Texts.Texts.Row_Other);

    /// <summary>Tvar slova „řádek" bez čísla.</summary>
    public static string RowForm(long count) =>
        Plural.Form(count, Texts.Texts.Row_One, Texts.Texts.Row_Few, Texts.Texts.Row_Other);

    /// <summary>Počet nálezů závažnosti chyba.</summary>
    public static string Errors(long count) =>
        Plural.Format(count, Texts.Texts.ErrorFinding_One, Texts.Texts.ErrorFinding_Few, Texts.Texts.ErrorFinding_Other);

    /// <summary>Počet nálezů závažnosti varování.</summary>
    public static string Warnings(long count) =>
        Plural.Format(count, Texts.Texts.WarningFinding_One, Texts.Texts.WarningFinding_Few, Texts.Texts.WarningFinding_Other);

    /// <summary>Počet aplikovaných migrací.</summary>
    public static string AppliedMigrations(long count) =>
        Plural.Format(count, Texts.Texts.AppliedMigration_One, Texts.Texts.AppliedMigration_Few, Texts.Texts.AppliedMigration_Other);

    /// <summary>Počet vazebních tabulek.</summary>
    public static string JoinTables(long count) =>
        Plural.Format(count, Texts.Texts.JoinTable_One, Texts.Texts.JoinTable_Few, Texts.Texts.JoinTable_Other);

    /// <summary>Počet položek v seznamu, který se nevešel celý.</summary>
    public static string Items(long count) =>
        Plural.Format(count, Texts.Texts.Item_One, Texts.Texts.Item_Few, Texts.Texts.Item_Other);
}
