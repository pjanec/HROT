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

    // For Library dispatch (G2) -- callable functions keyed by graph name. Empty for other kinds.
    // Populated by the generated [BlueprintRegistrar]; the runtime resolver seam invokes these.
    public IReadOnlyDictionary<string, LibraryFunctionDelegate> Functions { get; init; }
        = new Dictionary<string, LibraryFunctionDelegate>(StringComparer.Ordinal);

    // For inspector / debugger
    public Type? StateClrType { get; init; }
    public IReadOnlyDictionary<string, BlueprintFieldDescriptor> StateFields { get; init; }
        = new Dictionary<string, BlueprintFieldDescriptor>(StringComparer.Ordinal);

    // Backward-compatibility: asset GUID carried through for fixture/editor use.
    public Guid AssetId { get; init; }
}
