using System.Runtime.InteropServices;

namespace Fhsm.Kernel.Data
{
    /// <summary>
    /// Orthogonal region definition (ROM). Exactly 8 bytes.
    /// Defines a concurrent sub-state-machine within a composite state.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 8)]
    public struct RegionDef
    {
        // === Topology (4 bytes) ===
        [FieldOffset(0)] public ushort ParentStateIndex;    // Composite state containing this region
        [FieldOffset(2)] public ushort InitialStateIndex;   // Initial state of region

        // === Metadata (4 bytes) ===
        [FieldOffset(4)] public byte Priority;              // Arbitration priority (higher = wins)
        [FieldOffset(5)] public byte Reserved1;             // For alignment
        [FieldOffset(6)] public ushort Reserved2;           // For future use

        // Total: 8 bytes
    }
}
