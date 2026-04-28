using System;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Hrot.NED.Descriptors.Orchestration;
using ClusterState   = Hrot.NED.Descriptors.Orchestration.ClusterState;
using ClusterOpType  = Hrot.NED.Descriptors.Orchestration.ClusterOpType;
using FdpNodeOpType  = Fdp.Toolkit.Orchestration.NodeOpType;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// Tests for ClusterMaster 2PC handling of the Live-from-Replay branch transition
/// after TASK-T001. SnapAndPause / RestoreTime are now owned by
/// <see cref="LiveBranchProcessManager"/>; see LiveBranchProcessManagerTests (SC2).
/// This file verifies ClusterMaster correctly tracks ACKs for the standard PrepareLive
/// fan-out.
/// </summary>
[Collection("OrchestratorTests")]
public sealed class ClusterMasterBranchTests
{
    private static ClusterConfiguration MandatorySimHostConfig() => new ClusterConfiguration
    {
        Mandatory                  = new[] { "SimHost" },
        HeartbeatTimeoutSeconds    = 60f,
        TransactionHistoryCapacity = 10,
    };

    private static int BootstrapToOperatingReplay(FdpEventBus bus, ClusterMaster master)
    {
        const int nodeId = 1;

        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId        = nodeId,
            SubsystemName = "SimHost",
            LocalStateId  = (int)Fdp.Toolkit.Orchestration.ClusterState.Idle,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();

        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.OperatingReplay).ToString(),
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();

        return nodeId;
    }

    // ── T21: standard ACK tracking for PrepareLive after TASK-T001 ───────────

    /// <summary>
    /// T21 (revised): After TASK-T001, ClusterMaster tracks ACKs for PrepareLive via
    /// the standard _pendingBusTransitionAcks path. When all ACKs arrive,
    /// ClusterOpCompletedEvent is published. SnapAndPause is now handled by
    /// LiveBranchProcessManager (see LiveBranchProcessManagerTests.SC2).
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void LiveBranch_StandardAckTracking_PublishesClusterOpCompletedOnAllAcks()
    {
        var bus    = new FdpEventBus();
        var master = new ClusterMaster(bus, MandatorySimHostConfig());

        int nodeId = BootstrapToOperatingReplay(bus, master);

        var requestId = Guid.NewGuid();
        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = requestId,
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.OperatingLive).ToString(),
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();

        var intents = bus.ReadManaged<ExecuteNodeOpIntent>().ToList();
        Assert.True(intents.Any(i => i.Operation == FdpNodeOpType.PrepareLive),
            "ClusterMaster must fan out a PrepareLive NodeOpIntent.");
        var txId = intents.First(i => i.Operation == FdpNodeOpType.PrepareLive).TransactionId;

        // ACK all prepare ops (CommitState is handled synchronously and does not produce an ACK).
        foreach (var intent in intents.Where(i => i.Operation != FdpNodeOpType.CommitState))
        {
            bus.PublishManaged(new NodeOpCompletedEvent
            {
                TransactionId   = intent.TransactionId,
                Operation       = intent.Operation,
                NodeId          = nodeId,
                StatusCode      = OrchestrationStatusCode.Success,
                IsParticipating = true,
                ResultPayload   = intent.Operation == FdpNodeOpType.PrepareLive
                    ? (object?)new LiveBranchResult(new Fdp.Core.GlobalTime
                    {
                        TotalWallTicks = 7777L,
                        TotalTime      = 3.0,
                    })
                    : null,
            });
        }
        _ = txId; // captured for reference
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();

        // Verify ClusterOpCompletedEvent is published after all ACKs.
        var completed = bus.ReadManaged<ClusterOpCompletedEvent>().ToList();
        Assert.True(completed.Any(e => !e.StatusCode.IsError()),
            "ClusterMaster must publish ClusterOpCompletedEvent(Success) after all ACKs arrive.");
    }
}
