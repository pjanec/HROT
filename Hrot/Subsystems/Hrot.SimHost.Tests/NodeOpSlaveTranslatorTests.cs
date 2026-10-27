using System.Linq;
using System;
using System.Collections.Generic;
using System.Threading;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Hrot.Common.Orchestration;
using Hrot.NED.Descriptors.Orchestration;
using NedNodeOpType  = Hrot.NED.Descriptors.Orchestration.NodeOpType;
using NedClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;
using FdpNodeOpType  = Fdp.Toolkit.Orchestration.NodeOpType;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// Unit tests for CMC-S012: <see cref="NodeOpSlaveTranslator"/>.
/// </summary>
[Collection("SimHostDds")]
public sealed class NodeOpSlaveTranslatorTests
{
    private const int TestDomain = 18;

    // ── Test 1: DDS NodeOpCommand (matching nodeId) → ExecuteNodeOpIntent ────

    [Fact(Timeout = 10_000)]
    public void NodeOpCommand_MatchingNodeId_PublishesExecuteNodeOpIntent()
    {
        using var participant      = new DdsParticipant(TestDomain);
        using var cmdWriter        = new DdsWriter<NodeOpCommand>(participant);
        using var cmdReader        = new DdsReader<NodeOpCommand>(participant);
        using var statusWriter     = new DdsWriter<NodeOpStatus>(participant);
        using var statusReader     = new DdsReader<NodeOpStatus>(participant);
        using var heartbeatWriter  = new DdsWriter<NodeHeartbeat>(participant);

        var bus        = new FdpEventBus();
        const int nodeId = 5;
        var translator = new NodeOpSlaveTranslator(cmdReader, statusWriter, heartbeatWriter, bus, nodeId);

        Thread.Sleep(400); // DDS discovery

        cmdWriter.Write(new NodeOpCommand
        {
            TargetNodeId  = nodeId,
            TransactionId = Guid.NewGuid(),
            Operation     = NedNodeOpType.PrepareLive,
            PayloadJson   = "{\"TargetState\":\"LoadingLive\",\"ScenarioId\":\"test-s\"}",
        });

        Thread.Sleep(300);
        translator.Tick();
        bus.SwapBuffers();

        var intents = bus.ReadManaged<ExecuteNodeOpIntent>();
        Assert.Single(intents);
        Assert.Equal(FdpNodeOpType.PrepareLive, intents[0].Operation);
        Assert.IsType<EditLoadHandlerPayload>(intents[0].DomainPayload);
    }

    // ── C2 (Q91 §4-I I5, R-4006): the node reads the cluster SESSION, and learns when the master is gone ───────────

    /// <summary>
    /// ⭐⭐ C2 — a node's translator turns the master's cluster-state sample into a bus event carrying the SESSION, and the
    /// master's writer going away into a <c>MasterGone</c> event. ⛔ Before: no node but ExCon read this topic at all.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void ClusterState_ReachesTheNodeWithItsSession_AndTheWriterGoingAwayIsMasterGone()
    {
        using var participant     = new DdsParticipant(TestDomain);
        using var cmdReader       = new DdsReader<NodeOpCommand>(participant);
        using var statusWriter    = new DdsWriter<NodeOpStatus>(participant);
        using var heartbeatWriter = new DdsWriter<NodeHeartbeat>(participant);
        using var stateReader     = new DdsReader<ClusterStateTopic>(participant);
        var stateWriter           = new DdsWriter<ClusterStateTopic>(participant);

        var bus        = new FdpEventBus();
        var translator = new NodeOpSlaveTranslator(cmdReader, statusWriter, heartbeatWriter, bus, nodeId: 5,
            clusterStateReader: stateReader);
        Thread.Sleep(400);

        var session = Guid.NewGuid();
        stateWriter.Write(new ClusterStateTopic { CurrentState = NedClusterState.Idle, SessionId = session });
        Thread.Sleep(300);
        translator.Tick();
        bus.SwapBuffers();
        var seen = Assert.Single(bus.ReadManaged<ClusterStateUpdateEvent>());
        Assert.Equal(session, seen.SessionId);
        Assert.False(seen.MasterGone);

        stateWriter.Dispose();   // ⭐ the master process ends
        bool gone = false;
        for (int i = 0; i < 20 && !gone; i++)
        {
            Thread.Sleep(100);
            translator.Tick();
            bus.SwapBuffers();
            gone = bus.ReadManaged<ClusterStateUpdateEvent>().Any(e => e.MasterGone);
        }
        Assert.True(gone, "the master's writer going away must reach the node as MasterGone");
    }

