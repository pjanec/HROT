using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Orchestrator;
using ClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;
using NodeOpType   = Hrot.NED.Descriptors.Orchestration.NodeOpType;

namespace Hrot.Orchestrator.Integration.Tests;

// ── Stub handler that accepts every operation ─────────────────────────────────

/// <summary>
/// Accepts any <see cref="Fdp.Toolkit.Orchestration.NodeOpType"/> and completes
/// synchronously with <c>null</c> payload.  Used for AllInOne bus-mode tests.
/// </summary>
internal sealed class StubAllOpsHandler : IClusterStateHandler
{
    private readonly int _nodeId;
    public StubAllOpsHandler(int nodeId) => _nodeId = nodeId;
    public bool CanHandle(Fdp.Toolkit.Orchestration.NodeOpType op) => true;
    public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct) =>
        Task.FromResult<object?>(null);
    public void Commit(ExecuteNodeOpIntent intent, Fdp.Core.EntityRepository? repo) { }
    public void Abort(ExecuteNodeOpIntent  intent, Fdp.Core.EntityRepository? repo) { }
}

/// <summary>
/// Rejects PrepareAsync with a faulted task for any operation.
/// Used to test failure-status propagation.
/// </summary>
internal sealed class FailingPrepareHandler : IClusterStateHandler
{
    public bool CanHandle(Fdp.Toolkit.Orchestration.NodeOpType op) => true;
    public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct) =>
        Task.FromException<object?>(new InvalidOperationException("Simulated prepare failure"));
    public void Commit(ExecuteNodeOpIntent intent, Fdp.Core.EntityRepository? repo) { }
    public void Abort(ExecuteNodeOpIntent  intent, Fdp.Core.EntityRepository? repo) { }
}

// ── Collection declaration (no parallelism) ───────────────────────────────────

[CollectionDefinition("CqrsIntegrationTests", DisableParallelization = true)]
public class CqrsIntegrationTestCollection { }

// ── Test class ────────────────────────────────────────────────────────────────

