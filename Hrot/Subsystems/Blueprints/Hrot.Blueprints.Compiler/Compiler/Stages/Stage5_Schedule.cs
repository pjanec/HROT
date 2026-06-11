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
            Parameters    = BuildIrFields(asset.Parameters, typedAsset, asset.ParameterOrder),
            WorkingState  = BuildIrFields(asset.WorkingState, typedAsset, asset.WorkingStateOrder),
            Variables     = BuildIrFields(asset.Variables, typedAsset, asset.VariableOrder),
            CustomEvents  = BuildCustomEvents(asset.CustomEvents, typedAsset),
            CallablePeerBlueprintIds = BuildPeerIds(asset.CallablePeers),
            IsWorldSingleton = asset.IsWorldSingleton,
            Graphs        = irGraphs,
        };
    }

    private static IReadOnlyList<IrField> BuildIrFields(
        IEnumerable<ParameterDecl> decls, TypedAsset typed, List<Guid>? order)
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
                Comment = d.Comment,
            });
        }
        return GetOrdered(result, order);
    }

    private static IReadOnlyList<IrField> BuildIrFields(
        IEnumerable<VariableDecl> decls, TypedAsset typed, List<Guid>? order)
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
                Comment = d.Comment,
            });
        }
        return GetOrdered(result, order);
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
                Parameters = BuildIrFields(d.Parameters, typed, null),
            });
        }
        return result;
    }

    private static IReadOnlyList<IrField> GetOrdered(List<IrField> items, List<Guid>? order)
    {
        if (order == null || order.Count == 0)
            return items;

        var dict = items.ToDictionary(f => f.Id);
        var result = new List<IrField>();
        foreach (var id in order)
        {
            if (dict.TryGetValue(id, out var item))
            {
                result.Add(item);
                dict.Remove(id);
            }
        }
        result.AddRange(dict.Values.OrderBy(f => f.Id));
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

    // Tracks which block each exec node's statements landed in (nodeId → blockId).
    private readonly Dictionary<Guid, int> _execNodeToBlockId = new();

    // Fall-through redirect: when a branch's chain ends in block X,
    // control continues to _fallThroughTarget[X] instead of falling through.
    private readonly Dictionary<int, IrBlockId> _fallThroughTarget = new();

    // Post-BFS actions: appended to fired blocks after all user nodes are scheduled.
    private readonly List<(int blockId, IrStatement stmt)> _whenPostActions = new();

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
        _blockBuilders[entryBlockId.Value].SourceNodeId = entryNode.Id;
        _execNodeToBlockId[entryNode.Id] = entryBlockId.Value;
        _bfsQueue.Enqueue((entryBlockId.Value, entryNode));

        while (_bfsQueue.Count > 0)
        {
            var (blockId, startNode) = _bfsQueue.Dequeue();
            if (!_scheduledBlocks.Add(blockId)) continue;

            _pinValueCache.Clear();
            ScheduleBlock(blockId, startNode);
        }

        // Apply WhenNode post-actions: append StorePrev to each onFired block.
        foreach (var (blockId, stmt) in _whenPostActions)
            _blockBuilders[blockId].Statements.Add(stmt);

        // Propagate graph-level Inputs and Outputs (BATCH-03A: needed by EmitInstanceFunctionMethod
        // to generate the correct parameter list and by IrOp_ReadInputArg rendering).
        var irInputs = BuildIrFieldsFromGraphParams(_graph.Inputs);
        var irOutputs = BuildIrFieldsFromGraphParams(_graph.Outputs);

        // Build BreakpointTargets: authored exec node → its probe id.
        // For nodes that have their own ExecEntryNodeId-tagged statement (SetVariable,
        // BranchNode, LatentDelay, etc.) the probe id equals the node's own id (one-to-one).
        // For EventEntryNode (which produces no IR statements and therefore no own probe),
        // fall back to the containing block's SourceNodeId so the breakpoint target resolves
        // to an actually-emitted probe.  Non-exec / pure-data nodes are absent.
        var bpTargets = new Dictionary<Guid, Guid>();
        foreach (var kv in _execNodeToBlockId)
        {
            var nodeId  = kv.Key;
            var blockId = kv.Value;
            var block   = _blockBuilders[blockId];

            // Check whether this node has its own ExecEntryNodeId-tagged statement in the block.
            // If not (e.g. EventEntryNode, which emits no code), fall back to the block's
            // SourceNodeId so the target points to an actually-emitted probe.
            bool hasOwnStatement = block.Statements.Any(s => s.Debug?.ExecEntryNodeId == nodeId);
            Guid probeId = hasOwnStatement
                ? nodeId
                : (block.SourceNodeId ?? nodeId);

            bpTargets[nodeId] = probeId;
        }

        return new IrGraph
        {
            Id      = _graph.Id,
            Name    = _graph.Name,
            Kind    = MapGraphKind(_graph.Kind),
            Inputs  = irInputs,
            Outputs = irOutputs,
            Blocks  = _blockBuilders.Select(b => b.Build()).ToList().AsReadOnly(),
            Entry   = new IrBlockId(0),
            BreakpointTargets = bpTargets,
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
                    _execNodeToBlockId[node.Id] = blockId;
                    var esucc = GetSingleExecSuccessor(node);
                    if (esucc is null)
                    {
                        ReportDroppedExecSuccessors(node);
                        SealFallThrough(blockId, bb, DebugOf(node));
                        return;
                    }
                    node = esucc;
                    continue;

                case ReturnNode rn:
                    _execNodeToBlockId[rn.Id] = blockId;
                    // Tag a sentinel statement for the Return node so a NodeEnter probe is emitted
                    // when Return is the first (and only) exec node in the block.
                    // When Return is preceded by other exec nodes (their statements already occupy
                    // the block), do NOT add an extra anchor — the preceding nodes' probes cover
                    // the block, and a Return anchor would insert a spurious extra recorded node
                    // that shifts sub-tick recorder indices and breaks inspector assertions.
                    {
                        int retStmtsBefore = bb.Statements.Count;
                        // BuildReturnTerminator may call ResolveDataPin (adds data-dep stmts for output pin).
                        bb.Terminator = BuildReturnTerminator(rn, bb);
                        if (bb.Statements.Count > retStmtsBefore)
                        {
                            // Tag the first data-dep statement added for Return's output pin.
                            TagFirstNewStatement(bb.Statements, retStmtsBefore, rn.Id);
                        }
                        else if (retStmtsBefore == 0)
                        {
                            // Block is EMPTY (Return is sole exec node): emit a tagged nop so a
                            // breakpoint on this Return can fire (e.g. Delay resume block → Return).
                            bb.Statements.Add(new IrStatement
                            {
                                Operation = new IrOp_Const("0", Stage5_Schedule.Int32Type),
                                Debug = new IrDebugAnnotation
                                {
                                    GraphId       = _graph.Id,
                                    NodeId        = rn.Id,
                                    Synthesized   = "return-probe-anchor",
                                    ExecEntryNodeId = rn.Id,
                                },
                            });
                        }
                        // else: block has statements from preceding exec nodes; Return is the
                        // terminal — no anchor needed (Return stays in bpTargets but its probe is
                        // only emitted when it owns an empty block, which is the normal latent case).
                    }
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

                case WhenNode wn:
                    ScheduleWhenNode(wn, bb);
                    return;

                // AN8: non-channel action node (ActionFqn set) — inline-latent invocation.
                case ChannelCommandNode { ActionFqn: { } fqn } cc when !string.IsNullOrEmpty(fqn):
                    ScheduleInlineActionNode(cc, bb);
                    return;

                case SequenceNode seq:
                    ScheduleSequenceNode(seq, bb);
                    return;

                default:
                    // Regular node: emit statements, then follow exec chain.
                    _execNodeToBlockId[node.Id] = blockId;
                    bb.SourceNodeId ??= node.Id;
                    {
                        int stmtsBefore = bb.Statements.Count;
                        EmitNodeStatements(node, bb.Statements);
                        // Tag the first statement emitted for this exec node (including any data deps
                        // it resolves first) so DebugProbeInsertion inserts a per-node NodeEnter probe.
                        // If EmitNodeStatements produced no statements (e.g. SetVariable with no value
                        // pin), emit a tagged nop so the per-node probe has an anchor to precede.
                        if (bb.Statements.Count > stmtsBefore)
                        {
                            TagFirstNewStatement(bb.Statements, stmtsBefore, node.Id);
                        }
                        else
                        {
                            bb.Statements.Add(new IrStatement
                            {
                                Operation = new IrOp_Const("0", Stage5_Schedule.Int32Type),
                                Debug = new IrDebugAnnotation
                                {
                                    GraphId         = _graph.Id,
                                    NodeId          = node.Id,
                                    Synthesized     = "exec-probe-anchor",
                                    ExecEntryNodeId = node.Id,
                                },
                            });
                        }
                    }
                    var succ = GetSingleExecSuccessor(node);
                    if (succ is null)
                    {
                        ReportDroppedExecSuccessors(node);
                        SealFallThrough(blockId, bb, DebugOf(node));
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
        // The pre-suspend block now represents this latent node.
        bb.SourceNodeId = node.Id;
        _execNodeToBlockId[node.Id] = bb.Id.Value;

        // Append the latent marker as the last statement in the pre-suspend block.
        // Tag it as this latent node's exec-entry statement so DebugProbeInsertion
        // inserts a NodeEnter probe immediately before it.
        bb.Statements.Add(new IrStatement
        {
            ResultValue = null,
            Operation   = latentOp,
            Debug       = DebugOf(node) with { ExecEntryNodeId = node.Id },
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

        // Propagate fall-through target from current block to resume block
        // (so nested latent inside a Sequence branch continues to the next branch).
        if (_fallThroughTarget.TryGetValue(bb.Id.Value, out var latentFt))
            _fallThroughTarget[resumeBlockId.Value] = latentFt;

        // Enqueue continuation in resume block.
        var continuation = GetSingleExecSuccessor(node);
        if (continuation is not null)
            _bfsQueue.Enqueue((resumeBlockId.Value, continuation));
        else
        {
            // No successor -- resume block is empty with fall-through.
            SealFallThrough(resumeBlockId.Value, _blockBuilders[resumeBlockId.Value]);
            _scheduledBlocks.Add(resumeBlockId.Value);
        }
    }

    // -----------------------------------------------------------------------
    // AN8: inline-latent action node handling
    // -----------------------------------------------------------------------

    /// <summary>
    /// Schedules a non-channel behavior-action node (ChannelCommandNode with ActionFqn set).
    /// Emits an <see cref="IrOp_InlineActionCall"/> statement then delegates to
    /// <see cref="ScheduleLatentNode"/> to produce the suspend/resume block split.
    /// On each tick the action is re-invoked; Success/Failure routes exec-out; Running suspends.
    /// Stage 6 (WaitLowering_AiPrimitive) converts the IrTerm_Suspend into a phase-byte
    /// re-dispatch that re-calls the action every tick until non-Running.
    /// </summary>
    private void ScheduleInlineActionNode(ChannelCommandNode cc, BlockBuilder bb)
    {
        var actionFqn      = cc.ActionFqn!;
        var paramsTypeFqn  = cc.ActionParamsTypeFqn ?? "";

        // Collect data-IN pin values (field name → resolved SSA value).
        var paramFields = cc.Pins
            .Where(p => !p.IsExec && p.Direction == "In")
            .Select(p =>
            {
                var val = ResolveDataPin(cc.Id, p.Id, bb.Statements);
                return (p.Name, val);
            })
            .ToList();

        // Determine if this is an AiPrimitive (BlueprintCall) path.
        // Convention: AiPrimitive generated classes follow the "{Name}_{Id:X8}_Bp.Call" pattern,
        // so their ActionFqn always ends with "_Bp.Call".
        // [SharedAiAction] methods are direct static method FQNs and do NOT end with "_Bp.Call".
        bool isAiPrimitive = actionFqn.EndsWith("_Bp.Call", StringComparison.Ordinal);

        var actionCallOp = new IrOp_InlineActionCall(
            actionFqn,
            paramsTypeFqn,
            paramFields,
            isAiPrimitive);

        // ScheduleLatentNode will: emit the op as a statement, split the block,
        // and enqueue the exec-successor in the resume block.
        ScheduleLatentNode(cc, bb, actionCallOp);
    }

    // -----------------------------------------------------------------------
    // Branch node handling
    // -----------------------------------------------------------------------

    private void ScheduleBranchNode(BranchNode bn, BlockBuilder bb)
    {
        _execNodeToBlockId[bn.Id] = bb.Id.Value;
        // Resolve condition data input (first non-exec data-in pin).
        var condPin = bn.Pins.FirstOrDefault(p => !p.IsExec && p.Direction == "In");
        IrValue condValue;
        int branchStmtsBefore = bb.Statements.Count;
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
        // Tag the first statement added for this BranchNode so a NodeEnter probe is inserted.
        TagFirstNewStatement(bb.Statements, branchStmtsBefore, bn.Id);

        var idShort = bn.Id.ToString("N").Substring(0, 8);
        var trueBlock  = AllocBlock($"branch_{idShort}_true");
        var falseBlock = AllocBlock($"branch_{idShort}_false");

        // Propagate fall-through target to both branch exit blocks
        // (so a Branch inside a Sequence branch continues to the next branch).
        if (_fallThroughTarget.TryGetValue(bb.Id.Value, out var branchFt))
        {
            _fallThroughTarget[trueBlock.Value] = branchFt;
            _fallThroughTarget[falseBlock.Value] = branchFt;
        }

        bb.Terminator = new IrTerm_Branch(condValue, trueBlock, falseBlock)
        {
            Debug = DebugOf(bn),
        };

        var (trueSucc, falseSucc) = GetBranchSuccessors(bn);
        if (trueSucc  is not null) _bfsQueue.Enqueue((trueBlock.Value,  trueSucc));
        else SealFallThrough(trueBlock.Value, _blockBuilders[trueBlock.Value]);
        if (falseSucc is not null) _bfsQueue.Enqueue((falseBlock.Value, falseSucc));
        else SealFallThrough(falseBlock.Value, _blockBuilders[falseBlock.Value]);
    }

    // -----------------------------------------------------------------------
    // Sequence node handling (SEQ1)
    // -----------------------------------------------------------------------

    private void ScheduleSequenceNode(SequenceNode seq, BlockBuilder bb)
    {
        // This block carries the sequence's dispatch to its children.
        // Use ??= so that a preceding exec node's SourceNodeId (e.g. SetVarB before S1)
        // is NOT clobbered.  The sequence becomes block source only when it is the first
        // exec node in the block (bb.SourceNodeId was null coming in).
        bb.SourceNodeId ??= seq.Id;
        _execNodeToBlockId[seq.Id] = bb.Id.Value;

        // 1. Resolve ordered list of connected Then successors.
        //    Order by numeric suffix of pin Name (Then0, Then1, ...);
        //    fall back to Pins-list order for pins without a parseable suffix.
        var thenPins = seq.Pins
            .Where(p => p.IsExec && p.Direction == "Out"
                        && p.Name.StartsWith("Then", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p =>
            {
                var suffix = p.Name.Length > 4 ? p.Name.Substring(4) : "";
                return int.TryParse(suffix, out var n) ? n : int.MaxValue;
            })
            .ToList();

        var successors = new List<(Pin Pin, Node Node)>();
        foreach (var pin in thenPins)
        {
            var link = _graph.Links.FirstOrDefault(
                l => l.FromNodeId == seq.Id && l.FromPinId == pin.Id);
            if (link is not null && _nodeById.TryGetValue(link.ToNodeId, out var target))
                successors.Add((pin, target));
        }

        // 2. If zero connected branches, seal as fall-through and return.
        if (successors.Count == 0)
        {
            SealFallThrough(bb.Id.Value, bb, DebugOf(seq));
            return;
        }

        // 3. Allocate one block per connected branch.
        var idShort = seq.Id.ToString("N").Substring(0, 8);
        var branchBlocks = new List<IrBlockId>(successors.Count);
        for (int i = 0; i < successors.Count; i++)
            branchBlocks.Add(AllocBlock($"seq_{idShort}_then{i}"));

        // 3b. Emit a tagged seq-probe-anchor at the CURRENT block position so that
        //     DebugProbeInsertion inserts the sequence's NodeEnter probe in execution
        //     order (after any preceding exec-node statements, before the Goto).
        //     When the sequence is the block's first exec node (SourceNodeId == seq.Id
        //     after ??= above), this anchor sits at position 0 and takes the place of the
        //     old block-header probe — one probe, same identity, just emitted via the
        //     ExecEntryNodeId path (coveredByExecEntryId = true → needsHeaderProbe = false).
        bb.Statements.Add(new IrStatement
        {
            Operation = new IrOp_Const("0", Stage5_Schedule.Int32Type),
            Debug = new IrDebugAnnotation
            {
                GraphId         = _graph.Id,
                NodeId          = seq.Id,
                Synthesized     = "seq-probe-anchor",
                ExecEntryNodeId = seq.Id,
            },
        });

        // 4. Set current block's terminator to Goto first branch block.
        bb.Terminator = new IrTerm_Goto(branchBlocks[0]) { Debug = DebugOf(seq) };

        // 5. Chain branches: branch i falls through to branch i+1;
        //    the last branch falls through (no target).
        for (int i = 0; i < successors.Count; i++)
        {
            if (i < successors.Count - 1)
                _fallThroughTarget[branchBlocks[i].Value] = branchBlocks[i + 1];
            _bfsQueue.Enqueue((branchBlocks[i].Value, successors[i].Node));
        }

        // 6. Propagate outer fall-through target to the last branch block
        //    (handles nested Sequence inside Sequence).
        if (_fallThroughTarget.TryGetValue(bb.Id.Value, out var outerFt))
            _fallThroughTarget[branchBlocks[branchBlocks.Count - 1].Value] = outerFt;
    }

    // -----------------------------------------------------------------------
    // Fall-through sealing helper
    // -----------------------------------------------------------------------

    /// <summary>
    /// Sets the terminator on <paramref name="bb"/>: if a fall-through redirect
    /// is registered for <paramref name="blockId"/>, emits <see cref="IrTerm_Goto"/>;
    /// otherwise synthesizes the dispatch-appropriate implicit return
    /// (<see cref="IrTerm_ReturnStatus"/>(Success) for AiPrimitive/Library,
    /// void <see cref="IrTerm_Return"/> for Instance).
    /// Centralizes the decision so that branch chaining (Sequence) is honoured
    /// wherever a block's exec chain naturally ends.
    /// </summary>
    private void SealFallThrough(int blockId, BlockBuilder bb, IrDebugAnnotation? debug = null)
    {
        if (_fallThroughTarget.TryGetValue(blockId, out var t))
        {
            bb.Terminator = new IrTerm_Goto(t);
            return;
        }

        // Genuine end-of-chain — synthesize the implicit return per dispatch kind
        // (mirrors BuildReturnTerminator's defaults without an explicit ReturnNode).
        if (_typed.Asset.Dispatch == AssetDispatchKind.AiPrimitive
            || _typed.Asset.Dispatch == AssetDispatchKind.Library)
        {
            var term = new IrTerm_ReturnStatus(NodeStatus.Success);
            if (debug is not null) term = term with { Debug = debug };
            bb.Terminator = term;
        }
        else
        {
            var term = new IrTerm_Return(null /* void */);
            if (debug is not null) term = term with { Debug = debug };
            bb.Terminator = term;
        }
    }

    // -----------------------------------------------------------------------
    // WhenNode handling
    // -----------------------------------------------------------------------

    private void ScheduleWhenNode(WhenNode wn, BlockBuilder bb)
    {
        _execNodeToBlockId[wn.Id] = bb.Id.Value;
        var idShort = wn.Id.ToString("N").Substring(0, 8);
        var synthFieldName = $"_when_{idShort}_prev";
        var debug = DebugOf(wn);

        bool hasFired = (wn.Edges & WhenEdge.RisingEdge) != 0;
        bool hasEnded = (wn.Edges & WhenEdge.FallingEdge) != 0;

        // Allocate blocks
        IrBlockId? onFiredBlock = hasFired ? AllocBlock($"when_{idShort}_fired") : (IrBlockId?)null;
        // TODO M3: FallingEdge — block structure is allocated but condition logic deferred.
        IrBlockId? onEndedBlock = hasEnded ? AllocBlock($"when_{idShort}_ended") : (IrBlockId?)null;
        var outBlock = AllocBlock($"when_{idShort}_out");

        // Propagate fall-through target to WhenNode exit blocks
        // (so a WhenNode inside a Sequence branch continues to the next branch).
        if (_fallThroughTarget.TryGetValue(bb.Id.Value, out var whenFt))
        {
            if (onFiredBlock.HasValue) _fallThroughTarget[onFiredBlock.Value.Value] = whenFt;
            if (onEndedBlock.HasValue) _fallThroughTarget[onEndedBlock.Value.Value] = whenFt;
            _fallThroughTarget[outBlock.Value] = whenFt;
        }

        // Allocate result value (bool "fired/changed/matched")
        var boolType = new IrTypeRef { FullName = "System.Boolean", IsUnmanaged = true, SizeBytes = 1 };
        var condValue = AllocValue(boolType);

        // Emit the mode-specific check op
        switch (wn.Mode)
        {
            case WhenMode.ValueChanged:
            {
                var vc = wn.ValueChanged;
                if (vc is null) break; // BP2002 already reported in Stage 2

                string componentFqn = vc.ComponentTypeId;
                string propertyPath  = vc.PropertyPath;
                float epsilon = (float)vc.Epsilon;
                int sourceKind = (int)vc.Source; // 0=SelfComponent, 1=Peer, 2=WorkingState

                // Attempt to resolve the field C# type via reflection for vector-aware emission.
                // Falls back to "var" if the type cannot be resolved at compile time.
                string fieldCSharpType = TryResolveFieldCSharpType(componentFqn, propertyPath);

                // Only emit WhenValueChangedCheck when RisingEdge is active.
                // For FallingEdge-only (M2 deferred), emit a false constant so the
                // branch structure is preserved without synthesizing a state field.
                if (!hasFired)
                {
                    bb.Statements.Add(new IrStatement
                    {
                        ResultValue = condValue,
                        Operation   = new IrOp_Const("false", boolType),
                        Debug       = debug,
                    });
                    break;
                }

                // Determine the onFired block for StorePrev post-action
                IrBlockId effectiveFiredBlock = onFiredBlock ?? outBlock;

                bb.Statements.Add(new IrStatement
                {
                    ResultValue = condValue,
                    Operation   = new IrOp_WhenValueChangedCheck(
                        ComponentFqn:    componentFqn,
                        PropertyPath:    propertyPath,
                        Epsilon:         epsilon,
                        SynthFieldName:  synthFieldName,
                        FieldCSharpType: fieldCSharpType,
                        OnFiredBlock:    effectiveFiredBlock,
                        SourceKind:      sourceKind),
                    Debug = debug,
                });

                // Register StorePrev to be appended to the fired block after BFS.
                if (hasFired)
                {
                    _whenPostActions.Add((effectiveFiredBlock.Value, new IrStatement
                    {
                        Operation = new IrOp_WhenStorePrev(
                            ComponentFqn:   componentFqn,
                            PropertyPath:   propertyPath,
                            SynthFieldName: synthFieldName),
                        Debug = new IrDebugAnnotation { GraphId = _graph.Id, Synthesized = "when-store-prev" },
                    }));
                }
                break;
            }

            case WhenMode.EventFired:
            {
                var ef = wn.EventFired;
                if (ef is null) break;

                bool filterSelf = ef.TargetFilter == EventTargetFilter.Self;
                string? payloadField = ef.PayloadCheck?.PropertyPath;
                string? payloadOp    = ef.PayloadCheck is not null
                    ? ComparisonOpToCSharp(ef.PayloadCheck.Operator)
                    : null;
                string? payloadVal   = ef.PayloadCheck?.TargetValueText;

                bb.Statements.Add(new IrStatement
                {
                    ResultValue = condValue,
                    Operation   = new IrOp_WhenEventFiredCheck(
                        EventFqn:              ef.EventTypeId,
                        FilterSelf:            filterSelf,
                        TargetFieldName:       ef.TargetFieldName ?? "Target",
                        PayloadFieldPath:      payloadField,
                        PayloadOperatorCSharp: payloadOp,
                        PayloadValueLiteral:   payloadVal),
                    Debug = debug,
                });
                // No StorePrev for EventFired — no synthesized state field.
                break;
            }

            case WhenMode.ConditionMet:
            {
                var cm = wn.ConditionMet;
                if (cm is null) break; // BP2002 already reported

                // Serialize predicate DTO to JSON (embedded as const string in generated code).
                string predicateJson = cm.Condition is not null
                    ? System.Text.Json.JsonSerializer.Serialize(cm.Condition)
                    : "null";

                bb.Statements.Add(new IrStatement
                {
                    ResultValue = null, // No result value -- branching is embedded in the op emit
                    Operation   = new IrOp_WhenConditionMetCheck(
                        PredicateDtoJson: predicateJson,
                        SynthFieldName:   synthFieldName,
                        OnFiredBlock:     hasFired  ? onFiredBlock  : null,
                        OnEndedBlock:     hasEnded  ? onEndedBlock  : null),
                    Debug = debug,
                });

                // ConditionMet uses Goto terminator (not Branch): prev-update and gotos
                // are emitted inline by StatementEmitter.
                bb.Terminator = new IrTerm_Goto(outBlock) { Debug = debug };

                // Skip the standard IrTerm_Branch code below.
                goto scheduleSuccessors;
            }

            case WhenMode.EqsResult:
            {
                var er = wn.EqsResult;
                if (er is null) break; // BP2002 already reported

                string trigger = er.Trigger.ToString(); // "FirstReady", "TopChanged", "ScoreCrossed", "BecomesStale"

                // Determine struct shape from trigger
                string structTypeName = $"_WhenEqs{trigger}_{idShort}_PrevState";
                int structSizeBytes = trigger switch
                {
                    "TopChanged"   => 16, // uint LastEvaluatedEpoch + long PrevTopId + float PrevTopScore
                    "FirstReady"   => 4,  // uint LastEvaluatedEpoch
                    "ScoreCrossed" => 8,  // uint LastEvaluatedEpoch + float PrevTopScore
                    "BecomesStale" => 4,  // float PrevStaleCheckTime
                    _              => 8,
                };

                string? scoreThreshold = trigger == "ScoreCrossed"
                    ? $"{er.ScoreThreshold.ToString("G", System.Globalization.CultureInfo.InvariantCulture)}f"
                    : null;
                string? maxAge = trigger == "BecomesStale"
                    ? $"{er.MaxAgeSeconds.ToString("G", System.Globalization.CultureInfo.InvariantCulture)}f"
                    : null;

                bb.Statements.Add(new IrStatement
                {
                    ResultValue = null,
                    Operation   = new IrOp_WhenEqsResultCheck(
                        SensorVariableName:   er.SensorVariableName,
                        Trigger:              trigger,
                        SynthFieldName:       synthFieldName,
                        SynthStructTypeName:  structTypeName,
                        SynthStructSizeBytes: structSizeBytes,
                        ScoreThresholdLiteral: scoreThreshold,
                        MaxAgeLiteral:        maxAge,
                        OnFiredBlock:         hasFired ? onFiredBlock : null,
                        OnEndedBlock:         hasEnded ? onEndedBlock : null),
                    Debug = debug,
                });

                bb.Terminator = new IrTerm_Goto(outBlock) { Debug = debug };
                goto scheduleSuccessors;
            }

            default:
                // Unknown modes: emit noop false const.
                bb.Statements.Add(new IrStatement
                {
                    ResultValue = condValue,
                    Operation   = new IrOp_Const("false", boolType),
                    Debug       = debug,
                });
                break;
        }

        // The primary branch: condition true -> onFired (if any), else -> out
        IrBlockId trueTarget  = onFiredBlock ?? outBlock;
        IrBlockId falseTarget = outBlock;

        bb.Terminator = new IrTerm_Branch(condValue, trueTarget, falseTarget) { Debug = debug };

        scheduleSuccessors:
        // Schedule exec successors
        Node? firedSucc  = GetWhenExecSuccessor(wn, "OnFired");
        Node? endedSucc  = GetWhenExecSuccessor(wn, "OnEnded");
        Node? outSucc    = GetWhenExecSuccessor(wn, "Out");

        if (onFiredBlock.HasValue && firedSucc is not null)
            _bfsQueue.Enqueue((onFiredBlock.Value.Value, firedSucc));

        if (onEndedBlock.HasValue && endedSucc is not null)
            _bfsQueue.Enqueue((onEndedBlock.Value.Value, endedSucc));

        if (outSucc is not null)
            _bfsQueue.Enqueue((outBlock.Value, outSucc));
        // else outBlock stays empty -> auto-fallthrough from BlockBuilder.Build()
    }

    private static string ComparisonOpToCSharp(ComparisonOperator op) => op switch
    {
        ComparisonOperator.Equal              => "==",
        ComparisonOperator.NotEqual           => "!=",
        ComparisonOperator.LessThan           => "<",
        ComparisonOperator.LessThanOrEqual    => "<=",
        ComparisonOperator.GreaterThan        => ">",
        ComparisonOperator.GreaterThanOrEqual => ">=",
        _                                     => "==",
    };

    private Node? GetWhenExecSuccessor(WhenNode wn, string pinName)
    {
        var pin = wn.Pins.FirstOrDefault(
            p => p.IsExec && p.Direction == "Out" &&
                 string.Equals(p.Name, pinName, StringComparison.OrdinalIgnoreCase));
        if (pin is null) return null;
        var link = _graph.Links.FirstOrDefault(l => l.FromNodeId == wn.Id && l.FromPinId == pin.Id);
        return link is not null && _nodeById.TryGetValue(link.ToNodeId, out var t) ? t : null;
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

            case FunctionCallNode fc when !fc.IsPure && !string.IsNullOrEmpty(fc.TargetGraphId):
            {
                // Impure in-blueprint function-graph call (BATCH-03A).
                // Discriminator wins over the CLR library case below.
                if (!Guid.TryParse(fc.TargetGraphId, out var targetGraphGuid))
                {
                    _ctx.Diagnostics.Add(Diagnostic.Warning(DiagnosticCodes.BP4004,
                        $"FunctionCallNode TargetGraphId '{fc.TargetGraphId}' is not a valid GUID -- no IR emitted.",
                        _ctx.AssetId, _graph.Id, node.Id));
                    break;
                }
                var targetGraph = _typed.Asset.Graphs.FirstOrDefault(g => g.Id == targetGraphGuid);
                if (targetGraph is null || targetGraph.Kind != GraphKind.Function)
                {
                    _ctx.Diagnostics.Add(Diagnostic.Warning(DiagnosticCodes.BP4004,
                        $"FunctionCallNode references unknown or non-Function graph '{fc.TargetGraphId}' -- no IR emitted.",
                        _ctx.AssetId, _graph.Id, node.Id));
                    break;
                }
                var gcArgs   = ResolveAllDataInputs(node, stmts);
                var gcOutPin = node.Pins.FirstOrDefault(p => !p.IsExec && p.Direction == "Out");
                IrTypeRef gcRetType;
                if (gcOutPin is not null && _typed.PinTypes.TryGetValue(gcOutPin.Id, out var gcPinType))
                    gcRetType = gcPinType;
                else if (targetGraph.Outputs.Count > 0 && _ctx.TypeRegistry.TryResolve(targetGraph.Outputs[0].Type, out var resolvedOut))
                    gcRetType = resolvedOut;
                else
                    gcRetType = Stage5_Schedule.UnknownType;
                var gcResult = AllocValue(gcRetType);
                stmts.Add(new IrStatement
                {
                    ResultValue = gcResult,
                    Operation   = new IrOp_GraphCall(targetGraphGuid, gcArgs, gcRetType),
                    Debug       = DebugOf(node),
                });
                if (gcOutPin is not null)
                    _pinValueCache[gcOutPin.Id] = gcResult;
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

                // Look up the catalog to get a fully-qualified channel type, ActionId (numeric
                // ushort literal) and the params struct FQN.  cc.ActionId / cc.ChannelType are
                // short names (e.g. "MoveTo", "LocomotionChannel") from the authored JSON.
                // Emitting the raw short names would produce invalid C# ("= MoveTo;", "global::LocomotionChannel").
                var catalogEntry = _ctx.ChannelCommands.GetEntries()
                    .FirstOrDefault(e => string.Equals(e.Name, cc.ActionId,
                        StringComparison.OrdinalIgnoreCase));
                string channelTypeFqn = ResolveChannelTypeFqn(cc.ChannelType);
                // ActionIdConstantName must be a valid C# rvalue (ushort literal) while still
                // being recognizable by IR-level tests that check for the action name.
                // Embed the human-readable action name as a C# block comment so it survives
                // both runtime compile and IR inspection (e.g. Contains("MoveTo")).
                string actionIdLiteral = catalogEntry != null
                    ? $"(ushort){catalogEntry.ActionId} /* {cc.ActionId} */"
                    : $"/* unknown action '{cc.ActionId}' */ (ushort)0";
                string paramsStructFqn = catalogEntry?.ParamsTypeFqn ?? "";

                stmts.Add(new IrStatement
                {
                    Operation = new IrOp_ChannelCommand(
                        channelTypeFqn, actionIdLiteral, paramsStructFqn, paramFields),
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

            case SpawnEqsSensorNode ssn:
            {
                // Compute the baked InstanceId from the node's Guid hash.
                int bakedInstanceId = (int)BlueprintIdHash.Compute(ssn.Id);

                // Compute the template's BlueprintId from its AssetId.
                // BlueprintIdHash.Compute returns int; cast to uint for the EqsSensor.BlueprintId field.
                uint templateBpId = (uint)BlueprintIdHash.Compute(ssn.TemplateAssetId);
                string templateBpIdLiteral = $"0x{templateBpId:X8}u";

                // Resolve each parameter pin (SearchRadius, FactionFilter, ThreatThreshold, PublishPolicy, Priority).
                // For unconnected pins, use type-specific defaults via null return.
                IrValue? ResolveParamPin(string pinName)
                {
                    var pin = ssn.Pins.FirstOrDefault(p => !p.IsExec && p.Direction == "In"
                                  && string.Equals(p.Name, pinName, StringComparison.OrdinalIgnoreCase));
                    if (pin is null) return null;
                    var link = _graph.Links.FirstOrDefault(l => l.ToNodeId == ssn.Id && l.ToPinId == pin.Id);
                    if (link is null) return null;
                    return ResolveNodeOutput(link.FromNodeId, link.FromPinId, stmts);
                }

                var searchRadius    = ResolveParamPin("SearchRadius");
                var factionFilter   = ResolveParamPin("FactionFilter");
                var threatThreshold = ResolveParamPin("ThreatThreshold");
                var publishPolicy   = ResolveParamPin("PublishPolicy");
                var priority        = ResolveParamPin("Priority");

                // Emit the spawn op; result is the EqsSensorHandle
                var handleType = new IrTypeRef { FullName = "FDP.Eqs.EqsSensorHandle", IsUnmanaged = true, SizeBytes = 8 };
                var handleResult = AllocValue(handleType);
                stmts.Add(new IrStatement
                {
                    ResultValue = handleResult,
                    Operation   = new IrOp_SpawnEqsSensor(
                        TemplateBlueprintIdLiteral: templateBpIdLiteral,
                        BakedInstanceId:            bakedInstanceId,
                        SearchRadiusValue:          searchRadius,
                        FactionFilterValue:         factionFilter,
                        ThreatThresholdValue:       threatThreshold,
                        PublishPolicyValue:         publishPolicy,
                        PriorityValue:              priority),
                    Debug = DebugOf(ssn),
                });

                // Cache the Handle output pin value
                var handleOutPin = ssn.Pins.FirstOrDefault(p => !p.IsExec && p.Direction == "Out"
                                        && string.Equals(p.Name, "Handle", StringComparison.OrdinalIgnoreCase));
                if (handleOutPin is not null)
                    _pinValueCache[handleOutPin.Id] = handleResult;

                break;
            }

            case ScoreDecisionNode sdn:
            {
                string id8 = sdn.Id.ToString("N").Substring(0, 8);
                // Bake the decision ID at compile time using FNV-1a-32.
                int decisionId = ComputeDecisionId(sdn.AssetId);
                string decisionIdLiteral = decisionId.ToString();

                var byteType = new IrTypeRef { FullName = "System.Byte", IsUnmanaged = true, SizeBytes = 1 };
                var optionResult = AllocValue(byteType);
                stmts.Add(new IrStatement
                {
                    ResultValue = optionResult,
                    Operation   = new IrOp_ScoreDecision(decisionIdLiteral, id8),
                    Debug       = DebugOf(sdn),
                });

                var outPin = sdn.Pins.FirstOrDefault(p => !p.IsExec && p.Direction == "Out"
                                 && string.Equals(p.Name, "WinningOptionId", StringComparison.OrdinalIgnoreCase));
                if (outPin is not null)
                    _pinValueCache[outPin.Id] = optionResult;
                break;
            }

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

    private IrOperation BuildWaitForChannelOp(WaitForChannelNode wfc)
    {
        // Qualify wfc.ChannelType (stored as short name, e.g. "LocomotionChannel") to its FQN
        // using the catalog, matching by either action name or channel type short/full name.
        string channelFqn = ResolveChannelTypeFqn(wfc.ChannelType);
        return new IrOp_WaitForChannel(channelFqn, Array.Empty<IrField>());
    }

    private static IrOperation BuildWaitForEventOp(WaitForEventNode wfe)
        => new IrOp_WaitForEvent(wfe.EventTypeId, wfe.FilterByField, null,
                                  Array.Empty<IrField>());

    // -----------------------------------------------------------------------
    // Channel type FQN resolution helper
    // -----------------------------------------------------------------------

    /// <summary>
    /// Resolves a channel type short name (e.g. "LocomotionChannel") to its FQN using the
    /// channel command catalog.  Falls back to the input value unchanged (assumed already FQN).
    /// </summary>
    private string ResolveChannelTypeFqn(string channelTypeShortOrFqn)
    {
        if (string.IsNullOrEmpty(channelTypeShortOrFqn))
            return channelTypeShortOrFqn;

        foreach (var entry in _ctx.ChannelCommands.GetEntries())
        {
            // Already an FQN match?
            if (string.Equals(entry.ChannelTypeFqn, channelTypeShortOrFqn, StringComparison.OrdinalIgnoreCase))
                return entry.ChannelTypeFqn;

            // Short-name match (last segment of the FQN).
            var dot = entry.ChannelTypeFqn.LastIndexOf('.');
            var shortName = dot >= 0
                ? entry.ChannelTypeFqn.Substring(dot + 1)
                : entry.ChannelTypeFqn;
            if (string.Equals(shortName, channelTypeShortOrFqn, StringComparison.OrdinalIgnoreCase))
                return entry.ChannelTypeFqn;
        }

        // Not found in catalog — return as-is (may already be an FQN or an unknown type).
        return channelTypeShortOrFqn;
    }

    // -----------------------------------------------------------------------
    // Return terminator builder
    // -----------------------------------------------------------------------

    private IrTerminator BuildReturnTerminator(ReturnNode rn, BlockBuilder currentBlock)
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
            retVal = ResolveDataPin(rn.Id, outPin.Id, currentBlock.Statements);

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

            case FunctionCallNode fc when fc.IsPure && !string.IsNullOrEmpty(fc.TargetGraphId):
            {
                // Pure in-blueprint function-graph call (BATCH-03A).
                if (!Guid.TryParse(fc.TargetGraphId, out var pureGcGuid))
                {
                    result = AllocValue(pinType);
                    stmts.Add(new IrStatement
                    {
                        ResultValue = result,
                        Operation   = new IrOp_Const("default", pinType),
                        Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = fc.Id },
                    });
                    break;
                }
                var pureTargetGraph = _typed.Asset.Graphs.FirstOrDefault(g => g.Id == pureGcGuid);
                if (pureTargetGraph is null || pureTargetGraph.Kind != GraphKind.Function)
                {
                    result = AllocValue(pinType);
                    stmts.Add(new IrStatement
                    {
                        ResultValue = result,
                        Operation   = new IrOp_Const("default", pinType),
                        Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = fc.Id },
                    });
                    break;
                }
                var pureGcArgs = ResolveAllDataInputs(sourceNode, stmts);
                IrTypeRef pureGcRetType;
                if (pureTargetGraph.Outputs.Count > 0 && _ctx.TypeRegistry.TryResolve(pureTargetGraph.Outputs[0].Type, out var pureResolved))
                    pureGcRetType = pureResolved;
                else
                    pureGcRetType = pinType;
                result = AllocValue(pureGcRetType);
                stmts.Add(new IrStatement
                {
                    ResultValue = result,
                    Operation   = new IrOp_GraphCall(pureGcGuid, pureGcArgs, pureGcRetType),
                    Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = fc.Id, PinId = sourcePinId },
                });
                break;
            }

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

            case ReadEqsResultNode rer:
            {
                string id8 = rer.Id.ToString("N").Substring(0, 8);
                string structTypeName = $"_EqsResultRead_{id8}";

                var resultStructType = new IrTypeRef
                {
                    FullName    = structTypeName,
                    IsUnmanaged = true,
                    SizeBytes   = 32, // bool(1) + int(4) + Entity(8) + Vector2(8) + float(4) + pad ~= 32
                };

                // Resolve the ResultIndex input pin (default 0 if unconnected)
                var indexPin = rer.Pins.FirstOrDefault(p => !p.IsExec && p.Direction == "In"
                                                             && string.Equals(p.Name, "ResultIndex", StringComparison.OrdinalIgnoreCase));
                IrValue indexValue;
                if (indexPin is not null)
                {
                    var link = _graph.Links.FirstOrDefault(l => l.ToNodeId == rer.Id && l.ToPinId == indexPin.Id);
                    if (link is not null)
                        indexValue = ResolveNodeOutput(link.FromNodeId, link.FromPinId, stmts);
                    else
                    {
                        indexValue = AllocValue(Stage5_Schedule.Int32Type);
                        stmts.Add(new IrStatement
                        {
                            ResultValue = indexValue,
                            Operation   = new IrOp_Const("0", Stage5_Schedule.Int32Type),
                            Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = rer.Id },
                        });
                    }
                }
                else
                {
                    indexValue = AllocValue(Stage5_Schedule.Int32Type);
                    stmts.Add(new IrStatement
                    {
                        ResultValue = indexValue,
                        Operation   = new IrOp_Const("0", Stage5_Schedule.Int32Type),
                        Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = rer.Id },
                    });
                }

                // Emit the helper invocation
                var helperResult = AllocValue(resultStructType);
                stmts.Add(new IrStatement
                {
                    ResultValue = helperResult,
                    Operation   = new IrOp_ReadEqsResult(rer.SensorVariableName, indexValue, id8, structTypeName),
                    Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = rer.Id },
                });

                // Eagerly emit FieldRead for each output pin and cache all of them
                // so that multiple consumers share one helper invocation.
                foreach (var outPin in rer.Pins.Where(p => !p.IsExec && p.Direction == "Out"))
                {
                    if (_pinValueCache.ContainsKey(outPin.Id)) continue;

                    IrTypeRef fieldType = _typed.PinTypes.TryGetValue(outPin.Id, out var t2) ? t2 : Stage5_Schedule.UnknownType;
                    var fieldResult = AllocValue(fieldType);
                    stmts.Add(new IrStatement
                    {
                        ResultValue = fieldResult,
                        Operation   = new IrOp_FieldRead(helperResult, outPin.Name, fieldType),
                        Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = rer.Id, PinId = outPin.Id },
                    });
                    _pinValueCache[outPin.Id] = fieldResult;
                }

                // Return the value for the specifically requested pin
                result = _pinValueCache.TryGetValue(sourcePinId, out var pinRes) ? pinRes : helperResult;
                break;
            }

            case ReadRankedResultNode rrn:
            {
                string id8 = rrn.Id.ToString("N").Substring(0, 8);
                string structTypeName = $"_RankedResultRead_{id8}";

                var resultStructType = new IrTypeRef
                {
                    FullName    = structTypeName,
                    IsUnmanaged = true,
                    SizeBytes   = 16, // bool(1) + long(8) + float(4) + pad = 16
                };

                string rankLiteral = rrn.Rank.ToString();

                var helperResult2 = AllocValue(resultStructType);
                stmts.Add(new IrStatement
                {
                    ResultValue = helperResult2,
                    Operation   = new IrOp_ReadRankedResult(rankLiteral, id8, structTypeName),
                    Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = rrn.Id },
                });

                foreach (var outPin in rrn.Pins.Where(p => !p.IsExec && p.Direction == "Out"))
                {
                    if (_pinValueCache.ContainsKey(outPin.Id)) continue;
                    IrTypeRef fieldType = _typed.PinTypes.TryGetValue(outPin.Id, out var t2)
                        ? t2 : Stage5_Schedule.UnknownType;
                    var fieldResult = AllocValue(fieldType);
                    stmts.Add(new IrStatement
                    {
                        ResultValue = fieldResult,
                        Operation   = new IrOp_FieldRead(helperResult2, outPin.Name, fieldType),
                        Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = rrn.Id, PinId = outPin.Id },
                    });
                    _pinValueCache[outPin.Id] = fieldResult;
                }

                result = _pinValueCache.TryGetValue(sourcePinId, out var pinRes2) ? pinRes2 : helperResult2;
                break;
            }

            case EventEntryNode entry:
            {
                // Entry node data-out pin → IrOp_ReadInputArg(i) where i is the
                // ordinal index into graph.Inputs matched by pin name (fallback: pin ordinal).
                var dataOutPins = entry.Pins
                    .Where(p => !p.IsExec && p.Direction == "Out")
                    .ToList();
                int ordinal = dataOutPins.FindIndex(p => p.Id == sourcePinId);
                if (ordinal < 0) ordinal = 0;

                // Try name-match against graph.Inputs first.
                int argIndex = ordinal;
                var sourcePin = dataOutPins.Count > ordinal ? dataOutPins[ordinal] : null;
                if (sourcePin is not null && _graph.Inputs.Count > 0)
                {
                    int nameMatch = _graph.Inputs.FindIndex(
                        inp => string.Equals(inp.Name, sourcePin.Name, StringComparison.OrdinalIgnoreCase));
                    if (nameMatch >= 0) argIndex = nameMatch;
                }

                result = AllocValue(pinType);
                stmts.Add(new IrStatement
                {
                    ResultValue = result,
                    Operation   = new IrOp_ReadInputArg(argIndex),
                    Debug       = new IrDebugAnnotation
                    {
                        GraphId = _graph.Id,
                        NodeId  = entry.Id,
                        PinId   = sourcePinId,
                    },
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

    /// <summary>
    /// Emits <see cref="DiagnosticCodes.BP1412"/> (Error) when a node has outgoing exec links
    /// that the scheduler did not follow, causing successors to be silently dropped from
    /// the generated code.  Legitimate chain-ends (no exec-out pins, or pins with no links)
    /// do not trigger a diagnostic.
    /// </summary>
    private void ReportDroppedExecSuccessors(Node node)
    {
        var execOutPinIds = new HashSet<Guid>(
            node.Pins
                .Where(p => p.IsExec && p.Direction == "Out")
                .Select(p => p.Id));

        if (execOutPinIds.Count == 0)
            return; // node has no exec-out pins -- legitimate chain end

        var outgoingExecLinks = _graph.Links
            .Where(l => l.FromNodeId == node.Id && execOutPinIds.Contains(l.FromPinId))
            .ToList();

        if (outgoingExecLinks.Count == 0)
            return; // exec-out pins exist but none are linked -- legitimate chain end

        var nodeTypeName = node.GetType().Name;
        _ctx.Diagnostics.Add(Diagnostic.Error(
            DiagnosticCodes.BP1412,
            $"Exec output of node '{node.Id}' ({nodeTypeName}) has {outgoingExecLinks.Count} outgoing link(s) that the scheduler did not follow; those successors are dropped from the generated code. (A node type with multiple exec-out pins, e.g. Sequence, is not yet schedulable, or a link references an unresolved pin.)",
            _ctx.AssetId, _graph.Id, node.Id));
    }

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

        // VariableId may be in the form "var:<Guid>" — strip the prefix before parsing.
        // Mirrors Stage0_Rehydrate.ResolveVariableTypeId (lines 487-490).
        var idStr = variableId.StartsWith("var:", StringComparison.OrdinalIgnoreCase)
            ? variableId.Substring(4)
            : variableId;

        if (Guid.TryParse(idStr, out var guid))
        {
            for (int i = 0; i < variables.Count;  i++) if (variables[i].Id  == guid) return i;
            for (int i = 0; i < workState.Count;  i++) if (workState[i].Id  == guid) return i;
            for (int i = 0; i < parameters.Count; i++) if (parameters[i].Id == guid) return i;
        }
        // Name fallback
        for (int i = 0; i < variables.Count;  i++) if (variables[i].Name  == idStr) return i;
        for (int i = 0; i < workState.Count;  i++) if (workState[i].Name  == idStr) return i;
        for (int i = 0; i < parameters.Count; i++) if (parameters[i].Name == idStr) return i;
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
    // Graph-level Inputs/Outputs propagation (BATCH-03A)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Converts a list of ParameterDecl (from Graph.Inputs or Graph.Outputs) to IrFields,
    /// resolving each type via the context TypeRegistry. Falls back to UnknownType on miss.
    /// </summary>
    private IReadOnlyList<IrField> BuildIrFieldsFromGraphParams(IEnumerable<ParameterDecl> decls)
    {
        var result = new List<IrField>();
        foreach (var d in decls)
        {
            IrTypeRef irType;
            if (!_ctx.TypeRegistry.TryResolve(d.Type, out irType))
                irType = Stage5_Schedule.UnknownType;
            result.Add(new IrField
            {
                Id   = d.Id,
                Name = d.Name,
                Type = irType,
                DefaultValueCSharp = d.DefaultValueJson ?? "",
                Comment = d.Comment,
            });
        }
        return result;
    }

    // -----------------------------------------------------------------------
    // Allocation helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Tags the first statement appended to <paramref name="stmts"/> since
    /// <paramref name="countBefore"/> with <see cref="IrDebugAnnotation.ExecEntryNodeId"/>
    /// set to <paramref name="execNodeId"/>. Does nothing if no new statements were added.
    /// The first new statement marks the beginning of this exec node's execution;
    /// <c>DebugProbeInsertion</c> uses the tag to insert a <c>NodeEnter</c> probe before it.
    /// </summary>
    private static void TagFirstNewStatement(
        List<IrStatement> stmts, int countBefore, Guid execNodeId)
    {
        if (stmts.Count <= countBefore) return;
        var s = stmts[countBefore];
        var existing = s.Debug ?? new IrDebugAnnotation();
        stmts[countBefore] = s with { Debug = existing with { ExecEntryNodeId = execNodeId } };
    }

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
    // Reflection-based field type resolution for vector-aware emission (M10-T3)
    // -----------------------------------------------------------------------

    // Attempts to resolve the C# full type name of a component field/property.
    // Scans all loaded assemblies; returns "var" when resolution fails.
    private static string TryResolveFieldCSharpType(string componentFqn, string propertyPath)
    {
        if (string.IsNullOrEmpty(componentFqn) || string.IsNullOrEmpty(propertyPath)) return "var";

        System.Type? componentType = null;
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            componentType = asm.GetType(componentFqn);
            if (componentType != null) break;
        }
        if (componentType is null) return "var";

        var field = componentType.GetField(propertyPath,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (field is not null) return field.FieldType.FullName ?? "var";

        var prop = componentType.GetProperty(propertyPath,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (prop is not null) return prop.PropertyType.FullName ?? "var";

        return "var";
    }

    // -----------------------------------------------------------------------
    // Decision ID hash (FNV-1a-32 over chars -- matches UtilityDecisionCatalog.ComputeId)
    // -----------------------------------------------------------------------

    private static int ComputeDecisionId(string assetId)
    {
        uint hash = 2166136261u;
        foreach (char c in assetId)
        {
            hash ^= (byte)c;
            hash *= 16777619u;
        }
        return (int)hash;
    }

    // -----------------------------------------------------------------------
    // BlockBuilder: mutable accumulator for one IrBlock
    // -----------------------------------------------------------------------

    private sealed class BlockBuilder
    {
        public IrBlockId Id    { get; }
        public string Label    { get; }
        public List<IrStatement> Statements { get; } = new();
        public IrTerminator? Terminator { get; set; }
        /// <summary>
        /// The authored exec node that owns this block. Set for blocks that directly
        /// represent an authored exec node; null for infrastructure blocks.
        /// </summary>
        public Guid? SourceNodeId { get; set; }

        public BlockBuilder(IrBlockId id, string label)
        {
            Id    = id;
            Label = label;
        }

        public IrBlock Build() => new IrBlock
        {
            Id           = Id,
            Label        = Label,
            Statements   = Statements.AsReadOnly(),
            Terminator   = Terminator ?? new IrTerm_FallThrough
            {
                Debug = new IrDebugAnnotation { Synthesized = "auto-fallthrough" },
            },
            SourceNodeId = SourceNodeId,
        };
    }
}

