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
    private const int TestDomain = 15;

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
        var config = new ClusterConfiguration
        {
            Mandatory                = new[] { "SimHost" },
            HeartbeatTimeoutSeconds  = 60f,
            TransactionHistoryCapacity = 10,
        };

        using var participant     = new DdsParticipant(TestDomain);
        using var sysOpWriter     = new DdsWriter<ClusterOpRequest>(participant);
        using var hbWriter        = new DdsWriter<NodeHeartbeat>(participant);
        using var nodeOpCmdReader = new DdsReader<NodeOpCommand>(participant);
        using var nodeOpStatusWriter = new DdsWriter<NodeOpStatus>(participant);

        using var exercise = new ClusterMaster(participant, config);

        // First tick to settle DDS discovery.
        Thread.Sleep(400);

        // Register mandatory SimHost node.
        hbWriter.Write(new NodeHeartbeat
        {
            NodeId        = 1,
            SubsystemName = "SimHost",
            LocalClusterState = ClusterState.Idle,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
        });
        Thread.Sleep(200);
        exercise.Tick(); // bootstrap latch clears

        // Advance cluster to RunningLive (required for ManageEpisode).
        sysOpWriter.Write(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.OperatingLive).ToString(),
        });
        Thread.Sleep(200);
        exercise.Tick();

        // ── Issue a ManageEpisode(Start) request ────────────────────────────
        var episodeId = Guid.NewGuid();
        var scenarioId = "episode_2pc_test";
        sysOpWriter.Write(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.ManageEpisode,
            PayloadJson   = $"{{\"Mode\":\"Start\",\"EpisodeId\":\"{episodeId}\",\"ScenarioId\":\"{scenarioId}\"}}",
        });
        Thread.Sleep(200);
        exercise.Tick();

        // ── Assert 1: ActiveEpisodes must be empty before node ACKs arrive ──
        Assert.Empty(exercise.ActiveEpisodes);

        // ── Capture the StartEpisode command sent to node 1 ─────────────────
        Guid? episodeTxId = null;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && episodeTxId == null)
        {
            using var cmdScope = nodeOpCmdReader.Take();
            foreach (var s in cmdScope)
            {
                if (s.IsValid && s.Data.Operation == NodeOpType.StartEpisode)
                {
                    episodeTxId = s.Data.TransactionId;
                    break;
                }
            }
            if (episodeTxId == null) Thread.Sleep(20);
        }
        Assert.True(episodeTxId.HasValue,
            "ClusterMaster must fan out a StartEpisode NodeOpCommand after ManageEpisode.");

        // ── Node ACKs with IsParticipating=true ────────────────────────────
        nodeOpStatusWriter.Write(new NodeOpStatus
        {
            TransactionId   = episodeTxId!.Value,
            NodeId          = 1,
            StatusCode      = (int)OrchestrationStatusCode.Success,
            IsParticipating = true,
            ResultJson      = string.Empty,
        });
        Thread.Sleep(200);
        exercise.Tick(); // ConsumeNodeOpStatuses updates _activeEpisodes

        // ── Assert 2: ActiveEpisodes now contains the episode ─────────────────
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
        var config = new ClusterConfiguration
        {
            Mandatory                = new[] { "SimHost" },
            HeartbeatTimeoutSeconds  = 60f,
            TransactionHistoryCapacity = 10,
        };

        using var participant         = new DdsParticipant(TestDomain);
        using var sysOpWriter         = new DdsWriter<ClusterOpRequest>(participant);
        using var hbWriter            = new DdsWriter<NodeHeartbeat>(participant);
        using var nodeOpCmdReader     = new DdsReader<NodeOpCommand>(participant);
        using var nodeOpStatusWriter  = new DdsWriter<NodeOpStatus>(participant);

        using var exercise = new ClusterMaster(participant, config);
        Thread.Sleep(400);

        hbWriter.Write(new NodeHeartbeat
        {
            NodeId        = 2,
            SubsystemName = "SimHost",
            LocalClusterState = ClusterState.Idle,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
        });
        Thread.Sleep(200);
        exercise.Tick();

        sysOpWriter.Write(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.OperatingLive).ToString(),
        });
        Thread.Sleep(200);
        exercise.Tick();

        var episodeId = Guid.NewGuid();
        sysOpWriter.Write(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.ManageEpisode,
            PayloadJson   = $"{{\"Mode\":\"Start\",\"EpisodeId\":\"{episodeId}\",\"ScenarioId\":\"irrelevant\"}}",
        });
        Thread.Sleep(200);
        exercise.Tick();

        Assert.Empty(exercise.ActiveEpisodes); // still deferred

        Guid? episodeTxId = null;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && episodeTxId == null)
        {
            using var cmdScope = nodeOpCmdReader.Take();
            foreach (var s in cmdScope)
                if (s.IsValid && s.Data.Operation == NodeOpType.StartEpisode)
                { episodeTxId = s.Data.TransactionId; break; }
            if (episodeTxId == null) Thread.Sleep(20);
        }
        Assert.True(episodeTxId.HasValue);

        // Node ACKs with IsParticipating=false — should still count as a response.
        nodeOpStatusWriter.Write(new NodeOpStatus
        {
            TransactionId   = episodeTxId!.Value,
            NodeId          = 2,
            StatusCode      = (int)OrchestrationStatusCode.Success,
            IsParticipating = false,   // ← non-participating
            ResultJson      = string.Empty,
        });
        Thread.Sleep(200);
        exercise.Tick();

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
        var config = new ClusterConfiguration
        {
            Mandatory = new[] { "SimHost" },
            HeartbeatTimeoutSeconds = 60f,
            TransactionHistoryCapacity = 10,
        };

        using var participant        = new DdsParticipant(TestDomain);
        using var sysOpWriter        = new DdsWriter<ClusterOpRequest>(participant);
        using var sysOpStatusReader  = new DdsReader<ClusterOpStatus>(participant);
        using var hbWriter           = new DdsWriter<NodeHeartbeat>(participant);
        using var nodeOpCmdReader    = new DdsReader<NodeOpCommand>(participant);
        using var nodeOpStatusWriter = new DdsWriter<NodeOpStatus>(participant);

        using var exercise = new ClusterMaster(participant, config);
        Thread.Sleep(400);

        hbWriter.Write(new NodeHeartbeat
        {
            NodeId = 3, SubsystemName = "SimHost",
            LocalClusterState = ClusterState.Idle, WallTicksUtc = DateTimeOffset.UtcNow.Ticks,
        });
        Thread.Sleep(200);
        exercise.Tick();

        sysOpWriter.Write(new ClusterOpRequest
        {
            RequestId = Guid.NewGuid(), OperationType = ClusterOpType.TransitionState,
            PayloadJson = ((int)ClusterState.OperatingLive).ToString(),
        });
        Thread.Sleep(200);
        exercise.Tick();

        var episodeId    = Guid.NewGuid();
        var requestId  = Guid.NewGuid();
        sysOpWriter.Write(new ClusterOpRequest
        {
            RequestId     = requestId,
            OperationType = ClusterOpType.ManageEpisode,
            PayloadJson   = $"{{\"Mode\":\"Start\",\"EpisodeId\":\"{episodeId}\",\"ScenarioId\":\"nak_test\"}}",
        });
        Thread.Sleep(200);
        exercise.Tick();

        Assert.Empty(exercise.ActiveEpisodes);

        // Capture the StartEpisode command TransactionId.
        Guid? episodeTxId = null;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && episodeTxId == null)
        {
            using var cmdScope = nodeOpCmdReader.Take();
            foreach (var s in cmdScope)
                if (s.IsValid && s.Data.Operation == NodeOpType.StartEpisode)
                { episodeTxId = s.Data.TransactionId; break; }
            if (episodeTxId == null) Thread.Sleep(20);
        }
        Assert.True(episodeTxId.HasValue, "ClusterMaster must fan out StartEpisode.");

        // Node NAKs with an error StatusCode.
        nodeOpStatusWriter.Write(new NodeOpStatus
        {
            TransactionId   = episodeTxId!.Value,
            NodeId          = 3,
            StatusCode      = (int)OrchestrationStatusCode.Timeout, // ← error
            IsParticipating = true,
            ResultJson      = string.Empty,
        });
        Thread.Sleep(200);
        exercise.Tick();

        // ActiveEpisodes must NOT be updated on NAK.
        Assert.Empty(exercise.ActiveEpisodes);

        // SysOpStatus.Rejected must have been published.
        bool receivedRejected = false;
        var statusDeadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < statusDeadline && !receivedRejected)
        {
            using var statusScope = sysOpStatusReader.Take();
            foreach (var s in statusScope)
            {
                if (s.IsValid
                    && s.Data.RequestId == requestId
                    && s.Data.StatusCode == (int)OrchestrationStatusCode.Rejected)
                {
                    receivedRejected = true;
                    break;
                }
            }
            if (!receivedRejected) Thread.Sleep(20);
        }
        Assert.True(receivedRejected, "SysOpStatus.Rejected must be published when a node NAKs ManageEpisode.");
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
        var config = new ClusterConfiguration
        {
            Mandatory = new[] { "SimHost" },
            HeartbeatTimeoutSeconds = 60f,
            TransactionHistoryCapacity = 10,
        };

        using var participant       = new DdsParticipant(TestDomain);
        using var sysOpWriter       = new DdsWriter<ClusterOpRequest>(participant);
        using var sysOpStatusReader = new DdsReader<ClusterOpStatus>(participant);
        using var hbWriter          = new DdsWriter<NodeHeartbeat>(participant);
        using var nodeOpCmdReader   = new DdsReader<NodeOpCommand>(participant);

        using var exercise = new ClusterMaster(participant, config);
        Thread.Sleep(400);

        hbWriter.Write(new NodeHeartbeat
        {
            NodeId = 4, SubsystemName = "SimHost",
            LocalClusterState = ClusterState.Idle, WallTicksUtc = DateTimeOffset.UtcNow.Ticks,
        });
        Thread.Sleep(200);
        exercise.Tick();

        sysOpWriter.Write(new ClusterOpRequest
        {
            RequestId = Guid.NewGuid(), OperationType = ClusterOpType.TransitionState,
            PayloadJson = ((int)ClusterState.OperatingLive).ToString(),
        });
        Thread.Sleep(200);
        exercise.Tick();

        var requestId = Guid.NewGuid();
        // Payload missing EpisodeId → must be rejected.
        sysOpWriter.Write(new ClusterOpRequest
        {
            RequestId     = requestId,
            OperationType = ClusterOpType.ManageEpisode,
            PayloadJson   = "{\"Mode\":\"Start\",\"ScenarioId\":\"missing_episode_id\"}",
        });
        Thread.Sleep(200);
        exercise.Tick();

        // No StartEpisode command must have been issued.
        bool startEpisodeFannedOut = false;
        using (var cmdScope = nodeOpCmdReader.Take())
        {
            foreach (var s in cmdScope)
                if (s.IsValid && s.Data.Operation == NodeOpType.StartEpisode)
                { startEpisodeFannedOut = true; break; }
        }
        Assert.False(startEpisodeFannedOut, "No StartEpisode NodeOpCommand must be issued for a bad ManageEpisode payload.");

        // SysOpStatus.Rejected must be published.
        bool receivedRejected = false;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && !receivedRejected)
        {
            using var statusScope = sysOpStatusReader.Take();
            foreach (var s in statusScope)
            {
                if (s.IsValid
                    && s.Data.RequestId == requestId
                    && s.Data.StatusCode == (int)OrchestrationStatusCode.Rejected)
                {
                    receivedRejected = true;
                    break;
                }
            }
            if (!receivedRejected) Thread.Sleep(20);
        }
        Assert.True(receivedRejected, "SysOpStatus.Rejected must be published for a bad ManageEpisode payload.");

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
        var config = new ClusterConfiguration
        {
            Mandatory = new[] { "SimHost" },
            HeartbeatTimeoutSeconds = 60f,
            TransactionHistoryCapacity = 10,
        };

        using var participant        = new DdsParticipant(TestDomain);
        using var sysOpWriter        = new DdsWriter<ClusterOpRequest>(participant);
        using var sysOpStatusReader  = new DdsReader<ClusterOpStatus>(participant);
        using var hbWriter           = new DdsWriter<NodeHeartbeat>(participant);
        using var nodeOpCmdReader    = new DdsReader<NodeOpCommand>(participant);
        using var nodeOpStatusWriter = new DdsWriter<NodeOpStatus>(participant);

        using var exercise = new ClusterMaster(participant, config);
        Thread.Sleep(400);

        hbWriter.Write(new NodeHeartbeat
        {
            NodeId = 5, SubsystemName = "SimHost",
            LocalClusterState = ClusterState.Idle, WallTicksUtc = DateTimeOffset.UtcNow.Ticks,
        });
        Thread.Sleep(200);
        exercise.Tick();

        sysOpWriter.Write(new ClusterOpRequest
        {
            RequestId = Guid.NewGuid(), OperationType = ClusterOpType.TransitionState,
            PayloadJson = ((int)ClusterState.OperatingLive).ToString(),
        });
        Thread.Sleep(200);
        exercise.Tick();

        var episodeId   = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        sysOpWriter.Write(new ClusterOpRequest
        {
            RequestId     = requestId,
            OperationType = ClusterOpType.ManageEpisode,
            PayloadJson   = $"{{\"Mode\":\"Start\",\"EpisodeId\":\"{episodeId}\",\"ScenarioId\":\"completed_test\"}}",
        });
        Thread.Sleep(200);
        exercise.Tick();

        // Consume InProgress status that was immediately published on accept.
        // (The Completed/Success status only arrives after ACKs are consumed.)

        // Capture StartEpisode TransactionId.
        Guid? episodeTxId = null;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && episodeTxId == null)
        {
            using var cmdScope = nodeOpCmdReader.Take();
            foreach (var s in cmdScope)
                if (s.IsValid && s.Data.Operation == NodeOpType.StartEpisode)
                { episodeTxId = s.Data.TransactionId; break; }
            if (episodeTxId == null) Thread.Sleep(20);
        }
        Assert.True(episodeTxId.HasValue);

        // Node ACKs success.
        nodeOpStatusWriter.Write(new NodeOpStatus
        {
            TransactionId   = episodeTxId!.Value,
            NodeId          = 5,
            StatusCode      = (int)OrchestrationStatusCode.Success,
            IsParticipating = true,
            ResultJson      = string.Empty,
        });
        Thread.Sleep(200);
        exercise.Tick();

        // ActiveEpisodes updated.
        Assert.Contains(episodeId, exercise.ActiveEpisodes);

        // SysOpStatus.Success (Completed) must be published.
        bool receivedSuccess = false;
        var statusDeadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < statusDeadline && !receivedSuccess)
        {
            using var statusScope = sysOpStatusReader.Take();
            foreach (var s in statusScope)
            {
                if (s.IsValid
                    && s.Data.RequestId == requestId
                    && s.Data.StatusCode == (int)OrchestrationStatusCode.Success)
                {
                    receivedSuccess = true;
                    break;
                }
            }
            if (!receivedSuccess) Thread.Sleep(20);
        }
        Assert.True(receivedSuccess, "SysOpStatus.Success must be published after all ManageEpisode ACKs arrive.");
    }
}
