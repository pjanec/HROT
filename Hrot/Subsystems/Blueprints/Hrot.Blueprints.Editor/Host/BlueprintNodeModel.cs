using System.Numerics;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// <see cref="INodeModel"/> adapter projecting a <see cref="Hrot.Blueprints.Core.Assets.Node"/>
/// onto the NodeEdit canvas contract.
/// <para>
/// <see cref="Position"/> is mutable via <see cref="SetPosition"/> so that
/// <see cref="BlueprintCommandSink.ApplyMoveNodes"/> can update the existing instance
/// in place without a full model rebuild on every drag frame.
/// </para>
/// </summary>
internal sealed class BlueprintNodeModel : INodeModel
{
    private readonly List<IPinModel> _pins;
    private Vector2 _position;

    public NodeId      Id               { get; }
    public NodeKindKey Kind             { get; }
    public string      Title            { get; }
    public string?     Subtitle         => null;
    public NodeCategory Category        { get; }
    public Vector2     Position         => _position;
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
    public BlueprintNodeModel(Hrot.Blueprints.Core.Assets.Node node, IReadOnlyList<IPinModel> resolvedPins)
    {
        Id        = new NodeId(node.Id);
        Kind      = new NodeKindKey(node.GetType().Name);
        Title     = BuildTitle(node);
        Category  = BuildCategory(node);
        _position = new Vector2(node.EditorMetadata.X, node.EditorMetadata.Y);
        _pins     = new List<IPinModel>(resolvedPins);
    }

    /// <summary>Updates <see cref="Position"/> in place without rebuilding the model graph.</summary>
    internal void SetPosition(Vector2 pos) => _position = pos;

    // ── helpers ────────────────────────────────────────────────────────────

    private static string BuildTitle(Hrot.Blueprints.Core.Assets.Node node) => node switch
    {
        Hrot.Blueprints.Core.Assets.FunctionCallNode fc   => string.IsNullOrEmpty(fc.MethodName) ? "Function Call" : fc.MethodName,
        Hrot.Blueprints.Core.Assets.GetVariableNode gv    => $"Get {gv.VariableId}",
        Hrot.Blueprints.Core.Assets.SetVariableNode sv    => $"Set {sv.VariableId}",
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
}
