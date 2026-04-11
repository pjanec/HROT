using System.Runtime.InteropServices;

namespace Fdp.Kernel
{
    [StructLayout(LayoutKind.Explicit, Size = 8)]
    public struct DISEntityType
    {
        // --- The 64-bit "Single Integer" View ---
        [FieldOffset(0)] public ulong Value;

        // --- The Human-Readable View (Standard DIS) ---
        // Note: Field offsets allow overlapping overlay on the 'Value' field.
        // Kind is at offset 7 (MSB in Little Endian when reading Value)
        [FieldOffset(7)] public byte Kind;          // 1 = Platform, 2 = Munition, etc.
        [FieldOffset(6)] public byte Domain;        // 1 = Land, 2 = Air, etc.
        [FieldOffset(4)] public ushort Country;     // 16-bit Country Code
        [FieldOffset(3)] public byte Category;      // e.g., Tank vs Truck, Fixed vs Rotary
        [FieldOffset(2)] public byte Subcategory;   // e.g., M1A1 vs T-72
        [FieldOffset(1)] public byte Specific;      // Specific variation
        [FieldOffset(0)] public byte Extra;         // Extra
    }
}
