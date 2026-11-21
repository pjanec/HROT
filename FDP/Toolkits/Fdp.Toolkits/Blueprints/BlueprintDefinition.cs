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

    // ⛔ CE-448 — the `Resolvers` index (reusable blueprint-authored DTO→DTO resolvers) is RETIRED. A resolver
    //   is the ONE optional stage a behaviour names (R-155), emitted on its resolver asset and called by the
    //   behaviour's registrar — never looked up here.

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
    /// ⭐⭐ <b><c>CE-388</c> / <c>Q74 D-A2</c> — the actuator channel components this blueprint's
    /// graph commands, DERIVED by the compiler.</b> Empty for the overwhelming majority.
    ///
    /// <para>🔒 Nothing is authored: every channel-command node already names its channel type
    /// (<c>BuiltInChannelCommandCatalog</c>), so the compiler holds the fact and the author does
    /// not repeat it.</para>
    ///
    /// <para>⚠ <b>Informational — the exit CLEANUP does not go through this list.</b> The binding
    /// is an action id (the generated <c>HsmExitCleanup</c> thunk, keyed by the shared
    /// <c>HsmActionKey</c>), deliberately, so the HSM side never has to know a blueprint's channel
    /// set. ⛔ Do not reconstruct the cleanup from this property — that would be the second
    /// producer <c>R-132</c> forbids.</para>
    ///
    /// <para>⛔⛔ <b>EMPTY IS NOT "UNKNOWN".</b> The compiler emits this only when its derivation is
    /// COMPLETE; when a graph calls hardcoded C# or another asset it raises a diagnostic instead.
    /// So an empty list here means "commands no channel", never "we could not tell".</para>
    /// </summary>
    public IReadOnlyList<Type> WritesChannels { get; init; } = Array.Empty<Type>();

    /// <summary>
    /// ⭐ The SAME <see cref="Fdp.Toolkit.Behavior.ParseParamsDelegate"/> a behaviour uses -- only the
    /// destination pointer differs (a behaviour passes <c>&amp;bb.BehaviorParameters[0]</c>, an Instance
    /// passes <c>slotPayload + ParamsOffset</c>). Bakes the declared defaults, then overlays the
    /// incoming JSON. <c>null</c> when the blueprint declares no parameters.
    /// </summary>
    public Fdp.Toolkit.Behavior.ParseParamsDelegate? ParseParams { get; init; }

    /// <summary>
    /// ⭐ <c>CE-3044</c> (R-191) — the inverse of <see cref="ParseParams"/>: the live params region as JSON by name,
    /// non-default fields only. What a scenario save stores. <c>null</c> when the blueprint declares no parameters.
    /// </summary>
    public Fdp.Toolkit.Behavior.FormatParamsDelegate? FormatParams { get; init; }

    /// <summary>The declared parameter names — what a saved params object is checked against on load (a key not in
    /// here was renamed or removed since the save; it is warned and dropped, R-191).</summary>
    public IReadOnlyList<string> ParamNames { get; init; } = Array.Empty<string>();

    // For inspector / debugger
    public Type? StateClrType { get; init; }

    /// <summary>⭐ <c>CE-2086</c> — the generated <c>Params</c> struct (the params region's type, what the editor's one params
    /// form edits): nested in the generated class beside <see cref="StateClrType"/> (<c>InstanceEmitter</c>). DERIVED, so no
    /// registrar emits it; <c>null</c> when the blueprint declares no parameters. 📄 <c>DESIGN_Sensors_And_Doctrine.md</c> §7.6a A2.</summary>
    public Type? ParamsClrType => ParamsSize > 0 ? StateClrType?.DeclaringType?.GetNestedType("Params") : null;
    public IReadOnlyDictionary<string, BlueprintFieldDescriptor> StateFields { get; init; }
        = new Dictionary<string, BlueprintFieldDescriptor>(StringComparer.Ordinal);

    // Backward-compatibility: asset GUID carried through for fixture/editor use.
    public Guid AssetId { get; init; }
}
