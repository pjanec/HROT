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
                .FirstOrDefault(o => o is IrOp_WaitForChannel or IrOp_WaitForEvent or IrOp_LatentDelay);

            // Filter out: the wait-op stmt and the resume-point Const stmt.
            var keptStmts = sb.Statements
                .Where(s => s.Operation is not (IrOp_WaitForChannel or IrOp_WaitForEvent or IrOp_LatentDelay))
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
                    suspendBlocks[0].Id,   // phase-0 initial = modified original first block
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
                .FirstOrDefault(o => o is IrOp_WaitForChannel or IrOp_WaitForEvent or IrOp_LatentDelay);

            if (waitOp is IrOp_LatentDelay)
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
                        retRunningBlockId[k], resumeBlockId) { Debug = Synth() },
                });

                synthesizedBlocks.Add(new IrBlock
                {
                    Id         = retRunningBlockId[k],
                    Label      = $"phase{k}_ret_running",
                    Statements = Array.Empty<IrStatement>(),
                    Terminator = new IrTerm_ReturnStatus(
                        Hrot.Blueprints.Core.Assets.NodeStatus.Running) { Debug = Synth() },
                });

                // notRunning and failure blocks unused for delay; add them as dead code
                // to keep the pre-allocated IDs consistent (they won't appear in any branch).
                synthesizedBlocks.Add(new IrBlock
                {
                    Id         = notRunningBlockId[k],
                    Label      = $"phase{k}_not_running_unused",
                    Statements = Array.Empty<IrStatement>(),
                    Terminator = new IrTerm_ReturnStatus(
                        Hrot.Blueprints.Core.Assets.NodeStatus.Failure) { Debug = Synth() },
                });
                synthesizedBlocks.Add(new IrBlock
                {
                    Id         = failureBlockId[k],
                    Label      = $"phase{k}_failure_unused",
                    Statements = new[] { Stmt(null, new IrOp_WriteWorkingStatePhase(0)) },
                    Terminator = new IrTerm_ReturnStatus(
                        Hrot.Blueprints.Core.Assets.NodeStatus.Failure) { Debug = Synth() },
                });
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
        var finalBlocks = new List<IrBlock>();

        // 1. Dispatch block first (new entry).
        finalBlocks.Add(synthesizedBlocks.First(b => b.Id.Value == dispatchBlockId.Value));

        // 2. Carry over all original blocks (with suspend-blocks already modified).
        foreach (var b in graph.Blocks)
        {
            finalBlocks.Add(modifiedBlocks.TryGetValue(b.Id.Value, out var mod) ? mod : b);
        }

        // 3. Append chain blocks (in chain order).
        for (int k = 1; k <= n - 1; k++)
            finalBlocks.Add(synthesizedBlocks.First(b => b.Id.Value == chainBlockId[k].Value));

        // 4. Append check/retRunning/notRunning/failure blocks (in phase order).
        for (int k = 1; k <= n; k++)
        {
            finalBlocks.Add(synthesizedBlocks.First(b => b.Id.Value == checkBlockId[k].Value));
            finalBlocks.Add(synthesizedBlocks.First(b => b.Id.Value == retRunningBlockId[k].Value));
            finalBlocks.Add(synthesizedBlocks.First(b => b.Id.Value == notRunningBlockId[k].Value));
            finalBlocks.Add(synthesizedBlocks.First(b => b.Id.Value == failureBlockId[k].Value));
        }

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
}

