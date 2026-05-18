using System.Runtime.InteropServices;

namespace Fhsm.Kernel.Data
{
    /// <summary>
    /// Entry in the linker table mapping function IDs to addresses.
    /// Size: 16 bytes (cache-friendly)
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct LinkerTableEntry
    {
        /// <summary>FNV-1a hash of function name</summary>
        [FieldOffset(0)] public ushort FunctionId;
        
        /// <summary>Reserved for alignment</summary>
        [FieldOffset(2)] public ushort Reserved;
        
        /// <summary>Function pointer (64-bit)</summary>
        [FieldOffset(8)] public long FunctionPointer;
    }
}
