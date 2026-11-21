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

        // === Flags, Cost & Priority (4 bytes) ===
        [FieldOffset(12)] public TransitionFlags Flags;     // Behavior flags (2 bytes)
        [FieldOffset(14)] public byte Cost;                 // LCA cost (steps Up + steps Down)

        /// <summary>
        /// ⭐⭐ <b>CE-395 — the transition's priority, 0-255, higher wins.</b> The SAME representation as
        /// <see cref="GlobalTransitionDef.Priority"/>. 🔴 It used to be packed into 4 bits of <see cref="Flags"/>, and the
        /// writer (bits 8-11, <c>&amp; 0x0F</c>) and the reader (bits 12-15) disagreed ⇒ every priority read 0 and the
        /// first matching transition always won. <see cref="Cost"/> is a byte by construction (LCA steps), so the byte
        /// it freed carries the full authored value instead of a truncated nibble.
        /// </summary>
        [FieldOffset(15)] public byte Priority;

        // Total: 16 bytes
    }
}
