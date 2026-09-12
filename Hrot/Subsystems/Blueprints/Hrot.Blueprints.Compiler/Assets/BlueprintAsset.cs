namespace Hrot.Blueprints.Core.Assets;

public sealed class BlueprintAsset
{
    public Header Header { get; set; } = new();
    public Guid AssetId { get; set; }
    public string Name { get; set; } = "";
    public BlueprintDispatchKind Dispatch { get; set; }
    public BlackboardTierHint TierHint { get; set; } = BlackboardTierHint.Auto;
    public bool IsWorldSingleton { get; set; }

    // For AiPrimitive only:
    public AiPrimitiveDecl? Primitive { get; set; }

    /// <summary>
    /// ⭐⭐ <b>U-12 / D4 — THE STORE.</b> One list of tagged declarations. <c>Parameters</c>,
    /// <c>WorkingState</c> and <c>Variables</c> below are <b>windows onto it</b>, not storage.
    ///
    /// <para>
    /// ⚠ <b>Kept grouped in <see cref="DeclarationList.KindOrder"/></b> — Parameter, then WorkingState,
    /// then Variable — so each kind is ONE contiguous run. ⛔ That invariant is what lets the three
    /// properties be windows at all, and it is what keeps field order inside a kind independent of the
    /// order the JSON properties happen to arrive in.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>Internal, so the serializer cannot see it</b> — System.Text.Json does not bind non-public
    /// members. The v1 on-disk shape is written by the three windows below and by nothing else, which
    /// is the whole constraint of this task: ⭐ <b>the store moved, the bytes did not.</b> Writing v2
    /// is <c>U-10</c>'s wiring, not this.
    /// </para>
    /// </summary>
    internal List<BlueprintDeclaration> DeclarationStore { get; } = new();

    private readonly DeclarationView<ParameterDecl> _parameters;
    private readonly DeclarationView<VariableDecl>  _variables;

    public BlueprintAsset()
    {
        _parameters = new DeclarationView<ParameterDecl>(this, DeclarationKind.Parameter);
        _variables  = new DeclarationView<VariableDecl>(this, DeclarationKind.Variable);
    }

    /// <summary>
    /// ⭐ <b>A live window onto the store's <c>Parameter</c> run — the <c>Params</c> struct, offset 0.</b>
    /// ⛔ Not a snapshot: <c>asset.Parameters.Add(p)</c> writes to the store, because a projection that
    /// accepts a mutation and drops it is trap #5 wearing the model's name.
    /// ⚠ The setter <b>absorbs</b> rather than rebinds, so the same window object is returned for the
    /// life of the asset and reference identity across two reads is stable.
    /// </summary>
    public DeclarationView<ParameterDecl> Parameters
    {
        get => _parameters;
        set => _parameters.ReplaceWith(value);
    }

    public List<Guid>? ParameterOrder { get; set; }

    /// <summary>
    /// ⚠⚠ <b>Batch 86 — an ALIAS for <see cref="Variables"/>, and the SAME object.</b>
    ///
    /// <para>📌 <c>R-01</c>: one state run, two names for it. ⭐ <b>Kept, not deleted</b> — deleting the
    /// property is stage <c>D4</c>'s, and the <i>"no rush removals"</i> ruling applies.</para>
    ///
    /// <para>⛔⛔ <b>NEVER write <c>WorkingState.Concat(Variables)</c> — it yields every declaration
    /// TWICE.</b> ⭐ Batch 86 swept every such site to a single <c>Declarations.Of(Variable)</c>.</para>
    ///
    /// <para>⭐⭐ <b>The SETTER owns the run's LEADING segment</b>, because the deserializer drives both
    /// property setters *(v2 migrates DOWN to the three-list shape)* and a plain replace would let the
    /// second wipe the first — see <see cref="DeclarationView{T}.ReplaceSegment"/>.</para>
    /// </summary>
    public DeclarationView<VariableDecl> WorkingState
    {
        get => _variables;
        set => _leadingStateCount = _variables.ReplaceSegment(0, _leadingStateCount, value);
    }

    /// <summary>
    /// ⭐ How many of the state run's entries arrived under the <c>WorkingState</c> name. ⚠ Bookkeeping
    /// for the two setters ONLY — ⛔ it is not a kind, and nothing downstream may branch on it.
    /// </summary>
    private int _leadingStateCount;

    public List<Guid>? WorkingStateOrder { get; set; }

    // For Instance only:

    /// <summary>A live window onto the store's <c>Variable</c> run — the <c>State</c> struct, offset 16.</summary>
    public DeclarationView<VariableDecl> Variables
    {
        get => _variables;
        // ⭐ The TRAILING segment — everything after what came in under the WorkingState name.
        set => _variables.ReplaceSegment(
                   _leadingStateCount, Math.Max(0, _variables.Count - _leadingStateCount), value);
    }

    public List<Guid>? VariableOrder { get; set; }
    public List<EventDispatcherDecl> EventDispatchers { get; set; } = new();
    public List<CustomEventDecl> CustomEvents { get; set; } = new();
    public List<Guid> CallablePeers { get; set; } = new();

    // Common:
    public List<Graph> Graphs { get; set; } = new();
    public AssetMetadata EditorMetadata { get; set; } = new();

    /// <summary>
    /// <b>U-9 / D1 — the declarations as one tagged sequence. ⭐ U-12: now the STORE, not a projection.</b>
    ///
    /// <para>
    /// ⭐⭐ <b>The direction reversed at <c>U-12</c>, and the doc says so because the reversal is the
    /// point.</b> Under <c>U-9</c> the three lists were storage and this was a view over them; since
    /// the store flip <see cref="DeclarationStore"/> is the storage and the three are windows over it.
    /// ⚠ <b>Every caller sees the same behaviour either way</b> — which is precisely why <c>U-9</c>
    /// could be built inverse and paid for later.
    /// </para>
    ///
    /// <para>
    /// ⛔ <b>The tag must not reach JSON</b> — if it did, <c>U-10</c> would lose its own revert — so the
    /// property is <c>[JsonIgnore]</c>d and <c>PersistenceShapeTests.TheTagIsNotSerializable</c> asserts
    /// the attribute is still there. ⭐ Since the flip the store is <c>internal</c> as well, so the tag
    /// is now invisible to the serializer <b>twice over</b>: by attribute and by accessibility.
    /// </para>
    ///
    /// <para>
    /// ⚠ Allocates a thin view per access; it holds no state, so equality and identity live on the
    /// backing declarations rather than on the view.
    /// </para>
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public DeclarationList Declarations => new(this);
}

/// <summary>
/// Mirror of <c>Fdp.Toolkit.Blueprints.BlueprintDispatchKind</c>.
/// </summary>
public enum BlueprintDispatchKind { Library, AiPrimitive, Instance }

public enum BlackboardTierHint { Auto, Force1024, Force4096, Force16384 }

public sealed class AiPrimitiveDecl
{
    public AiPrimitiveIntent Intent { get; set; }
    public List<AiPrimitiveHosting> Hostings { get; set; } = new();
}

public enum AiPrimitiveIntent { Action, Condition }

public enum AiPrimitiveHosting
{
    BTreeAction,
    BTreeCondition,
    HsmAction,
    HsmGuard,
    BlueprintCall,
}
