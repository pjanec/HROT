using System.Runtime.InteropServices;

namespace Fhsm.Kernel.Data
{
    /// <summary>
    /// Common instance header (16 bytes).
    /// Shared by all tier variants (64B, 128B, 256B).
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct InstanceHeader
    {
        // === Identity (8 bytes) ===
        [FieldOffset(0)] public uint MachineId;         // DefinitionBlob structure hash
        [FieldOffset(4)] public uint RngState;          // Deterministic RNG state (Initialized with Seed)

        // === State (4 bytes) ===
        [FieldOffset(8)] public ushort Generation;      // Increments on hard reset
        [FieldOffset(10)] public InstanceFlags Flags;   // Status flags (1 byte)
        [FieldOffset(11)] public InstancePhase Phase;   // Current execution phase (1 byte)
        
        // === Execution Tracking (4 bytes) ===
        [FieldOffset(12)] public byte MicroStep;        // Current RTC microstep
        [FieldOffset(13)] public byte QueueHead;        // Event queue read cursor
        [FieldOffset(14)] public byte ActiveTail;       // Active queue write cursor
        [FieldOffset(15)] public byte DeferredTail;     // Deferred queue write cursor

        // Total: 16 bytes
    }
}
