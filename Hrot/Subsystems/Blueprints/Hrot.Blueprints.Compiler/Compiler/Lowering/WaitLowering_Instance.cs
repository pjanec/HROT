using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Lowering;

/// <summary>
/// Converts IrTerm_Suspend terminators in an Instance graph into
/// a cursor-based state machine (switch on state.Cursor.ResumeAt).
/// </summary>
internal static class WaitLowering_Instance
{
    private static readonly IrTypeRef BoolType =
        new IrTypeRef { FullName = "System.Boolean", IsUnmanaged = true, SizeBytes = 1 };
    private static readonly IrTypeRef UInt32Type =
        new IrTypeRef { FullName = "System.UInt32", IsUnmanaged = true, SizeBytes = 4 };
    private static readonly IrTypeRef SingleType =
        new IrTypeRef { FullName = "System.Single", IsUnmanaged = true, SizeBytes = 4 };
    private static readonly IrTypeRef NodeStatusType =
        new IrTypeRef { FullName = "Hrot.Blueprints.Core.Assets.NodeStatus", IsUnmanaged = true, SizeBytes = 4 };
    private static readonly IrTypeRef EntityType =
        new IrTypeRef { FullName = "Hrot.Blueprints.Core.Assets.Entity", IsUnmanaged = true, SizeBytes = 4 };

