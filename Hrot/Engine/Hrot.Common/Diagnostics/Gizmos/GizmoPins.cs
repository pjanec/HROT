using System;
using System.Linq;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Hrot.Common.Constants;

namespace Hrot.Common.Diagnostics.Gizmos
{
    /// <summary>
    /// ⭐ <c>CE-3120</c> (R-227) — pinning a gizmo family on ONE unit: the pin is a bit of the unit's <see cref="DebugState.Ai"/>,
    /// changed by a <see cref="PatchDebugStateCommand"/> (the same path the AI-trace toggles use), applied by
    /// <c>DebugStatePatchSystem</c>. A pinned family draws for that unit even when its scope is "selected only".
    /// 📄 docs/DESIGN_Terrain_Combat_Tuning.md §5b.
    /// </summary>
    public static class GizmoPins
    {
        /// <summary>The menu action of a family's pin toggle (or 0).</summary>
        public static int ActionIdOf(AiOverlayFlags family) => family switch
        {
            AiOverlayFlags.Path            => GlobalActionIds.PinGizmosPath,
            AiOverlayFlags.Perception      => GlobalActionIds.PinGizmosPerception,
            AiOverlayFlags.TargetMemory    => GlobalActionIds.PinGizmosContacts,
            AiOverlayFlags.Eqs             => GlobalActionIds.PinGizmosEqs,
            AiOverlayFlags.UtilityDecision => GlobalActionIds.PinGizmosUtility,
            AiOverlayFlags.SquadAssignment => GlobalActionIds.PinGizmosSquad,
            _                              => 0,
        };

        /// <summary>Whether <paramref name="target"/> has <paramref name="family"/> pinned.</summary>
        public static bool IsPinned(ISimulationView view, Entity target, AiOverlayFlags family) =>
            (view is not EntityRepository repo || repo.IsComponentTypeRegistered<DebugState>())
            && view.HasComponent<DebugState>(target) && (view.GetComponentRO<DebugState>(target).Ai & family) != 0;

        /// <summary>Flips <paramref name="family"/>'s pin on <paramref name="target"/>.</summary>
        public static void Toggle(ISimulationView view, Entity target, AiOverlayFlags family) =>
            Set(view, target, family, !IsPinned(view, target, family));

        /// <summary>Pins (or unpins) every family on <paramref name="target"/>.</summary>
        public static void SetAll(ISimulationView view, Entity target, bool pinned)
        {
            foreach (var f in GizmoFamilies.All) Set(view, target, f, pinned);
        }

        /// <summary>Pins or unpins one family; ⚠ a no-op on a host that does not register <see cref="DebugState"/> (nothing to pin).</summary>
        public static void Set(ISimulationView view, Entity target, AiOverlayFlags family, bool pinned)
        {
            if (target == Entity.Null || view is not EntityRepository repo || !repo.IsAlive(target)) return;
            if (!repo.IsComponentTypeRegistered<DebugState>()) return;
            repo.Bus.PublishManaged(new PatchDebugStateCommand
            {
                Target    = target,
                PatchJson = $$"""{ "{{nameof(DebugState.Ai)}}": { "{{family}}": {{(pinned ? "true" : "false")}} } }""",
            });
        }

        /// <summary>Registers every pin action on <paramref name="register"/> (the map's action registry).</summary>
        public static void RegisterActions(Hrot.Common.Interactions.GlobalActionRegistry actions)
            => RegisterActions((id, handler) => actions.Register(id, (view, target) => handler(view, target)));

        /// <summary>Registers every pin action through <paramref name="register"/>.</summary>
        public static void RegisterActions(Action<int, Action<ISimulationView, Entity>> register)
        {
            foreach (var f in GizmoFamilies.All)
            {
                var family = f;
                register(ActionIdOf(family), (view, target) => Toggle(view, target, family));
            }
            register(GlobalActionIds.PinGizmosAll,   (view, target) => SetAll(view, target, true));
            register(GlobalActionIds.UnpinGizmosAll, (view, target) => SetAll(view, target, false));
        }

        /// <summary>The <b>Pin gizmos</b> submenu of a unit's map context menu.</summary>
        public static ContextMenuItemDto Submenu() => new()
        {
            Label = "Pin gizmos",
            Children = GizmoFamilies.All
                .Select(f => new ContextMenuItemDto { Id = ActionIdOf(f), Label = GizmoFamilies.Label(f) })
                .Concat(new[]
                {
                    new ContextMenuItemDto { IsSeparator = true },
                    new ContextMenuItemDto { Id = GlobalActionIds.PinGizmosAll,   Label = "Pin all" },
                    new ContextMenuItemDto { Id = GlobalActionIds.UnpinGizmosAll, Label = "Unpin all" },
                })
                .ToArray(),
        };
    }
}
