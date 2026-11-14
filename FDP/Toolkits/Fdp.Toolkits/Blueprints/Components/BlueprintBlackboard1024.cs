using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints.Partitioning;

namespace Fdp.Toolkit.Blueprints.Components;

/// <summary>
/// Small blackboard tier — up to 800 bytes of Blueprint state in up to 12 occurrence
/// slots, plus a 224-byte header+slot-table.
///
/// <para>⚠ MaxSlots was re-picked 4/8/16 → 12/16/16 by <c>B3②</c> (2026-09-20); the numbers live in
/// <c>BlueprintTierLadder</c>. See that file for the measurement that justified it.</para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.BlueprintBlackboard1024)]
[DataPolicy(DataPolicy.NoScenario)]
public unsafe struct BlueprintBlackboard1024
{
    // ⭐⭐ O3a / B3② — THE NUMBERS COME FROM ONE PLACE NOW.
    //   BlueprintTierLadder is an internal, netstandard2.0-subset file LINKED into
    //   Hrot.Blueprints.Compiler, which cannot reference Fdp.Toolkits under netstandard2.0 and was
    //   hard-coding these budgets as literals (design §17.1 N1). ⛔ Do not re-declare a number here.
    public const int TotalSize     = Shared.BlueprintTierLadder.Tier1024TotalSize;
    public const int HeaderSize    = Shared.BlueprintTierLadder.HeaderSize;
    public const int MaxSlots      = Shared.BlueprintTierLadder.Tier1024MaxSlots;
    public const int SlotTableSize = MaxSlots * BlueprintBlackboardPartitions.SlotEntrySize;
    public const int PayloadStart  = HeaderSize + SlotTableSize;
    public const int PayloadSize   = Shared.BlueprintTierLadder.Tier1024PayloadSize;

    /// <summary>
    /// Entire component memory: header (32) + slot table (192) + payload (800) = 1024 bytes.
    /// All access is via BlueprintBlackboardPartitions helpers.
    /// </summary>
    public fixed byte Memory[TotalSize];
}
