namespace Fdp.Toolkit.Blueprints;

/// <summary>
/// Runtime definition produced from a compiled BlueprintAsset.
/// Full implementation in Phase 2 (TASK-RT-002).
/// </summary>
public sealed class BlueprintDefinition
{
    public Guid AssetId { get; init; }
    public string Name { get; init; } = string.Empty;
    public int StateSize { get; init; }

    /// <summary>Named state fields used by BlueprintStateView.GetField.</summary>
    public IReadOnlyDictionary<string, int> StateFields { get; init; }
        = new Dictionary<string, int>();

    /// <summary>Initializes the entity's blackboard slot to its default state.</summary>
    public unsafe void InitDefault(byte* slotPtr, int slotSize) { }
}
