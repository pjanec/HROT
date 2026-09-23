using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints.Partitioning;

namespace Fdp.Toolkit.Blueprints.Components;

/// <summary>
/// Smallest blackboard tier — up to 176 bytes of Blueprint state in up to 3 occurrence slots,
/// plus an 80-byte header+slot-table.
///
/// <para>⭐⭐ <c>O3b</c> / task <c>B4</c>. It exists to price <b>the simple case</b>: 📐 measured over
/// all 30 generated behaviours, <b>25 of them (83 %)</b> fit here once <c>O4</c>'s root occurrence is
/// counted. ⛔ Without it every AI entity pays the 1024 tier — <b>4×</b> — for a root occurrence and
/// a slot or two. 📄 design §17, "B4's PRE-MEASUREMENT".</para>
///
/// <para>⛔ The numbers come from <c>BlueprintTierLadder</c>, which is LINKED into
/// <c>Hrot.Blueprints.Compiler</c> across the netstandard wall. Do not re-declare one here.</para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[ComponentId(GlobalComponentIds.BlueprintBlackboard256)]
[DataPolicy(DataPolicy.NoScenario)]
public unsafe struct BlueprintBlackboard256
{
    public const int TotalSize     = Shared.BlueprintTierLadder.Tier256TotalSize;
    public const int HeaderSize    = Shared.BlueprintTierLadder.HeaderSize;
    public const int MaxSlots      = Shared.BlueprintTierLadder.Tier256MaxSlots;
    public const int SlotTableSize = MaxSlots * BlueprintBlackboardPartitions.SlotEntrySize;
    public const int PayloadStart  = HeaderSize + SlotTableSize;
    public const int PayloadSize   = Shared.BlueprintTierLadder.Tier256PayloadSize;

    /// <summary>
    /// Entire component memory: header (32) + slot table (48) + payload (176) = 256 bytes.
    /// All access is via BlueprintBlackboardPartitions helpers.
    /// </summary>
    public fixed byte Memory[TotalSize];
}
