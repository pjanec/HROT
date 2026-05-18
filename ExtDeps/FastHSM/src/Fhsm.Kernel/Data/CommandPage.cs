using System.Runtime.InteropServices;

namespace Fhsm.Kernel.Data
{
    /// <summary>
    /// Command buffer page (4096 bytes).
    /// ARCHITECT DECISION Q2: Fixed 4KB pages for command allocation.
    /// Simple, allocator-friendly, and standard page size.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 4096)]
    public unsafe struct CommandPage
    {
        // === Header (16 bytes) ===
        [FieldOffset(0)] public ushort BytesUsed;       // Current write position
        [FieldOffset(2)] public ushort PageIndex;       // Page number in chain
        [FieldOffset(4)] public uint NextPageOffset;    // Offset to next page (0 = none)
        [FieldOffset(8)] public ulong Reserved;         // Future use

        // === Data (4080 bytes) ===
        [FieldOffset(16)] public fixed byte Data[4080]; // Command data

        // Total: 4096 bytes (16 + 4080)
    }
}