/// <summary>
/// End-to-end AllInOne tests for the bus-mode 2PC orchestration pipeline.
/// No DDS — all communication goes through <see cref="FdpEventBus"/>.
/// </summary>
[Collection("CqrsIntegrationTests")]
public sealed class CqrsOrchestrationIntegrationTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ClusterConfiguration NoMandatoryConfig() => new ClusterConfiguration
    {
        Mandatory                  = Array.Empty<string>(),
        HeartbeatTimeoutSeconds    = 60f,
        TransactionHistoryCapacity = 10,
    };

    /// <summary>
    /// Advances one logical frame: swap buffers first (makes previous writes readable),
    /// then tick master, then tick slave.
    /// </summary>
    private static void Frame(FdpEventBus bus, ClusterMaster master, ClusterSlave slave)
    {
        bus.SwapBuffers();
        master.Tick();
        slave.Tick();
    }

    /// <summary>
    /// Runs up to <paramref name="maxFrames"/> frames and returns the first
    /// <see cref="ClusterOpCompletedEvent"/> found, or <c>null</c> if none arrived.
    /// The bus is swapped an extra time at the end so the caller can immediately
    /// call <see cref="FdpEventBus.ConsumeManaged{T}"/>.
    /// </summary>
    private static ClusterOpCompletedEvent? RunUntilCompleted(
        FdpEventBus bus, ClusterMaster master, ClusterSlave slave, int maxFrames = 15)
    {
        for (int i = 0; i < maxFrames; i++)
        {
            Frame(bus, master, slave);
            // Check read buffer (was swapped at start of this frame)
            var events = bus.ConsumeManaged<ClusterOpCompletedEvent>();
            if (events.Count > 0)
                return events[0];
        }
        // One extra swap so the caller can inspect the final read buffer.
        bus.SwapBuffers();
        return bus.ConsumeManaged<ClusterOpCompletedEvent>()
                  .Cast<ClusterOpCompletedEvent?>()
                  .FirstOrDefault();
    }

    /// <summary>
    /// Registers node 1 in the ClusterMaster roster by publishing one heartbeat
    /// and advancing one frame.
    /// </summary>
    private static void RegisterNode(FdpEventBus bus, ClusterMaster master, ClusterSlave slave,
        int nodeId = 1, string subsystem = "SimHost",
        ClusterState state = ClusterState.Idle)
    {
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId        = nodeId,
            LocalStateId  = (int)state,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
            SubsystemName = subsystem,
        });
        Frame(bus, master, slave);  // master reads heartbeat → roster updated
    }

    // ── CMC-S017 Test 1: Full 2PC round-trip ─────────────────────────────────

    /// <summary>
    /// AllInOne full 2PC: register node → push <see cref="TransitionStateIntent"/> →
    /// expect <see cref="ClusterOpCompletedEvent"/> with <see cref="OrchestrationStatusCode.Success"/>.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void TransitionState_AllInOne_CompletesCqrsRoundTrip()
    {
        var bus    = new FdpEventBus();
        var master = new ClusterMaster(bus, NoMandatoryConfig());
        using var slave = new ClusterSlave(1, "SimHost", bus);
        slave.RegisterHandler(new StubAllOpsHandler(1));

        RegisterNode(bus, master, slave);

        var txId = Guid.NewGuid();
        bus.PublishManaged(new TransitionStateIntent
        {
            TransactionId = txId,
            TargetState   = Fdp.Toolkit.Orchestration.ClusterState.LoadingLive,
        });

        var completed = RunUntilCompleted(bus, master, slave);

        Assert.NotNull(completed);
        Assert.Equal(OrchestrationStatusCode.Success, completed!.Value.StatusCode);
    }

    // ── CMC-S017 Test 2: Bootstrap latch blocks fan-out ───────────────────────

    /// <summary>
    /// When a <see cref="TransitionStateIntent"/> is pushed but NO node heartbeat has been
    /// received, the bootstrap latch prevents the request from being processed and no
    /// <see cref="ExecuteNodeOpIntent"/> is published on the bus.
    /// </summary>
    [Fact(Timeout = 5_000)]
    public void TransitionState_WithNoNodeRegistered_NoFanOut()
    {
        var bus    = new FdpEventBus();
        var config = new ClusterConfiguration
        {
            Mandatory                  = new[] { "SimHost" },  // mandatory → latch not cleared immediately
            HeartbeatTimeoutSeconds    = 60f,
            TransactionHistoryCapacity = 10,
        };
        var master = new ClusterMaster(bus, config);
        using var slave = new ClusterSlave(1, "SimHost", bus);

        // Push intent WITHOUT registering any node first.
        bus.PublishManaged(new TransitionStateIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetState   = Fdp.Toolkit.Orchestration.ClusterState.LoadingLive,
        });

        // Tick a few frames — bootstrap latch should block fan-out.
        for (int i = 0; i < 5; i++)
            Frame(bus, master, slave);

        bus.SwapBuffers();
        var intents = bus.ConsumeManaged<ExecuteNodeOpIntent>();
        Assert.Empty(intents);
    }

    // ── CMC-S017 Test 3: ManageEpisode publishes StartEpisode ────────────────

    /// <summary>
    /// Once the cluster is in <c>OperatingLive</c> state, a <see cref="ManageEpisodeIntent"/>
    /// with <c>IsStart = true</c> causes ClusterMaster to fan out a
    /// <see cref="ExecuteNodeOpIntent"/> with <c>Operation == NodeOpType.StartEpisode</c>.
    ///
    /// The two-step path (Idle→LoadingLive→OperatingLive) is performed as two separate
    /// single-step transitions so that each has only ONE PrepareOp and ONE expected ACK —
    /// matching the single-consumer-per-frame bus semantics of ClusterSlave.
    /// </summary>
    [Fact(Timeout = 20_000)]
    public void ManageEpisode_AllInOne_FansOutStartEpisodeIntent()
    {
        var bus    = new FdpEventBus();
        var master = new ClusterMaster(bus, NoMandatoryConfig());
        using var slave = new ClusterSlave(1, "SimHost", bus);
        slave.RegisterHandler(new StubAllOpsHandler(1));

        RegisterNode(bus, master, slave);

        // Step 1: Idle → LoadingLive (single TransitionStep → 1 ACK → works in bus mode)
        bus.PublishManaged(new TransitionStateIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetState   = Fdp.Toolkit.Orchestration.ClusterState.LoadingLive,
        });
        var step1 = RunUntilCompleted(bus, master, slave);
        Assert.NotNull(step1);
        Assert.Equal(OrchestrationStatusCode.Success, step1!.Value.StatusCode);

        // Update heartbeat for new state.
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId        = 1,
            LocalStateId  = (int)Fdp.Toolkit.Orchestration.ClusterState.LoadingLive,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
            SubsystemName = "SimHost",
        });
        Frame(bus, master, slave);

        // Step 2: LoadingLive → OperatingLive (single TransitionStep → 1 ACK)
        bus.PublishManaged(new TransitionStateIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetState   = Fdp.Toolkit.Orchestration.ClusterState.OperatingLive,
        });
        var step2 = RunUntilCompleted(bus, master, slave);
        Assert.NotNull(step2);
        Assert.Equal(OrchestrationStatusCode.Success, step2!.Value.StatusCode);

        // Update heartbeat to OperatingLive so master roster stays fresh.
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId        = 1,
            LocalStateId  = (int)Fdp.Toolkit.Orchestration.ClusterState.OperatingLive,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
            SubsystemName = "SimHost",
        });
        Frame(bus, master, slave);

        var episodeId = Guid.NewGuid();
        bus.PublishManaged(new ManageEpisodeIntent
        {
            TransactionId = Guid.NewGuid(),
            IsStart       = true,
            EpisodeId     = episodeId,
            ScenarioId    = "test_episode_scenario",  // required when IsStart = true
        });

        ExecuteNodeOpIntent? fanOut = null;
        for (int i = 0; i < 10 && fanOut is null; i++)
        {
            Frame(bus, master, slave);
            var intents = bus.ConsumeManaged<ExecuteNodeOpIntent>();
            fanOut = intents.Cast<ExecuteNodeOpIntent?>()
                           .FirstOrDefault(x => x.HasValue &&
                               x.Value.Operation == (Fdp.Toolkit.Orchestration.NodeOpType)
                                   (int)NodeOpType.StartEpisode);
        }

        Assert.NotNull(fanOut);
    }

    // ── CMC-S017 Test 4: CancelOperation fans out AbortTransaction ────────────

    /// <summary>
    /// After a <see cref="TransitionStateIntent"/> is dispatched,
    /// pushing <see cref="CancelOperationIntent"/> causes ClusterMaster to fan out
    /// a <see cref="ExecuteNodeOpIntent"/> with <c>Operation == NodeOpType.AbortTransaction</c>
    /// to active nodes.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void CancelOperation_FansOutAbortTransaction()
    {
        var bus    = new FdpEventBus();
        var master = new ClusterMaster(bus, NoMandatoryConfig());
        // No slave handler registered — ACK will never arrive naturally.
        using var slave = new ClusterSlave(1, "SimHost", bus);

        RegisterNode(bus, master, slave);

        var txId = Guid.NewGuid();
        bus.PublishManaged(new TransitionStateIntent
        {
            TransactionId = txId,
            TargetState   = Fdp.Toolkit.Orchestration.ClusterState.LoadingLive,
        });

        // Advance 3 frames so the intent is processed and fan-out dispatched.
        for (int i = 0; i < 3; i++)
            Frame(bus, master, slave);

        // Push cancel for the original request ID.
        bus.PublishManaged(new CancelOperationIntent { TargetRequestId = txId });

        ExecuteNodeOpIntent? abortIntent = null;
        for (int i = 0; i < 8 && abortIntent is null; i++)
        {
            Frame(bus, master, slave);
            var intents = bus.ConsumeManaged<ExecuteNodeOpIntent>();
            abortIntent = intents.Cast<ExecuteNodeOpIntent?>()
                                 .FirstOrDefault(x => x.HasValue &&
                                     x.Value.Operation == (Fdp.Toolkit.Orchestration.NodeOpType)
                                         (int)NodeOpType.AbortTransaction);
        }

        Assert.NotNull(abortIntent);
    }

    // ── CMC-S017 Test 5: Failure status propagation ───────────────────────────

    /// <summary>
    /// When the slave's PrepareAsync throws an exception (simulated failure), the slave
    /// publishes a <see cref="NodeOpCompletedEvent"/> with <see cref="OrchestrationStatusCode.Failure"/>
    /// and the master propagates this to a <see cref="ClusterOpCompletedEvent"/> with
    /// a non-success status code.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void NodeOpCompleted_WithFailure_PropagatesFailureStatus()
    {
        var bus    = new FdpEventBus();
        var master = new ClusterMaster(bus, NoMandatoryConfig());
        using var slave = new ClusterSlave(1, "SimHost", bus);
        // FailingPrepareHandler returns a faulted task → slave publishes Failure ACK.
        slave.RegisterHandler(new FailingPrepareHandler());

        RegisterNode(bus, master, slave);

        bus.PublishManaged(new TransitionStateIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetState   = Fdp.Toolkit.Orchestration.ClusterState.LoadingLive,
        });

        // Run enough frames for: intent→fan-out→slave-fails→ACK→master-publishes-failure
        var result = RunUntilCompleted(bus, master, slave);

        Assert.NotNull(result);
        Assert.True(result!.Value.StatusCode.IsError(),
            $"Expected error status after prepare failure, got {result.Value.StatusCode}");
    }

    // ── CMC-S017 Test 6: Echo-chamber regression ──────────────────────────────

    /// <summary>
    /// Regression guard: after <see cref="NodeOpCompletedEvent"/> is published by the
    /// slave and consumed by the master, the bus must NOT contain a new
    /// <see cref="ExecuteNodeOpIntent"/> (no infinite ACK→fan-out loop).
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void NoBusEchoChamber_AfterNodeOpCompleted()
    {
        var bus    = new FdpEventBus();
        var master = new ClusterMaster(bus, NoMandatoryConfig());
        using var slave = new ClusterSlave(1, "SimHost", bus);
        slave.RegisterHandler(new StubAllOpsHandler(1));

        RegisterNode(bus, master, slave);

        bus.PublishManaged(new TransitionStateIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetState   = Fdp.Toolkit.Orchestration.ClusterState.LoadingLive,
        });

        // Run until completed.
        RunUntilCompleted(bus, master, slave);

        // Extra frames — no stray ExecuteNodeOpIntents should appear.
        for (int i = 0; i < 3; i++)
            Frame(bus, master, slave);

        bus.SwapBuffers();
        var strayIntents = bus.ConsumeManaged<ExecuteNodeOpIntent>();
        Assert.Empty(strayIntents);
    }
}
