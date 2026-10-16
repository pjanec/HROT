using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Xunit;

namespace Fdp.Toolkit.Orchestration.Tests;

/// <summary>
/// Unit and integration tests for <see cref="ClusterSlave"/> (G0402 success conditions).
/// </summary>
public sealed class ClusterSlaveTests
{
    // ── Stub IClusterStateHandler used by unit tests ───────────────────────────────

    private sealed class StubHandler : IClusterStateHandler
    {
        private readonly int _operationId;
        public int PrepareCallCount;
        public int CommitCallCount;

        public TaskCompletionSource<object?>? PrepareGate;

        public StubHandler(int operationId)
        {
            _operationId = operationId;
        }

        public bool CanHandle(NodeOpType operation) => operation == (NodeOpType)_operationId;

        public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
        {
            PrepareCallCount++;
            return PrepareGate?.Task ?? Task.FromResult<object?>(null);
        }

        public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo)
        {
            CommitCallCount++;
        }

        public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo) { }
    }

    // ── CGF1-G0402 success condition 1 ───────────────────────────────────

    /// <summary>
    /// Fact: Toolkit ClusterSlave dispatches PrepareAsync + Commit.
    /// A stub handler receives PrepareAsync then Commit on the same Tick() when
    /// PrepareAsync completes synchronously.
    /// </summary>
    [Fact]
    public void ClusterSlave_DispatchesPrepareAsyncAndCommit_SynchronousHandler()
    {
        const int opId = 5; // arbitrary non-CommitState id
        var handler = new StubHandler(opId);
        using var slave = new ClusterSlave();
        slave.RegisterHandler(handler);

        slave.EnqueueIntentForTest(new ExecuteNodeOpIntent
        {
            TransactionId = Guid.NewGuid(), TargetNodeId = 1,
            Operation = (NodeOpType)opId,
        });

        slave.Tick();

        Assert.Equal(1, handler.PrepareCallCount);
        Assert.Equal(1, handler.CommitCallCount);
    }

    /// <summary>
    /// Fact: Toolkit ClusterSlave defers Commit to next tick when PrepareAsync is async.
    /// </summary>
    [Fact]
    public void ClusterSlave_DeferCommit_WhenPrepareAsyncIsAsync()
    {
        const int opId = 5;
        var handler = new StubHandler(opId) { PrepareGate = new TaskCompletionSource<object?>() };
        using var slave = new ClusterSlave();
        slave.RegisterHandler(handler);

        slave.EnqueueIntentForTest(new ExecuteNodeOpIntent
        {
            TransactionId = Guid.NewGuid(), TargetNodeId = 1,
            Operation = (NodeOpType)opId,
        });

        slave.Tick(); // PrepareAsync started but not completed
        Assert.Equal(1, handler.PrepareCallCount);
        Assert.Equal(0, handler.CommitCallCount); // deferred

        handler.PrepareGate.SetResult(null); // complete the task
        slave.Tick(); // Commit should now fire
        Assert.Equal(1, handler.CommitCallCount);
    }

    /// <summary>
    /// ⭐⭐ <c>P4</c> (Q91 §4-B2, R-4001) — a deferred prepare is reported ONCE as <c>InProgress</c> (progress for the
    /// master, never the answer), and the answer follows when the prepare completes.
    /// </summary>
    [Fact]
    public void P4_ADeferredPrepare_ReportsInProgressOnce_ThenItsAnswer()
    {
        const int opId = 5;
        var handler = new StubHandler(opId) { PrepareGate = new TaskCompletionSource<object?>() };
        var bus = new FdpEventBus();
        using var slave = new ClusterSlave(bus, nodeId: 3);
        slave.RegisterHandler(handler);
        var txId = Guid.NewGuid();
        bus.PublishManaged(new ExecuteNodeOpIntent { TransactionId = txId, TargetNodeId = 3, Operation = (NodeOpType)opId });

        var seen = new List<OrchestrationStatusCode>();
        for (int frame = 0; frame < 3; frame++)   // ⭐ several frames while pending: still ONE report
        {
            bus.SwapBuffers();
            slave.Tick();
            bus.SwapBuffers();
            foreach (var ev in bus.ReadManaged<NodeOpCompletedEvent>())
                if (ev.TransactionId == txId) seen.Add(ev.StatusCode);
        }
        Assert.Equal(new[] { OrchestrationStatusCode.InProgress }, seen);

        handler.PrepareGate!.SetResult(null);
        bus.SwapBuffers();
        slave.Tick();
        bus.SwapBuffers();
        foreach (var ev in bus.ReadManaged<NodeOpCompletedEvent>())
            if (ev.TransactionId == txId) seen.Add(ev.StatusCode);

        Assert.Equal(new[] { OrchestrationStatusCode.InProgress, OrchestrationStatusCode.Success }, seen);
    }

    /// <summary>A handler that claims every abort and counts them — the shape <c>TerrainAssetHandler</c> has.</summary>
    private sealed class AbortCounter : IClusterStateHandler
    {
        public int Aborts;
        public bool CanHandle(NodeOpType operation) => operation == NodeOpType.AbortTransaction;
        public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct) => Task.FromResult<object?>(null);
        public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo) => Aborts++;
        public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo) { }
    }

    /// <summary>
    /// ⭐⭐⭐ <c>P8</c> (Q91 §4-H.3a, H7 + H8) — an abort naming the transaction of a STUCK prepare does not wait behind it:
    /// it preempts it (the stuck op is answered <c>Cancelled</c>, its queued <c>CommitState</c> is dropped), and it reaches
    /// EVERY handler that claims aborts. ⛔ Before: the abort queued behind the prepare forever, and only the first claimant
    /// would ever have seen one.
    /// </summary>
    [Fact]
    public void P8_AnAbort_PreemptsTheStuckPrepareOfItsTransaction_AndReachesEveryClaimant()
    {
        const int opId = 5;
        var stuck = new StubHandler(opId) { PrepareGate = new TaskCompletionSource<object?>() };   // never completes
        var first = new AbortCounter();
        var second = new AbortCounter();
        var bus = new FdpEventBus();
        using var slave = new ClusterSlave(bus, nodeId: 3);
        slave.RegisterHandler(stuck);
        slave.RegisterHandler(first);
        slave.RegisterHandler(second);

        var loadTx = Guid.NewGuid();
        bus.PublishManaged(new ExecuteNodeOpIntent { TransactionId = loadTx, TargetNodeId = 3, Operation = (NodeOpType)opId });
        bus.PublishManaged(new ExecuteNodeOpIntent
        {
            TransactionId = loadTx, TargetNodeId = 3, Operation = NodeOpType.CommitState,
            DomainPayload = new CommitStatePayload(ClusterState.OperatingLive),
        });
        bus.SwapBuffers();
        slave.Tick();                    // the prepare is now stuck; CommitState is queued behind it
        bus.SwapBuffers();
        _ = bus.ReadManaged<NodeOpCompletedEvent>().ToList();

        var abortTx = Guid.NewGuid();
        bus.PublishManaged(new ExecuteNodeOpIntent
        {
            TransactionId = abortTx, TargetNodeId = 0, Operation = NodeOpType.AbortTransaction,
            DomainPayload = new AbortTransactionPayload(loadTx),
        });
        bus.SwapBuffers();
        slave.Tick();
        bus.SwapBuffers();
        var answers = bus.ReadManaged<NodeOpCompletedEvent>().ToList();

        Assert.Equal(1, first.Aborts);
        Assert.Equal(1, second.Aborts);   // ⭐ H7 — every claimant, not the first only
        Assert.Contains(answers, a => a.TransactionId == loadTx && a.StatusCode == OrchestrationStatusCode.Cancelled);
        Assert.Contains(answers, a => a.TransactionId == abortTx && a.StatusCode == OrchestrationStatusCode.Success && a.IsParticipating);

        slave.Tick();   // ⭐ the queued CommitState of the aborted load was dropped — the node never reaches OperatingLive
        Assert.NotEqual((int)ClusterState.OperatingLive, slave.LocalStateIdForTest);
        Assert.Equal(0, stuck.CommitCallCount);
    }

    /// <summary>
    /// ⭐⭐ C2 (Q91 §4-I I5, R-4006) — a node follows ONE cluster session: the first one it sees is adopted (no reset), a
    /// DIFFERENT one drops it to <c>Idle</c> and forgets the old master's queued work, and the master's writer gone does the
    /// same. The heartbeat carries the session it follows.
    /// </summary>
    [Fact]
    public void C2_ANodeFollowsOneSession_AndANewSessionOrAGoneMasterDropsItToIdle()
    {
        const int opId = 5;
        var stuck = new StubHandler(opId) { PrepareGate = new TaskCompletionSource<object?>() };
        var bus = new FdpEventBus();
        using var slave = new ClusterSlave(bus, nodeId: 3);
        slave.RegisterHandler(stuck);

        var first = Guid.NewGuid();
        bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = ClusterState.Idle, SessionId = first });
        bus.PublishManaged(new ExecuteNodeOpIntent
        {
            TransactionId = Guid.NewGuid(), TargetNodeId = 3, Operation = NodeOpType.CommitState,
            DomainPayload = new CommitStatePayload(ClusterState.OperatingLive),
        });
        bus.SwapBuffers();
        slave.Tick();
        Assert.Equal(first, slave.FollowingSessionId);
        Assert.Equal((int)ClusterState.OperatingLive, slave.LocalStateIdForTest);   // ⭐ adopting the first session resets nothing

        bus.PublishManaged(new ExecuteNodeOpIntent { TransactionId = Guid.NewGuid(), TargetNodeId = 3, Operation = (NodeOpType)opId });
        bus.SwapBuffers();
        slave.Tick();                                              // a prepare of the old session is now stuck
        var second = Guid.NewGuid();
        bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = ClusterState.Idle, SessionId = second });
        bus.SwapBuffers();
        slave.Tick();

        Assert.Equal(second, slave.FollowingSessionId);
        Assert.Equal((int)ClusterState.Idle, slave.LocalStateIdForTest);   // ⭐ a new session ⇒ Idle

        bus.PublishManaged(new ExecuteNodeOpIntent
        {
            TransactionId = Guid.NewGuid(), TargetNodeId = 3, Operation = NodeOpType.CommitState,
            DomainPayload = new CommitStatePayload(ClusterState.OperatingLive),
        });
        bus.SwapBuffers();
        slave.Tick();
        Assert.Equal((int)ClusterState.OperatingLive, slave.LocalStateIdForTest);   // ⭐ the old stuck prepare no longer blocks it

        bus.PublishManaged(new ClusterStateUpdateEvent { MasterGone = true });
        bus.SwapBuffers();
        slave.Tick();
        Assert.Equal(Guid.Empty, slave.FollowingSessionId);
        Assert.Equal((int)ClusterState.Idle, slave.LocalStateIdForTest);   // ⭐ the master gone ⇒ Idle
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-4000</c> F2 / <c>R-4019</c> — the node ENTERING <c>Idle</c> tells every world-owning handler, once, on BOTH
    /// routes: a committed transition and a drop to <c>Idle</c> (here: the master gone). ⛔ Not on a commit to another state, and
    /// not again while it stays in <c>Idle</c>.
    /// </summary>
    [Fact]
    public void CE4000_EnteringIdle_TellsTheWorldOwner_OnceOnEachRoute()
    {
        var bus = new FdpEventBus();
        using var slave = new ClusterSlave(bus, nodeId: 3);
        var owner = new IdleCounter();
        slave.RegisterHandler(owner);

        void Commit(ClusterState to)
        {
            bus.PublishManaged(new ExecuteNodeOpIntent
            {
                TransactionId = Guid.NewGuid(), TargetNodeId = 3, Operation = NodeOpType.CommitState,
                DomainPayload = new CommitStatePayload(to),
            });
            bus.SwapBuffers();
            slave.Tick();
        }

        Commit(ClusterState.OperatingLive);
        Assert.Equal(0, owner.Entered);
        Commit(ClusterState.Idle);
        Assert.Equal(1, owner.Entered);          // ⭐ the committed route
        Commit(ClusterState.Idle);
        Assert.Equal(1, owner.Entered);          // ⛔ staying in Idle is not entering it

        bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = ClusterState.Idle, SessionId = Guid.NewGuid() });   // follow a session
        Commit(ClusterState.OperatingLive);
        bus.PublishManaged(new ClusterStateUpdateEvent { MasterGone = true });
        bus.SwapBuffers();
        slave.Tick();
        Assert.Equal((int)ClusterState.Idle, slave.LocalStateIdForTest);
        Assert.Equal(2, owner.Entered);          // ⭐ the drop route
    }

    /// <summary>
    /// ⭐⭐⭐ <c>R-4007</c> (Q91 §4-M) — the join freeze: a node is frozen only when it is BEHIND the cluster it follows (it is
    /// <c>Idle</c>, the cluster is not). ⛔ A node with no master, and a cluster at rest in <c>Idle</c>, are never frozen — 📐
    /// dozens of integration suites spawn and replicate exactly there.
    /// </summary>
    [Fact]
    public void R4007_ANodeIsJoinFrozen_OnlyWhileItIsBehindTheClusterItFollows()
    {
        var bus = new FdpEventBus();
        using var slave = new ClusterSlave(bus, nodeId: 3);
        void Cluster(ClusterState state, Guid session)
        {
            bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = state, SessionId = session });
            bus.SwapBuffers();
            slave.Tick();
        }

        Assert.False(slave.IsJoinFrozen);                         // no master
        var session = Guid.NewGuid();
        Cluster(ClusterState.Idle, session);
        Assert.False(slave.IsJoinFrozen);                         // a cluster at rest
        Cluster(ClusterState.OperatingLive, session);
        Assert.True(slave.IsJoinFrozen);                          // ⭐ a late joiner: Idle while the cluster runs

        bus.PublishManaged(new ExecuteNodeOpIntent
        {
            TransactionId = Guid.NewGuid(), TargetNodeId = 3, Operation = NodeOpType.CommitState,
            DomainPayload = new CommitStatePayload(ClusterState.LoadingLive),
        });
        bus.SwapBuffers();
        slave.Tick();
        Assert.False(slave.IsJoinFrozen);                         // ⭐ it committed a load: the gate opens

        bus.PublishManaged(new ClusterStateUpdateEvent { MasterGone = true });
        bus.SwapBuffers();
        slave.Tick();
        Assert.False(slave.IsJoinFrozen);                         // dropped to Idle with no master to be behind
    }

    private sealed class IdleCounter : IIdleResetHandler
    {
        public int Entered { get; private set; }
        public void OnEnteredIdle() => Entered++;
        public bool CanHandle(NodeOpType operation) => false;
        public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct) => Task.FromResult<object?>(null);
        public void Commit(ExecuteNodeOpIntent intent, Fdp.Core.EntityRepository? repo) { }
        public void Abort(ExecuteNodeOpIntent intent, Fdp.Core.EntityRepository? repo) { }
    }

    // ── CGF1-G0402 success condition 2 ───────────────────────────────────

    /// <summary>
    /// Fact: Toolkit ClusterSlave deduplicates transactions.
    /// Two commands with the same TransactionId — handler PrepareAsync called only once.
    /// </summary>
    [Fact]
    public void ClusterSlave_DeduplicatesTransactions()
    {
        const int opId = 7;
        var handler = new StubHandler(opId);
        using var slave = new ClusterSlave();
        slave.RegisterHandler(handler);

        var txId = Guid.NewGuid();
        var intent = new ExecuteNodeOpIntent
        {
            TransactionId = txId, TargetNodeId = 1,
            Operation = (NodeOpType)opId,
        };

        slave.EnqueueIntentForTest(intent);
        slave.EnqueueIntentForTest(intent); // duplicate

        slave.Tick();

        Assert.Equal(1, handler.PrepareCallCount);  // second silently dropped
        Assert.Equal(1, handler.CommitCallCount);
    }

    // ── CGF1-G0402 success condition 3 ───────────────────────────────────

    /// <summary>
    /// Fact: TkClusterStateChangedEvent published on CommitState.
    /// CommitState(nextStateId=5) → TkClusterStateChangedEvent{PreviousStateId=0, NextStateId=5}.
    /// </summary>
    [Fact]
    public void ClusterSlave_PublishesTkClusterStateChangedEvent_OnCommitState()
    {
        const int CommitState = 2; // NodeOpType.CommitState
        const int nextState   = 5;

        var eventBus = new FdpEventBus();
        using var slave = new ClusterSlave(eventBus);

        slave.EnqueueIntentForTest(new ExecuteNodeOpIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetNodeId  = 0,
            Operation     = NodeOpType.CommitState,
            DomainPayload = new CommitStatePayload((ClusterState)nextState),
        });

        slave.Tick();
        eventBus.SwapBuffers();

        var events = new List<TkClusterStateChangedEvent>();
        foreach (var e in eventBus.Read<TkClusterStateChangedEvent>())
            events.Add(e);

        Assert.Single(events);
        Assert.Equal(0,         events[0].PreviousStateId);
        Assert.Equal(nextState, events[0].NextStateId);
        Assert.Equal(nextState, slave.LocalStateIdForTest);
    }

    // ── CMC-S006: Bus dispatch tests ──────────────────────────────────────

    /// <summary>
    /// CMC-S006 test 1: ClusterSlave constructed with only an eventBus (no transport)
    /// dispatches intents consumed from the bus.
    /// </summary>
    [Fact]
    public void ClusterSlave_BusDispatch_CallsHandlerWhenIntentOnBus()
    {
        const int opId = 5;
        var handler  = new StubHandler(opId);
        var eventBus = new FdpEventBus();
        using var slave = new ClusterSlave(eventBus);
        slave.RegisterHandler(handler);

        eventBus.PublishManaged(new ExecuteNodeOpIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetNodeId  = 0,
            Operation     = (NodeOpType)opId,
        });
        eventBus.SwapBuffers();

        slave.Tick();

        Assert.Equal(1, handler.PrepareCallCount);
        Assert.Equal(1, handler.CommitCallCount);
    }

    /// <summary>
    /// CMC-S006 test 2: After Tick() processes a bus intent, ClusterSlave publishes
    /// a NodeOpCompletedEvent with IsParticipating = true on the bus.
    /// </summary>
    [Fact]
    public void ClusterSlave_BusDispatch_PublishesNodeOpCompletedEvent()
    {
        const int opId = 5;
        var handler  = new StubHandler(opId);
        var eventBus = new FdpEventBus();
        using var slave = new ClusterSlave(eventBus);
        slave.RegisterHandler(handler);

        var txId = Guid.NewGuid();
        eventBus.PublishManaged(new ExecuteNodeOpIntent
        {
            TransactionId = txId,
            TargetNodeId  = 0,
            Operation     = (NodeOpType)opId,
        });
        eventBus.SwapBuffers();

        slave.Tick();
        eventBus.SwapBuffers();

        var completed = new List<NodeOpCompletedEvent>();
        foreach (var e in eventBus.ReadManaged<NodeOpCompletedEvent>())
            completed.Add(e);

        Assert.Single(completed);
        Assert.Equal(txId, completed[0].TransactionId);
        Assert.True(completed[0].IsParticipating);
        Assert.Equal(OrchestrationStatusCode.Success, completed[0].StatusCode);
    }

    /// <summary>
    /// CMC-S006 test 3: ClusterSlave publishes NodeHeartbeatEvent after 1 second elapses.
    /// </summary>
    [Fact]
    public void ClusterSlave_PublishesNodeHeartbeatEvent_AfterOneSecond()
    {
        const int nodeId = 42;
        var eventBus = new FdpEventBus();
        using var slave = new ClusterSlave(nodeId, "TestNode", eventBus);

        // Wait long enough for the heartbeat timer to fire (>1 s).
        Thread.Sleep(1100);

        slave.Tick();
        eventBus.SwapBuffers();

        var heartbeats = new List<NodeHeartbeatEvent>();
        foreach (var e in eventBus.ReadManaged<NodeHeartbeatEvent>())
            heartbeats.Add(e);

        Assert.Single(heartbeats);
        Assert.Equal(nodeId, heartbeats[0].NodeId);
        Assert.Equal("TestNode", heartbeats[0].SubsystemName);
    }

    /// <summary>
    /// ⭐⭐ A <see cref="ClusterSlave"/> registers the orchestration vocabulary on the bus it is
    /// handed, so it can always publish what it publishes — <b>without</b> its host having
    /// remembered to call <c>OrchestrationEventRegistry.RegisterAll</c>.
    ///
    /// <para>
    /// 🔴 <b>The crash this locks.</b> Under <c>--mode all</c> each subsystem gets its OWN isolated
    /// bus, so a <c>RegisterAll</c> in one subsystem's bootstrap does nothing for another's. CGF,
    /// Editor and <c>HrotNodeBuilder</c> called it; <b>IG did not</b>. IG's first <c>Tick()</c>
    /// published <c>NodeHeartbeatEvent</c> on an unregistered stream and strict mode took the whole
    /// process down on frame one.
    /// </para>
    ///
    /// <para>
    /// ⚠ Asserts against <b>strict mode ON</b> — with it off, an unregistered publish auto-creates
    /// the stream and the test would pass whether or not the fix exists.
    /// </para>
    /// </summary>
    [Fact]
    public void ClusterSlave_RegistersItsOwnEvents_OnABusItsHostNeverPrepared()
    {
        bool previous = FdpConfig.EnforceExplicitEventRegistration;
        FdpConfig.EnforceExplicitEventRegistration = true;
        try
        {
            // A bare bus — deliberately NOT passed through OrchestrationEventRegistry.RegisterAll,
            // exactly as IgSubsystem's bus arrived.
            var eventBus = new FdpEventBus();
            using var slave = new ClusterSlave(7, "IG", eventBus);

            Thread.Sleep(1100);

            var ex = Record.Exception(() => slave.Tick());

            Assert.Null(ex);   // was: InvalidOperationException "Strict Mode Violation"
        }
        finally
        {
            FdpConfig.EnforceExplicitEventRegistration = previous;
        }
    }

    /// <summary>
    /// A null bus means "no I/O" (the offline/test shape) — construction must stay silent rather
    /// than trying to register anything.
    /// </summary>
    [Fact]
    public void ClusterSlave_WithNoBus_ConstructsAndTicksWithoutThrowing()
    {
        using var slave = new ClusterSlave(1, "Headless", eventBus: null);

        Assert.Null(Record.Exception(() => slave.Tick()));
    }

    /// <summary>
    /// CMC-S006 test 4: ClusterSlave(null) does not throw when Tick() is called with no bus.
    /// </summary>
    [Fact]
    public void ClusterSlave_NullBus_DoesNotThrowOnTick()
    {
        using var slave = new ClusterSlave(eventBus: null);
        var ex = Record.Exception(() => slave.Tick());
        Assert.Null(ex);
    }

    // ── DEBT-007 multi-intent queue tests ─────────────────────────────────

    /// <summary>
    /// DEBT-007 test 1: CommitState intent queued in the same write cycle as an async Prepare
    /// survives the next SwapBuffers() and is dispatched after the prepare completes.
    /// </summary>
    [Fact]
    public void Queue_Survives_SwapBuffers_When_AsyncPrepareIsActive()
    {
        const int opId     = 5;
        const int targetState = 10;
        var txId    = Guid.NewGuid();
        var handler = new StubHandler(opId) { PrepareGate = new TaskCompletionSource<object?>() };
        var eventBus = new FdpEventBus();
        using var slave = new ClusterSlave(eventBus);
        slave.RegisterHandler(handler);

        // Publish PrepareXxx + CommitState in the same write buffer.
        eventBus.PublishManaged(new ExecuteNodeOpIntent
        {
            TransactionId = txId,
            TargetNodeId  = 0,
            Operation     = (NodeOpType)opId,
        });
        eventBus.PublishManaged(new ExecuteNodeOpIntent
        {
            TransactionId = txId,
            TargetNodeId  = 0,
            Operation     = NodeOpType.CommitState,
            DomainPayload = new CommitStatePayload((ClusterState)targetState),
        });

        // SwapBuffers + Tick: PrepareXxx starts async; CommitState should be buffered internally.
        eventBus.SwapBuffers();
        slave.Tick();

        Assert.Equal(1, handler.PrepareCallCount);
        Assert.Equal(0, handler.CommitCallCount); // still pending
        Assert.Equal(0, slave.LocalStateIdForTest); // CommitState not yet applied

        // Complete the async prepare; SwapBuffers clears the old read buffer (CommitState already saved).
        handler.PrepareGate!.SetResult(null);
        eventBus.SwapBuffers();

        // Next tick: pending prepare resolves + buffered CommitState dispatched.
        slave.Tick();

        Assert.Equal(1, handler.CommitCallCount);
        Assert.Equal(targetState, slave.LocalStateIdForTest);
    }

    /// <summary>
    /// DEBT-007 test 2: Two CommitState intents for different target states in the same
    /// transaction (multi-step trajectory) are both applied — neither is treated as a duplicate.
    /// Before the DEBT-007 dedup key fix, the second CommitState was dropped because
    /// (txId, CommitState) was the same key regardless of DomainPayload.
    /// </summary>
    [Fact]
    public void MultiStep_Trajectory_BothCommitStatesApplied()
    {
        const int stateLoading   = 3;  // LoadingLive
        const int stateOperating = 4;  // OperatingLive
        var txId = Guid.NewGuid();
        using var slave = new ClusterSlave();

        // First CommitState: Idle → LoadingLive.
        slave.EnqueueIntentForTest(new ExecuteNodeOpIntent
        {
            TransactionId = txId,
            Operation     = NodeOpType.CommitState,
            DomainPayload = new CommitStatePayload((ClusterState)stateLoading),
        });
        Assert.Equal(stateLoading, slave.LocalStateIdForTest);

        // Second CommitState: same txId but different target state → must NOT be dropped as duplicate.
        slave.EnqueueIntentForTest(new ExecuteNodeOpIntent
        {
            TransactionId = txId,
            Operation     = NodeOpType.CommitState,
            DomainPayload = new CommitStatePayload((ClusterState)stateOperating),
        });
        Assert.Equal(stateOperating, slave.LocalStateIdForTest);
    }

    /// <summary>
    /// DEBT-007 test 3: When a prepare faults, the buffered pending intents queue is cleared
    /// so the subsequent CommitState for that transaction is NOT dispatched.
    /// </summary>
    [Fact]
    public void FaultedPrepare_ClearsPendingQueue()
    {
        const int opId = 5;
        const int targetState = 99;
        var txId    = Guid.NewGuid();
        var handler = new StubHandler(opId) { PrepareGate = new TaskCompletionSource<object?>() };
        var eventBus = new FdpEventBus();
        using var slave = new ClusterSlave(eventBus);
        slave.RegisterHandler(handler);

        // Publish PrepareXxx + CommitState in the same write cycle.
        eventBus.PublishManaged(new ExecuteNodeOpIntent
        {
            TransactionId = txId, TargetNodeId = 0,
            Operation     = (NodeOpType)opId,
        });
        eventBus.PublishManaged(new ExecuteNodeOpIntent
        {
            TransactionId = txId, TargetNodeId = 0,
            Operation     = NodeOpType.CommitState, DomainPayload = new CommitStatePayload((ClusterState)targetState),
        });

        eventBus.SwapBuffers();
        slave.Tick(); // async prepare starts; CommitState buffered internally

        Assert.Equal(0, handler.CommitCallCount);

        // Fault the prepare.
        handler.PrepareGate!.SetException(new InvalidOperationException("simulated fault"));
        eventBus.SwapBuffers();
        slave.Tick(); // faulted path clears _pendingIntents

        // CommitState must NOT have been applied.
        Assert.Equal(0, slave.LocalStateIdForTest);
        Assert.Equal(0, handler.CommitCallCount);
    }

    // ── TASK-D01: CommitStatePayload dispatch and dedup tests ─────────────────

    /// <summary>
    /// CommitState intent with CommitStatePayload (not boxed int) correctly updates local state.
    /// </summary>
    [Fact]
    public void ClusterSlave_CommitState_WithCommitStatePayload_UpdatesLocalState()
    {
        var bus   = new FdpEventBus();
        var slave = new ClusterSlave(1, "Test", bus);
        var txId  = Guid.NewGuid();

        slave.EnqueueIntentForTest(new ExecuteNodeOpIntent
        {
            TransactionId = txId,
            TargetNodeId  = 1,
            Operation     = NodeOpType.CommitState,
            DomainPayload = new CommitStatePayload(TargetState: (ClusterState)5),
        });

        Assert.Equal(5, slave.LocalStateIdForTest);
    }

    /// <summary>
    /// Two CommitState intents with different TargetState values should each be processed once
    /// (dedup key includes TargetState discriminant).
    /// </summary>
    [Fact]
    public void ClusterSlave_CommitState_DeduplicatesOnStateId()
    {
        var bus   = new FdpEventBus();
        var slave = new ClusterSlave(1, "Test", bus);
        var txId  = Guid.NewGuid();

        // Same transaction but two different target states
        slave.EnqueueIntentForTest(new ExecuteNodeOpIntent
        {
            TransactionId = txId,
            TargetNodeId  = 1,
            Operation     = NodeOpType.CommitState,
            DomainPayload = new CommitStatePayload((ClusterState)2),
        });
        slave.EnqueueIntentForTest(new ExecuteNodeOpIntent
        {
            TransactionId = txId,
            TargetNodeId  = 1,
            Operation     = NodeOpType.CommitState,
            DomainPayload = new CommitStatePayload((ClusterState)5),
        });

        // Last CommitState wins
        Assert.Equal(5, slave.LocalStateIdForTest);
    }
}
