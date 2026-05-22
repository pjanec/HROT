using Hrot.Blueprints.Core.Assets;
using AssetDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;
using Hrot.Blueprints.Core.Compiler.Determinism;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Emit;
using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Stages;

internal static class Stage5_Schedule
{
    // Sentinel unresolved IrTypeRef used when no type information is available.
    internal static readonly IrTypeRef UnknownType = new IrTypeRef
    {
        FullName = "?",
        IsUnmanaged = false,
        SizeBytes = 0,
    };

    internal static readonly IrTypeRef Int32Type = new IrTypeRef
    {
        FullName = "System.Int32",
        IsUnmanaged = true,
        SizeBytes = 4,
    };

    public static IrAsset Run(TypedAsset typedAsset, ValidationContext ctx)
    {
        var irGraphs = new List<IrGraph>();
        foreach (var graph in typedAsset.Asset.Graphs)
        {
            var scheduler = new GraphScheduler(graph, typedAsset, ctx);
            irGraphs.Add(scheduler.Schedule());
        }

        var asset = typedAsset.Asset;
        return new IrAsset
        {
            AssetId       = asset.AssetId,
            Name          = asset.Name,
            SanitizedName = Sanitizer.SanitizeName(asset.Name),
            BlueprintId   = BlueprintIdHash.Compute(asset.AssetId),
            StructureHash = 0,  // assigned in Stage 6 after layout finalization
            Dispatch      = asset.Dispatch,
            Intent        = asset.Primitive?.Intent,
            Hostings      = (IReadOnlyList<AiPrimitiveHosting>?)asset.Primitive?.Hostings ?? Array.Empty<AiPrimitiveHosting>(),
            Parameters    = BuildIrFields(asset.Parameters, typedAsset),
            WorkingState  = BuildIrFields(asset.WorkingState, typedAsset),
            Variables     = BuildIrFields(asset.Variables, typedAsset),
            CustomEvents  = BuildCustomEvents(asset.CustomEvents, typedAsset),
            CallablePeerBlueprintIds = BuildPeerIds(asset.CallablePeers),
            IsWorldSingleton = asset.IsWorldSingleton,
            Graphs        = irGraphs,
        };
    }

    private static IReadOnlyList<IrField> BuildIrFields(
        IEnumerable<ParameterDecl> decls, TypedAsset typed)
    {
        var result = new List<IrField>();
        foreach (var d in decls)
        {
            typed.FieldTypes.TryGetValue(d.Id, out var irType);
            result.Add(new IrField
            {
                Id = d.Id,
                Name = d.Name,
                Type = irType ?? UnknownType,
                DefaultValueCSharp = d.DefaultValueJson ?? "",
            });
        }
        return result;
    }

    private static IReadOnlyList<IrField> BuildIrFields(
        IEnumerable<VariableDecl> decls, TypedAsset typed)
    {
        var result = new List<IrField>();
        foreach (var d in decls)
        {
            typed.FieldTypes.TryGetValue(d.Id, out var irType);
            result.Add(new IrField
            {
                Id = d.Id,
                Name = d.Name,
                Type = irType ?? UnknownType,
                DefaultValueCSharp = d.DefaultValueJson ?? "",
            });
        }
        return result;
    }

    private static IReadOnlyList<IrCustomEvent> BuildCustomEvents(
        IEnumerable<CustomEventDecl> decls, TypedAsset typed)
    {
        var result = new List<IrCustomEvent>();
        foreach (var d in decls)
        {
            result.Add(new IrCustomEvent
            {
                Id = d.Id,
                Name = d.Name,
                Parameters = BuildIrFields(d.Parameters, typed),
            });
        }
        return result;
    }

    private static IReadOnlyList<int> BuildPeerIds(IEnumerable<Guid> peerGuids)
    {
        return peerGuids.Select(BlueprintIdHash.Compute).ToList();
    }
}

// ---------------------------------------------------------------------------
// GraphScheduler -- BFS-based basic-block scheduler
// ---------------------------------------------------------------------------

internal sealed class GraphScheduler
{
    private readonly Graph           _graph;
    private readonly TypedAsset      _typed;
    private readonly ValidationContext _ctx;
    private readonly Dictionary<Guid, Node> _nodeById;

    // Mutable state
    private int _nextBlockId   = 0;
    private int _nextValueIndex = 0;
    private int _resumeCounter = 0;

