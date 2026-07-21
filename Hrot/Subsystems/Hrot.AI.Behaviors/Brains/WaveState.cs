using System.Runtime.InteropServices;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// Curated bundle of the wave-monitor mutable state for <c>Condition_IsWaveCompleted</c> (architect
    /// Q#8-A/D): the runner tracker plus the two slot bitmasks the monitor updates as runners die or
    /// finish. Held as ONE Blueprint <c>WorkingState</c> var so <see cref="WaveMonitorOps.Update"/> can
    /// return all three mutated together by value (the whole bundle is written back via one
    /// GetVariable→FunctionCall→SetVariable — no per-field accessors, no by-ref). Blittable/unmanaged.
    /// (<c>IsWaveCompleted</c>'s reverse-walk-with-swap-remove has no visual-node form, so its loop stays
    /// a curated kernel; the visual graph is the thin Running/Success routing around it.)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct WaveState
    {
        /// <summary>Live attackers (oracle SoA tracker).</summary>
        public MemberSlotList Runners;

        /// <summary>Permanently-burned firing slots (a dead runner's slot is burned).</summary>
        public ushort BurnedSlotsMask;

        /// <summary>Reserved return-baseline slots (released when a runner dies or finishes its run).</summary>
        public ushort BaselineReservedMask;
    }
}
