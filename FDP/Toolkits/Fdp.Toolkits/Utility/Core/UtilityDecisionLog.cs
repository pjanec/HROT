using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Utility
{
    /// <summary>The ranked results of one logged decision (<see cref="UtilityDecisionLog.MaxRanked"/> slots).</summary>
    [InlineArray(UtilityDecisionLog.MaxRanked)]
    public struct UtilityLogRanked
    {
        private UtilityResultEntry _element;
    }

    /// <summary>
    /// The last result of ONE decision on one unit: its ranking after hysteresis, the winner, the winner before it, and
    /// how often it changed. A winner is an option id (an option decision) or a packed candidate handle (a ranking).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UtilityLogSlot
    {
        /// <summary>The decision id; 0 = an empty slot.</summary>
        public int   DecisionId;
        /// <summary>How many times the decision was scored while the unit was observed.</summary>
        public int   EvalCount;
        /// <summary>The order this slot was last written in (the oldest slot is reused first).</summary>
        public int   LastSeq;
        /// <summary>How many times the winner changed.</summary>
        public int   SwitchCount;
        /// <summary>The winner: an option id, or a packed candidate handle.</summary>
        public long  Winner;
        /// <summary>The winner before the last change (0 = none yet).</summary>
        public long  PreviousWinner;
        /// <summary>The top score minus the runner-up's, after hysteresis.</summary>
        public float Margin;
        /// <summary>Valid entries in <see cref="Ranked"/>.</summary>
        public int   Count;
        /// <summary>The ranking, best first, after hysteresis.</summary>
        public UtilityLogRanked Ranked;

        /// <summary>The ranking as a read-only span (bypasses the [InlineArray] defensive-copy trap).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly ReadOnlySpan<UtilityResultEntry> RankedRO() =>
            MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<UtilityLogRanked, UtilityResultEntry>(ref Unsafe.AsRef(in Ranked)), UtilityDecisionLog.MaxRanked);
    }

    /// <summary>Slots of <see cref="UtilityDecisionLog"/>.</summary>
    [InlineArray(UtilityDecisionLog.MaxDecisions)]
    public struct UtilityLogSlots
    {
        private UtilityLogSlot _element;
    }

    /// <summary>
    /// ⭐ <c>CE-3069</c> G2 — a per-decision record of what a unit's utility decisions chose, for the debug API
    /// (<c>GET /entities/{id}/utility</c>). 📄 <c>docs/DESIGN_Utility_AI_Demo_Scenarios.md</c> §5.1.
    /// <para>⭐ Why not <see cref="UtilityResultBuffer"/>: a unit has ONE buffer and every decision it runs overwrites it
    /// (CombatPosture and the threat ranking both run every tick), so the buffer shows whichever ran last. This keeps one
    /// slot PER decision. Attached only while the unit is observed (<c>POST /trace/observe</c>, through
    /// <c>TraceBufferLifecycleSystem</c>); the scorer writes it only then.</para>
    /// </summary>
    [ComponentId(GlobalComponentIds.UtilityDecisionLog)]
    [DataPolicy(DataPolicy.NoScenario)]
    [StructLayout(LayoutKind.Sequential)]
    public struct UtilityDecisionLog
    {
        /// <summary>Decisions logged per unit.</summary>
        public const int MaxDecisions = 4;
        /// <summary>Ranked entries kept per decision.</summary>
        public const int MaxRanked = 8;

        /// <summary>The write counter behind <see cref="UtilityLogSlot.LastSeq"/>.</summary>
        public int Seq;
        /// <summary>One slot per decision.</summary>
        public UtilityLogSlots Slots;

        /// <summary>The slots as a writable span.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<UtilityLogSlot> SlotsRW() =>
            MemoryMarshal.CreateSpan(ref Unsafe.As<UtilityLogSlots, UtilityLogSlot>(ref Slots), MaxDecisions);

        /// <summary>The slots as a read-only span.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly ReadOnlySpan<UtilityLogSlot> SlotsRO() =>
            MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<UtilityLogSlots, UtilityLogSlot>(ref Unsafe.AsRef(in Slots)), MaxDecisions);

        /// <summary>
        /// Records a decision's final ranking (after hysteresis). <paramref name="isOption"/>: the winner is the top entry's
        /// option id; otherwise its candidate handle. Reuses the decision's own slot, else an empty one, else the oldest.
        /// </summary>
        public void Record(int decisionId, in UtilityResultBuffer result, bool isOption)
        {
            var slots = SlotsRW();
            int pick = -1, oldest = 0;
            for (int i = 0; i < MaxDecisions; i++)
            {
                if (slots[i].DecisionId == decisionId) { pick = i; break; }
                if (pick < 0 && slots[i].DecisionId == 0) pick = i;
                if (slots[i].LastSeq < slots[oldest].LastSeq) oldest = i;
            }
            if (pick < 0) pick = oldest;

            ref var s = ref slots[pick];
            if (s.DecisionId != decisionId) s = default;   // a new decision takes the slot whole
            s.DecisionId = decisionId;
            s.EvalCount++;
            s.LastSeq = ++Seq;
            s.Margin  = result.RunnerUpMargin;

            var src = result.GetSpanRO();
            int n = Math.Min(result.Count, MaxRanked);
            var dst = MemoryMarshal.CreateSpan(ref Unsafe.As<UtilityLogRanked, UtilityResultEntry>(ref s.Ranked), MaxRanked);
            for (int i = 0; i < MaxRanked; i++) dst[i] = i < n ? src[i] : default;
            s.Count = n;

            long winner = n == 0 ? 0 : (isOption ? src[0].WinningPostureId : src[0].CandidateHandle);
            if (winner != s.Winner)
            {
                if (s.Winner != 0 || s.EvalCount > 1) s.SwitchCount++;
                s.PreviousWinner = s.Winner;
                s.Winner = winner;
            }
        }
    }
}
