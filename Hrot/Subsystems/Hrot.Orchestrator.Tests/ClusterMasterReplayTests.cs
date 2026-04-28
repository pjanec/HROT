using System;
using System.Linq;
using Fdp.Core;
using Hrot.NED.Descriptors.Orchestration;
using Fdp.Toolkit.Orchestration;
using ClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using FdpNodeOpType = Fdp.Toolkit.Orchestration.NodeOpType;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// Tests that verify ClusterMaster behaviour for the Live-from-Replay transition
/// path (CGF1-S0305) after TASK-T001.
/// The temporal interlock (FreezeTime / RestoreTime / SnapAndPause) has moved to
/// <see cref="LiveBranchProcessManager"/>; see LiveBranchProcessManagerTests for those
/// assertions.
/// </summary>
[Collection("OrchestratorTests")]
public sealed class ClusterMasterReplayTests
{
    // ── CGF1-S0305: standard fan-out after TASK-T001 ──────────────────────

    /// <summary>
    /// Verifies that when transitioning from <see cref="ClusterState.OperatingReplay"/>
    /// to <see cref="ClusterState.OperatingLive"/>, ClusterMaster fans out PrepareLive
    /// via the standard 2PC path (no separate branch fan-out).
    /// Time freeze is NOT the responsibility of ClusterMaster after TASK-T001.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void BranchTransition_FansOut_PrepareLiveAsStandardOp()
    {
        var config = new ClusterConfiguration
        {
            Mandatory                  = new[] { "SimHost" },
            HeartbeatTimeoutSeconds    = 60f,
            TransactionHistoryCapacity = 10,
        };

        var bus = new FdpEventBus();

        using var master = new ClusterMaster(bus, config);

        // Register mandatory node.
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId        = 1,
            SubsystemName = "SimHost",
            LocalStateId  = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();

        // Transition to OperatingReplay.
        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.OperatingReplay).ToString(),
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();

        // Drain the OperatingReplay fan-out intents.
        bus.ReadManaged<ExecuteNodeOpIntent>().ToList();

        // Transition from OperatingReplay to OperatingLive (Live-from-Replay branch).
        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.OperatingLive).ToString(),
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();

        // Verify: ClusterMaster fans out PrepareLive via the standard 2PC path.
        var intents = bus.ReadManaged<ExecuteNodeOpIntent>()
            .Where(i => i.Operation == FdpNodeOpType.PrepareLive)
            .ToList();
        Assert.True(intents.Any(),
            "ClusterMaster must fan out PrepareLive as a standard 2PC operation after TASK-T001.");
    }
}
