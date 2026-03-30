using System;
using System.Threading;
using Bagira.BDC.SSTD.Orchestration;
using CycloneDDS.Runtime;
using FDP.Toolkit.Orchestration;
using Xunit;

namespace Bagira.Orchestrator.Tests;

/// <summary>
/// Tests for the CGF1-S0502 fan-out loop in <see cref="DrillMaster"/>:
/// <list type="bullet">
///   <item>A <see cref="SysOpType.TransitionState"/> request fans out
///     <see cref="NodeOpType.PrepareLive"/> and <see cref="NodeOpType.CommitState"/>
///     commands to all active nodes.</item>
///   <item>When no active nodes are registered the fan-out guard skips without error.</item>
///   <item><see cref="DistributedTransaction.SourceDsmState"/> is captured from the
///     state <em>before</em> the optimistic advance.</item>
///   <item><see cref="DistributedTransaction.PayloadJson"/> is copied from the
///     originating <see cref="SysOpRequest"/>.</item>
/// </list>
/// </summary>
[Collection("OrchestratorTests")]
public sealed class DrillMasterFanOutTests
{
    private const int TestDomain = 15;

    private static ClusterConfiguration NoMandatoryConfig() => new ClusterConfiguration
    {
        Mandatory                  = Array.Empty<string>(),
        HeartbeatTimeoutSeconds    = 60f,
        TransactionHistoryCapacity = 10,
    };

    /// <summary>
    /// Writes a Standby heartbeat for a single node and ticks DrillMaster so that the
    /// node is present in <see cref="DrillMaster.NodeRoster"/>.
    /// </summary>
    private static void RegisterNode(
        DdsWriter<NodeHeartbeat> hbWriter, DrillMaster drill,
        int nodeId = 1, string subsystem = "SimHost")
    {
        hbWriter.Write(new NodeHeartbeat
        {
            NodeId        = nodeId,
            SubsystemName = subsystem,
            LocalDsmState = DSMState.Standby,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
        });
        Thread.Sleep(200);
        drill.Tick();
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

        using var drill = new DrillMaster(participant, NoMandatoryConfig());
        Thread.Sleep(400);

        RegisterNode(hbWriter, drill);

        drill.HandleSysOpRequest(new SysOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = SysOpType.TransitionState,
            PayloadJson   = ((int)DSMState.LoadingLive).ToString(),
        });
        drill.Tick();   // drain injected request queue

