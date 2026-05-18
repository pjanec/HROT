using System.Runtime.InteropServices;

namespace Fhsm.Kernel.Data
{
    /// <summary>
    /// Tier 1: Crowd AI (hordes, simple NPCs).
    /// Size: Exactly 64 bytes.
    /// 
    /// ARCHITECT NOTE (CRITICAL): Uses SINGLE SHARED QUEUE due to space constraints.
    /// Priority events overwrite oldest normal events if full.
    /// Math: 32 bytes / 3 queues = 10 bytes each, but 1 event = 24 bytes.
    /// Therefore, separate queues are mathematically impossible for Tier 1.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public unsafe struct HsmInstance64
    {
        // === Header (16 bytes) ===
        [FieldOffset(0)] public InstanceHeader Header;

        // === Active Configuration (4 bytes) ===
        // Max 2 orthogonal regions supported
        [FieldOffset(16)] public fixed ushort ActiveLeafIds[2];

        // === Timers (8 bytes) ===
        // 2 timer slots × 4 bytes = tick deadlines
        [FieldOffset(24)] public fixed uint TimerDeadlines[2];

        // === History/Scratch (4 bytes) ===
        // 2 slots for history OR scratch registers (dual-purpose)
        // ARCHITECT NOTE: Can be used for simple counters/flags
        [FieldOffset(32)] public fixed ushort HistorySlots[2];

        // === Event Queue (28 bytes) - SINGLE SHARED QUEUE ===
        // ARCHITECT DECISION Q1: Tier 1 special case
        // Can hold 1 full event (24B) with 4B metadata
        // Priority logic: Interrupt events can evict oldest Normal event
        [FieldOffset(36)] public byte EventCount;       // Current count (max 1)
        [FieldOffset(37)] public byte Reserved1;        // Alignment
        [FieldOffset(38)] public ushort Reserved2;      // Future use
        [FieldOffset(40)] public fixed byte EventBuffer[24]; // 1 event (24B)

        // Total: 64 bytes (40 + 24 = 64)
    }
}
