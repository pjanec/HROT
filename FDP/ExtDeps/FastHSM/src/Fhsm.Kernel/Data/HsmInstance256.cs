using System.Runtime.InteropServices;

namespace Fhsm.Kernel.Data
{
    /// <summary>
    /// Tier 3: Player characters, bosses.
    /// Size: Exactly 256 bytes.
    /// 
    /// ARCHITECT NOTE: Uses HYBRID QUEUE strategy.
    /// One reserved slot for Interrupt events + shared ring for Normal/Low.
    /// [0-23] = Reserved for Interrupt (1 event)
    /// [24-155] = Shared ring for Normal/Low (5-6 events)
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    public unsafe struct HsmInstance256
    {
        // === Header (16 bytes) ===
        [FieldOffset(0)] public InstanceHeader Header;

        // === Active Configuration (16 bytes) ===
        // Max 8 orthogonal regions
        [FieldOffset(16)] public fixed ushort ActiveLeafIds[8];

        // === Timers (32 bytes) ===
        // 8 timer slots
        [FieldOffset(32)] public fixed uint TimerDeadlines[8];

        // === History/Scratch (32 bytes) ===
        // 16 slots (dual-purpose)
        [FieldOffset(64)] public fixed ushort HistorySlots[16];

        // === Event Queue (160 bytes) - HYBRID QUEUE ===
        // ARCHITECT DECISION Q1: Reserved interrupt slot + shared ring
        // Metadata (4 bytes)
        [FieldOffset(96)] public byte InterruptSlotUsed;    // 0 or 1
        [FieldOffset(97)] public byte EventCount;           // Normal/Low count
        [FieldOffset(98)] public ushort Reserved1;          // Alignment
        
        // Queue data (156 bytes = 24B interrupt + 132B shared)
        // Layout: [0-23] Interrupt reserved, [24-155] Shared for Normal/Low (5-6 events)
        [FieldOffset(100)] public fixed byte EventBuffer[156];

        // Total: 256 bytes (100 + 156 = 256)
    }
}
