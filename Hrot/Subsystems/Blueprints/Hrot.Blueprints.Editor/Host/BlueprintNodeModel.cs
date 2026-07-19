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
    /// <summary>
    /// Punch-list #4: for a <see cref="Hrot.Blueprints.Core.Assets.FunctionCallNode"/> this carries the
    /// resolved signature + XML-doc summary shown on node hover (see <see cref="FunctionCallTooltip"/>);
    /// null for every other node kind.
    /// </summary>
    public string?     StatusTooltip    { get; }
    public bool        IsCollapsed      => false;
    public bool        ShowAdvancedPins => false;
    public NodeId?     ParentContainerId => null;
    /// <summary>
    /// UE-style: function-call nodes are marked with an italic <c>ƒ</c> in the header corner
    /// (native and Blueprint calls alike — they are told apart by workflow, not paint). Null for
    /// every other node kind.
    /// </summary>
    public string?     HeaderGlyph      { get; }
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
        StatusTooltip = node is Hrot.Blueprints.Core.Assets.FunctionCallNode fc
            ? FunctionCallTooltip.Build(fc, _pins)
            : null;
        // "ƒ" (U+0192) reads as UE's italic function mark and is in the canvas font range.
        HeaderGlyph = node is Hrot.Blueprints.Core.Assets.FunctionCallNode ? "ƒ" : null;
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
        // Slice 2a-3: GetShared/SetShared — VariableId is a raw manifest-provisioned slot name
        // (not a blueprint VariableDecl GUID), so no ResolveVariableName lookup is needed.
        Hrot.Blueprints.Core.Assets.GetSharedNode gsn      => $"Get Shared: {(string.IsNullOrEmpty(gsn.VariableId) ? "(unset)" : gsn.VariableId)}",
        Hrot.Blueprints.Core.Assets.SetSharedNode ssn      => $"Set Shared: {(string.IsNullOrEmpty(ssn.VariableId) ? "(unset)" : ssn.VariableId)}",
        // Punch-list #1/#5/#8: show the node's own DATA in the body instead of the generic "Value"
        // pin label — the literal's value, the parameter's name, the compare/arith/bool operator.
        Hrot.Blueprints.Core.Assets.GetParameterNode gp   => $"Get Param: {ResolveParameterName(gp.ParameterId, asset)}",
        Hrot.Blueprints.Core.Assets.LiteralNode lt        => FormatLiteral(lt),
        Hrot.Blueprints.Core.Assets.CompareNode cmp       => $"Compare {OperatorSymbol(cmp.Operator)}",
        Hrot.Blueprints.Core.Assets.BinaryOpNode bin      => $"Math {OperatorSymbol(bin.Operator)}",
        Hrot.Blueprints.Core.Assets.BooleanOpNode boo     => $"Logic {OperatorSymbol(boo.Operator)}",
        Hrot.Blueprints.Core.Assets.NotNode               => "Not (!)",
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
        Hrot.Blueprints.Core.Assets.GetSharedNode            => NodeCategory.VariableGet,
        Hrot.Blueprints.Core.Assets.SetSharedNode            => NodeCategory.VariableSet,
        Hrot.Blueprints.Core.Assets.LiteralNode              => NodeCategory.Pure,
        Hrot.Blueprints.Core.Assets.GetParameterNode         => NodeCategory.Pure,
        Hrot.Blueprints.Core.Assets.CompareNode              => NodeCategory.Pure,
        Hrot.Blueprints.Core.Assets.BinaryOpNode             => NodeCategory.Pure,
        Hrot.Blueprints.Core.Assets.BooleanOpNode            => NodeCategory.Pure,
        Hrot.Blueprints.Core.Assets.NotNode                  => NodeCategory.Pure,
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

    /// <summary>
    /// Resolves the display NAME of a blueprint parameter from the owning asset's parameter list.
    /// Mirrors <see cref="ResolveVariableName"/> but over <c>asset.Parameters</c> and also accepts the
    /// <c>param:&lt;guid&gt;</c> item-id form (Stage5 strips both <c>var:</c>/<c>param:</c> prefixes).
    /// Falls back to the raw id when the asset is null or the parameter is not found.
    /// </summary>
    private static string ResolveParameterName(
        string parameterId,
        Hrot.Blueprints.Core.Assets.BlueprintAsset? asset)
    {
        if (asset == null || string.IsNullOrEmpty(parameterId))
            return string.IsNullOrEmpty(parameterId) ? "(unset)" : parameterId;

        var idStr = parameterId;
        if (idStr.StartsWith("param:", StringComparison.OrdinalIgnoreCase)) idStr = idStr[6..];
        else if (idStr.StartsWith("var:", StringComparison.OrdinalIgnoreCase)) idStr = idStr[4..];

        if (Guid.TryParse(idStr, out var guid))
        {
            var decl = asset.Parameters.FirstOrDefault(p => p.Id == guid);
            if (decl != null && !string.IsNullOrEmpty(decl.Name))
                return decl.Name;
        }

        return parameterId;
    }

    /// <summary>
    /// Renders a <see cref="Hrot.Blueprints.Core.Assets.LiteralNode"/>'s title as its actual value
    /// (punch-list #5) — strings shown unquoted, floats without the <c>f</c> suffix. Falls back to
    /// <c>Literal (Type)</c> when no value has been entered yet.
    /// </summary>
    private static string FormatLiteral(Hrot.Blueprints.Core.Assets.LiteralNode lt)
    {
        var raw = lt.ValueJson ?? string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return $"Literal ({ShortTypeName(lt.TypeId)})";

        // Strings persist as C# literals with surrounding quotes; floats carry an 'f' suffix.
        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
            return raw[1..^1];
        if ((raw[^1] == 'f' || raw[^1] == 'F') && raw.Length > 1 && (char.IsDigit(raw[0]) || raw[0] == '-' || raw[0] == '.'))
            return raw[..^1];
        return raw;
    }

    private static string ShortTypeName(string typeId)
    {
        if (string.IsNullOrEmpty(typeId)) return "?";
        var dot = typeId.LastIndexOf('.');
        return dot >= 0 ? typeId[(dot + 1)..] : typeId;
    }

    private static string OperatorSymbol(Hrot.Blueprints.Core.Assets.ComparisonOperator op) => op switch
    {
        Hrot.Blueprints.Core.Assets.ComparisonOperator.Equal              => "==",
        Hrot.Blueprints.Core.Assets.ComparisonOperator.NotEqual           => "!=",
        Hrot.Blueprints.Core.Assets.ComparisonOperator.LessThan           => "<",
        Hrot.Blueprints.Core.Assets.ComparisonOperator.LessThanOrEqual    => "<=",
        Hrot.Blueprints.Core.Assets.ComparisonOperator.GreaterThan        => ">",
        Hrot.Blueprints.Core.Assets.ComparisonOperator.GreaterThanOrEqual => ">=",
        _                                                                 => op.ToString(),
    };

    private static string OperatorSymbol(Hrot.Blueprints.Core.Assets.ArithmeticOperator op) => op switch
    {
        Hrot.Blueprints.Core.Assets.ArithmeticOperator.Add      => "+",
        Hrot.Blueprints.Core.Assets.ArithmeticOperator.Subtract => "-",
        Hrot.Blueprints.Core.Assets.ArithmeticOperator.Multiply => "*",
        Hrot.Blueprints.Core.Assets.ArithmeticOperator.Divide   => "/",
        Hrot.Blueprints.Core.Assets.ArithmeticOperator.Modulo   => "%",
        _                                                       => op.ToString(),
    };

    private static string OperatorSymbol(Hrot.Blueprints.Core.Assets.BooleanOperator op) => op switch
    {
        Hrot.Blueprints.Core.Assets.BooleanOperator.And => "&&",
        Hrot.Blueprints.Core.Assets.BooleanOperator.Or  => "||",
        _                                               => op.ToString(),
    };
}