    // BlockBuilder is mutated during scheduling, then sealed into IrBlock.
    private readonly List<BlockBuilder> _blockBuilders = new();

    // BFS queue: (blockId, startNode for this block)
    private readonly Queue<(int blockId, Node startNode)> _bfsQueue = new();

    // Per-block CSE cache (cleared when starting each new block)
    private readonly Dictionary<Guid, IrValue> _pinValueCache = new();

    // Tracks whether a block has been fully scheduled
    private readonly HashSet<int> _scheduledBlocks = new();

    public GraphScheduler(Graph graph, TypedAsset typed, ValidationContext ctx)
    {
        _graph   = graph;
        _typed   = typed;
        _ctx     = ctx;
        _nodeById = graph.Nodes.ToDictionary(n => n.Id);
    }

    public IrGraph Schedule()
    {
        var entryNode = V_GraphStructure.FindEntryNode(_graph);
        if (entryNode is null)
        {
            // Validation already emitted BP1602; return an empty graph.
            return new IrGraph
            {
                Id    = _graph.Id,
                Name  = _graph.Name,
                Kind  = MapGraphKind(_graph.Kind),
                Entry = new IrBlockId(0),
            };
        }

        var entryBlockId = AllocBlock("entry");
        _bfsQueue.Enqueue((entryBlockId.Value, entryNode));

        while (_bfsQueue.Count > 0)
        {
            var (blockId, startNode) = _bfsQueue.Dequeue();
            if (!_scheduledBlocks.Add(blockId)) continue;

            _pinValueCache.Clear();
            ScheduleBlock(blockId, startNode);
        }

        return new IrGraph
        {
            Id     = _graph.Id,
            Name   = _graph.Name,
            Kind   = MapGraphKind(_graph.Kind),
            Blocks = _blockBuilders.Select(b => b.Build()).ToList().AsReadOnly(),
            Entry  = new IrBlockId(0),
        };
    }

    // -----------------------------------------------------------------------
    // ScheduleBlock -- walk exec chain and populate a block
    // -----------------------------------------------------------------------

    private void ScheduleBlock(int blockId, Node startNode)
    {
        var bb = _blockBuilders[blockId];
        var node = startNode;

        while (true)
        {
            switch (node)
            {
                case EventEntryNode:
                    // No statements; continue to exec successor.
                    var esucc = GetSingleExecSuccessor(node);
                    if (esucc is null)
                    {
                        bb.Terminator = new IrTerm_FallThrough
                        {
                            Debug = DebugOf(node),
                        };
                        return;
                    }
                    node = esucc;
                    continue;

                case ReturnNode rn:
                    bb.Terminator = BuildReturnTerminator(rn);
                    return;

                case BranchNode bn:
                    ScheduleBranchNode(bn, bb);
                    return;

                case LatentDelayNode ld:
                    ScheduleLatentNode(ld, bb, BuildLatentDelayOp(ld, bb.Statements));
                    return;

                case WaitForChannelNode wfc:
                    ScheduleLatentNode(wfc, bb, BuildWaitForChannelOp(wfc));
                    return;

                case WaitForEventNode wfe:
                    ScheduleLatentNode(wfe, bb, BuildWaitForEventOp(wfe));
                    return;

                default:
                    // Regular node: emit statements, then follow exec chain.
                    EmitNodeStatements(node, bb.Statements);
                    var succ = GetSingleExecSuccessor(node);
                    if (succ is null)
                    {
                        bb.Terminator = new IrTerm_FallThrough { Debug = DebugOf(node) };
                        return;
                    }
                    node = succ;
                    continue;
            }
        }
    }

    // -----------------------------------------------------------------------
    // Latent node handling (SC5)
    // -----------------------------------------------------------------------

