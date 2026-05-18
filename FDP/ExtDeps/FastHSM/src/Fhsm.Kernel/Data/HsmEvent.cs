using System.Runtime.InteropServices;

namespace Fhsm.Kernel.Data
{
    /// <summary>
    /// Event structure (exactly 24 bytes).
    /// Events are fixed-size for predictable memory layout and cache efficiency.
    /// Payloads larger than 16 bytes must use indirection (ID-only).
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public struct HsmEvent
    {
        // === Header (8 bytes) ===
        [FieldOffset(0)] public ushort EventId;         // Event type identifier
        [FieldOffset(2)] public EventPriority Priority; // Priority class (1 byte)
        [FieldOffset(3)] public EventFlags Flags;       // Event flags (deferred, etc.)
        [FieldOffset(4)] public uint Timestamp;         // Frame/tick when enqueued

        // === Payload (16 bytes) ===
        [FieldOffset(8)] public unsafe fixed byte Payload[16]; // Inline data or ID

        // Total: 24 bytes (8 + 16)
    }
}
