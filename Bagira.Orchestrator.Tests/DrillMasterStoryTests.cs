using System;
using System.Threading;
using Bagira.BDC.SSTD.Orchestration;
using CycloneDDS.Runtime;
using FDP.Toolkit.Orchestration;
using Xunit;

namespace Bagira.Orchestrator.Tests;

/// <summary>
/// Tests for the ManageStory 2PC in <see cref="DrillMaster"/> (BATCH-21 Part A.1 /
/// CGF1-S0308):
/// <para>
/// <see cref="DrillMaster.ActiveStories"/> must NOT be mutated immediately when
/// <see cref="SysOpType.ManageStory"/> is processed — the update is deferred until
/// node <see cref="NodeOpStatus"/> ACKs are consumed.
/// </para>
/// </summary>
[Collection("OrchestratorTests")]
public sealed class DrillMasterStoryTests
{
    private const int TestDomain = 15;

    /// <summary>
    /// Verifies the end-to-end 2PC story flow:
    /// <list type="number">
    ///   <item>After FanOutNodeOp for <see cref="NodeOpType.StartStory"/>,
    ///     <c>ActiveStories</c> is still empty (deferred).</item>
    ///   <item>After the targeted node ACKs with <c>IsParticipating=true</c>,
    ///     <c>ActiveStories</c> contains the story.</item>
    /// </list>
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void StartStory_ActiveStoriesUpdated_AfterNodeAck_NotBefore()
    {
        var config = new ClusterConfiguration
        {
            Mandatory                = new[] { "SimHost" },
            HeartbeatTimeoutSeconds  = 60f,
            TransactionHistoryCapacity = 10,
        };

        using var participant     = new DdsParticipant(TestDomain);
        using var sysOpWriter     = new DdsWriter<SysOpRequest>(participant);
        using var hbWriter        = new DdsWriter<NodeHeartbeat>(participant);
        using var nodeOpCmdReader = new DdsReader<NodeOpCommand>(participant);
        using var nodeOpStatusWriter = new DdsWriter<NodeOpStatus>(participant);

        using var drill = new DrillMaster(participant, config);

        // First tick to settle DDS discovery.
        Thread.Sleep(400);

        // Register mandatory SimHost node.
        hbWriter.Write(new NodeHeartbeat
        {
            NodeId        = 1,
            SubsystemName = "SimHost",
            LocalDsmState = DSMState.Standby,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
        });
        Thread.Sleep(200);
        drill.Tick(); // bootstrap latch clears

        // Advance cluster to RunningLive (required for ManageStory).
        sysOpWriter.Write(new SysOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = SysOpType.TransitionState,
            PayloadJson   = ((int)DSMState.RunningLive).ToString(),
        });
        Thread.Sleep(200);
        drill.Tick();

