using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints.Partitioning;

namespace Fdp.Toolkit.Blueprints.Components;

/// <summary>
/// Small blackboard tier -- up to 928 bytes of Blueprint state plus a 96-byte header+slot-table.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.BlueprintBlackboard1024)]
public unsafe struct BlueprintBlackboard1024
{
    public const int TotalSize     = 1024;
    public const int HeaderSize    = 32;
    public const int MaxSlots      = 4;
    public const int SlotTableSize = MaxSlots * BlueprintBlackboardPartitions.SlotEntrySize; // 64
    public const int PayloadStart  = HeaderSize + SlotTableSize;                              // 96
    public const int PayloadSize   = TotalSize - PayloadStart;                                // 928

    /// <summary>
    /// Entire component memory: header (32) + slot table (64) + payload (928) = 1024 bytes.
    /// All access is via BlueprintBlackboardPartitions helpers.
    /// </summary>
    public fixed byte Memory[TotalSize];
}