    // ── Test 2: DDS NodeOpCommand (different nodeId) → no intent ─────────────

    [Fact(Timeout = 10_000)]
    public void NodeOpCommand_DifferentNodeId_DoesNotPublishIntent()
    {
        using var participant      = new DdsParticipant(TestDomain);
        using var cmdWriter        = new DdsWriter<NodeOpCommand>(participant);
        using var cmdReader        = new DdsReader<NodeOpCommand>(participant);
        using var statusWriter     = new DdsWriter<NodeOpStatus>(participant);
        using var heartbeatWriter  = new DdsWriter<NodeHeartbeat>(participant);

        var bus        = new FdpEventBus();
        const int nodeId = 5;
        var translator = new NodeOpSlaveTranslator(cmdReader, statusWriter, heartbeatWriter, bus, nodeId);

        Thread.Sleep(400);

        cmdWriter.Write(new NodeOpCommand
        {
            TargetNodeId  = nodeId + 1,   // different node
            TransactionId = Guid.NewGuid(),
            Operation     = NedNodeOpType.PrepareLive,
            PayloadJson   = "{}",
        });

        Thread.Sleep(300);
        translator.Tick();
        bus.SwapBuffers();

        var intents = bus.ReadManaged<ExecuteNodeOpIntent>();
        Assert.Empty(intents);
    }

    // ── Test 3: NodeOpCompletedEvent → NodeOpStatus written to DDS ───────────

    [Fact(Timeout = 10_000)]
    public void NodeOpCompletedEvent_NullResultPayload_WritesEmptyResultJson()
    {
        using var participant      = new DdsParticipant(TestDomain);
        using var cmdReader        = new DdsReader<NodeOpCommand>(participant);
        using var statusWriter     = new DdsWriter<NodeOpStatus>(participant);
        using var statusReader     = new DdsReader<NodeOpStatus>(participant);
        using var heartbeatWriter  = new DdsWriter<NodeHeartbeat>(participant);

        var bus        = new FdpEventBus();
        const int nodeId = 5;
        var translator = new NodeOpSlaveTranslator(cmdReader, statusWriter, heartbeatWriter, bus, nodeId);

        Thread.Sleep(400);

        var txId = Guid.NewGuid();
        bus.PublishManaged(new NodeOpCompletedEvent
        {
            TransactionId   = txId,
            NodeId          = nodeId,
            StatusCode      = 0,
            IsParticipating = true,
            ResultPayload   = null,
        });
        bus.SwapBuffers();

        translator.Tick();

        var statuses = new List<NodeOpStatus>();
        var deadline = DateTime.UtcNow.AddSeconds(4);
        while (DateTime.UtcNow < deadline)
        {
            using var scope = statusReader.Take();
            foreach (var s in scope)
            {
                if (!s.IsValid) continue;
                statuses.Add(s.Data);
            }
            if (statuses.Count > 0) break;
            Thread.Sleep(30);
        }

        Assert.Single(statuses);
        Assert.Equal(txId, statuses[0].TransactionId);
        Assert.Equal(string.Empty, statuses[0].ResultJson);
    }

    // ── CE-279 Layer B: SerializeLocal wire carries scenario-save AND archive payloads ──────────────────

    /// <summary>
    /// ⭐⭐⭐ REGRESSION: <c>SerializeLocal</c> is shared by the scenario-JSON save and the <c>.fdp</c> archive.
    /// The slave translator used to rebuild EVERY <c>SerializeLocal</c> payload as an
    /// <see cref="ArchiveHandlerPayload"/>, so on a remote node the distributed scenario save's payload — and its
    /// name — was silently dropped and no slice was ever written (measured live 2026-09-14/15). The wire now
    /// discriminates: <c>ScenarioId</c> present ⇒ <see cref="ScenarioSaveHandlerPayload"/> (carrying the name);
    /// otherwise ⇒ <see cref="ArchiveHandlerPayload"/> (carrying the exercise id).
    /// </summary>
    [Fact]
    public void DeserializeNodePayload_SerializeLocal_ScenarioId_RebuildsScenarioSavePayload()
    {
        var payload = NodeOpSlaveTranslator.DeserializeNodePayload(
            NedNodeOpType.SerializeLocal, "{\"ScenarioId\":\"my-scn\"}");

        var scn = Assert.IsType<ScenarioSaveHandlerPayload>(payload);
        Assert.Equal("my-scn", scn.ScenarioName);
    }

