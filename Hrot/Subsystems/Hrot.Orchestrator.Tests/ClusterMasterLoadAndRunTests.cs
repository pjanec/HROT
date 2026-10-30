using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Hrot.NED.Descriptors.Orchestration;
using ClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using FdpNodeOpType = Fdp.Toolkit.Orchestration.NodeOpType;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// ⭐⭐ CE-3354 (R-3075, docs/DESIGN_Cluster_Load_Phase.md §11.10a) — "loaded" is the load REQUEST's op status Success, which the master
/// publishes only when EVERY node acknowledged; the announced cluster state is advanced optimistically at the round's start and is NOT that fact.
/// </summary>
[Collection("OrchestratorTests")]
public sealed class ClusterMasterLoadAndRunTests
{
    private static ClusterConfiguration Config() => new()
    {
        MandatoryRoles = NodeRole.None,
        Mandatory = Array.Empty<string>(), HeartbeatTimeoutSeconds = 60f, TransactionHistoryCapacity = 10,
    };

    private static void Heartbeat(FdpEventBus bus, ClusterMaster master, params int[] nodeIds)
    {
        foreach (var id in nodeIds)
        {
            bus.PublishManaged(new NodeHeartbeatEvent
            {
                NodeId = id, SubsystemName = "TestNode",
                LocalStateId = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
                WallTicksUtc = DateTimeOffset.UtcNow.Ticks,
            });
            bus.PublishManaged(new NodeCapabilitiesEvent { NodeId = id, Capabilities = new[] { NodeRoleTokens.Brain, NodeRoleTokens.MuscleGround } });
        }
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();
    }

    /// <summary>Answers the given node's prepares (never its CommitState — the master counts prepares), then lets the master see them.
    /// Returns what the master published in reply.</summary>
    private static List<ClusterOpCompletedEvent> Answer(FdpEventBus bus, ClusterMaster master, IEnumerable<ExecuteNodeOpIntent> ops, int nodeId)
    {
        foreach (var op in ops.Where(o => o.TargetNodeId == nodeId && o.Operation != FdpNodeOpType.CommitState))
            bus.PublishManaged(new NodeOpCompletedEvent
                { TransactionId = op.TransactionId, Operation = op.Operation, NodeId = nodeId, StatusCode = OrchestrationStatusCode.Success });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();
        return bus.ReadManaged<ClusterOpCompletedEvent>().ToList();
    }

    /// <summary>
    /// ⭐⭐ §11.10a's premise, measured at the master: with two nodes, the load round's Success for the REQUEST arrives only once the SLOWER
    /// node answered — while the master's state already reads OperatingLive from the round's start (the optimistic advance). ⇒ a consumer
    /// that waits for "every node loaded" (load-and-run, waitForReady) must key on the request's Success, never on the state.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void CE3354_TheLoadRequestsSuccess_WaitsForTheSlowestNode_WhileTheStateAlreadyReadsOperatingLive()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, Config());
        Heartbeat(bus, master, 1, 2);