    public static IrGraph Apply(IrGraph graph)
    {
        var suspendBlocks = graph.Blocks
            .Where(b => b.Terminator is IrTerm_Suspend)
            .ToList();

        if (suspendBlocks.Count == 0) return graph;

        int n = suspendBlocks.Count;

        int nextVal = MaxValueIdx(graph) + 1;
        int nextBlkId = graph.Blocks.Max(b => b.Id.Value) + 1;

        IrValue Alloc(IrTypeRef t) => new IrValue(nextVal++, t);
        IrBlockId NewBlk() => new IrBlockId(nextBlkId++);

        IrDebugAnnotation Synth() =>
            new IrDebugAnnotation { GraphId = graph.Id, Synthesized = "stage6-wait-lower-inst" };

        IrStatement Stmt(IrValue? result, IrOperation op) =>
            new IrStatement { ResultValue = result, Operation = op, Debug = Synth() };

        // --- Pre-allocate IDs ---
        var dispatchBlockId = NewBlk();

        var resumeCheckBlockId    = new IrBlockId[n + 1]; // check block per resume label 1..N
        var retReturnBlockId      = new IrBlockId[n + 1]; // returns void (running) per label
        var notRunningBlockId     = new IrBlockId[n + 1]; // failure/success branch per label
        var failureBlockId        = new IrBlockId[n + 1]; // resets cursor and returns per label
        for (int k = 1; k <= n; k++)
        {
            resumeCheckBlockId[k] = NewBlk();
            retReturnBlockId[k]   = NewBlk();
            notRunningBlockId[k]  = NewBlk();
            failureBlockId[k]     = NewBlk();
        }

        // Chain blocks for dispatch (when N > 1).
        var chainBlockId = new IrBlockId[n]; // chainBlockId[k] checks ResumeAt==k (1-indexed)
        for (int k = 1; k <= n - 1; k++)
            chainBlockId[k] = NewBlk();

        // ---------------------------------------------------------------
        // Modify each suspend block to become the "initial" block:
        //   remove wait-op + resume-point const
        //   append WriteCursorResumeAt(k+1), WriteCursorInstanceVersion
        //   optionally WriteCursorWaitUntilTime (for LatentDelay)
        //   change Suspend terminator → IrTerm_Return(null)
        // ---------------------------------------------------------------
        var modifiedBlocks = new Dictionary<int, IrBlock>();

        for (int k = 0; k < n; k++)
        {
            var sb = suspendBlocks[k];
            var suspend = (IrTerm_Suspend)sb.Terminator;
            int resumePointIdx = suspend.ResumePoint.Index;

            IrOperation? waitOp = sb.Statements
                .Select(s => s.Operation)
                .FirstOrDefault(o => o is IrOp_WaitForChannel or IrOp_WaitForEvent or IrOp_LatentDelay or IrOp_InlineActionCall);

            var keptStmts = sb.Statements
                .Where(s => s.Operation is not (IrOp_WaitForChannel or IrOp_WaitForEvent or IrOp_LatentDelay or IrOp_InlineActionCall))
                .Where(s => !(s.ResultValue.HasValue && s.ResultValue.Value.Index == resumePointIdx))
                .ToList();

            keptStmts.Add(Stmt(null, new IrOp_WriteCursorResumeAt(k + 1)));
            keptStmts.Add(Stmt(null, new IrOp_WriteCursorInstanceVersion()));

            if (waitOp is IrOp_LatentDelay ld)
            {
                // Compute time + duration and store as wait-until (relative, not absolute).
                var timeV = Alloc(SingleType);
                keptStmts.Add(Stmt(timeV, new IrOp_Time()));

                var waitUntilV = Alloc(SingleType);
                keptStmts.Add(Stmt(waitUntilV,
                    new IrOp_PureCall("op_Add_Single",
                        new[] { timeV, ld.Seconds },
                        SingleType)));
                keptStmts.Add(Stmt(null, new IrOp_WriteCursorWaitUntilTime(waitUntilV)));
            }

            modifiedBlocks[sb.Id.Value] = sb with
            {
                Statements = keptStmts,
                Terminator = new IrTerm_Return(null) { Debug = Synth() },
            };
        }

        // ---------------------------------------------------------------
        // Build synthesized blocks.
        // ---------------------------------------------------------------
        var synthesizedBlocks = new List<IrBlock>();

        // --- Dispatch block ---
        {
            var resumeAtV  = Alloc(UInt32Type);
            var constZeroV = Alloc(UInt32Type);
            var isZeroV    = Alloc(BoolType);

            var stmts = new List<IrStatement>
            {
                Stmt(resumeAtV,  new IrOp_ReadCursorResumeAt()),
                Stmt(constZeroV, new IrOp_Const("0u", UInt32Type)),
                Stmt(isZeroV,    new IrOp_PureCall("op_Eq_UInt32",
                                     new[] { resumeAtV, constZeroV }, BoolType)),
            };

            IrBlockId elseTarget = n == 1 ? resumeCheckBlockId[1] : chainBlockId[1];

            synthesizedBlocks.Add(new IrBlock
            {
                Id         = dispatchBlockId,
                Label      = "cursor_dispatch",
                Statements = stmts,
                Terminator = new IrTerm_Branch(isZeroV,
                    graph.Entry,  // ResumeAt==0 → original entry (runs all pre-latent blocks)
                    elseTarget) { Debug = Synth() },
            });
        }

        // --- Chain blocks (N > 1) ---
        for (int k = 1; k <= n - 1; k++)
        {
            var resumeAtV  = Alloc(UInt32Type);
            var constKV    = Alloc(UInt32Type);
            var isKV       = Alloc(BoolType);

            var stmts = new List<IrStatement>
            {
                Stmt(resumeAtV, new IrOp_ReadCursorResumeAt()),
                Stmt(constKV,   new IrOp_Const($"{k}u", UInt32Type)),
                Stmt(isKV,      new IrOp_PureCall("op_Eq_UInt32",
                                    new[] { resumeAtV, constKV }, BoolType)),
            };

            IrBlockId elseOfChain = resumeCheckBlockId[k + 1];

            synthesizedBlocks.Add(new IrBlock
            {
                Id         = chainBlockId[k],
                Label      = $"cursor_dispatch_chain_{k}",
                Statements = stmts,
                Terminator = new IrTerm_Branch(isKV,
                    resumeCheckBlockId[k], elseOfChain) { Debug = Synth() },
            });
        }

        // --- Resume check blocks per label ---
        for (int k = 1; k <= n; k++)
        {
            var suspendIdx    = k - 1;
            var sb            = suspendBlocks[suspendIdx];
            var suspend       = (IrTerm_Suspend)sb.Terminator;
            var resumeBlockId = suspend.ResumeBlock;

            IrOperation? waitOp = sb.Statements
                .Select(s => s.Operation)
                .FirstOrDefault(o => o is IrOp_WaitForChannel or IrOp_WaitForEvent or IrOp_LatentDelay or IrOp_InlineActionCall);

            if (waitOp is IrOp_InlineActionCall iac)
            {
                // AN8 inline-latent: cursor-based re-invoke (Instance blueprint).
                // Check block: CheckCursorVersion + re-call action + branch on Running.
                var statusV    = Alloc(NodeStatusType);
                var constRunV  = Alloc(NodeStatusType);
                var isRunV     = Alloc(BoolType);

                var checkStmts = new List<IrStatement>
                {
                    Stmt(null,      new IrOp_CheckCursorVersion()),
                    Stmt(statusV,   new IrOp_InlineActionCall(iac.ActionFqn, iac.ParamsTypeFqn, iac.ParamFields, iac.IsAiPrimitive)),
                    Stmt(constRunV, new IrOp_Const("NodeStatus.Running", NodeStatusType)),
                    Stmt(isRunV,    new IrOp_PureCall("op_Eq_NodeStatus",
                                        new[] { statusV, constRunV }, BoolType)),
                };

                synthesizedBlocks.Add(new IrBlock
                {
                    Id         = resumeCheckBlockId[k],
                    Label      = $"resume_{k}_action_check",
                    Statements = checkStmts,
                    Terminator = new IrTerm_Branch(isRunV,
                        retReturnBlockId[k], notRunningBlockId[k]) { Debug = Synth() },
                });

                synthesizedBlocks.Add(new IrBlock
                {
                    Id         = retReturnBlockId[k],
                    Label      = $"resume_{k}_ret_void",
                    Statements = Array.Empty<IrStatement>(),
                    Terminator = new IrTerm_Return(null) { Debug = Synth() },
                });

                // Not-running: distinguish Success from Failure using statusV (same C# local).
                var constFailV2 = Alloc(NodeStatusType);
                var isFailV2    = Alloc(BoolType);

                synthesizedBlocks.Add(new IrBlock
                {
                    Id         = notRunningBlockId[k],
                    Label      = $"resume_{k}_not_running",
                    Statements = new List<IrStatement>
                    {
                        Stmt(constFailV2, new IrOp_Const("NodeStatus.Failure", NodeStatusType)),
                        Stmt(isFailV2,    new IrOp_PureCall("op_Eq_NodeStatus",
                                              new[] { statusV, constFailV2 }, BoolType)),
                    },
                    Terminator = new IrTerm_Branch(isFailV2,
                        failureBlockId[k], resumeBlockId) { Debug = Synth() },
                });

                synthesizedBlocks.Add(new IrBlock
                {
                    Id         = failureBlockId[k],
                    Label      = $"resume_{k}_failure",
                    Statements = new[] { Stmt(null, new IrOp_WriteCursorResumeAt(0)) },
                    Terminator = new IrTerm_Return(null) { Debug = Synth() },
                });
            }
            else if (waitOp is IrOp_LatentDelay)
            {
                // Delay check: IrOp_CheckCursorVersion first, then time comparison.
                var timeV      = Alloc(SingleType);
                var waitUntilV = Alloc(SingleType);
                var isLessV    = Alloc(BoolType);

                var stmts = new List<IrStatement>
                {
                    Stmt(null,      new IrOp_CheckCursorVersion()),
                    Stmt(timeV,     new IrOp_Time()),
                    Stmt(waitUntilV, new IrOp_ReadCursorWaitUntilTime()),
                    Stmt(isLessV,   new IrOp_PureCall("op_LessThan_Single",
                                        new[] { timeV, waitUntilV }, BoolType)),
                };

                synthesizedBlocks.Add(new IrBlock
                {
                    Id         = resumeCheckBlockId[k],
                    Label      = $"resume_{k}_delay_check",
                    Statements = stmts,
                    Terminator = new IrTerm_Branch(isLessV,
                        retReturnBlockId[k], failureBlockId[k]) { Debug = Synth() },
                });

                synthesizedBlocks.Add(new IrBlock
                {
                    Id         = retReturnBlockId[k],
                    Label      = $"resume_{k}_ret_void",
                    Statements = Array.Empty<IrStatement>(),
                    Terminator = new IrTerm_Return(null) { Debug = Synth() },
                });

                // Unused notRunning/failure (keep for ID consistency).
                synthesizedBlocks.Add(new IrBlock
                {
                    Id         = notRunningBlockId[k],
                    Label      = $"resume_{k}_not_running_unused",
                    Statements = Array.Empty<IrStatement>(),
                    Terminator = new IrTerm_Return(null) { Debug = Synth() },
                });
                synthesizedBlocks.Add(new IrBlock
                {
                    Id         = failureBlockId[k],
                    Label      = $"resume_{k}_failure",
                    Statements = new[] { Stmt(null, new IrOp_WriteCursorResumeAt(0)) },
                    Terminator = new IrTerm_Goto(resumeBlockId) { Debug = Synth() },
                });
            }
            else
            {
                // Channel/event wait: CheckCursorVersion + GetComponentRO + status switch.
                string channelTypeFqn = waitOp is IrOp_WaitForChannel wfc
                    ? wfc.ChannelComponentTypeFqn
                    : waitOp is IrOp_WaitForEvent wfe
                        ? wfe.EventTypeFqn
                        : "?";

                var channelTypeRef = new IrTypeRef
                {
                    FullName    = channelTypeFqn,
                    IsUnmanaged = true,
                    SizeBytes   = 0,
                };

                var selfV1    = Alloc(EntityType);
                var channelV1 = Alloc(channelTypeRef);
                var statusV1  = Alloc(NodeStatusType);
                var constRunV = Alloc(NodeStatusType);
                var isRunV    = Alloc(BoolType);

                var checkStmts = new List<IrStatement>
                {
                    Stmt(null,      new IrOp_CheckCursorVersion()),
                    Stmt(selfV1,    new IrOp_Self()),
                    Stmt(channelV1, new IrOp_GetComponentRO(channelTypeFqn, selfV1, channelTypeRef)),
                    Stmt(statusV1,  new IrOp_FieldRead(channelV1, "Status", NodeStatusType)),
                    Stmt(constRunV, new IrOp_Const("NodeStatus.Running", NodeStatusType)),
                    Stmt(isRunV,    new IrOp_PureCall("op_Eq_NodeStatus",
                                        new[] { statusV1, constRunV }, BoolType)),
                };

                synthesizedBlocks.Add(new IrBlock
                {
                    Id         = resumeCheckBlockId[k],
                    Label      = $"resume_{k}_channel_check",
                    Statements = checkStmts,
                    Terminator = new IrTerm_Branch(isRunV,
                        retReturnBlockId[k], notRunningBlockId[k]) { Debug = Synth() },
                });

                synthesizedBlocks.Add(new IrBlock
                {
                    Id         = retReturnBlockId[k],
                    Label      = $"resume_{k}_ret_void",
                    Statements = Array.Empty<IrStatement>(),
                    Terminator = new IrTerm_Return(null) { Debug = Synth() },
                });

                // Not-running: check for failure vs success.
                var selfV2     = Alloc(EntityType);
                var channelV2  = Alloc(channelTypeRef);
                var statusV2   = Alloc(NodeStatusType);
                var constFailV = Alloc(NodeStatusType);
                var isFailV    = Alloc(BoolType);

                synthesizedBlocks.Add(new IrBlock
                {
                    Id         = notRunningBlockId[k],
                    Label      = $"resume_{k}_not_running",
                    Statements = new List<IrStatement>
                    {
                        Stmt(selfV2,     new IrOp_Self()),
                        Stmt(channelV2,  new IrOp_GetComponentRO(channelTypeFqn, selfV2, channelTypeRef)),
                        Stmt(statusV2,   new IrOp_FieldRead(channelV2, "Status", NodeStatusType)),
                        Stmt(constFailV, new IrOp_Const("NodeStatus.Failure", NodeStatusType)),
                        Stmt(isFailV,    new IrOp_PureCall("op_Eq_NodeStatus",
                                             new[] { statusV2, constFailV }, BoolType)),
                    },
                    Terminator = new IrTerm_Branch(isFailV,
                        failureBlockId[k], resumeBlockId) { Debug = Synth() },
                });

                synthesizedBlocks.Add(new IrBlock
                {
                    Id         = failureBlockId[k],
                    Label      = $"resume_{k}_failure",
                    Statements = new[] { Stmt(null, new IrOp_WriteCursorResumeAt(0)) },
                    Terminator = new IrTerm_Return(null) { Debug = Synth() },
                });
            }
        }

        // ---------------------------------------------------------------
        // Assemble final block list.
        // ---------------------------------------------------------------
        var allCandidateBlocks = new List<IrBlock>();

        // 1. Dispatch block first (new entry).
        allCandidateBlocks.Add(synthesizedBlocks.First(b => b.Id.Value == dispatchBlockId.Value));

        // 2. Carry over all original blocks (with suspend-blocks already modified).
        foreach (var b in graph.Blocks)
            allCandidateBlocks.Add(modifiedBlocks.TryGetValue(b.Id.Value, out var mod) ? mod : b);

        // 3. Append chain blocks (in chain order).
        for (int k = 1; k <= n - 1; k++)
            allCandidateBlocks.Add(synthesizedBlocks.First(b => b.Id.Value == chainBlockId[k].Value));

        // 4. Append check/retReturn/notRunning/failure blocks (in phase order).
        for (int k = 1; k <= n; k++)
        {
            if (synthesizedBlocks.Any(b => b.Id.Value == resumeCheckBlockId[k].Value))
                allCandidateBlocks.Add(synthesizedBlocks.First(b => b.Id.Value == resumeCheckBlockId[k].Value));
            if (synthesizedBlocks.Any(b => b.Id.Value == retReturnBlockId[k].Value))
                allCandidateBlocks.Add(synthesizedBlocks.First(b => b.Id.Value == retReturnBlockId[k].Value));
            if (synthesizedBlocks.Any(b => b.Id.Value == notRunningBlockId[k].Value))
                allCandidateBlocks.Add(synthesizedBlocks.First(b => b.Id.Value == notRunningBlockId[k].Value));
            if (synthesizedBlocks.Any(b => b.Id.Value == failureBlockId[k].Value))
                allCandidateBlocks.Add(synthesizedBlocks.First(b => b.Id.Value == failureBlockId[k].Value));
        }

        // Filter dead blocks (e.g. _unused blocks from LatentDelay path)
        // to prevent CS0162/CS0164.
        var finalBlocks = FilterDeadBlocks(allCandidateBlocks, dispatchBlockId);

        return graph with { Blocks = finalBlocks, Entry = dispatchBlockId };
    }

