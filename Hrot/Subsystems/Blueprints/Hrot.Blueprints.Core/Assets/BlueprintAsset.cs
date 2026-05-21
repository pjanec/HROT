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
    public List<VariableDecl> WorkingState { get; set; } = new();

    // For Instance only:
    public List<VariableDecl> Variables { get; set; } = new();
    public List<EventDispatcherDecl> EventDispatchers { get; set; } = new();
    public List<CustomEventDecl> CustomEvents { get; set; } = new();
    public List<Guid> CallablePeers { get; set; } = new();

    // Common:
    public List<Graph> Graphs { get; set; } = new();
    public AssetMetadata EditorMetadata { get; set; } = new();
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
