using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Blueprints.Partitioning;

/// <summary>
/// 16-byte entry in the slot table of a Blueprint blackboard component.
/// Identifies the Blueprint occupying a payload slot and its reload version.
/// Per Runtime DD §4.4.
/// Note: StructureHash is stored as uint (32 bits) rather than ulong to keep the
/// struct at exactly 16 bytes (int+uint+ushort+ushort+uint = 16, no padding).
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 16)]
public struct BlueprintSlotEntry
{
    public int    BlueprintId;      // 4 bytes -- 0 = unused slot
    public uint   InstanceVersion;  // 4 bytes -- bumped on hard reload
    public ushort PayloadOffset;    // 2 bytes -- byte offset from component start
    public ushort PayloadSize;      // 2 bytes -- length of payload in bytes
    public uint   StructureHash;    // 4 bytes -- lower 32 bits of the Blueprint's StructureHash
}

