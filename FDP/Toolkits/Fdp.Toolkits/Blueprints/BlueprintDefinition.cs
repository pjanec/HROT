namespace Fdp.Toolkit.Blueprints;

/// <summary>
/// Immutable runtime definition for a compiled Blueprint.
/// Produced by [BlueprintRegistrar].Register and stored in BlueprintRegistry.
/// Per Runtime DD §3.2.
/// </summary>
public sealed record BlueprintDefinition
{
    // Identity and validation -- required
    public required string               Name          { get; init; }
    public required BlueprintDispatchKind Kind          { get; init; }
    public required ulong                StructureHash { get; init; }
    public required int                  StateSize     { get; init; }

    // For Instance dispatch -- null for Library/AiPrimitive
    public InitDefaultDelegate?  InitDefault   { get; init; }
    public TickDelegate?         Tick          { get; init; }
    public IReadOnlyDictionary<string, EventHandlerDelegate> EventHandlers { get; init; }
        = new Dictionary<string, EventHandlerDelegate>(StringComparer.Ordinal);

    // For inspector / debugger
    public Type? StateClrType { get; init; }
    public IReadOnlyList<BlueprintFieldDescriptor> StateFields { get; init; }
        = Array.Empty<BlueprintFieldDescriptor>();

    // Backward-compatibility: asset GUID carried through for fixture/editor use.
    public Guid AssetId { get; init; }
}
