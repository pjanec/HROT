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
/// Tests for the ManageEpisode 2PC in <see cref="ClusterMaster"/> (BATCH-21 Part A.1 /
/// CGF1-S0308):
/// <para>
/// <see cref="ClusterMaster.ActiveEpisodes"/> must NOT be mutated immediately when
/// <see cref="ClusterOpType.ManageEpisode"/> is processed — the update is deferred until
/// node <see cref="NodeOpStatus"/> ACKs are consumed.
/// </para>
/// </summary>
[Collection("OrchestratorTests")]
public sealed class ClusterMasterEpisodeTests
{
    /// <summary>
    /// Bootstraps a bus-mode ClusterMaster with one mandatory SimHost node and transitions
    /// optimistically to OperatingLive so that ManageEpisode requests are accepted.
    /// </summary>
    private static (ClusterMaster master, FdpEventBus bus) BootstrapToOperatingLive(int nodeId = 1)
    {
        var config = new ClusterConfiguration
        {
            Mandatory                = new[] { "SimHost" },
            HeartbeatTimeoutSeconds  = 60f,
            TransactionHistoryCapacity = 10,
        };
        var bus    = new FdpEventBus();
        var master = new ClusterMaster(bus, config);

        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId        = nodeId,
            SubsystemName = "SimHost",
            LocalStateId  = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
        });
        bus.SwapBuffers();
        master.Tick(); // bootstrap latch clears
        bus.SwapBuffers();

        // Transition to OperatingLive (optimistic advance — ACKs not required).
        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.OperatingLive).ToString(),
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();
        // Drain transition fan-out intents so they don't interfere with episode assertions.
        bus.ReadManaged<ExecuteNodeOpIntent>().ToList();

        return (master, bus);
    }

    /// <summary>
    /// Verifies the end-to-end 2PC episode flow:
    /// <list type="number">
    ///   <item>After FanOutNodeOp for <see cref="NodeOpType.StartEpisode"/>,
    ///     <c>ActiveEpisodes</c> is still empty (deferred).</item>
    ///   <item>After the targeted node ACKs with <c>IsParticipating=true</c>,
    ///     <c>ActiveEpisodes</c> contains the episode.</item>
    /// </list>
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void StartEpisode_ActiveEpisodesUpdated_AfterNodeAck_NotBefore()
    {
        var (exercise, bus) = BootstrapToOperatingLive(nodeId: 1);
        using var _ = exercise;

        // ── Issue a ManageEpisode(Start) request ────────────────────────────
        var episodeId  = Guid.NewGuid();
        var scenarioId = "episode_2pc_test";
        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.ManageEpisode,
            PayloadJson   = $"{{\"IsStart\":true,\"EpisodeId\":\"{episodeId}\",\"ScenarioId\":\"{scenarioId}\"}}",
        });
        bus.SwapBuffers();
        exercise.Tick();
        bus.SwapBuffers();

        // ── Assert 1: ActiveEpisodes must be empty before node ACKs arrive ──
        Assert.Empty(exercise.ActiveEpisodes);

        // ── Capture the StartEpisode intent TransactionId ──────────────────
        var intents = bus.ReadManaged<ExecuteNodeOpIntent>()
            .Where(i => i.Operation == FdpNodeOpType.StartEpisode)
            .ToList();
        Assert.True(intents.Any(),
            "ClusterMaster must fan out a StartEpisode ExecuteNodeOpIntent after ManageEpisode.");
        var episodeTxId = intents[0].TransactionId;

        // ── Node ACKs with IsParticipating=true ────────────────────────────
        bus.PublishManaged(new NodeOpCompletedEvent
        {
            TransactionId   = episodeTxId,
            Operation       = FdpNodeOpType.StartEpisode,
            NodeId          = 1,
            StatusCode      = OrchestrationStatusCode.Success,
            IsParticipating = true,
        });
        bus.SwapBuffers();
        exercise.Tick();
        bus.SwapBuffers();

        // ── Assert 2: ActiveEpisodes now contains the episode ─────────────
        Assert.Contains(episodeId, exercise.ActiveEpisodes);
    }

    /// <summary>
    /// Non-participating ACK (<c>IsParticipating=false</c>) must count towards
    /// completion.  When ALL targeted nodes reply non-participating, the episode set
    /// must still be updated (the operation completes).
    ///
    /// <para>Policy: every ACK — participating or not — removes the node from the
    /// pending set.  A non-participating reply must <b>not block</b> completion.</para>
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void StartEpisode_NonParticipatingAck_CountsTowardCompletion()
    {
        var (exercise, bus) = BootstrapToOperatingLive(nodeId: 2);
        using var _ = exercise;

        var episodeId = Guid.NewGuid();
        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.ManageEpisode,
            PayloadJson   = $"{{\"IsStart\":true,\"EpisodeId\":\"{episodeId}\",\"ScenarioId\":\"irrelevant\"}}",
        });
        bus.SwapBuffers();
        exercise.Tick();
        bus.SwapBuffers();

        Assert.Empty(exercise.ActiveEpisodes); // still deferred

        var intents = bus.ReadManaged<ExecuteNodeOpIntent>()
            .Where(i => i.Operation == FdpNodeOpType.StartEpisode)
            .ToList();
        Assert.True(intents.Any());
        var episodeTxId = intents[0].TransactionId;

        // Node ACKs with IsParticipating=false — should still count as a response.
        bus.PublishManaged(new NodeOpCompletedEvent
        {
            TransactionId   = episodeTxId,
            Operation       = FdpNodeOpType.StartEpisode,
            NodeId          = 2,
            StatusCode      = OrchestrationStatusCode.Success,
            IsParticipating = false,   // ← non-participating
        });
        bus.SwapBuffers();
        exercise.Tick();
        bus.SwapBuffers();

        // Non-participating ACK must not block — episode set still updated.
        Assert.Contains(episodeId, exercise.ActiveEpisodes);
    }

    /// <summary>
    /// When a node responds to a StartEpisode with an error StatusCode (NAK), the
    /// ManageEpisode 2PC must abort immediately:
    /// - ActiveEpisodes must NOT be updated.
    /// - SysOpStatus must be published with StatusCode == Rejected.
    /// (BATCH-22 Part A.1 / DEBT-TRACKER row CGF-1-BATCH-21 review)
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void StartEpisode_NakFromNode_AbortsPendingTask_ActiveEpisodesUnchanged()
    {
        var (exercise, bus) = BootstrapToOperatingLive(nodeId: 3);
        using var _ = exercise;

        var episodeId  = Guid.NewGuid();
        var requestId  = Guid.NewGuid();
        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = requestId,
            OperationType = ClusterOpType.ManageEpisode,
            PayloadJson   = $"{{\"IsStart\":true,\"EpisodeId\":\"{episodeId}\",\"ScenarioId\":\"nak_test\"}}",
        });
        bus.SwapBuffers();
        exercise.Tick();
        bus.SwapBuffers();

        Assert.Empty(exercise.ActiveEpisodes);

        // Capture the StartEpisode intent TransactionId.
        var intents = bus.ReadManaged<ExecuteNodeOpIntent>()
            .Where(i => i.Operation == FdpNodeOpType.StartEpisode)
            .ToList();
        Assert.True(intents.Any(), "ClusterMaster must fan out StartEpisode.");
        var episodeTxId = intents[0].TransactionId;

        // Node NAKs with an error StatusCode.
        bus.PublishManaged(new NodeOpCompletedEvent
        {
            TransactionId   = episodeTxId,
            Operation       = FdpNodeOpType.StartEpisode,
            NodeId          = 3,
            StatusCode      = OrchestrationStatusCode.Timeout, // ← error
            IsParticipating = true,
        });
        bus.SwapBuffers();
        exercise.Tick();
        bus.SwapBuffers();

        // ActiveEpisodes must NOT be updated on NAK.
        Assert.Empty(exercise.ActiveEpisodes);

        // ClusterOpCompletedEvent with Rejected status must be published.
        var completed = bus.ReadManaged<ClusterOpCompletedEvent>().ToList();
        Assert.True(
            completed.Any(e => e.RequestId == requestId && e.StatusCode == OrchestrationStatusCode.Rejected),
            "ClusterOpCompletedEvent(Rejected) must be published when a node NAKs ManageEpisode.");
    }

    /// <summary>
    /// When the ManageEpisode payload is missing a valid EpisodeId or Mode, the ClusterOpRequest
    /// must be rejected immediately with SysOpStatus.Rejected. No NodeOpCommand (StartEpisode)
    /// must be fanned out to nodes.
    /// (BATCH-22 Part A.2 / DEBT-TRACKER row CGF-1-BATCH-21 review — orphan node ops)
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ManageEpisode_BadPayload_Rejected_NoStartEpisodeFanOut()
    {
        var (exercise, bus) = BootstrapToOperatingLive(nodeId: 4);
        using var _ = exercise;

        var requestId = Guid.NewGuid();
        // Payload missing EpisodeId → must be rejected.
        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = requestId,
            OperationType = ClusterOpType.ManageEpisode,
            PayloadJson   = "{\"IsStart\":true,\"ScenarioId\":\"missing_episode_id\"}",
        });
        bus.SwapBuffers();
        exercise.Tick();
        bus.SwapBuffers();

        // No StartEpisode intent must have been issued.
        var intents = bus.ReadManaged<ExecuteNodeOpIntent>().ToList();
        Assert.False(intents.Any(i => i.Operation == FdpNodeOpType.StartEpisode),
            "No StartEpisode intent must be issued for a bad ManageEpisode payload.");

        // ClusterOpCompletedEvent(Rejected) must be published.
        var completed = bus.ReadManaged<ClusterOpCompletedEvent>().ToList();
        Assert.True(
            completed.Any(e => e.RequestId == requestId && e.StatusCode == OrchestrationStatusCode.Rejected),
            "ClusterOpCompletedEvent(Rejected) must be published for bad ManageEpisode payload.");

        // ActiveEpisodes must be empty.
        Assert.Empty(exercise.ActiveEpisodes);
    }

    /// <summary>
    /// When all nodes ACK successfully, SysOpStatus with StatusCode == Success (Completed)
    /// must be published via the sys-op channel so clients can correlate the ManageEpisode
    /// round-trip end-to-end.
    /// (BATCH-22 Part A.1 / DEBT-TRACKER row CGF-1-BATCH-21 review — no Completed)
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void StartEpisode_AllAcks_EmitsSysOpStatusSuccess()
    {
        var (exercise, bus) = BootstrapToOperatingLive(nodeId: 5);
        using var _ = exercise;

        var episodeId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        exercise.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = requestId,
            OperationType = ClusterOpType.ManageEpisode,
            PayloadJson   = $"{{\"IsStart\":true,\"EpisodeId\":\"{episodeId}\",\"ScenarioId\":\"completed_test\"}}",
        });
        bus.SwapBuffers();
        exercise.Tick();
        bus.SwapBuffers();

        // Capture StartEpisode TransactionId.
        var intents = bus.ReadManaged<ExecuteNodeOpIntent>()
            .Where(i => i.Operation == FdpNodeOpType.StartEpisode)
            .ToList();
        Assert.True(intents.Any());
        var episodeTxId = intents[0].TransactionId;

        // Node ACKs success.
        bus.PublishManaged(new NodeOpCompletedEvent
        {
            TransactionId   = episodeTxId,
            Operation       = FdpNodeOpType.StartEpisode,
            NodeId          = 5,
            StatusCode      = OrchestrationStatusCode.Success,
            IsParticipating = true,
        });
        bus.SwapBuffers();
        exercise.Tick();
        bus.SwapBuffers();

        // ActiveEpisodes updated.
        Assert.Contains(episodeId, exercise.ActiveEpisodes);

        // ClusterOpCompletedEvent(Success) must be published.
        var completed = bus.ReadManaged<ClusterOpCompletedEvent>().ToList();
        Assert.True(
            completed.Any(e => e.RequestId == requestId && !e.StatusCode.IsError()),
            "ClusterOpCompletedEvent(Success) must be published after all ManageEpisode ACKs arrive.");
    }
}
