using Fdp.Core;
using Fdp.Toolkit.Blueprints.Components;

namespace Fdp.Toolkit.Blueprints.Partitioning;

/// <summary>
/// Manages per-entity Blueprint slot allocation within the blackboard components.
/// Minimal stub for Phase 1; full implementation in TASK-RT-004.
/// </summary>
public static class BlueprintBlackboardPartitions
{
    /// <summary>
    /// Attempts to attach a Blueprint definition to an entity's blackboard slot.
    /// Stub always returns false (no slots allocated yet until Phase 2).
    /// </summary>
    public static bool TryAttach(
        EntityRepository repo,
        Entity entity,
        BlueprintDefinition def,
        BlackboardTier tier,
        out int slotIndex)
    {
        slotIndex = -1;
        return false;
    }

    /// <summary>
    /// Attempts to find the slot for a given blueprint on an entity.
    /// Stub always returns false.
    /// </summary>
    public static bool TryGetSlotOffset(
        EntityRepository repo,
        Entity entity,
        Guid blueprintId,
        out BlackboardTier tier,
        out int slotIndex,
        out int payloadOffset)
    {
        tier = BlackboardTier.B1024;
        slotIndex = -1;
        payloadOffset = -1;
        return false;
    }
}
