using Hrot.Editor.AiShared;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// Curated by-value field accessors over <see cref="HillAttackSharedState"/> for the tree-integration
    /// track (architect Q#9-A). An integrated blueprint reads the whole struct once via <c>GetShared</c>,
    /// pulls the fields it needs through the <c>Get*</c> readers, mutates via the <c>With*</c> writers
    /// (each returns the modified copy — the migration's proven by-value pattern), and writes the struct
    /// back once via <c>SetShared</c>. Trivial one-liners; kept curated because a blueprint has no
    /// generic struct-field read/write node (and none is warranted for one commander struct). Does not
    /// modify the C# oracle.
    /// </summary>
    public static class HillAttackSharedStateOps
    {
        // ── readers ─────────────────────────────────────────────────────────────
        [BlueprintCallable("SharedState")]
        public static int          GetTotalSlots(HillAttackSharedState s)            => s.TotalSlots;
        [BlueprintCallable("SharedState")]
        public static ushort       GetBurnedSlotsMask(HillAttackSharedState s)       => s.BurnedSlotsMask;
        [BlueprintCallable("SharedState")]
        public static ushort       GetWaveUsedSlotsMask(HillAttackSharedState s)     => s.WaveUsedSlotsMask;
        [BlueprintCallable("SharedState")]
        public static ushort       GetBaselineReservedMask(HillAttackSharedState s)  => s.BaselineReservedMask;
        [BlueprintCallable("SharedState")]
        public static MemberSlotList GetActiveRunners(HillAttackSharedState s)       => s.ActiveRunners;
        [BlueprintCallable("SharedState")]
        public static long         GetCachedEqsRequestId(HillAttackSharedState s)    => s.CachedEqsRequestId;
        [BlueprintCallable("SharedState")]
        public static int          GetCachedTargetGroupHandle(HillAttackSharedState s) => s.CachedTargetGroupHandle;
        [BlueprintCallable("SharedState")]
        public static float        GetEqsRequestTime(HillAttackSharedState s)        => s.EqsRequestTime;
        /// <summary>Current wave parity as <c>int</c> (the wave kernels take <c>int currentWave</c>).</summary>
        [BlueprintCallable("SharedState")]
        public static int          GetCurrentWave(HillAttackSharedState s)           => s.CurrentWave;

        // ── writers (return the mutated copy; caller SetShares it back) ──────────
        [BlueprintCallable("SharedState")]
        public static HillAttackSharedState WithTotalSlots(HillAttackSharedState s, int v)            { s.TotalSlots = v; return s; }
        [BlueprintCallable("SharedState")]
        public static HillAttackSharedState WithBurnedSlotsMask(HillAttackSharedState s, ushort v)    { s.BurnedSlotsMask = v; return s; }
        [BlueprintCallable("SharedState")]
        public static HillAttackSharedState WithWaveUsedSlotsMask(HillAttackSharedState s, ushort v)  { s.WaveUsedSlotsMask = v; return s; }
        [BlueprintCallable("SharedState")]
        public static HillAttackSharedState WithBaselineReservedMask(HillAttackSharedState s, ushort v){ s.BaselineReservedMask = v; return s; }
        [BlueprintCallable("SharedState")]
        public static HillAttackSharedState WithActiveRunners(HillAttackSharedState s, MemberSlotList v){ s.ActiveRunners = v; return s; }
        [BlueprintCallable("SharedState")]
        public static HillAttackSharedState WithCachedEqsRequestId(HillAttackSharedState s, long v)   { s.CachedEqsRequestId = v; return s; }
        [BlueprintCallable("SharedState")]
        public static HillAttackSharedState WithCachedTargetGroupHandle(HillAttackSharedState s, int v){ s.CachedTargetGroupHandle = v; return s; }
        [BlueprintCallable("SharedState")]
        public static HillAttackSharedState WithEqsRequestTime(HillAttackSharedState s, float v)      { s.EqsRequestTime = v; return s; }
        /// <summary>Sets the wave parity from an <c>int</c> (stored as <c>byte</c>).</summary>
        [BlueprintCallable("SharedState")]
        public static HillAttackSharedState WithCurrentWave(HillAttackSharedState s, int v)           { s.CurrentWave = (byte)v; return s; }
    }
}
