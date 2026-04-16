using System;
using System.Collections.Generic;
using System.Threading;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Hrot.Common.Orchestration;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Orchestrator;
using Hrot.Network.Orchestration;
using NedNodeOpType  = Hrot.NED.Descriptors.Orchestration.NodeOpType;
using NedClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;
using FdpNodeOpType  = Fdp.Toolkit.Orchestration.NodeOpType;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// Unit tests for CMC-S013: <see cref="NodeOpMasterTranslator"/>.
/// </summary>
[Collection("OrchestratorTests")]
public sealed class NodeOpMasterTranslatorTests
{
    private const int TestDomain = 16;

    // ── Test 1: Egress — PrepareLive with EditLoadHandlerPayload ─────────────

    [Fact(Timeout = 10_000)]
    public void ExecuteNodeOpIntent_PrepareLive_WritesNodeOpCommand()
    {
        using var participant     = new DdsParticipant(TestDomain);
        using var nodeOpCmdReader = new DdsReader<NodeOpCommand>(participant);
        using var statusReader    = new DdsReader<NodeOpStatus>(participant);
        using var statusWriter    = new DdsWriter<NodeOpStatus>(participant);
        using var nodeWriter2     = new DdsWriter<NodeOpCommand>(participant);

        var bus = new FdpEventBus();
        var writerMap = new Dictionary<int, DdsWriter<NodeOpCommand>> { [2] = nodeWriter2 };
        var translator = new NodeOpMasterTranslator(writerMap, statusReader, bus);

        // Publish intent to bus (write buffer)
        bus.PublishManaged(new ExecuteNodeOpIntent
        {
            TargetNodeId  = 2,
            TransactionId = Guid.NewGuid(),
            Operation     = FdpNodeOpType.PrepareLive,
            DomainPayload = new EditLoadHandlerPayload("scene1", false, (int)NedClusterState.LoadingLive),
        });
        // Swap so translator can read
        bus.SwapBuffers();

        Thread.Sleep(300); // DDS discovery
        translator.Tick();

        // Verify DDS write
        var received = new List<NodeOpCommand>();
        var deadline = DateTime.UtcNow.AddSeconds(4);
        while (DateTime.UtcNow < deadline)
        {
            using var scope = nodeOpCmdReader.Take();
            foreach (var s in scope)
            {
                if (!s.IsValid) continue;
                received.Add(s.Data);
            }
            if (received.Count > 0) break;
            Thread.Sleep(30);
        }

        Assert.Single(received);
        Assert.Equal(NedNodeOpType.PrepareLive, received[0].Operation);
        Assert.Equal(2, received[0].TargetNodeId);
        Assert.Contains("TargetState", received[0].PayloadJson);
    }

    // ── Test 2: Egress — null DomainPayload → empty PayloadJson ──────────────

    [Fact(Timeout = 10_000)]
    public void ExecuteNodeOpIntent_NullPayload_WritesEmptyPayloadJson()
    {
        using var participant     = new DdsParticipant(TestDomain);
        using var nodeOpCmdReader = new DdsReader<NodeOpCommand>(participant);
        using var statusReader    = new DdsReader<NodeOpStatus>(participant);
        using var nodeWriter1     = new DdsWriter<NodeOpCommand>(participant);

        var bus = new FdpEventBus();
        var writerMap = new Dictionary<int, DdsWriter<NodeOpCommand>> { [1] = nodeWriter1 };
        var translator = new NodeOpMasterTranslator(writerMap, statusReader, bus);

        bus.PublishManaged(new ExecuteNodeOpIntent
        {
            TargetNodeId  = 1,
            TransactionId = Guid.NewGuid(),
            Operation     = FdpNodeOpType.CommitState,
            DomainPayload = null,
        });
        bus.SwapBuffers();

        Thread.Sleep(300);
        translator.Tick();

        var received = new List<NodeOpCommand>();
        var deadline = DateTime.UtcNow.AddSeconds(4);
        while (DateTime.UtcNow < deadline)
        {
            using var scope = nodeOpCmdReader.Take();
            foreach (var s in scope)
            {
                if (!s.IsValid) continue;
                received.Add(s.Data);
            }
            if (received.Count > 0) break;
            Thread.Sleep(30);
        }

        Assert.Single(received);
        Assert.Equal(string.Empty, received[0].PayloadJson);
    }

    // ── Test 3: Ingress — NodeOpStatus (empty ResultJson) → null ResultPayload

    [Fact(Timeout = 10_000)]
    public void NodeOpStatus_EmptyResultJson_PublishesNullResultPayload()
    {
        using var participant  = new DdsParticipant(TestDomain);
        using var statusWriter = new DdsWriter<NodeOpStatus>(participant);
        using var statusReader = new DdsReader<NodeOpStatus>(participant);
        using var nodeWriter   = new DdsWriter<NodeOpCommand>(participant);

        var bus = new FdpEventBus();
        var translator = new NodeOpMasterTranslator(_ => nodeWriter, statusReader, bus);

        statusWriter.Write(new NodeOpStatus
        {
            TransactionId   = Guid.NewGuid(),
            Operation       = NedNodeOpType.PrepareState,
            NodeId          = 3,
            StatusCode      = 0,
            IsParticipating = true,
            ResultJson      = string.Empty,
        });

        Thread.Sleep(300);
        translator.Tick();
        bus.SwapBuffers();

        var events = bus.ReadManaged<NodeOpCompletedEvent>();
        Assert.Single(events);
        Assert.Equal(3, events[0].NodeId);
        Assert.Null(events[0].ResultPayload);
    }

    // ── Test 4: CommitStatePayload round-trip (TASK-D01) ─────────────────────

