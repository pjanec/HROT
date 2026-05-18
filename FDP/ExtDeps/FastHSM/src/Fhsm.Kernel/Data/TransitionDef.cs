using System.Runtime.InteropServices;

namespace Fhsm.Kernel.Data
{
    /// <summary>
    /// Transition definition (ROM). Exactly 16 bytes.
    /// Defines a single transition between states.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct TransitionDef
    {
        // === Topology (8 bytes) ===
        [FieldOffset(0)] public ushort SourceStateIndex;    // Source state
        [FieldOffset(2)] public ushort TargetStateIndex;    // Target state
        [FieldOffset(4)] public ushort EventId;             // Event that triggers (0 = completion)
        [FieldOffset(6)] public ushort SyncGroupId;         // Sync group (0 = none)

        // === Logic (4 bytes) ===
        [FieldOffset(8)] public ushort GuardId;             // Guard condition (0 = none)
        [FieldOffset(10)] public ushort ActionId;           // Effect action (0 = none)

        // === Flags & Cost (4 bytes) ===
        [FieldOffset(12)] public TransitionFlags Flags;     // Behavior + priority (2 bytes)
        [FieldOffset(14)] public ushort Cost;               // LCA cost (steps Up + steps Down)

        // Total: 16 bytes
    }
}
