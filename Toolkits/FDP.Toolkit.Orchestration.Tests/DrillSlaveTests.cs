using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagira.BDC.SSTD.Orchestration;
using Bagira.Common.Orchestration;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Toolkit.Orchestration;
using Xunit;

namespace FDP.Toolkit.Orchestration.Tests;

/// <summary>
/// Unit and integration tests for <see cref="DrillSlave"/> (G0402 success conditions).
/// </summary>
public sealed class DrillSlaveTests
{
    // ── Stub IDsmHandler used by unit tests ───────────────────────────────

    private sealed class StubHandler : IDsmHandler
    {
        private readonly int _operationId;
        public int PrepareCallCount;
        public int CommitCallCount;

        public TaskCompletionSource<string?>? PrepareGate;

        public StubHandler(int operationId)
        {
            _operationId = operationId;
        }

        public bool CanHandle(int operationId) => operationId == _operationId;

        public Task<string?> PrepareAsync(OrchestrationCommand cmd, CancellationToken ct)
        {
            PrepareCallCount++;
            return PrepareGate?.Task ?? Task.FromResult<string?>(null);
        }

        public void Commit(OrchestrationCommand cmd, EntityRepository? repo)
        {
            CommitCallCount++;
        }

        public void Abort(OrchestrationCommand cmd, EntityRepository? repo) { }
    }

    // ── CGF1-G0402 success condition 1 ───────────────────────────────────

    /// <summary>
    /// Fact: Toolkit DrillSlave dispatches PrepareAsync + Commit.
    /// A stub handler receives PrepareAsync then Commit on the same Tick() when
    /// PrepareAsync completes synchronously.
    /// </summary>
    [Fact]
    public void DrillSlave_DispatchesPrepareAsyncAndCommit_SynchronousHandler()
    {
        const int opId = 5; // arbitrary non-CommitState id
        var handler = new StubHandler(opId);
        using var slave = new DrillSlave();
        slave.RegisterHandler(handler);

        slave.EnqueueCommandForTest(new OrchestrationCommand(
            Guid.NewGuid(), TargetNodeId: 1, OperationId: opId, PayloadJson: "{}"));

        slave.Tick();

        Assert.Equal(1, handler.PrepareCallCount);
        Assert.Equal(1, handler.CommitCallCount);
    }

    /// <summary>
    /// Fact: Toolkit DrillSlave defers Commit to next tick when PrepareAsync is async.
    /// </summary>
    [Fact]
    public void DrillSlave_DeferCommit_WhenPrepareAsyncIsAsync()
    {
        const int opId = 5;
        var handler = new StubHandler(opId) { PrepareGate = new TaskCompletionSource<string?>() };
        using var slave = new DrillSlave();
        slave.RegisterHandler(handler);

        slave.EnqueueCommandForTest(new OrchestrationCommand(
            Guid.NewGuid(), TargetNodeId: 1, OperationId: opId, PayloadJson: "{}"));

        slave.Tick(); // PrepareAsync started but not completed
        Assert.Equal(1, handler.PrepareCallCount);
        Assert.Equal(0, handler.CommitCallCount); // deferred

        handler.PrepareGate.SetResult(null); // complete the task
        slave.Tick(); // Commit should now fire
        Assert.Equal(1, handler.CommitCallCount);
    }

    // ── CGF1-G0402 success condition 2 ───────────────────────────────────

    /// <summary>
    /// Fact: Toolkit DrillSlave deduplicates transactions.
    /// Two commands with the same TransactionId — handler PrepareAsync called only once.
    /// </summary>
    [Fact]
    public void DrillSlave_DeduplicatesTransactions()
    {
        const int opId = 7;
        var handler = new StubHandler(opId);
        using var slave = new DrillSlave();
        slave.RegisterHandler(handler);

        var txId = Guid.NewGuid();
        var cmd = new OrchestrationCommand(txId, TargetNodeId: 1, OperationId: opId, PayloadJson: "{}");

        slave.EnqueueCommandForTest(cmd);
        slave.EnqueueCommandForTest(cmd); // duplicate

        slave.Tick();

        Assert.Equal(1, handler.PrepareCallCount);  // second silently dropped
        Assert.Equal(1, handler.CommitCallCount);
    }

    // ── CGF1-G0402 success condition 3 ───────────────────────────────────

    /// <summary>
    /// Fact: TkDsmStateChangedEvent published on CommitState.
    /// CommitState(nextStateId=5) → TkDsmStateChangedEvent{PreviousStateId=0, NextStateId=5}.
    /// </summary>
    [Fact]
    public void DrillSlave_PublishesTkDsmStateChangedEvent_OnCommitState()
    {
        const int CommitState = 2; // NodeOpType.CommitState
        const int nextState   = 5;

        var eventBus = new FdpEventBus();
        using var slave = new DrillSlave(eventBus);

        slave.EnqueueCommandForTest(new OrchestrationCommand(
            Guid.NewGuid(), TargetNodeId: 0, OperationId: CommitState,
            PayloadJson: nextState.ToString()));

        slave.Tick();
        eventBus.SwapBuffers();

        var events = new List<TkDsmStateChangedEvent>();
        foreach (var e in eventBus.Consume<TkDsmStateChangedEvent>())
            events.Add(e);

        Assert.Single(events);
        Assert.Equal(0,         events[0].PreviousStateId);
        Assert.Equal(nextState, events[0].NextStateId);
        Assert.Equal(nextState, slave.LocalStateIdForTest);
    }

    // ── CGF1-G0402 success condition 4 (DDS integration) ─────────────────

    /// <summary>
    /// Fact: DdsOrchestrationTransport delivers commands to DrillSlave.
    /// Sends a NodeOpCommand over DDS and verifies the toolkit DrillSlave
    /// dispatches it to a registered handler within 2 s.
    /// </summary>
    [Fact(Timeout = 5_000)]
    public void DdsTransport_DeliversCommand_ToDrillSlave()
    {
        // Domain 17 reserved for this test.
        const int TestDomain = 17;
        const int nodeId     = 42;
        const int opId       = 5; // arbitrary non-CommitState

        using var participant      = new DdsParticipant(TestDomain);
        using var commandPublisher = new DdsWriter<NodeOpCommand>(participant);
        using var transport        = new DdsOrchestrationTransport(participant, nodeId);

        var handler = new StubHandler(opId);
        using var slave = new DrillSlave(transport, nodeId, "TestSubsystem");
        slave.RegisterHandler(handler);

        Thread.Sleep(200); // DDS discovery

        commandPublisher.Write(new NodeOpCommand
        {
            TransactionId = Guid.NewGuid(),
            TargetNodeId  = nodeId,
            Operation     = (NodeOpType)opId,
            PayloadJson   = "{}",
        });

        // Poll until handler receives the command (up to 2 s).
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline && handler.CommitCallCount == 0)
        {
            slave.Tick();
            Thread.Sleep(20);
        }

        Assert.Equal(1, handler.PrepareCallCount);
        Assert.Equal(1, handler.CommitCallCount);
    }
}
