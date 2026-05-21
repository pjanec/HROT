using Fdp.Core;
using Fdp.Toolkit.Blueprints.Components;

namespace Fdp.Toolkit.Blueprints.Partitioning;

/// <summary>
/// Manages per-entity Blueprint slot allocation within the blackboard components.
/// Minimal stub for Phase 1/2; full allocator implementation in TASK-RT-004.
/// </summary>
public static class BlueprintBlackboardPartitions
{
    /// <summary>Byte size of a single BlueprintSlotEntry. Used in tier constant arithmetic.</summary>
    public const int SlotEntrySize = 16;

    /// <summary>
    /// Attempts to attach a Blueprint definition to an entity's blackboard slot.
    /// Stub always returns false (no slots allocated until TASK-RT-004).
    /// </summary>
    public static bool TryAttach(
        EntityRepository  repo,
        Entity            entity,
        BlueprintDefinition def,
        BlackboardTier    tier,
        out int           slotIndex)
    {
        slotIndex = -1;
        return false;
    }

    /// <summary>
    /// Attempts to find the payload offset for a given blueprint ID on an entity.
    /// Stub always returns false (no slots allocated until TASK-RT-004).
    /// </summary>
    public static bool TryGetSlotOffset(
        EntityRepository repo,
        Entity           entity,
        int              blueprintId,
        out BlackboardTier tier,
        out int          slotIndex,
        out int          payloadOffset)
    {
        tier         = BlackboardTier.B1024;
        slotIndex    = -1;
        payloadOffset = -1;
        return false;
    }
}

