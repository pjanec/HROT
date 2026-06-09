using System.Runtime.CompilerServices;
using Fdp.Toolkit.Blueprints.Partitioning;

namespace Fdp.Toolkit.Blueprints;

/// <summary>A single slot summary, produced by <see cref="BlueprintTierSummary.Read"/>.</summary>
public readonly record struct SlotSummary(
    Guid AssetId,
    int BlueprintId,
    string Name,
    uint InstanceVersion,
    ushort PayloadOffset,
    ushort PayloadSize);

/// <summary>
/// Read-only, allocation-free scanner that extracts blueprint slot summaries
/// from a blackboard tier's unmanaged memory. Used by the Entity Inspector
/// renderers to replace the raw byte-dump.
/// </summary>
public static unsafe class BlueprintTierSummary
{
    /// <summary>
    /// Reads all allocated slots from the given blackboard memory.
    /// Returns an empty list if the tier is uninitialized (no header magic).
    /// </summary>
    /// <param name="memory">Pointer to the blackboard component's fixed buffer.</param>
    /// <param name="registry">Blueprint registry for id→name resolution.</param>
    /// <returns>A list of slot summaries (one per allocated slot).</returns>
    public static List<SlotSummary> Read(byte* memory, BlueprintRegistry registry)
    {
        var result = new List<SlotSummary>();
        AppendSlots(memory, registry, result);
        return result;
    }

    /// <summary>
    /// Same as <see cref="Read"/> but appends into an existing list to avoid allocation.
    /// </summary>
    public static void AppendSlots(byte* memory, BlueprintRegistry registry, List<SlotSummary> target)
    {
        // Check header magic — uninitialized tier is all zeros.
        ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(memory);
        if (header.MagicAndVersion != BlueprintBlackboardHeader.MagicValue)
            return;

        int count = BlueprintBlackboardPartitions.GetSlotCount(memory);
        for (int i = 0; i < count; i++)
        {
            ref var slot = ref BlueprintBlackboardPartitions.GetSlot(memory, i);
            if (slot.BlueprintId == 0) continue;

            Guid assetId = Guid.Empty;
            string name = $"0x{slot.BlueprintId:X8}";
            if (registry.TryGetById(slot.BlueprintId, out var def) && def != null)
            {
                assetId = def.AssetId;
                name = def.Name;
            }

            target.Add(new SlotSummary(
                assetId,
                slot.BlueprintId,
                name,
                slot.InstanceVersion,
                slot.PayloadOffset,
                slot.PayloadSize));
        }
    }
}
