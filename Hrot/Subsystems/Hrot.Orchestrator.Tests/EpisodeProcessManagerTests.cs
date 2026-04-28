using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Hrot.NED.Descriptors.Orchestration;
using Xunit;
using FdpNodeOpType = Fdp.Toolkit.Orchestration.NodeOpType;
using ClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// Unit tests for TASK-S003: EpisodeConsensusAggregator and EpisodeProcessManager.
/// Verifies that episode consensus is aggregated correctly and that EpisodeStateChangedEvent
/// is published after episode state mutations.
/// </summary>
[Collection("OrchestratorTests")]
public sealed class EpisodeProcessManagerTests
{
    /// <summary>
    /// SC1: StartEpisode fan-out → node ACK → EpisodeStateChangedEvent contains episode ID.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void StartEpisode_SuccessfulAck_PublishesEpisodeStateChangedEvent()
    {
        var config = new ClusterConfiguration
        {
            Mandatory                = new[] { "SimHost" },
            HeartbeatTimeoutSeconds  = 60f,
            TransactionHistoryCapacity = 10,
        };
        var bus    = new FdpEventBus();
        var master = new ClusterMaster(bus, config);
        var episodeMgr = new EpisodeProcessManager(bus);

        master.RegisterAggregator(new EpisodeConsensusAggregator(FdpNodeOpType.StartEpisode));

        // Bootstrap to OperatingLive.
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId        = 1,
            SubsystemName = "SimHost",
            LocalStateId  = (int)ClusterState.Idle,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();
        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.OperatingLive).ToString(),
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();
        bus.ReadManaged<ExecuteNodeOpIntent>().ToList();

        // Issue ManageEpisode(Start).
        var episodeId = Guid.NewGuid();
        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.ManageEpisode,
            PayloadJson   = $"{{\"IsStart\":true,\"EpisodeId\":\"{episodeId}\",\"ScenarioId\":\"test\"}}",
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();  // Move published intents from WRITE to READ buffer

        var intents = bus.ReadManaged<ExecuteNodeOpIntent>()
            .Where(i => i.Operation == FdpNodeOpType.StartEpisode).ToList();
        var txId = intents[0].TransactionId;

        bus.SwapBuffers();

