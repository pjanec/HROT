using System;
using System.Threading;
using Hrot.NED.Descriptors.Orchestration;
using CycloneDDS.Runtime;
using FDP.Toolkit.Orchestration;
using ClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using NodeOpType = Hrot.NED.Descriptors.Orchestration.NodeOpType;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// Tests for the CGF1-S0502 fan-out loop in <see cref="ClusterMaster"/>:
/// <list type="bullet">
///   <item>A <see cref="ClusterOpType.TransitionState"/> request fans out
///     <see cref="NodeOpType.PrepareLive"/> and <see cref="NodeOpType.CommitState"/>
///     commands to all active nodes.</item>
///   <item>When no active nodes are registered the fan-out guard skips without error.</item>
///   <item><see cref="DistributedTransaction.SourceDsmState"/> is captured from the
///     state <em>before</em> the optimistic advance.</item>
///   <item><see cref="DistributedTransaction.PayloadJson"/> is copied from the
///     originating <see cref="ClusterOpRequest"/>.</item>
/// </list>
/// </summary>
[Collection("OrchestratorTests")]
public sealed class ClusterMasterFanOutTests
{
    private const int TestDomain = 15;

    private static ClusterConfiguration NoMandatoryConfig() => new ClusterConfiguration
    {
        Mandatory                  = Array.Empty<string>(),
        HeartbeatTimeoutSeconds    = 60f,
        TransactionHistoryCapacity = 10,
    };

    /// <summary>
    /// Writes a Standby heartbeat for a single node and ticks ClusterMaster so that the
    /// node is present in <see cref="ClusterMaster.NodeRoster"/>.
    /// </summary>
    private static void RegisterNode(
        DdsWriter<NodeHeartbeat> hbWriter, ClusterMaster exercise,
        int nodeId = 1, string subsystem = "SimHost")
    {
        hbWriter.Write(new NodeHeartbeat
        {
            NodeId        = nodeId,
            SubsystemName = subsystem,
            LocalClusterState = ClusterState.Idle,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
        });
        Thread.Sleep(200);
        exercise.Tick();
    }

    // ── CGF1-S0502: PrepareLive fan-out ───────────────────────────────────────