    /// <summary>
    /// CommitStatePayload serialized by NodeOpMasterTranslator produces a raw integer
    /// string, which NodeOpSlaveTranslator correctly deserializes back to CommitStatePayload.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void CommitStatePayload_RoundTrips_ThroughTranslators()
    {
        using var participant      = new DdsParticipant(TestDomain);
        using var nodeOpCmdWriter  = new DdsWriter<NodeOpCommand>(participant);
        using var nodeOpCmdReader  = new DdsReader<NodeOpCommand>(participant);
        using var statusWriter     = new DdsWriter<NodeOpStatus>(participant);
        using var statusReader     = new DdsReader<NodeOpStatus>(participant);
        using var hbWriter         = new DdsWriter<NodeHeartbeat>(participant);

        // ── Master side ───────────────────────────────────────────────────────
        var masterBus  = new FdpEventBus();
        var nodeId     = 7;
        var masterTranslator = new NodeOpMasterTranslator(
            _ => nodeOpCmdWriter, statusReader, masterBus);

        // ── Slave side ────────────────────────────────────────────────────────
        var slaveBus = new FdpEventBus();
        var slaveTranslator = new NodeOpSlaveTranslator(
            nodeOpCmdReader, statusWriter, hbWriter, slaveBus, nodeId);

        Thread.Sleep(300); // DDS discovery

        var txId = Guid.NewGuid();
        masterBus.PublishManaged(new ExecuteNodeOpIntent
        {
            TargetNodeId  = nodeId,
            TransactionId = txId,
            Operation     = FdpNodeOpType.CommitState,
            DomainPayload = new CommitStatePayload(3),
        });
        masterBus.SwapBuffers();
        masterTranslator.Tick(); // serializes to DDS

        Thread.Sleep(300); // wait for DDS loopback

        slaveTranslator.Tick(); // deserializes from DDS, publishes ExecuteNodeOpIntent
        slaveBus.SwapBuffers();

        var intents = slaveBus.ReadManaged<ExecuteNodeOpIntent>();
        Assert.Single(intents);
        Assert.Equal(txId,   intents[0].TransactionId);
        var payload = Assert.IsType<CommitStatePayload>(intents[0].DomainPayload);
        Assert.Equal(3, payload.TargetStateId);
    }

    // ── Test 5: Ingress — SerializeLocal ResultJson → List<FileManifestEntry> ──

    /// <summary>
    /// When a <see cref="NodeOpStatus"/> with Operation=SerializeLocal arrives with valid
    /// ResultJson, <see cref="NodeOpMasterTranslator"/> must deserialise the JSON into a
    /// <c>List&lt;FileManifestEntry&gt;</c> and set it as the <see cref="NodeOpCompletedEvent.ResultPayload"/>.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void NodeOpStatus_SerializeLocal_ReturnsFileManifestEntries()
    {
        using var participant  = new DdsParticipant(TestDomain);
        using var statusWriter = new DdsWriter<NodeOpStatus>(participant);
        using var statusReader = new DdsReader<NodeOpStatus>(participant);
        using var nodeWriter   = new DdsWriter<NodeOpCommand>(participant);

        var bus = new FdpEventBus();
        var translator = new NodeOpMasterTranslator(_ => nodeWriter, statusReader, bus);

        const string json =
            "[{\"SourceUnc\":\"\\\\\\\\NODE01\\\\c$\\\\file.fdp\",\"RelativeDest\":\"exercises/file.fdp\"}]";

        statusWriter.Write(new NodeOpStatus
        {
            TransactionId   = Guid.NewGuid(),
            Operation       = NedNodeOpType.SerializeLocal,
            NodeId          = 5,
            StatusCode      = 0,
            IsParticipating = true,
            ResultJson      = json,
        });

        Thread.Sleep(300);
        translator.Tick();
        bus.SwapBuffers();

        var events = bus.ReadManaged<NodeOpCompletedEvent>();
        Assert.Single(events);
        Assert.Equal(FdpNodeOpType.SerializeLocal, events[0].Operation);
        var entries = Assert.IsType<List<FileManifestEntry>>(events[0].ResultPayload);
        Assert.Single(entries);
        Assert.Equal("\\\\NODE01\\c$\\file.fdp", entries[0].SourceUnc);
        Assert.Equal("exercises/file.fdp", entries[0].RelativeDest);
    }

    // ── Test 6: Ingress — non-SerializeLocal with ResultJson → null ResultPayload

    [Fact(Timeout = 10_000)]
    public void NodeOpStatus_NonSerializeLocal_ReturnsNullResultPayload()
    {
        using var participant  = new DdsParticipant(TestDomain);
        using var statusWriter = new DdsWriter<NodeOpStatus>(participant);
        using var statusReader = new DdsReader<NodeOpStatus>(participant);
        using var nodeWriter   = new DdsWriter<NodeOpCommand>(participant);

        var bus = new FdpEventBus();
        var translator = new NodeOpMasterTranslator(_ => nodeWriter, statusReader, bus);

        statusWriter.Write(new NodeOpStatus
        {
            TransactionId   = Guid.NewGuid(),
            Operation       = NedNodeOpType.CommitState,
            NodeId          = 2,
            StatusCode      = 0,
            IsParticipating = true,
            ResultJson      = "[{\"SourceUnc\":\"path\",\"RelativeDest\":\"dest\"}]",
        });

        Thread.Sleep(300);
        translator.Tick();
        bus.SwapBuffers();

        var events = bus.ReadManaged<NodeOpCompletedEvent>();
        Assert.Single(events);
        Assert.Equal(FdpNodeOpType.CommitState, events[0].Operation);
        Assert.Null(events[0].ResultPayload);
    }
}