        // Node ACK.
        bus.PublishManaged(new NodeOpCompletedEvent
        {
            TransactionId   = txId,
            Operation       = FdpNodeOpType.StartEpisode,
            NodeId          = 1,
            StatusCode      = OrchestrationStatusCode.Success,
            IsParticipating = true,
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();
        episodeMgr.Tick();
        bus.SwapBuffers();

        // Assert: EpisodeStateChangedEvent contains the episode ID.
        var stateEvents = bus.ReadManaged<EpisodeStateChangedEvent>().ToList();
        Assert.True(stateEvents.Any(e => e.ActiveEpisodeIds.Contains(episodeId)),
            "EpisodeStateChangedEvent must contain the started episode ID");
    }

    /// <summary>
    /// SC2: StopEpisode → EpisodeStateChangedEvent does NOT contain episode ID.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void StopEpisode_SuccessfulAck_RemovesEpisodeFromStateEvent()
    {
        var config = new ClusterConfiguration
        {
            Mandatory                = new[] { "SimHost" },
            HeartbeatTimeoutSeconds  = 60f,
            TransactionHistoryCapacity = 10,
        };
        var bus    = new FdpEventBus();
        var master = new ClusterMaster(bus, config);
        var episodeMgr = new EpisodeProcessManager(bus);

        master.RegisterAggregator(new EpisodeConsensusAggregator(FdpNodeOpType.StartEpisode));
        master.RegisterAggregator(new EpisodeConsensusAggregator(FdpNodeOpType.StopEpisode));

        // Bootstrap to OperatingLive.
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId        = 1,
            SubsystemName = "SimHost",
            LocalStateId  = (int)ClusterState.Idle,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();
        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.OperatingLive).ToString(),
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();
        bus.ReadManaged<ExecuteNodeOpIntent>().ToList();

        // Start episode.
        var episodeId = Guid.NewGuid();
        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.ManageEpisode,
            PayloadJson   = $"{{\"IsStart\":true,\"EpisodeId\":\"{episodeId}\",\"ScenarioId\":\"test\"}}",
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();  // Move published intents from WRITE to READ buffer
        var startIntents = bus.ReadManaged<ExecuteNodeOpIntent>()
            .Where(i => i.Operation == FdpNodeOpType.StartEpisode).ToList();
        var startTxId = startIntents[0].TransactionId;
        bus.SwapBuffers();

        bus.PublishManaged(new NodeOpCompletedEvent
        {
            TransactionId   = startTxId,
            Operation       = FdpNodeOpType.StartEpisode,
            NodeId          = 1,
            StatusCode      = OrchestrationStatusCode.Success,
            IsParticipating = true,
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();
        episodeMgr.Tick();
        bus.SwapBuffers();

        // Drain start event.
        bus.ReadManaged<EpisodeStateChangedEvent>().ToList();

        // Stop episode.
        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.ManageEpisode,
            PayloadJson   = $"{{\"IsStart\":false,\"EpisodeId\":\"{episodeId}\",\"ScenarioId\":\"test\"}}",
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();  // Move published intents from WRITE to READ buffer
        var stopIntents = bus.ReadManaged<ExecuteNodeOpIntent>()
            .Where(i => i.Operation == FdpNodeOpType.StopEpisode).ToList();
        var stopTxId = stopIntents[0].TransactionId;
        bus.SwapBuffers();

        bus.PublishManaged(new NodeOpCompletedEvent
        {
            TransactionId   = stopTxId,
            Operation       = FdpNodeOpType.StopEpisode,
            NodeId          = 1,
            StatusCode      = OrchestrationStatusCode.Success,
            IsParticipating = true,
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();
        episodeMgr.Tick();
        bus.SwapBuffers();

        // Assert: EpisodeStateChangedEvent does NOT contain the episode ID.
        var stateEvents = bus.ReadManaged<EpisodeStateChangedEvent>().ToList();
        Assert.True(stateEvents.Any(e => !e.ActiveEpisodeIds.Contains(episodeId)),
            "EpisodeStateChangedEvent must NOT contain the stopped episode ID");
    }

    /// <summary>
    /// SC3: NAK → no EpisodeStateChangedEvent published.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void StartEpisode_NakFromNode_NoEpisodeStateChangedEvent()
    {
        var config = new ClusterConfiguration
        {
            Mandatory                = new[] { "SimHost" },
            HeartbeatTimeoutSeconds  = 60f,
            TransactionHistoryCapacity = 10,
        };
        var bus    = new FdpEventBus();
        var master = new ClusterMaster(bus, config);
        var episodeMgr = new EpisodeProcessManager(bus);

        master.RegisterAggregator(new EpisodeConsensusAggregator(FdpNodeOpType.StartEpisode));

        // Bootstrap to OperatingLive.
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId        = 1,
            SubsystemName = "SimHost",
            LocalStateId  = (int)ClusterState.Idle,
            WallTicksUtc  = DateTimeOffset.UtcNow.Ticks,
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();
        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = ((int)ClusterState.OperatingLive).ToString(),
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();
        bus.ReadManaged<ExecuteNodeOpIntent>().ToList();

        // Issue ManageEpisode(Start).
        var episodeId = Guid.NewGuid();
        master.HandleClusterOpRequest(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.ManageEpisode,
            PayloadJson   = $"{{\"IsStart\":true,\"EpisodeId\":\"{episodeId}\",\"ScenarioId\":\"test\"}}",
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();  // Move published intents from WRITE to READ buffer
        var intents = bus.ReadManaged<ExecuteNodeOpIntent>()
            .Where(i => i.Operation == FdpNodeOpType.StartEpisode).ToList();
        var txId = intents[0].TransactionId;
        bus.SwapBuffers();

        // Node NAK.
        bus.PublishManaged(new NodeOpCompletedEvent
        {
            TransactionId   = txId,
            Operation       = FdpNodeOpType.StartEpisode,
            NodeId          = 1,
            StatusCode      = OrchestrationStatusCode.Timeout,  // error
            IsParticipating = true,
        });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();
        episodeMgr.Tick();
        bus.SwapBuffers();

        // Assert: No EpisodeStateChangedEvent published.
        var stateEvents = bus.ReadManaged<EpisodeStateChangedEvent>().ToList();
        Assert.Empty(stateEvents);
    }
}
