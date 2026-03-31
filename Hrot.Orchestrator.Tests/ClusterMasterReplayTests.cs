using System;
using System.Collections.Generic;
using System.Threading;
using Hrot.NED.Descriptors.Orchestration;
using CycloneDDS.Runtime;
using FDP.Toolkit.Orchestration;
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
    private const int TestDomain = 15;

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

        using var participant      = new DdsParticipant(TestDomain);
        using var sysOpWriter      = new DdsWriter<ClusterOpRequest>(participant);
        using var hbWriter         = new DdsWriter<NodeHeartbeat>(participant);

        float currentScale = 1.0f;
        var   module       = new ReplayMasterModule(
            s => currentScale = s,
            () => currentScale);

        using var exercise = new ClusterMaster(participant, config);
        exercise.SetReplayMasterModule(module);

        Thread.Sleep(400);

        // ── Register mandatory node ────────────────────────────────────────
        hbWriter.Write(new NodeHeartbeat
        {
            NodeId          = 1,
            SubsystemName   = "SimHost",
            LocalClusterState   = ClusterState.Idle,
            WallTicksUtc    = DateTimeOffset.UtcNow.Ticks,
            CpuUsagePercent = 0f,
            RamUsedBytes    = 0L,
        });
        Thread.Sleep(200);
        exercise.Tick(); // bootstrap latch clears

        // ── Step 1: Transition Standby → RunningReplay ─────────────────────
        sysOpWriter.Write(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.OperatingReplay).ToString(),
        });
        Thread.Sleep(200);
        exercise.Tick(); // processes Standby → RunningReplay
        Assert.Equal(1.0f, currentScale); // not frozen yet

        // ── Step 2: Transition RunningReplay → RunningLive (passes LoadingLive) ──
        sysOpWriter.Write(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.OperatingLive).ToString(),
        });
        Thread.Sleep(200);
        exercise.Tick(); // detects RunningReplay → LoadingLive branch — freezes time

        // ── Assertion: time must be frozen (node ACK not yet delivered) ────
        Assert.Equal(0.0f, currentScale);
        // Assert: time must be frozen before the branch ACK arrives
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

        using var participant      = new DdsParticipant(TestDomain);
        using var sysOpWriter      = new DdsWriter<ClusterOpRequest>(participant);
        using var hbWriter         = new DdsWriter<NodeHeartbeat>(participant);
        using var nodeOpWriter     = new DdsWriter<NodeOpCommand>(participant); // for sending ACKs
        using var nodeOpStatusWriter = new DdsWriter<NodeOpStatus>(participant);
        using var nodeOpCmdReader  = new DdsReader<NodeOpCommand>(participant);

        float currentScale = 1.0f;
        var   module       = new ReplayMasterModule(
            s => currentScale = s,
            () => currentScale);

        using var exercise = new ClusterMaster(participant, config);
        exercise.SetReplayMasterModule(module);

        Thread.Sleep(400);

        // ── Register the mandatory node with a Standby heartbeat ─────────
        hbWriter.Write(new NodeHeartbeat
        {
            NodeId          = 1,
            SubsystemName   = "SimHost",
            LocalClusterState   = ClusterState.Idle,
            WallTicksUtc    = DateTimeOffset.UtcNow.Ticks,
            CpuUsagePercent = 0f,
            RamUsedBytes    = 0L,
        });
        Thread.Sleep(200);
        exercise.Tick(); // latch clears

        // ── Advance to RunningReplay ──────────────────────────────────────
        sysOpWriter.Write(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.OperatingReplay).ToString(),
        });
        Thread.Sleep(200);
        exercise.Tick();
        Assert.Equal(1.0f, currentScale);

        // ── Branch to RunningLive while one node is active ────────────────
        sysOpWriter.Write(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.OperatingLive).ToString(),
        });
        Thread.Sleep(200);
        exercise.Tick(); // time frozen, PrepareLive fanned out to node 1

        Assert.Equal(0.0f, currentScale);
        // Assert: time must be frozen after issuing PrepareLive from RunningReplay

        // ── Capture the branch TransactionId from the fanned-out command ──
        Guid? branchTxId = null;
        var   deadline   = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && branchTxId == null)
        {
            using var scope = nodeOpCmdReader.Take();
            foreach (var sample in scope)
            {
                if (sample.IsValid && sample.Data.Operation == NodeOpType.PrepareLive)
                {
                    branchTxId = sample.Data.TransactionId;
                    break;
                }
            }
            if (branchTxId == null) Thread.Sleep(20);
        }
        Assert.True(branchTxId.HasValue, "ClusterMaster must fan out a PrepareLive NodeOpCommand.");

        // ── ACK the branch (simulates node completing PrepareLive) ─────────
        nodeOpStatusWriter.Write(new NodeOpStatus
        {
            TransactionId   = branchTxId!.Value,
            NodeId          = 1,
            StatusCode      = OrchestrationStatusCode.Success,
            IsParticipating = true,
            ResultJson      = string.Empty,
        });
        Thread.Sleep(200);
        exercise.Tick(); // ConsumeNodeOpStatuses restores time

        Assert.Equal(1.0f, currentScale);
        // Assert: time scale must be restored to 1.0 after all branch ACKs are received
    }
}
