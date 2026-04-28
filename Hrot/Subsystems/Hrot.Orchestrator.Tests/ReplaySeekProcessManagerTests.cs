// ReplaySeekProcessManagerTests.cs
// Tests for ReplaySeekProcessManager (TASK-T002).

using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Time.Controllers;
using Fdp.Toolkit.Time.Domain;
using Hrot.NED.Descriptors.Orchestration;
using ClusterOpType  = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using FdpNodeOpType  = Fdp.Toolkit.Orchestration.NodeOpType;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// Tests for ReplaySeekProcessManager (TASK-T002).
/// </summary>
[Collection("OrchestratorTests")]
public sealed class ReplaySeekProcessManagerTests
{
    private static ClusterConfiguration NoMandatoryConfig() => new ClusterConfiguration
    {
        Mandatory                  = Array.Empty<string>(),
        HeartbeatTimeoutSeconds    = 60f,
        TransactionHistoryCapacity = 10,
    };

    private static MasterSyncController MakeMasterSync(FdpEventBus bus)
    {
        return new MasterSyncController(bus, new HashSet<int>(), tickSource: () => 0L);
    }

    private static void RegisterNode(
        FdpEventBus   bus,
        ClusterMaster master,
        ReplaySeekProcessManager mgr,
        int    nodeId   = 1,
        string subsystem = "SimHost")
    {
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId        = nodeId,
            SubsystemName = subsystem,
            LocalStateId  = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
        });
        bus.SwapBuffers();
        mgr.Tick();
        master.Tick();
        bus.SwapBuffers();
    }

    // ── SC1: SlaveNodeSetUpdatedEvent + PauseTimeIntent on SeekReplayIntent ─

    /// <summary>
    /// SC1: When SeekReplayIntent is published, ReplaySeekProcessManager must publish
    /// SlaveNodeSetUpdatedEvent and PauseTimeIntent, and ClusterMaster must fan out
    /// NodeReplaySeek. ClusterMaster itself must NOT publish the precondition events.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void SeekProcessManager_OnSeekReplayIntent_PublishesPreconditions()
    {
        var masterSyncBus = new FdpEventBus();
        var masterSync    = MakeMasterSync(masterSyncBus);
        masterSyncBus.SwapBuffers();

        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, NoMandatoryConfig());
        master.RegisterAggregator(new ReplaySeekAggregator());

        var mgr = new ReplaySeekProcessManager(bus, masterSync);

        RegisterNode(bus, master, mgr, nodeId: 1, subsystem: "SimHost");

        // Publish SeekReplayIntent directly to the bus (as the egress translator would do).
        bus.PublishManaged(new SeekReplayIntent
        {
            RequestId      = Guid.NewGuid(),
            TargetWallTicks = 1000L,
        });
        bus.SwapBuffers();

        // Tick both (seek manager before cluster master, simulating the tick order in production).
        mgr.Tick();    // reads SeekReplayIntent, publishes SlaveNodeSetUpdated + PauseTime
        master.Tick(); // reads SeekReplayIntent, fans out NodeReplaySeek
        bus.SwapBuffers();

        var slaveUpdates = bus.ReadManaged<SlaveNodeSetUpdatedEvent>().ToList();
        var pauses       = bus.ReadManaged<PauseTimeIntent>().ToList();
        var fanOuts      = bus.ReadManaged<ExecuteNodeOpIntent>()
            .Where(i => i.Operation == FdpNodeOpType.NodeReplaySeek)
            .ToList();

        Assert.True(slaveUpdates.Count > 0, "ReplaySeekProcessManager must publish SlaveNodeSetUpdatedEvent.");
        Assert.True(pauses.Count > 0, "ReplaySeekProcessManager must publish PauseTimeIntent.");
        Assert.True(fanOuts.Count > 0, "ClusterMaster must fan out NodeReplaySeek.");
    }

    // ── SC2: SnapAndPause called on successful seek ACK ───────────────────

    /// <summary>
    /// SC2: After all nodes ACK with a non-zero ReplaySeekResult (via aggregator),
    /// ReplaySeekProcessManager must call SnapAndPause on MasterSyncController.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void SeekProcessManager_OnSeekAck_WithResult_CallsSnapAndPause()
    {
        var masterSyncBus = new FdpEventBus();
        var masterSync    = MakeMasterSync(masterSyncBus);
        masterSyncBus.SwapBuffers();

        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, NoMandatoryConfig());
        master.RegisterAggregator(new ReplaySeekAggregator());

        var mgr = new ReplaySeekProcessManager(bus, masterSync);

        RegisterNode(bus, master, mgr, nodeId: 1, subsystem: "SimHost");

        var requestId = Guid.NewGuid();
        bus.PublishManaged(new SeekReplayIntent
        {
            RequestId       = requestId,
            TargetWallTicks = 5000L,
        });
        bus.SwapBuffers();
        mgr.Tick();
        master.Tick();
        bus.SwapBuffers();

        // Capture the NodeReplaySeek fan-out txId.
        var fanOut = bus.ReadManaged<ExecuteNodeOpIntent>()
            .FirstOrDefault(i => i.Operation == FdpNodeOpType.NodeReplaySeek);
        Assert.True(fanOut.TransactionId != default, "Expected NodeReplaySeek fan-out intent.");

        // ACK with a real ReplaySeekResult.
        bus.PublishManaged(new NodeOpCompletedEvent
        {
            TransactionId   = fanOut.TransactionId,
            Operation       = FdpNodeOpType.NodeReplaySeek,
            NodeId          = 1,
            StatusCode      = OrchestrationStatusCode.Success,
            IsParticipating = true,
            // Typed payload: ConsumeNodeOpStatuses serializes to JSON for aggregator.
            ResultPayload   = new ReplaySeekResult(new GlobalTime
            {
                TotalWallTicks = 5000L,
                TotalTime      = 2.5,
            }),
        });
        bus.SwapBuffers();
        // mgr reads NodeHeartbeatEvent (none here) and SeekReplayIntent (already consumed)
        mgr.Tick();
        master.Tick(); // processes ACK, calls TryAggregate (ReplaySeekAggregator), publishes ClusterOpCompletedEvent
        bus.SwapBuffers();
        mgr.Tick(); // reads ClusterOpCompletedEvent, calls SnapAndPause

        Assert.Equal(5000L, masterSync.GetCurrentState().TotalWallTicks);
    }

    // ── SC3: No SnapAndPause when TotalWallTicks == 0 ────────────────────

    /// <summary>
    /// SC3: When ACK carries a ReplaySeekResult with TotalWallTicks == 0 (default),
    /// SnapAndPause must NOT be called.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void SeekProcessManager_OnSeekAck_WithDefaultResult_DoesNotCallSnapAndPause()
    {
        var masterSyncBus = new FdpEventBus();
        var masterSync    = MakeMasterSync(masterSyncBus);
        masterSyncBus.SwapBuffers();

        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, NoMandatoryConfig());
        master.RegisterAggregator(new ReplaySeekAggregator());

        var mgr = new ReplaySeekProcessManager(bus, masterSync);

        RegisterNode(bus, master, mgr, nodeId: 1, subsystem: "SimHost");

        long initialWallTicks = masterSync.GetCurrentState().TotalWallTicks;

        bus.PublishManaged(new SeekReplayIntent
        {
            RequestId       = Guid.NewGuid(),
            TargetWallTicks = 5000L,
        });
        bus.SwapBuffers();
        mgr.Tick();
        master.Tick();
        bus.SwapBuffers();

        var fanOut = bus.ReadManaged<ExecuteNodeOpIntent>()
            .FirstOrDefault(i => i.Operation == FdpNodeOpType.NodeReplaySeek);
        Assert.True(fanOut.TransactionId != default, "Expected NodeReplaySeek fan-out intent.");

        // ACK with default (zero) ReplaySeekResult.
        bus.PublishManaged(new NodeOpCompletedEvent
        {
            TransactionId   = fanOut.TransactionId,
            Operation       = FdpNodeOpType.NodeReplaySeek,
            NodeId          = 1,
            StatusCode      = OrchestrationStatusCode.Success,
            IsParticipating = true,
            ResultPayload   = new ReplaySeekResult(default(GlobalTime)),
        });
        bus.SwapBuffers();
        mgr.Tick();
        master.Tick();
        bus.SwapBuffers();
        mgr.Tick();

        // Wall ticks must be unchanged -- SnapAndPause must NOT have been called.
        Assert.Equal(initialWallTicks, masterSync.GetCurrentState().TotalWallTicks);
    }
}
