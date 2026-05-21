using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Blueprints.Partitioning;

/// <summary>
/// 32-byte header written at offset 0 of every Blueprint blackboard component.
/// Magic value 0x42504257 identifies an initialized component.
/// Per Runtime DD §4.3.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 32)]
public struct BlueprintBlackboardHeader
{
    /// <summary>Magic constant 'BPBW': 0x42504257. Zero = uninitialized.</summary>
    public const uint MagicValue = 0x42504257u;

    public uint   MagicAndVersion;  // 4 bytes
    public byte   SlotCount;        // 1 byte  -- number of allocated slots (<= MaxSlots)
    public byte   MaxSlots;         // 1 byte  -- capacity per tier
    public ushort FreeListHead;     // 2 bytes -- payload offset of first free block (0 = none)
    public ushort PayloadStart;     // 2 bytes -- constant per tier (redundant but explicit)
    public ushort PayloadSize;      // 2 bytes -- constant per tier
    public ushort PayloadFree;      // 2 bytes -- bytes currently free
    public ushort PayloadHighWater; // 2 bytes -- highest allocated payload offset
    public ulong  Reserved;         // 8 bytes -- padding to 32 bytes; reserved for future use
}

