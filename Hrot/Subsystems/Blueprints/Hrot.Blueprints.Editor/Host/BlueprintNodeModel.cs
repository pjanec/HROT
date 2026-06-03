using System.Numerics;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// <see cref="INodeModel"/> adapter projecting a <see cref="Hrot.Blueprints.Core.Assets.Node"/>
/// onto the NodeEdit canvas contract.
/// <para>
/// <see cref="Position"/> reads live from <see cref="Hrot.Blueprints.Core.Assets.NodeMetadata"/>
/// so it is always in sync with the asset after any mutation (no stale snapshot).
/// </para>
/// </summary>
internal sealed class BlueprintNodeModel : INodeModel
{
    private readonly List<IPinModel> _pins;
    // Keep the asset node reference so Position reads live from EditorMetadata.
    private readonly Hrot.Blueprints.Core.Assets.Node _node;

    public NodeId      Id               { get; }
    public NodeKindKey Kind             { get; }
    public string      Title            { get; }
    public string?     Subtitle         => null;
    public NodeCategory Category        { get; }
    /// <summary>
    /// Reads live from the asset's <see cref="Hrot.Blueprints.Core.Assets.NodeMetadata"/>
    /// so it cannot go stale after a move or a <c>ChangeParentMultiple</c> command.
    /// </summary>
    public Vector2     Position         => new(_node.EditorMetadata.X, _node.EditorMetadata.Y);
    public Vector2?    SizeOverride     => null;
    public NodeState   State            { get; } = NodeState.Normal;
    public string?     StatusTooltip    => null;
    public bool        IsCollapsed      => false;
    public bool        ShowAdvancedPins => false;
    public NodeId?     ParentContainerId => null;
    public IReadOnlyList<IPinModel> Pins => _pins;

    /// <summary>
    /// Constructs a node model from a raw asset node, using the pre-resolved
    /// <paramref name="resolvedPins"/> list built by the two-pass GUID-binding algorithm.
    /// </summary>
    /// <param name="node">The asset node to project.</param>
    /// <param name="resolvedPins">The GUID-bound pin list built by the graph model.</param>
    /// <param name="asset">
    /// The owning asset, threaded so Get/Set variable titles can resolve the variable's
    /// declared NAME (instead of showing the raw <c>var:&lt;guid&gt;</c> id). May be null
    /// in unit tests that don't exercise variable nodes.
    /// </param>
    public BlueprintNodeModel(
        Hrot.Blueprints.Core.Assets.Node node,
        IReadOnlyList<IPinModel> resolvedPins,
        Hrot.Blueprints.Core.Assets.BlueprintAsset? asset = null)
    {
        _node     = node;
        Id        = new NodeId(node.Id);
        Kind      = new NodeKindKey(node.GetType().Name);
        Title     = BuildTitle(node, asset);
        Category  = BuildCategory(node);
        _pins     = new List<IPinModel>(resolvedPins);
    }

    /// <summary>
    /// Updates the asset's <see cref="Hrot.Blueprints.Core.Assets.NodeMetadata"/> position in place.
    /// Because <see cref="Position"/> reads live from the asset, the canvas sees the new value
    /// immediately without a full model rebuild.
    /// </summary>
    internal void SetPosition(Vector2 pos)
    {
        _node.EditorMetadata.X = pos.X;
        _node.EditorMetadata.Y = pos.Y;
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static string BuildTitle(
        Hrot.Blueprints.Core.Assets.Node node,
        Hrot.Blueprints.Core.Assets.BlueprintAsset? asset) => node switch
    {
        Hrot.Blueprints.Core.Assets.FunctionCallNode fc   => string.IsNullOrEmpty(fc.MethodName) ? "Function Call" : fc.MethodName,
        Hrot.Blueprints.Core.Assets.GetVariableNode gv    => $"Get {ResolveVariableName(gv.VariableId, asset)}",
        Hrot.Blueprints.Core.Assets.SetVariableNode sv    => $"Set {ResolveVariableName(sv.VariableId, asset)}",
        Hrot.Blueprints.Core.Assets.LiteralNode lt        => $"Literal ({lt.TypeId})",
        Hrot.Blueprints.Core.Assets.EventEntryNode ee     => $"Event: {ee.EventTypeId}",
        Hrot.Blueprints.Core.Assets.CallPeerBlueprintNode cp => $"Call Peer: {cp.FunctionRef}",
        Hrot.Blueprints.Core.Assets.CallCustomEventNode ce   => $"Call {ce.EventId}",
        Hrot.Blueprints.Core.Assets.ChannelCommandNode cc    => $"Command: {cc.ActionId}",
        Hrot.Blueprints.Core.Assets.WhenNode                 => "When",
        Hrot.Blueprints.Core.Assets.ReturnNode               => "Return",
        Hrot.Blueprints.Core.Assets.BranchNode               => "Branch",
        Hrot.Blueprints.Core.Assets.SequenceNode             => "Sequence",
        Hrot.Blueprints.Core.Assets.CastNode ca              => $"Cast to {ca.TargetTypeId}",
        Hrot.Blueprints.Core.Assets.LatentDelayNode          => "Delay",
        Hrot.Blueprints.Core.Assets.ArrayMakeNode            => "Make Array",
        Hrot.Blueprints.Core.Assets.ArrayGetNode             => "Get Array",
        Hrot.Blueprints.Core.Assets.WaitForChannelNode wfc   => $"Wait: {wfc.ChannelType}",
        Hrot.Blueprints.Core.Assets.WaitForEventNode wfe     => $"Wait Event: {wfe.EventTypeId}",
        Hrot.Blueprints.Core.Assets.ReadEqsResultNode        => "Read EQS Result",
        Hrot.Blueprints.Core.Assets.SpawnEqsSensorNode       => "Spawn EQS Sensor",
        _ => node.GetType().Name,
    };

    private static NodeCategory BuildCategory(Hrot.Blueprints.Core.Assets.Node node) => node switch
    {
        Hrot.Blueprints.Core.Assets.EventEntryNode           => NodeCategory.Event,
        Hrot.Blueprints.Core.Assets.GetVariableNode          => NodeCategory.VariableGet,
        Hrot.Blueprints.Core.Assets.SetVariableNode          => NodeCategory.VariableSet,
        Hrot.Blueprints.Core.Assets.LiteralNode              => NodeCategory.Pure,
        Hrot.Blueprints.Core.Assets.FunctionCallNode fc when fc.IsPure => NodeCategory.Pure,
        Hrot.Blueprints.Core.Assets.BranchNode               => NodeCategory.FlowControl,
        Hrot.Blueprints.Core.Assets.SequenceNode             => NodeCategory.FlowControl,
        _                                                    => NodeCategory.Function,
    };

    /// <summary>
    /// Resolves the display NAME of a variable from the owning asset's variable list.
    /// Accepts both the raw GUID string and the My-Blueprint <c>var:&lt;guid&gt;</c> item-id
    /// form. Falls back to the original id string when the asset is null or the variable
    /// is not found.
    /// </summary>
    private static string ResolveVariableName(
        string variableId,
        Hrot.Blueprints.Core.Assets.BlueprintAsset? asset)
    {
        if (asset == null || string.IsNullOrEmpty(variableId))
            return variableId;

        var idStr = variableId.StartsWith("var:", StringComparison.OrdinalIgnoreCase)
            ? variableId[4..]
            : variableId;

        if (Guid.TryParse(idStr, out var guid))
        {
            var decl = asset.Variables.FirstOrDefault(v => v.Id == guid);
            if (decl != null && !string.IsNullOrEmpty(decl.Name))
                return decl.Name;
        }

        return variableId;
    }
}
