using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Hrot.Network.Orchestration;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// Tests for TASK-S001: StorageConsensusAggregator integration with ClusterMaster.
/// </summary>
[Collection("OrchestratorTests")]
public sealed class StorageConsensusAggregatorTests
{
    private static ClusterConfiguration NoMandatoryConfig() => new ClusterConfiguration
    {
        Mandatory                  = Array.Empty<string>(),
        HeartbeatTimeoutSeconds    = 60f,
        TransactionHistoryCapacity = 10,
    };

    /// <summary>
    /// Registers a single node via heartbeat so it appears in the roster.
    /// </summary>
    private static void RegisterNode(
        FdpEventBus bus, ClusterMaster master,
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
        master.Tick();
        bus.SwapBuffers();
    }

    // ── Success Condition 1: Two nodes, valid manifests ──────────────────────

    /// <summary>
    /// SC1: With two registered nodes and a registered StorageConsensusAggregator,
    /// when each node ACKs SerializeLocal with a single FileManifestEntry,
    /// the ClusterOpCompletedEvent must carry a List&lt;FileManifestEntry&gt; with
    /// exactly two entries.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void SerializeLocal_TwoNodesWithValidManifests_AggregatesIntoSingleList()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, NoMandatoryConfig());

        // Register two nodes.
        RegisterNode(bus, master, nodeId: 1, subsystem: "SimHost");
        RegisterNode(bus, master, nodeId: 2, subsystem: "SimHost");

        // Register the storage aggregator.
        master.RegisterAggregator(new StorageConsensusAggregator());

        // Fan out SerializeLocal.
        var requestId = Guid.NewGuid();
        master.FanOutSerializeLocal(requestId, new[] { 1, 2 });
        var txId = requestId;  // requestId is used as transactionId

        bus.SwapBuffers();
        master.Tick();

        // Publish two ACKs with distinct manifests.
        var manifest1 = new List<FileManifestEntry>
        {
            new FileManifestEntry { SourceUnc = @"\\NODE01\file1.fdp", RelativeDest = "node1/file1.fdp" }
        };
        var manifest2 = new List<FileManifestEntry>
        {
            new FileManifestEntry { SourceUnc = @"\\NODE02\file2.fdp", RelativeDest = "node2/file2.fdp" }
        };

        bus.PublishManaged(new NodeOpCompletedEvent
        {
            TransactionId   = txId,
            Operation       = Fdp.Toolkit.Orchestration.NodeOpType.SerializeLocal,
            NodeId          = 1,
            StatusCode      = OrchestrationStatusCode.Success,
            ResultPayload   = manifest1,
            IsParticipating = true,
        });
        bus.PublishManaged(new NodeOpCompletedEvent
        {
            TransactionId   = txId,
            Operation       = Fdp.Toolkit.Orchestration.NodeOpType.SerializeLocal,
            NodeId          = 2,
            StatusCode      = OrchestrationStatusCode.Success,
            ResultPayload   = manifest2,
            IsParticipating = true,
        });
        bus.SwapBuffers();
        master.Tick();

        // Assert: ClusterOpCompletedEvent with aggregated manifest.
        bus.SwapBuffers();
        var completed = bus.ReadManaged<ClusterOpCompletedEvent>().ToList();
        var evt = Assert.Single(completed, e => e.RequestId == requestId);
        Assert.Equal(OrchestrationStatusCode.Success, evt.StatusCode);
        
