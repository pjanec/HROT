using System;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
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
}
