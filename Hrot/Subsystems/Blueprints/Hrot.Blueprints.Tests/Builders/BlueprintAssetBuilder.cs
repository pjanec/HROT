using System.Security.Cryptography;
using System.Text;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Tests.Builders;

/// <summary>
/// Helper that computes deterministic Guids from fixed inputs using SHA256.
/// </summary>
public static class SyntheticGuidHelper
{
    /// <summary>
    /// Computes a deterministic Guid from the given inputs.
    /// Uses SHA256 over the UTF-8 encodings of all parts; takes first 16 bytes.
    /// </summary>
    public static Guid Compute(Guid assetId, Guid graphId, params object[] parts)
    {
        using var sha = SHA256.Create();
        var buf = new List<byte>(128);

        buf.AddRange(assetId.ToByteArray());
        buf.AddRange(graphId.ToByteArray());

        foreach (var part in parts)
        {
            var s = part?.ToString() ?? "";
            buf.AddRange(Encoding.UTF8.GetBytes(s));
            buf.Add(0x1F); // field separator
        }

        var hash = sha.ComputeHash(buf.ToArray());
        byte[] guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, 16);
        return new Guid(guidBytes);
    }
}

// ============================================================
// NodeBuilder -- helper to attach data pins to a node
// ============================================================

/// <summary>
/// Fluent helper for attaching data pins to a specific node inside GraphBuilder callbacks.
/// </summary>
public sealed class NodeBuilder
{
    private readonly Node _node;
    private readonly Guid _assetId;
    private readonly Guid _graphId;

    internal NodeBuilder(Node node, Guid assetId, Guid graphId)
    {
        _node = node;
        _assetId = assetId;
        _graphId = graphId;
    }

    /// <summary>Adds a data input pin with the given name and type.</summary>
    public NodeBuilder WithInputPin(string name, string typeId)
    {
        _node.Pins.Add(new Pin
        {
            Id = SyntheticGuidHelper.Compute(_assetId, _graphId, _node.Id, "In", name),
            Name = name,
            Direction = "In",
            TypeRef = new BlueprintTypeRef { TypeId = typeId },
            IsExec = false,
        });
        return this;
    }

    /// <summary>Adds a data output pin with the given name and type.</summary>
    public NodeBuilder WithOutputPin(string name, string typeId)
    {
        _node.Pins.Add(new Pin
        {
            Id = SyntheticGuidHelper.Compute(_assetId, _graphId, _node.Id, "Out", name),
            Name = name,
            Direction = "Out",
            TypeRef = new BlueprintTypeRef { TypeId = typeId },
            IsExec = false,
        });
        return this;
    }
}

// ============================================================
// GraphBuilder
// ============================================================

/// <summary>
/// Fluent builder for constructing a single Blueprint Graph with auto exec-wire chaining.
/// Each node-producing method automatically wires the previous node's exec-out to the
/// new node's exec-in. Returns 'this' for method chaining.
/// </summary>
public sealed class GraphBuilder
{
    private readonly string _name;
    private readonly GraphKind _kind;
    private readonly Guid _assetId;
    private readonly Guid _graphId;
    private readonly List<Node> _nodes = new();
    private readonly List<Link> _links = new();
    private readonly List<ParameterDecl> _inputs = new();

    // Tracks the last added node for automatic exec-wire chaining.
    private Guid _lastNodeId = Guid.Empty;
    private Guid _lastExecOutPinId = Guid.Empty;

    internal GraphBuilder(string name, GraphKind kind, Guid assetId)
    {
        _name = name;
        _kind = kind;
        _assetId = assetId;
        _graphId = SyntheticGuidHelper.Compute(assetId, Guid.Empty, "Graph", name);
    }

    // ---- Private helpers ----

    private Guid MakeNodeId(string tag, int index)
        => SyntheticGuidHelper.Compute(_assetId, _graphId, tag, index.ToString());

