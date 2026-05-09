using Hrot.Map.Common.Components;

namespace Hrot.ScenarioEditor.Gizmos
{
    // Interface that lets WaypointEditorPanel read per-waypoint state from
    // RouteWaypointGizmo without depending on the concrete gizmo class.
    // WaypointEditorPanel receives a Func<IRouteWaypointEditorState?> in its
    // constructor and calls it each frame to get the currently active gizmo state.
    public interface IRouteWaypointEditorState
    {
        // Index of the vertex currently selected for editing, or -1 if none.
        int SelectedVertexIndex { get; }

        // Returns a ref to the selected waypoint so the panel can mutate
        // TargetSpeed and ExtensionJson in-place (same pattern as RouteEditTool).
        ref RouteWaypoint GetSelectedWaypointRef();
    }
}