    /// <summary>
    /// After a Standby→LoadingLive transition request is processed against a cluster
    /// with one active node, a <see cref="NodeOpType.PrepareLive"/> NodeOpCommand
    /// must be published on the DDS bus.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void TransitionState_Standby_To_LoadingLive_FansOutPrepareLive()
    {
        using var participant     = new DdsParticipant(TestDomain);
        using var hbWriter        = new DdsWriter<NodeHeartbeat>(participant);
        using var nodeOpCmdReader = new DdsReader<NodeOpCommand>(participant);

        using var exercise = new ClusterMaster(participant, NoMandatoryConfig());
        Thread.Sleep(400);

        RegisterNode(hbWriter, exercise);

        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.LoadingLive).ToString(),
        });
        exercise.Tick();   // drain injected request queue

        bool foundPrepareLive = false;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            exercise.Tick();
            using var cmdScope = nodeOpCmdReader.Take();
            foreach (var s in cmdScope)
            {
                if (s.IsValid && s.Data.Operation == NodeOpType.PrepareLive)
                {
                    foundPrepareLive = true;
                    break;
                }
            }
            if (foundPrepareLive) break;
            Thread.Sleep(20);
        }

        Assert.True(foundPrepareLive,
            "ClusterMaster must fan out a PrepareLive NodeOpCommand for Standby→LoadingLive.");
    }

    // ── CGF1-S0502: CommitState fan-out ───────────────────────────────────────

    /// <summary>
    /// After <see cref="NodeOpType.PrepareLive"/>, ClusterMaster must also fan out a
    /// <see cref="NodeOpType.CommitState"/> command whose <c>PayloadJson</c> is the
    /// integer representation of <see cref="ClusterState.LoadingLive"/>.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void TransitionState_Standby_To_LoadingLive_FansOutCommitState()
    {
        using var participant     = new DdsParticipant(TestDomain);
        using var hbWriter        = new DdsWriter<NodeHeartbeat>(participant);
        using var nodeOpCmdReader = new DdsReader<NodeOpCommand>(participant);

        using var exercise = new ClusterMaster(participant, NoMandatoryConfig());
        Thread.Sleep(400);

        RegisterNode(hbWriter, exercise);

        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.LoadingLive).ToString(),
        });
        exercise.Tick();   // drain injected request queue

        bool foundCommitState = false;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            exercise.Tick();
            using var cmdScope = nodeOpCmdReader.Take();
            foreach (var s in cmdScope)
            {
                if (s.IsValid && s.Data.Operation == NodeOpType.CommitState &&
                    s.Data.PayloadJson == ((int)ClusterState.LoadingLive).ToString())
                {
                    foundCommitState = true;
                    break;
                }
            }
            if (foundCommitState) break;
            Thread.Sleep(20);
        }

        Assert.True(foundCommitState,
            "ClusterMaster must fan out a CommitState command with target-state payload after PrepareXxx.");
    }

    // ── CGF1-S0502: no-node guard ─────────────────────────────────────────────

    /// <summary>
    /// When no nodes are registered, the fan-out guard skips the <see cref="NodeOpCommand"/>
    /// writes without throwing.  The transaction is still recorded in
    /// <see cref="ClusterMaster.TransactionHistory"/>.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void NoActiveNodes_FanOutSkipped_NoException()
    {
        using var participant = new DdsParticipant(TestDomain);
        using var exercise       = new ClusterMaster(participant, NoMandatoryConfig());
        Thread.Sleep(400);

        // No heartbeat written → roster is empty → fan-out guard must skip without error.
        var ex = Record.Exception(() =>
        {
            exercise.HandleClusterOpRequest(new ClusterOpRequest
            {
                RequestId     = Guid.NewGuid(),
                OperationType = ClusterOpType.TransitionState,
                PayloadJson   = ((int)ClusterState.LoadingLive).ToString(),
            });
            exercise.Tick();   // drain injected request queue
        });

        Assert.Null(ex);
        Assert.True(exercise.TransactionHistory.Count > 0,
            "TransactionHistory must contain an entry even when no NodeOpCommands were sent.");
    }

    // ── CGF1-S0501: SourceDsmState capture ───────────────────────────────────

    /// <summary>
    /// <see cref="DistributedTransaction.SourceDsmState"/> must record the state
    /// that was active <em>before</em> the optimistic advance, not the target state.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void SourceDsmState_CapturedBeforeOptimisticAdvance()
    {
        using var participant = new DdsParticipant(TestDomain);
        using var exercise       = new ClusterMaster(participant, NoMandatoryConfig());
        Thread.Sleep(400);

        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.LoadingLive).ToString(),
        });
        exercise.Tick();   // drain injected request queue

        var tx = exercise.TransactionHistory[exercise.TransactionHistory.Count - 1];
        Assert.Equal(ClusterState.Idle, tx.SourceDsmState);
    }

    // ── CMC-S010: DistributedTransaction state capture ─────────────────────────

    /// <summary>
    /// After CMC-S010, <see cref="DistributedTransaction.PayloadJson"/> is always empty
    /// (JSON parsing moved to <see cref="ClusterOpRequestAdapter"/>).
    /// The transaction still records <see cref="DistributedTransaction.TargetDsmState"/>
    /// and <see cref="DistributedTransaction.SourceDsmState"/> for the 2PC history table.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void PayloadJson_PopulatedFromClusterOpRequest()
    {
        using var participant = new DdsParticipant(TestDomain);
        using var exercise       = new ClusterMaster(participant, NoMandatoryConfig());
        Thread.Sleep(400);

        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = $"{{\"TargetState\":{(int)ClusterState.LoadingLive}}}",
        });
        exercise.Tick();   // drain injected request queue

        var tx = exercise.TransactionHistory[exercise.TransactionHistory.Count - 1];
        // CMC-S010: raw JSON is no longer stored in the transaction; PayloadJson is empty.
        Assert.Equal(string.Empty,         tx.PayloadJson);
        // TargetDsmState and SourceDsmState are still captured.
        Assert.Equal(ClusterState.LoadingLive, tx.TargetDsmState);
        Assert.Equal(ClusterState.Idle,         tx.SourceDsmState);
    }

    // ── P3 Debt: standalone ReplaySeek fan-out ────────────────────────────────

    /// <summary>
    /// A standalone <see cref="ClusterOpType.ReplaySeek"/> request fans out
    /// <see cref="NodeOpType.NodeReplaySeek"/> to all active nodes.
    /// Requires the cluster to be in <see cref="ClusterState.OperatingReplay"/> first.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ReplaySeekStep_FansOutNodeReplaySeek()
    {
        using var participant     = new DdsParticipant(TestDomain);
        using var hbWriter        = new DdsWriter<NodeHeartbeat>(participant);
        using var nodeOpCmdReader = new DdsReader<NodeOpCommand>(participant);

        using var exercise = new ClusterMaster(participant, NoMandatoryConfig());
        Thread.Sleep(400);

        RegisterNode(hbWriter, exercise);

        // Transition to RunningReplay (optimistic: sets _currentDsmState without waiting for node ACKs).
        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = $"{{\"TargetState\":{(int)ClusterState.OperatingReplay}}}",
        });
        exercise.Tick();

        // Allow DDS to propagate transition fan-out messages.
        Thread.Sleep(200);

        // Drain any PrepareReplay / CommitState messages from the transition.
        using (var drained = nodeOpCmdReader.Take()) { }

        // Now send standalone ReplaySeek.
        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.ReplaySeek,
            PayloadJson   = "{\"TargetWallTicks\":1000}",
        });
        exercise.Tick();

        bool foundNodeReplaySeek = false;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            exercise.Tick();
            using var cmdScope = nodeOpCmdReader.Take();
            foreach (var s in cmdScope)
            {
                if (s.IsValid && s.Data.Operation == NodeOpType.NodeReplaySeek)
                {
                    foundNodeReplaySeek = true;
                    break;
                }
            }
            if (foundNodeReplaySeek) break;
            Thread.Sleep(20);
        }

        Assert.True(foundNodeReplaySeek,
            "ClusterMaster must fan out a NodeReplaySeek command for a standalone ReplaySeek request.");
    }
}