    private void ScheduleLatentNode(Node node, BlockBuilder bb, IrOperation latentOp)
    {
        // Append the latent marker as the last statement in the pre-suspend block.
        bb.Statements.Add(new IrStatement
        {
            ResultValue = null,
            Operation   = latentOp,
            Debug       = DebugOf(node),
        });

        // Allocate resume block.
        var resumeLabel = $"wait_resume_{_resumeCounter++}";
        var resumeBlockId = AllocBlock(resumeLabel);

        // Resume point value: a const int carrying the resume index.
        var resumePointValue = AllocValue(Stage5_Schedule.Int32Type);
        bb.Statements.Add(new IrStatement
        {
            ResultValue = resumePointValue,
            Operation   = new IrOp_Const((_resumeCounter - 1).ToString(),
                                          Stage5_Schedule.Int32Type),
            Debug       = new IrDebugAnnotation
            {
                GraphId    = _graph.Id,
                Synthesized = "resume-point",
            },
        });

        bb.Terminator = new IrTerm_Suspend(
            ResumePoint  : resumePointValue,
            WaitUntilTime: null,
            ResumeBlock  : resumeBlockId)
        {
            Debug = DebugOf(node),
        };

        // Enqueue continuation in resume block.
        var continuation = GetSingleExecSuccessor(node);
        if (continuation is not null)
            _bfsQueue.Enqueue((resumeBlockId.Value, continuation));
        else
            // No successor -- resume block is empty with fall-through.
            _scheduledBlocks.Add(resumeBlockId.Value);
    }

    // -----------------------------------------------------------------------
    // Branch node handling
    // -----------------------------------------------------------------------

    private void ScheduleBranchNode(BranchNode bn, BlockBuilder bb)
    {
        // Resolve condition data input (first non-exec data-in pin).
        var condPin = bn.Pins.FirstOrDefault(p => !p.IsExec && p.Direction == "In");
        IrValue condValue;
        if (condPin is not null)
            condValue = ResolveDataPin(bn.Id, condPin.Id, bb.Statements);
        else
        {
            // No condition pin -- synthesize a false const.
            condValue = AllocValue(new IrTypeRef
                { FullName = "System.Boolean", IsUnmanaged = true, SizeBytes = 1 });
            bb.Statements.Add(new IrStatement
            {
                ResultValue = condValue,
                Operation   = new IrOp_Const("false",
                    new IrTypeRef { FullName = "System.Boolean", IsUnmanaged = true, SizeBytes = 1 }),
                Debug = DebugOf(bn),
            });
        }

        var idShort = bn.Id.ToString("N").Substring(0, 8);
        var trueBlock  = AllocBlock($"branch_{idShort}_true");
        var falseBlock = AllocBlock($"branch_{idShort}_false");

        bb.Terminator = new IrTerm_Branch(condValue, trueBlock, falseBlock)
        {
            Debug = DebugOf(bn),
        };

        var (trueSucc, falseSucc) = GetBranchSuccessors(bn);
        if (trueSucc  is not null) _bfsQueue.Enqueue((trueBlock.Value,  trueSucc));
        if (falseSucc is not null) _bfsQueue.Enqueue((falseBlock.Value, falseSucc));
    }

    // -----------------------------------------------------------------------
    // Emit statements for a regular (non-terminal) node
    // -----------------------------------------------------------------------

