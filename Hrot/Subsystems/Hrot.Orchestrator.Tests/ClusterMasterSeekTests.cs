using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.ModuleHost.Time;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Time.Controllers;
using Fdp.Toolkit.Time.Domain;
using Hrot.NED.Descriptors.Orchestration;
using ClusterOpType  = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using FdpNodeOpType  = Fdp.Toolkit.Orchestration.NodeOpType;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// Tests for RT-008 (ACK-tracked seek fan-out) and RT-009 (server-side pause precondition).
/// </summary>
[Collection("OrchestratorTests")]
public sealed class ClusterMasterSeekTests
{
    private static ClusterConfiguration NoMandatoryConfig() => new ClusterConfiguration
    {
        Mandatory                  = Array.Empty<string>(),
        HeartbeatTimeoutSeconds    = 60f,
        TransactionHistoryCapacity = 10,
    };

    /// <summary>
    /// Registers a single node via heartbeat so it appears in the roster.
    /// </summary>
    private static void RegisterNode(
        FdpEventBus bus, ClusterMaster master,
        int nodeId = 1, string subsystem = "SimHost")
    {
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId        = nodeId,
            SubsystemName = subsystem,
            LocalStateId  = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();
    }

    // ── RT-008: ACK-tracked seek fan-out ─────────────────────────────────────

    /// <summary>
    /// T8a: When the cluster has one active node, a ReplaySeek request must NOT publish
    /// an immediate ClusterOpCompletedEvent.  Success must only arrive after the node ACKs
    /// the NodeReplaySeek fan-out.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ReplaySeek_WithActiveNodes_RegistersAckTracker_AndDoesNotPublishImmediateSuccess()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, NoMandatoryConfig());

        RegisterNode(bus, master, nodeId: 1, subsystem: "SimHost");