        // ── Issue a ManageStory(Start) request ────────────────────────────
        var storyId = Guid.NewGuid();
        var scenarioId = "story_2pc_test";
        sysOpWriter.Write(new SysOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = SysOpType.ManageStory,
            PayloadJson   = $"{{\"Mode\":\"Start\",\"StoryId\":\"{storyId}\",\"ScenarioId\":\"{scenarioId}\"}}",
        });
        Thread.Sleep(200);
        drill.Tick();

        // ── Assert 1: ActiveStories must be empty before node ACKs arrive ──
        Assert.Empty(drill.ActiveStories);

        // ── Capture the StartStory command sent to node 1 ─────────────────
        Guid? storyTxId = null;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && storyTxId == null)
        {
            using var cmdScope = nodeOpCmdReader.Take();
            foreach (var s in cmdScope)
            {
                if (s.IsValid && s.Data.Operation == NodeOpType.StartStory)
                {
                    storyTxId = s.Data.TransactionId;
                    break;
                }
            }
            if (storyTxId == null) Thread.Sleep(20);
        }
        Assert.True(storyTxId.HasValue,
            "DrillMaster must fan out a StartStory NodeOpCommand after ManageStory.");

        // ── Node ACKs with IsParticipating=true ────────────────────────────
        nodeOpStatusWriter.Write(new NodeOpStatus
        {
            TransactionId   = storyTxId!.Value,
            NodeId          = 1,
            StatusCode      = OrchestrationStatusCode.Success,
            IsParticipating = true,
            ResultJson      = string.Empty,
        });
        Thread.Sleep(200);
        drill.Tick(); // ConsumeNodeOpStatuses updates _activeStories

        // ── Assert 2: ActiveStories now contains the story ─────────────────
        Assert.Contains(storyId, drill.ActiveStories);
    }

    /// <summary>
    /// Non-participating ACK (<c>IsParticipating=false</c>) must count towards
    /// completion.  When ALL targeted nodes reply non-participating, the story set
    /// must still be updated (the operation completes).
    ///
    /// <para>Policy: every ACK — participating or not — removes the node from the
    /// pending set.  A non-participating reply must <b>not block</b> completion.</para>
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void StartStory_NonParticipatingAck_CountsTowardCompletion()
    {
        var config = new ClusterConfiguration
        {
            Mandatory                = new[] { "SimHost" },
            HeartbeatTimeoutSeconds  = 60f,
            TransactionHistoryCapacity = 10,
        };

        using var participant         = new DdsParticipant(TestDomain);
        using var sysOpWriter         = new DdsWriter<SysOpRequest>(participant);
        using var hbWriter            = new DdsWriter<NodeHeartbeat>(participant);
        using var nodeOpCmdReader     = new DdsReader<NodeOpCommand>(participant);
        using var nodeOpStatusWriter  = new DdsWriter<NodeOpStatus>(participant);

        using var drill = new DrillMaster(participant, config);
        Thread.Sleep(400);

        hbWriter.Write(new NodeHeartbeat
        {
            NodeId        = 2,
            SubsystemName = "SimHost",
            LocalDsmState = DSMState.Standby,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
        });
        Thread.Sleep(200);
        drill.Tick();

        sysOpWriter.Write(new SysOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = SysOpType.TransitionState,
            PayloadJson   = ((int)DSMState.RunningLive).ToString(),
        });
        Thread.Sleep(200);
        drill.Tick();

        var storyId = Guid.NewGuid();
        sysOpWriter.Write(new SysOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = SysOpType.ManageStory,
            PayloadJson   = $"{{\"Mode\":\"Start\",\"StoryId\":\"{storyId}\",\"ScenarioId\":\"irrelevant\"}}",
        });
        Thread.Sleep(200);
        drill.Tick();

        Assert.Empty(drill.ActiveStories); // still deferred

        Guid? storyTxId = null;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && storyTxId == null)
        {
            using var cmdScope = nodeOpCmdReader.Take();
            foreach (var s in cmdScope)
                if (s.IsValid && s.Data.Operation == NodeOpType.StartStory)
                { storyTxId = s.Data.TransactionId; break; }
            if (storyTxId == null) Thread.Sleep(20);
        }
        Assert.True(storyTxId.HasValue);

        // Node ACKs with IsParticipating=false — should still count as a response.
        nodeOpStatusWriter.Write(new NodeOpStatus
        {
            TransactionId   = storyTxId!.Value,
            NodeId          = 2,
            StatusCode      = OrchestrationStatusCode.Success,
            IsParticipating = false,   // ← non-participating
            ResultJson      = string.Empty,
        });
        Thread.Sleep(200);
        drill.Tick();

        // Non-participating ACK must not block — story set still updated.
        Assert.Contains(storyId, drill.ActiveStories);
    }

    /// <summary>
    /// When a node responds to a StartStory with an error StatusCode (NAK), the
    /// ManageStory 2PC must abort immediately:
    /// - ActiveStories must NOT be updated.
    /// - SysOpStatus must be published with StatusCode == Rejected.
    /// (BATCH-22 Part A.1 / DEBT-TRACKER row CGF-1-BATCH-21 review)
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void StartStory_NakFromNode_AbortsPendingTask_ActiveStoriesUnchanged()
    {
        var config = new ClusterConfiguration
        {
            Mandatory = new[] { "SimHost" },
            HeartbeatTimeoutSeconds = 60f,
            TransactionHistoryCapacity = 10,
        };

        using var participant        = new DdsParticipant(TestDomain);
        using var sysOpWriter        = new DdsWriter<SysOpRequest>(participant);
        using var sysOpStatusReader  = new DdsReader<SysOpStatus>(participant);
        using var hbWriter           = new DdsWriter<NodeHeartbeat>(participant);
        using var nodeOpCmdReader    = new DdsReader<NodeOpCommand>(participant);
        using var nodeOpStatusWriter = new DdsWriter<NodeOpStatus>(participant);

        using var drill = new DrillMaster(participant, config);
        Thread.Sleep(400);

        hbWriter.Write(new NodeHeartbeat
        {
            NodeId = 3, SubsystemName = "SimHost",
            LocalDsmState = DSMState.Standby, WallTicksUtc = DateTimeOffset.UtcNow.Ticks,
        });
        Thread.Sleep(200);
        drill.Tick();

        sysOpWriter.Write(new SysOpRequest
        {
            RequestId = Guid.NewGuid(), OperationType = SysOpType.TransitionState,
            PayloadJson = ((int)DSMState.RunningLive).ToString(),
        });
        Thread.Sleep(200);
        drill.Tick();

        var storyId    = Guid.NewGuid();
        var requestId  = Guid.NewGuid();
        sysOpWriter.Write(new SysOpRequest
        {
            RequestId     = requestId,
            OperationType = SysOpType.ManageStory,
            PayloadJson   = $"{{\"Mode\":\"Start\",\"StoryId\":\"{storyId}\",\"ScenarioId\":\"nak_test\"}}",
        });
        Thread.Sleep(200);
        drill.Tick();

        Assert.Empty(drill.ActiveStories);

        // Capture the StartStory command TransactionId.
        Guid? storyTxId = null;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && storyTxId == null)
        {
            using var cmdScope = nodeOpCmdReader.Take();
            foreach (var s in cmdScope)
                if (s.IsValid && s.Data.Operation == NodeOpType.StartStory)
                { storyTxId = s.Data.TransactionId; break; }
            if (storyTxId == null) Thread.Sleep(20);
        }
        Assert.True(storyTxId.HasValue, "DrillMaster must fan out StartStory.");

        // Node NAKs with an error StatusCode.
        nodeOpStatusWriter.Write(new NodeOpStatus
        {
            TransactionId   = storyTxId!.Value,
            NodeId          = 3,
            StatusCode      = OrchestrationStatusCode.Timeout, // ← error
            IsParticipating = true,
            ResultJson      = string.Empty,
        });
        Thread.Sleep(200);
        drill.Tick();

        // ActiveStories must NOT be updated on NAK.
        Assert.Empty(drill.ActiveStories);

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
                    && s.Data.StatusCode == OrchestrationStatusCode.Rejected)
                {
                    receivedRejected = true;
                    break;
                }
            }
            if (!receivedRejected) Thread.Sleep(20);
        }
        Assert.True(receivedRejected, "SysOpStatus.Rejected must be published when a node NAKs ManageStory.");
    }

    /// <summary>
    /// When the ManageStory payload is missing a valid StoryId or Mode, the SysOpRequest
    /// must be rejected immediately with SysOpStatus.Rejected. No NodeOpCommand (StartStory)
    /// must be fanned out to nodes.
    /// (BATCH-22 Part A.2 / DEBT-TRACKER row CGF-1-BATCH-21 review — orphan node ops)
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ManageStory_BadPayload_Rejected_NoStartStoryFanOut()
    {
        var config = new ClusterConfiguration
        {
            Mandatory = new[] { "SimHost" },
            HeartbeatTimeoutSeconds = 60f,
            TransactionHistoryCapacity = 10,
        };

        using var participant       = new DdsParticipant(TestDomain);
        using var sysOpWriter       = new DdsWriter<SysOpRequest>(participant);
        using var sysOpStatusReader = new DdsReader<SysOpStatus>(participant);
        using var hbWriter          = new DdsWriter<NodeHeartbeat>(participant);
        using var nodeOpCmdReader   = new DdsReader<NodeOpCommand>(participant);

        using var drill = new DrillMaster(participant, config);
        Thread.Sleep(400);

        hbWriter.Write(new NodeHeartbeat
        {
            NodeId = 4, SubsystemName = "SimHost",
            LocalDsmState = DSMState.Standby, WallTicksUtc = DateTimeOffset.UtcNow.Ticks,
        });
        Thread.Sleep(200);
        drill.Tick();

        sysOpWriter.Write(new SysOpRequest
        {
            RequestId = Guid.NewGuid(), OperationType = SysOpType.TransitionState,
            PayloadJson = ((int)DSMState.RunningLive).ToString(),
        });
        Thread.Sleep(200);
        drill.Tick();

        var requestId = Guid.NewGuid();
        // Payload missing StoryId → must be rejected.
        sysOpWriter.Write(new SysOpRequest
        {
            RequestId     = requestId,
            OperationType = SysOpType.ManageStory,
            PayloadJson   = "{\"Mode\":\"Start\",\"ScenarioId\":\"missing_story_id\"}",
        });
        Thread.Sleep(200);
        drill.Tick();

        // No StartStory command must have been issued.
        bool startStoryFannedOut = false;
        using (var cmdScope = nodeOpCmdReader.Take())
        {
            foreach (var s in cmdScope)
                if (s.IsValid && s.Data.Operation == NodeOpType.StartStory)
                { startStoryFannedOut = true; break; }
        }
        Assert.False(startStoryFannedOut, "No StartStory NodeOpCommand must be issued for a bad ManageStory payload.");

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
                    && s.Data.StatusCode == OrchestrationStatusCode.Rejected)
                {
                    receivedRejected = true;
                    break;
                }
            }
            if (!receivedRejected) Thread.Sleep(20);
        }
        Assert.True(receivedRejected, "SysOpStatus.Rejected must be published for a bad ManageStory payload.");

        // ActiveStories must be empty.
        Assert.Empty(drill.ActiveStories);
    }

    /// <summary>
    /// When all nodes ACK successfully, SysOpStatus with StatusCode == Success (Completed)
    /// must be published via the sys-op channel so clients can correlate the ManageStory
    /// round-trip end-to-end.
    /// (BATCH-22 Part A.1 / DEBT-TRACKER row CGF-1-BATCH-21 review — no Completed)
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void StartStory_AllAcks_EmitsSysOpStatusSuccess()
    {
        var config = new ClusterConfiguration
        {
            Mandatory = new[] { "SimHost" },
            HeartbeatTimeoutSeconds = 60f,
            TransactionHistoryCapacity = 10,
        };

        using var participant        = new DdsParticipant(TestDomain);
        using var sysOpWriter        = new DdsWriter<SysOpRequest>(participant);
        using var sysOpStatusReader  = new DdsReader<SysOpStatus>(participant);
        using var hbWriter           = new DdsWriter<NodeHeartbeat>(participant);
        using var nodeOpCmdReader    = new DdsReader<NodeOpCommand>(participant);
        using var nodeOpStatusWriter = new DdsWriter<NodeOpStatus>(participant);

        using var drill = new DrillMaster(participant, config);
        Thread.Sleep(400);

        hbWriter.Write(new NodeHeartbeat
        {
            NodeId = 5, SubsystemName = "SimHost",
            LocalDsmState = DSMState.Standby, WallTicksUtc = DateTimeOffset.UtcNow.Ticks,
        });
        Thread.Sleep(200);
        drill.Tick();

        sysOpWriter.Write(new SysOpRequest
        {
            RequestId = Guid.NewGuid(), OperationType = SysOpType.TransitionState,
            PayloadJson = ((int)DSMState.RunningLive).ToString(),
        });
        Thread.Sleep(200);
        drill.Tick();

        var storyId   = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        sysOpWriter.Write(new SysOpRequest
        {
            RequestId     = requestId,
            OperationType = SysOpType.ManageStory,
            PayloadJson   = $"{{\"Mode\":\"Start\",\"StoryId\":\"{storyId}\",\"ScenarioId\":\"completed_test\"}}",
        });
        Thread.Sleep(200);
        drill.Tick();

        // Consume InProgress status that was immediately published on accept.
        // (The Completed/Success status only arrives after ACKs are consumed.)

        // Capture StartStory TransactionId.
        Guid? storyTxId = null;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && storyTxId == null)
        {
            using var cmdScope = nodeOpCmdReader.Take();
            foreach (var s in cmdScope)
                if (s.IsValid && s.Data.Operation == NodeOpType.StartStory)
                { storyTxId = s.Data.TransactionId; break; }
            if (storyTxId == null) Thread.Sleep(20);
        }
        Assert.True(storyTxId.HasValue);

        // Node ACKs success.
        nodeOpStatusWriter.Write(new NodeOpStatus
        {
            TransactionId   = storyTxId!.Value,
            NodeId          = 5,
            StatusCode      = OrchestrationStatusCode.Success,
            IsParticipating = true,
            ResultJson      = string.Empty,
        });
        Thread.Sleep(200);
        drill.Tick();

        // ActiveStories updated.
        Assert.Contains(storyId, drill.ActiveStories);

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
                    && s.Data.StatusCode == OrchestrationStatusCode.Success)
                {
                    receivedSuccess = true;
                    break;
                }
            }
            if (!receivedSuccess) Thread.Sleep(20);
        }
        Assert.True(receivedSuccess, "SysOpStatus.Success must be published after all ManageStory ACKs arrive.");
    }
}