        bool foundPrepareLive = false;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            drill.Tick();
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
            "DrillMaster must fan out a PrepareLive NodeOpCommand for Standby→LoadingLive.");
    }

    // ── CGF1-S0502: CommitState fan-out ───────────────────────────────────────

    /// <summary>
    /// After <see cref="NodeOpType.PrepareLive"/>, DrillMaster must also fan out a
    /// <see cref="NodeOpType.CommitState"/> command whose <c>PayloadJson</c> is the
    /// integer representation of <see cref="DSMState.LoadingLive"/>.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void TransitionState_Standby_To_LoadingLive_FansOutCommitState()
    {
        using var participant     = new DdsParticipant(TestDomain);
        using var hbWriter        = new DdsWriter<NodeHeartbeat>(participant);
        using var nodeOpCmdReader = new DdsReader<NodeOpCommand>(participant);

        using var drill = new DrillMaster(participant, NoMandatoryConfig());
        Thread.Sleep(400);

        RegisterNode(hbWriter, drill);

        drill.HandleSysOpRequest(new SysOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = SysOpType.TransitionState,
            PayloadJson   = ((int)DSMState.LoadingLive).ToString(),
        });
        drill.Tick();   // drain injected request queue

        bool foundCommitState = false;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            drill.Tick();
            using var cmdScope = nodeOpCmdReader.Take();
            foreach (var s in cmdScope)
            {
                if (s.IsValid && s.Data.Operation == NodeOpType.CommitState &&
                    s.Data.PayloadJson == ((int)DSMState.LoadingLive).ToString())
                {
                    foundCommitState = true;
                    break;
                }
            }
            if (foundCommitState) break;
            Thread.Sleep(20);
        }

        Assert.True(foundCommitState,
            "DrillMaster must fan out a CommitState command with target-state payload after PrepareXxx.");
    }

    // ── CGF1-S0502: no-node guard ─────────────────────────────────────────────

    /// <summary>
    /// When no nodes are registered, the fan-out guard skips the <see cref="NodeOpCommand"/>
    /// writes without throwing.  The transaction is still recorded in
    /// <see cref="DrillMaster.TransactionHistory"/>.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void NoActiveNodes_FanOutSkipped_NoException()
    {
        using var participant = new DdsParticipant(TestDomain);
        using var drill       = new DrillMaster(participant, NoMandatoryConfig());
        Thread.Sleep(400);

        // No heartbeat written → roster is empty → fan-out guard must skip without error.
        var ex = Record.Exception(() =>
        {
            drill.HandleSysOpRequest(new SysOpRequest
            {
                RequestId     = Guid.NewGuid(),
                OperationType = SysOpType.TransitionState,
                PayloadJson   = ((int)DSMState.LoadingLive).ToString(),
            });
            drill.Tick();   // drain injected request queue
        });

        Assert.Null(ex);
        Assert.True(drill.TransactionHistory.Count > 0,
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
        using var drill       = new DrillMaster(participant, NoMandatoryConfig());
        Thread.Sleep(400);

        drill.HandleSysOpRequest(new SysOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = SysOpType.TransitionState,
            PayloadJson   = ((int)DSMState.LoadingLive).ToString(),
        });
        drill.Tick();   // drain injected request queue

        var tx = drill.TransactionHistory[drill.TransactionHistory.Count - 1];
        Assert.Equal(DSMState.Standby, tx.SourceDsmState);
    }

    // ── CGF1-S0501: PayloadJson capture ───────────────────────────────────────

    /// <summary>
    /// <see cref="DistributedTransaction.PayloadJson"/> must equal the
    /// <see cref="SysOpRequest.PayloadJson"/> verbatim so that the 2PC history
    /// table can display it.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void PayloadJson_PopulatedFromSysOpRequest()
    {
        const string payload = "{\"TargetState\":30}";
        using var participant = new DdsParticipant(TestDomain);
        using var drill       = new DrillMaster(participant, NoMandatoryConfig());
        Thread.Sleep(400);

        drill.HandleSysOpRequest(new SysOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = SysOpType.TransitionState,
            PayloadJson   = payload,
        });
        drill.Tick();   // drain injected request queue

        var tx = drill.TransactionHistory[drill.TransactionHistory.Count - 1];
        Assert.Equal(payload, tx.PayloadJson);
    }

    // ── P3 Debt: standalone ReplaySeek fan-out ────────────────────────────────

    /// <summary>
    /// A standalone <see cref="SysOpType.ReplaySeek"/> request fans out
    /// <see cref="NodeOpType.NodeReplaySeek"/> to all active nodes.
    /// Requires the cluster to be in <see cref="DSMState.RunningReplay"/> first.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ReplaySeekStep_FansOutNodeReplaySeek()
    {
        using var participant     = new DdsParticipant(TestDomain);
        using var hbWriter        = new DdsWriter<NodeHeartbeat>(participant);
        using var nodeOpCmdReader = new DdsReader<NodeOpCommand>(participant);

        using var drill = new DrillMaster(participant, NoMandatoryConfig());
        Thread.Sleep(400);

        RegisterNode(hbWriter, drill);

        // Transition to RunningReplay (optimistic: sets _currentDsmState without waiting for node ACKs).
        drill.HandleSysOpRequest(new SysOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = SysOpType.TransitionState,
            PayloadJson   = $"{{\"TargetState\":{(int)DSMState.RunningReplay}}}",
        });
        drill.Tick();

        // Allow DDS to propagate transition fan-out messages.
        Thread.Sleep(200);

        // Drain any PrepareReplay / CommitState messages from the transition.
        using (var drained = nodeOpCmdReader.Take()) { }

        // Now send standalone ReplaySeek.
        drill.HandleSysOpRequest(new SysOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = SysOpType.ReplaySeek,
            PayloadJson   = "{\"TargetWallTicks\":1000}",
        });
        drill.Tick();

        bool foundNodeReplaySeek = false;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            drill.Tick();
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
            "DrillMaster must fan out a NodeReplaySeek command for a standalone ReplaySeek request.");
    }
}
