using System;
using System.Linq;
using Fdp.Core;
using Fdp.ModuleHost.Time;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Time.Controllers;
using Fdp.Toolkit.Time.Domain;
using Hrot.NED.Descriptors.Orchestration;
using ClusterState   = Hrot.NED.Descriptors.Orchestration.ClusterState;
using ClusterOpType  = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using FdpNodeOpType  = Fdp.Toolkit.Orchestration.NodeOpType;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// Tests for RT-021: master clock snap on Live-from-Replay branch completion.
/// Verifies that <see cref="ClusterMaster"/> calls
/// <see cref="MasterSyncController.SnapAndPause"/> with the historical time captured
/// from the first valid <see cref="LiveBranchResult"/> ACK before calling
/// <see cref="ReplayMasterModule.RestoreTime"/> (CGF1-S0305 Phase 6).
/// </summary>
[Collection("OrchestratorTests")]
public sealed class ClusterMasterBranchTests
{
    private static ClusterConfiguration MandatorySimHostConfig() => new ClusterConfiguration
    {
        Mandatory                  = new[] { "SimHost" },
        HeartbeatTimeoutSeconds    = 60f,
        TransactionHistoryCapacity = 10,
    };

    /// <summary>
    /// Bootstraps a single SimHost node and advances the cluster to
    /// <see cref="ClusterState.OperatingReplay"/>.
    /// Returns the node ID used.
    /// </summary>
    private static int BootstrapToOperatingReplay(FdpEventBus bus, ClusterMaster master)
    {
        const int nodeId = 1;

        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId        = nodeId,
            SubsystemName = "SimHost",
            LocalStateId  = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();

        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.OperatingReplay).ToString(),
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();

        return nodeId;
    }

    /// <summary>
    /// Triggers a branch transition (OperatingReplay -> OperatingLive) and returns
    /// the TransactionId of the fanned-out PrepareLive NodeOpIntent.
    /// </summary>
    private static Guid TriggerBranchAndGetTxId(FdpEventBus bus, ClusterMaster master)
    {
        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.OperatingLive).ToString(),
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();

        var intents = bus.ReadManaged<ExecuteNodeOpIntent>()
            .Where(i => i.Operation == FdpNodeOpType.PrepareLive)
            .ToList();
        Assert.True(intents.Any(), "ClusterMaster must fan out a PrepareLive NodeOpIntent.");
        return intents[0].TransactionId;
    }

    // ── T21a/T21b ─────────────────────────────────────────────────────────────

    /// <summary>
    /// T21a: When all nodes ACK a branch transition with a valid <see cref="LiveBranchResult"/>,
    /// the master clock must be snapped to the historical wall ticks.
    /// T21b: After the snap, <see cref="MasterSyncController.GetMode"/> returns
    /// <see cref="TimeMode.Deterministic"/> (paused).
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void LiveBranch_OnAllNodesAck_WithLiveBranchResult_SnapsAndPausesMasterClock()
    {
        var bus    = new FdpEventBus();
        var master = new ClusterMaster(bus, MandatorySimHostConfig());

        float currentScale = 1.0f;
        var   replayModule = new ReplayMasterModule(s => currentScale = s, () => currentScale);
        master.SetReplayMasterModule(replayModule);

        var masterSync = new MasterSyncController(bus, tickSource: () => 1L);
        master.SetMasterSync(masterSync);

        int nodeId = BootstrapToOperatingReplay(bus, master);
        Guid branchTxId = TriggerBranchAndGetTxId(bus, master);

        var historicalTime = new GlobalTime
        {
            TotalWallTicks = 7777L,
            TotalTime      = 3.0,
        };

        bus.PublishManaged(new NodeOpCompletedEvent
        {
            TransactionId   = branchTxId,
            Operation       = FdpNodeOpType.PrepareLive,
            NodeId          = nodeId,
            StatusCode      = OrchestrationStatusCode.Success,
            IsParticipating = true,
            ResultPayload   = new LiveBranchResult(historicalTime),
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();

        // T21a: master clock wall ticks snapped to historical value
        Assert.Equal(7777L, masterSync.GetCurrentState().TotalWallTicks);

        // T21b: master is paused (Deterministic) after snap
        Assert.Equal(TimeMode.Deterministic, masterSync.GetMode());
    }

    // ── T21d ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// T21d: When all nodes ACK a branch transition with a default (zero)
    /// <see cref="LiveBranchResult"/>, the master clock must NOT be snapped.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void LiveBranch_OnAllNodesAck_WithDefaultResult_DoesNotSnapMasterClock()
    {
        var bus    = new FdpEventBus();
        var master = new ClusterMaster(bus, MandatorySimHostConfig());

        float currentScale = 1.0f;
        var   replayModule = new ReplayMasterModule(s => currentScale = s, () => currentScale);
        master.SetReplayMasterModule(replayModule);

        // Use a controlled tick source so we can read the initial wall ticks
        long fakeTick = 1000L;
        var masterSync = new MasterSyncController(bus, tickSource: () => fakeTick);
        master.SetMasterSync(masterSync);

        long initialWallTicks = masterSync.GetCurrentState().TotalWallTicks;

        int  nodeId     = BootstrapToOperatingReplay(bus, master);
        Guid branchTxId = TriggerBranchAndGetTxId(bus, master);

        // ACK with default (zero) LiveBranchResult -- should not trigger snap
        bus.PublishManaged(new NodeOpCompletedEvent
        {
            TransactionId   = branchTxId,
            Operation       = FdpNodeOpType.PrepareLive,
            NodeId          = nodeId,
            StatusCode      = OrchestrationStatusCode.Success,
            IsParticipating = true,
            ResultPayload   = new LiveBranchResult(default(GlobalTime)),
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();

        // T21d: master clock must NOT have been snapped to a historical value
        Assert.Equal(initialWallTicks, masterSync.GetCurrentState().TotalWallTicks);
    }
}
