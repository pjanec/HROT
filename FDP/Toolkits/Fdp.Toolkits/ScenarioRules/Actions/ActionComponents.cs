using System;
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
            if (!world.HasComponent<ActionFailure>(self))
            {
                if (Fdp.Core.ParallelReadCheck.Defers(world))
                {
                    // ⭐ CE-3448 — inside a parallel phase the add is DEFERRED (it lands after the phase), so the rule that ran the verb could not read
                    //   the reason in this tick and the journal said "an action failed". The reason waits in this thread's slot (the verb, its Stamp
                    //   and the trigger's read run in ONE entity's job, on one thread); Stamp makes the one add with the final value.
                    Pending.Hold(self, -1, new FixedString64(reason ?? ""));
                    return;
                }
                world.AddComponent(self, new ActionFailure { Step = -1, Reason = new FixedString64(reason ?? "") });
                return;
            }
            ref var f = ref world.GetComponentRW<ActionFailure>(self);
            f.Reason = new FixedString64(reason ?? "");
        }

        /// <summary>⭐ CE-3448 — the failure recorded inside a parallel phase before its deferred add lands: per THREAD, keyed by the entity.</summary>
        private static class Pending
        {
            [ThreadStatic] private static bool t_held;
            [ThreadStatic] private static Entity t_for;
            [ThreadStatic] private static int t_step;
            [ThreadStatic] private static FixedString64 t_reason;

            public static void Hold(Entity e, int step, FixedString64 reason) { t_held = true; t_for = e; t_step = step; t_reason = reason; }
            public static bool For(Entity e, out ActionFailure f)
            {
                f = default;
                if (!t_held || !t_for.Equals(e)) return false;
                f = new ActionFailure { Step = t_step, Reason = t_reason };
                return true;
            }
            public static void Drop(Entity e) { if (t_held && t_for.Equals(e)) t_held = false; }
        }

        /// <summary>Forgets the last failure (a step starts: a stale reason must never be read as this step's). No-op when none was ever set.</summary>
        public static void Clear(EntityRepository world, Entity self)
        {
            Pending.Drop(self);   // CE-3448 — a held reason is this entity's last failure too
            if (!world.IsComponentTypeRegistered<ActionFailure>() || !world.HasComponent<ActionFailure>(self)) return;
            ref var f = ref world.GetComponentRW<ActionFailure>(self);
            f.Step = -1;
            f.Reason = default;
        }

        /// <summary>The recorded failure, or <c>false</c> when nothing failed.</summary>
        public static bool TryGet(EntityRepository world, Entity self, out ActionFailure failure)
        {
            if (world.IsComponentTypeRegistered<ActionFailure>() && world.HasComponent<ActionFailure>(self)) { failure = world.GetComponentRO<ActionFailure>(self); return failure.Step >= 0 || failure.Reason.Length > 0; }
            if (Pending.For(self, out failure)) return failure.Step >= 0 || failure.Reason.Length > 0;   // CE-3448 — recorded this tick, its add deferred
            failure = default;
            return false;
        }

        /// <summary>RunActions: the step that failed; fills in a generic reason when the action gave none.</summary>
        internal static void Stamp(EntityRepository world, Entity self, int step, string act)
        {
            if (!world.HasComponent<ActionFailure>(self))
            {
                // N-11 (CE-4473) — added with its final value: inside the brains' parallel phase an add is deferred.
                // ⭐ CE-3448 — the ONE add carries the verb's own reason when Set held one (it did not add — two deferred adds would let the generic
                //   text overwrite it), and the record stays readable this tick from the slot until the add lands.
                var reason = Pending.For(self, out var held) && held.Reason.Length > 0 ? held.Reason : new FixedString64($"'{act}' failed");
                world.AddComponent(self, new ActionFailure { Step = step, Reason = reason });
                if (Fdp.Core.ParallelReadCheck.Defers(world)) Pending.Hold(self, step, reason);
                else Pending.Drop(self);
                return;
            }
            ref var f = ref world.GetComponentRW<ActionFailure>(self);
            f.Step = step;
            if (f.Reason.Length == 0) f.Reason = new FixedString64($"'{act}' failed");
        }
    }
}
