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

    internal static readonly IrTypeRef BoolType = new IrTypeRef
    {
        FullName = "System.Boolean",
        IsUnmanaged = true,
        SizeBytes = 1,
    };

    // FDP entity handle -- mirrors StaticTypeRegistry's "Fdp.Core.Entity" table entry. Used by
    // GetComponentNode's self-default lowering (IrOp_Self's result value type).
    internal static readonly IrTypeRef EntityType = new IrTypeRef
    {
        FullName = "Fdp.Core.Entity",
        IsUnmanaged = true,
        SizeBytes = 8,
        IsEntityHandle = true,
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

    // Cross-block cache for values produced by IMPURE exec statements (e.g. a non-pure
    // FunctionCall's Return pin, a CallPeerBlueprint's Return pin). Unlike _pinValueCache
    // (cleared per block -- correct for pure/recomputable reads whose upstream may have been
    // mutated by an intervening write), a statement-produced value is materialized exactly
    // once as a real C# local (__tN) at the point the statement is scheduled; the emitted
    // TickCore body is flat (goto-based, no nested scopes), so that local remains in scope and
    // definitely-assigned for any later block reachable only through the block that declared
    // it. Consuming such a pin from a later block must reuse the already-materialized value,
    // NOT fall through ResolveNodeOutput's switch to the "default" fallback (which would both
    // produce a bogus value AND, if it matched a re-invocable case, incorrectly re-run the side
    // effect). Never cleared -- populated at the same sites that write _pinValueCache for
    // statement-scheduled (non-pure) node outputs, checked first in ResolveDataPin /
    // ResolveNodeOutput.
    private readonly Dictionary<Guid, IrValue> _statementPinCache = new();

    // Tracks whether a block has been fully scheduled
    private readonly HashSet<int> _scheduledBlocks = new();

    // Tracks which block each exec node's statements landed in (nodeId → blockId).
    private readonly Dictionary<Guid, int> _execNodeToBlockId = new();

    // Convergent control flow (merge points). A node reached by >= 2 incoming exec edges is a
    // "merge point": every predecessor path must JUMP to one shared block for it, rather than
    // re-inlining its downstream chain into each predecessor's block (which would duplicate the
    // node's code and, for a Branch, produce duplicate goto labels -> CS0140). _mergePoints is
    // precomputed from exec in-degree; _mergeBlockForNode lazily allocates the one shared block.
    // Scope: applied at the Branch-arm, linear-chain, FlowForEach-"Completed", and latent-resume
    // successor sites -- the ways a diamond/join is formed in a valid graph. (A Sequence-branch or
    // When-arm root cannot also be a merge target in a well-structured graph: its fall-through
    // continuation is position-dependent, which a shared block cannot express, so those sites keep
    // their 1-edge behavior.)
    private readonly HashSet<Guid> _mergePoints = new();
    private readonly Dictionary<Guid, int> _mergeBlockForNode = new();

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

        ComputeMergePoints();

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
            // Q#14: for an Event graph, carry the event identity from its EventEntry so the emitter can key
            // EventHandlers by it (the graph name is a method-name suffix and can't be the FQN).
            EventTypeFqn = _graph.Kind == GraphKind.Event
                ? (entryNode as EventEntryNode)?.EventTypeId
                : null,
            // Q#14 (3d): Self/Any recipient filter, carried from the EventEntry for the thunk guard.
            TargetFilterSelf = _graph.Kind == GraphKind.Event
                && (entryNode as EventEntryNode)?.TargetFilterSelf == true,
            TargetFieldName = _graph.Kind == GraphKind.Event
                ? (entryNode as EventEntryNode)?.TargetFieldName
                : null,
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
                    // Q#13: the success continuation is the exec-out that is NOT "OnFailure" (named
                    // "Out" in real pin-less assets, "ExecOut" via the test builder — resolve by
                    // exclusion, not a fixed name). The optional "OnFailure" exec-out, when wired,
                    // becomes the channel-Failure continuation. GetSingleExecSuccessor no longer works
                    // (two exec-outs). Unwired OnFailure ⇒ null ⇒ auto-Failure (byte-identical).
                    ScheduleLatentNode(wfc, bb, BuildWaitForChannelOp(wfc),
                        successSuccessor: GetExecSuccessorExcludingPinName(wfc, "OnFailure"),
                        failureSuccessor: GetExecSuccessorByPinName(wfc, "OnFailure"));
                    return;

                case WaitForEventNode wfe:
                    // Q#13-D: same OnFailure split as WaitForChannel — success is the non-"OnFailure"
                    // exec-out; a wired "OnFailure" routes the failure resume. Unwired ⇒ unchanged.
                    ScheduleLatentNode(wfe, bb, BuildWaitForEventOp(wfe),
                        successSuccessor: GetExecSuccessorExcludingPinName(wfe, "OnFailure"),
                        failureSuccessor: GetExecSuccessorByPinName(wfe, "OnFailure"));
                    return;

                case WhenNode wn:
                    ScheduleWhenNode(wn, bb);
                    return;

                // AN8: non-channel action node (ActionFqn set) — inline-latent invocation.
                case ChannelCommandNode { ActionFqn: { } fqn } cc when !string.IsNullOrEmpty(fqn):
                    ScheduleInlineActionNode(cc, bb);
                    return;

                case FlowForEachNode fe:
                    // P1 (GAP-1) -- inline bounded loop. Emits the self/roster read + IrOp_ForEach
                    // (with the Body scheduled INLINE as a nested statement list) into THIS block,
                    // then continues the OUTER exec chain at "Completed" in the SAME block (the loop
                    // is a single statement, not a per-iteration block split).
                    _execNodeToBlockId[fe.Id] = blockId;
                    bb.SourceNodeId ??= fe.Id;
                    ScheduleFlowForEachNode(fe, bb);
                    var feCompleted = GetExecSuccessorByPinName(fe, "Completed");
                    if (feCompleted is null)
                    {
                        ReportDroppedExecSuccessors(fe);
                        SealFallThrough(blockId, bb, DebugOf(fe));
                        return;
                    }
                    if (IsMergePoint(feCompleted.Id))
                    {
                        bb.Terminator = new IrTerm_Goto(GetOrAllocMergeBlock(feCompleted)) { Debug = DebugOf(fe) };
                        return;
                    }
                    node = feCompleted;
                    continue;

                case ComponentForEachNode cfe:
                    // CA-07b -- component-collection inline bounded loop. Schedules + continues the
                    // outer chain EXACTLY like FlowForEachNode above (see ScheduleComponentForEachNode's
                    // doc comment for the one lowering difference: the component is re-read off the
                    // resolved "Collection" in-pin entity, not self).
                    _execNodeToBlockId[cfe.Id] = blockId;
                    bb.SourceNodeId ??= cfe.Id;
                    ScheduleComponentForEachNode(cfe, bb);
                    var cfeCompleted = GetExecSuccessorByPinName(cfe, "Completed");
                    if (cfeCompleted is null)
                    {
                        ReportDroppedExecSuccessors(cfe);
                        SealFallThrough(blockId, bb, DebugOf(cfe));
                        return;
                    }
                    if (IsMergePoint(cfeCompleted.Id))
                    {
                        bb.Terminator = new IrTerm_Goto(GetOrAllocMergeBlock(cfeCompleted)) { Debug = DebugOf(cfe) };
                        return;
                    }
                    node = cfeCompleted;
                    continue;

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
                    if (IsMergePoint(succ.Id))
                    {
                        // Convergent successor: jump to its shared block instead of re-inlining its
                        // chain here (another predecessor also reaches it).
                        bb.Terminator = new IrTerm_Goto(GetOrAllocMergeBlock(succ)) { Debug = DebugOf(node) };
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

    private void ScheduleLatentNode(Node node, BlockBuilder bb, IrOperation latentOp,
        Node? successSuccessor = null, Node? failureSuccessor = null)
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

        // Q#13: when a failure continuation is supplied (WaitForChannel with OnFailure wired),
        // allocate a parallel failure-resume block, enqueue its exec chain, and thread it via
        // Suspend.FailureBlock so WaitLowering routes a channel-Failure resume here instead of
        // returning NodeStatus.Failure. Null ⇒ unchanged auto-Failure behavior.
        IrBlockId? failureResumeBlockId = null;
        if (failureSuccessor is not null)
        {
            var failBlockId = AllocBlock($"wait_fail_{_resumeCounter - 1}");
            failureResumeBlockId = failBlockId;
            if (IsMergePoint(failureSuccessor.Id))
            {
                _blockBuilders[failBlockId.Value].Terminator =
                    new IrTerm_Goto(GetOrAllocMergeBlock(failureSuccessor)) { Debug = DebugOf(node) };
                _scheduledBlocks.Add(failBlockId.Value);
            }
            else
                _bfsQueue.Enqueue((failBlockId.Value, failureSuccessor));
        }

        bb.Terminator = new IrTerm_Suspend(
            ResumePoint  : resumePointValue,
            WaitUntilTime: null,
            ResumeBlock  : resumeBlockId,
            FailureBlock : failureResumeBlockId)
        {
            Debug = DebugOf(node),
        };

        // Propagate fall-through target from current block to resume block
        // (so nested latent inside a Sequence branch continues to the next branch).
        if (_fallThroughTarget.TryGetValue(bb.Id.Value, out var latentFt))
            _fallThroughTarget[resumeBlockId.Value] = latentFt;

        // Enqueue continuation in resume block. Q#13: prefer the caller-resolved success successor
        // (WaitForChannel's "Out") — GetSingleExecSuccessor returns null once the node has >1 exec-out.
        var continuation = successSuccessor ?? GetSingleExecSuccessor(node);
        if (continuation is not null && IsMergePoint(continuation.Id))
        {
            // Convergent continuation: the resume block jumps to the shared merge block rather than
            // re-inlining the join here (another path also reaches it).
            _blockBuilders[resumeBlockId.Value].Terminator =
                new IrTerm_Goto(GetOrAllocMergeBlock(continuation)) { Debug = DebugOf(node) };
            _scheduledBlocks.Add(resumeBlockId.Value);
        }
        else if (continuation is not null)
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
        var (trueSucc, falseSucc) = GetBranchSuccessors(bn);

        // Fall-through target inherited by FRESH arm blocks (Branch inside a Sequence branch -> the
        // arm continues to the next sequence branch). A shared merge block does NOT inherit it.
        IrBlockId? branchFt = _fallThroughTarget.TryGetValue(bb.Id.Value, out var ft) ? ft : (IrBlockId?)null;

        var trueBlock  = ResolveArmBlock(trueSucc,  $"branch_{idShort}_true",  branchFt);
        var falseBlock = ResolveArmBlock(falseSucc, $"branch_{idShort}_false", branchFt);

        bb.Terminator = new IrTerm_Branch(condValue, trueBlock, falseBlock)
        {
            Debug = DebugOf(bn),
        };
    }

    /// <summary>
    /// Resolves the target block for one <see cref="BranchNode"/> arm (also usable by any 2-way
    /// terminator). A merge-point successor shares its single <see cref="GetOrAllocMergeBlock"/> block
    /// (all predecessors jump to it, scheduled once); a normal successor gets a fresh block enqueued
    /// for scheduling; a null successor gets a fresh, sealed fall-through block. <paramref name="fallThrough"/>
    /// is applied only to FRESH/sealed blocks -- a shared merge block's continuation is a property of the
    /// join node itself, set once when it is scheduled, not of the arm that reached it.
    /// </summary>
    private IrBlockId ResolveArmBlock(Node? succ, string label, IrBlockId? fallThrough)
    {
        if (succ is null)
        {
            var b = AllocBlock(label);
            if (fallThrough.HasValue) _fallThroughTarget[b.Value] = fallThrough.Value;
            SealFallThrough(b.Value, _blockBuilders[b.Value]);
            return b;
        }
        if (IsMergePoint(succ.Id))
            return GetOrAllocMergeBlock(succ);   // shared block; enqueued on first allocation
        var fresh = AllocBlock(label);
        if (fallThrough.HasValue) _fallThroughTarget[fresh.Value] = fallThrough.Value;
        _bfsQueue.Enqueue((fresh.Value, succ));
        return fresh;
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
    /// (<see cref="IrTerm_ReturnStatus"/>(Success) for AiPrimitive, and for Library only
    /// while <see cref="_graph"/> declares no outputs; void <see cref="IrTerm_Return"/>
    /// otherwise -- Instance, matching <see cref="BuildReturnTerminator"/>'s rule -- BP-104).
    ///
    /// <para>
    /// BP-117: the one case BP-104 got wrong is an outputs-declaring <b>Library</b> graph whose chain
    /// fell off the end. Its method returns <c>T</c> or a <c>ValueTuple</c>, so the void return BP-104
    /// chose is <b>CS0126</b>. That case now emits <c>return default;</c>
    /// (<see cref="IrTerm_Return.ReturnsDefault"/>) <b>and</b> <c>BP1657</c> -- valid C#, plus a
    /// diagnostic, because a silently-defaulted return value is worse than a compile error.
    /// </para>
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
        // BP-104: _graph (the graph currently being scheduled) is in scope here, so the same
        // outputs-driven rule as BuildReturnTerminator applies: AiPrimitive is unconditional;
        // Library only takes the status branch when it declares no outputs. An outputs-declaring
        // Library graph that falls off the end with no ReturnNode gets an implicit VOID return here
        // -- same as Instance -- rather than a NodeStatus that would mismatch its declared C# return
        // type (the same CS0029 shape BuildReturnTerminator fixes for the explicit-Return case).
        if (_typed.Asset.Dispatch == AssetDispatchKind.AiPrimitive
            || (_typed.Asset.Dispatch == AssetDispatchKind.Library && _graph.Outputs.Count == 0))
        {
            var term = new IrTerm_ReturnStatus(NodeStatus.Success);
            if (debug is not null) term = term with { Debug = debug };
            bb.Terminator = term;
        }
        else
        {
            // BP-117: BP-104 correctly stopped emitting a NodeStatus here, but the void return it put
            // in its place is only right for Instance (a void method). A Library graph DECLARING
            // outputs compiles to a method returning T or a ValueTuple, and `return;` there is CS0126
            // -- reported by Roslyn against generated code the author never wrote. Emit
            // `return default;` so the generated C# is valid, and report BP1657 so the implicit
            // default is never silently returned: this is exactly C#'s "not all code paths return a
            // value", and a wrong VALUE is worse than a compile error.
            bool libraryOwesAValue =
                _typed.Asset.Dispatch == AssetDispatchKind.Library && _graph.Outputs.Count > 0;

            if (libraryOwesAValue)
            {
                _ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1657,
                    $"Library graph \"{_graph.Name}\" declares {_graph.Outputs.Count} output(s) but an "
                    + "execution path ends without a Return node; the generated function would return "
                    + "an unspecified default. Add a Return node on every path (C#: \"not all code "
                    + "paths return a value\").",
                    _typed.Asset.AssetId, _graph.Id));
            }

            var term = new IrTerm_Return(null /* void */, ReturnsDefault: libraryOwesAValue);
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

            case SetSharedNode ssn:
            {
                // Name-keyed slot -- NOT FindVariableIndex (there is no variable/struct-field index;
                // the accessor resolves the slot by string variableId at runtime).
                string sharedTypeFqn = NormalizeSharedTypeFqn(ssn.SharedTypeId);

                // Q#14 multi-pin: baked per-field decls → one per-field write per WIRED field pin
                // (unwired = not written = preserved). Sources resolve top-to-bottom into temporaries
                // (evaluate-then-write); the writes touch distinct offsets, so they are order-independent.
                if (ssn.Fields is { Count: > 0 })
                {
                    foreach (var f in ssn.Fields)
                    {
                        var fieldPin = node.Pins.FirstOrDefault(p =>
                            !p.IsExec && p.Direction == "In"
                            && string.Equals(p.Name, f.Name, StringComparison.OrdinalIgnoreCase));
                        if (fieldPin is null) continue;
                        var link = _graph.Links.FirstOrDefault(
                            l => l.ToNodeId == node.Id && l.ToPinId == fieldPin.Id);
                        if (link is null) continue; // unwired field → leave the slot's value untouched
                        var fieldVal = ResolveNodeOutput(link.FromNodeId, link.FromPinId, stmts);
                        stmts.Add(new IrStatement
                        {
                            Operation = new IrOp_WriteSharedField(
                                ssn.VariableId, sharedTypeFqn, NormalizeSharedTypeFqn(f.TypeId),
                                f.Offset, fieldVal),
                            Debug = DebugOf(node),
                        });
                    }
                    break;
                }

                var dataPin = node.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "In"
                    && string.Equals(p.Name, "Value", StringComparison.OrdinalIgnoreCase));
                if (dataPin is null) break;
                var val = ResolveDataPin(node.Id, dataPin.Id, stmts);

                var writtenPin = node.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "Out"
                    && string.Equals(p.Name, "Written", StringComparison.OrdinalIgnoreCase));
                IrValue? writtenResult = writtenPin is not null
                    ? AllocValue(Stage5_Schedule.BoolType)
                    : null;

                stmts.Add(new IrStatement
                {
                    ResultValue = writtenResult,
                    Operation   = new IrOp_WriteShared(ssn.VariableId, sharedTypeFqn, val),
                    Debug       = DebugOf(node),
                });

                if (writtenPin is not null && writtenResult.HasValue)
                    _pinValueCache[writtenPin.Id] = writtenResult.Value;
                break;
            }

            case SetComponentNode { IsManaged: true } scnM:
            {
                // CA-06 (Slice W2, Q#16-C) -- managed whole-replace write-if-present, self-only.
                // Shape mirrors the unmanaged branch below (self via IrOp_Self, one guarded
                // ResultValue doubling as "Written"), but the node projects a SINGLE data-IN "Value"
                // pin (component-typed) instead of per-field pins (see Stage0_Rehydrate
                // .EnrichSetComponentPins's IsManaged branch) -- resolve THAT pin's wire directly
                // (mirrors the per-field lookup's own link-lookup style, just for one named pin),
                // not ResolveDataPin (which would emit a spurious BP4001 for an intentionally-unwired
                // pin -- unwired here is a legal "guard only, nothing to write" state, see
                // IrOp_SetManagedComponent's doc comment).
                var valuePinM = scnM.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "In"
                    && string.Equals(p.Name, "Value", StringComparison.OrdinalIgnoreCase));

                IrValue? wiredValueM = null;
                if (valuePinM is not null)
                {
                    var valueLinkM = _graph.Links.FirstOrDefault(
                        l => l.ToNodeId == node.Id && l.ToPinId == valuePinM.Id);
                    if (valueLinkM is not null)
                        wiredValueM = ResolveNodeOutput(valueLinkM.FromNodeId, valueLinkM.FromPinId, stmts);
                }

                // Self-only by construction (Q#16) -- same as the unmanaged branch.
                var selfEntityM = AllocValue(Stage5_Schedule.EntityType);
                stmts.Add(new IrStatement
                {
                    ResultValue = selfEntityM,
                    Operation   = new IrOp_Self(),
                    Debug       = DebugOf(node),
                });

                // ALWAYS allocated (guards the write AND drives "Written"), same reasoning as the
                // unmanaged branch's writtenResultC.
                var writtenResultM = AllocValue(Stage5_Schedule.BoolType);
                stmts.Add(new IrStatement
                {
                    ResultValue = writtenResultM,
                    Operation   = new IrOp_SetManagedComponent(scnM.ComponentTypeFqn, selfEntityM, wiredValueM),
                    Debug       = DebugOf(node),
                });

                var writtenPinM = node.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "Out"
                    && string.Equals(p.Name, "Written", StringComparison.OrdinalIgnoreCase));
                if (writtenPinM is not null)
                    _pinValueCache[writtenPinM.Id] = writtenResultM;
                break;
            }

            case SetComponentNode scn:
            {
                // CA-03 (Slice W1, Q#16) -- unmanaged write-if-present, self-only. Resolve ONLY
                // the WIRED field data-in pins into an (Name, Value) list -- an unwired field is
                // simply never added, which is exactly how "unwired preserved" is achieved (mirrors
                // SetSharedNode's per-field branch above, minus the byte offset -- there is no
                // blittable-blob write here, just a typed member assignment).
                var fields = new List<(string Name, IrValue Value)>();
                if (scn.Fields is { Count: > 0 })
                {
                    foreach (var f in scn.Fields)
                    {
                        var fieldPin = node.Pins.FirstOrDefault(p =>
                            !p.IsExec && p.Direction == "In"
                            && string.Equals(p.Name, f.Name, StringComparison.OrdinalIgnoreCase));
                        if (fieldPin is null) continue;
                        var link = _graph.Links.FirstOrDefault(
                            l => l.ToNodeId == node.Id && l.ToPinId == fieldPin.Id);
                        if (link is null) continue; // unwired field -> not written -> preserved
                        var fieldVal = ResolveNodeOutput(link.FromNodeId, link.FromPinId, stmts);
                        fields.Add((f.Name, fieldVal));
                    }
                }

                // Self-only by construction (Q#16) -- SetComponentNode has no "Target" pin at all;
                // entity is ALWAYS the resolved self (mirrors GetComponentNode's unwired-Target
                // self-default, just unconditional here).
                var selfEntity = AllocValue(Stage5_Schedule.EntityType);
                stmts.Add(new IrStatement
                {
                    ResultValue = selfEntity,
                    Operation   = new IrOp_Self(),
                    Debug       = DebugOf(node),
                });

                // This ResultValue doubles as the guarded block's HasComponent bool AND the
                // "Written" out-pin's value -- ALWAYS allocated (the write must be guarded whether
                // or not a graph author actually wires "Written" downstream).
                var writtenResultC = AllocValue(Stage5_Schedule.BoolType);
                stmts.Add(new IrStatement
                {
                    ResultValue = writtenResultC,
                    Operation   = new IrOp_WriteComponentFields(scn.ComponentTypeFqn, selfEntity, fields),
                    Debug       = DebugOf(node),
                });

                var writtenPinC = node.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "Out"
                    && string.Equals(p.Name, "Written", StringComparison.OrdinalIgnoreCase));
                if (writtenPinC is not null)
                    _pinValueCache[writtenPinC.Id] = writtenResultC;
                break;
            }

            case CollectionWriteNode cwn:
            {
                // FC-1 (Q#20) -- component-collection element write. The "Ok" ResultValue is ALWAYS
                // allocated (mirrors SetComponentNode's "Written"), so downstream wires resolve even
                // on the degraded paths below.
                var okResult = AllocValue(Stage5_Schedule.BoolType);
                var okPin = node.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "Out"
                    && string.Equals(p.Name, "Ok", StringComparison.OrdinalIgnoreCase));
                if (okPin is not null) _pinValueCache[okPin.Id] = okResult;

                // Author-time binding check -- mirrors the CA-07 consumers' unwired/unbaked safe
                // default (Stage2's BP2067 catches the wired-but-unbaked half at validation time;
                // unwired is legitimately "not used yet"). The wire is NEVER the write entity (G4
                // defense-in-depth: self-only regardless of what the producer resolved to).
                var cwCollPin = node.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "In"
                    && string.Equals(p.Name, "Collection", StringComparison.OrdinalIgnoreCase));
                bool cwWired = cwCollPin is not null && _graph.Links.Any(
                    l => l.ToNodeId == node.Id && l.ToPinId == cwCollPin.Id);

                // Per-op operand pins -- required operands resolved WIRED-ONLY (an unwired required
                // operand degrades to the same safe no-write default; never a dangling IrValue).
                bool needsInt   = cwn.Op is CollectionWriteOp.SetAt or CollectionWriteOp.InsertAt
                                            or CollectionWriteOp.RemoveAt or CollectionWriteOp.Resize;
                bool needsValue = cwn.Op is CollectionWriteOp.Add or CollectionWriteOp.SetAt
                                            or CollectionWriteOp.InsertAt;
                string intPinName = cwn.Op == CollectionWriteOp.Resize ? "Length" : "Index";

                IrValue? ResolveWiredOperand(string pinName)
                {
                    var pin = node.Pins.FirstOrDefault(p =>
                        !p.IsExec && p.Direction == "In"
                        && string.Equals(p.Name, pinName, StringComparison.OrdinalIgnoreCase));
                    if (pin is null) return null;
                    var link = _graph.Links.FirstOrDefault(
                        l => l.ToNodeId == node.Id && l.ToPinId == pin.Id);
                    if (link is null) return null;
                    return ResolveNodeOutput(link.FromNodeId, link.FromPinId, stmts);
                }

                IrValue? cwIntArg = needsInt   ? ResolveWiredOperand(intPinName) : null;
                IrValue? cwValue  = needsValue ? ResolveWiredOperand("Value")    : null;

                bool degraded = !cwWired
                    || string.IsNullOrEmpty(cwn.ComponentTypeFqn)
                    || string.IsNullOrEmpty(cwn.WriteAccessorFqn)
                    || cwn.CollectionKind == CollectionKind.ManagedMember   // BP2068 backstop
                    || (needsInt   && cwIntArg is null)
                    || (needsValue && cwValue  is null);
                if (degraded)
                {
                    stmts.Add(new IrStatement
                    {
                        ResultValue = okResult,
                        Operation   = new IrOp_Const("false", Stage5_Schedule.BoolType),
                        Debug       = new IrDebugAnnotation
                        {
                            GraphId     = _graph.Id,
                            NodeId      = node.Id,
                            Synthesized = "collection-write-unwired-or-unbaked",
                        },
                    });
                    break;
                }

                // Self-only by construction (Q#16/Q#20) -- entity is ALWAYS the resolved self,
                // mirrors SetComponentNode exactly; the Collection wire's entity is deliberately
                // never read here.
                var cwSelf = AllocValue(Stage5_Schedule.EntityType);
                stmts.Add(new IrStatement
                {
                    ResultValue = cwSelf,
                    Operation   = new IrOp_Self(),
                    Debug       = DebugOf(node),
                });

                stmts.Add(new IrStatement
                {
                    ResultValue = okResult,
                    Operation   = new IrOp_CollectionWrite(
                        cwn.ComponentTypeFqn,
                        cwSelf,
                        cwn.WriteAccessorFqn,
                        cwn.Op.ToString(),
                        node.Id,
                        cwIntArg,
                        cwValue,
                        ReturnsBool: cwn.Op != CollectionWriteOp.Clear),
                    Debug       = DebugOf(node),
                });
                break;
            }

            case ListWriteNode lwn:
            {
                // FC-2/LV-3 -- fixed-list VARIABLE write. Mirrors CollectionWriteNode's shape
                // (always-allocated Ok except Clear, wired-only required operands, degrade to a
                // safe no-write) but the target is the state field named by VariableId -- no
                // entity, no accessor, in-place mutation via IrOp_ListWrite.
                var lwDecl = TryGetListVariableDeclById(lwn.VariableId);

                IrValue? lwOk = null;
                if (lwn.Op != CollectionWriteOp.Clear)
                {
                    lwOk = AllocValue(Stage5_Schedule.BoolType);
                    var lwOkPin = node.Pins.FirstOrDefault(p =>
                        !p.IsExec && p.Direction == "Out"
                        && string.Equals(p.Name, "Ok", StringComparison.OrdinalIgnoreCase));
                    if (lwOkPin is not null) _pinValueCache[lwOkPin.Id] = lwOk.Value;
                }

                bool lwNeedsInt   = lwn.Op is CollectionWriteOp.SetAt or CollectionWriteOp.InsertAt
                                             or CollectionWriteOp.RemoveAt or CollectionWriteOp.Resize;
                bool lwNeedsValue = lwn.Op is CollectionWriteOp.Add or CollectionWriteOp.SetAt
                                             or CollectionWriteOp.InsertAt;
                string lwIntPinName = lwn.Op == CollectionWriteOp.Resize ? "Length" : "Index";

                IrValue? ResolveWiredLwOperand(string pinName)
                {
                    var pin = node.Pins.FirstOrDefault(p =>
                        !p.IsExec && p.Direction == "In"
                        && string.Equals(p.Name, pinName, StringComparison.OrdinalIgnoreCase));
                    if (pin is null) return null;
                    var link = _graph.Links.FirstOrDefault(
                        l => l.ToNodeId == node.Id && l.ToPinId == pin.Id);
                    if (link is null) return null;
                    return ResolveNodeOutput(link.FromNodeId, link.FromPinId, stmts);
                }

                IrValue? lwIntArg = lwNeedsInt   ? ResolveWiredLwOperand(lwIntPinName) : null;
                IrValue? lwValue  = lwNeedsValue ? ResolveWiredLwOperand("Value")      : null;

                bool lwDegraded = lwDecl is null
                    || (lwNeedsInt   && lwIntArg is null)
                    || (lwNeedsValue && lwValue  is null);
                if (lwDegraded)
                {
                    // Unbound target / unwired required operand -- safe no-write, Ok=false
                    // (Stage2's BP1505 catches the unbound-target half at validation time).
                    if (lwOk is { } lwOkVal)
                    {
                        stmts.Add(new IrStatement
                        {
                            ResultValue = lwOkVal,
                            Operation   = new IrOp_Const("false", Stage5_Schedule.BoolType),
                            Debug       = new IrDebugAnnotation
                            {
                                GraphId     = _graph.Id,
                                NodeId      = node.Id,
                                Synthesized = "list-write-unbound-or-unwired",
                            },
                        });
                    }
                    break;
                }

                stmts.Add(new IrStatement
                {
                    ResultValue = lwOk,
                    Operation   = new IrOp_ListWrite(
                        lwDecl!.Name,
                        lwDecl.Type.TypeId,
                        lwDecl.Type.Capacity,
                        lwn.Op.ToString(),
                        node.Id,
                        lwIntArg,
                        lwValue),
                    Debug       = DebugOf(node),
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
                var gcArgs    = ResolveAllDataInputs(node, stmts);
                var gcOutPins = node.Pins.Where(p => !p.IsExec && p.Direction == "Out").ToList();
                var gcOutPin  = gcOutPins.FirstOrDefault();

                // BP-73: a target graph with N outputs returns a ValueTuple carrier, which is then
                // fanned out one statement per out-pin. Each fan-out value is cached in
                // _statementPinCache (NOT _pinValueCache) because it is produced by a REAL statement
                // already in the block -- the distinction that stops a later consumer from
                // recomputing or defaulting it.
                if (targetGraph.Outputs.Count > 1)
                {
                    var multiCarrier = AllocValue(Stage5_Schedule.UnknownType);
                    stmts.Add(new IrStatement
                    {
                        ResultValue = multiCarrier,
                        Operation   = new IrOp_GraphCall(
                            targetGraphGuid, gcArgs, Stage5_Schedule.UnknownType),
                        Debug       = DebugOf(node),
                    });
                    EmitCarrierFanOut(multiCarrier, gcOutPins, targetGraph, stmts, node);
                    break;
                }

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
                // Impure CLR method call (curated helper, e.g. AreaQueryBatchOps.Request/Free) --
                // resolve inputs, emit call, cache output. This is NOT a call into another
                // Library-dispatch blueprint (that is IrOp_LibraryCall's actual purpose, keyed by
                // a real LibraryBlueprintId resolved elsewhere); fc.TargetTypeId here is an
                // ordinary CLR type FQN, so this must lower exactly like the pure-FunctionCall
                // case below (IrOp_PureCall -> `global::{TargetTypeId}.{MethodName}(...)`), just
                // scheduled eagerly as an exec statement instead of resolved lazily on demand.
                // Using IrOp_LibraryCall(0, ...) here was a bug: LibraryBlueprintId 0 resolves to
                // a nonexistent `__LibBp_00000000_Bp` class (CS0103).
                var inputVals = ResolveAllDataInputs(node, stmts);
                var outPin = node.Pins.FirstOrDefault(p => !p.IsExec && p.Direction == "Out");
                var (appendSelf, appendView) =
                    ResolveFunctionCallTrailingContext(fc);
                if (outPin is not null)
                {
                    IrTypeRef retType = _typed.PinTypes.TryGetValue(outPin.Id, out var t)
                        ? t : Stage5_Schedule.UnknownType;
                    var result = AllocValue(retType);
                    stmts.Add(new IrStatement
                    {
                        ResultValue = result,
                        Operation   = new IrOp_PureCall($"{fc.TargetTypeId}.{fc.MethodName}",
                                                         inputVals, retType, appendSelf, appendView),
                        Debug = DebugOf(node),
                    });
                    // Cross-block persistent: this value was materialized as a real statement
                    // (not recomputable on demand -- re-invoking would re-run the side effect),
                    // so later blocks reached only through this one must reuse it, not
                    // recompute/default it. See _statementPinCache.
                    _pinValueCache[outPin.Id] = result;
                    _statementPinCache[outPin.Id] = result;
                }
                else
                {
                    // Void return -- bare statement call, no ResultValue (idx stays -1 so the
                    // emitter writes `{call};` rather than the uncompilable `var __tN = {call};`
                    // that a void C# method invocation would produce).
                    stmts.Add(new IrStatement
                    {
                        ResultValue = null,
                        Operation   = new IrOp_PureCall($"{fc.TargetTypeId}.{fc.MethodName}",
                                                         inputVals, Stage5_Schedule.UnknownType,
                                                         appendSelf, appendView),
                        Debug = DebugOf(node),
                    });
                }
                break;
            }

            case CallPeerBlueprintNode cpb:
            {
                if (!Guid.TryParse(cpb.PeerBlueprintId, out var peerId)) break;
                int peerId32 = BlueprintIdHash.Compute(peerId);
                var inputVals = ResolveAllDataInputs(node, stmts);
                var peerOutPins = node.Pins.Where(p => !p.IsExec && p.Direction == "Out").ToList();

                // BP-113: a peer function declaring N outputs returns a ValueTuple carrier, fanned
                // out one statement per out-pin -- byte-for-byte what BP-73 does for the same-asset
                // FunctionCall. Without this the two pin projections would advertise N pins that the
                // compiler silently collapsed to one: the editor half fixed, the lowering not.
                var peerFuncSig = _ctx.SiblingSignaturesById.TryGetValue(peerId, out var peerSigForCall)
                    ? peerSigForCall.ExportedFunctions.FirstOrDefault(
                        f => string.Equals(f.Name, cpb.FunctionRef, StringComparison.Ordinal))
                    : null;

                if (peerFuncSig is not null && peerFuncSig.Outputs.Count > 1)
                {
                    var peerCarrier = AllocValue(Stage5_Schedule.UnknownType);
                    stmts.Add(new IrStatement
                    {
                        ResultValue = peerCarrier,
                        Operation   = new IrOp_PeerCall(peerId32, cpb.FunctionRef, inputVals,
                                                        Stage5_Schedule.UnknownType),
                        Debug       = DebugOf(node),
                    });
                    EmitCarrierFanOut(
                        peerCarrier, peerOutPins,
                        peerFuncSig.Outputs
                            .Select(o => new BlueprintTypeRef { TypeId = o.TypeId })
                            .ToList(),
                        stmts, node);
                    break;
                }

                var outPin = peerOutPins.FirstOrDefault();
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
                {
                    // Cross-block persistent: same reasoning as the impure-FunctionCall case
                    // above -- a statement-produced value, not recomputable on demand.
                    _pinValueCache[outPin.Id] = result;
                    _statementPinCache[outPin.Id] = result;
                }
                break;
            }

            case ChannelCommandNode cc:
            {
                // Blocker-1 (ChannelCommand enricher round-out): a baked ParamFields entry now
                // surfaces EVERY struct field as a data-IN pin, not just the subset a given asset
                // happens to wire (see BuiltInChannelCommandCatalog). Most fields are legitimately
                // optional (the struct's own zero-value is a meaningful default -- e.g. RouteHandle
                // 0 = fire-and-forget, BackendForce 0 = Auto). Only emit a field into the initializer
                // when it is ACTUALLY connected (an author-drawn link, or a Stage3-materialized
                // default-literal link) -- resolved the same way PublishEvent's optional "Target" pin
                // is resolved (direct link lookup, NOT the unconditional ResolveDataPin below, which
                // would emit a spurious BP4001 AND an IrValue with no backing statement for every
                // truly-unwired optional field, breaking codegen with an undeclared __tN reference).
                // An unconnected field is simply omitted from the `new Params { ... }` initializer,
                // so the struct's own default (0) applies -- exactly the semantics NodePinSchema's
                // editor projection already implies (a pin with no wire = "use the default").
                var paramFields = new List<(string FieldName, IrValue Value)>();
                foreach (var p in node.Pins.Where(p => !p.IsExec && p.Direction == "In"))
                {
                    var link = _graph.Links.FirstOrDefault(
                        l => l.ToNodeId == node.Id && l.ToPinId == p.Id);
                    if (link is null) continue; // unwired optional field — struct default applies.
                    var val = ResolveDataPin(node.Id, p.Id, stmts);
                    paramFields.Add((p.Name, val));
                }

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

            case PublishEventNode pen:
            {
                // P4 (GAP-3) -- catalog-driven exec node, mirrors ChannelCommandNode's shape
                // above, but publishes via world.Bus.Publish (architect ruling Q#5-A) instead of
                // the ECB -- `ecb` is deliberately absent from the AiPrimitive TickCore ABI.
                // Q#14: baked custom-event fields (EventTypeFqn set by the editor from discovery) take
                // precedence; otherwise resolve the shape from the EngineEventCatalog by EventId.
                string eventTypeFqn;
                string? targetFieldName;
                bool managed;
                if (!string.IsNullOrEmpty(pen.EventTypeFqn))
                {
                    eventTypeFqn    = pen.EventTypeFqn!;
                    targetFieldName = pen.TargetFieldName;
                    managed         = pen.Managed;
                }
                else
                {
                    var catalogEntry = _ctx.EngineEvents.GetEntries()
                        .FirstOrDefault(e => string.Equals(e.Name, pen.EventId,
                            StringComparison.OrdinalIgnoreCase));
                    if (catalogEntry is null)
                    {
                        // Unknown EventId + no baked FQN -- no safe publish shape to construct
                        // (`new global::{}`), so emit no IR rather than uncompilable C#.
                        break;
                    }
                    eventTypeFqn    = catalogEntry.EventTypeFqn;
                    targetFieldName = catalogEntry.TargetFieldName;
                    managed         = catalogEntry.Managed;
                }

                // Target -- OPTIONAL "Target" data-in pin, resolved EXACTLY like
                // GetSharedNode/GetComponentNode's Slice-2b "Target" pin: look up the pin, then
                // its link directly (NOT ResolveDataPin, which would emit a spurious BP4001 for
                // an intentionally-unwired optional pin). No pin or no link => self-default: emit
                // IrOp_Self into a fresh Entity-typed IrValue.
                var targetPin = pen.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "In"
                    && string.Equals(p.Name, "Target", StringComparison.OrdinalIgnoreCase));

                IrValue? wiredTarget = null;
                if (targetPin is not null)
                {
                    var targetLink = _graph.Links.FirstOrDefault(
                        l => l.ToNodeId == pen.Id && l.ToPinId == targetPin.Id);
                    if (targetLink is not null)
                        wiredTarget = ResolveNodeOutput(targetLink.FromNodeId, targetLink.FromPinId, stmts);
                }

                IrValue targetEntity;
                if (wiredTarget is { } wt)
                {
                    targetEntity = wt;
                }
                else
                {
                    targetEntity = AllocValue(Stage5_Schedule.EntityType);
                    stmts.Add(new IrStatement
                    {
                        ResultValue = targetEntity,
                        Operation   = new IrOp_Self(),
                        Debug       = DebugOf(node),
                    });
                }

                var fields = new List<(string FieldName, IrValue Value)>();
                if (!string.IsNullOrEmpty(targetFieldName))
                    fields.Add((targetFieldName!, targetEntity));

                // Other payload data-in pins (excluding "Target"), reified exactly like
                // ChannelCommand's paramFields above.
                fields.AddRange(pen.Pins
                    .Where(p => !p.IsExec && p.Direction == "In"
                        && !string.Equals(p.Name, "Target", StringComparison.OrdinalIgnoreCase))
                    .Select(p => (p.Name, ResolveDataPin(pen.Id, p.Id, stmts))));

                stmts.Add(new IrStatement
                {
                    Operation = new IrOp_PublishBusEvent(
                        eventTypeFqn, fields, managed),
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
        // BP-104: hoisted above the dispatch branch -- both arms need it. Stage 0 projects exactly
        // one data-in pin on the Return node per Graph.Outputs entry (in declaration order -- see
        // ResolveAllDataInputs), so "has value pins" == "the containing graph declares outputs".
        //
        // BP-71 / Q24-B1: accept EITHER direction. The projections now emit "In" (the only form a
        // designer can wire, and the convention everywhere else -- see ResolveAllDataInputs), but
        // hand-authored JSON may still carry the legacy "Out" form, which this method has always
        // resolved as an input anyway. Accepting both means there is nothing to migrate and no
        // silently-void return for an asset written against the old shape.
        var valuePins = rn.Pins
            .Where(p => !p.IsExec && (p.Direction == "In" || p.Direction == "Out"))
            .ToList();

        // BP-104: three OTHER halves already derive the method shape from graph.Outputs --
        // LibraryEmitter.CSharpReturnType (0 outputs -> NodeStatus/void, >=1 -> the output type or a
        // tuple), CSharpEmitter.EmitLibraryFunctionAdapter (writes the RETURN VALUE into the outputs
        // span), and BP73_MultipleFunctionOutputsTests' Library adapter test. This terminator was the
        // ONE remaining half still emitting IrTerm_ReturnStatus unconditionally for Library, so a
        // Library function that declares outputs got a method declared to return that type/tuple
        // whose body executed `return NodeStatus.Success;` -- CS0029, a hard Roslyn error that
        // `CompileOk`-style helpers (which only assert `Succeeded`, never invoking the C# compiler)
        // let through silently.
        //
        // AiPrimitive is UNCONDITIONAL: NodeStatus is its BTree/HSM hosting contract, independent of
        // whether the graph happens to declare outputs. Library instead takes the status branch only
        // when it declares NO outputs -- that zero-output case is deliberate and test-locked
        // (BPC_ImplicitReturnTests.Library_NoReturn_EmitsImplicitSuccessReturn feeds LibraryMath.bp.json)
        // and must keep returning NodeStatus, not be swept into the value-return path below.
        bool wantsStatusReturn =
            _typed.Asset.Dispatch == AssetDispatchKind.AiPrimitive
            || (_typed.Asset.Dispatch == AssetDispatchKind.Library && valuePins.Count == 0);

        if (wantsStatusReturn)
        {
            // AiPrimitive returns a NodeStatus. So does a Library function with zero outputs.
            return new IrTerm_ReturnStatus(rn.Status) { Debug = DebugOf(rn) };
        }

        // Function graph (Instance dispatch), or a Library function that DOES declare outputs:
        // return the data value wired into the Return node's value pin (if any).
        //
        // BP-73: N outputs. The pins are collected in declaration order (Stage 0 / the editor both
        // project one per Graph.Outputs entry, in order), each resolved exactly as the single-output
        // case always was, then packed into one carrier value by IrOp_MakeTuple.
        //
        // ⚠ IrTerm_Return keeps its SINGLE IrValue. Packing in a preceding statement rather than
        // widening the terminator means every consumer of IrTerm_Return -- the block emitters, the
        // debug map, the breakpoint anchoring above -- is untouched by this feature.
        if (valuePins.Count > 1)
        {
            var parts = new List<IrValue>(valuePins.Count);
            foreach (var vp in valuePins)
                parts.Add(ResolveReturnValuePin(rn, vp, currentBlock));

            var carrier = AllocValue(Stage5_Schedule.UnknownType);
            currentBlock.Statements.Add(new IrStatement
            {
                ResultValue = carrier,
                Operation   = new IrOp_MakeTuple(parts),
                Debug       = DebugOf(rn),
            });
            return new IrTerm_Return(carrier) { Debug = DebugOf(rn) };
        }

        var valuePin = valuePins.FirstOrDefault();

        IrValue? retVal = valuePin is not null
            ? ResolveReturnValuePin(rn, valuePin, currentBlock)
            : null;

        return new IrTerm_Return(retVal) { Debug = DebugOf(rn) };
    }

    /// <summary>
    /// BP-73: unpacks a multi-output call's ValueTuple carrier into one value per out-pin, in
    /// declaration order, and caches each against its pin so downstream consumers resolve normally.
    /// <para>
    /// Emits a statement per pin even when only some are wired. That is deliberate: the alternative
    /// -- emitting lazily on first use -- would put the extraction inside whichever block first
    /// consumed the pin, which for a call whose result crosses a branch is a different block than the
    /// call. An unused <c>var</c> is harmless; a value read in a block that never declared it is
    /// CS0103.
    /// </para>
    /// <para>
    /// ⚠ Cached in <see cref="_statementPinCache"/>, not <c>_pinValueCache</c>: these values are
    /// produced by real statements already appended to the block.
    /// </para>
    /// </summary>
    private void EmitCarrierFanOut(
        IrValue carrier, List<Pin> outPins, Graph targetGraph,
        List<IrStatement> stmts, Node node)
        => EmitCarrierFanOut(
            carrier, outPins, targetGraph.Outputs.Select(o => o.Type).ToList(), stmts, node);

    /// <summary>
    /// BP-113: the same fan-out, driven by a bare list of declared output types rather than a local
    /// <see cref="Graph"/> — so a <b>cross-asset</b> peer call, whose target is a
    /// <c>BlueprintFunctionSig</c> in a sibling's signature and not a graph in this asset, lowers
    /// through the identical path as the same-asset call.
    /// </summary>
    private void EmitCarrierFanOut(
        IrValue carrier, List<Pin> outPins, IReadOnlyList<BlueprintTypeRef> outputTypes,
        List<IrStatement> stmts, Node node)
    {
        // Pair pins to outputs POSITIONALLY -- both projections emit one out-pin per declared output
        // in declaration order, so index i of each list is the same output. Guard on the shorter list
        // so a stale asset with fewer pins than the target now declares cannot throw.
        int n = Math.Min(outPins.Count, outputTypes.Count);
        for (int i = 0; i < n; i++)
        {
            var pin = outPins[i];
            var fieldType = _typed.PinTypes.TryGetValue(pin.Id, out var pt)
                ? pt
                : _ctx.TypeRegistry.TryResolve(outputTypes[i], out var rt)
                    ? rt
                    : Stage5_Schedule.UnknownType;

            var fieldVal = AllocValue(fieldType);
            stmts.Add(new IrStatement
            {
                ResultValue = fieldVal,
                Operation   = new IrOp_TupleField(carrier, i),
                Debug       = new IrDebugAnnotation
                {
                    GraphId = _graph.Id, NodeId = node.Id, PinId = pin.Id,
                },
            });
            _statementPinCache[pin.Id] = fieldVal;
        }
    }

    /// <summary>
    /// Resolves one of the <c>Return</c> node's value pins: the wired value, or a declared
    /// <c>default(T)</c> when nothing is wired to it.
    /// <para>
    /// BP-71 / Q24-C3: only call <see cref="ResolveDataPin"/> when a link actually arrives. Its
    /// unwired path emits BP4001 and hands back a dummy — historically one that was never DECLARED,
    /// so the emitter wrote <c>return __t7;</c> with no <c>var __t7</c>: CS0103 with no BP diagnostic
    /// (BP-69's shape). Stage 2's <c>V_FunctionGraphReturnValue</c> makes the unwired case a hard
    /// error; this keeps the GENERATED C# compilable regardless.
    /// </para>
    /// <para>
    /// BP-73: shared by the single-output path and by each element of a multi-output carrier, so an
    /// unwired output among N behaves exactly like the one unwired output of a single-output graph.
    /// </para>
    /// </summary>
    private IrValue ResolveReturnValuePin(ReturnNode rn, Pin valuePin, BlockBuilder currentBlock)
    {
        bool wired = _graph.Links.Any(
            l => l.ToNodeId == rn.Id && l.ToPinId == valuePin.Id);

        if (wired)
            return ResolveDataPin(rn.Id, valuePin.Id, currentBlock.Statements);

        var retType = _typed.PinTypes.TryGetValue(valuePin.Id, out var pt)
            ? pt : Stage5_Schedule.UnknownType;
        var dflt = AllocValue(retType);
        currentBlock.Statements.Add(new IrStatement
        {
            ResultValue = dflt,
            Operation   = new IrOp_Const("default", retType),
            Debug       = DebugOf(rn),
        });
        return dflt;
    }

    // -----------------------------------------------------------------------
    // Data flow resolution (CSE via _pinValueCache)
    // -----------------------------------------------------------------------

    private IrValue ResolveDataPin(Guid consumerNodeId, Guid pinId,
                                    List<IrStatement> stmts)
    {
        if (_statementPinCache.TryGetValue(pinId, out var stmtCached)) return stmtCached;
        if (_pinValueCache.TryGetValue(pinId, out var cached)) return cached;

        // Find link providing data to this pin.
        var link = _graph.Links.FirstOrDefault(
            l => l.ToNodeId == consumerNodeId && l.ToPinId == pinId);

        if (link == null)
        {
            // Unconnected -- emit BP4001 and return a DECLARED default.
            //
            // BP-69/BP-71: this used to `AllocValue` a bare dummy and return it. An IrValue is only
            // declared in the generated C# by the statement that produces it, so a dummy with no
            // statement produced `Foo(__t7)` / `return __t7;` with no `var __t7` anywhere --
            // **CS0103 from Roslyn with only a BP4001 *warning* to explain it**. That is the same
            // unattributable shape as BP-69 itself, and it is reachable from every one of the ~20
            // ResolveDataPin call sites, not just the return terminator BP-71 hardened.
            //
            // Emitting a typed `default(T)` statement makes the value real: the warning still names
            // the unwired pin, the designer still gets Stage 2 errors where a validator covers the
            // case (e.g. BP1655 for a function return), and Roslyn can no longer fail for a reason
            // no diagnostic explains. Type comes from Stage 4's resolved pin type where known, so
            // `default(float)` is passed to a float parameter rather than `default(object)`.
            _ctx.Diagnostics.Add(Diagnostic.Warning(DiagnosticCodes.BP4001,
                $"Unconnected required data input pin {pinId} on node {consumerNodeId}.",
                _ctx.AssetId, _graph.Id, consumerNodeId, pinId));

            var dummyType = _typed.PinTypes.TryGetValue(pinId, out var resolvedPinType)
                ? resolvedPinType : Stage5_Schedule.UnknownType;
            var dummy = AllocValue(dummyType);
            stmts.Add(new IrStatement
            {
                ResultValue = dummy,
                Operation   = new IrOp_Const("default", dummyType),
                Debug       = new IrDebugAnnotation
                {
                    GraphId = _graph.Id, NodeId = consumerNodeId, PinId = pinId,
                },
            });
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
        if (_statementPinCache.TryGetValue(sourcePinId, out var stmtCached)) return stmtCached;
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

            // Q#14 Option B — MakeStruct: build a struct value from its wired field data-ins (unwired
            // fields keep the struct default). The single "Value" out-pin carries the constructed struct.
            case MakeStructNode msn:
            {
                string structFqn = NormalizeSharedTypeFqn(msn.StructTypeId);
                var madeFields = new List<(string, IrValue)>();
                foreach (var f in msn.Fields)
                {
                    var pin = msn.Pins.FirstOrDefault(p =>
                        !p.IsExec && p.Direction == "In"
                        && string.Equals(p.Name, f.Name, StringComparison.OrdinalIgnoreCase));
                    if (pin is null) continue;
                    var link = _graph.Links.FirstOrDefault(l => l.ToNodeId == msn.Id && l.ToPinId == pin.Id);
                    if (link is null) continue; // unwired field → left at the struct's default
                    madeFields.Add((f.Name, ResolveNodeOutput(link.FromNodeId, link.FromPinId, stmts)));
                }
                var structType = new IrTypeRef { FullName = structFqn, IsUnmanaged = true, SizeBytes = 0 };
                result = AllocValue(structType);
                stmts.Add(new IrStatement
                {
                    ResultValue = result,
                    Operation   = new IrOp_MakeStruct(structFqn, madeFields),
                    Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = msn.Id, PinId = sourcePinId },
                });
                break;
            }

            // Q#14 Option B — BreakStruct: read the "Value" struct data-in once, project each field data-out
            // via IrOp_FieldRead (same read-once-then-project idiom as multi-pin GetShared).
            case BreakStructNode bsn:
            {
                var valuePin = bsn.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "In"
                    && string.Equals(p.Name, "Value", StringComparison.OrdinalIgnoreCase));
                IrValue structVal;
                var vLink = valuePin is null ? null
                    : _graph.Links.FirstOrDefault(l => l.ToNodeId == bsn.Id && l.ToPinId == valuePin.Id);
                if (vLink is not null)
                    structVal = ResolveNodeOutput(vLink.FromNodeId, vLink.FromPinId, stmts);
                else
                {
                    // Unwired struct input → default(struct) so field reads are well-defined.
                    structVal = AllocValue(new IrTypeRef { FullName = NormalizeSharedTypeFqn(bsn.StructTypeId), IsUnmanaged = true, SizeBytes = 0 });
                    stmts.Add(new IrStatement
                    {
                        ResultValue = structVal,
                        Operation   = new IrOp_Const($"default(global::{NormalizeSharedTypeFqn(bsn.StructTypeId)})", structVal.Type),
                        Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = bsn.Id, PinId = sourcePinId },
                    });
                }
                foreach (var f in bsn.Fields)
                {
                    var fPin = bsn.Pins.FirstOrDefault(p =>
                        !p.IsExec && p.Direction == "Out"
                        && string.Equals(p.Name, f.Name, StringComparison.OrdinalIgnoreCase));
                    if (fPin is null) continue;
                    IrTypeRef fType = _typed.PinTypes.TryGetValue(fPin.Id, out var fpt)
                        ? fpt
                        : new IrTypeRef { FullName = NormalizeSharedTypeFqn(f.TypeId), IsUnmanaged = true, SizeBytes = 0 };
                    var fRes = AllocValue(fType);
                    stmts.Add(new IrStatement
                    {
                        ResultValue = fRes,
                        Operation   = new IrOp_FieldRead(structVal, f.Name, fType),
                        Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = bsn.Id, PinId = fPin.Id },
                    });
                    _pinValueCache[fPin.Id] = fRes;
                }
                result = _pinValueCache.TryGetValue(sourcePinId, out var bpr) ? bpr : structVal;
                break;
            }

            // Q#14 Option B — SetMembers: copy the "Source" struct, overwrite wired members, output "Result".
            case SetMembersNode smn:
            {
                string structFqn = NormalizeSharedTypeFqn(smn.StructTypeId);
                var structType = new IrTypeRef { FullName = structFqn, IsUnmanaged = true, SizeBytes = 0 };

                var srcPin = smn.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "In"
                    && string.Equals(p.Name, "Source", StringComparison.OrdinalIgnoreCase));
                var srcLink = srcPin is null ? null
                    : _graph.Links.FirstOrDefault(l => l.ToNodeId == smn.Id && l.ToPinId == srcPin.Id);
                IrValue input;
                if (srcLink is not null)
                    input = ResolveNodeOutput(srcLink.FromNodeId, srcLink.FromPinId, stmts);
                else
                {
                    input = AllocValue(structType);
                    stmts.Add(new IrStatement
                    {
                        ResultValue = input,
                        Operation   = new IrOp_Const($"default(global::{structFqn})", structType),
                        Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = smn.Id, PinId = sourcePinId },
                    });
                }

                var setFields = new List<(string, IrValue)>();
                foreach (var f in smn.Fields)
                {
                    var fp = smn.Pins.FirstOrDefault(p =>
                        !p.IsExec && p.Direction == "In"
                        && string.Equals(p.Name, f.Name, StringComparison.OrdinalIgnoreCase));
                    if (fp is null) continue;
                    var fl = _graph.Links.FirstOrDefault(l => l.ToNodeId == smn.Id && l.ToPinId == fp.Id);
                    if (fl is null) continue; // unwired member → keep source's value
                    setFields.Add((f.Name, ResolveNodeOutput(fl.FromNodeId, fl.FromPinId, stmts)));
                }

                result = AllocValue(structType);
                stmts.Add(new IrStatement
                {
                    ResultValue = result,
                    Operation   = new IrOp_SetMembers(structFqn, input, setFields),
                    Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = smn.Id, PinId = sourcePinId },
                });
                break;
            }

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

            // GetParameter (GAP-11): reads a declared AiPrimitive/Instance Parameter -- mirrors
            // GetVariableNode exactly except it resolves against a PARAMS-ONLY index (never a
            // combined Variables/WorkingState/Parameters index -- see FindParameterIndex) and
            // lowers to the pre-existing IrOp_ReadParam (StatementEmitter already emits
            // `p.{ParamFieldName}` for it; no new IR op, no new StatementEmitter case).
            case GetParameterNode gp:
                int paramIdx = FindParameterIndex(gp.ParameterId);
                result = AllocValue(pinType);
                stmts.Add(new IrStatement
                {
                    ResultValue = result,
                    Operation   = new IrOp_ReadParam(paramIdx),
                    Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = gp.Id, PinId = sourcePinId },
                });
                break;

            // GetAllParameters: one out-pin per asset.Parameters entry -- mirrors GetParameterNode's
            // IrOp_ReadParam lowering, but (like EventEntryNode's data-out pins, which are matched
            // by NAME against Graph.Inputs) resolves the param index by matching the SPECIFIC
            // requested out-pin's Name against asset.Parameters (FindParameterIndex's non-Guid
            // fallback already does a plain name match), rather than a single baked ParameterId.
            case GetAllParametersNode gap:
            {
                var dataOutPins = gap.Pins
                    .Where(p => !p.IsExec && p.Direction == "Out")
                    .ToList();
                var sourcePin = dataOutPins.FirstOrDefault(p => p.Id == sourcePinId);
                int gapIdx = sourcePin is not null ? FindParameterIndex(sourcePin.Name) : -1;

                result = AllocValue(pinType);
                stmts.Add(new IrStatement
                {
                    ResultValue = result,
                    Operation   = new IrOp_ReadParam(gapIdx),
                    Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = gap.Id, PinId = sourcePinId },
                });
                break;
            }

            // WaitForChannel "Status" data-out (Q#13): re-read channel.Status at point of use.
            // The continuation only runs after the channel is non-Running, so this yields Success on
            // the "Out" path and Failure on the "OnFailure" path. Self + GetComponentRO + FieldRead,
            // mirroring the check-block reads WaitLowering emits. Only the Status pin is a data-out on
            // this node, so no per-pin discrimination is needed.
            case WaitForChannelNode wfcStatus:
            {
                string channelFqnS = ResolveChannelTypeFqn(wfcStatus.ChannelType);
                var chTypeRefS = new IrTypeRef { FullName = channelFqnS, IsUnmanaged = true, SizeBytes = 0 };

                var selfVS = AllocValue(Stage5_Schedule.EntityType);
                stmts.Add(new IrStatement
                {
                    ResultValue = selfVS,
                    Operation   = new IrOp_Self(),
                    Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = wfcStatus.Id, PinId = sourcePinId },
                });

                var chVS = AllocValue(chTypeRefS);
                stmts.Add(new IrStatement
                {
                    ResultValue = chVS,
                    Operation   = new IrOp_GetComponentRO(channelFqnS, selfVS, chTypeRefS),
                    Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = wfcStatus.Id, PinId = sourcePinId },
                });

                result = AllocValue(pinType);
                stmts.Add(new IrStatement
                {
                    ResultValue = result,
                    Operation   = new IrOp_FieldRead(chVS, "Status", pinType),
                    Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = wfcStatus.Id, PinId = sourcePinId },
                });
                break;
            }

            case GetSharedNode gsn:
            {
                // Name-keyed slot -- NOT FindVariableIndex (the shared struct is foreign to this
                // asset's variable list; the accessor resolves the slot by string variableId).
                string sharedTypeFqn = NormalizeSharedTypeFqn(gsn.SharedTypeId);

                var valuePin = gsn.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "Out"
                    && string.Equals(p.Name, "Value", StringComparison.OrdinalIgnoreCase));
                var foundPin = gsn.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "Out"
                    && string.Equals(p.Name, "Found", StringComparison.OrdinalIgnoreCase));

                // Slice 2b: OPTIONAL "Target" data-in pin (cross-entity read). Resolve it the same
                // way SpawnEqsSensorNode resolves its optional parameter pins -- look up the pin,
                // then look up its link directly (NOT via ResolveDataPin, which emits BP4001 for an
                // unconnected pin); no pin or no link => null => Stage 7 emits `self`, byte-identical
                // to the pre-Slice-2b unwired path. Mirrors how IrOp_GetComponent carries its
                // resolved Entity argument as an IrValue.
                var targetPin = gsn.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "In"
                    && string.Equals(p.Name, "Target", StringComparison.OrdinalIgnoreCase));
                IrValue? targetEntity = null;
                if (targetPin is not null)
                {
                    var targetLink = _graph.Links.FirstOrDefault(
                        l => l.ToNodeId == gsn.Id && l.ToPinId == targetPin.Id);
                    if (targetLink is not null)
                        targetEntity = ResolveNodeOutput(targetLink.FromNodeId, targetLink.FromPinId, stmts);
                }

                // Q#14 multi-pin: read the whole struct ONCE, then project each field via IrOp_FieldRead
                // (the same field-read op GetComponent uses). "Found" is the read's bool. All out-pins are
                // cached so the single read is shared across every consumed field pin.
                if (gsn.Fields is { Count: > 0 })
                {
                    var structType = new IrTypeRef { FullName = sharedTypeFqn, IsUnmanaged = true, SizeBytes = 0 };
                    var structVal  = AllocValue(structType);
                    var foundRes   = AllocValue(Stage5_Schedule.BoolType);
                    stmts.Add(new IrStatement
                    {
                        ResultValue = structVal,
                        Operation   = new IrOp_ReadShared(gsn.VariableId, sharedTypeFqn, foundRes, targetEntity),
                        Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = gsn.Id, PinId = sourcePinId },
                    });

                    var foundP = gsn.Pins.FirstOrDefault(p =>
                        !p.IsExec && p.Direction == "Out"
                        && string.Equals(p.Name, "Found", StringComparison.OrdinalIgnoreCase));
                    if (foundP is not null) _pinValueCache[foundP.Id] = foundRes;

                    foreach (var f in gsn.Fields)
                    {
                        var fPin = gsn.Pins.FirstOrDefault(p =>
                            !p.IsExec && p.Direction == "Out"
                            && string.Equals(p.Name, f.Name, StringComparison.OrdinalIgnoreCase));
                        if (fPin is null) continue;
                        IrTypeRef fType = _typed.PinTypes.TryGetValue(fPin.Id, out var fpt)
                            ? fpt
                            : new IrTypeRef { FullName = NormalizeSharedTypeFqn(f.TypeId), IsUnmanaged = true, SizeBytes = 0 };
                        var fRes = AllocValue(fType);
                        stmts.Add(new IrStatement
                        {
                            ResultValue = fRes,
                            Operation   = new IrOp_FieldRead(structVal, f.Name, fType),
                            Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = gsn.Id, PinId = fPin.Id },
                        });
                        _pinValueCache[fPin.Id] = fRes;
                    }

                    result = _pinValueCache.TryGetValue(sourcePinId, out var mpr) ? mpr : structVal;
                    break;
                }

                // Prefer the resolved pin type (from Stage4) when available; otherwise fall back
                // to a locally-built IrTypeRef from the SharedTypeFqn (mirrors ReadEqsResult /
                // ReadRankedResult building their own result-struct IrTypeRef rather than relying
                // on PinTypes).
                IrTypeRef valueType = valuePin is not null
                    && _typed.PinTypes.TryGetValue(valuePin.Id, out var vt)
                        ? vt
                        : new IrTypeRef { FullName = sharedTypeFqn, IsUnmanaged = true, SizeBytes = 0 };

                var valueResult = AllocValue(valueType);
                var foundResult = AllocValue(Stage5_Schedule.BoolType);

                stmts.Add(new IrStatement
                {
                    ResultValue = valueResult,
                    Operation   = new IrOp_ReadShared(gsn.VariableId, sharedTypeFqn, foundResult, targetEntity),
                    Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = gsn.Id, PinId = sourcePinId },
                });

                if (valuePin is not null) _pinValueCache[valuePin.Id] = valueResult;
                if (foundPin is not null) _pinValueCache[foundPin.Id] = foundResult;

                // Return the value for the specifically requested pin (mirrors ReadEqsResult /
                // ReadRankedResult's multi-output cache-then-select pattern).
                result = _pinValueCache.TryGetValue(sourcePinId, out var pinRes) ? pinRes : valueResult;
                break;
            }

            case GetComponentNode gcn:
            {
                // P2 (Hill-attack -> Blueprints migration) -- reflection-free ECS component field
                // read. Chains three EXISTING IR ops -- no new IrOperation, no new
                // StatementEmitter case. The exact same three-op sequence already appears inline
                // in WaitLowering_AiPrimitive's channel-check block (IrOp_Self ->
                // IrOp_GetComponentRO -> IrOp_FieldRead), so this is not a novel lowering shape,
                // just the first place a NODE (rather than a synthesized wait-check) drives it.
                // CA-01 (Slice 1a) adds a MULTI-FIELD path below (baked Fields != null): read the
                // component ONCE via the SAME IrOp_GetComponentRO, then project each field via
                // IrOp_FieldRead -- the exact read-once-then-project idiom multi-pin GetShared uses
                // (IrOp_ReadShared -> N x IrOp_FieldRead) -- plus IrOp_HasComponent for "Found".
                // Still no new IrOperation. The legacy single-field path below is BYTE-IDENTICAL to
                // before (it is skipped over entirely -- via the Fields-baked branch's `break` --
                // when Fields is null, so existing pin-authored assets are untouched).
                //
                // Reflection-free rationale: ComponentTypeFqn/FieldName/FieldTypeFqn/Fields[].TypeId
                // are baked strings authored at edit time (mirrors GetSharedNode.SharedTypeId/Fields
                // and the P7.1 FunctionCallNode.TrailingContext bake) -- Stage5 never inspects a real
                // CLR Type to build them, so this survives running inside the Roslyn incremental
                // generator's netstandard2.0 analyzer host, which cannot load game assemblies
                // (Hrot.AI.Behaviors.dll etc.) to reflect over.

                // OPTIONAL "Target" data-in pin (cross-entity read) -- resolved EXACTLY like
                // GetSharedNode's Slice-2b "Target" pin above: look up the pin, then its link
                // directly (NOT ResolveDataPin, which would emit a spurious BP4001 for an
                // intentionally-unwired optional pin). No pin or no link => self-default: emit
                // IrOp_Self into a fresh Entity-typed IrValue and read off that.
                var targetPin = gcn.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "In"
                    && string.Equals(p.Name, "Target", StringComparison.OrdinalIgnoreCase));

                IrValue? wiredTarget = null;
                if (targetPin is not null)
                {
                    var targetLink = _graph.Links.FirstOrDefault(
                        l => l.ToNodeId == gcn.Id && l.ToPinId == targetPin.Id);
                    if (targetLink is not null)
                        wiredTarget = ResolveNodeOutput(targetLink.FromNodeId, targetLink.FromPinId, stmts);
                }

                IrValue entityValue;
                if (wiredTarget is { } wt)
                {
                    entityValue = wt;
                }
                else
                {
                    entityValue = AllocValue(Stage5_Schedule.EntityType);
                    stmts.Add(new IrStatement
                    {
                        ResultValue = entityValue,
                        Operation   = new IrOp_Self(),
                        Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = gcn.Id, PinId = sourcePinId },
                    });
                }

                // Component IrTypeRef -- built locally from the baked FQN (AN2-style "trust the
                // string" -- no StaticTypeRegistry lookup needed; the FQN is emitted verbatim as
                // `global::{FQN}` and validated by the downstream Roslyn compile, exactly like
                // GetSharedNode's SharedTypeFqn / IrOp_GetComponentRO's other call sites).
                // CA-05: IsUnmanaged mirrors gcn.IsManaged -- metadata only (the FQN is emitted
                // verbatim regardless), but correctly reflects which of the two ECS tiers this read
                // targets.
                var componentTypeRef = new IrTypeRef
                {
                    FullName    = gcn.ComponentTypeFqn,
                    IsUnmanaged = !gcn.IsManaged,
                    SizeBytes   = 0,
                };

                // CA-01 multi-pin: baked per-field decls -> read the component ONCE, then project
                // each field via IrOp_FieldRead (same read-once-then-project idiom as multi-pin
                // GetShared), plus "Found" via IrOp_HasComponent. All out-pins are cached so the
                // single read is shared across every consumed field/Found pin.
                // CA-05 (Slice 1b): when gcn.IsManaged, the read op is IrOp_GetManagedComponentRO
                // (not IrOp_GetComponentRO), Found's guard op is HasManagedComponent (IsManaged:
                // true on the SAME IrOp_HasComponent -- no new Found-op needed), and each field
                // projection is SourceIsManaged so a null (absent) managed instance degrades to the
                // field's default instead of an NRE -- see those ops' doc comments for the throw-
                // safety rationale.
                if (gcn.Fields is { Count: > 0 })
                {
                    var compValM = AllocValue(componentTypeRef);
                    stmts.Add(new IrStatement
                    {
                        ResultValue = compValM,
                        Operation   = gcn.IsManaged
                            ? new IrOp_GetManagedComponentRO(gcn.ComponentTypeFqn, entityValue, componentTypeRef)
                            : new IrOp_GetComponentRO(gcn.ComponentTypeFqn, entityValue, componentTypeRef),
                        Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = gcn.Id, PinId = sourcePinId },
                    });

                    var foundRes = AllocValue(Stage5_Schedule.BoolType);
                    stmts.Add(new IrStatement
                    {
                        ResultValue = foundRes,
                        Operation   = new IrOp_HasComponent(gcn.ComponentTypeFqn, entityValue, IsManaged: gcn.IsManaged),
                        Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = gcn.Id, PinId = sourcePinId },
                    });

                    var foundPinM = gcn.Pins.FirstOrDefault(p =>
                        !p.IsExec && p.Direction == "Out"
                        && string.Equals(p.Name, "Found", StringComparison.OrdinalIgnoreCase));
                    if (foundPinM is not null) _pinValueCache[foundPinM.Id] = foundRes;

                    foreach (var f in gcn.Fields)
                    {
                        // CA-07b (supersedes CA-07a's "skip entirely" comment): a collection decl's
                        // out-pin gets NO IrOp_FieldRead -- there is no runtime "collection value",
                        // only the curated accessor pair. Instead, cache the out-pin DIRECTLY to the
                        // already-computed entityValue (the entity the component was read off, in
                        // scope above): a downstream ComponentForEach/ComponentItemGet/
                        // ComponentItemCount consumer resolves ITS "Collection" in-pin to that SAME
                        // entity, re-reads the component there, and calls its own baked accessors
                        // (see Stage5's ComponentForEachNode/ComponentItemGetNode/
                        // ComponentItemCountNode cases below). The pin is not inert -- it now
                        // carries the entity instead of a field value.
                        if (f.IsCollection)
                        {
                            var colPin = gcn.Pins.FirstOrDefault(p => !p.IsExec && p.Direction == "Out"
                                && string.Equals(p.Name, f.Name, StringComparison.OrdinalIgnoreCase));
                            if (colPin is not null) _pinValueCache[colPin.Id] = entityValue;   // collection pin => the entity
                            continue;
                        }

                        var fPin = gcn.Pins.FirstOrDefault(p =>
                            !p.IsExec && p.Direction == "Out"
                            && string.Equals(p.Name, f.Name, StringComparison.OrdinalIgnoreCase));
                        if (fPin is null) continue;
                        IrTypeRef fType = _typed.PinTypes.TryGetValue(fPin.Id, out var fpt)
                            ? fpt
                            : new IrTypeRef { FullName = NormalizeSharedTypeFqn(f.TypeId), IsUnmanaged = true, SizeBytes = 0 };
                        var fRes = AllocValue(fType);
                        stmts.Add(new IrStatement
                        {
                            ResultValue = fRes,
                            Operation   = new IrOp_FieldRead(compValM, f.Name, fType, SourceIsManaged: gcn.IsManaged),
                            Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = gcn.Id, PinId = fPin.Id },
                        });
                        _pinValueCache[fPin.Id] = fRes;
                    }

                    result = _pinValueCache.TryGetValue(sourcePinId, out var mpr) ? mpr : compValM;
                    break;
                }

                // Legacy single-field path (unchanged emit shape/behavior — Fields is null here).
                var compVal = AllocValue(componentTypeRef);
                stmts.Add(new IrStatement
                {
                    ResultValue = compVal,
                    Operation   = new IrOp_GetComponentRO(gcn.ComponentTypeFqn, entityValue, componentTypeRef),
                    Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = gcn.Id, PinId = sourcePinId },
                });

                var valuePin = gcn.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "Out"
                    && string.Equals(p.Name, "Value", StringComparison.OrdinalIgnoreCase));

                // Field result type: prefer the Stage4-resolved out-pin type when available
                // (mirrors GetSharedNode's valuePin.Id -> PinTypes lookup); else fall back to a
                // locally-built IrTypeRef from FieldTypeFqn when authored; else the generic
                // pinType already resolved for sourcePinId (UnknownType in the worst case).
                IrTypeRef fieldTypeRef = valuePin is not null
                    && _typed.PinTypes.TryGetValue(valuePin.Id, out var fvt)
                        ? fvt
                        : !string.IsNullOrEmpty(gcn.FieldTypeFqn)
                            ? new IrTypeRef { FullName = gcn.FieldTypeFqn, IsUnmanaged = true, SizeBytes = 4 }
                            : pinType;

                var fieldVal = AllocValue(fieldTypeRef);
                stmts.Add(new IrStatement
                {
                    ResultValue = fieldVal,
                    Operation   = new IrOp_FieldRead(compVal, gcn.FieldName, fieldTypeRef),
                    Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = gcn.Id, PinId = sourcePinId },
                });

                if (valuePin is not null) _pinValueCache[valuePin.Id] = fieldVal;

                result = fieldVal;
                break;
            }

            case ComponentItemGetNode cign:
            {
                // CA-07b -- reads one element off a component collection via its baked curated Item
                // accessor. "Collection" resolves to the source ENTITY the GetComponent
                // collection-decl branch cached there (Stage5's GetComponentNode case above) -- there
                // is no runtime "collection value", only the entity + the accessor pair. Unwired
                // Collection OR empty baked ComponentTypeFqn/ItemAccessorFqn => safe default (no read
                // emitted; a bare default(...) value, same shape the generic "unknown pure source"
                // fallback at the bottom of this switch uses).
                var collPin = cign.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "In"
                    && string.Equals(p.Name, "Collection", StringComparison.OrdinalIgnoreCase));
                var collLink = collPin is null ? null : _graph.Links.FirstOrDefault(
                    l => l.ToNodeId == cign.Id && l.ToPinId == collPin.Id);

                // CA-07d-2: managed collections bake CollectionFieldName (native member access) instead
                // of the curated ItemAccessorFqn -- the unbaked guard checks the kind's OWN required key.
                bool cignManaged = cign.CollectionKind == CollectionKind.ManagedMember;
                var cignListDecl = TryGetListVariableDecl(collLink);   // FC-2/LV-2: list-variable source
                if (collLink is null
                    || (cignListDecl is null
                        && (string.IsNullOrEmpty(cign.ComponentTypeFqn)
                            || (cignManaged ? string.IsNullOrEmpty(cign.CollectionFieldName)
                                            : string.IsNullOrEmpty(cign.ItemAccessorFqn)))))
                {
                    result = AllocValue(pinType);
                    stmts.Add(new IrStatement
                    {
                        ResultValue = result,
                        Operation   = new IrOp_Const("default", pinType),
                        Debug       = new IrDebugAnnotation
                        {
                            GraphId     = _graph.Id,
                            NodeId      = cign.Id,
                            PinId       = sourcePinId,
                            Synthesized = "component-item-get-unwired-or-unbaked",
                        },
                    });
                    break;
                }

                IrValue compVal;
                if (cignListDecl is not null)
                {
                    // FC-2/LV-2: bind a ref onto the state field -- no entity, no component re-read.
                    compVal = EmitListStateFieldRef(cignListDecl, stmts, cign.Id, sourcePinId);
                }
                else
                {
                    var entity = ResolveNodeOutput(collLink!.FromNodeId, collLink.FromPinId, stmts);

                    // CA-07d-2: managed component -> IrOp_GetManagedComponentRO (null-safe, IsUnmanaged=false),
                    // mirroring GetComponentNode's managed read; curated -> unchanged IrOp_GetComponentRO.
                    var compTypeRef = new IrTypeRef { FullName = cign.ComponentTypeFqn, IsUnmanaged = !cignManaged, SizeBytes = 0 };
                    compVal = AllocValue(compTypeRef);
                    stmts.Add(new IrStatement
                    {
                        ResultValue = compVal,
                        Operation   = cignManaged
                            ? new IrOp_GetManagedComponentRO(cign.ComponentTypeFqn, entity, compTypeRef)
                            : new IrOp_GetComponentRO(cign.ComponentTypeFqn, entity, compTypeRef),
                        Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = cign.Id, PinId = sourcePinId },
                    });
                }

                var indexPin = cign.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "In"
                    && string.Equals(p.Name, "Index", StringComparison.OrdinalIgnoreCase));
                IrValue indexVal = indexPin is not null
                    ? ResolveDataPin(cign.Id, indexPin.Id, stmts)
                    : AllocValue(Stage5_Schedule.Int32Type);

                var cignElemFqn = !string.IsNullOrEmpty(cign.ElementTypeFqn) ? cign.ElementTypeFqn
                    : cignListDecl is not null ? cignListDecl.Type.TypeId
                    : "System.Object";
                var elemTypeRef = new IrTypeRef
                {
                    FullName    = cignElemFqn,
                    IsUnmanaged = !cignManaged,
                    SizeBytes   = 0,
                };
                var elemVal = AllocValue(elemTypeRef);
                stmts.Add(new IrStatement
                {
                    ResultValue = elemVal,
                    Operation   = new IrOp_ComponentAccessorCall(
                        cign.ItemAccessorFqn, compVal, indexVal, elemTypeRef,
                        cignListDecl is not null ? CollectionKind.BlackboardFixedList : cign.CollectionKind,
                        cign.CollectionFieldName ?? "", cignElemFqn,
                        Capacity: cignListDecl?.Type.Capacity ?? 0),
                    Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = cign.Id, PinId = sourcePinId },
                });

                var elementPin = cign.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "Out"
                    && string.Equals(p.Name, "Element", StringComparison.OrdinalIgnoreCase));
                if (elementPin is not null) _pinValueCache[elementPin.Id] = elemVal;

                result = elemVal;
                break;
            }

            case ComponentItemCountNode cicn:
            {
                // CA-07b -- reads a component collection's Count via its baked curated accessor.
                // Mirrors ComponentItemGetNode's case above exactly, minus the Index operand.
                var collPin = cicn.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "In"
                    && string.Equals(p.Name, "Collection", StringComparison.OrdinalIgnoreCase));
                var collLink = collPin is null ? null : _graph.Links.FirstOrDefault(
                    l => l.ToNodeId == cicn.Id && l.ToPinId == collPin.Id);

                bool cicnManaged = cicn.CollectionKind == CollectionKind.ManagedMember;
                var cicnListDecl = TryGetListVariableDecl(collLink);   // FC-2/LV-2
                if (collLink is null
                    || (cicnListDecl is null
                        && (string.IsNullOrEmpty(cicn.ComponentTypeFqn)
                            || (cicnManaged ? string.IsNullOrEmpty(cicn.CollectionFieldName)
                                            : string.IsNullOrEmpty(cicn.CountAccessorFqn)))))
                {
                    result = AllocValue(pinType);
                    stmts.Add(new IrStatement
                    {
                        ResultValue = result,
                        Operation   = new IrOp_Const("default", pinType),
                        Debug       = new IrDebugAnnotation
                        {
                            GraphId     = _graph.Id,
                            NodeId      = cicn.Id,
                            PinId       = sourcePinId,
                            Synthesized = "component-item-count-unwired-or-unbaked",
                        },
                    });
                    break;
                }

                IrValue compVal;
                if (cicnListDecl is not null)
                {
                    compVal = EmitListStateFieldRef(cicnListDecl, stmts, cicn.Id, sourcePinId);
                }
                else
                {
                    var entity = ResolveNodeOutput(collLink!.FromNodeId, collLink.FromPinId, stmts);

                    var compTypeRef = new IrTypeRef { FullName = cicn.ComponentTypeFqn, IsUnmanaged = !cicnManaged, SizeBytes = 0 };
                    compVal = AllocValue(compTypeRef);
                    stmts.Add(new IrStatement
                    {
                        ResultValue = compVal,
                        Operation   = cicnManaged
                            ? new IrOp_GetManagedComponentRO(cicn.ComponentTypeFqn, entity, compTypeRef)
                            : new IrOp_GetComponentRO(cicn.ComponentTypeFqn, entity, compTypeRef),
                        Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = cicn.Id, PinId = sourcePinId },
                    });
                }

                var countVal = AllocValue(Stage5_Schedule.Int32Type);
                stmts.Add(new IrStatement
                {
                    ResultValue = countVal,
                    // Count shape (Index null). Managed passes CollectionFieldName + element type (to type
                    // the IReadOnlyList<TElem> local so a T[] field still exposes .Count).
                    Operation   = new IrOp_ComponentAccessorCall(
                        cicn.CountAccessorFqn, compVal, null, Stage5_Schedule.Int32Type,
                        cicnListDecl is not null ? CollectionKind.BlackboardFixedList : cicn.CollectionKind,
                        cicn.CollectionFieldName ?? "",
                        !string.IsNullOrEmpty(cicn.ElementTypeFqn) ? cicn.ElementTypeFqn!
                            : cicnListDecl is not null ? cicnListDecl.Type.TypeId : "System.Object",
                        Capacity: cicnListDecl?.Type.Capacity ?? 0),
                    Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = cicn.Id, PinId = sourcePinId },
                });

                var countPin = cicn.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "Out"
                    && string.Equals(p.Name, "Count", StringComparison.OrdinalIgnoreCase));
                if (countPin is not null) _pinValueCache[countPin.Id] = countVal;

                result = countVal;
                break;
            }

            case ComponentContainsNode ccn:
            {
                // CA-07d-1 -- linear search: does the collection contain the "Item" query value?
                // Resolves "Collection" -> entity -> re-read component EXACTLY like ComponentItemGetNode,
                // then emits a single IrOp_ComponentCollectionSearch (ContainsResult set). Unwired
                // Collection OR empty baked ComponentTypeFqn/Count/Item accessors => safe default (false).
                var collPin = ccn.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "In"
                    && string.Equals(p.Name, "Collection", StringComparison.OrdinalIgnoreCase));
                var collLink = collPin is null ? null : _graph.Links.FirstOrDefault(
                    l => l.ToNodeId == ccn.Id && l.ToPinId == collPin.Id);

                bool ccnManaged = ccn.CollectionKind == CollectionKind.ManagedMember;
                var ccnListDecl = TryGetListVariableDecl(collLink);   // FC-2/LV-2
                if (collLink is null
                    || (ccnListDecl is null
                        && (string.IsNullOrEmpty(ccn.ComponentTypeFqn)
                            || (ccnManaged ? string.IsNullOrEmpty(ccn.CollectionFieldName)
                                           : (string.IsNullOrEmpty(ccn.CountAccessorFqn)
                                              || string.IsNullOrEmpty(ccn.ItemAccessorFqn))))))
                {
                    result = AllocValue(pinType);
                    stmts.Add(new IrStatement
                    {
                        ResultValue = result,
                        Operation   = new IrOp_Const("default", pinType),
                        Debug       = new IrDebugAnnotation
                        {
                            GraphId = _graph.Id, NodeId = ccn.Id, PinId = sourcePinId,
                            Synthesized = "component-contains-unwired-or-unbaked",
                        },
                    });
                    break;
                }

                IrValue compVal;
                if (ccnListDecl is not null)
                {
                    compVal = EmitListStateFieldRef(ccnListDecl, stmts, ccn.Id, sourcePinId);
                }
                else
                {
                    var entity = ResolveNodeOutput(collLink!.FromNodeId, collLink.FromPinId, stmts);
                    var compTypeRef = new IrTypeRef { FullName = ccn.ComponentTypeFqn, IsUnmanaged = !ccnManaged, SizeBytes = 0 };
                    compVal = AllocValue(compTypeRef);
                    stmts.Add(new IrStatement
                    {
                        ResultValue = compVal,
                        Operation   = ccnManaged
                            ? new IrOp_GetManagedComponentRO(ccn.ComponentTypeFqn, entity, compTypeRef)
                            : new IrOp_GetComponentRO(ccn.ComponentTypeFqn, entity, compTypeRef),
                        Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = ccn.Id, PinId = sourcePinId },
                    });
                }

                var ccnElemFqn = !string.IsNullOrEmpty(ccn.ElementTypeFqn) ? ccn.ElementTypeFqn
                    : ccnListDecl is not null ? ccnListDecl.Type.TypeId
                    : "System.Object";
                var itemPin = ccn.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "In"
                    && string.Equals(p.Name, "Item", StringComparison.OrdinalIgnoreCase));
                var elemTypeRef = new IrTypeRef
                {
                    FullName    = ccnElemFqn,
                    IsUnmanaged = !ccnManaged, SizeBytes = 0,
                };
                IrValue queryVal = itemPin is not null
                    ? ResolveDataPin(ccn.Id, itemPin.Id, stmts)
                    : AllocValue(elemTypeRef);

                var boolVal = AllocValue(Stage5_Schedule.BoolType);
                stmts.Add(new IrStatement
                {
                    ResultValue = boolVal,
                    Operation   = new IrOp_ComponentCollectionSearch(
                        ccn.CountAccessorFqn, ccn.ItemAccessorFqn, ccnElemFqn,
                        compVal, queryVal, ContainsResult: boolVal,
                        Kind: ccnListDecl is not null ? CollectionKind.BlackboardFixedList : ccn.CollectionKind,
                        ManagedFieldName: ccn.CollectionFieldName ?? "",
                        Capacity: ccnListDecl?.Type.Capacity ?? 0),
                    Debug = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = ccn.Id, PinId = sourcePinId },
                });

                var resultPin = ccn.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "Out"
                    && string.Equals(p.Name, "Result", StringComparison.OrdinalIgnoreCase));
                if (resultPin is not null) _pinValueCache[resultPin.Id] = boolVal;

                result = boolVal;
                break;
            }

            case ComponentFindNode cfn:
            {
                // CA-07d-1 -- linear search returning the first index (Q#18-B: Index + Found out-pins).
                // Same Collection->entity->component resolution as ComponentContainsNode; one
                // IrOp_ComponentCollectionSearch sets BOTH FindIndex (int, -1 absent) and FindFound.
                var collPin = cfn.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "In"
                    && string.Equals(p.Name, "Collection", StringComparison.OrdinalIgnoreCase));
                var collLink = collPin is null ? null : _graph.Links.FirstOrDefault(
                    l => l.ToNodeId == cfn.Id && l.ToPinId == collPin.Id);

                bool cfnManaged = cfn.CollectionKind == CollectionKind.ManagedMember;
                var cfnListDecl = TryGetListVariableDecl(collLink);   // FC-2/LV-2
                if (collLink is null
                    || (cfnListDecl is null
                        && (string.IsNullOrEmpty(cfn.ComponentTypeFqn)
                            || (cfnManaged ? string.IsNullOrEmpty(cfn.CollectionFieldName)
                                           : (string.IsNullOrEmpty(cfn.CountAccessorFqn)
                                              || string.IsNullOrEmpty(cfn.ItemAccessorFqn))))))
                {
                    result = AllocValue(pinType);
                    stmts.Add(new IrStatement
                    {
                        ResultValue = result,
                        Operation   = new IrOp_Const("default", pinType),
                        Debug       = new IrDebugAnnotation
                        {
                            GraphId = _graph.Id, NodeId = cfn.Id, PinId = sourcePinId,
                            Synthesized = "component-find-unwired-or-unbaked",
                        },
                    });
                    break;
                }

                IrValue compVal;
                if (cfnListDecl is not null)
                {
                    compVal = EmitListStateFieldRef(cfnListDecl, stmts, cfn.Id, sourcePinId);
                }
                else
                {
                    var entity = ResolveNodeOutput(collLink!.FromNodeId, collLink.FromPinId, stmts);
                    var compTypeRef = new IrTypeRef { FullName = cfn.ComponentTypeFqn, IsUnmanaged = !cfnManaged, SizeBytes = 0 };
                    compVal = AllocValue(compTypeRef);
                    stmts.Add(new IrStatement
                    {
                        ResultValue = compVal,
                        Operation   = cfnManaged
                            ? new IrOp_GetManagedComponentRO(cfn.ComponentTypeFqn, entity, compTypeRef)
                            : new IrOp_GetComponentRO(cfn.ComponentTypeFqn, entity, compTypeRef),
                        Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = cfn.Id, PinId = sourcePinId },
                    });
                }

                var cfnElemFqn = !string.IsNullOrEmpty(cfn.ElementTypeFqn) ? cfn.ElementTypeFqn
                    : cfnListDecl is not null ? cfnListDecl.Type.TypeId
                    : "System.Object";
                var itemPin = cfn.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "In"
                    && string.Equals(p.Name, "Item", StringComparison.OrdinalIgnoreCase));
                var elemTypeRef = new IrTypeRef
                {
                    FullName    = cfnElemFqn,
                    IsUnmanaged = !cfnManaged, SizeBytes = 0,
                };
                IrValue queryVal = itemPin is not null
                    ? ResolveDataPin(cfn.Id, itemPin.Id, stmts)
                    : AllocValue(elemTypeRef);

                var indexVal = AllocValue(Stage5_Schedule.Int32Type);
                var foundVal = AllocValue(Stage5_Schedule.BoolType);
                stmts.Add(new IrStatement
                {
                    ResultValue = indexVal,
                    Operation   = new IrOp_ComponentCollectionSearch(
                        cfn.CountAccessorFqn, cfn.ItemAccessorFqn, cfnElemFqn,
                        compVal, queryVal, FindIndex: indexVal, FindFound: foundVal,
                        Kind: cfnListDecl is not null ? CollectionKind.BlackboardFixedList : cfn.CollectionKind,
                        ManagedFieldName: cfn.CollectionFieldName ?? "",
                        Capacity: cfnListDecl?.Type.Capacity ?? 0),
                    Debug = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = cfn.Id, PinId = sourcePinId },
                });

                var indexPin = cfn.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "Out"
                    && string.Equals(p.Name, "Index", StringComparison.OrdinalIgnoreCase));
                var foundPin = cfn.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "Out"
                    && string.Equals(p.Name, "Found", StringComparison.OrdinalIgnoreCase));
                if (indexPin is not null) _pinValueCache[indexPin.Id] = indexVal;
                if (foundPin is not null) _pinValueCache[foundPin.Id] = foundVal;

                result = _pinValueCache.TryGetValue(sourcePinId, out var cfr) ? cfr : indexVal;
                break;
            }

            case CompareNode cn:
            {
                // GAP-12 -- native comparison node. Pure data, mirrors GetComponentNode/LiteralNode
                // above: find the "A"/"B" data-in pins and the "Result" data-out pin by NAME off the
                // asset-authored Pins (registry returns Array.Empty -- Stage0 leaves the authored
                // pins alone), resolve the two operands via ResolveDataPin (same as BranchNode's
                // condPin resolution), then lower to the NEW IrOp_Compare into a fresh BoolType
                // value. No StaticTypeRegistry involvement -- A/B's types flow from their wired
                // sources (reflection-free by construction, per the operand contract in the design
                // doc).
                var aPin = cn.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "In"
                    && string.Equals(p.Name, "A", StringComparison.OrdinalIgnoreCase));
                var bPin = cn.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "In"
                    && string.Equals(p.Name, "B", StringComparison.OrdinalIgnoreCase));
                var resultPin = cn.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "Out"
                    && string.Equals(p.Name, "Result", StringComparison.OrdinalIgnoreCase));

                IrValue aVal = aPin is not null
                    ? ResolveDataPin(cn.Id, aPin.Id, stmts)
                    : AllocValue(Stage5_Schedule.UnknownType);
                IrValue bVal = bPin is not null
                    ? ResolveDataPin(cn.Id, bPin.Id, stmts)
                    : AllocValue(Stage5_Schedule.UnknownType);

                var cmpResult = AllocValue(Stage5_Schedule.BoolType);
                stmts.Add(new IrStatement
                {
                    ResultValue = cmpResult,
                    Operation   = new IrOp_Compare(aVal, bVal, cn.Operator),
                    Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = cn.Id, PinId = sourcePinId },
                });

                if (resultPin is not null) _pinValueCache[resultPin.Id] = cmpResult;

                result = cmpResult;
                break;
            }

            case BinaryOpNode bo:
            {
                // Native arithmetic node (Compare's arithmetic sibling). Pure data, mirrors the
                // CompareNode case above exactly: find the "A"/"B" data-in pins and the "Result"
                // data-out pin by NAME off the asset-authored Pins (registry returns Array.Empty --
                // Stage0 leaves the authored pins alone), resolve the two operands via
                // ResolveDataPin, then lower to the NEW IrOp_BinaryOp. Unlike CompareNode, the
                // result value is typed = A's operand type (NOT BoolType) -- `a + b` on type T
                // yields T. No StaticTypeRegistry involvement -- A/B's types flow from their wired
                // sources (reflection-free by construction).
                var aPin = bo.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "In"
                    && string.Equals(p.Name, "A", StringComparison.OrdinalIgnoreCase));
                var bPin = bo.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "In"
                    && string.Equals(p.Name, "B", StringComparison.OrdinalIgnoreCase));
                var resultPin = bo.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "Out"
                    && string.Equals(p.Name, "Result", StringComparison.OrdinalIgnoreCase));

                IrValue aVal = aPin is not null
                    ? ResolveDataPin(bo.Id, aPin.Id, stmts)
                    : AllocValue(Stage5_Schedule.UnknownType);
                IrValue bVal = bPin is not null
                    ? ResolveDataPin(bo.Id, bPin.Id, stmts)
                    : AllocValue(Stage5_Schedule.UnknownType);

                var binOpResult = AllocValue(aVal.Type);
                stmts.Add(new IrStatement
                {
                    ResultValue = binOpResult,
                    Operation   = new IrOp_BinaryOp(aVal, bVal, bo.Operator),
                    Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = bo.Id, PinId = sourcePinId },
                });

                if (resultPin is not null) _pinValueCache[resultPin.Id] = binOpResult;

                result = binOpResult;
                break;
            }

            case BooleanOpNode boolOp:
            {
                // Native boolean logic node (Compare's boolean sibling). Pure data, mirrors the
                // CompareNode case above exactly: find the "A"/"B" data-in pins and the "Result"
                // data-out pin by NAME off the asset-authored Pins (registry returns Array.Empty --
                // Stage0 leaves the authored pins alone), resolve the two operands via
                // ResolveDataPin, then lower to the NEW IrOp_BooleanOp into a fresh BoolType value
                // (like CompareNode, NOT the operand type -- though the operand type is already
                // bool here). No StaticTypeRegistry involvement -- A/B's types flow from their
                // wired sources (reflection-free by construction). No short-circuit: both operands
                // are resolved as values before the And/Or combines them.
                var aPin = boolOp.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "In"
                    && string.Equals(p.Name, "A", StringComparison.OrdinalIgnoreCase));
                var bPin = boolOp.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "In"
                    && string.Equals(p.Name, "B", StringComparison.OrdinalIgnoreCase));
                var resultPin = boolOp.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "Out"
                    && string.Equals(p.Name, "Result", StringComparison.OrdinalIgnoreCase));

                IrValue aVal = aPin is not null
                    ? ResolveDataPin(boolOp.Id, aPin.Id, stmts)
                    : AllocValue(Stage5_Schedule.UnknownType);
                IrValue bVal = bPin is not null
                    ? ResolveDataPin(boolOp.Id, bPin.Id, stmts)
                    : AllocValue(Stage5_Schedule.UnknownType);

                var boolOpResult = AllocValue(Stage5_Schedule.BoolType);
                stmts.Add(new IrStatement
                {
                    ResultValue = boolOpResult,
                    Operation   = new IrOp_BooleanOp(aVal, bVal, boolOp.Operator),
                    Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = boolOp.Id, PinId = sourcePinId },
                });

                if (resultPin is not null) _pinValueCache[resultPin.Id] = boolOpResult;

                result = boolOpResult;
                break;
            }

            case NotNode notNode:
            {
                // Native unary boolean negation node (Compare's boolean sibling). Pure data,
                // mirrors the CompareNode/BooleanOpNode cases above but with a SINGLE operand: find
                // the "A" data-in pin and the "Result" data-out pin by NAME off the asset-authored
                // Pins (registry returns Array.Empty -- Stage0 leaves the authored pins alone),
                // resolve the operand via ResolveDataPin, then lower to the NEW IrOp_Not into a
                // fresh BoolType value. No operator enum -- unary negation only.
                var aPin = notNode.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "In"
                    && string.Equals(p.Name, "A", StringComparison.OrdinalIgnoreCase));
                var resultPin = notNode.Pins.FirstOrDefault(p =>
                    !p.IsExec && p.Direction == "Out"
                    && string.Equals(p.Name, "Result", StringComparison.OrdinalIgnoreCase));

                IrValue aVal = aPin is not null
                    ? ResolveDataPin(notNode.Id, aPin.Id, stmts)
                    : AllocValue(Stage5_Schedule.UnknownType);

                var notResult = AllocValue(Stage5_Schedule.BoolType);
                stmts.Add(new IrStatement
                {
                    ResultValue = notResult,
                    Operation   = new IrOp_Not(aVal),
                    Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = notNode.Id, PinId = sourcePinId },
                });

                if (resultPin is not null) _pinValueCache[resultPin.Id] = notResult;

                result = notResult;
                break;
            }

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

                // BP-73: multi-output pure call. ResolveNodeOutput is asked for ONE source pin at a
                // time, so the call is emitted once and every out-pin's extraction is cached by
                // EmitCarrierFanOut; the pin actually requested is then read straight back out of
                // that cache. A second out-pin resolved later hits _statementPinCache at the top of
                // this method and never re-emits the call.
                if (pureTargetGraph.Outputs.Count > 1)
                {
                    var pureOutPins = sourceNode.Pins
                        .Where(p => !p.IsExec && p.Direction == "Out").ToList();

                    var pureCarrier = AllocValue(Stage5_Schedule.UnknownType);
                    stmts.Add(new IrStatement
                    {
                        ResultValue = pureCarrier,
                        Operation   = new IrOp_GraphCall(
                            pureGcGuid, pureGcArgs, Stage5_Schedule.UnknownType),
                        Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = fc.Id },
                    });
                    EmitCarrierFanOut(pureCarrier, pureOutPins, pureTargetGraph, stmts, sourceNode);

                    if (_statementPinCache.TryGetValue(sourcePinId, out var fanned))
                        return fanned;

                    // Requested pin is not among the projected out-pins (stale asset): fall back to a
                    // declared default so the generated C# still compiles.
                    result = AllocValue(pinType);
                    stmts.Add(new IrStatement
                    {
                        ResultValue = result,
                        Operation   = new IrOp_Const("default", pinType),
                        Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = fc.Id },
                    });
                    break;
                }

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
                var (pureAppendSelf, pureAppendView) =
                    ResolveFunctionCallTrailingContext(fc);
                stmts.Add(new IrStatement
                {
                    ResultValue = result,
                    Operation   = new IrOp_PureCall(
                        $"{fc.TargetTypeId}.{fc.MethodName}", inputArgs, pinType,
                        pureAppendSelf, pureAppendView),
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
    // P7: trailing engine-context recognition (FunctionCall context-aware args)
    // -----------------------------------------------------------------------

    /// <summary>
    /// P7 -- resolves whether a FunctionCall's target CLR method ends with the recognized
    /// trailing engine-context parameters (<c>Entity self</c> / <c>ISimulationView view</c>; see
    /// <see cref="ResolveTrailingContext"/>) that <see cref="StatementEmitter"/> must append to the
    /// emitted call. The node's data-IN pins already OMIT these trailing parameters (Stage0's
    /// <c>EnrichClrFunctionCallPins</c>/the editor's <c>NodePinSchema.FunctionCallPins</c> --
    /// kept in parity), so <see cref="ResolveAllDataInputs"/>'s result never includes them; this
    /// method decides only whether to append <c>self</c>/the read-only view as EXTRA trailing
    /// arguments at emit time.
    /// <para>
    /// P7.1 -- <see cref="FunctionCallNode.TrailingContext"/> is checked FIRST: when the author (or
    /// editor bake step) has recorded an explicit non-<see cref="FunctionCallContextKind.Unspecified"/>
    /// value, it is honored directly with NO reflection, mapping straight to (AppendSelf,
    /// AppendView). This is what lets a hand-authored/baked FunctionCall survive the real MSBuild
    /// build: the Roslyn source generator runs as a netstandard2.0 analyzer that cannot load
    /// arbitrary game assemblies (e.g. <c>Hrot.AI.Behaviors.dll</c>), so the CLR-reflection fallback
    /// below always resolves null there, silently dropping self/view and producing uncompilable C#
    /// (CS7036: missing required parameter 'self'). Only when <c>TrailingContext</c> is
    /// <see cref="FunctionCallContextKind.Unspecified"/> (legacy/in-process-authored nodes --
    /// including every existing P7 test's programmatically-built <see cref="FunctionCallNode"/>) does
    /// this method fall back to the original reflection-based resolution, unchanged.
    /// </para>
    /// <para>
    /// Gated off for a Library-dispatch asset -- <see cref="EmissionContext.HasSelfInScope"/>
    /// is false there (no <c>self</c>/<c>view</c> local in the generated stateless static method),
    /// so appending either would emit an undefined-identifier reference. In that case this method
    /// always returns <c>(false, false)</c> regardless of a baked <c>TrailingContext</c>; a trailing
    /// Entity/ISimulationView-typed parameter is therefore left as an ordinary (already-resolved)
    /// positional data argument, matching pre-P7 behavior byte-for-byte (see the recorded gap in the
    /// P7 report for why no diagnostic is raised for this edge case).
    /// </para>
    /// Returns <c>(false, false)</c> when <c>TrailingContext</c> is Unspecified and the method
    /// cannot be resolved via reflection (graceful -- matches the existing CLR-reflection
    /// NO-SWALLOW fallback elsewhere; no context can be inferred without the method's actual
    /// signature).
    /// </summary>
    private (bool AppendSelf, bool AppendView) ResolveFunctionCallTrailingContext(FunctionCallNode fc)
    {
        if (_typed.Asset.Dispatch == AssetDispatchKind.Library)
            return (false, false);

        // P7.1 -- baked decision wins over reflection; no reflection attempted at all.
        switch (fc.TrailingContext)
        {
            case FunctionCallContextKind.None:        return (false, false);
            case FunctionCallContextKind.Self:         return (true, false);
            case FunctionCallContextKind.View:         return (false, true);
            case FunctionCallContextKind.SelfAndView:  return (true, true);
            case FunctionCallContextKind.Unspecified:
            default:
                break; // fall through to the legacy reflection path below.
        }

        var method = ResolveClrMethodForContext(fc.TargetTypeId, fc.MethodName);
        if (method is null)
            return (false, false);

        var (_, appendSelf, appendView) = ResolveTrailingContext(method.GetParameters());
        return (appendSelf, appendView);
    }

    /// <summary>
    /// P7 -- resolves a CLR method by declaring-type FQN + method name across loaded assemblies,
    /// for trailing-context recognition only. Mirrors the reflection idiom already used by
    /// <c>Stage0_Rehydrate.ResolveMethod</c>/<c>NodePinSchema.ResolveMethod</c> (kept in parity;
    /// not shared directly -- Stage5_Schedule has no reference to either of those internal
    /// helpers' owning types). Returns null (graceful) when the type/method cannot be resolved.
    /// </summary>
    private static System.Reflection.MethodInfo? ResolveClrMethodForContext(
        string targetTypeId, string methodName)
    {
        if (string.IsNullOrEmpty(targetTypeId) || string.IsNullOrEmpty(methodName))
            return null;

        System.Type? type = System.Type.GetType(targetTypeId, throwOnError: false);
        if (type is null)
        {
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    type = asm.GetType(targetTypeId, throwOnError: false);
                    if (type is not null) break;
                }
                catch
                {
                    // Ignore assemblies that fail type resolution.
                }
            }
        }
        if (type is null) return null;

        try
        {
            return type.GetMethods(
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == methodName);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// P7 -- recognizes the trailing engine-context parameter convention on a FunctionCall's
    /// resolved CLR method. The parameter list MAY end with <c>Entity self</c>, or an
    /// <c>ISimulationView</c>-typed parameter (any name), or both in that exact order
    /// (<c>..., Entity self, ISimulationView &lt;name&gt;</c> -- mirrors the parameter order the
    /// compiler itself uses for generated methods, e.g. <c>TickCore(..., self, world, time)</c>).
    /// <para>
    /// Recognition is by TYPE (exact <c>Type.FullName</c> match against
    /// <c>Fdp.Core.Entity</c> / <c>Fdp.ModuleHost.Abstractions.ISimulationView</c>, after stripping
    /// a by-ref wrapper). The <c>Entity</c> case ALSO requires the parameter be named exactly
    /// <c>"self"</c> (ordinal) -- <c>Entity</c> is a legitimate ordinary data-pin type elsewhere
    /// (e.g. <see cref="GetSharedNode"/>'s "Target" pin), so the name disambiguates a genuine
    /// trailing self-context parameter from an author-supplied <c>Entity</c> data argument.
    /// <c>ISimulationView</c> has no legitimate ordinary blueprint-data use, so type alone suffices.
    /// </para>
    /// Kept in parity with <c>Stage0_Rehydrate.ResolveTrailingContext</c> (compiler pin rehydration)
    /// and <c>NodePinSchema.ResolveTrailingContext</c> (editor pin projection) -- all three must
    /// agree on which trailing parameters are "context" so the omitted pin count always matches
    /// the appended call-argument count.
    /// </summary>
    private static (int ContextCount, bool AppendSelf, bool AppendView) ResolveTrailingContext(
        System.Reflection.ParameterInfo[] parameters)
    {
        const string EntityFqn = "Fdp.Core.Entity";
        const string ViewFqn   = "Fdp.ModuleHost.Abstractions.ISimulationView";

        int n = parameters.Length;
        if (n == 0) return (0, false, false);

        static System.Type StripByRef(System.Type t) => t.IsByRef ? (t.GetElementType() ?? t) : t;
        bool IsSelfParam(System.Reflection.ParameterInfo p) =>
            StripByRef(p.ParameterType).FullName == EntityFqn
            && string.Equals(p.Name, "self", StringComparison.Ordinal);
        bool IsViewParam(System.Reflection.ParameterInfo p) =>
            StripByRef(p.ParameterType).FullName == ViewFqn;

        if (IsViewParam(parameters[n - 1]))
        {
            if (n >= 2 && IsSelfParam(parameters[n - 2]))
                return (2, true, true);
            return (1, false, true);
        }

        if (IsSelfParam(parameters[n - 1]))
            return (1, true, false);

        return (0, false, false);
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

            // netstandard2.0 has no string.Contains(string, StringComparison); IndexOf is equivalent.
            if (pin.Name.IndexOf("True", StringComparison.OrdinalIgnoreCase) >= 0)
                trueSucc = target;
            else
                falseSucc = target;
        }
        return (trueSucc, falseSucc);
    }

    // -----------------------------------------------------------------------
    // FlowForEach (P1 -- GAP-1) inline loop scheduling
    // -----------------------------------------------------------------------

    /// <summary>
    /// P1a: schedules a <see cref="FlowForEachNode"/> as an inline bounded loop. Emits (into the
    /// CURRENT block) the reflection-free self/roster read (<see cref="IrOp_Self"/> +
    /// <see cref="IrOp_GetComponentRO"/> on the baked <c>SourceComponentFqn</c> -- reusing P2's exact
    /// pattern), then schedules the "Body" exec-chain INLINE into a nested statement list (NOT the BFS
    /// block queue) with the "CurrentItem" out-pin bound to the per-iteration item value, then emits
    /// <see cref="IrOp_ForEach"/>. The caller (<c>ScheduleBlock</c>) continues the outer chain at
    /// "Completed" in the same block. The body must be branch-free + latent-free (Stage2 BP2050).
    /// </summary>
    private void ScheduleFlowForEachNode(FlowForEachNode fe, BlockBuilder bb)
    {
        // self + roster component read (reuse P2 machinery), into the OUTER block, before the loop.
        var selfVal = AllocValue(Stage5_Schedule.EntityType);
        bb.Statements.Add(new IrStatement
        {
            ResultValue = selfVal,
            Operation   = new IrOp_Self(),
            Debug       = DebugOf(fe),
        });
        var rosterTypeRef = new IrTypeRef { FullName = fe.SourceComponentFqn, IsUnmanaged = true, SizeBytes = 0 };
        var rosterVal = AllocValue(rosterTypeRef);
        bb.Statements.Add(new IrStatement
        {
            ResultValue = rosterVal,
            Operation   = new IrOp_GetComponentRO(fe.SourceComponentFqn, selfVal, rosterTypeRef),
            Debug       = DebugOf(fe),
        });

        // Per-iteration item value -- declared INSIDE the emitted for by IrOp_ForEach (no defining
        // statement of its own here).
        var itemVar = AllocValue(Stage5_Schedule.EntityType);

        // Optional "Count" out-pin -> loop-invariant element count, hoisted to an OUTER-scope local by
        // IrOp_ForEach's emit. Bound BEFORE the body-cache snapshot so it survives body-scope cleanup
        // (the count is valid inside the body AND in the "Completed" chain after the loop).
        var countPin = fe.Pins.FirstOrDefault(p =>
            !p.IsExec && p.Direction == "Out"
            && string.Equals(p.Name, "Count", StringComparison.OrdinalIgnoreCase));
        IrValue? countVar = null;
        if (countPin is not null)
        {
            var cv = AllocValue(Stage5_Schedule.Int32Type);
            countVar = cv;
            _pinValueCache[countPin.Id] = cv;
        }

        // Bind "CurrentItem" + optional "CurrentIndex" out-pins, then schedule the Body exec-chain
        // INLINE into a nested statement list. Cache isolation: snapshot the pin-value cache keys, and
        // after the body remove every entry added during body scheduling (incl. CurrentItem/Index) so
        // body-scoped values -- which depend on the changing loop var -- never leak to the outer scope.
        var currentItemPin = fe.Pins.FirstOrDefault(p =>
            !p.IsExec && p.Direction == "Out"
            && string.Equals(p.Name, "CurrentItem", StringComparison.OrdinalIgnoreCase));
        var currentIndexPin = fe.Pins.FirstOrDefault(p =>
            !p.IsExec && p.Direction == "Out"
            && string.Equals(p.Name, "CurrentIndex", StringComparison.OrdinalIgnoreCase));

        var bodyStmts = new List<IrStatement>();
        var savedKeys = new HashSet<Guid>(_pinValueCache.Keys);
        if (currentItemPin is not null)
            _pinValueCache[currentItemPin.Id] = itemVar;
        IrValue? indexVar = null;
        if (currentIndexPin is not null)
        {
            var iv = AllocValue(Stage5_Schedule.Int32Type);
            indexVar = iv;
            _pinValueCache[currentIndexPin.Id] = iv;
        }

        // P1b (GAP-1): schedule the Body exec-chain inline; an in-body Branch lowers to a nested
        // IrOp_If (see ScheduleInlineBodyChain), NOT a BFS block split -- an inline for-body cannot
        // span blocks. Latent nodes remain forbidden in the body (Stage2 BP2050).
        ScheduleInlineBodyChain(GetExecSuccessorByPinName(fe, "Body"), null, bodyStmts, bb.Id.Value);

        foreach (var k in _pinValueCache.Keys.Where(k => !savedKeys.Contains(k)).ToList())
            _pinValueCache.Remove(k);

        bb.Statements.Add(new IrStatement
        {
            Operation = new IrOp_ForEach(
                fe.CountAccessorFqn, fe.ItemAccessorFqn, rosterVal, itemVar, bodyStmts, countVar, indexVar),
            Debug = DebugOf(fe),
        });
    }

    // -----------------------------------------------------------------------
    // ComponentForEach (CA-07b) inline loop scheduling
    // -----------------------------------------------------------------------

    /// <summary>
    /// CA-07b: schedules a <see cref="ComponentForEachNode"/> as an inline bounded loop -- copies
    /// <see cref="ScheduleFlowForEachNode"/>'s shape EXACTLY (same <see cref="IrOp_ForEach"/>,
    /// unchanged; same <see cref="ScheduleInlineBodyChain"/> body scheduling; same
    /// CurrentItem/CurrentIndex/Count pin binding + body-cache isolation snapshot), with THREE
    /// differences: (a) the component is re-read off the ENTITY the wired "Collection" data-in pin
    /// resolves to (see the section doc comment on <see cref="Assets.ComponentForEachNode"/> in
    /// Nodes.cs) instead of <c>self</c>; (b) <c>itemVar</c> is allocated with the node's OWN baked
    /// <see cref="ComponentForEachNode.ElementTypeFqn"/> (falls back to System.Object), not the
    /// fixed <c>Fdp.Core.Entity</c> FlowForEach always uses; (c) the accessor FQNs come from this
    /// node's OWN baked <see cref="ComponentForEachNode.CountAccessorFqn"/>/
    /// <see cref="ComponentForEachNode.ItemAccessorFqn"/> instead of a fixed roster contract.
    /// <para>
    /// "Collection" unwired, OR the node's baked ComponentTypeFqn/CountAccessorFqn/ItemAccessorFqn
    /// are empty (not yet baked by CA-07c at wire time): safe default -- NOTHING is emitted (no
    /// read, no <see cref="IrOp_ForEach"/>) and this method returns immediately. The "Body" chain
    /// simply never runs (an empty loop); the caller (<c>ScheduleBlock</c>) still continues the
    /// outer chain at "Completed" as normal -- no diagnostic, no crash, mirrors how an unconnected
    /// OPTIONAL pin degrades elsewhere in this file (Stage2's <c>V_ComponentAccessRules</c> BP2066
    /// catches the "wired but not baked" half of this at validation time; unwired is legitimately
    /// just "not used yet").
    /// </para>
    /// </summary>

    /// <summary>
    /// FC-2/LV-2 (Q#19-A/F1) -- when the "Collection" wire's PRODUCER is a <see cref="GetVariableNode"/>
    /// referencing a FIXED-LIST variable (Capacity &gt; 0, in Variables or WorkingState), returns that
    /// declaration; else null. Producer-driven (robust even for a hand-authored consumer whose
    /// CollectionKind was never baked) -- the baked Kind/CollectionFieldName serve the editor/Stage2
    /// gates, the WIRE is the source of truth here.
    /// </summary>
    private VariableDecl? TryGetListVariableDecl(Link? collLink)
    {
        if (collLink is null) return null;
        if (!_nodeById.TryGetValue(collLink.FromNodeId, out var n) || n is not GetVariableNode gv) return null;
        var vid = gv.VariableId ?? "";
        if (vid.StartsWith("var:", StringComparison.Ordinal)) vid = vid.Substring(4);
        if (!Guid.TryParse(vid, out var id)) return null;
        var decl = _typed.Asset.Variables.FirstOrDefault(v => v.Id == id)
                ?? _typed.Asset.WorkingState.FirstOrDefault(v => v.Id == id);
        return decl is { Type.Capacity: > 0 } ? decl : null;
    }

    /// <summary>
    /// FC-2/LV-3 -- resolves a <see cref="ListWriteNode.VariableId"/> ("var:"-prefix tolerated)
    /// to its FIXED-LIST declaration (Capacity &gt; 0, Variables or WorkingState); else null.
    /// </summary>
    private VariableDecl? TryGetListVariableDeclById(string? variableId)
    {
        var vid = variableId ?? "";
        if (vid.StartsWith("var:", StringComparison.Ordinal)) vid = vid.Substring(4);
        if (!Guid.TryParse(vid, out var id)) return null;
        var decl = _typed.Asset.Variables.FirstOrDefault(v => v.Id == id)
                ?? _typed.Asset.WorkingState.FirstOrDefault(v => v.Id == id);
        return decl is { Type.Capacity: > 0 } ? decl : null;
    }

    /// <summary>
    /// FC-2/LV-2 -- emits the <see cref="IrOp_StateFieldRef"/> binding a writable `ref` local onto the
    /// list variable's state field; the collection consumers use the returned value exactly where the
    /// component path uses its <c>IrOp_GetComponentRO</c> roster value.
    /// </summary>
    private IrValue EmitListStateFieldRef(VariableDecl decl, List<IrStatement> stmts, Guid nodeId, Guid pinId)
    {
        var listType = _typed.FieldTypes.TryGetValue(decl.Id, out var lt)
            ? lt
            : new IrTypeRef { FullName = "__List_Unresolved", IsUnmanaged = true, SizeBytes = 0, Capacity = decl.Type.Capacity };
        var refVal = AllocValue(listType);
        stmts.Add(new IrStatement
        {
            ResultValue = refVal,
            Operation   = new IrOp_StateFieldRef(decl.Name, listType),
            Debug       = new IrDebugAnnotation { GraphId = _graph.Id, NodeId = nodeId, PinId = pinId },
        });
        return refVal;
    }

    private void ScheduleComponentForEachNode(ComponentForEachNode cfe, BlockBuilder bb)
    {
        var collPin = cfe.Pins.FirstOrDefault(p =>
            !p.IsExec && p.Direction == "In"
            && string.Equals(p.Name, "Collection", StringComparison.OrdinalIgnoreCase));
        var collLink = collPin is null ? null : _graph.Links.FirstOrDefault(
            l => l.ToNodeId == cfe.Id && l.ToPinId == collPin.Id);

        bool cfeManaged = cfe.CollectionKind == CollectionKind.ManagedMember;
        var cfeListDecl = TryGetListVariableDecl(collLink);   // FC-2/LV-2: list-variable source
        if (collLink is null
            || (cfeListDecl is null
                && (string.IsNullOrEmpty(cfe.ComponentTypeFqn)
                    || (cfeManaged ? string.IsNullOrEmpty(cfe.CollectionFieldName)
                                   : (string.IsNullOrEmpty(cfe.CountAccessorFqn)
                                      || string.IsNullOrEmpty(cfe.ItemAccessorFqn))))))
        {
            return;
        }

        IrValue compVal;
        if (cfeListDecl is not null)
        {
            // FC-2/LV-2: bind a ref onto the state field (no entity, no component re-read).
            compVal = EmitListStateFieldRef(cfeListDecl, bb.Statements, cfe.Id, Guid.Empty);
        }
        else
        {
            // Resolve "Collection" to the source ENTITY (cached there by Stage5's GetComponentNode
            // case, collection-decl branch), into the OUTER block, before the loop -- mirrors
            // ScheduleFlowForEachNode's self+roster read, but off the resolved entity instead of self.
            var entityVal = ResolveNodeOutput(collLink!.FromNodeId, collLink.FromPinId, bb.Statements);

            // CA-07d-2: managed -> IrOp_GetManagedComponentRO (null-safe), curated -> IrOp_GetComponentRO.
            var compTypeRef = new IrTypeRef { FullName = cfe.ComponentTypeFqn, IsUnmanaged = !cfeManaged, SizeBytes = 0 };
            compVal = AllocValue(compTypeRef);
            bb.Statements.Add(new IrStatement
            {
                ResultValue = compVal,
                Operation   = cfeManaged
                    ? new IrOp_GetManagedComponentRO(cfe.ComponentTypeFqn, entityVal, compTypeRef)
                    : new IrOp_GetComponentRO(cfe.ComponentTypeFqn, entityVal, compTypeRef),
                Debug       = DebugOf(cfe),
            });
        }

        // Per-iteration item value, ELEMENT-typed (not Fdp.Core.Entity -- the one FlowForEach-shape
        // difference besides the entity source) -- declared INSIDE the emitted for by IrOp_ForEach.
        var elemTypeRef = new IrTypeRef
        {
            FullName    = !string.IsNullOrEmpty(cfe.ElementTypeFqn) ? cfe.ElementTypeFqn
                : cfeListDecl is not null ? cfeListDecl.Type.TypeId
                : "System.Object",
            IsUnmanaged = true,
            SizeBytes   = 0,
        };
        var itemVar = AllocValue(elemTypeRef);

        // Optional "Count" out-pin -- identical shape to ScheduleFlowForEachNode's countPin handling.
        var countPin = cfe.Pins.FirstOrDefault(p =>
            !p.IsExec && p.Direction == "Out"
            && string.Equals(p.Name, "Count", StringComparison.OrdinalIgnoreCase));
        IrValue? countVar = null;
        if (countPin is not null)
        {
            var cv = AllocValue(Stage5_Schedule.Int32Type);
            countVar = cv;
            _pinValueCache[countPin.Id] = cv;
        }

        // Bind "CurrentItem" + optional "CurrentIndex" out-pins, then schedule the Body exec-chain
        // INLINE -- identical cache-isolation shape to ScheduleFlowForEachNode.
        var currentItemPin = cfe.Pins.FirstOrDefault(p =>
            !p.IsExec && p.Direction == "Out"
            && string.Equals(p.Name, "CurrentItem", StringComparison.OrdinalIgnoreCase));
        var currentIndexPin = cfe.Pins.FirstOrDefault(p =>
            !p.IsExec && p.Direction == "Out"
            && string.Equals(p.Name, "CurrentIndex", StringComparison.OrdinalIgnoreCase));

        var bodyStmts = new List<IrStatement>();
        var savedKeys = new HashSet<Guid>(_pinValueCache.Keys);
        if (currentItemPin is not null)
            _pinValueCache[currentItemPin.Id] = itemVar;
        IrValue? indexVar = null;
        if (currentIndexPin is not null)
        {
            var iv = AllocValue(Stage5_Schedule.Int32Type);
            indexVar = iv;
            _pinValueCache[currentIndexPin.Id] = iv;
        }

        ScheduleInlineBodyChain(GetExecSuccessorByPinName(cfe, "Body"), null, bodyStmts, bb.Id.Value);

        foreach (var k in _pinValueCache.Keys.Where(k => !savedKeys.Contains(k)).ToList())
            _pinValueCache.Remove(k);

        // Reuses IrOp_ForEach UNCHANGED -- it only needs a component ref-readonly local
        // (compVal, from IrOp_GetComponentRO -- doesn't care which entity produced it), the
        // Count/Item accessor FQNs, and an ItemVar of the right type. None of that is
        // FlowForEach-specific.
        bb.Statements.Add(new IrStatement
        {
            Operation = new IrOp_ForEach(
                cfe.CountAccessorFqn, cfe.ItemAccessorFqn, compVal, itemVar, bodyStmts, countVar, indexVar,
                Kind: cfeListDecl is not null ? CollectionKind.BlackboardFixedList : cfe.CollectionKind,
                ManagedFieldName: cfe.CollectionFieldName ?? "",
                Capacity: cfeListDecl?.Type.Capacity ?? 0),
            Debug = DebugOf(cfe),
        });
    }

    /// <summary>
    /// P1b (GAP-1): schedules an inline exec-chain -- a <see cref="FlowForEachNode"/> body, or a
    /// <see cref="BranchNode"/> arm within one -- into <paramref name="stmts"/> as NESTED statements,
    /// walking exec successors until it reaches <paramref name="stopAtNodeId"/> (the branch join) or
    /// the chain ends. A <see cref="BranchNode"/> lowers to a nested <see cref="IrOp_If"/> (NOT a BFS
    /// block split -- an inline for-body cannot span blocks): the condition is resolved into the
    /// CURRENT scope (before the if), each arm is scheduled inline up to the branch's immediate join,
    /// and the outer chain resumes ONCE at that join. All visited nodes are mapped to
    /// <paramref name="blockId"/> (the loop's owning block) for debug attribution -- mirroring P1a's
    /// body walk, which likewise emits no per-node probe anchors inside the loop body.
    /// </summary>
    private void ScheduleInlineBodyChain(
        Node? node, Guid? stopAtNodeId, List<IrStatement> stmts, int blockId)
    {
        while (node is not null && node.Id != stopAtNodeId)
        {
            if (node is BranchNode bn)
            {
                _execNodeToBlockId[bn.Id] = blockId;

                // Resolve the Branch condition into the CURRENT (enclosing) scope, before the if.
                var condPin = bn.Pins.FirstOrDefault(p => !p.IsExec && p.Direction == "In");
                IrValue condValue;
                if (condPin is not null)
                    condValue = ResolveDataPin(bn.Id, condPin.Id, stmts);
                else
                {
                    condValue = AllocValue(Stage5_Schedule.BoolType);
                    stmts.Add(new IrStatement
                    {
                        ResultValue = condValue,
                        Operation   = new IrOp_Const("false", Stage5_Schedule.BoolType),
                        Debug       = DebugOf(bn),
                    });
                }

                var (trueSucc, falseSucc) = GetBranchSuccessors(bn);
                var joinId = FindInlineBranchJoin(trueSucc, falseSucc);

                var thenStmts = ScheduleInlineArm(trueSucc,  joinId, blockId);
                var elseStmts = ScheduleInlineArm(falseSucc, joinId, blockId);

                stmts.Add(new IrStatement
                {
                    Operation = new IrOp_If(condValue, thenStmts, elseStmts),
                    Debug     = DebugOf(bn),
                });

                node = joinId is Guid jid && _nodeById.TryGetValue(jid, out var joinNode)
                    ? joinNode : null;
                continue;
            }

            _execNodeToBlockId[node.Id] = blockId;
            EmitNodeStatements(node, stmts);
            node = GetSingleExecSuccessor(node);
        }
    }

    /// <summary>
    /// P1b: schedules one Branch arm inline into a fresh nested statement list, with pin-value-cache
    /// isolation -- arm-scoped values (defined only on that path) must not leak to the sibling arm or
    /// to the post-join outer scope. Snapshots the cache keys, schedules the arm up to
    /// <paramref name="joinId"/>, then removes every entry the arm added.
    /// </summary>
    private List<IrStatement> ScheduleInlineArm(Node? armStart, Guid? joinId, int blockId)
    {
        var armStmts  = new List<IrStatement>();
        var savedKeys = new HashSet<Guid>(_pinValueCache.Keys);
        ScheduleInlineBodyChain(armStart, joinId, armStmts, blockId);
        foreach (var k in _pinValueCache.Keys.Where(k => !savedKeys.Contains(k)).ToList())
            _pinValueCache.Remove(k);
        return armStmts;
    }

    /// <summary>
    /// P1b: finds the inline join (immediate common successor) of a Branch's two arms within a
    /// FlowForEach body, so <see cref="IrOp_If"/> emits each arm only up to the reconvergence point
    /// and the outer chain resumes there ONCE. Returns null when EITHER arm ends (no successor) --
    /// then each arm is self-contained (e.g. slice-4's `if (!arrived) set=false;`, whose True arm
    /// ends). Otherwise the join is the successor reachable from BOTH arms nearest along the true arm
    /// (nested intra-arm merges stay inside their arm -- they are not reachable from the sibling).
    /// </summary>
    private Guid? FindInlineBranchJoin(Node? trueSucc, Node? falseSucc)
    {
        if (trueSucc is null || falseSucc is null) return null;

        // All nodes reachable from the false arm (outer control flow only).
        var fromFalse = new HashSet<Guid>();
        var qf = new Queue<Node>();
        qf.Enqueue(falseSucc);
        while (qf.Count > 0)
        {
            var n = qf.Dequeue();
            if (!fromFalse.Add(n.Id)) continue;
            foreach (var s in GetOuterExecSuccessors(n)) qf.Enqueue(s);
        }

        // BFS the true arm in distance order; the first node also reachable from the false arm is
        // the immediate join (nearest common successor).
        var visited = new HashSet<Guid>();
        var qt = new Queue<Node>();
        qt.Enqueue(trueSucc);
        while (qt.Count > 0)
        {
            var n = qt.Dequeue();
            if (!visited.Add(n.Id)) continue;
            if (fromFalse.Contains(n.Id)) return n.Id;
            foreach (var s in GetOuterExecSuccessors(n)) qt.Enqueue(s);
        }
        return null;
    }

    /// <summary>
    /// Outer control-flow exec successors of a body node used for inline-join reconvergence detection:
    /// a <see cref="BranchNode"/> yields both arms; a <see cref="FlowForEachNode"/> yields its
    /// "Completed" successor (its "Body" is nested, not outer flow); any other node yields its single
    /// exec successor (0 or 1).
    /// </summary>
    private IEnumerable<Node> GetOuterExecSuccessors(Node node)
    {
        switch (node)
        {
            case BranchNode bn:
            {
                var (t, f) = GetBranchSuccessors(bn);
                if (t is not null) yield return t;
                if (f is not null) yield return f;
                break;
            }
            case FlowForEachNode fe:
            {
                var comp = GetExecSuccessorByPinName(fe, "Completed");
                if (comp is not null) yield return comp;
                break;
            }
            case ComponentForEachNode cfe:
            {
                // CA-07b -- same "Body" is nested / "Completed" is outer flow" shape as FlowForEachNode.
                var comp = GetExecSuccessorByPinName(cfe, "Completed");
                if (comp is not null) yield return comp;
                break;
            }
            default:
            {
                var s = GetSingleExecSuccessor(node);
                if (s is not null) yield return s;
                break;
            }
        }
    }

    /// <summary>
    /// Resolves the exec successor reached via the single exec-out pin whose name is NOT
    /// <paramref name="excludedName"/> (Q#13: WaitForChannel's success continuation — the one
    /// exec-out that isn't "OnFailure", robust to "Out" vs the builder's "ExecOut"). Returns null
    /// unless exactly one such pin exists and it is wired.
    /// </summary>
    private Node? GetExecSuccessorExcludingPinName(Node node, string excludedName)
    {
        var pins = node.Pins
            .Where(p => p.IsExec && p.Direction == "Out"
                     && !string.Equals(p.Name, excludedName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (pins.Count != 1) return null;
        var link = _graph.Links.FirstOrDefault(l => l.FromNodeId == node.Id && l.FromPinId == pins[0].Id);
        return link is not null && _nodeById.TryGetValue(link.ToNodeId, out var t) ? t : null;
    }

    /// <summary>Resolves the exec successor reached via a specific named exec-out pin (e.g. "Body"/"Completed").</summary>
    private Node? GetExecSuccessorByPinName(Node node, string pinName)
    {
        var pin = node.Pins.FirstOrDefault(p =>
            p.IsExec && p.Direction == "Out"
            && string.Equals(p.Name, pinName, StringComparison.OrdinalIgnoreCase));
        if (pin is null) return null;
        var link = _graph.Links.FirstOrDefault(l => l.FromNodeId == node.Id && l.FromPinId == pin.Id);
        if (link is null) return null;
        return _nodeById.TryGetValue(link.ToNodeId, out var target) ? target : null;
    }

    // -----------------------------------------------------------------------
    // Variable index helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Normalizes a GetShared/SetShared node's <c>SharedTypeId</c> (as authored on the node, e.g.
    /// possibly carrying the AN2 "global::" pin-type sentinel) down to a plain FQN suitable for the
    /// IR op's <c>SharedTypeFqn</c> field and for the codegen's own single "global::" stamp
    /// (Stage 7 emits <c>global::{SharedTypeFqn}</c> -- stamping twice would emit
    /// <c>global::global::...</c>, CS0234). Also converts reflection's nested-type '+' separator to
    /// '.' (Category-1 shared structs are expected to be top-level, but this is defensive).
    /// </summary>
    private static string NormalizeSharedTypeFqn(string sharedTypeId)
    {
        if (string.IsNullOrEmpty(sharedTypeId)) return sharedTypeId ?? "";
        var fqn = sharedTypeId.StartsWith("global::", StringComparison.Ordinal)
            ? sharedTypeId.Substring("global::".Length)
            : sharedTypeId;
        return fqn.Replace('+', '.');
    }

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

    /// <summary>
    /// Params-ONLY index lookup for <see cref="GetParameterNode"/> (GAP-11). Unlike
    /// <see cref="FindVariableIndex"/> -- which searches Variables, then WorkingState, then
    /// Parameters and returns a COMBINED index (correct for GetVariable/SetVariable, which only
    /// ever emit <c>IrOp_ReadVariable</c>/<c>IrOp_WriteVariable</c> against that same combined
    /// space) -- this searches ONLY <c>_typed.Asset.Parameters</c>, since <c>IrOp_ReadParam</c>'s
    /// index is looked up via <c>EmissionContext.ParamFieldName</c> against
    /// <c>Asset.Parameters</c> alone. Using the combined index here would silently emit the wrong
    /// field (or an out-of-range <c>__p_{idx}</c> placeholder) whenever Variables/WorkingState are
    /// non-empty.
    /// </summary>
    private int FindParameterIndex(string parameterId)
    {
        var parameters = _typed.Asset.Parameters;

        // ParameterId may be in the form "var:<Guid>" or "param:<Guid>" -- strip the prefix before
        // parsing. Mirrors FindVariableIndex's "var:" handling.
        var idStr = parameterId.StartsWith("var:", StringComparison.OrdinalIgnoreCase)
            ? parameterId.Substring(4)
            : parameterId.StartsWith("param:", StringComparison.OrdinalIgnoreCase)
                ? parameterId.Substring(6)
                : parameterId;

        if (Guid.TryParse(idStr, out var guid))
        {
            for (int i = 0; i < parameters.Count; i++) if (parameters[i].Id == guid) return i;
        }
        // Name fallback
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

    /// <summary>
    /// Precomputes <see cref="_mergePoints"/>: every node reached by 2+ incoming EXEC edges. Such a
    /// node is a control-flow join and must be scheduled into ONE shared block that all predecessors
    /// jump to. Counts only exec-out → (exec-in) links; data links are ignored.
    /// </summary>
    private void ComputeMergePoints()
    {
        var inDegree = new Dictionary<Guid, int>();
        foreach (var link in _graph.Links)
        {
            if (!_nodeById.TryGetValue(link.FromNodeId, out var fromNode)) continue;
            var fromPin = fromNode.Pins.FirstOrDefault(p => p.Id == link.FromPinId);
            if (fromPin is null || !fromPin.IsExec || fromPin.Direction != "Out") continue;
            inDegree.TryGetValue(link.ToNodeId, out var c);
            inDegree[link.ToNodeId] = c + 1;
        }
        foreach (var kv in inDegree)
            if (kv.Value >= 2) _mergePoints.Add(kv.Key);
    }

    /// <summary>True when <paramref name="nodeId"/> is a convergent control-flow join (exec in-degree ≥ 2).</summary>
    private bool IsMergePoint(Guid nodeId) => _mergePoints.Contains(nodeId);

    /// <summary>
    /// The single shared block for a merge-point node, allocated on first request. Every predecessor
    /// path terminates with a <c>Goto</c> to this block; it is walked exactly once (the BFS dedups on
    /// block id via <see cref="_scheduledBlocks"/>, and per-block <see cref="_pinValueCache"/> reset
    /// makes the join re-resolve its own pure-data inputs independently of which arm reached it).
    /// </summary>
    private IrBlockId GetOrAllocMergeBlock(Node n)
    {
        if (_mergeBlockForNode.TryGetValue(n.Id, out var existing))
            return new IrBlockId(existing);
        var b = AllocBlock($"merge_{n.Id.ToString("N").Substring(0, 8)}");
        _mergeBlockForNode[n.Id] = b.Value;
        _bfsQueue.Enqueue((b.Value, n));
        return b;
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

