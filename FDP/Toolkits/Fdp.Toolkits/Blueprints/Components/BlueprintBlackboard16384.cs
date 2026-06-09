using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints.Partitioning;

namespace Fdp.Toolkit.Blueprints.Components;

/// <summary>
/// Large blackboard tier -- up to 16096 bytes of Blueprint state plus a 288-byte header+slot-table.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.BlueprintBlackboard16384)]
[DataPolicy(DataPolicy.NoSave)]
public unsafe struct BlueprintBlackboard16384
{
    public const int TotalSize     = 16384;
    public const int HeaderSize    = 32;
    public const int MaxSlots      = 16;
    public const int SlotTableSize = MaxSlots * BlueprintBlackboardPartitions.SlotEntrySize; // 256
    public const int PayloadStart  = HeaderSize + SlotTableSize;                              // 288
    public const int PayloadSize   = TotalSize - PayloadStart;                                // 16096

    /// <summary>
    /// Entire component memory: header (32) + slot table (256) + payload (16096) = 16384 bytes.
    /// All access is via BlueprintBlackboardPartitions helpers.
    /// </summary>
    public fixed byte Memory[TotalSize];
}
