using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Kernel;
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
            DomainPayload = new CommitStatePayload(nextState),
        });

        slave.Tick();
        eventBus.SwapBuffers();

        var events = new List<TkClusterStateChangedEvent>();
        foreach (var e in eventBus.Consume<TkClusterStateChangedEvent>())
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
        foreach (var e in eventBus.ConsumeManaged<NodeOpCompletedEvent>())
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
        foreach (var e in eventBus.ConsumeManaged<NodeHeartbeatEvent>())
            heartbeats.Add(e);

        Assert.Single(heartbeats);
        Assert.Equal(nodeId, heartbeats[0].NodeId);
        Assert.Equal("TestNode", heartbeats[0].SubsystemName);
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
            DomainPayload = new CommitStatePayload(targetState),
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
            DomainPayload = new CommitStatePayload(stateLoading),
        });
        Assert.Equal(stateLoading, slave.LocalStateIdForTest);

        // Second CommitState: same txId but different target state → must NOT be dropped as duplicate.
        slave.EnqueueIntentForTest(new ExecuteNodeOpIntent
        {
            TransactionId = txId,
            Operation     = NodeOpType.CommitState,
            DomainPayload = new CommitStatePayload(stateOperating),
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
            Operation     = NodeOpType.CommitState, DomainPayload = new CommitStatePayload(targetState),
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
            DomainPayload = new CommitStatePayload(TargetStateId: 5),
        });

        Assert.Equal(5, slave.LocalStateIdForTest);
    }

    /// <summary>
    /// Two CommitState intents with different TargetStateIds should each be processed once
    /// (dedup key includes TargetStateId discriminant).
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
            DomainPayload = new CommitStatePayload(2),
        });
        slave.EnqueueIntentForTest(new ExecuteNodeOpIntent
        {
            TransactionId = txId,
            TargetNodeId  = 1,
            Operation     = NodeOpType.CommitState,
            DomainPayload = new CommitStatePayload(5),
        });

        // Last CommitState wins
        Assert.Equal(5, slave.LocalStateIdForTest);
    }
}