    private Guid MakePinId(Guid nodeId, string pinRole)
        => SyntheticGuidHelper.Compute(_assetId, _graphId, nodeId, pinRole);

    /// <summary>
    /// Wires exec from the last node to the new node and updates tracking state.
    /// Silently does nothing when there is no predecessor (fromNode == Guid.Empty).
    /// </summary>
    private void LinkExec(Guid fromNodeId, Guid fromPinId, Guid toNodeId, Guid toPinId)
    {
        if (fromNodeId == Guid.Empty) return;
        _links.Add(new Link
        {
            FromNodeId = fromNodeId,
            FromPinId = fromPinId,
            ToNodeId = toNodeId,
            ToPinId = toPinId,
        });
    }

    /// <summary>
    /// Registers a node in the graph, auto-wires exec from the previous node,
    /// assigns exec pins, and updates chaining state.
    /// </summary>
    /// <param name="node">Node to add.</param>
    /// <param name="hasExecIn">Whether this node type accepts an exec-in pin.</param>
    /// <param name="hasExecOut">Whether this node type produces an exec-out pin.</param>
    private void RegisterNode(Node node, bool hasExecIn, bool hasExecOut)
    {
        Guid execInPinId = Guid.Empty;
        Guid execOutPinId = Guid.Empty;

        if (hasExecIn)
        {
            execInPinId = MakePinId(node.Id, "ExecIn");
            node.Pins.Add(new Pin { Id = execInPinId, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() });
        }
        if (hasExecOut)
        {
            execOutPinId = MakePinId(node.Id, "ExecOut");
            node.Pins.Add(new Pin { Id = execOutPinId, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() });
        }

        // Auto-wire from previous node's exec-out to this node's exec-in.
        if (hasExecIn)
            LinkExec(_lastNodeId, _lastExecOutPinId, node.Id, execInPinId);

        _nodes.Add(node);
        _lastNodeId = node.Id;
        _lastExecOutPinId = execOutPinId;
    }

    // ---- Node-producing methods ----

    /// <summary>Adds an EventEntryNode (graph entry point, exec-out only). Q#14: optional event identity
    /// (<paramref name="eventTypeId"/>) marks which event an Event graph subscribes to.</summary>
    public GraphBuilder Entry(string? eventTypeId = null)
    {
        var nodeId = MakeNodeId("EventEntry", _nodes.Count);
        var node = new EventEntryNode { Id = nodeId, EventTypeId = eventTypeId ?? "" };
        RegisterNode(node, hasExecIn: false, hasExecOut: true);
        return this;
    }

    /// <summary>Q#14: declares a graph input (Event-graph payload field). Feeds Graph.Inputs.</summary>
    public GraphBuilder WithInput(string name, string typeId)
    {
        _inputs.Add(new ParameterDecl { Name = name, Type = new BlueprintTypeRef { TypeId = typeId } });
        return this;
    }

    /// <summary>Adds a ReturnNode (terminal node, exec-in only).</summary>
    public GraphBuilder Return(NodeStatus status = NodeStatus.Success)
    {
        var nodeId = MakeNodeId("Return", _nodes.Count);
        var node = new ReturnNode { Id = nodeId, Status = status };
        RegisterNode(node, hasExecIn: true, hasExecOut: false);
        return this;
    }

    /// <summary>Adds a LatentDelayNode.</summary>
    public GraphBuilder Delay(float duration)
    {
        var nodeId = MakeNodeId("Delay", _nodes.Count);
        var node = new LatentDelayNode { Id = nodeId };
        RegisterNode(node, hasExecIn: true, hasExecOut: true);
        return this;
    }

    /// <summary>Adds a ChannelCommandNode and calls the configure callback for data pins.</summary>
    public GraphBuilder ChannelCommand(string channelType, string actionId, Action<NodeBuilder>? configure = null)
    {
        var nodeId = MakeNodeId("ChannelCommand", _nodes.Count);
        var node = new ChannelCommandNode { Id = nodeId, ChannelType = channelType, ActionId = actionId };
        RegisterNode(node, hasExecIn: true, hasExecOut: true);
        configure?.Invoke(new NodeBuilder(node, _assetId, _graphId));
        return this;
    }

