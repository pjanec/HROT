namespace Fdp.Toolkit.Blueprints.Partitioning;

/// <summary>Identifies one Blueprint slot within an entity's blackboard.</summary>
public readonly struct BlueprintSlotEntry
{
    public Guid BlueprintId { get; init; }
    public int SlotIndex { get; init; }
    public int PayloadOffset { get; init; }
    public BlackboardTier Tier { get; init; }
}