        var requestId = Guid.NewGuid();
        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId = requestId, OperationType = ClusterOpType.TransitionState,
            PayloadJson = "{\"TargetState\":\"OperatingLive\"}",
        });
        master.Tick();
        bus.SwapBuffers();
        var ops = bus.ReadManaged<ExecuteNodeOpIntent>().ToList();
        Assert.Contains(ops, o => o.TargetNodeId == 1);
        Assert.Contains(ops, o => o.TargetNodeId == 2);

        // node 1 is done; node 2 (the filming node, still building its map) is not
        var afterFirst = Answer(bus, master, ops, nodeId: 1);
        Assert.Equal(ClusterState.OperatingLive, master.CurrentClusterState);                  // ⚠ the state already says it
        Assert.DoesNotContain(afterFirst, e => e.RequestId == requestId);                       // ⭐ the request does not

        var afterSecond = Answer(bus, master, ops, nodeId: 2);
        var done = Assert.Single(afterSecond, e => e.RequestId == requestId);
        Assert.Equal(OrchestrationStatusCode.Success, done.StatusCode);
    }

    // ── ⭐⭐ CE-3354 (R-3075) — LOAD AND RUN is the master's: the clock runs when the load round succeeded on EVERY node ──

    /// <summary>A scenario load parks for its staging (L8); state the staging's completion directly.</summary>
    private static void Unpark(FdpEventBus bus, ClusterMaster master, string scenarioId)
    {
        if (master.ParkedRequestId is not { } parked) return;
        bus.PublishManaged(new PrefetchDistributionCompletedEvent { RequestId = parked, ScenarioId = scenarioId, IsSuccess = true });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
    }

    /// <summary>Starts a transition request and returns the node ops it fanned out (past the staging park).</summary>
    private static List<ExecuteNodeOpIntent> Request(FdpEventBus bus, ClusterMaster master, string payloadJson, string? scenarioId)
    {
        master.HandleClusterOpRequest(new ClusterOpRequest { RequestId = Guid.NewGuid(), OperationType = ClusterOpType.TransitionState, PayloadJson = payloadJson });
        master.Tick();
        bus.SwapBuffers();
        if (scenarioId != null) Unpark(bus, master, scenarioId);
        return bus.ReadManaged<ExecuteNodeOpIntent>().ToList();
    }

    private static int Resumes(FdpEventBus bus) => bus.ReadManaged<Fdp.Toolkit.Time.Domain.ResumeTimeIntent>().Count;

    /// <summary>
    /// ⭐⭐ A live load that did not ask to start paused runs exactly when its round succeeded on EVERY node — not when the first answered
    /// (the state already read OperatingLive then), and once. ⭐ CE-4122's trigger is folded in: a run-settings change between the two answers
    /// re-announces the state mid-round; it no longer matters, because nothing reads the state for the run.
    /// <para>✅ Red-proof: before CE-3354 the master published no resume at all (the run was GlobalContextProcessManager's).</para>
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void CE3354_ALiveLoad_RunsOnce_WhenEveryNodeLoaded_AndAReannouncedStateMidRoundDoesNotMatter()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, Config());
        Heartbeat(bus, master, 1, 2);
        var ops = Request(bus, master, "{\"TargetState\":\"OperatingLive\",\"ScenarioId\":\"s1\"}", "s1");
        Assert.Contains(ops, o => o.TargetNodeId == 2);

        Answer(bus, master, ops, nodeId: 1);
        Assert.Equal(0, Resumes(bus));

        bus.PublishManaged(new SetRunSettingsIntent { RequestId = Guid.NewGuid(), AskToKeepRecordings = true });   // re-announces the state (CE-4122)
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
        Assert.Equal(0, Resumes(bus));

        Answer(bus, master, ops, nodeId: 2);
        Assert.Equal(1, Resumes(bus));
    }

    /// <summary>⭐ Q86 §4-G, re-homed from GlobalContextProcessManager — a live load that asked to start paused stays paused after its round succeeded.</summary>
    [Fact(Timeout = 10_000)]
    public void CE3354_ALiveLoad_ThatAskedToStartPaused_StaysPaused()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, Config());
        Heartbeat(bus, master, 1);
        var ops = Request(bus, master, "{\"TargetState\":\"OperatingLive\",\"ScenarioId\":\"s1\",\"TimeMode\":\"Deterministic\"}", "s1");
        var done = Answer(bus, master, ops, nodeId: 1);
        Assert.Contains(done, e => e.StatusCode == OrchestrationStatusCode.Success);
        Assert.Equal(0, Resumes(bus));
    }

    /// <summary>⭐ Q86 §4-G, re-homed — a load whose round FAILS never runs (it ends in Idle, CE-3345).</summary>
    [Fact(Timeout = 10_000)]
    public void CE3354_ALiveLoad_WhoseRoundFails_NeverRuns()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, Config());
        Heartbeat(bus, master, 1);
        var ops = Request(bus, master, "{\"TargetState\":\"OperatingLive\",\"ScenarioId\":\"s1\"}", "s1");
        foreach (var op in ops.Where(o => o.Operation != FdpNodeOpType.CommitState))
            bus.PublishManaged(new NodeOpCompletedEvent { TransactionId = op.TransactionId, Operation = op.Operation, NodeId = 1, StatusCode = OrchestrationStatusCode.Failure });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
        Assert.Equal(0, Resumes(bus));
    }

    /// <summary>⭐ CE-3329 row 21, re-homed — a DRY RUN asked to start running (the Play button from edit: TimeMode Continuous) runs on its round's
    /// success; one that did not, does not.</summary>
    [Theory(Timeout = 10_000)]
    [InlineData("Continuous", 1)]
    [InlineData(null, 0)]
    public void CE3354_ADryRun_RunsOnItsRoundsSuccess_OnlyWhenItAskedTo(string? timeMode, int expected)
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, Config());
        Heartbeat(bus, master, 1);
        var editOps = Request(bus, master, "{\"TargetState\":\"OperatingEdit\",\"ScenarioId\":\"s1\"}", "s1");
        Answer(bus, master, editOps, nodeId: 1);
        Assert.Equal(ClusterState.OperatingEdit, master.CurrentClusterState);
        var mode = timeMode == null ? "" : ",\"TimeMode\":\"" + timeMode + "\"";
        var ops = Request(bus, master, "{\"TargetState\":\"OperatingPreview\"" + mode + "}", null);
        Answer(bus, master, ops, nodeId: 1);
        Assert.Equal(expected, Resumes(bus));
    }
}
