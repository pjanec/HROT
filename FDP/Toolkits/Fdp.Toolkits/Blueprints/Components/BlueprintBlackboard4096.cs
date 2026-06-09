using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints.Partitioning;

namespace Fdp.Toolkit.Blueprints.Components;

/// <summary>
/// Medium blackboard tier -- up to 3936 bytes of Blueprint state plus a 160-byte header+slot-table.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.BlueprintBlackboard4096)]
[DataPolicy(DataPolicy.NoSave)]
public unsafe struct BlueprintBlackboard4096
{
    public const int TotalSize     = 4096;
    public const int HeaderSize    = 32;
    public const int MaxSlots      = 8;
    public const int SlotTableSize = MaxSlots * BlueprintBlackboardPartitions.SlotEntrySize; // 128
    public const int PayloadStart  = HeaderSize + SlotTableSize;                              // 160
    public const int PayloadSize   = TotalSize - PayloadStart;                                // 3936

    /// <summary>
    /// Entire component memory: header (32) + slot table (128) + payload (3936) = 4096 bytes.
    /// All access is via BlueprintBlackboardPartitions helpers.
    /// </summary>
    public fixed byte Memory[TotalSize];
}