    /// <summary>
    /// AN8 — Adds a ChannelCommandNode with ActionFqn set (non-channel behavior action, inline-latent).
    /// <paramref name="actionFqn"/> = <c>"{ClassFqn}.Call"</c>, e.g.
    /// <c>"Hrot.AI.Behaviors.Generated.MyAction_12345678_Bp.Call"</c>.
    /// <paramref name="paramsTypeFqn"/> = nested Params type ('+' or '.' separator accepted).
    /// </summary>
    public GraphBuilder ActionInvocation(
        string actionFqn,
        string? paramsTypeFqn = null,
        Action<NodeBuilder>? configure = null)
    {
        var nodeId = MakeNodeId("ActionInvocation", _nodes.Count);
        var node = new ChannelCommandNode
        {
            Id                  = nodeId,
            ActionFqn           = actionFqn,
            ActionParamsTypeFqn = paramsTypeFqn,
        };
        RegisterNode(node, hasExecIn: true, hasExecOut: true);
        configure?.Invoke(new NodeBuilder(node, _assetId, _graphId));
        return this;
    }

    /// <summary>Adds a CallCustomEventNode referencing the named custom event by name (name-based lookup).</summary>
    public GraphBuilder CallCustomEvent(string eventName)
    {
        var nodeId = MakeNodeId("CallCustomEvent", _nodes.Count);
        var node = new CallCustomEventNode { Id = nodeId, EventId = eventName };
        RegisterNode(node, hasExecIn: true, hasExecOut: true);
        return this;
    }

    /// <summary>
    /// Q#14 (2a): adds a PublishEvent node baked with a custom event's FQN + fields (the editor-discovered
    /// shape), exercising the baked branch (vs the catalog path). Target self-defaults when unwired.
    /// </summary>
    public GraphBuilder PublishCustomEvent(
        string eventTypeFqn, string? targetFieldName = null,
        IReadOnlyList<(string Name, string TypeId)>? fields = null)
    {
        var nodeId = MakeNodeId("PublishEvent", _nodes.Count);
        var node = new PublishEventNode
        {
            Id              = nodeId,
            EventId         = eventTypeFqn,
            EventTypeFqn    = eventTypeFqn,
            TargetFieldName = targetFieldName,
            PayloadFields   = (fields ?? new List<(string, string)>())
                .Select(f => new PublishEventFieldDecl { Name = f.Name, TypeId = f.TypeId }).ToList(),
        };
        RegisterNode(node, hasExecIn: true, hasExecOut: true);
        return this;
    }

    /// <summary>Adds a WaitForChannelNode.</summary>
    public GraphBuilder WaitForChannel(string channelType)
    {
        var nodeId = MakeNodeId("WaitForChannel", _nodes.Count);
        var node = new WaitForChannelNode { Id = nodeId, ChannelType = channelType };
        RegisterNode(node, hasExecIn: true, hasExecOut: true);
        return this;
    }

