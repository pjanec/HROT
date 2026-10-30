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
}
