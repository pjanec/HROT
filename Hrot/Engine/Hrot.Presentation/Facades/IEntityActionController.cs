namespace Hrot.UI.Common.Facades;

/// <summary>
/// Port interface for entity-level actions available via context menus or toolbar buttons.
/// Panels use this interface to trigger entity-focused map and authoring operations.
/// </summary>
public interface IEntityActionController
{
    /// <summary>Pans and zooms the map to centre on the specified entity.</summary>
    /// <param name="entityId">The network entity ID to centre on.</param>
    void CenterOnEntity(long entityId);

    /// <summary>Requests deletion of the specified entity from the scenario.</summary>
    /// <param name="entityId">The network entity ID to delete.</param>
    void DeleteEntity(long entityId);

    /// <summary>Opens the overlay (tactical graphic) editor for the specified entity.</summary>
    /// <param name="entityId">The network entity ID whose overlay is to be edited.</param>
    void EditOverlay(long entityId);

    /// <summary>Opens the route editor for the specified entity.</summary>
    /// <param name="entityId">The network entity ID whose route is to be edited.</param>
    void EditRoute(long entityId);

    /// <summary>Opens the rename dialog for the specified entity.</summary>
    /// <param name="entityId">The network entity ID to rename.</param>
    void Rename(long entityId);

    /// <summary>Activates the distance measurement tool on the map.</summary>
    void ActivateMeasureTool();

    /// <summary>Activates the entity rotation tool on the map for the specified entity.</summary>
    /// <param name="entityId">The network entity ID of the entity to rotate.</param>
    void ActivateRotateTool(long entityId);
}