    private void EmitNodeStatements(Node node, List<IrStatement> stmts)
    {
        switch (node)
        {
            case SetVariableNode sv:
            {
                int idx = FindVariableIndex(sv.VariableId);
                var dataPin = node.Pins.FirstOrDefault(p => !p.IsExec && p.Direction == "In");
                if (dataPin is null) break;
                var val = ResolveDataPin(node.Id, dataPin.Id, stmts);
                stmts.Add(new IrStatement
                {
                    Operation = new IrOp_WriteVariable(idx, val),
                    Debug     = DebugOf(node),
                });
                break;
            }

            case FunctionCallNode fc when !fc.IsPure:
            {
                // Impure library call -- resolve inputs, emit call, cache output.
                var inputVals = ResolveAllDataInputs(node, stmts);
                var outPin = node.Pins.FirstOrDefault(p => !p.IsExec && p.Direction == "Out");
                IrTypeRef retType = outPin is not null && _typed.PinTypes.TryGetValue(outPin.Id, out var t)
                    ? t
                    : Stage5_Schedule.UnknownType;
                var result = AllocValue(retType);
                stmts.Add(new IrStatement
                {
                    ResultValue = result,
                    Operation   = new IrOp_LibraryCall(0, $"{fc.TargetTypeId}.{fc.MethodName}",
                                                       inputVals, retType),
                    Debug = DebugOf(node),
                });
                if (outPin is not null)
                    _pinValueCache[outPin.Id] = result;
                break;
            }

            case CallPeerBlueprintNode cpb:
            {
                if (!Guid.TryParse(cpb.PeerBlueprintId, out var peerId)) break;
                int peerId32 = BlueprintIdHash.Compute(peerId);
                var inputVals = ResolveAllDataInputs(node, stmts);
                var outPin = node.Pins.FirstOrDefault(p => !p.IsExec && p.Direction == "Out");
                IrTypeRef retType = outPin is not null && _typed.PinTypes.TryGetValue(outPin.Id, out var t)
                    ? t : Stage5_Schedule.UnknownType;
                var result = AllocValue(retType);
                stmts.Add(new IrStatement
                {
                    ResultValue = result,
                    Operation   = new IrOp_PeerCall(peerId32, cpb.FunctionRef, inputVals, retType),
                    Debug       = DebugOf(node),
                });
                if (outPin is not null)
                    _pinValueCache[outPin.Id] = result;
                break;
            }

            case ChannelCommandNode cc:
            {
                var paramFields = node.Pins
                    .Where(p => !p.IsExec && p.Direction == "In")
                    .Select(p =>
                    {
                        var val = ResolveDataPin(node.Id, p.Id, stmts);
                        return (p.Name, val);
                    })
                    .ToList();
                stmts.Add(new IrStatement
                {
                    Operation = new IrOp_ChannelCommand(
                        cc.ChannelType, cc.ActionId, "", paramFields),
                    Debug = DebugOf(node),
                });
                break;
            }

            case CallCustomEventNode cce:
            {
                int idx = FindCustomEventIndex(cce.EventId);
                var inputVals = ResolveAllDataInputs(node, stmts);
                stmts.Add(new IrStatement
                {
                    Operation = new IrOp_RaiseCustomEvent(idx, inputVals),
                    Debug     = DebugOf(node),
                });
                break;
            }

            case SequenceNode:
                // SequenceNode just chains execution; no statements needed.
                break;

            default:
                // Unknown impure node kind -- emit BP4004 and skip.
                _ctx.Diagnostics.Add(Diagnostic.Warning(DiagnosticCodes.BP4004,
                    $"Unknown node kind '{node.GetType().Name}' -- no IR emitted.",
                    _ctx.AssetId, _graph.Id, node.Id));
                break;
        }
    }

    // -----------------------------------------------------------------------
    // Latent operation builders
    // -----------------------------------------------------------------------

    private IrOperation BuildLatentDelayOp(LatentDelayNode ld, List<IrStatement> stmts)
    {
        // Resolve seconds data input if available.
        var secsPin = ld.Pins.FirstOrDefault(p => !p.IsExec && p.Direction == "In");
        IrValue secsValue;
        if (secsPin is not null)
            secsValue = ResolveDataPin(ld.Id, secsPin.Id, stmts);
        else
        {
            secsValue = AllocValue(new IrTypeRef
                { FullName = "System.Single", IsUnmanaged = true, SizeBytes = 4 });
            stmts.Add(new IrStatement
            {
                ResultValue = secsValue,
                Operation   = new IrOp_Const("0f",
                    new IrTypeRef { FullName = "System.Single", IsUnmanaged = true, SizeBytes = 4 }),
                Debug = DebugOf(ld),
            });
        }
        return new IrOp_LatentDelay(secsValue);
    }

    private static IrOperation BuildWaitForChannelOp(WaitForChannelNode wfc)
        => new IrOp_WaitForChannel(wfc.ChannelType, Array.Empty<IrField>());

    private static IrOperation BuildWaitForEventOp(WaitForEventNode wfe)
        => new IrOp_WaitForEvent(wfe.EventTypeId, wfe.FilterByField, null,
                                  Array.Empty<IrField>());

    // -----------------------------------------------------------------------
    // Return terminator builder
    // -----------------------------------------------------------------------

    private IrTerminator BuildReturnTerminator(ReturnNode rn)
    {
        if (_typed.Asset.Dispatch == AssetDispatchKind.AiPrimitive
            || _typed.Asset.Dispatch == AssetDispatchKind.Library)
        {
            // AiPrimitive returns a NodeStatus.
            return new IrTerm_ReturnStatus(rn.Status) { Debug = DebugOf(rn) };
        }

        // Function graph: return data value from output pin (if any).
        var outPin = rn.Pins.FirstOrDefault(p => !p.IsExec && p.Direction == "Out");
        IrValue? retVal = null;
        if (outPin is not null)
            retVal = ResolveDataPin(rn.Id, outPin.Id, _blockBuilders[_blockBuilders.Count - 1].Statements);

        return new IrTerm_Return(retVal) { Debug = DebugOf(rn) };
    }

    // -----------------------------------------------------------------------
    // Data flow resolution (CSE via _pinValueCache)
    // -----------------------------------------------------------------------

