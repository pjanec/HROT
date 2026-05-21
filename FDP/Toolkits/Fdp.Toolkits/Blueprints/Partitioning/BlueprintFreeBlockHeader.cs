using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Blueprints.Partitioning;

/// <summary>
/// 4-byte in-line header at the start of every free block in the blackboard payload.
/// Free blocks are threaded into a singly-linked list sorted by ascending offset.
/// Per Runtime DD §4.5.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 4)]
public struct BlueprintFreeBlockHeader
{
    /// <summary>Payload offset (from component start) of the next free block; 0 = end of list.</summary>
    public ushort NextFreeOffset;

    /// <summary>Size of this free block in bytes (includes these 4 header bytes).</summary>
    public ushort Size;
}