    /// <summary>
    /// Q#13: adds a WaitForChannelNode with the "OnFailure" exec-out wired to a sub-chain, plus the
    /// normal success exec-out ("ExecOut") that the MAIN chain continues from. Mirrors <see cref="Branch"/>'s
    /// named-exec-out sub-builder pattern. The success exec-out is deliberately NOT named "OnFailure"
    /// so Stage5's exclude-OnFailure resolution treats it as the success path.
    /// </summary>
    public GraphBuilder WaitForChannelWithFailure(string channelType, Action<GraphBuilder> onFailure)
    {
        var nodeId = MakeNodeId("WaitForChannel", _nodes.Count);
        var node = new WaitForChannelNode { Id = nodeId, ChannelType = channelType };

        var execInPinId    = MakePinId(nodeId, "ExecIn");
        var execOutPinId   = MakePinId(nodeId, "ExecOut");    // success continuation
        var onFailurePinId = MakePinId(nodeId, "OnFailure");  // failure continuation

        node.Pins.Add(new Pin { Id = execInPinId,    Name = "ExecIn",    Direction = "In",  IsExec = true, TypeRef = new() });
        node.Pins.Add(new Pin { Id = execOutPinId,   Name = "ExecOut",   Direction = "Out", IsExec = true, TypeRef = new() });
        node.Pins.Add(new Pin { Id = onFailurePinId, Name = "OnFailure", Direction = "Out", IsExec = true, TypeRef = new() });

        LinkExec(_lastNodeId, _lastExecOutPinId, nodeId, execInPinId);
        _nodes.Add(node);

        // OnFailure sub-chain starts from the OnFailure exec-out.
        var failBuilder = new GraphBuilder(_name + "_OnFailure", _kind, _assetId);
        failBuilder._lastNodeId = nodeId;
        failBuilder._lastExecOutPinId = onFailurePinId;
        onFailure(failBuilder);
        _nodes.AddRange(failBuilder._nodes);
        _links.AddRange(failBuilder._links);

        // Main chain continues from the SUCCESS exec-out.
        _lastNodeId = nodeId;
        _lastExecOutPinId = execOutPinId;
        return this;
    }

    /// <summary>
    /// Q#13-D: adds a WaitForEventNode with the "OnFailure" exec-out wired to a sub-chain, plus the
    /// success exec-out ("ExecOut") the main chain continues from. Mirrors <see cref="WaitForChannelWithFailure"/>.
    /// </summary>
    public GraphBuilder WaitForEventWithFailure(string eventTypeId, Action<GraphBuilder> onFailure)
    {
        var nodeId = MakeNodeId("WaitForEvent", _nodes.Count);
        var node = new WaitForEventNode { Id = nodeId, EventTypeId = eventTypeId };

        var execInPinId    = MakePinId(nodeId, "ExecIn");
        var execOutPinId   = MakePinId(nodeId, "ExecOut");
        var onFailurePinId = MakePinId(nodeId, "OnFailure");

        node.Pins.Add(new Pin { Id = execInPinId,    Name = "ExecIn",    Direction = "In",  IsExec = true, TypeRef = new() });
        node.Pins.Add(new Pin { Id = execOutPinId,   Name = "ExecOut",   Direction = "Out", IsExec = true, TypeRef = new() });
        node.Pins.Add(new Pin { Id = onFailurePinId, Name = "OnFailure", Direction = "Out", IsExec = true, TypeRef = new() });

        LinkExec(_lastNodeId, _lastExecOutPinId, nodeId, execInPinId);
        _nodes.Add(node);

        var failBuilder = new GraphBuilder(_name + "_OnFailure", _kind, _assetId);
        failBuilder._lastNodeId = nodeId;
        failBuilder._lastExecOutPinId = onFailurePinId;
        onFailure(failBuilder);
        _nodes.AddRange(failBuilder._nodes);
        _links.AddRange(failBuilder._links);

        _lastNodeId = nodeId;
        _lastExecOutPinId = execOutPinId;
        return this;
    }

    /// <summary>Adds a SetVariableNode.</summary>
    public GraphBuilder SetVariable(string variableName, string valueExpression)
    {
        var nodeId = MakeNodeId("SetVariable", _nodes.Count);
        var node = new SetVariableNode { Id = nodeId, VariableId = variableName };
        RegisterNode(node, hasExecIn: true, hasExecOut: true);
        return this;
    }

