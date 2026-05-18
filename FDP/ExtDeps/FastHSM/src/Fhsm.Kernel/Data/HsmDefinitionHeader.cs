using System.Runtime.InteropServices;

namespace Fhsm.Kernel.Data
{
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct HsmDefinitionHeader
    {
        [FieldOffset(0)] public uint Magic;             // 0x4D534846 ('FHSM')
        [FieldOffset(4)] public ushort FormatVersion;
        [FieldOffset(6)] public ushort Reserved1;
        
        [FieldOffset(8)] public uint StructureHash;     // Topology hash
        [FieldOffset(12)] public uint ParameterHash;    // Logic hash
        
        [FieldOffset(16)] public ushort StateCount;
        [FieldOffset(18)] public ushort TransitionCount;
        [FieldOffset(20)] public ushort RegionCount;
        [FieldOffset(22)] public ushort GlobalTransitionCount;
        [FieldOffset(24)] public ushort EventDefinitionCount;
        [FieldOffset(26)] public ushort ActionCount;
        [FieldOffset(28)] public ushort GuardCount;
        
        [FieldOffset(30)] public ushort Reserved2;
        
        public const uint MagicNumber = 0x4D534846;
        
        public bool IsValid() => Magic == MagicNumber;
    }
}
