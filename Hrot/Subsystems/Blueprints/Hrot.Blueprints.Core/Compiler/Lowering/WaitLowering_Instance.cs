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
                .FirstOrDefault(o => o is IrOp_WaitForChannel or IrOp_WaitForEvent or IrOp_LatentDelay);

            var keptStmts = sb.Statements
                .Where(s => s.Operation is not (IrOp_WaitForChannel or IrOp_WaitForEvent or IrOp_LatentDelay))
                .Where(s => !(s.ResultValue.HasValue && s.ResultValue.Value.Index == resumePointIdx))
                .ToList();

            keptStmts.Add(Stmt(null, new IrOp_WriteCursorResumeAt(k + 1)));
            keptStmts.Add(Stmt(null, new IrOp_WriteCursorInstanceVersion()));

            if (waitOp is IrOp_LatentDelay ld)
            {
                // IrOp_WriteCursorWaitUntilTime carries the seconds; emitter adds Time.
                keptStmts.Add(Stmt(null, new IrOp_WriteCursorWaitUntilTime(ld.Seconds)));
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
                    suspendBlocks[0].Id,  // ResumeAt==0 → initial block
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
                .FirstOrDefault(o => o is IrOp_WaitForChannel or IrOp_WaitForEvent or IrOp_LatentDelay);

            if (waitOp is IrOp_LatentDelay)
            {
                // Delay check: IrOp_CheckCursorVersion first, then time comparison.
                var timeV      = Alloc(SingleType);
                var waitUntilV = Alloc(SingleType);
                var isLessV    = Alloc(BoolType);

                var stmts = new List<IrStatement>
                {
                    Stmt(null,      new IrOp_CheckCursorVersion()),
                    Stmt(timeV,     new IrOp_Time()),
                    Stmt(waitUntilV, new IrOp_ReadWorkingStateWaitUntilTime()),
                    Stmt(isLessV,   new IrOp_PureCall("op_LessThan_Single",
                                        new[] { timeV, waitUntilV }, BoolType)),
                };

                synthesizedBlocks.Add(new IrBlock
                {
                    Id         = resumeCheckBlockId[k],
                    Label      = $"resume_{k}_delay_check",
                    Statements = stmts,
                    Terminator = new IrTerm_Branch(isLessV,
                        retReturnBlockId[k], resumeBlockId) { Debug = Synth() },
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
                    Label      = $"resume_{k}_failure_unused",
                    Statements = new[] { Stmt(null, new IrOp_WriteCursorResumeAt(0)) },
                    Terminator = new IrTerm_Return(null) { Debug = Synth() },
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
        var finalBlocks = new List<IrBlock>();

        finalBlocks.Add(synthesizedBlocks.First(b => b.Id.Value == dispatchBlockId.Value));

        foreach (var b in graph.Blocks)
            finalBlocks.Add(modifiedBlocks.TryGetValue(b.Id.Value, out var mod) ? mod : b);

        for (int k = 1; k <= n - 1; k++)
            finalBlocks.Add(synthesizedBlocks.First(b => b.Id.Value == chainBlockId[k].Value));

        for (int k = 1; k <= n; k++)
        {
            finalBlocks.Add(synthesizedBlocks.First(b => b.Id.Value == resumeCheckBlockId[k].Value));
            finalBlocks.Add(synthesizedBlocks.First(b => b.Id.Value == retReturnBlockId[k].Value));
            finalBlocks.Add(synthesizedBlocks.First(b => b.Id.Value == notRunningBlockId[k].Value));
            finalBlocks.Add(synthesizedBlocks.First(b => b.Id.Value == failureBlockId[k].Value));
        }

        return graph with { Blocks = finalBlocks, Entry = dispatchBlockId };
    }

    private static int MaxValueIdx(IrGraph graph)
        => graph.Blocks
            .SelectMany(b => b.Statements)
            .Where(s => s.ResultValue.HasValue)
            .Select(s => s.ResultValue!.Value.Index)
            .DefaultIfEmpty(-1)
            .Max();
}

