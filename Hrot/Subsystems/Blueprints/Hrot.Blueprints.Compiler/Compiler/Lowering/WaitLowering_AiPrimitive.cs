using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Lowering;

/// <summary>
/// Converts IrTerm_Suspend terminators in an AiPrimitive graph into
/// a phase-byte state machine: a dispatch entry block plus per-phase
/// initial and check blocks.
/// </summary>
internal static class WaitLowering_AiPrimitive
{
    // Shared primitive type references used during lowering.
    private static readonly IrTypeRef ByteType =
        new IrTypeRef { FullName = "System.Byte", IsUnmanaged = true, SizeBytes = 1 };
    private static readonly IrTypeRef BoolType =
        new IrTypeRef { FullName = "System.Boolean", IsUnmanaged = true, SizeBytes = 1 };
    private static readonly IrTypeRef SingleType =
        new IrTypeRef { FullName = "System.Single", IsUnmanaged = true, SizeBytes = 4 };
    private static readonly IrTypeRef NodeStatusType =
        new IrTypeRef { FullName = "Hrot.Blueprints.Core.Assets.NodeStatus", IsUnmanaged = true, SizeBytes = 4 };
    private static readonly IrTypeRef EntityType =
        new IrTypeRef { FullName = "Hrot.Blueprints.Core.Assets.Entity", IsUnmanaged = true, SizeBytes = 4 };

