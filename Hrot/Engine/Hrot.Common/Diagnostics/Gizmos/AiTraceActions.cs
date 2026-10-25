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
        /// <summary>⭐ <c>CE-1329</c> — who asks for a unit's trace (Brain View §12r): the trace runs while ANY of them asks.</summary>
        public enum Requester
        {
            /// <summary>Someone switched it on (the map menu, the Decision tab's "Trace this unit", the HTTP debug settings) — remembered.</summary>
            User,
            /// <summary>A Brain tab is showing the unit — temporary.</summary>
            Viewed,
            /// <summary>The HTTP observe routes or a breakpoint scoped to the unit — temporary.</summary>
            Armed,
        }

        /// <summary>Whether <paramref name="target"/> has <paramref name="flag"/> set. ⭐ <c>CE-1329</c> — the trace switch of a unit with a network
        /// id is answered from the ONE store (any requester).</summary>
        public static bool IsOn(ISimulationView view, Entity target, BehaviorDebugFlags flag)
        {
            if (flag == BehaviorDebugFlags.EnableTraceBuffer && view is EntityRepository w && UnitDebugSettings.NetIdOf(w, target) is var id and not 0)
                return UnitDebugSettings.Peek(w)?.TraceOn(id) == true;
            return (view is not EntityRepository repo || repo.IsComponentTypeRegistered<DebugState>())
                   && view.HasComponent<DebugState>(target) && (view.GetComponentRO<DebugState>(target).Behavior & flag) != 0;
        }

        /// <summary>Flips <paramref name="flag"/> on a unit with a brain; a no-op on anything else, or where nothing can apply it.</summary>
        public static void Toggle(ISimulationView view, Entity target, BehaviorDebugFlags flag)
        {
            if (target == Entity.Null || view is not EntityRepository repo || !repo.IsAlive(target)) return;
            if (!HasBrain(repo, target) || !repo.IsComponentTypeRegistered<DebugState>()) return;
            // ⭐ CE-1329 — the menu toggles the user's own switch (a tab showing it does not count)
            bool on = flag == BehaviorDebugFlags.EnableTraceBuffer && UnitDebugSettings.NetIdOf(repo, target) is var id and not 0
                ? UnitDebugSettings.Peek(repo)?.IsTraceSet(id) == true : IsOn(view, target, flag);
            Set(view, target, flag, !on);
        }

        /// <summary>⭐ <c>CE-1257</c> — sets <paramref name="flag"/> on <paramref name="target"/>. ⭐ <c>CE-1329</c> — the trace switch of a unit
        /// with a network id goes to the ONE store, as <paramref name="by"/>'s request (only the user's is remembered); anything else, and a unit
        /// without a network id, is patched onto its <see cref="DebugState"/> as before.</summary>
        public static void Set(ISimulationView view, Entity target, BehaviorDebugFlags flag, bool on, Requester by = Requester.User)
        {
            if (target == Entity.Null || view is not EntityRepository repo || !repo.IsAlive(target)) return;
            if (!HasBrain(repo, target) || !repo.IsComponentTypeRegistered<DebugState>()) return;
            if (flag == BehaviorDebugFlags.EnableTraceBuffer && UnitDebugSettings.NetIdOf(repo, target) is var id and not 0)
            {
                var store = UnitDebugSettings.Of(repo);
                if (by == Requester.User) store.SetTrace(id, on);
                else if (by == Requester.Viewed) store.SetViewed(id, on);
                else store.SetArmed(id, on);
                return;
            }
            repo.Bus.PublishManaged(new PatchDebugStateCommand
            {
                Target    = target,
                PatchJson = $$"""{ "{{nameof(DebugState.Behavior)}}": { "{{flag}}": {{(on ? "true" : "false")}} } }""",
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
            // ⛔ CE-1329 (BV-72) — "Debug this unit" is gone: it only switched the trace on, which "Trace" here does (and remembers)
        }

        /// <summary>The <b>AI trace</b> submenu of a unit's map context menu (only offered for a unit with a brain). ⭐ <c>CE-1329</c> — "Trace"
        /// checked while the user's switch is on (remembered per scenario, the same store as the Brain tab).</summary>
        public static ContextMenuItemDto Submenu(bool traceOn = false) => new()
        {
            Label = "AI trace",
            Children = new[]
            {
                new ContextMenuItemDto { Id = GlobalActionIds.ToggleAiTrace,    Label = "Trace (remembered)", IsChecked = traceOn },
                new ContextMenuItemDto { Id = GlobalActionIds.ToggleAiTraceLog, Label = "Toggle trace log" },
            },
        };
    }
}