    private IrValue ResolveDataPin(Guid consumerNodeId, Guid pinId,
                                    List<IrStatement> stmts)
    {
        if (_pinValueCache.TryGetValue(pinId, out var cached)) return cached;

        // Find link providing data to this pin.
        var link = _graph.Links.FirstOrDefault(
            l => l.ToNodeId == consumerNodeId && l.ToPinId == pinId);

        if (link == null)
        {
            // Unconnected -- emit BP4001 and return a dummy value.
            _ctx.Diagnostics.Add(Diagnostic.Warning(DiagnosticCodes.BP4001,
                $"Unconnected required data input pin {pinId} on node {consumerNodeId}.",
                _ctx.AssetId, _graph.Id, consumerNodeId, pinId));
            var dummy = AllocValue(Stage5_Schedule.UnknownType);
            _pinValueCache[pinId] = dummy;
            return dummy;
        }

        // Resolve source node's output.
        var value = ResolveNodeOutput(link.FromNodeId, link.FromPinId, stmts);
        _pinValueCache[pinId] = value;
        return value;
    }

    private IrValue ResolveNodeOutput(Guid sourceNodeId, Guid sourcePinId,
                                       List<IrStatement> stmts)
    {
        if (_pinValueCache.TryGetValue(sourcePinId, out var cached)) return cached;

        if (!_nodeById.TryGetValue(sourceNodeId, out var sourceNode))
        {
            var dummy = AllocValue(Stage5_Schedule.UnknownType);
            _pinValueCache[sourcePinId] = dummy;
            return dummy;
        }

        _typed.PinTypes.TryGetValue(sourcePinId, out var pinType);
        pinType ??= Stage5_Schedule.UnknownType;
        IrValue result;

        switch (sourceNode)
        {
            case LiteralNode ln:
                result = AllocValue(pinType);
                stmts.Add(new IrStatement
                {
                    ResultValue = result,
                    Operation   = new IrOp_Const(ln.ValueJson, pinType),
                    Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = ln.Id, PinId = sourcePinId },
                });
                break;

            case GetVariableNode gv:
                int varIdx = FindVariableIndex(gv.VariableId);
                result = AllocValue(pinType);
                stmts.Add(new IrStatement
                {
                    ResultValue = result,
                    Operation   = new IrOp_ReadVariable(varIdx),
                    Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = gv.Id, PinId = sourcePinId },
                });
                break;

