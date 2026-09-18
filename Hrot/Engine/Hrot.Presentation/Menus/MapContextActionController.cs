using System;
using Hrot.UI.Common.Facades;

namespace Hrot.UI.Common.Menus;

/// <summary>
/// Minimal <see cref="IEntityActionController"/> for inline map context menus.
/// Delegates centre, delete, and rotate to caller-supplied callbacks.
/// EditOverlay, EditRoute, Rename, and ActivateMeasureTool are intentional no-ops
/// because those operations are not available from the map right-click popup.
/// </summary>
public sealed class MapContextActionController : IEntityActionController
{
    private readonly Action<long> _centerOnEntity;
    private readonly Action<long> _deleteEntity;
    private readonly Action<long> _rotateTool;
    private readonly Action<long>? _loadZone;

    /// <summary>
    /// Constructs a <see cref="MapContextActionController"/>.
    /// </summary>
    /// <param name="centerOnEntity">Called when "Centre on Entity" is activated.</param>
    /// <param name="deleteEntity">Called when "Delete" is activated.</param>
    /// <param name="rotateTool">Called when "Rotate" is activated.</param>
    /// <param name="loadZone">
    /// ⭐ <c>E2</c> — called when "Load zone" is activated, to publish the CLUSTER-wide zone-load op
    /// (design §9.6). ⚠ Optional because this controller also serves hosts with no orchestrator reach;
    /// a host that HAS one must pass it, or the menu item silently does nothing — the silent-default
    /// shape. <see cref="LoadZone"/> logs rather than swallowing when it is absent.
    /// </param>
    public MapContextActionController(
        Action<long> centerOnEntity,
        Action<long> deleteEntity,
        Action<long> rotateTool,
        Action<long>? loadZone = null)
    {
        _centerOnEntity = centerOnEntity;
        _deleteEntity   = deleteEntity;
        _rotateTool     = rotateTool;
        _loadZone       = loadZone;
    }

    /// <inheritdoc/>
    public void CenterOnEntity(long entityId)     => _centerOnEntity(entityId);

    /// <inheritdoc/>
    public void DeleteEntity(long entityId)       => _deleteEntity(entityId);

    /// <inheritdoc/>
    public void ActivateRotateTool(long entityId) => _rotateTool(entityId);

    /// <inheritdoc/>
    public void EditOverlay(long entityId)        { }

    /// <inheritdoc/>
    public void EditRoute(long entityId)          { }

    /// <inheritdoc/>
    public void Rename(long entityId)             { }

    /// <inheritdoc/>
    public void ActivateMeasureTool()             { }

    /// <inheritdoc/>
    /// <remarks>
    /// ⚠ When no <c>loadZone</c> callback was supplied this LOGS rather than doing nothing quietly: a
    /// menu item that appears and silently no-ops is worse than one that is absent (<c>R-133</c>), and
    /// the absence is a composition gap on that host, not a valid state.
    /// </remarks>
    public void LoadZone(long entityId)
    {
        if (_loadZone == null)
        {
            Fdp.Core.Logging.FdpLog<MapContextActionController>.Warn(
                "[ContextMenu] 'Load zone' activated for entity {0} but this host composed no zone-load "
              + "callback — the cluster op was NOT published. This is a composition gap, not a no-op.",
                entityId);
            return;
        }
        _loadZone(entityId);
    }
}
