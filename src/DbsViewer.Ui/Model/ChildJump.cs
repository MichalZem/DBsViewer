namespace DbsViewer.Ui.Model;

/// <summary>
/// Požadavek mřížky na zobrazení podřízených záznamů jednoho řádku.
/// </summary>
/// <remarks>
/// Mřížka sama neví, jak se přepnout na jinou tabulku — od toho je hostitelská
/// komponenta, která zároveň zapisuje adresu. Proto se ven posílá dvojice „kam"
/// a „s čím", ne hotová navigace.
/// </remarks>
/// <param name="Table">Podřízená tabulka, na kterou se má prohlížečka přepnout.</param>
/// <param name="Filters">Podmínka odvozená z nadřazeného řádku.</param>
public sealed record ChildJump(DbObjectName Table, IReadOnlyList<ChildFilter> Filters);
