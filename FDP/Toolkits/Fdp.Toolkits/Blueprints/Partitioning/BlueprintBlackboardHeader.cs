using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Blueprints.Partitioning;

/// <summary>
/// Header written at offset 0 of every blackboard slot.
/// Magic bytes identify the slot as initialized.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct BlueprintBlackboardHeader
{
    public const uint MagicValue = 0x42503132u; // 'BP12' in ASCII
    public uint Magic;           // 0 = uninitialized, MagicValue = initialized
    public int SlotCount;        // how many blueprint slots are active
    public fixed byte Reserved[8];
}
