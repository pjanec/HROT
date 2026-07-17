using System.Runtime.InteropServices;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// Curated fixed-capacity (8) SoA runner-tracker for the Hill-attack wave core (architect Q#8-A/D:
    /// a curated struct held as ONE Blueprint <c>WorkingState</c> var, mutated via
    /// <see cref="MemberSlotListOps"/> — no generic <c>MemberSlotList</c> node vocabulary, no raw-array
    /// nodes). Mirrors the C# oracle's <c>HillAttackMutableState</c> SoA tracker
    /// (<c>ActiveEntityPacked[8]</c>/<c>ActiveSlotIndex[8]</c>/<c>ReturnBaselineSlotIndex[8]</c>/
    /// <c>HasStartedRun[8]</c> + a live count), which <c>DispatchWaveWithTargets</c> appends to and
    /// <c>IsWaveCompleted</c> swap-removes from. Capacity 8 matches the oracle's
    /// <c>ActiveAttackerCount &lt; 8</c> cap and its <c>fixed [8]</c> buffers.
    /// <para>
    /// Blittable/unmanaged (only a scalar + <c>fixed</c> buffers) so it fits an AiPrimitive
    /// <c>WorkingState</c> partition slot. The <see cref="MemberSlotListOps"/> verbs take it BY VALUE and
    /// return the mutated copy (<c>ws.Tracker = MemberSlotListOps.Add(ws.Tracker, …)</c>) — the shipped
    /// GetVariable→FunctionCall→SetVariable pattern, no by-ref/new compiler capability required.
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct MemberSlotList
    {
        /// <summary>Number of live entries (0..8).</summary>
        public int Count;

        /// <summary>Packed <c>Entity</c> handle per live runner (oracle <c>ActiveEntityPacked</c>).</summary>
        public fixed long EntityPacked[8];

        /// <summary>Assigned firing-line slot index per runner (oracle <c>ActiveSlotIndex</c>).</summary>
        public fixed byte SlotIndex[8];

        /// <summary>Reserved return-baseline slot index per runner (oracle <c>ReturnBaselineSlotIndex</c>).</summary>
        public fixed byte BaselineSlotIndex[8];

        /// <summary>Run-started latch per runner: 0 = intent still propagating, 1 = HullDownAttackRun seen
        /// (oracle <c>HasStartedRun</c>).</summary>
        public fixed byte Started[8];
    }
}
