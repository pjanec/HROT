using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// Curated wave-completion monitor kernel for the Hill-attack wave core (architect Q#8-A/B) — a
    /// faithful by-value port of the C# oracle <c>HillAttackCommanderNodes.Condition_IsWaveCompleted</c>'s
    /// reverse walk with swap-remove (which has no visual-node form: it mutates the runner list while
    /// iterating). Takes/returns the whole <see cref="WaveState"/> bundle so the graph writes it back with
    /// one SetVariable. The visual graph then reads <see cref="ActiveCount"/> and routes
    /// Running/Success. Does not modify the C# oracle.
    /// </summary>
    public static unsafe class WaveMonitorOps
    {
        // Integer id of the HullDownAttackRun subordinate behavior — compared against
        // BehaviorState.ActiveBehaviorHash to detect run start/end (mirrors the oracle's static field).
        private static readonly int HullDownAttackRunBehaviorId =
            BehaviorHash.FromName(BehaviorNames.HullDownAttackRun);

        /// <summary>
        /// Advances the monitor one tick: for each live runner (reverse order so swap-remove never
        /// reprocesses a compacted entry) — dead → burn its firing slot + release its baseline slot +
        /// swap-remove; not-yet-started → latch <c>Started</c> once <c>HullDownAttackRun</c> is observed;
        /// started-and-finished (active behavior no longer <c>HullDownAttackRun</c>) → release its
        /// baseline slot + swap-remove. Returns the mutated <see cref="WaveState"/>. No-op for a
        /// non-repository view.
        /// <para>P7: trailing <c>ISimulationView view</c> is baked <c>TrailingContext:"View"</c> and
        /// downcast to <see cref="EntityRepository"/> (GAP-10).</para>
        /// </summary>
        public static WaveState Update(WaveState s, ISimulationView view)
        {
            if (view is not EntityRepository world) return s;

            for (int i = s.Runners.Count - 1; i >= 0; i--)
            {
                var attacker = new Entity((ulong)s.Runners.EntityPacked[i]);

                if (!world.IsAlive(attacker))
                {
                    // Dead: permanently burn the firing slot, release the baseline slot, remove.
                    s.BurnedSlotsMask      = (ushort)(s.BurnedSlotsMask | (1 << s.Runners.SlotIndex[i]));
                    s.BaselineReservedMask = (ushort)(s.BaselineReservedMask & ~(1 << s.Runners.BaselineSlotIndex[i]));
                    s.Runners = MemberSlotListOps.SwapRemoveAt(s.Runners, i);
                }
                else if (s.Runners.Started[i] == 0)
                {
                    // Intent still propagating; latch once the HullDownAttackRun behavior is seen.
                    if (world.HasComponent<BehaviorState>(attacker)
                        && world.GetComponent<BehaviorState>(attacker).ActiveBehaviorHash == HullDownAttackRunBehaviorId)
                    {
                        s.Runners = MemberSlotListOps.SetStarted(s.Runners, i, 1);
                    }
                }
                else
                {
                    // Started: complete once the run behavior is no longer active — release baseline, remove.
                    if (world.HasComponent<BehaviorState>(attacker)
                        && world.GetComponent<BehaviorState>(attacker).ActiveBehaviorHash != HullDownAttackRunBehaviorId)
                    {
                        s.BaselineReservedMask = (ushort)(s.BaselineReservedMask & ~(1 << s.Runners.BaselineSlotIndex[i]));
                        s.Runners = MemberSlotListOps.SwapRemoveAt(s.Runners, i);
                    }
                }
            }

            return s;
        }

        /// <summary>Number of runners still active after <see cref="Update"/> — the graph compares this
        /// against 0 to return Success (wave complete) vs Running.</summary>
        public static int ActiveCount(WaveState s) => s.Runners.Count;

        // ── tree-integration overloads (architect Q#9) ──────────────────────────
        // The integrated IsWaveCompleted blueprint shares state via HillAttackSharedState (GetShared/
        // SetShared), not a private WaveState WorkingState var. These thin adapters project the three
        // wave fields into a WaveState, reuse the proven WaveState monitor above, and write them back --
        // so the integrated node stays a thin GetShared -> Update -> SetShared wrapper with zero logic
        // duplication.

        /// <summary>Runs the wave-completion monitor over the shared commander struct (projects the
        /// <c>ActiveRunners</c>/<c>BurnedSlotsMask</c>/<c>BaselineReservedMask</c> fields into a
        /// <see cref="WaveState"/>, calls <see cref="Update(WaveState, ISimulationView)"/>, writes them
        /// back), returning the mutated <see cref="HillAttackSharedState"/>.</summary>
        public static HillAttackSharedState Update(HillAttackSharedState s, ISimulationView view)
        {
            var wave = new WaveState
            {
                Runners              = s.ActiveRunners,
                BurnedSlotsMask      = s.BurnedSlotsMask,
                BaselineReservedMask = s.BaselineReservedMask,
            };
            wave = Update(wave, view);
            s.ActiveRunners        = wave.Runners;
            s.BurnedSlotsMask      = wave.BurnedSlotsMask;
            s.BaselineReservedMask = wave.BaselineReservedMask;
            return s;
        }

        /// <summary>Active-runner count off the shared struct (== <c>ActiveRunners.Count</c>).</summary>
        public static int ActiveCount(HillAttackSharedState s) => s.ActiveRunners.Count;
    }
}
