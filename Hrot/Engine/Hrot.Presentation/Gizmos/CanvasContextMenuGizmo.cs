using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Hrot.IG.Components;

namespace Hrot.Presentation.Gizmos
{
    /// <summary>
    /// Global stateless gizmo that projects the canvas context menu into the gizmo
    /// primitive buffer as a <c>ContextMenuBinding</c> meta-primitive keyed by
    /// <see cref="CanvasAnchorId"/> (<c>-1L</c>).
    ///
    /// <para>When the operator right-clicks empty map space, <c>DebugGizmoLayer</c> falls
    /// back to this anchor, resolves the JSON from the intern map, and opens the popup
    /// via <c>ContextMenuAdapter</c> — identical to how entity menus are resolved.</para>
    /// </summary>
    [GizmoProjector]
    public sealed class CanvasContextMenuGizmo : IGlobalStatelessGizmo
    {
        /// <summary>Well-known anchor ID representing the empty-canvas context menu.</summary>
        public const long CanvasAnchorId = -1L;

        public void Draw(ISimulationView view, IDebugDrawBuilder drawBuilder)
        {
            var repo = (EntityRepository)view;
            if (!repo.HasSingletonManaged<CanvasContextMenuState>()) return;
            var state = repo.GetSingletonManaged<CanvasContextMenuState>();
            if (string.IsNullOrEmpty(state.MenuJson)) return;
            drawBuilder.DrawContextMenuBinding(CanvasAnchorId, state.MenuJson);
        }
    }
}
