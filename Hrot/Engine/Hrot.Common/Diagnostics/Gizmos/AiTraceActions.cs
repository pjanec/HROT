using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Hrot.Common.Constants;

namespace Hrot.Common.Diagnostics.Gizmos
{
    /// <summary>
    /// ⭐ <c>CE-3123</c> (R-228) — the AI-trace toggles of a unit with a brain (<see cref="BehaviorDebugFlags.EnableTraceBuffer"/>,
    /// <see cref="BehaviorDebugFlags.EmitToLog"/>): ONE implementation, registered by <c>MapInteractionPack</c> on every map host and
    /// offered in the unit's map context menu. It replaces SimHost's <c>AiTraceContextMenu</c> helper and the Editor's inline copy.
    /// Like <see cref="GizmoPins"/> it publishes a <see cref="PatchDebugStateCommand"/>, applied by <c>DebugStatePatchSystem</c>.
    /// ⚠ The flag lands on THIS node's <see cref="DebugState"/>; the trace ring buffers fill only where the brain runs.
    /// 📄 docs/DESIGN_Uniform_Gizmo_Membership.md §10.
    /// </summary>
    public static class AiTraceActions
    {
        /// <summary>Whether <paramref name="target"/> has <paramref name="flag"/> set.</summary>
        public static bool IsOn(ISimulationView view, Entity target, BehaviorDebugFlags flag) =>
            (view is not EntityRepository repo || repo.IsComponentTypeRegistered<DebugState>())
            && view.HasComponent<DebugState>(target) && (view.GetComponentRO<DebugState>(target).Behavior & flag) != 0;

        /// <summary>Flips <paramref name="flag"/> on a unit with a brain; a no-op on anything else, or where nothing can apply it.</summary>
        public static void Toggle(ISimulationView view, Entity target, BehaviorDebugFlags flag)
        {
            if (target == Entity.Null || view is not EntityRepository repo || !repo.IsAlive(target)) return;
            if (!HasBrain(repo, target) || !repo.IsComponentTypeRegistered<DebugState>()) return;
            bool next = !IsOn(view, target, flag);
            repo.Bus.PublishManaged(new PatchDebugStateCommand
            {
                Target    = target,
                PatchJson = $$"""{ "{{nameof(DebugState.Behavior)}}": { "{{flag}}": {{(next ? "true" : "false")}} } }""",
            });
        }

        /// <summary>Whether <paramref name="entity"/> runs (or mirrors) a brain — the units the toggles mean anything for.</summary>
        public static bool HasBrain(ISimulationView view, Entity entity) =>
            (view is not EntityRepository repo || repo.IsComponentTypeRegistered<BehaviorState>())
            && view.HasComponent<BehaviorState>(entity);

        /// <summary>Registers both toggles on the map's action registry.</summary>
        public static void RegisterActions(Hrot.Common.Interactions.GlobalActionRegistry actions)
        {
            actions.Register(GlobalActionIds.ToggleAiTrace,    (view, target) => Toggle(view, target, BehaviorDebugFlags.EnableTraceBuffer));
            actions.Register(GlobalActionIds.ToggleAiTraceLog, (view, target) => Toggle(view, target, BehaviorDebugFlags.EmitToLog));
        }

        /// <summary>The <b>AI trace</b> submenu of a unit's map context menu (only offered for a unit with a brain).</summary>
        public static ContextMenuItemDto Submenu() => new()
        {
            Label = "AI trace",
            Children = new[]
            {
                new ContextMenuItemDto { Id = GlobalActionIds.ToggleAiTrace,    Label = "Toggle trace buffer" },
                new ContextMenuItemDto { Id = GlobalActionIds.ToggleAiTraceLog, Label = "Toggle trace log" },
            },
        };
    }
}
