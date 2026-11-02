using System.Collections.Generic;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.ScenarioRules.Actions
{
    /// <summary>
    /// ⭐ rules S2-d (📄 docs/DESIGN_Scenario_Rules.md §00.9, SR-97) — ONE generic side record: the JSON of every step list / arg set an entity's
    /// actions use (a trigger keeps its <c>when</c> at item 0 and its <c>do</c> list at item 1), indexed by an <c>int</c> ref.
    /// <para>⭐ MANAGED, so IMMUTABLE and REPLACED whole (CLAUDE.md "NEW COMPONENTS"): it is written at assignment only; nothing edits an item in
    /// place. ⛔ A managed component changes by <c>SetManagedComponent(new ActionArgs(...))</c>, never by mutating a held reference — the table's
    /// chunk version (what the recorder reads) moves only on a Set, and a snapshot shares the reference.</para>
    /// <para><c>NoScenario</c>: it is rebuilt from the owning behaviour's params at assignment.</para>
    /// </summary>
    [ComponentId(GlobalComponentIds.ActionArgs)]
    [DataPolicy(DataPolicy.NoScenario)]
    public sealed record ActionArgs(IReadOnlyList<string> Items);

    /// <summary>
    /// ⭐ rules S2-d (CE-4165) — THE ONE reason channel of a failed shared action. <c>NodeStatus</c> carries no reason, so a failing action calls
    /// <see cref="Set"/>; <c>RunActions</c> stamps <see cref="Step"/> (which step of the list it was). Read by whatever ran the list (the trigger:
    /// its status <c>Reason</c>). Brain-local, <c>NoScenario</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.ActionFailure)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct ActionFailure
    {
        /// <summary>The index, in its list, of the step that failed (-1 = none recorded).</summary>
        public int Step;
        /// <summary>A short text: <c>"unresolved reference @Hq"</c>, <c>"target is dead"</c>.</summary>
        public FixedString64 Reason;

        /// <summary>
        /// ⭐ A failing action records WHY (<paramref name="reason"/> is cut to 64 bytes). Adds the component on first use — a structural
        /// change on <paramref name="self"/> that moves nothing a caller holds (the stores are per-type tables).
        /// </summary>
        public static void Set(EntityRepository world, Entity self, string reason)
        {
            if (!world.HasComponent<ActionFailure>(self)) world.AddComponent(self, new ActionFailure { Step = -1 });
            ref var f = ref world.GetComponentRW<ActionFailure>(self);
            f.Reason = new FixedString64(reason ?? "");
        }

        /// <summary>Forgets the last failure (a step starts: a stale reason must never be read as this step's). No-op when none was ever set.</summary>
        public static void Clear(EntityRepository world, Entity self)
        {
            if (!world.IsComponentTypeRegistered<ActionFailure>() || !world.HasComponent<ActionFailure>(self)) return;
            ref var f = ref world.GetComponentRW<ActionFailure>(self);
            f.Step = -1;
            f.Reason = default;
        }

        /// <summary>The recorded failure, or <c>false</c> when nothing failed.</summary>
        public static bool TryGet(EntityRepository world, Entity self, out ActionFailure failure)
        {
            if (world.IsComponentTypeRegistered<ActionFailure>() && world.HasComponent<ActionFailure>(self)) { failure = world.GetComponentRO<ActionFailure>(self); return failure.Step >= 0 || failure.Reason.Length > 0; }
            failure = default;
            return false;
        }

        /// <summary>RunActions: the step that failed; fills in a generic reason when the action gave none.</summary>
        internal static void Stamp(EntityRepository world, Entity self, int step, string act)
        {
            if (!world.HasComponent<ActionFailure>(self)) world.AddComponent(self, new ActionFailure { Step = -1 });
            ref var f = ref world.GetComponentRW<ActionFailure>(self);
            f.Step = step;
            if (f.Reason.Length == 0) f.Reason = new FixedString64($"'{act}' failed");
        }
    }
}
