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

    // ── Parameters (DESIGN_Parameter_Model.md §3.3) ──────────────────────────
    //
    // ⭐⭐ An Instance payload is ONE struct: [BlueprintLatentCursor 16][Params N][State M].
    //    StateSize stays "the whole payload", so ChooseTier/TryAttach are unchanged; these two say
    //    WHERE inside it the params live. ⛔ Emitted by the compiler so that no runtime call site
    //    re-derives 16 -- that constant has exactly one home.
    //
    // ⚠ Defaults 0/0 are the truthful answer for a blueprint with no parameters, and for the
    //   Library/AiPrimitive kinds that do not attach through BlueprintInstanceService at all.

    /// <summary>Byte offset of the params region inside the payload (16 for an Instance).</summary>
    public int ParamsOffset { get; init; }

    /// <summary>Bytes the params region occupies; 0 when the blueprint declares no parameters.</summary>
    public int ParamsSize { get; init; }

    /// <summary>
    /// ⭐ The SAME <see cref="Fdp.Toolkit.Behavior.ParseParamsDelegate"/> a behaviour uses -- only the
    /// destination pointer differs (a behaviour passes <c>&amp;bb.BehaviorParameters[0]</c>, an Instance
    /// passes <c>slotPayload + ParamsOffset</c>). Bakes the declared defaults, then overlays the
    /// incoming JSON. <c>null</c> when the blueprint declares no parameters.
    /// </summary>
    public Fdp.Toolkit.Behavior.ParseParamsDelegate? ParseParams { get; init; }

    // For inspector / debugger
    public Type? StateClrType { get; init; }
    public IReadOnlyDictionary<string, BlueprintFieldDescriptor> StateFields { get; init; }
        = new Dictionary<string, BlueprintFieldDescriptor>(StringComparer.Ordinal);

    // Backward-compatibility: asset GUID carried through for fixture/editor use.
    public Guid AssetId { get; init; }
}