    private static int MaxValueIdx(IrGraph graph)
        => graph.Blocks
            .SelectMany(b => b.Statements)
            .Where(s => s.ResultValue.HasValue)
            .Select(s => s.ResultValue!.Value.Index)
            .DefaultIfEmpty(-1)
            .Max();

    /// <summary>
    /// Remove blocks that are neither the entry nor referenced by any
    /// terminator (Goto.Target, Branch.IfTrue/IfFalse, Suspend.ResumeBlock).
    /// Follows FallThrough edges via block-ordering to retain the entire
    /// linear chain from entry.
    /// </summary>
    private static List<IrBlock> FilterDeadBlocks(List<IrBlock> candidates, IrBlockId entry)
    {
        var byId = candidates.ToDictionary(b => b.Id.Value);

        // Collect all block ids explicitly referenced by terminators.
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

        // BFS from entry following all edges + implicit FallThrough.
        var reachable = new HashSet<int>();
        var queue = new Queue<int>();
        reachable.Add(entry.Value);
        queue.Enqueue(entry.Value);

        var blockOrder = candidates.Select(b => b.Id.Value).ToList();

        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            if (!byId.TryGetValue(cur, out var curBlock)) continue;

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
                    int idx = blockOrder.IndexOf(cur);
                    if (idx >= 0 && idx + 1 < blockOrder.Count)
                        EnqueueIfNew(blockOrder[idx + 1]);
                    break;
                }
            }
        }

        void EnqueueIfNew(int id)
        {
            if (reachable.Add(id))
                queue.Enqueue(id);
        }

        // Also keep any block that a retained terminator references.
        foreach (int refId in referenced)
            reachable.Add(refId);

        return candidates.Where(b => reachable.Contains(b.Id.Value)).ToList();
    }
}

