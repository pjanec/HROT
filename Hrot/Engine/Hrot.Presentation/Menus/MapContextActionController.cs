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

    /// <summary>
    /// Constructs a <see cref="MapContextActionController"/>.
    /// </summary>
    /// <param name="centerOnEntity">Called when "Centre on Entity" is activated.</param>
    /// <param name="deleteEntity">Called when "Delete" is activated.</param>
    /// <param name="rotateTool">Called when "Rotate" is activated.</param>
    public MapContextActionController(
        Action<long> centerOnEntity,
        Action<long> deleteEntity,
        Action<long> rotateTool)
    {
        _centerOnEntity = centerOnEntity;
        _deleteEntity   = deleteEntity;
        _rotateTool     = rotateTool;
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
}