    [Fact]
    public void DeserializeNodePayload_SerializeLocal_ExerciseId_RebuildsArchivePayload()
    {
        var id = Guid.NewGuid();
        var payload = NodeOpSlaveTranslator.DeserializeNodePayload(
            NedNodeOpType.SerializeLocal, $"{{\"ExerciseId\":\"{id}\"}}");

        var arch = Assert.IsType<ArchiveHandlerPayload>(payload);
        Assert.Equal(id, arch.ExerciseId);
    }

    // ── Test 4: NodeHeartbeatEvent → NodeHeartbeat written to DDS ────────────

    [Fact(Timeout = 10_000)]
    public void NodeHeartbeatEvent_WritesCorrectNodeHeartbeat()
    {
        using var participant      = new DdsParticipant(TestDomain);
        using var cmdReader        = new DdsReader<NodeOpCommand>(participant);
        using var statusWriter     = new DdsWriter<NodeOpStatus>(participant);
        using var heartbeatWriter  = new DdsWriter<NodeHeartbeat>(participant);
        using var heartbeatReader  = new DdsReader<NodeHeartbeat>(participant);

        var bus        = new FdpEventBus();
        const int nodeId = 5;
        var translator = new NodeOpSlaveTranslator(cmdReader, statusWriter, heartbeatWriter, bus, nodeId);

        Thread.Sleep(400);

        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId        = nodeId,
            LocalStateId  = (int)NedClusterState.LoadingLive,
            WallTicksUtc  = 12345L,
            SubsystemName = "SimHost",
        });
        bus.SwapBuffers();

        translator.Tick();

        var hbs = new List<NodeHeartbeat>();
        var deadline = DateTime.UtcNow.AddSeconds(4);
        while (DateTime.UtcNow < deadline)
        {
            using var scope = heartbeatReader.Take();
            foreach (var s in scope)
            {
                if (!s.IsValid) continue;
                hbs.Add(s.Data);
            }
            if (hbs.Count > 0) break;
            Thread.Sleep(30);
        }

        Assert.Single(hbs);
        Assert.Equal(nodeId, hbs[0].NodeId);
        Assert.Equal(NedClusterState.LoadingLive, hbs[0].LocalClusterState);
    }

    // ── ⭐ AQ107 I: the CPU load is a 0…1 FRACTION on the wire, a percent in the cluster cache ───────────────

    /// <summary>
    /// ⭐ <c>AQ107 I</c> — <c>NodeHeartbeat.CpuUsage</c> is a 0…1 fraction (SI: a share, not a percent). A heartbeat carrying 0.25 over
    /// real DDS, read by the shared membership ingest (<c>ClusterCapabilityIngestSystem</c>), lands in the cluster cache as 25 percent —
    /// <c>NodeCapability.CpuUsagePercent</c>, the unit the least-loaded pick compares, is unchanged. ✅ Red-proof: the ingest storing the
    /// fraction unconverted ⇒ 0.25 ⇒ red.
    /// </summary>
    [Fact(Timeout = 15_000)]
    public void AQ107I_ACpuLoadOnTheWire_IsAFraction_AndThePercentInTheClusterCache()
    {
        using var participant      = new DdsParticipant(TestDomain);
        using var heartbeatWriter  = new DdsWriter<NodeHeartbeat>(participant);
        using var heartbeatReader  = new DdsReader<NodeHeartbeat>(participant);
        using var capsReader       = new DdsReader<NodeCapabilitiesTopic>(participant);

        var cache  = new Hrot.Network.Routing.SimpleClusterStateCache();
        var ingest = new Hrot.Network.Routing.ClusterCapabilityIngestSystem(heartbeatReader, capsReader, cache);
        using var view = new EntityRepository();
        Thread.Sleep(400);

        const int nodeId = 77;
        heartbeatWriter.Write(new NodeHeartbeat { NodeId = nodeId, SubsystemName = "SimHost", WallTicksUtc = 12345L, CpuUsage = 0.25f, RamUsedBytes = 1L << 20 });

        Hrot.Network.Routing.NodeCapability? got = null;
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline && got is null)
        {
            ingest.Execute(view, 0f);
            got = cache.TryGet(nodeId);
            if (got is null) Thread.Sleep(30);
        }

        Assert.NotNull(got);
        Assert.Equal(25f, got!.CpuUsagePercent, 1e-3f);
        Assert.Equal(1L << 20, got.RamUsedBytes);
    }
}
