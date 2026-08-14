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
    public List<ParameterDecl> Parameters { get; set; } = new();
    public List<Guid>? ParameterOrder { get; set; }
    public List<VariableDecl> WorkingState { get; set; } = new();
    public List<Guid>? WorkingStateOrder { get; set; }

    // For Instance only:
    public List<VariableDecl> Variables { get; set; } = new();
    public List<Guid>? VariableOrder { get; set; }
    public List<EventDispatcherDecl> EventDispatchers { get; set; } = new();
    public List<CustomEventDecl> CustomEvents { get; set; } = new();
    public List<Guid> CallablePeers { get; set; } = new();

    // Common:
    public List<Graph> Graphs { get; set; } = new();
    public AssetMetadata EditorMetadata { get; set; } = new();

    /// <summary>
    /// <b>U-9 / D1 — the three declaration lists above, as one tagged sequence.</b>
    ///
    /// <para>
    /// ⭐⭐ <b>A live write-through view, and NOT a fourth place to store anything.</b> The lists
    /// remain the storage; this projects them. ⛔ <b>The tag must not reach JSON</b> — if it did,
    /// <c>U-9</c> and <c>U-10</c> would collapse into one task and the migrator would lose its own
    /// revert — so the property is <c>[JsonIgnore]</c>d and
    /// <c>PersistenceShapeTests.TheTagIsNotSerializable</c> asserts the attribute is still there.
    /// </para>
    ///
    /// <para>
    /// ⚠ Allocates a thin view per access; it holds no state, so equality and identity live on the
    /// backing declarations rather than on the view or its facades.
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