    /// <summary>
    /// Adds a SequenceNode and invokes sub-builder callbacks for each Then branch.
    /// Both branches execute sequentially in the same tick — produces separate IR blocks per branch.
    /// After this call, exec chaining from the main builder is suspended.
    /// </summary>
    public GraphBuilder Sequence(Action<GraphBuilder> then0, Action<GraphBuilder> then1)
    {
        var nodeId = MakeNodeId("Sequence", _nodes.Count);
        var seqNode = new SequenceNode { Id = nodeId };

        var execInPinId   = MakePinId(nodeId, "In");
        var execOutThen0  = MakePinId(nodeId, "Then0");
        var execOutThen1  = MakePinId(nodeId, "Then1");

        seqNode.Pins.Add(new Pin { Id = execInPinId,  Name = "In",    Direction = "In",  IsExec = true, TypeRef = new() });
        seqNode.Pins.Add(new Pin { Id = execOutThen0, Name = "Then0", Direction = "Out", IsExec = true, TypeRef = new() });
        seqNode.Pins.Add(new Pin { Id = execOutThen1, Name = "Then1", Direction = "Out", IsExec = true, TypeRef = new() });

        LinkExec(_lastNodeId, _lastExecOutPinId, nodeId, execInPinId);
        _nodes.Add(seqNode);

        // Then0 branch sub-builder.
        var then0Builder = new GraphBuilder(_name + "_Then0", _kind, _assetId);
        then0Builder._lastNodeId      = nodeId;
        then0Builder._lastExecOutPinId = execOutThen0;
        then0(then0Builder);
        _nodes.AddRange(then0Builder._nodes);
        _links.AddRange(then0Builder._links);

        // Then1 branch sub-builder.
        var then1Builder = new GraphBuilder(_name + "_Then1", _kind, _assetId);
        then1Builder._lastNodeId      = nodeId;
        then1Builder._lastExecOutPinId = execOutThen1;
        then1(then1Builder);
        _nodes.AddRange(then1Builder._nodes);
        _links.AddRange(then1Builder._links);

        // After a Sequence, exec chaining from this builder is suspended (divergence).
        _lastNodeId      = Guid.Empty;
        _lastExecOutPinId = Guid.Empty;
        return this;
    }

    /// <summary>
    /// Adds a BranchNode and invokes two sub-builder callbacks for the true and false branches.
    /// After this call, exec chaining from the main builder is suspended (branch is a divergence).
    /// </summary>
    public GraphBuilder Branch(
        string conditionExpression,
        Action<GraphBuilder> trueBranch,
        Action<GraphBuilder> falseBranch)
    {
        var nodeId = MakeNodeId("Branch", _nodes.Count);
        var branchNode = new BranchNode { Id = nodeId };

        var execInPinId = MakePinId(nodeId, "ExecIn");
        var execOutTruePinId = MakePinId(nodeId, "ExecOutTrue");
        var execOutFalsePinId = MakePinId(nodeId, "ExecOutFalse");

        branchNode.Pins.Add(new Pin { Id = execInPinId, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() });
        branchNode.Pins.Add(new Pin { Id = execOutTruePinId, Name = "ExecOutTrue", Direction = "Out", IsExec = true, TypeRef = new() });
        branchNode.Pins.Add(new Pin { Id = execOutFalsePinId, Name = "ExecOutFalse", Direction = "Out", IsExec = true, TypeRef = new() });

        LinkExec(_lastNodeId, _lastExecOutPinId, nodeId, execInPinId);
        _nodes.Add(branchNode);

        // True branch sub-builder -- starts from branch node's true exec-out.
        var trueBuilder = new GraphBuilder(_name + "_True", _kind, _assetId);
        trueBuilder._lastNodeId = nodeId;
        trueBuilder._lastExecOutPinId = execOutTruePinId;
        trueBranch(trueBuilder);
        _nodes.AddRange(trueBuilder._nodes);
        _links.AddRange(trueBuilder._links);

        // False branch sub-builder -- starts from branch node's false exec-out.
        var falseBuilder = new GraphBuilder(_name + "_False", _kind, _assetId);
        falseBuilder._lastNodeId = nodeId;
        falseBuilder._lastExecOutPinId = execOutFalsePinId;
        falseBranch(falseBuilder);
        _nodes.AddRange(falseBuilder._nodes);
        _links.AddRange(falseBuilder._links);

        // After a branch, exec chaining from this level is cleared (divergence point).
        _lastNodeId = Guid.Empty;
        _lastExecOutPinId = Guid.Empty;
        return this;
    }

