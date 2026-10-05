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
    private static readonly IrTypeRef UInt16Type =
        new IrTypeRef { FullName = "System.UInt16", IsUnmanaged = true, SizeBytes = 2 };
    private static readonly IrTypeRef SingleType =
        new IrTypeRef { FullName = "System.Single", IsUnmanaged = true, SizeBytes = 4 };
    private static readonly IrTypeRef NodeStatusType =
        new IrTypeRef { FullName = "Hrot.Blueprints.Core.Assets.NodeStatus", IsUnmanaged = true, SizeBytes = 4 };
    private static readonly IrTypeRef EntityType =
        new IrTypeRef { FullName = "Hrot.Blueprints.Core.Assets.Entity", IsUnmanaged = true, SizeBytes = 4 };

    /// <param name="behaviorTick">⭐ S5d — the graph is a behaviour's <c>Tick</c>, where a plain return means RUNNING
    /// (<c>CE-446</c>). ⇒ an unwired <c>OnFailure</c> must return <c>Failure</c> explicitly, as Q#13 rules, rather than
    /// fall off into Running and silently retry the wait from Entry next frame.</param>
    public static IrGraph Apply(IrGraph graph, bool behaviorTick = false)
    {
        var suspendBlocks = graph.Blocks
            .Where(b => b.Terminator is IrTerm_Suspend)
            .ToList();

        if (suspendBlocks.Count == 0) return graph;

        int n = suspendBlocks.Count;

        // ⭐ S7a (DESIGN_Unified_Behaviour_Run "S7 design" D5) — every Behaviour Task this graph aborts gets a second
        //   resume label, n + j: the dispatch routes it to a block that clears the cursor and continues on Failed. The
        //   task is found by its suspend block (its node id, or its site id before the debug-id rewrite).
        var taskOfK = new Dictionary<int, IrOp_RunBehavior>();
        for (int k = 1; k <= n; k++)
            if (suspendBlocks[k - 1].Statements.Select(st => st.Operation).FirstOrDefault(SuspendOps.Is) is IrOp_RunBehavior rbk)
                taskOfK[k] = rbk;
        int TaskK(Guid taskNodeId)
        {
            for (int k = 1; k <= n; k++)
                if (taskOfK.TryGetValue(k, out var rbk) && (rbk.SiteId == taskNodeId || suspendBlocks[k - 1].SourceNodeId == taskNodeId))
                    return k;
            throw new InvalidOperationException(
                $"Abort of Behaviour Task {taskNodeId:N} found no waiting task in graph '{graph.Name}' (BP1685 should have refused it).");
        }
        var abortedTasks = new List<int>();   // k of each aborted task, in label order
        foreach (var op in AllOps(graph.Blocks).OfType<IrOp_AbortTask>())
        {
            int k = TaskK(op.TaskNodeId);
            if (!abortedTasks.Contains(k)) abortedTasks.Add(k);
        }
        // ⭐ S7b — a task fiber's own task can be aborted from the graph that STARTED it (IrOp_AbortStartedTask, emitted
        //   elsewhere), so it always has an aborted label.
        if (graph.LiftedTaskSite is { } liftedSite)
        {
            int k = TaskK(liftedSite);
            if (!abortedTasks.Contains(k)) abortedTasks.Add(k);
        }
        int m = abortedTasks.Count;
        int total = n + m;

        int nextVal = MaxValueIdx(graph) + 1;
        int nextBlkId = graph.Blocks.Max(b => b.Id.Value) + 1;

        IrValue Alloc(IrTypeRef t) => new IrValue(nextVal++, t);
        IrBlockId NewBlk() => new IrBlockId(nextBlkId++);

        IrDebugAnnotation Synth(Guid? originNodeId = null) =>
            new IrDebugAnnotation { GraphId = graph.Id, Synthesized = "stage6-wait-lower-inst", OriginNodeId = originNodeId };

        IrStatement Stmt(IrValue? result, IrOperation op, Guid? originNodeId = null) =>
            new IrStatement { ResultValue = result, Operation = op, Debug = Synth(originNodeId) };

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

        // Chain blocks for dispatch (when N > 1). ⭐ S7a — over every label, the aborted ones included (m = 0 ⇒ unchanged).
        var chainBlockId = new IrBlockId[Math.Max(total, 1)]; // chainBlockId[k] checks ResumeAt==k (1-indexed)
        for (int k = 1; k <= total - 1; k++)
            chainBlockId[k] = NewBlk();

        // ⭐ S1 / I12 (DESIGN_Unified_Behaviour_Run §2) — a SUCCESS resume clears the cursor before it continues, exactly as
        //   the Failure and Delay paths already do. 🔴 It used to jump straight to the continuation with ResumeAt still k,
        //   so the next pass re-entered this resume check and re-ran ONLY the code after the wait. Allocated after the
        //   chain blocks so every pre-existing block keeps its id.
        var successBlockId = new IrBlockId[n + 1];
        for (int k = 1; k <= n; k++)
            successBlockId[k] = NewBlk();

        // ⭐ S7a — one "aborted" block per aborted task (label n + j).
        var abortedBlockId = new IrBlockId[m + 1];
        for (int j = 1; j <= m; j++)
            abortedBlockId[j] = NewBlk();
        IrBlockId Target(int label) => label <= n ? resumeCheckBlockId[label] : abortedBlockId[label - n];

        // ⭐ CE-2081 — a channel wait's "is my command still there?" check (allocated last: every earlier block keeps its id).
        var statusCheckBlockId = new IrBlockId[n + 1];
        for (int k = 1; k <= n; k++)
            if (suspendBlocks[k - 1].Statements.Select(st => st.Operation).FirstOrDefault(SuspendOps.Is) is IrOp_WaitForChannel w
                && IssuesOn(suspendBlocks[k - 1], w.ChannelComponentTypeFqn))
                statusCheckBlockId[k] = NewBlk();

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
            var originNodeId = sb.SourceNodeId;

            IrOperation? waitOp = sb.Statements
                .Select(s => s.Operation)
                .FirstOrDefault(SuspendOps.Is);

            var keptStmts = sb.Statements
                .Where(s => !SuspendOps.Is(s.Operation))
                .Where(s => !(s.ResultValue.HasValue && s.ResultValue.Value.Index == resumePointIdx))
                .ToList();

            keptStmts.Add(Stmt(null, new IrOp_WriteCursorResumeAt(k + 1), originNodeId));
            keptStmts.Add(Stmt(null, new IrOp_WriteCursorInstanceVersion(), originNodeId));

            if (waitOp is IrOp_LatentDelay ld)
            {
                // Compute time + duration and store as wait-until (relative, not absolute).
                var timeV = Alloc(SingleType);
                keptStmts.Add(Stmt(timeV, new IrOp_Time(), originNodeId));

                var waitUntilV = Alloc(SingleType);
                keptStmts.Add(Stmt(waitUntilV,
                    new IrOp_PureCall("op_Add_Single",
                        new[] { timeV, ld.Seconds },
                        SingleType), originNodeId));
                keptStmts.Add(Stmt(null, new IrOp_WriteCursorWaitUntilTime(waitUntilV), originNodeId));
            }

            modifiedBlocks[sb.Id.Value] = sb with
            {
                Statements = keptStmts,
                Terminator = new IrTerm_Return(null) { Debug = Synth(originNodeId) },
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

            IrBlockId elseTarget = total == 1 ? Target(1) : chainBlockId[1];

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
        for (int k = 1; k <= total - 1; k++)
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

            // ⭐ CE-2018 — the else goes to the NEXT LINK of the chain, and only the last link falls to the last check.
            //   🔴 It went to check[k+1] for every link, so with three or more waits ResumeAt == 3 resumed the SECOND
            //   wait (the third re-entered itself forever). Two waits — every golden — are byte-identical.
            IrBlockId elseOfChain = k + 1 <= total - 1 ? chainBlockId[k + 1] : Target(k + 1);

            synthesizedBlocks.Add(new IrBlock
            {
                Id         = chainBlockId[k],
                Label      = $"cursor_dispatch_chain_{k}",
                Statements = stmts,
                Terminator = new IrTerm_Branch(isKV,
                    Target(k), elseOfChain) { Debug = Synth() },
            });
        }

        // --- ⭐ S7a — aborted blocks: the task was aborted from its While Running chain ⇒ continue on Failed ---
        for (int j = 1; j <= m; j++)
        {
            var suspend = (IrTerm_Suspend)suspendBlocks[abortedTasks[j - 1] - 1].Terminator;
            synthesizedBlocks.Add(new IrBlock
            {
                Id         = abortedBlockId[j],
                Label      = $"resume_{abortedTasks[j - 1]}_aborted",
                Statements = new[] { Stmt(null, new IrOp_CheckCursorVersion()), Stmt(null, new IrOp_WriteCursorResumeAt(0)) },
                Terminator = suspend.FailureBlock is { } onFailBlk
                    ? new IrTerm_Goto(onFailBlk) { Debug = Synth() }
                    : UnwiredFailure(behaviorTick, Synth()),
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
                .FirstOrDefault(SuspendOps.Is);

            if (waitOp is IrOp_InlineActionCall or IrOp_RunBehavior)
            {
                // ⭐ S5d — Run Behaviour shares this path: the op is re-invoked each frame and its status routes the resume.
                // AN8 inline-latent: cursor-based re-invoke (Instance blueprint).
                // Check block: CheckCursorVersion + re-call action + branch on Running.
                var statusV    = Alloc(NodeStatusType);
                var constRunV  = Alloc(NodeStatusType);
                var isRunV     = Alloc(BoolType);

                var checkStmts = new List<IrStatement>
                {
                    Stmt(null,      new IrOp_CheckCursorVersion()),
                    Stmt(statusV,   waitOp),   // records are immutable ⇒ the same op, re-emitted here
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
                    // ⭐ S7a — a Behaviour Task's While Running chain runs each frame the child is still Running.
                    Terminator = suspend.WhileRunningBlock is { } wr
                        ? new IrTerm_Goto(wr) { Debug = Synth() }
                        : new IrTerm_Return(null) { Debug = Synth() },
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
                        failureBlockId[k], successBlockId[k]) { Debug = Synth() },
                });

                synthesizedBlocks.Add(new IrBlock
                {
                    Id         = successBlockId[k],
                    Label      = $"resume_{k}_success",
                    Statements = new[] { Stmt(null, new IrOp_WriteCursorResumeAt(0)) },
                    Terminator = new IrTerm_Goto(resumeBlockId) { Debug = Synth() },
                });

                synthesizedBlocks.Add(new IrBlock
                {
                    Id         = failureBlockId[k],
                    Label      = $"resume_{k}_failure",
                    Statements = new[] { Stmt(null, new IrOp_WriteCursorResumeAt(0)) },
                    // Q#13: route a channel-Failure resume to the wired OnFailure continuation when
                    // present; null FailureBlock (inline-action / unwired OnFailure) ⇒ plain return.
                    Terminator = suspend.FailureBlock is { } onFailBlk
                        ? new IrTerm_Goto(onFailBlk) { Debug = Synth() }
                        : UnwiredFailure(behaviorTick, Synth()),
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

                if (waitOp is IrOp_WaitForChannel && IssuesOn(sb, channelTypeFqn))
                {
                    // ⭐⭐ CE-2081 (R-203) — the command this wait waits on can be CANCELLED under it: a reaction's token resets
                    //   the channel (ChannelArbitrationSystem: ActiveAction = 0), and a resumed task must not wait on it forever
                    //   — nor take the reaction's finished Status for its own. The dispatcher never clears ActiveAction on
                    //   completion ⇒ ActiveAction == 0 while waiting means CANCELLED. ⇒ re-enter the issuing block when it is
                    //   safe to re-run (it re-evaluates the command's inputs, re-issues, re-arms the cursor — a first issue
                    //   again, as the BTree's leaf re-activation); otherwise the wait FAILS (the author's OnFailure).
                    //   📄 docs/DESIGN_Decision_Layer.md §4.9a.
                    var activeV    = Alloc(UInt16Type);
                    var zeroV      = Alloc(UInt16Type);
                    var cancelledV = Alloc(BoolType);
                    checkStmts.Add(Stmt(activeV,    new IrOp_FieldRead(channelV1, "ActiveAction", UInt16Type)));
                    checkStmts.Add(Stmt(zeroV,      new IrOp_Const("0", UInt16Type)));
                    checkStmts.Add(Stmt(cancelledV, new IrOp_Compare(activeV, zeroV,
                                                       Hrot.Blueprints.Core.Assets.ComparisonOperator.Equal)));
                    var reissueTarget = CanReissue(sb, channelTypeFqn) ? sb.Id : failureBlockId[k];

                    synthesizedBlocks.Add(new IrBlock
                    {
                        Id         = resumeCheckBlockId[k],
                        Label      = $"resume_{k}_channel_check",
                        Statements = checkStmts,
                        Terminator = new IrTerm_Branch(cancelledV, reissueTarget, statusCheckBlockId[k]) { Debug = Synth() },
                    });
                    synthesizedBlocks.Add(new IrBlock
                    {
                        Id         = statusCheckBlockId[k],
                        Label      = $"resume_{k}_channel_status",
                        Statements = Array.Empty<IrStatement>(),
                        Terminator = new IrTerm_Branch(isRunV, retReturnBlockId[k], notRunningBlockId[k]) { Debug = Synth() },
                    });
                }
                else
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
                        failureBlockId[k], successBlockId[k]) { Debug = Synth() },
                });

                synthesizedBlocks.Add(new IrBlock
                {
                    Id         = successBlockId[k],
                    Label      = $"resume_{k}_success",
                    Statements = new[] { Stmt(null, new IrOp_WriteCursorResumeAt(0)) },
                    Terminator = new IrTerm_Goto(resumeBlockId) { Debug = Synth() },
                });

                synthesizedBlocks.Add(new IrBlock
                {
                    Id         = failureBlockId[k],
                    Label      = $"resume_{k}_failure",
                    Statements = new[] { Stmt(null, new IrOp_WriteCursorResumeAt(0)) },
                    // Q#13: route a channel-Failure resume to the wired OnFailure continuation when
                    // present; null FailureBlock (inline-action / unwired OnFailure) ⇒ plain return.
                    Terminator = suspend.FailureBlock is { } onFailBlk
                        ? new IrTerm_Goto(onFailBlk) { Debug = Synth() }
                        : UnwiredFailure(behaviorTick, Synth()),
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
        for (int k = 1; k <= total - 1; k++)
            allCandidateBlocks.Add(synthesizedBlocks.First(b => b.Id.Value == chainBlockId[k].Value));

        // 4. Append check/retReturn/notRunning/failure blocks (in phase order).
        for (int k = 1; k <= n; k++)
        {
            if (synthesizedBlocks.Any(b => b.Id.Value == resumeCheckBlockId[k].Value))
                allCandidateBlocks.Add(synthesizedBlocks.First(b => b.Id.Value == resumeCheckBlockId[k].Value));
            // ⭐ CE-2081 — a channel wait's status check, behind its cancelled check.
            if (statusCheckBlockId[k].Value != 0 && synthesizedBlocks.Any(b => b.Id.Value == statusCheckBlockId[k].Value))
                allCandidateBlocks.Add(synthesizedBlocks.First(b => b.Id.Value == statusCheckBlockId[k].Value));
            if (synthesizedBlocks.Any(b => b.Id.Value == retReturnBlockId[k].Value))
                allCandidateBlocks.Add(synthesizedBlocks.First(b => b.Id.Value == retReturnBlockId[k].Value));
            if (synthesizedBlocks.Any(b => b.Id.Value == notRunningBlockId[k].Value))
                allCandidateBlocks.Add(synthesizedBlocks.First(b => b.Id.Value == notRunningBlockId[k].Value));
            if (synthesizedBlocks.Any(b => b.Id.Value == failureBlockId[k].Value))
                allCandidateBlocks.Add(synthesizedBlocks.First(b => b.Id.Value == failureBlockId[k].Value));
            // ⭐ S1 / I12 — the success block (clears the cursor, then continues).
            if (synthesizedBlocks.Any(b => b.Id.Value == successBlockId[k].Value))
                allCandidateBlocks.Add(synthesizedBlocks.First(b => b.Id.Value == successBlockId[k].Value));
        }

        // 5. ⭐ S7a — the aborted blocks, then every Abort becomes: reset the child + cursor to its aborted label.
        for (int j = 1; j <= m; j++)
            allCandidateBlocks.Add(synthesizedBlocks.First(b => b.Id.Value == abortedBlockId[j].Value));
        if (m > 0)
            allCandidateBlocks = allCandidateBlocks.Select(b => b with { Statements = RewriteAborts(b.Statements) }).ToList();

        IReadOnlyList<IrStatement> RewriteAborts(IReadOnlyList<IrStatement> list)
        {
            var result = new List<IrStatement>(list.Count);
            foreach (var st in list)
                switch (st.Operation)
                {
                    case IrOp_AbortTask at:
                    {
                        int k = TaskK(at.TaskNodeId);
                        result.Add(st with { Operation = new IrOp_ResetHostedSite(taskOfK[k].SiteId) });
                        result.Add(new IrStatement { Operation = new IrOp_WriteCursorResumeAt(n + abortedTasks.IndexOf(k) + 1), Debug = Synth() });
                        break;
                    }
                    case IrOp_ForEach fe: result.Add(st with { Operation = fe with { Body = RewriteAborts(fe.Body) } }); break;
                    case IrOp_If br:      result.Add(st with { Operation = br with { Then = RewriteAborts(br.Then), Else = RewriteAborts(br.Else) } }); break;
                    default: result.Add(st); break;
                }
            return result;
        }

        // Filter dead blocks (e.g. _unused blocks from LatentDelay path)
        // to prevent CS0162/CS0164.
        var finalBlocks = FilterDeadBlocks(allCandidateBlocks, dispatchBlockId);

        // ⭐ S7b — each abortable task's labels, for an abort emitted from another graph.
        var taskLabels = new Dictionary<Guid, (int Wait, int Aborted)>();
        for (int j = 0; j < abortedTasks.Count; j++)
            taskLabels[taskOfK[abortedTasks[j]].SiteId] = (abortedTasks[j], n + j + 1);

        return graph with { Blocks = finalBlocks, Entry = dispatchBlockId, TaskLabels = taskLabels.Count > 0 ? taskLabels : null };
    }

    /// <summary>Every operation of every block, nested loop / branch bodies included.</summary>
    private static IEnumerable<IrOperation> AllOps(IEnumerable<IrBlock> blocks)
    {
        static IEnumerable<IrOperation> Of(IEnumerable<IrStatement> list)
        {
            foreach (var st in list)
            {
                yield return st.Operation;
                var nested = st.Operation switch
                {
                    IrOp_ForEach fe => Of(fe.Body),
                    IrOp_If br      => Of(br.Then).Concat(Of(br.Else)),
                    _               => Enumerable.Empty<IrOperation>(),
                };
                foreach (var op in nested) yield return op;
            }
        }
        return blocks.SelectMany(b => Of(b.Statements));
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

    /// <summary>Q#13 — an unwired <c>OnFailure</c>: a behaviour Tick returns <c>Failure</c>; any other graph has no
    /// status to report and simply returns.</summary>
    private static IrTerminator UnwiredFailure(bool behaviorTick, IrDebugAnnotation debug)
        => behaviorTick
            ? new IrTerm_ReturnStatus(Hrot.Blueprints.Core.Assets.NodeStatus.Failure) { Debug = debug }
            : new IrTerm_Return(null) { Debug = debug };

    /// <summary>
    /// ⭐ <c>CE-2081</c> — may the suspend block <paramref name="sb"/> be RE-ENTERED to re-issue its channel command? Only when it
    /// issues a <see cref="IrOp_ChannelCommand"/> on that channel and everything else in it can run again with no effect but
    /// recomputing the same values (reads, pure calls, comparisons, the entry block's local reset, debug probes).
    /// </summary>
    /// <summary>⭐ <c>CE-2081</c> — does <paramref name="sb"/> issue a command on <paramref name="channelTypeFqn"/> itself? Only such a
    /// wait knows the command it waits on, so only it can tell that the command was cancelled (a wait on a channel commanded
    /// elsewhere keeps waiting on the status, as before).</summary>
    private static bool IssuesOn(IrBlock sb, string channelTypeFqn)
        => sb.Statements.Any(st => st.Operation is IrOp_ChannelCommand cc && cc.ChannelComponentTypeFqn == channelTypeFqn);

    private static bool CanReissue(IrBlock sb, string channelTypeFqn)
    {
        bool issues = false;
        foreach (var st in sb.Statements)
        {
            switch (st.Operation)
            {
                case IrOp_ChannelCommand cc:
                    if (cc.ChannelComponentTypeFqn != channelTypeFqn) return false;
                    issues = true;
                    break;
                case var op when SuspendOps.Is(op):
                case IrOp_Const: case IrOp_ReadParam: case IrOp_ReadVariable: case IrOp_ReadLocal: case IrOp_ReadInputArg:
                case IrOp_ResetLocals:
                case IrOp_Self: case IrOp_Time: case IrOp_DeltaTime: case IrOp_ReadInstanceVersion:
                case IrOp_PureCall: case IrOp_HasComponent: case IrOp_GetComponentRO: case IrOp_FieldRead:
                case IrOp_Compare: case IrOp_BinaryOp: case IrOp_BooleanOp: case IrOp_Not:
                case IrOp_MakeStruct: case IrOp_MakeTuple: case IrOp_TupleField: case IrOp_FormatString:
                case IrOp_ReadEqsResult: case IrOp_ReadRankedResult:
                case IrOp_DebugProbe_NodeEnter: case IrOp_DebugProbe_PinValue:
                    break;
                default:
                    return false;
            }
        }
        return issues;
    }
}

