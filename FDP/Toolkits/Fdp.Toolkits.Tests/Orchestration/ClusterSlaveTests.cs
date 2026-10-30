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
    /// ⭐⭐ <c>CE-4096</c> (AQ108 §2.6) — the start gate's input, as a PUBLIC read of what the slave already follows: the cluster state the
    /// master last announced (NOT the node's own committed state), whether any has been heard, and whether the master is gone — set by the
    /// MasterGone broadcast, cleared by the next real one.
    /// </summary>
    [Fact]
    public void CE4096_TheSlaveExposesTheFollowedClusterState_AndMasterGone()
    {
        var bus = new FdpEventBus();
        using var slave = new ClusterSlave(bus, nodeId: 3);
        void Hear(ClusterStateUpdateEvent ev) { bus.PublishManaged(ev); bus.SwapBuffers(); slave.Tick(); }

        Assert.False(slave.HasFollowedState);                    // ⭐ "no broadcast yet" is distinguishable from a heard Idle
        Assert.False(slave.MasterGone);

        var session = Guid.NewGuid();
        Hear(new ClusterStateUpdateEvent { CurrentState = ClusterState.Idle, SessionId = session });
        Assert.True(slave.HasFollowedState);
        Assert.Equal(ClusterState.Idle, slave.FollowedClusterState);

        Hear(new ClusterStateUpdateEvent { CurrentState = ClusterState.LoadingLive, SessionId = session });
        Assert.Equal(ClusterState.LoadingLive, slave.FollowedClusterState);   // ⭐ the cluster's, while this node's own commit is still Idle
        Assert.Equal(ClusterState.Idle, slave.LocalClusterState);
        Assert.False(slave.MasterGone);

        Hear(new ClusterStateUpdateEvent { MasterGone = true });
        Assert.True(slave.MasterGone);

        Hear(new ClusterStateUpdateEvent { CurrentState = ClusterState.OperatingEdit, SessionId = Guid.NewGuid() });
        Assert.False(slave.MasterGone);                          // ⭐ cleared by the next real broadcast
        Assert.Equal(ClusterState.OperatingEdit, slave.FollowedClusterState);
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-1436</c> (R-3066, design V3) — the slave's follow-fields are the node's ONE <see cref="ClusterView"/>, not a second copy: after one
    /// broadcast and one inventory every follow-field equals the view's, the check round's outcome is readable from the same view, and "no inventory
    /// yet" stays <c>null</c> (not an empty list). 🔴 Mutation: <c>FollowedClusterState</c> returning a constant (a private copy that never folds) ⇒ the
    /// state assertion fails; <c>KnownExercises</c> returning <c>View.AvailableExercises</c> unconditionally ⇒ the first null assertion fails.
    /// </summary>
    [Fact]
    public void CE1436_TheSlaveFollowFields_AreTheOneClusterView()
    {
        var bus = new FdpEventBus();
        using var slave = new ClusterSlave(bus, nodeId: 3);
        void Hear<T>(T ev) { bus.PublishManaged(ev); bus.SwapBuffers(); slave.Tick(); }

        Assert.Null(slave.KnownExercises);                       // no inventory yet ⇒ null, as before
        Assert.False(slave.View.HasInventory);

        Hear(new ClusterStateUpdateEvent
        {
            CurrentState = ClusterState.OperatingEdit, SessionId = Guid.NewGuid(), ReplayAxis = ReplayAxis.Wall, RunNotRecorded = true, RecordLiveRunsOff = true, AskToKeepRecordings = true,
        });
        var ex = new ExerciseInventoryItem(Guid.NewGuid(), DateTime.UtcNow, TimeSpan.FromSeconds(1), "s");
        Hear(new AssetInventoryUpdateEvent { LocalExercises = new[] { ex } });
        var check = Guid.NewGuid();
        Hear(new ClusterOpCompletedEvent { RequestId = check, StatusCode = OrchestrationStatusCode.Success, ResultPayload = new UnsavedCheckResult(true, new[] { 2 }) });

        Assert.Equal(ClusterState.OperatingEdit, slave.View.State);
        Assert.Equal(slave.View.State, slave.FollowedClusterState);
        Assert.Equal((slave.View.HasFollowedState, slave.View.MasterGone, slave.View.ReplayAxis, slave.View.RunNotRecorded, slave.View.RecordLiveRunsOff),
                     (slave.HasFollowedState, slave.MasterGone, slave.ReplayAxis, slave.RunNotRecorded, slave.RecordLiveRunsOff));
        Assert.True(slave.RunNotRecorded && slave.RecordLiveRunsOff && slave.ReplayAxis == ReplayAxis.Wall);
        Assert.True(slave.AskToKeepRecordings && slave.View.AskToKeepRecordings);   // ⭐ CE-4100 — the node's accessor reads the ONE view
        Assert.Same(slave.View.AvailableExercises, slave.KnownExercises);
        Assert.True(slave.View.UnsavedCheckOutcomeFor(check)!.Result!.Changed);   // the round's answer reaches the node's view (op status on every node)
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

    /// <summary>
    /// ⭐⭐ <c>CE-3344</c> (R-3069) — the capability advert (the node's explicit "I am ready" report) goes out on the FIRST <c>Tick</c> by default — and a host that called
    /// <c>HoldReady()</c> before it sends the advert only after <c>MarkReady()</c>; <c>HoldReady()</c> after the advert is too late and throws.
    /// 🔴 Mutations (each compiles): <c>Tick</c> ignoring <c>_readyHeld</c> ⇒ the held cell reds; <c>MarkReady</c> a no-op ⇒ the released cell reds.
    /// </summary>
    [Fact]
    public void CE3344_TheAdvertGoesOutOnTheFirstTick_UnlessTheHostHeldReadyUntilMarkReady()
    {
        static int Adverts(FdpEventBus bus) { bus.SwapBuffers(); return bus.ReadManaged<NodeCapabilitiesEvent>().Count(); }

        var plainBus = new FdpEventBus();
        using (var plain = new ClusterSlave(plainBus, nodeId: 1, subsystemName: "Plain"))
        {
            plain.Tick();
            Assert.Equal(1, Adverts(plainBus));                 // today's behaviour: the first Tick IS the ready report
            Assert.Throws<InvalidOperationException>(() => plain.HoldReady());   // too late: the advert is durable and went out
        }

        var bus = new FdpEventBus();
        using var held = new ClusterSlave(bus, nodeId: 2, subsystemName: "Held");
        held.HoldReady();
        Assert.True(held.IsReadyHeld);
        held.Tick(); held.Tick();
        Assert.Equal(0, Adverts(bus));                          // the host is still initialising: no report

        held.MarkReady();
        Assert.False(held.IsReadyHeld);
        held.Tick();
        var advert = Assert.Single(ReadAdverts(bus));
        Assert.Equal(2, advert.NodeId);

        held.Tick();
        Assert.Empty(ReadAdverts(bus));                         // once
    }

    private static List<NodeCapabilitiesEvent> ReadAdverts(FdpEventBus bus) { bus.SwapBuffers(); return bus.ReadManaged<NodeCapabilitiesEvent>().ToList(); }

    // ── ⭐⭐ CE-3345 part B (R-3070, R-3072, AQ110 A) — the readiness report on the heartbeat, the load's progress, the cancel ────────────────

    /// <summary>A node's LOAD participant as the slave sees it: an async prepare (gated by the test) that names the step it is on, a REAL token it records, and an abort counter.</summary>
    private sealed class FakeLoad : ILoadProgressSource
    {
        public string? Step;
        public TaskCompletionSource<object?> Gate = new();
        public CancellationToken Token;
        public int Aborts;
        public string? CurrentLoadStep => Step;
        public bool CanHandle(NodeOpType operation) => operation == NodeOpType.PrepareLive;
        public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct) { Token = ct; return Gate.Task; }
        public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo) { }
        public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo) => Aborts++;
    }

    private static List<NodeHeartbeatEvent> Beats(FdpEventBus bus) { bus.SwapBuffers(); return bus.ReadManaged<NodeHeartbeatEvent>().ToList(); }
    private static List<NodeOpCompletedEvent> Acks(FdpEventBus bus) { bus.SwapBuffers(); return bus.ReadManaged<NodeOpCompletedEvent>().ToList(); }

    private static Guid Send(FdpEventBus bus, int nodeId, NodeOpType op, object? payload = null)
    {
        var tx = Guid.NewGuid();
        bus.PublishManaged(new ExecuteNodeOpIntent { TransactionId = tx, TargetNodeId = nodeId, Operation = op, DomainPayload = payload });
        return tx;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3345</c> success condition 1 — a node's heartbeat goes <b>Booting → Ready → Loading (the step names, in order) → PlayReady</b> through a load; and <b>Failed + the reason</b> after a failed prepare.
    /// Booting is what a host that called <c>HoldReady()</c> reports until <c>MarkReady()</c> (a default host's advert goes out before its first beat).
    /// 🔴 Mutations (each compiles): <c>Phase</c> ignoring <c>_readyHeld</c> ⇒ the Booting cell reds; <c>LoadStep</c> returning <c>null</c> ⇒ the step cells red; the <c>OperatingLive</c> arm removed from <c>Phase</c> ⇒ the PlayReady cell reds;
    /// <c>FailPrepare</c> not setting <c>_failureReason</c> ⇒ the Failed cell reds.
    /// </summary>
    [Fact]
    public void CE3345_TheHeartbeat_GoesBooting_Ready_Loading_WithTheStepsInOrder_PlayReady_AndFailedWithItsReason()
    {
        var bus = new FdpEventBus();
        var load = new FakeLoad { Step = "KnowledgeBase" };
        using var slave = new ClusterSlave(bus, nodeId: 3, subsystemName: "N");
        slave.RegisterHandler(load);
        slave.HoldReady();

        slave.Tick();
        var b = Assert.Single(Beats(bus));
        Assert.Equal(NodePhase.Booting, b.Phase);                                  // the host is still initialising

        slave.MarkReady();
        slave.Tick();
        b = Assert.Single(Beats(bus));
        Assert.Equal(NodePhase.Ready, b.Phase);

        Send(bus, 3, NodeOpType.PrepareLive);
        bus.SwapBuffers(); slave.Tick();
        b = Assert.Single(Beats(bus));
        Assert.Equal((NodePhase.Loading, "KnowledgeBase"), (b.Phase, b.Step));

        load.Step = "Terrain";            slave.Tick(); b = Assert.Single(Beats(bus)); Assert.Equal((NodePhase.Loading, "Terrain"), (b.Phase, b.Step));
        load.Step = "ScenarioEntities";   slave.Tick(); b = Assert.Single(Beats(bus)); Assert.Equal((NodePhase.Loading, "ScenarioEntities"), (b.Phase, b.Step));

        load.Gate.SetResult(null);                                                 // the prepare resolves…
        Send(bus, 3, NodeOpType.CommitState, new CommitStatePayload(ClusterState.OperatingLive));   // …and the cluster commits the operating state
        bus.SwapBuffers(); slave.Tick();
        b = Assert.Single(Beats(bus));
        Assert.Equal(NodePhase.PlayReady, b.Phase);
        Assert.Null(b.Step);

        // a failed prepare: Failed + the reason, on the heartbeat AND on the failure ack
        load.Gate = new TaskCompletionSource<object?>();
        load.Gate.SetException(new InvalidOperationException("terrain 'Alps' not found"));
        var failedTx = Send(bus, 3, NodeOpType.PrepareLive);
        bus.SwapBuffers(); slave.Tick();
        bus.SwapBuffers();                                                         // ONE swap: the beat and the ack of the same frame
        var beat = Assert.Single(bus.ReadManaged<NodeHeartbeatEvent>().ToList());
        Assert.Equal((NodePhase.Failed, "terrain 'Alps' not found"), (beat.Phase, beat.Reason));
        var ack = Assert.Single(bus.ReadManaged<NodeOpCompletedEvent>().ToList(), a => a.TransactionId == failedTx && a.StatusCode == OrchestrationStatusCode.Failure);
        Assert.Equal("terrain 'Alps' not found", ack.ResultPayload);               // ⭐ the ack carries the reason (it carried none)

        // the NEXT transaction ends the Failed phase
        Send(bus, 3, NodeOpType.CommitState, new CommitStatePayload(ClusterState.Idle));
        bus.SwapBuffers(); slave.Tick();
        Assert.Equal(NodePhase.Ready, Assert.Single(Beats(bus)).Phase);
    }

    /// <summary>
    /// ⭐ <c>CE-3345</c> success condition 2 — a phase / step change publishes a heartbeat AT ONCE (not at the 1 Hz tick), and never more than once per frame; a quiet frame publishes none.
    /// 🔴 Mutations: the change test in <c>PublishHeartbeat</c> removed (timer only) ⇒ the "at once" cells red; the beat published per change inside the step loop (two per frame) ⇒ the "one per frame" cell reds.
    /// </summary>
    [Fact]
    public void CE3345_AStepChange_PublishesAHeartbeatAtOnce_NeverMoreThanOncePerFrame()
    {
        var bus = new FdpEventBus();
        var load = new FakeLoad { Step = "KnowledgeBase" };
        using var slave = new ClusterSlave(bus, nodeId: 3, subsystemName: "N");
        slave.RegisterHandler(load);
        slave.Tick(); Beats(bus);                                                  // first tick: nothing to say yet (Ready is what a heartbeat always meant)

        slave.Tick();
        Assert.Empty(Beats(bus));                                                  // a quiet frame: no beat (the 1 Hz timer has not run out)

        Send(bus, 3, NodeOpType.PrepareLive);
        bus.SwapBuffers(); slave.Tick();
        Assert.Single(Beats(bus));                                                 // Ready → Loading: at once

        slave.Tick();
        Assert.Empty(Beats(bus));                                                  // the same step again: nothing

        load.Step = "Terrain";
        slave.Tick();
        Assert.Equal("Terrain", Assert.Single(Beats(bus)).Step);                   // the step moved: at once

        load.Step = "ScenarioEntities";                                            // step AND failure in one frame ⇒ still ONE beat
        load.Gate.SetException(new InvalidOperationException("x"));
        slave.Tick();
        Assert.Single(Beats(bus));
    }

    /// <summary>
    /// ⭐ <c>CE-3345</c> — the P4 <c>InProgress</c> ack of a load prepare carries the step name in its result and is RE-SENT when the step changes — once per step, not once per frame.
    /// 🔴 Mutations: <c>ResultPayload = null</c> in the first ack ⇒ the step cell reds; <c>PublishProgressIfStepChanged</c> dropped ⇒ the re-send cells red; its <c>step == _progressStepAcked</c> guard removed ⇒ the "once per step" cell reds.
    /// </summary>
    [Fact]
    public void CE3345_TheProgressAck_NamesTheStep_AndIsResentWhenTheStepChanges()
    {
        var bus = new FdpEventBus();
        var load = new FakeLoad { Step = "KnowledgeBase" };
        using var slave = new ClusterSlave(bus, nodeId: 3, subsystemName: "N");
        slave.RegisterHandler(load);
        Send(bus, 3, NodeOpType.PrepareLive);
        bus.SwapBuffers(); slave.Tick();

        var progress = Acks(bus).Where(a => a.StatusCode == OrchestrationStatusCode.InProgress).ToList();
        Assert.Equal("KnowledgeBase", Assert.Single(progress).ResultPayload);

        slave.Tick();
        Assert.DoesNotContain(Acks(bus), a => a.StatusCode == OrchestrationStatusCode.InProgress);   // same step: not again

        load.Step = "Terrain";
        slave.Tick();
        Assert.Equal("Terrain", Assert.Single(Acks(bus), a => a.StatusCode == OrchestrationStatusCode.InProgress).ResultPayload);

        slave.Tick(); slave.Tick();
        Assert.DoesNotContain(Acks(bus), a => a.StatusCode == OrchestrationStatusCode.InProgress);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3345</c> success condition 4 (the node's half; R-3070) — <c>CancelLoad</c> on a node mid-load: the REAL token the prepare runs with is cancelled, the chain's abort runs, the prepare is answered
    /// <b>Failure WITH the reason</b>, the node reports <c>Failed</c>, and the transaction's later steps (its queued <c>CommitState</c>) do not run. With no load held it is a no-op returning false.
    /// 🔴 Mutations: no <c>cts.Cancel()</c> ⇒ the token cell reds; no <c>Handler.Abort</c> ⇒ the abort cell reds; the ack's <c>ResultPayload</c> null ⇒ the reason cell reds; the cancelled-transaction guard removed ⇒ both CommitState cells (queued behind, and arriving after the cancel) red. (⭐ a queue purge existed beside the guard; it was redundant — nothing observable differs — so it was removed: one mechanism.)
    /// </summary>
    [Fact]
    public void CE3345_CancelLoad_CancelsTheToken_AbortsTheChain_AndAnswersFailureWithTheReason()
    {
        var bus = new FdpEventBus();
        var load = new FakeLoad { Step = "Terrain" };
        using var slave = new ClusterSlave(bus, nodeId: 3, subsystemName: "N");
        slave.RegisterHandler(load);
        Assert.False(slave.CancelLoad());                                          // nothing held: nothing to cancel

        var tx = Send(bus, 3, NodeOpType.PrepareLive);
        bus.SwapBuffers(); slave.Tick();
        Assert.True(slave.IsLoadHeld);
        Assert.False(load.Token.IsCancellationRequested);
        Assert.True(load.Token.CanBeCanceled);                                     // ⭐ a real token, not `default`
        Acks(bus);

        // the same transaction's commit is queued behind the held prepare
        bus.PublishManaged(new ExecuteNodeOpIntent { TransactionId = tx, TargetNodeId = 3, Operation = NodeOpType.CommitState, DomainPayload = new CommitStatePayload(ClusterState.LoadingLive) });
        bus.SwapBuffers(); slave.Tick(); Acks(bus);

        Assert.True(slave.CancelLoad());
        Assert.True(load.Token.IsCancellationRequested);
        Assert.Equal(1, load.Aborts);
        Assert.False(slave.IsLoadHeld);
        var ack = Assert.Single(Acks(bus), a => a.StatusCode == OrchestrationStatusCode.Failure);
        Assert.Equal((tx, NodeOpType.PrepareLive), (ack.TransactionId, ack.Operation));
        Assert.Equal("cancelled by the user on node 3", ack.ResultPayload);        // ⭐ the default reason names the node
        Assert.Equal(NodePhase.Failed, slave.Phase);
        Assert.Equal("cancelled by the user on node 3", slave.FailureReason);

        slave.Tick();                                                              // the cancelled transaction's queued commit never runs
        Assert.Equal((int)ClusterState.Idle, slave.LocalStateIdForTest);

        // ⭐ and one that ARRIVES after the cancel (the master's fan-out was already on the wire) is dropped too — the node does not enter a load it abandoned
        bus.PublishManaged(new ExecuteNodeOpIntent { TransactionId = tx, TargetNodeId = 3, Operation = NodeOpType.CommitState, DomainPayload = new CommitStatePayload(ClusterState.LoadingLive) });
        bus.SwapBuffers(); slave.Tick();
        Assert.Equal((int)ClusterState.Idle, slave.LocalStateIdForTest);
    }
}