            case FunctionCallNode fc when fc.IsPure:
                var inputArgs = ResolveAllDataInputs(sourceNode, stmts);
                result = AllocValue(pinType);
                stmts.Add(new IrStatement
                {
                    ResultValue = result,
                    Operation   = new IrOp_PureCall(
                        $"{fc.TargetTypeId}.{fc.MethodName}", inputArgs, pinType),
                    Debug = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = fc.Id, PinId = sourcePinId },
                });
                break;

            case CastNode cn:
            {
                var castInputPin = cn.Pins.FirstOrDefault(p => !p.IsExec && p.Direction == "In");
                IrValue castInput;
                if (castInputPin is not null)
                    castInput = ResolveDataPin(cn.Id, castInputPin.Id, stmts);
                else
                    castInput = AllocValue(Stage5_Schedule.UnknownType);

                result = AllocValue(pinType);
                stmts.Add(new IrStatement
                {
                    ResultValue = result,
                    Operation   = new IrOp_PureCall($"Cast.{cn.TargetTypeId}",
                                                    new[] { castInput }, pinType),
                    Debug = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = cn.Id },
                });
                break;
            }

            default:
            {
                // Unknown pure source -- dummy value.
                result = AllocValue(pinType);
                stmts.Add(new IrStatement
                {
                    ResultValue = result,
                    Operation   = new IrOp_Const("default", pinType),
                    Debug       = new IrDebugAnnotation
                    {
                        GraphId    = _graph.Id,
                        Synthesized = $"unknown-source-{sourceNode.GetType().Name}",
                    },
                });
                break;
            }
        }

        _pinValueCache[sourcePinId] = result;
        return result;
    }

    private IReadOnlyList<IrValue> ResolveAllDataInputs(Node node, List<IrStatement> stmts)
    {
        return node.Pins
            .Where(p => !p.IsExec && p.Direction == "In")
            .Select(p => ResolveDataPin(node.Id, p.Id, stmts))
            .ToList();
    }

    // -----------------------------------------------------------------------
    // Exec chain helpers
    // -----------------------------------------------------------------------

    private Node? GetSingleExecSuccessor(Node node)
    {
        var execOutPins = node.Pins.Where(p => p.IsExec && p.Direction == "Out").ToList();
        if (execOutPins.Count != 1) return null;

        var link = _graph.Links.FirstOrDefault(
            l => l.FromNodeId == node.Id && l.FromPinId == execOutPins[0].Id);
        return link is not null && _nodeById.TryGetValue(link.ToNodeId, out var t) ? t : null;
    }

    private (Node? trueSucc, Node? falseSucc) GetBranchSuccessors(BranchNode branch)
    {
        Node? trueSucc = null, falseSucc = null;
        foreach (var pin in branch.Pins.Where(p => p.IsExec && p.Direction == "Out"))
        {
            var link = _graph.Links.FirstOrDefault(
                l => l.FromNodeId == branch.Id && l.FromPinId == pin.Id);
            if (link is null) continue;
            if (!_nodeById.TryGetValue(link.ToNodeId, out var target)) continue;

            if (pin.Name.Contains("True", StringComparison.OrdinalIgnoreCase))
                trueSucc = target;
            else
                falseSucc = target;
        }
        return (trueSucc, falseSucc);
    }

    // -----------------------------------------------------------------------
    // Variable index helpers
    // -----------------------------------------------------------------------

    private int FindVariableIndex(string variableId)
    {
        // Search Instance variables first, then AiPrimitive working-state and parameters.
        var variables  = _typed.Asset.Variables;
        var workState  = _typed.Asset.WorkingState;
        var parameters = _typed.Asset.Parameters;

        if (Guid.TryParse(variableId, out var guid))
        {
            for (int i = 0; i < variables.Count;  i++) if (variables[i].Id  == guid) return i;
            for (int i = 0; i < workState.Count;  i++) if (workState[i].Id  == guid) return i;
            for (int i = 0; i < parameters.Count; i++) if (parameters[i].Id == guid) return i;
        }
        // Name fallback
        for (int i = 0; i < variables.Count;  i++) if (variables[i].Name  == variableId) return i;
        for (int i = 0; i < workState.Count;  i++) if (workState[i].Name  == variableId) return i;
        for (int i = 0; i < parameters.Count; i++) if (parameters[i].Name == variableId) return i;
        return -1;
    }

    private int FindCustomEventIndex(string eventId)
    {
        var events = _typed.Asset.CustomEvents;
        if (Guid.TryParse(eventId, out var guid))
            for (int i = 0; i < events.Count; i++)
                if (events[i].Id == guid) return i;
        for (int i = 0; i < events.Count; i++)
            if (events[i].Name == eventId) return i;
        return -1;
    }

    // -----------------------------------------------------------------------
    // Allocation helpers
    // -----------------------------------------------------------------------

    private IrBlockId AllocBlock(string label)
    {
        var id = new IrBlockId(_nextBlockId++);
        _blockBuilders.Add(new BlockBuilder(id, label));
        return id;
    }

    private IrValue AllocValue(IrTypeRef type)
        => new IrValue(_nextValueIndex++, type);

    private static IrDebugAnnotation DebugOf(Node node) =>
        new IrDebugAnnotation { GraphId = default, NodeId = node.Id };

    private static IrGraphKind MapGraphKind(GraphKind kind) => kind switch
    {
        GraphKind.Function     => IrGraphKind.Function,
        GraphKind.Event        => IrGraphKind.Event,
        GraphKind.Construction => IrGraphKind.Construction,
        _                       => IrGraphKind.Function,
    };

    // -----------------------------------------------------------------------
    // BlockBuilder: mutable accumulator for one IrBlock
    // -----------------------------------------------------------------------

    private sealed class BlockBuilder
    {
        public IrBlockId Id    { get; }
        public string Label    { get; }
        public List<IrStatement> Statements { get; } = new();
        public IrTerminator? Terminator { get; set; }

        public BlockBuilder(IrBlockId id, string label)
        {
            Id    = id;
            Label = label;
        }

        public IrBlock Build() => new IrBlock
        {
            Id         = Id,
            Label      = Label,
            Statements = Statements.AsReadOnly(),
            Terminator = Terminator ?? new IrTerm_FallThrough
            {
                Debug = new IrDebugAnnotation { Synthesized = "auto-fallthrough" },
            },
        };
    }
}

