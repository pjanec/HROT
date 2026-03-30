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
}
