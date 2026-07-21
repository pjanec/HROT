using System.Runtime.InteropServices;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// Category-1 shared commander state for the Hill-attack TREE-INTEGRATION track (architect Q#9-A):
    /// the blueprint-world equivalent of the C# oracle's single behavior-scoped
    /// <c>HillAttackMutableState</c>. When the per-node twins are assembled into a running commander
    /// <c>.btree.json</c>, they can no longer each own a private <c>WorkingState</c> (they must see each
    /// other's writes: <c>CalculateSegments</c> sets <see cref="TotalSlots"/>, <c>DispatchWave</c> reads it;
    /// <c>DispatchWave</c> fills <see cref="ActiveRunners"/>, <c>IsWaveCompleted</c> drains it; …). Per the
    /// architect, the sanctioned model is: each integrated blueprint leaves its native <c>WorkingState</c>
    /// EMPTY and converses over THIS standalone struct via <c>GetShared</c>/<c>SetShared</c>
    /// (Role=State, Scope=Entity → <c>BlueprintSharedState.TryGetShared/TrySetShared</c>) — NOT a
    /// behavior-scoped shared <c>WorkingState</c> slot (each blueprint generates a distinct
    /// <c>_Bp+WorkingState</c> type, which would collide). Read/mutated by value via
    /// <see cref="HillAttackSharedStateOps"/>, exactly like the migration's other curated structs.
    /// Blittable/unmanaged. Does not modify the C# oracle.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HillAttackSharedState
    {
        /// <summary>Firing-line slot count (oracle <c>TotalSlots</c>).</summary>
        public int TotalSlots;

        /// <summary>Permanently-burned firing slots.</summary>
        public ushort BurnedSlotsMask;

        /// <summary>Firing slots used by the current wave.</summary>
        public ushort WaveUsedSlotsMask;

        /// <summary>Reserved return-baseline slots.</summary>
        public ushort BaselineReservedMask;

        /// <summary>Current wave's active-runner tracker (oracle SoA; == <c>ActiveAttackerCount</c> via its Count).</summary>
        public MemberSlotList ActiveRunners;

        /// <summary>In-flight EQS batch request slot id (<c>-1</c> = none).</summary>
        public long CachedEqsRequestId;

        /// <summary>Cached EQS target-pool handle (<c>-1</c> = none).</summary>
        public int CachedTargetGroupHandle;

        /// <summary>SimulationTime at EQS request submission (timeout base).</summary>
        public float EqsRequestTime;

        /// <summary>Current wave parity (0/1).</summary>
        public byte CurrentWave;
    }
}
