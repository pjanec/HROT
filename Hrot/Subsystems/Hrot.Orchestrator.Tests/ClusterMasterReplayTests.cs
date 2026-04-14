using System;
using System.Linq;
using System.Threading;
using Fdp.Core;
using Hrot.NED.Descriptors.Orchestration;
using Fdp.Toolkit.Orchestration;
using ClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using NodeOpType = Hrot.NED.Descriptors.Orchestration.NodeOpType;
using FdpNodeOpType = Fdp.Toolkit.Orchestration.NodeOpType;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// Tests that verify the Live-from-Replay temporal interlock (CGF1-S0305):
/// <see cref="ClusterMaster"/> must freeze the cluster time scale to <c>0.0</c>
/// before issuing a <see cref="NodeOpType.PrepareLive"/> command when transitioning
/// from <see cref="ClusterState.OperatingReplay"/> to <see cref="ClusterState.LoadingLive"/>.
/// </summary>
[Collection("OrchestratorTests")]
public sealed class ClusterMasterReplayTests
{

    // ── CGF1-S0305 success condition: TimeFrozenDuringBranchTransition ────

    /// <summary>
    /// Verifies that <see cref="ClusterMaster"/> calls
    /// <see cref="ReplayMasterModule.FreezeTime"/> (setting scale to 0.0) the moment
    /// a <c>TransitionState → RunningLive</c> request is processed while the cluster is
    /// in <see cref="ClusterState.OperatingReplay"/>.
    ///
    /// <para>
    /// A single SimHost node is registered so the fan-out is tracked and time stays
    /// frozen until an explicit ACK arrives.  The test asserts that after the branch
    /// tick (before the ACK) the scale is <c>0.0f</c>.
    /// </para>
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void TimeFrozenDuringBranchTransition()
    {
        // One mandatory node so the bootstrap requires a heartbeat.
        var config = new ClusterConfiguration
        {
            Mandatory                  = new[] { "SimHost" },
            HeartbeatTimeoutSeconds    = 60f,
            TransactionHistoryCapacity = 10,
        };

        var bus = new FdpEventBus();
        float currentScale = 1.0f;
        var   module       = new ReplayMasterModule(
            s => currentScale = s,
            () => currentScale);

        using var exercise = new ClusterMaster(bus, config);
        exercise.SetReplayMasterModule(module);

        // ── Register mandatory node ────────────────────────────────────────
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId          = 1,
            SubsystemName   = "SimHost",
            LocalStateId    = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
            WallTicksUtc    = DateTimeOffset.UtcNow.Ticks,
        });
        bus.SwapBuffers();
        exercise.Tick(); // bootstrap latch clears
        bus.SwapBuffers();

        // ── Step 1: Transition Standby → RunningReplay ─────────────────────
        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.OperatingReplay).ToString(),
        });
        bus.SwapBuffers();
        exercise.Tick(); // processes Standby → RunningReplay
        bus.SwapBuffers();
        Assert.Equal(1.0f, currentScale); // not frozen yet

        // ── Step 2: Transition RunningReplay → RunningLive (passes LoadingLive) ──
        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.OperatingLive).ToString(),
        });
        bus.SwapBuffers();
        exercise.Tick(); // detects RunningReplay → LoadingLive branch — freezes time
        bus.SwapBuffers();

        // ── Assertion: time must be frozen (node ACK not yet delivered) ────
        Assert.Equal(0.0f, currentScale);
    }

    /// <summary>
    /// When a branch transition has active nodes, time stays frozen until all
    /// branch ACKs arrive.  This verifies the ACK-restore path in
    /// <see cref="ClusterMaster.ConsumeNodeOpStatuses"/>.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void TimeFrozen_RestoredAfterAllAcks()
    {
        var config = new ClusterConfiguration
        {
            Mandatory                  = new[] { "SimHost" },
            HeartbeatTimeoutSeconds    = 60f,
            TransactionHistoryCapacity = 10,
        };

        var bus = new FdpEventBus();
        float currentScale = 1.0f;
        var   module       = new ReplayMasterModule(
            s => currentScale = s,
            () => currentScale);

        using var exercise = new ClusterMaster(bus, config);
        exercise.SetReplayMasterModule(module);

        // ── Register the mandatory node with a Standby heartbeat ─────────
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId          = 1,
            SubsystemName   = "SimHost",
            LocalStateId    = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
            WallTicksUtc    = DateTimeOffset.UtcNow.Ticks,
        });
        bus.SwapBuffers();
        exercise.Tick(); // latch clears
        bus.SwapBuffers();

        // ── Advance to RunningReplay ──────────────────────────────────────
        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.OperatingReplay).ToString(),
        });
        bus.SwapBuffers();
        exercise.Tick();
        bus.SwapBuffers();
        Assert.Equal(1.0f, currentScale);

        // ── Branch to RunningLive while one node is active ────────────────
        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.OperatingLive).ToString(),
        });
        bus.SwapBuffers();
        exercise.Tick(); // time frozen, PrepareLive fanned out to node 1
        bus.SwapBuffers();

        Assert.Equal(0.0f, currentScale);

        // ── Capture the branch TransactionId from the fanned-out ExecuteNodeOpIntent ──
        var intents = bus.ConsumeManaged<ExecuteNodeOpIntent>()
            .Where(i => i.Operation == FdpNodeOpType.PrepareLive)
            .ToList();
        Assert.True(intents.Any(), "ClusterMaster must fan out a PrepareLive NodeOpIntent.");
        var branchTxId = intents[0].TransactionId;

        // ── ACK the branch (simulates node completing PrepareLive) ─────────
        bus.PublishManaged(new NodeOpCompletedEvent
        {
            TransactionId   = branchTxId,
            Operation       = FdpNodeOpType.PrepareLive,
            NodeId          = 1,
            StatusCode      = OrchestrationStatusCode.Success,
            IsParticipating = true,
        });
        bus.SwapBuffers();
        exercise.Tick(); // ConsumeNodeOpStatuses restores time
        bus.SwapBuffers();

        Assert.Equal(1.0f, currentScale);
    }
}