    public static IrGraph Apply(IrGraph graph)
    {
        // Collect blocks with Suspend terminators in their original order.
        var suspendBlocks = graph.Blocks
            .Where(b => b.Terminator is IrTerm_Suspend)
            .ToList();

        if (suspendBlocks.Count == 0) return graph;

        int n = suspendBlocks.Count;

        // Counters for fresh SSA-value indices and block IDs.
        int nextVal = MaxValueIdx(graph) + 1;
        int nextBlkId = graph.Blocks.Max(b => b.Id.Value) + 1;

        IrValue Alloc(IrTypeRef t) => new IrValue(nextVal++, t);
        IrBlockId NewBlk() => new IrBlockId(nextBlkId++);

        IrDebugAnnotation Synth() =>
            new IrDebugAnnotation { GraphId = graph.Id, Synthesized = "stage6-wait-lower-ai" };

        IrStatement Stmt(IrValue? result, IrOperation op) =>
            new IrStatement { ResultValue = result, Operation = op, Debug = Synth() };

        // --- Pre-allocate IDs for all new blocks ---
        var dispatchBlockId = NewBlk();

        // For each wait (1-indexed phase k = k-th wait):
        //   checkBlockId[k], retRunningBlockId[k], notRunningBlockId[k], failureBlockId[k]
        var checkBlockId     = new IrBlockId[n + 1];
        var retRunningBlockId = new IrBlockId[n + 1];
        var notRunningBlockId = new IrBlockId[n + 1];
        var failureBlockId   = new IrBlockId[n + 1];
        for (int k = 1; k <= n; k++)
        {
            checkBlockId[k]     = NewBlk();
            retRunningBlockId[k] = NewBlk();
            notRunningBlockId[k] = NewBlk();
            failureBlockId[k]   = NewBlk();
        }

        // Dispatch chain: for phase > 0, check phases 1..N in sequence.
        // chainTargetId[k] = target when phase != k-1 (from the k-1 comparison block)
        //   chainTargetId[1] = checkBlockId[1] if N == 1, else a synthesized chain block
        //   For N > 1 we need chain blocks for phases 1..N-1.
        var chainBlockId = new IrBlockId[n]; // chainBlockId[k] checks phase==k (1-indexed)
        // chainBlockId[0] unused; chainBlockId[1..N-1] are synthesized chain nodes.
        // Actually: dispatch checks phase==0. If not 0, goes to chainBlockId[1].
        // chainBlockId[k] checks phase==k; if not k, goes to chainBlockId[k+1] or checkBlockId[N].
        for (int k = 1; k <= n - 1; k++)
            chainBlockId[k] = NewBlk();

        // For N==1 dispatch goes directly to checkBlockId[1] on the else branch (no chain needed).

        // ---------------------------------------------------------------
        // Build a lookup of modified suspend blocks (by original block ID).
        // ---------------------------------------------------------------
        var modifiedBlocks = new Dictionary<int, IrBlock>();

        for (int k = 0; k < n; k++)
        {
            var sb = suspendBlocks[k];
            var suspend = (IrTerm_Suspend)sb.Terminator;
            int resumePointIdx = suspend.ResumePoint.Index;

            // Find the wait op for this suspend.
            IrOperation? waitOp = sb.Statements
                .Select(s => s.Operation)
                .FirstOrDefault(o => o is IrOp_WaitForChannel or IrOp_WaitForEvent or IrOp_LatentDelay or IrOp_InlineActionCall);

            // Filter out: the wait-op stmt and the resume-point Const stmt.
            var keptStmts = sb.Statements
                .Where(s => s.Operation is not (IrOp_WaitForChannel or IrOp_WaitForEvent or IrOp_LatentDelay or IrOp_InlineActionCall))
                .Where(s => !(s.ResultValue.HasValue && s.ResultValue.Value.Index == resumePointIdx))
                .ToList();

            // Append: for LatentDelay, compute and store waitUntil; always write phase.
            if (waitOp is IrOp_LatentDelay ld)
            {
                // time + seconds -> workingState.WaitUntilTime
                var timeV = Alloc(SingleType);
                keptStmts.Add(Stmt(timeV, new IrOp_Time()));

                var waitUntilV = Alloc(SingleType);
                keptStmts.Add(Stmt(waitUntilV,
                    new IrOp_PureCall(
                        "op_Add_Single",
                        new[] { timeV, ld.Seconds },
                        SingleType)));
                keptStmts.Add(Stmt(null, new IrOp_WriteWorkingStateWaitUntilTime(waitUntilV)));
            }

            keptStmts.Add(Stmt(null, new IrOp_WriteWorkingStatePhase(k + 1)));

            modifiedBlocks[sb.Id.Value] = sb with
            {
                Statements = keptStmts,
                Terminator = new IrTerm_ReturnStatus(
                    Hrot.Blueprints.Core.Assets.NodeStatus.Running) { Debug = Synth() },
            };
        }

        // ---------------------------------------------------------------
        // Build synthesized check blocks for each phase k (1..N).
        // ---------------------------------------------------------------
        var synthesizedBlocks = new List<IrBlock>();

        // --- Dispatch block ---
        {
            var phaseV    = Alloc(ByteType);
            var constZero = Alloc(ByteType);
            var isZero    = Alloc(BoolType);

            var dispatchStmts = new List<IrStatement>
            {
                Stmt(phaseV,    new IrOp_ReadWorkingStatePhase()),
                Stmt(constZero, new IrOp_Const("0", ByteType)),
                Stmt(isZero,    new IrOp_PureCall("op_Eq_Byte",
                                    new[] { phaseV, constZero }, BoolType)),
            };

            // Else branch: for N==1 go directly to checkBlockId[1], otherwise to chain block 1.
            var elseTarget = n == 1 ? checkBlockId[1] : chainBlockId[1];

            synthesizedBlocks.Add(new IrBlock
            {
                Id         = dispatchBlockId,
                Label      = "dispatch",
                Statements = dispatchStmts,
                Terminator = new IrTerm_Branch(isZero,
                    graph.Entry,   // phase-0 initial = original entry (runs all pre-latent blocks)
                    elseTarget) { Debug = Synth() },
            });
        }

        // --- Chain blocks (for N > 1): chain[k] checks phase==k, routes to checkBlockId[k] or next ---
        for (int k = 1; k <= n - 1; k++)
        {
            var phaseV  = Alloc(ByteType);
            var constKV = Alloc(ByteType);
            var isKV    = Alloc(BoolType);

            var chainStmts = new List<IrStatement>
            {
                Stmt(phaseV,  new IrOp_ReadWorkingStatePhase()),
                Stmt(constKV, new IrOp_Const(k.ToString(), ByteType)),
                Stmt(isKV,    new IrOp_PureCall("op_Eq_Byte",
                                  new[] { phaseV, constKV }, BoolType)),
            };

            // If phase != k, go to checkBlockId[k+1] (last chain goes directly to last check).
            IrBlockId elseOfChain = checkBlockId[k + 1];

            synthesizedBlocks.Add(new IrBlock
            {
                Id         = chainBlockId[k],
                Label      = $"dispatch_chain_{k}",
                Statements = chainStmts,
                Terminator = new IrTerm_Branch(isKV,
                    checkBlockId[k],
                    elseOfChain) { Debug = Synth() },
            });
        }

        // --- Per-phase check + return-running + not-running + failure blocks ---
        for (int k = 1; k <= n; k++)
        {
            var suspendIdx = k - 1;
            var sb = suspendBlocks[suspendIdx];
            var suspend = (IrTerm_Suspend)sb.Terminator;
            var resumeBlockId = suspend.ResumeBlock;   // success continues here

            IrOperation? waitOp = sb.Statements
                .Select(s => s.Operation)
                .FirstOrDefault(o => o is IrOp_WaitForChannel or IrOp_WaitForEvent or IrOp_LatentDelay or IrOp_InlineActionCall);

            if (waitOp is IrOp_InlineActionCall iac)
            {
                // Inline-latent action call: re-invoke the action every tick until non-Running.
                // Check block: call action → if Running return Running; if Failure → failureBlock; else → resumeBlock.
                var statusV    = Alloc(NodeStatusType);
                var constRunV  = Alloc(NodeStatusType);
                var isRunV     = Alloc(BoolType);

                var checkStmts = new List<IrStatement>
                {
                    Stmt(statusV,   new IrOp_InlineActionCall(iac.ActionFqn, iac.ParamsTypeFqn, iac.ParamFields, iac.IsAiPrimitive)),
                    Stmt(constRunV, new IrOp_Const("NodeStatus.Running", NodeStatusType)),
                    Stmt(isRunV,    new IrOp_PureCall("op_Eq_NodeStatus",
                                        new[] { statusV, constRunV }, BoolType)),
                };

                synthesizedBlocks.Add(new IrBlock
                {
                    Id         = checkBlockId[k],
                    Label      = $"phase{k}_action_check",
                    Statements = checkStmts,
                    Terminator = new IrTerm_Branch(isRunV,
                        retRunningBlockId[k], notRunningBlockId[k]) { Debug = Synth() },
                });

                // Return-running block.
                synthesizedBlocks.Add(new IrBlock
                {
                    Id         = retRunningBlockId[k],
                    Label      = $"phase{k}_ret_running",
                    Statements = Array.Empty<IrStatement>(),
                    Terminator = new IrTerm_ReturnStatus(
                        Hrot.Blueprints.Core.Assets.NodeStatus.Running) { Debug = Synth() },
                });

                // Not-running check: distinguish Success from Failure.
                // The check block branched on Running; we land here when status != Running.
                // statusV was assigned in checkBlock; in generated C# it is a local variable
                // in the same method scope and remains accessible across goto targets.
                var constFailV2 = Alloc(NodeStatusType);
                var isFailV2    = Alloc(BoolType);

                var notRunStmts = new List<IrStatement>
                {
                    // statusV is the action result from the checkBlock — same C# local.
                    Stmt(constFailV2, new IrOp_Const("NodeStatus.Failure", NodeStatusType)),
                    Stmt(isFailV2,    new IrOp_PureCall("op_Eq_NodeStatus",
                                          new[] { statusV, constFailV2 }, BoolType)),
                };

                synthesizedBlocks.Add(new IrBlock
                {
                    Id         = notRunningBlockId[k],
                    Label      = $"phase{k}_not_running",
                    Statements = notRunStmts,
                    Terminator = new IrTerm_Branch(isFailV2,
                        failureBlockId[k], resumeBlockId) { Debug = Synth() },
                });

                // Failure block: reset phase to 0 and return Failure.
                synthesizedBlocks.Add(new IrBlock
                {
                    Id         = failureBlockId[k],
                    Label      = $"phase{k}_failure",
                    Statements = new[] { Stmt(null, new IrOp_WriteWorkingStatePhase(0)) },
                    Terminator = new IrTerm_ReturnStatus(
                        Hrot.Blueprints.Core.Assets.NodeStatus.Failure) { Debug = Synth() },
                });
            }
            else if (waitOp is IrOp_LatentDelay)
            {
                // Delay check: if (time < workingState.WaitUntilTime) Running else continue.
                var timeV     = Alloc(SingleType);
                var waitUntilV = Alloc(SingleType);
                var isLessV   = Alloc(BoolType);

                var checkStmts = new List<IrStatement>
                {
                    Stmt(timeV,      new IrOp_Time()),
                    Stmt(waitUntilV, new IrOp_ReadWorkingStateWaitUntilTime()),
                    Stmt(isLessV,    new IrOp_PureCall("op_LessThan_Single",
                                         new[] { timeV, waitUntilV }, BoolType)),
                };

                synthesizedBlocks.Add(new IrBlock
                {
                    Id         = checkBlockId[k],
                    Label      = $"phase{k}_delay_check",
                    Statements = checkStmts,
                    Terminator = new IrTerm_Branch(isLessV,
                        retRunningBlockId[k], failureBlockId[k]) { Debug = Synth() },
                });

                synthesizedBlocks.Add(new IrBlock
                {
                    Id         = retRunningBlockId[k],
                    Label      = $"phase{k}_ret_running",
                    Statements = Array.Empty<IrStatement>(),
                    Terminator = new IrTerm_ReturnStatus(
                        Hrot.Blueprints.Core.Assets.NodeStatus.Running) { Debug = Synth() },
                });

                // failureBlockId[k]: reset phase to 0 and goto the resume block
                // so the continuation (e.g. Sequence next branch, cursor/phase reset)
                // executes after the delay completes.
                synthesizedBlocks.Add(new IrBlock
                {
                    Id         = failureBlockId[k],
                    Label      = $"phase{k}_failure",
                    Statements = new[] { Stmt(null, new IrOp_WriteWorkingStatePhase(0)) },
                    Terminator = new IrTerm_Goto(resumeBlockId) { Debug = Synth() },
                });

                // notRunningBlockId[k] is not referenced; it will be filtered
                // by dead-block removal at assembly time.
            }
            else
            {
                // Channel or event wait check: GetComponentRO + FieldRead(Status) + status switch.
                string channelTypeFqn = waitOp is IrOp_WaitForChannel wfc
                    ? wfc.ChannelComponentTypeFqn
                    : waitOp is IrOp_WaitForEvent wfe
                        ? wfe.EventTypeFqn
                        : "?";

                var channelTypeRef = new IrTypeRef
                {
                    FullName     = channelTypeFqn,
                    IsUnmanaged  = true,
                    SizeBytes    = 0,
                };

                var selfV1      = Alloc(EntityType);
                var channelV1   = Alloc(channelTypeRef);
                var statusV1    = Alloc(NodeStatusType);
                var constRunV   = Alloc(NodeStatusType);
                var isRunV      = Alloc(BoolType);

                var checkStmts = new List<IrStatement>
                {
                    Stmt(selfV1,    new IrOp_Self()),
                    Stmt(channelV1, new IrOp_GetComponentRO(channelTypeFqn, selfV1, channelTypeRef)),
                    Stmt(statusV1,  new IrOp_FieldRead(channelV1, "Status", NodeStatusType)),
                    Stmt(constRunV, new IrOp_Const("NodeStatus.Running", NodeStatusType)),
                    Stmt(isRunV,    new IrOp_PureCall("op_Eq_NodeStatus",
                                        new[] { statusV1, constRunV }, BoolType)),
                };

                synthesizedBlocks.Add(new IrBlock
                {
                    Id         = checkBlockId[k],
                    Label      = $"phase{k}_channel_check",
                    Statements = checkStmts,
                    Terminator = new IrTerm_Branch(isRunV,
                        retRunningBlockId[k], notRunningBlockId[k]) { Debug = Synth() },
                });

                // Return-running block.
                synthesizedBlocks.Add(new IrBlock
                {
                    Id         = retRunningBlockId[k],
                    Label      = $"phase{k}_ret_running",
                    Statements = Array.Empty<IrStatement>(),
                    Terminator = new IrTerm_ReturnStatus(
                        Hrot.Blueprints.Core.Assets.NodeStatus.Running) { Debug = Synth() },
                });

                // Not-running check: re-read status and compare to Failure.
                var selfV2     = Alloc(EntityType);
                var channelV2  = Alloc(channelTypeRef);
                var statusV2   = Alloc(NodeStatusType);
                var constFailV = Alloc(NodeStatusType);
                var isFailV    = Alloc(BoolType);

                var notRunStmts = new List<IrStatement>
                {
                    Stmt(selfV2,     new IrOp_Self()),
                    Stmt(channelV2,  new IrOp_GetComponentRO(channelTypeFqn, selfV2, channelTypeRef)),
                    Stmt(statusV2,   new IrOp_FieldRead(channelV2, "Status", NodeStatusType)),
                    Stmt(constFailV, new IrOp_Const("NodeStatus.Failure", NodeStatusType)),
                    Stmt(isFailV,    new IrOp_PureCall("op_Eq_NodeStatus",
                                         new[] { statusV2, constFailV }, BoolType)),
                };

                synthesizedBlocks.Add(new IrBlock
                {
                    Id         = notRunningBlockId[k],
                    Label      = $"phase{k}_not_running",
                    Statements = notRunStmts,
                    Terminator = new IrTerm_Branch(isFailV,
                        failureBlockId[k], resumeBlockId) { Debug = Synth() },
                });

                // Failure block: reset phase to 0 and return Failure.
                synthesizedBlocks.Add(new IrBlock
                {
                    Id         = failureBlockId[k],
                    Label      = $"phase{k}_failure",
                    Statements = new[] { Stmt(null, new IrOp_WriteWorkingStatePhase(0)) },
                    Terminator = new IrTerm_ReturnStatus(
                        Hrot.Blueprints.Core.Assets.NodeStatus.Failure) { Debug = Synth() },
                });
            }
        }

        // ---------------------------------------------------------------
        // Assemble the final block list.
        // ---------------------------------------------------------------
        var allCandidateBlocks = new List<IrBlock>();

        // 1. Dispatch block first (new entry).
        allCandidateBlocks.Add(synthesizedBlocks.First(b => b.Id.Value == dispatchBlockId.Value));

        // 2. Carry over all original blocks (with suspend-blocks already modified).
        foreach (var b in graph.Blocks)
        {
            allCandidateBlocks.Add(modifiedBlocks.TryGetValue(b.Id.Value, out var mod) ? mod : b);
        }

        // 3. Append chain blocks (in chain order).
        for (int k = 1; k <= n - 1; k++)
            allCandidateBlocks.Add(synthesizedBlocks.First(b => b.Id.Value == chainBlockId[k].Value));

        // 4. Append check/retRunning/notRunning/failure blocks (in phase order).
        for (int k = 1; k <= n; k++)
        {
            if (synthesizedBlocks.Any(b => b.Id.Value == checkBlockId[k].Value))
                allCandidateBlocks.Add(synthesizedBlocks.First(b => b.Id.Value == checkBlockId[k].Value));
            if (synthesizedBlocks.Any(b => b.Id.Value == retRunningBlockId[k].Value))
                allCandidateBlocks.Add(synthesizedBlocks.First(b => b.Id.Value == retRunningBlockId[k].Value));
            if (synthesizedBlocks.Any(b => b.Id.Value == notRunningBlockId[k].Value))
                allCandidateBlocks.Add(synthesizedBlocks.First(b => b.Id.Value == notRunningBlockId[k].Value));
            if (synthesizedBlocks.Any(b => b.Id.Value == failureBlockId[k].Value))
                allCandidateBlocks.Add(synthesizedBlocks.First(b => b.Id.Value == failureBlockId[k].Value));
        }

        // C0: dead-block filtering — remove unreferenced blocks (e.g. LatentDelay
        // _unused blocks) to prevent CS0162/CS0164.
        var finalBlocks = FilterDeadBlocks(allCandidateBlocks, dispatchBlockId);

        return graph with { Blocks = finalBlocks, Entry = dispatchBlockId };
    }

