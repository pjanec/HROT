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

        // ⭐ CE-1096 (docs/designs/routes-1/ROUTES1-DESIGN.md §11.4 W-3) — the panel changed a field: write the route back and publish it,
        // as the end of a drag does (an edit used to wait for a right-click and was lost on Escape).
        void CommitEdits();
    }
}
