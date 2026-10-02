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

    /// <summary>
    /// ⭐⭐⭐ <c>E2</c> — requests a CLUSTER-WIDE load of the terrain covering one zone.
    ///
    /// <para>🔒 <b>User ruling: the zone-load action is ALWAYS cluster-wide</b> (design §9.6). ⛔ There is
    /// no local-only zone load on any host. On the editor this still goes through the orchestrator,
    /// because the editor IS a single-node cluster — the same principle <c>CE-275</c> established for
    /// saving ("no direct write in the editor… same code everywhere"). One path, so the editor cannot
    /// drift from the cluster.</para>
    ///
    /// <para>⛔⛔ <b>The caller must NEVER gate this on local freshness</b> (§9.7 ③b). A host whose own
    /// copy looks fresh may be the one node that is stale, and the local marker cannot see the others —
    /// it is deliberately never replicated (§9.1). ⇒ the menu item is always enabled.</para>
    /// </summary>
    /// <param name="entityId">The network entity ID of the zone to load. This is the zone's id on the wire.</param>
    void LoadZone(long entityId);
}
