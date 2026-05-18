using System.Runtime.InteropServices;

namespace Fhsm.Kernel.Data
{
    /// <summary>
    /// Tier 2: Standard enemies, items.
    /// Size: Exactly 128 bytes.
    /// 
    /// ARCHITECT NOTE: Uses HYBRID QUEUE strategy.
    /// One reserved slot for Interrupt events + shared ring for Normal/Low.
    /// [0-23] = Reserved for Interrupt (1 event)
    /// [24-67] = Shared ring for Normal/Low (2 events)
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    public unsafe struct HsmInstance128
    {
        // === Header (16 bytes) ===
        [FieldOffset(0)] public InstanceHeader Header;

        // === Active Configuration (8 bytes) ===
        // Max 4 orthogonal regions
        [FieldOffset(16)] public fixed ushort ActiveLeafIds[4];

        // === Timers (16 bytes) ===
        // 4 timer slots
        [FieldOffset(24)] public fixed uint TimerDeadlines[4];

        // === History/Scratch (16 bytes) ===
        // 8 slots (dual-purpose)
        [FieldOffset(40)] public fixed ushort HistorySlots[8];

        // === Event Queue (72 bytes) - HYBRID QUEUE ===
        // ARCHITECT DECISION Q1: Reserved interrupt slot + shared ring
        // Metadata (4 bytes)
        [FieldOffset(56)] public byte InterruptSlotUsed;    // 0 or 1
        [FieldOffset(57)] public byte EventCount;           // Normal/Low count
        [FieldOffset(58)] public ushort Reserved1;          // Alignment
        
        // Queue data (68 bytes = 24B interrupt + 44B shared)
        // Layout: [0-23] Interrupt reserved, [24-67] Shared for Normal/Low (1-2 events)
        [FieldOffset(60)] public fixed byte EventBuffer[68];

        // Total: 128 bytes (60 + 68 = 128)
    }
}
