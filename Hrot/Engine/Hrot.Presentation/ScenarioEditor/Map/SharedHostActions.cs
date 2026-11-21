using Fdp.Core;
using Hrot.Common.Interactions;

namespace Hrot.ScenarioEditor.Map
{
    /// <summary>
    /// ⭐⭐ <c>CE-1150</c> (docs/designs/gizmos-1/canvas-context-menu-design.md §12 HA-1 / HA-2) — a host's actions, made ONCE by the host
    /// object at its start, with the actions every ECS host runs whether or not it shows a map: Center View, Select, the gizmo pins and
    /// the AI-trace toggles. 🔒 User, 2026-10-16: <i>"action channel must exists without a map, handling menus etc"</i>. The map pack then
    /// registers the map's own actions on it (<see cref="MapInteractionContext.Host"/>); the Details bundle registers Properties… and the
    /// View ▸ Details items. ⛔ SUPERSEDED: <c>MapInteractionPack.RegisterViewActions</c>, and the pins / AI trace registered by the pack.
    /// </summary>
    public static class SharedHostActions
    {
        public static HostActions Create()
        {
            var host = new HostActions();
            var actions = host.Actions;
            // ⭐ CE-1134 — Center View: the canvas showing this world (2-D or 3-D) moves to the entity — this node only.
            actions.RegisterLocal(Hrot.Common.Constants.GlobalActionIds.CenterOnEntity, (view, target) =>
            {
                if (view is EntityRepository w && MapUiStates.CanvasOf(w)?.Camera is { } camera)
                    Hrot.ScenarioEditor.Systems.CenterOnEntitySystem.CenterOn(camera, w, target);
            });
            // ⭐ CE-1134 — Select: the one selection request (UXI-11 S-2 — a handler REQUESTS; the inspector follows the notification).
            actions.Register(Hrot.Common.Constants.GlobalActionIds.Select, (view, target) =>
            {
                if (target.IsNull || view is not EntityRepository w) return;
                w.Bus.PublishManaged(Fdp.Toolkit.Vis2D.Abstractions.SelectionChangeRequest.ReplaceWith(target, "ContextMenu.Select"));
            });
            // ⭐ CE-3120 (R-227) — the Pin gizmos submenu's actions.
            Hrot.Common.Diagnostics.Gizmos.GizmoPins.RegisterActions(actions);
            // ⭐ CE-3123 (R-228) — the AI-trace toggles.
            Hrot.Common.Diagnostics.Gizmos.AiTraceActions.RegisterActions(actions);
            return host;
        }
    }
}
