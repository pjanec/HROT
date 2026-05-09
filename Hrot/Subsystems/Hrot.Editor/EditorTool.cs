namespace Hrot.Editor;

/// <summary>
/// Identifies the currently active interactive tool in the HROT Editor.
/// Used with <see cref="IEditorLogic.ActivateTool"/>.
/// </summary>
public enum EditorTool
{
    /// <summary>Standard selection + drag mode (default).</summary>
    Select,
    /// <summary>Entity placement / spawn mode (activates <c>CreationTool</c>).</summary>
    Spawn,
    /// <summary>Vertex edit mode for overlay shapes (activates <c>EditTool</c>).</summary>
    Edit,
    /// <summary>Route waypoint edit mode (activates <c>RouteEditTool</c>).</summary>
    Route,
    /// <summary>Measurement line mode (activates <c>MeasureTool</c>).</summary>
    Measure,
    /// <summary>Entity rotation mode (adds <c>ActiveRotationToolRequest</c> marker via ECS).</summary>
    Rotate,
}
