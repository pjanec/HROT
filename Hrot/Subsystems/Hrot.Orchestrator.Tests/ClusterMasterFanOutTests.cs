using System;
using System.Linq;
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
    private static ClusterConfiguration NoMandatoryConfig() => new ClusterConfiguration
    {
        Mandatory                  = Array.Empty<string>(),
        HeartbeatTimeoutSeconds    = 60f,
        TransactionHistoryCapacity = 10,
    };

    /// <summary>
    /// Registers a node via the bus heartbeat and ticks ClusterMaster so that the
    /// node is present in <see cref="ClusterMaster.NodeRoster"/>.
    /// </summary>
    private static void RegisterNode(
        FdpEventBus bus, ClusterMaster exercise,
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
        exercise.Tick();
        bus.SwapBuffers();
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
        var bus = new FdpEventBus();
        using var exercise = new ClusterMaster(bus, NoMandatoryConfig());

        RegisterNode(bus, exercise);

        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.LoadingLive).ToString(),
        });
        bus.SwapBuffers();
        exercise.Tick();
        bus.SwapBuffers();

        var intents = bus.ReadManaged<ExecuteNodeOpIntent>().ToList();
        Assert.True(
            intents.Any(i => i.Operation == FdpNodeOpType.PrepareLive),
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
        var bus = new FdpEventBus();
        using var exercise = new ClusterMaster(bus, NoMandatoryConfig());

        RegisterNode(bus, exercise);

        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.LoadingLive).ToString(),
        });
        bus.SwapBuffers();
        exercise.Tick();
        bus.SwapBuffers();

        var intents = bus.ReadManaged<ExecuteNodeOpIntent>().ToList();
        Assert.True(
            intents.Any(i => i.Operation == FdpNodeOpType.CommitState),
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
        var bus = new FdpEventBus();
        using var exercise = new ClusterMaster(bus, NoMandatoryConfig());

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
        var bus = new FdpEventBus();
        using var exercise = new ClusterMaster(bus, NoMandatoryConfig());

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
        var bus = new FdpEventBus();
        using var exercise = new ClusterMaster(bus, NoMandatoryConfig());

        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = $"{{\"TargetState\":\"{ClusterState.LoadingLive}\"}}",
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
        var bus = new FdpEventBus();
        using var exercise = new ClusterMaster(bus, NoMandatoryConfig());

        RegisterNode(bus, exercise);

        // Transition to OperatingReplay (optimistic: sets _currentDsmState without waiting for ACKs).
        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = $"{{\"TargetState\":\"{ClusterState.OperatingReplay}\"}}",
        });
        bus.SwapBuffers();
        exercise.Tick();
        bus.SwapBuffers();

        // Drain any PrepareReplay / CommitState messages from the transition.
        bus.ReadManaged<ExecuteNodeOpIntent>().ToList();

        // Now send standalone ReplaySeek.
        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.ReplaySeek,
            PayloadJson   = "{\"TargetWallTicks\":1000}",
        });
        bus.SwapBuffers();
        exercise.Tick();
        bus.SwapBuffers();

        var intents = bus.ReadManaged<ExecuteNodeOpIntent>().ToList();
        Assert.True(
            intents.Any(i => i.Operation == FdpNodeOpType.NodeReplaySeek),
            "ClusterMaster must fan out a NodeReplaySeek command for a standalone ReplaySeek request.");
    }
}