    /// <summary>Builds and returns the completed Graph object.</summary>
    public Graph Build()
    {
        return new Graph
        {
            Id = _graphId,
            Name = _name,
            Kind = _kind,
            Nodes = new List<Node>(_nodes),
            Links = new List<Link>(_links),
            Inputs = new List<ParameterDecl>(_inputs),
            Outputs = new(),
        };
    }
}

// ============================================================
// BlueprintAssetBuilder
// ============================================================

/// <summary>
/// Fluent builder for constructing BlueprintAsset objects for tests.
/// All list fields are non-null (empty lists if unused).
/// NewSyntheticGuid is deterministic: same call sequence produces identical GUIDs.
/// </summary>
public sealed class BlueprintAssetBuilder
{
    private string _assetId;
    private Guid _assetGuid;
    private BlueprintDispatchKind _dispatch;
    private BlackboardTierHint _tierHint = BlackboardTierHint.Auto;
    private bool _isWorldSingleton;
    private AiPrimitiveDecl? _primitive;
    private readonly List<ParameterDecl> _parameters = new();
    private readonly List<VariableDecl> _workingState = new();
    private readonly List<VariableDecl> _variables = new();
    private readonly List<EventDispatcherDecl> _eventDispatchers = new();
    private readonly List<CustomEventDecl> _customEvents = new();
    private readonly List<Guid> _callablePeers = new();
    private readonly List<Graph> _graphs = new();

    private BlueprintAssetBuilder(string name, BlueprintDispatchKind dispatch)
    {
        _assetId = name;
        _dispatch = dispatch;
        _assetGuid = NewSyntheticGuid(name);
    }

    // -- Static factories --

    public static BlueprintAssetBuilder Library(string name)
        => new(name, BlueprintDispatchKind.Library);

    public static BlueprintAssetBuilder AiPrimitive(string name)
    {
        var builder = new BlueprintAssetBuilder(name, BlueprintDispatchKind.AiPrimitive);
        builder._primitive = new AiPrimitiveDecl
        {
            Intent = AiPrimitiveIntent.Action,
            Hostings = new List<AiPrimitiveHosting>(),
        };
        return builder;
    }

    public static BlueprintAssetBuilder Instance(string name)
        => new(name, BlueprintDispatchKind.Instance);

    public static BlueprintAssetBuilder Instance(string name, Guid assetId)
    {
        var builder = new BlueprintAssetBuilder(name, BlueprintDispatchKind.Instance);
        builder._assetGuid = assetId;
        return builder;
    }

    // -- Fluent methods --

    public BlueprintAssetBuilder WithAssetId(string id)
    {
        _assetId = id;
        _assetGuid = NewSyntheticGuid(id);
        return this;
    }

    public BlueprintAssetBuilder WithTierHint(BlackboardTierHint hint)
    {
        _tierHint = hint;
        return this;
    }

    public BlueprintAssetBuilder WithWorldSingleton(bool isWorldSingleton = true)
    {
        _isWorldSingleton = isWorldSingleton;
        return this;
    }

    public BlueprintAssetBuilder WithIntent(AiPrimitiveIntent intent)
    {
        if (_primitive is null)
            throw new InvalidOperationException("WithIntent can only be called on an AiPrimitive builder.");
        _primitive.Intent = intent;
        return this;
    }

    public BlueprintAssetBuilder WithHostings(params AiPrimitiveHosting[] hostings)
    {
        if (_primitive is null)
            throw new InvalidOperationException("WithHostings can only be called on an AiPrimitive builder.");
        _primitive.Hostings.AddRange(hostings);
        return this;
    }