    // Returns the highest SSA value index used in the graph, or -1 if none.
    private static int MaxValueIdx(IrGraph graph)
        => graph.Blocks
            .SelectMany(b => b.Statements)
            .Where(s => s.ResultValue.HasValue)
            .Select(s => s.ResultValue!.Value.Index)
            .DefaultIfEmpty(-1)
            .Max();

    /// <summary>
    /// C0: Remove blocks that are neither the entry nor referenced by any
    /// terminator (Goto.Target, Branch.IfTrue/IfFalse, Suspend.ResumeBlock).
    /// Follows FallThrough edges via block-ordering to ensure that the
    /// entire linear chain from entry is retained when blocks use FallThrough.
    /// Eliminates dead blocks from the LatentDelay path (e.g. _unused)
    /// so they don't produce CS0162/CS0164 in the generated source.
    /// </summary>
    private static List<IrBlock> FilterDeadBlocks(List<IrBlock> candidates, IrBlockId entry)
    {
        // Build a lookup by Id.Value for fast access.
        var byId = candidates.ToDictionary(b => b.Id.Value);

        // First pass: collect all block ids that are explicitly referenced
        // by any terminator (Goto, Branch, Suspend).  These are "targeted"
        // and must be retained.
        var referenced = new HashSet<int>();
        foreach (var b in candidates)
        {
            switch (b.Terminator)
            {
                case IrTerm_Goto go:
                    referenced.Add(go.Target.Value);
                    break;
                case IrTerm_Branch br:
                    referenced.Add(br.IfTrue.Value);
                    referenced.Add(br.IfFalse.Value);
                    break;
                case IrTerm_Suspend sus:
                    referenced.Add(sus.ResumeBlock.Value);
                    break;
            }
        }

        // Second pass: BFS from entry following every edge (Goto, Branch,
        // Suspend, AND implicit FallThrough to the next block in order).
        // A block with FallThrough keeps the next block in the list reachable.
        var reachable = new HashSet<int>();
        var queue = new Queue<int>();
        reachable.Add(entry.Value);
        queue.Enqueue(entry.Value);

        // For FallThrough, we need to know "next block in order" for each candidate.
        var blockOrder = candidates.Select(b => b.Id.Value).ToList();

        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            if (!byId.TryGetValue(cur, out var curBlock)) continue;

            // Explicit edges
            switch (curBlock.Terminator)
            {
                case IrTerm_Goto go:
                    EnqueueIfNew(go.Target.Value);
                    break;
                case IrTerm_Branch br:
                    EnqueueIfNew(br.IfTrue.Value);
                    EnqueueIfNew(br.IfFalse.Value);
                    break;
                case IrTerm_Suspend sus:
                    EnqueueIfNew(sus.ResumeBlock.Value);
                    break;
                case IrTerm_FallThrough:
                {
                    // Implicit next-block: find cur in blockOrder and enqueue the successor.
                    int idx = blockOrder.IndexOf(cur);
                    if (idx >= 0 && idx + 1 < blockOrder.Count)
                        EnqueueIfNew(blockOrder[idx + 1]);
                    break;
                }
                // Return / ReturnStatus don't name a successor block — end of chain.
            }
        }

        void EnqueueIfNew(int id)
        {
            if (reachable.Add(id))
                queue.Enqueue(id);
        }

        // Keep: entry (already in reachable), blocks reachable via edges,
        // AND any block that some retained terminator explicitly references
        // (e.g. an unreachable Suspend.ResumeBlock that IS a Goto target
        // from a different block).
        foreach (int refId in referenced)
            reachable.Add(refId);

        return candidates.Where(b => reachable.Contains(b.Id.Value)).ToList();
    }
}

