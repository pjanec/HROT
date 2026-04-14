using System;
using System.Linq;
using Fdp.Kernel;
using Fdp.Toolkit.Orchestration;
using Hrot.NED.Descriptors.Orchestration;
using ClusterState   = Hrot.NED.Descriptors.Orchestration.ClusterState;
using ClusterOpType  = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using NodeOpType     = Hrot.NED.Descriptors.Orchestration.NodeOpType;
using FdpNodeOpType  = Fdp.Toolkit.Orchestration.NodeOpType;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// Tests for TakeCheckpoint and ReplaySeek cluster operations in bus mode
/// (the all-in-one / CMC-S016 path used by <c>clusterrunner -m all</c>).
/// </summary>
[Collection("OrchestratorTests")]
public sealed class ClusterMasterCheckpointTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Bootstraps a bus-mode <see cref="ClusterMaster"/> with one active node,
    /// transitions the cluster to <see cref="ClusterState.OperatingLive"/>,
    /// and returns the master together with its bus and the registered node ID.
    /// </summary>
    private static (ClusterMaster master, FdpEventBus bus, int nodeId) BootstrapToOperatingLive()
    {
        const int NId = 42;

        var bus = new FdpEventBus();
        // No mandatory nodes  → bootstrap latch clears immediately.
        var master = new ClusterMaster(bus);

        // Register node via heartbeat.
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId        = NId,
            LocalStateId  = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
            SubsystemName = "SimHost",
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();

        // Transition to OperatingLive — ACK each prepare step instantly.
        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = System.Text.Json.JsonSerializer.Serialize(
                new { TargetState = (int)ClusterState.OperatingLive }),
        });

        // ACK all prepare intents until status arrives (max 10 rounds).
        for (int round = 0; round < 10; round++)
        {
            bus.SwapBuffers();
            foreach (var intent in bus.ConsumeManaged<ExecuteNodeOpIntent>()
                         .Where(i => i.TargetNodeId == NId && i.Operation != FdpNodeOpType.CommitState))
            {
                bus.PublishManaged(new NodeOpCompletedEvent
                {
                    TransactionId   = intent.TransactionId,
                    Operation       = intent.Operation,
                    NodeId          = NId,
                    StatusCode      = OrchestrationStatusCode.Success,
                    IsParticipating = true,
                });
            }
            bus.SwapBuffers();
            master.Tick();

            if (master.CurrentSystemState == ClusterState.OperatingLive) break;
        }

        return (master, bus, NId);
    }

    // ── TakeCheckpoint ────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that <see cref="ClusterMaster.HandleClusterOpRequest"/> for
    /// <see cref="ClusterOpType.TakeCheckpoint"/> fans out
    /// <see cref="NodeOpType.TakeSnapshot"/> to all active nodes in bus mode and
    /// publishes <see cref="OrchestrationStatusCode.Success"/> once all ACKs arrive.
    ///
    /// <para>Regression test for the missing TakeCheckpoint handler — previously the
    /// request was silently ignored and no status was ever published, causing
    /// <c>ClusterOpActionHandler</c> to time out waiting for a <c>ClusterOpStatus</c>.</para>
    /// </summary>
    [Fact(Timeout = 5_000)]
    public void TakeCheckpoint_BusMode_PublishesSuccessAfterNodeAck()
    {
        var (master, bus, nodeId) = BootstrapToOperatingLive();

        var ckRequestId = Guid.NewGuid();
        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = ckRequestId,
            OperationType = ClusterOpType.TakeCheckpoint,
            PayloadJson   = string.Empty,
        });

        // Step 1: flush request → should fan out TakeSnapshot.
        bus.SwapBuffers();
        master.Tick();

        // Collect TakeSnapshot intent.
        bus.SwapBuffers();
        var intents = bus.ConsumeManaged<ExecuteNodeOpIntent>()
            .Where(i => i.Operation == FdpNodeOpType.TakeSnapshot)
            .ToList();

        Assert.Single(intents);
        Assert.Equal(nodeId, intents[0].TargetNodeId);

        // Step 2: ACK from node.
        bus.PublishManaged(new NodeOpCompletedEvent
        {
            TransactionId   = intents[0].TransactionId,
            Operation       = FdpNodeOpType.TakeSnapshot,
            NodeId          = nodeId,
            StatusCode      = OrchestrationStatusCode.Success,
            IsParticipating = true,
        });
        bus.SwapBuffers();
        master.Tick();

        // Step 3: ClusterMaster should now publish ClusterOpCompletedEvent(Success).
        bus.SwapBuffers();
        var completed = bus.ConsumeManaged<ClusterOpCompletedEvent>().ToList();
        Assert.True(
            completed.Any(e => e.RequestId == ckRequestId && !e.StatusCode.IsError()),
            $"Expected ClusterOpCompletedEvent(Success) for requestId={ckRequestId}. " +
            $"Events found: [{string.Join(", ", completed.Select(e => $"{e.RequestId}:{e.StatusCode}"))}]");

        master.Dispose();
    }

    /// <summary>
    /// Verifies that <see cref="ClusterOpType.TakeCheckpoint"/> with an empty roster
    /// immediately publishes <see cref="OrchestrationStatusCode.Success"/> (no nodes → no-op).
    /// </summary>
    [Fact(Timeout = 5_000)]
    public void TakeCheckpoint_BusMode_EmptyRoster_ImmediateSuccess()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus);  // no mandatory → latch set immediately

        var ckRequestId = Guid.NewGuid();
        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = ckRequestId,
            OperationType = ClusterOpType.TakeCheckpoint,
            PayloadJson   = string.Empty,
        });

        bus.SwapBuffers();
        master.Tick();

        bus.SwapBuffers();
        var completed = bus.ConsumeManaged<ClusterOpCompletedEvent>().ToList();
        Assert.True(
            completed.Any(e => e.RequestId == ckRequestId && !e.StatusCode.IsError()),
            "Expected immediate success for TakeCheckpoint with empty roster.");
    }

    // ── ReplaySeek ────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that <see cref="ClusterOpType.ReplaySeek"/> injected via
    /// <see cref="ClusterMaster.HandleClusterOpRequest"/> publishes an immediate
    /// <see cref="OrchestrationStatusCode.Success"/> response in bus mode.
    ///
    /// <para>Regression test: previously no status was published, causing
    /// <c>ClusterOpActionHandler</c> to time out waiting for <c>ClusterOpStatus</c>.</para>
    /// </summary>
    [Fact(Timeout = 5_000)]
    public void ReplaySeek_BusMode_PublishesImmediateSuccess()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus);

        var seekRequestId = Guid.NewGuid();
        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = seekRequestId,
            OperationType = ClusterOpType.ReplaySeek,
            PayloadJson   = "{\"TargetWallTicks\": 12345678}",
        });

        bus.SwapBuffers();
        master.Tick();

        bus.SwapBuffers();
        var completed = bus.ConsumeManaged<ClusterOpCompletedEvent>().ToList();
        Assert.True(
            completed.Any(e => e.RequestId == seekRequestId && !e.StatusCode.IsError()),
            $"Expected immediate ClusterOpCompletedEvent(Success) for ReplaySeek requestId={seekRequestId}. " +
            $"Events found: [{string.Join(", ", completed.Select(e => $"{e.RequestId}:{e.StatusCode}"))}]");
    }
}