        var seekRequestId = Guid.NewGuid();
        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = seekRequestId,
            OperationType = ClusterOpType.ReplaySeek,
            PayloadJson   = "{\"TargetWallTicks\":99000}",
        });
        bus.SwapBuffers();
        master.Tick();

        // T8a: No ClusterOpCompletedEvent yet.
        bus.SwapBuffers();
        var earlyCompleted = bus.ReadManaged<ClusterOpCompletedEvent>().ToList();
        Assert.Empty(earlyCompleted);

        // T8b: A NodeReplaySeek fan-out intent must have been published.
        // Read the intents that were written during Tick() (they are in the already-swapped read buffer).
        // We need to look at what was published in the write buffer during Tick — drain remaining
        // from the current read buffer.
        // Re-run: during the Tick the master writes ExecuteNodeOpIntent to the write buffer.
        // After the first SwapBuffers+Tick we did: bus.SwapBuffers() to swap again.
        // Let's send another Tick to flush intent reading opportunity.
        // Actually: bus.SwapBuffers (before Tick) puts the seek request in read; Tick runs,
        // publishes ExecuteNodeOpIntent to WRITE; then bus.SwapBuffers (after) promotes it to READ.
        var fanOutIntents = bus.ReadManaged<ExecuteNodeOpIntent>().ToList();
        Assert.True(
            fanOutIntents.Any(i => i.Operation == FdpNodeOpType.NodeReplaySeek),
            "ClusterMaster must fan out NodeReplaySeek to active nodes.");

        // T8c: ACK the node op — success must arrive after the next Tick.
        var fanOutIntent = fanOutIntents.First(i => i.Operation == FdpNodeOpType.NodeReplaySeek);
        bus.PublishManaged(new NodeOpCompletedEvent
        {
            TransactionId   = fanOutIntent.TransactionId,
            Operation       = fanOutIntent.Operation,
            NodeId          = 1,
            StatusCode      = OrchestrationStatusCode.Success,
            IsParticipating = true,
        });
        bus.SwapBuffers();
        master.Tick();

        bus.SwapBuffers();
        var completed = bus.ReadManaged<ClusterOpCompletedEvent>().ToList();
        Assert.True(
            completed.Any(e => e.RequestId == seekRequestId && !e.StatusCode.IsError()),
            $"Expected ClusterOpCompletedEvent(Success) for requestId={seekRequestId} after node ACK. " +
            $"Events: [{string.Join(", ", completed.Select(e => $"{e.RequestId}:{e.StatusCode}"))}]");
    }

    // ── RT-009: server-side pause precondition ────────────────────────────────

    /// <summary>
    /// T9a: Every ReplaySeek — even with active nodes — must publish
    /// SlaveNodeSetUpdatedEvent and PauseTimeIntent on the bus before the fan-out.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ReplaySeek_AlwaysPublishes_SlaveNodeSetUpdatedEvent_And_PauseTimeIntent()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, NoMandatoryConfig());

        RegisterNode(bus, master, nodeId: 1, subsystem: "SimHost");

        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.ReplaySeek,
            PayloadJson   = "{\"TargetWallTicks\":5000}",
        });
        bus.SwapBuffers();
        master.Tick();

        bus.SwapBuffers();
        var slaveUpdates = bus.ReadManaged<SlaveNodeSetUpdatedEvent>().ToList();
        var pauses       = bus.ReadManaged<PauseTimeIntent>().ToList();

        Assert.True(slaveUpdates.Count > 0,
            "ClusterMaster must publish SlaveNodeSetUpdatedEvent before seek fan-out.");
        Assert.True(pauses.Count > 0,
            "ClusterMaster must publish PauseTimeIntent before seek fan-out.");
    }

    /// <summary>
    /// T9b: The SlaveNodeSetUpdatedEvent must contain only SimHost/IG/CGF node IDs.
    /// A non-matching subsystem (e.g. "Editor") must not appear in SlaveNodeIds.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ReplaySeek_SlaveNodeSetUpdatedEvent_ContainsOnlySimHostIgCgfNodes()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, NoMandatoryConfig());

        RegisterNode(bus, master, nodeId: 1, subsystem: "SimHost");
        RegisterNode(bus, master, nodeId: 2, subsystem: "Editor");

        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.ReplaySeek,
            PayloadJson   = "{\"TargetWallTicks\":1000}",
        });
        bus.SwapBuffers();
        master.Tick();

        bus.SwapBuffers();
        var slaveUpdates = bus.ReadManaged<SlaveNodeSetUpdatedEvent>().ToList();
        Assert.True(slaveUpdates.Count > 0, "SlaveNodeSetUpdatedEvent must be published.");

        var slaveIds = slaveUpdates[0].SlaveNodeIds;
        Assert.Contains(1, slaveIds);
        Assert.DoesNotContain(2, slaveIds);
    }

    // ── RT-015: SnapAndPause wired into ConsumeNodeOpStatuses ────────────────

    private static MasterSyncController CreateMasterSync(FdpEventBus bus, long initialTicks = 0)
    {
        long ticks = initialTicks;
        return new MasterSyncController(bus, new HashSet<int>(), tickSource: () => ticks);
    }

    /// <summary>
    /// Gets the txId of the NodeReplaySeek fan-out intent published during a seek.
    /// After calling Tick, swaps bus buffers and reads the intent.
    /// </summary>
    private static Guid GetSeekTxId(FdpEventBus bus, ClusterMaster master)
    {
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();
        var intents = bus.ReadManaged<ExecuteNodeOpIntent>().ToList();
        return intents.First(i => i.Operation == FdpNodeOpType.NodeReplaySeek).TransactionId;
    }

    /// <summary>
    /// T15a/T15b — After all nodes ACK with a non-default ReplaySeekResult, SnapAndPause
    /// is called: master clock snaps to RestoredTime and enters Deterministic mode.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ReplaySeek_OnAllNodesAck_WithSeekResult_CallsSnapAndPause()
    {
        var masterBus  = new FdpEventBus();
        var masterSync = CreateMasterSync(masterBus);
        // Drain the initial baseline published by the constructor.
        masterBus.SwapBuffers();
        masterBus.Read<Fdp.Toolkit.Time.Messages.SwitchTimeModeEvent>();

        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, NoMandatoryConfig());
        master.SetMasterSync(masterSync);

        RegisterNode(bus, master, nodeId: 1, subsystem: "SimHost");

        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.ReplaySeek,
            PayloadJson   = "{\"TargetWallTicks\":50000}",
        });

        Guid txId = GetSeekTxId(bus, master);

        // ACK from node 1 with a real ReplaySeekResult.
        bus.PublishManaged(new NodeOpCompletedEvent
        {
            TransactionId   = txId,
            Operation       = FdpNodeOpType.NodeReplaySeek,
            NodeId          = 1,
            StatusCode      = OrchestrationStatusCode.Success,
            IsParticipating = true,
            ResultPayload   = new ReplaySeekResult(new GlobalTime
            {
                TotalWallTicks = 9999L,
                TotalTime      = 5.0,
            }),
        });
        bus.SwapBuffers();
        master.Tick();

        // T15a: master clock was snapped to the seek result wall ticks.
        Assert.Equal(9999L, masterSync.GetCurrentState().TotalWallTicks);
        // T15b: master clock is now in Deterministic mode.
        Assert.Equal(TimeMode.Deterministic, masterSync.GetMode());
    }

    /// <summary>
    /// T15d — When ACK carries default(GlobalTime) (TotalWallTicks == 0), SnapAndPause
    /// must NOT be called; master clock must remain unchanged.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ReplaySeek_OnAllNodesAck_WithDefaultResult_DoesNotCallSnapAndPause()
    {
        var masterBus  = new FdpEventBus();
        var masterSync = CreateMasterSync(masterBus);
        masterBus.SwapBuffers();
        masterBus.Read<Fdp.Toolkit.Time.Messages.SwitchTimeModeEvent>();

        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, NoMandatoryConfig());
        master.SetMasterSync(masterSync);

        RegisterNode(bus, master, nodeId: 1, subsystem: "SimHost");

        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.ReplaySeek,
            PayloadJson   = "{\"TargetWallTicks\":50000}",
        });

        Guid txId = GetSeekTxId(bus, master);

        long wallTicksBefore = masterSync.GetCurrentState().TotalWallTicks;

        bus.PublishManaged(new NodeOpCompletedEvent
        {
            TransactionId   = txId,
            Operation       = FdpNodeOpType.NodeReplaySeek,
            NodeId          = 1,
            StatusCode      = OrchestrationStatusCode.Success,
            IsParticipating = true,
            ResultPayload   = new ReplaySeekResult(default(GlobalTime)),
        });
        bus.SwapBuffers();
        master.Tick();

        // Master wall ticks must be unchanged (SnapAndPause not called).
        Assert.Equal(wallTicksBefore, masterSync.GetCurrentState().TotalWallTicks);
    }

    /// <summary>
    /// T15c — A non-seek transition (e.g. TakeCheckpoint) must not call SnapAndPause.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ReplaySeek_NonSeekTransition_DoesNotCallSnapAndPause()
    {
        var masterBus  = new FdpEventBus();
        var masterSync = CreateMasterSync(masterBus);
        masterBus.SwapBuffers();
        masterBus.Read<Fdp.Toolkit.Time.Messages.SwitchTimeModeEvent>();

        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, NoMandatoryConfig());
        master.SetMasterSync(masterSync);

        RegisterNode(bus, master, nodeId: 1, subsystem: "SimHost");

        // Issue a TakeCheckpoint (not a seek).
        var requestId = Guid.NewGuid();
        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = requestId,
            OperationType = ClusterOpType.TakeCheckpoint,
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();
        var intents = bus.ReadManaged<ExecuteNodeOpIntent>().ToList();
        Assert.True(intents.Count > 0, "Expected TakeSnapshot fan-out intent.");
        var txId = intents[0].TransactionId;

        long wallTicksBefore = masterSync.GetCurrentState().TotalWallTicks;

        bus.PublishManaged(new NodeOpCompletedEvent
        {
            TransactionId   = txId,
            Operation       = intents[0].Operation,
            NodeId          = 1,
            StatusCode      = OrchestrationStatusCode.Success,
            IsParticipating = true,
            ResultPayload   = null,
        });
        bus.SwapBuffers();
        master.Tick();

        // Master wall ticks must be unchanged (SnapAndPause not called).
        Assert.Equal(wallTicksBefore, masterSync.GetCurrentState().TotalWallTicks);
    }
}