    public BlueprintAssetBuilder WithParameter(string name, Type type, string? defaultJson = null)
    {
        _parameters.Add(new ParameterDecl
        {
            Id = NewSyntheticGuid("Param", name),
            Name = name,
            Type = new BlueprintTypeRef { TypeId = type.FullName ?? type.Name },
            DefaultValueJson = defaultJson,
        });
        return this;
    }

    public BlueprintAssetBuilder WithWorkingStateField(string name, Type type)
    {
        _workingState.Add(new VariableDecl
        {
            Id = NewSyntheticGuid("WorkingState", name),
            Name = name,
            Type = new BlueprintTypeRef { TypeId = type.FullName ?? type.Name },
        });
        return this;
    }

    public BlueprintAssetBuilder WithVariable(string name, Type type, string? defaultJson = null)
    {
        _variables.Add(new VariableDecl
        {
            Id = NewSyntheticGuid("Var", name),
            Name = name,
            Type = new BlueprintTypeRef { TypeId = type.FullName ?? type.Name },
            DefaultValueJson = defaultJson,
        });
        return this;
    }

    public BlueprintAssetBuilder WithCallablePeer(string peerAssetId)
    {
        _callablePeers.Add(NewSyntheticGuid("Peer", peerAssetId));
        return this;
    }

    public BlueprintAssetBuilder WithCustomEvent(string name, params (string paramName, Type paramType)[] parameters)
    {
        var paramDecls = parameters.Select(p => new ParameterDecl
        {
            Id = NewSyntheticGuid("CEParam", name, p.paramName),
            Name = p.paramName,
            Type = new BlueprintTypeRef { TypeId = p.paramType.FullName ?? p.paramType.Name },
        }).ToList();

        _customEvents.Add(new CustomEventDecl
        {
            Id = NewSyntheticGuid("CustomEvent", name),
            Name = name,
            Parameters = paramDecls,
        });
        return this;
    }

    public BlueprintAssetBuilder WithGraph(string name, Action<GraphBuilder> configure)
        => WithGraph(name, GraphKind.Function, configure);

    public BlueprintAssetBuilder WithGraph(string name, GraphKind kind, Action<GraphBuilder> configure)
    {
        var gb = new GraphBuilder(name, kind, _assetGuid);
        configure(gb);
        _graphs.Add(gb.Build());
        return this;
    }

    public BlueprintAssetBuilder WithEventGraph(string eventTypeName, Action<GraphBuilder> configure)
        => WithGraph(eventTypeName, GraphKind.Event, configure);

    // -- Build --

    public BlueprintAsset Build()
    {
        return new BlueprintAsset
        {
            AssetId = _assetGuid,
            Name = _assetId,
            Dispatch = _dispatch,
            TierHint = _tierHint,
            IsWorldSingleton = _isWorldSingleton,
            Primitive = _dispatch == BlueprintDispatchKind.AiPrimitive ? _primitive : null,
            Parameters = new List<ParameterDecl>(_parameters),
            WorkingState = new List<VariableDecl>(_workingState),
            Variables = new List<VariableDecl>(_variables),
            EventDispatchers = new List<EventDispatcherDecl>(_eventDispatchers),
            CustomEvents = new List<CustomEventDecl>(_customEvents),
            CallablePeers = new List<Guid>(_callablePeers),
            Graphs = new List<Graph>(_graphs),
            Header = new Header(),
        };
    }

    // -- Internal helpers --

    private Guid NewSyntheticGuid(params object[] parts)
    {
        using var sha = SHA256.Create();
        var buf = new List<byte>(128);
        buf.AddRange(System.Text.Encoding.UTF8.GetBytes(_assetId));
        buf.Add(0x1F);
        foreach (var part in parts)
        {
            buf.AddRange(System.Text.Encoding.UTF8.GetBytes(part?.ToString() ?? ""));
            buf.Add(0x1F);
        }
        var hash = sha.ComputeHash(buf.ToArray());
        byte[] guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, 16);
        return new Guid(guidBytes);
    }
}
