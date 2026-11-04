using Fdp.Core;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// ⭐ CE-2382 (T1; docs/DESIGN_Evaluation_Cost_And_Cadence.md §3.0) — WHEN a brain slot ticks. A generic brain mechanism, not a rules one:
    /// the task slot keeps its mode in <c>BehaviorState.Cadence</c> (beside the SOP slot's own <c>SopNextTick</c> / <c>SopWake</c>), a POLICY
    /// (the rules' <c>ObjectiveHost</c>; later a soldier LOD) writes it. ⭐ <see cref="EveryFrame"/> is 0 — every brain that nothing sets ticks
    /// every frame, exactly as before.
    /// </summary>
    public enum CadenceMode : byte
    {
        /// <summary>Every frame (the default — soldiers, vehicles, everything nothing cadences).</summary>
        EveryFrame = 0,
        /// <summary>Every <c>CadencePeriod</c> seconds of SIM time, staggered by entity index, and at once when woken.</summary>
        Periodic = 1,
        /// <summary>Only when woken, or at <c>CadenceNextTick</c> when the policy set one (a known time — a trigger's <c>ExpectedAt</c>).</summary>
        Asleep = 2,
    }

    /// <summary>⭐ CE-2382 — the ONE cadence rule, shared by the task slot and the SOP slot (D7): due? and the next staggered tick.</summary>
    public static class CadenceGate
    {
        /// <summary>The stagger: a slot's phase is <c>entity index % Phases</c> tenths of its period (the SOP's rule since CE-3035).</summary>
        public const int Phases = 10;

        /// <summary>The period a <see cref="CadenceMode.Periodic"/> slot uses when its policy set none (5 Hz — the SOP's, design D1).</summary>
        public const double DefaultPeriod = 0.2;

        /// <summary>A sleep with no timed wake.</summary>
        public const double Never = double.MaxValue;

        /// <summary>Is the slot due at <paramref name="now"/> (sim seconds)?</summary>
        public static bool Due(CadenceMode mode, double nextTick, byte wake, double now)
            => mode == CadenceMode.EveryFrame || wake != 0 || now >= nextTick;

        /// <summary>The next tick of a periodic slot that ticked at <paramref name="now"/>: one period on, plus its stagger (deterministic in sim time).</summary>
        public static double Next(double now, double period, int entityIndex)
        {
            double p = period > 0 ? period : DefaultPeriod;
            return now + p + (entityIndex % Phases) * (p / Phases);
        }

        /// <summary>Does a slot in this state need its event window kept (an EveryFrame slot reads the bus; an indefinite sleep reads nothing)?</summary>
        public static bool KeepsWindow(CadenceMode mode, double nextTick)
            => mode == CadenceMode.Periodic || (mode == CadenceMode.Asleep && nextTick < Never);
    }

    /// <summary>⭐ CE-2382 — a cadence POLICY's after-tick step: called by <c>BrainTickSystem</c> right after the task slot of an entity whose
    /// <c>BehaviorState.CadencePolicy</c> names it has ticked, to set the slot's mode / next tick / wakes from what the tick left.</summary>
    public delegate void CadencePolicy(EntityRepository world, Entity self, double now);

    /// <summary>⭐ CE-2382 — the policies by id (0 = none). The mechanism knows no policy; a policy registers itself (the rules: <c>RuleCadence</c>).</summary>
    public static class CadencePolicies
    {
        private static readonly CadencePolicy?[] s_policies = new CadencePolicy?[256];

        public static void Register(byte id, CadencePolicy policy)
        {
            if (id == 0) throw new System.ArgumentOutOfRangeException(nameof(id), "0 means no policy");
            s_policies[id] = policy;
        }

        public static void Apply(byte id, EntityRepository world, Entity self, double now) => s_policies[id]?.Invoke(world, self, now);
    }
}