        var aggregated = Assert.IsType<List<FileManifestEntry>>(evt.ResultPayload);
        Assert.Equal(2, aggregated.Count);
        Assert.Contains(aggregated, e => e.RelativeDest == "node1/file1.fdp");
        Assert.Contains(aggregated, e => e.RelativeDest == "node2/file2.fdp");
    }

    // ── Success Condition 2: One node with malformed JSON ─────────────────────

    /// <summary>
    /// SC2: When one node returns malformed JSON and another returns valid JSON,
    /// the aggregator must skip the malformed entry without throwing, and the
    /// ClusterOpCompletedEvent must carry the one valid entry.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void SerializeLocal_OneMalformedPayload_SkipsAndAggregatesValidEntry()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, NoMandatoryConfig());

        RegisterNode(bus, master, nodeId: 1, subsystem: "SimHost");
        RegisterNode(bus, master, nodeId: 2, subsystem: "SimHost");

        master.RegisterAggregator(new StorageConsensusAggregator());

        var requestId = Guid.NewGuid();
        master.FanOutSerializeLocal(requestId, new[] { 1, 2 });
        var txId = requestId;  // requestId is used as transactionId

        bus.SwapBuffers();
        master.Tick();

        // Node 1: valid manifest.
        var manifest1 = new List<FileManifestEntry>
        {
            new FileManifestEntry { SourceUnc = @"\\NODE01\file1.fdp", RelativeDest = "node1/file1.fdp" }
        };

        // Node 2: malformed JSON string (not a valid List<FileManifestEntry>).
        var malformedJson = "{\"garbage\":\"invalid\"}";

        bus.PublishManaged(new NodeOpCompletedEvent
        {
            TransactionId   = txId,
            Operation       = Fdp.Toolkit.Orchestration.NodeOpType.SerializeLocal,
            NodeId          = 1,
            StatusCode      = OrchestrationStatusCode.Success,
            ResultPayload   = manifest1,
            IsParticipating = true,
        });
        bus.PublishManaged(new NodeOpCompletedEvent
        {
            TransactionId   = txId,
            Operation       = Fdp.Toolkit.Orchestration.NodeOpType.SerializeLocal,
            NodeId          = 2,
            StatusCode      = OrchestrationStatusCode.Success,
            ResultPayload   = malformedJson,  // String payload, will be serialized, then fail to deserialize
            IsParticipating = true,
        });
        bus.SwapBuffers();
        master.Tick();

        // Assert: ClusterOpCompletedEvent published with only the valid entry.
        bus.SwapBuffers();
        var completed = bus.ReadManaged<ClusterOpCompletedEvent>().ToList();
        var evt = Assert.Single(completed, e => e.RequestId == requestId);
        Assert.Equal(OrchestrationStatusCode.Success, evt.StatusCode);
        
        var aggregated = Assert.IsType<List<FileManifestEntry>>(evt.ResultPayload);
        Assert.Single(aggregated);
        Assert.Equal("node1/file1.fdp", aggregated[0].RelativeDest);
    }

    // ── Success Condition 3: No aggregator registered ─────────────────────────

    /// <summary>
    /// SC3: When no StorageConsensusAggregator is registered,
    /// ClusterOpCompletedEvent is still published (backward-compatible),
    /// with ResultPayload null.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void SerializeLocal_NoAggregatorRegistered_StillPublishesEvent()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, NoMandatoryConfig());

        RegisterNode(bus, master, nodeId: 1, subsystem: "SimHost");

        // Do NOT register aggregator.

        var requestId = Guid.NewGuid();
        master.FanOutSerializeLocal(requestId, new[] { 1 });
        var txId = requestId;  // requestId is used as transactionId

        bus.SwapBuffers();
        master.Tick();

        var manifest1 = new List<FileManifestEntry>
        {
            new FileManifestEntry { SourceUnc = @"\\NODE01\file1.fdp", RelativeDest = "node1/file1.fdp" }
        };

        bus.PublishManaged(new NodeOpCompletedEvent
        {
            TransactionId   = txId,
            Operation       = Fdp.Toolkit.Orchestration.NodeOpType.SerializeLocal,
            NodeId          = 1,
            StatusCode      = OrchestrationStatusCode.Success,
            ResultPayload   = manifest1,
            IsParticipating = true,
        });
        bus.SwapBuffers();
        master.Tick();

        // Assert: ClusterOpCompletedEvent published with null ResultPayload.
        bus.SwapBuffers();
        var completed = bus.ReadManaged<ClusterOpCompletedEvent>().ToList();
        var evt = Assert.Single(completed, e => e.RequestId == requestId);
        Assert.Equal(OrchestrationStatusCode.Success, evt.StatusCode);
        Assert.Null(evt.ResultPayload);
    }

    // ── Success Condition 4: Aggregator registration ──────────────────────────

    /// <summary>
    /// SC4: RegisterAggregator stores the aggregator keyed by NodeOpType.SerializeLocal.
    /// A second call with the same operation type replaces the first without error.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void RegisterAggregator_StoresAndReplacesWithoutError()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, NoMandatoryConfig());

        var aggregator1 = new StorageConsensusAggregator();
        var aggregator2 = new StorageConsensusAggregator();

        // First registration.
        master.RegisterAggregator(aggregator1);

        // Second registration with same TargetOp — should replace without error.
        master.RegisterAggregator(aggregator2);

        // Verify by running a SerializeLocal and seeing that aggregation works.
        RegisterNode(bus, master, nodeId: 1, subsystem: "SimHost");

        var requestId = Guid.NewGuid();
        master.FanOutSerializeLocal(requestId, new[] { 1 });
        var txId = requestId;  // requestId is used as transactionId

        bus.SwapBuffers();
        master.Tick();

        var manifest1 = new List<FileManifestEntry>
        {
            new FileManifestEntry { SourceUnc = @"\\NODE01\file1.fdp", RelativeDest = "node1/file1.fdp" }
        };

        bus.PublishManaged(new NodeOpCompletedEvent
        {
            TransactionId   = txId,
            Operation       = Fdp.Toolkit.Orchestration.NodeOpType.SerializeLocal,
            NodeId          = 1,
            StatusCode      = OrchestrationStatusCode.Success,
            ResultPayload   = manifest1,
            IsParticipating = true,
        });
        bus.SwapBuffers();
        master.Tick();

        bus.SwapBuffers();
        var completed = bus.ReadManaged<ClusterOpCompletedEvent>().ToList();
        var evt = Assert.Single(completed, e => e.RequestId == requestId);
        Assert.Equal(OrchestrationStatusCode.Success, evt.StatusCode);
        
        // ResultPayload should be aggregated (not null).
        var aggregated = Assert.IsType<List<FileManifestEntry>>(evt.ResultPayload);
        Assert.Single(aggregated);
    }
}
