namespace Hrot.Common;

/// <summary>
/// Identifies the currently active interactive tool in the HROT Editor.
/// Published as <see cref="Events.ActivateEditorToolEvent"/> and drained by the shared
/// <c>ToolActivationDrainSystem</c> (<c>Hrot.Presentation</c>).
///
/// <para>⭐⭐ <b><c>CE-051</c> (Axis-C E3) — MOVED here from <c>Hrot.Editor</c>.</b> 📄
/// <c>docs/DESIGN_Cgf_Tool_Selection_Camera_Slice.md</c> §3 ①. ⛔ It had to leave the editor assembly
/// because the shared drain system that switches on it lives in <c>Hrot.Presentation</c>, which cannot
/// reference a host. ⚠ There is deliberately still <b>no <c>ITool</c>/<c>ToolManager</c> registry</b> —
/// design §1 measured that the "tool system" IS this enum plus a switch plus the (already shared)
/// gizmos, and inventing a registry is not what E3 is for.</para>
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
    /// <summary>Entity rotation mode (injects <c>EntityRotatorGizmo</c> directly via <c>DataDrivenGizmoSystem</c>).</summary>
    Rotate,
}
