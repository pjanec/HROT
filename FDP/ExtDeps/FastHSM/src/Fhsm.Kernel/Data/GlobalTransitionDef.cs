using System.Runtime.InteropServices;

namespace Fhsm.Kernel.Data
{
    /// <summary>
    /// Global transition definition (ROM). Exactly 16 bytes.
    /// Global transitions are checked first every tick (e.g., Death, Stun).
    /// ARCHITECT DECISION Q7: Separate table for O(G) performance.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct GlobalTransitionDef
    {
        // === Topology (4 bytes) ===
        [FieldOffset(0)] public ushort TargetStateIndex;    // Target state (global interrupt destination)
        [FieldOffset(2)] public ushort EventId;             // Event that triggers

        // === Logic (4 bytes) ===
        [FieldOffset(4)] public ushort GuardId;             // Guard condition (0 = none)
        [FieldOffset(6)] public ushort ActionId;            // Effect action (0 = none)

        // === Flags & Priority (4 bytes) ===
        [FieldOffset(8)] public TransitionFlags Flags;      // Behavior flags (2 bytes)
        [FieldOffset(10)] public byte Priority;             // Priority (higher checked first)
        [FieldOffset(11)] public byte Reserved1;            // Alignment

        // === Reserved (4 bytes) ===
        [FieldOffset(12)] public uint Reserved2;            // For future use

        // Total: 16 bytes
    }
}
