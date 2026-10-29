using System.Linq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Text.Json;
using Fdp.Core.Serialization;
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

    /// <summary>
    /// ⭐ CE-4090 (AQ108 D14) — the live load's "do not record" decision reaches the node with the payload: <c>RecordingOff</c> decodes from the
    /// wire, and a sender that omits it (an older peer, or a recording run) decodes to false — record, exactly as before.
    /// </summary>
    [Theory]
    [InlineData("{\"TargetState\":\"LoadingLive\",\"ScenarioId\":\"s\",\"RecordingOff\":true}", true)]
    [InlineData("{\"TargetState\":\"LoadingLive\",\"ScenarioId\":\"s\",\"RecordingOff\":false}", false)]
    [InlineData("{\"TargetState\":\"LoadingLive\",\"ScenarioId\":\"s\"}", false)]
    public void CE4090_DeserializeNodePayload_PrepareLive_CarriesRecordingOff(string json, bool expected)
    {
        var payload = NodeOpSlaveTranslator.DeserializeNodePayload(NedNodeOpType.PrepareLive, json);
        Assert.Equal(expected, Assert.IsType<EditLoadHandlerPayload>(payload).RecordingOff);
    }

    /// <summary>⭐ CE-4090 — the master's cluster-state broadcast tells EVERY node (and the orchestrator's own observers) whether the run is recorded:
    /// the one decoding (<c>StateOf</c>) keeps <c>RunNotRecorded</c>; an older sender's 0 is "recorded".</summary>
    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void CE4090_TheClusterStateBroadcast_CarriesWhetherTheRunIsRecorded(int onTheWire, bool expected)
    {
        var seen = ClusterFactsIngress.StateOf(new ClusterStateTopic { CurrentState = NedClusterState.OperatingLive, RunNotRecorded = onTheWire });
        Assert.Equal(expected, seen.RunNotRecorded);
    }

    /// <summary>⭐ CE-4090 — the broadcast carries the SETTING too (inverted: an older sender's 0 reads as ON) through the one decoding.</summary>
    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void CE4090_TheClusterStateBroadcast_CarriesTheRecordLiveRunsSetting(int onTheWire, bool expectedOff)
    {
        var seen = ClusterFactsIngress.StateOf(new ClusterStateTopic { CurrentState = NedClusterState.Idle, RecordLiveRunsOff = onTheWire });
        Assert.Equal(expectedOff, seen.RecordLiveRunsOff);
    }

    /// <summary>⭐ CE-3341 (R-3068) — the armed mode crosses the wire whole: the master's encoding (<c>StateTopicOf</c>) and every node's decoding (<c>StateOf</c>)
    /// agree for each mode, the values are the permanent ones (R-42).</summary>
    [Theory]
    [InlineData(ClusterArmedMode.Live, 0)]
    [InlineData(ClusterArmedMode.Preview, 1)]
    [InlineData(ClusterArmedMode.AAR, 2)]
    public void CE3341_TheClusterStateBroadcast_CarriesTheArmedModeBothWays(ClusterArmedMode mode, int permanentWireValue)
    {
        var topic = Hrot.Network.Orchestration.ClusterOpMasterTranslator.StateTopicOf(new ClusterStateTransitionedEvent { NewStateId = Fdp.Toolkit.Orchestration.ClusterState.OperatingEdit, ArmedMode = mode });
        Assert.Equal(permanentWireValue, topic.ArmedMode);
        Assert.Equal(mode, ClusterFactsIngress.StateOf(topic).ArmedMode);
    }

    /// <summary>⭐ CE-3341 — an older sender's broadcast (the field never set) and a value this build does not know both read as the default, Live.</summary>
    [Fact]
    public void CE3341_AnOlderSendersBroadcastAndAnUnknownValue_ReadAsLive()
    {
        Assert.Equal(ClusterArmedMode.Live, ClusterFactsIngress.StateOf(new ClusterStateTopic { CurrentState = NedClusterState.Idle }).ArmedMode);
        Assert.Equal(ClusterArmedMode.Live, ClusterFactsIngress.StateOf(new ClusterStateTopic { CurrentState = NedClusterState.Idle, ArmedMode = 99 }).ArmedMode);
    }

    /// <summary>⭐⭐ CE-3344 (R-3069) — what the cluster is still WAITING FOR crosses the wire whole: the master's encoding (<c>StateTopicOf</c>) and every node's decoding (<c>StateOf</c>) agree,
    /// and an older sender's omission (null / empty) reads as "nothing missing". 🔴 Mutation: <c>StateOf</c> not mapping <c>WaitingFor</c> ⇒ red.</summary>
    [Fact]
    public void CE3344_TheClusterStateBroadcast_CarriesWhatTheClusterIsWaitingFor_BothWays()
    {
        var topic = Hrot.Network.Orchestration.ClusterOpMasterTranslator.StateTopicOf(new ClusterStateTransitionedEvent
            { NewStateId = Fdp.Toolkit.Orchestration.ClusterState.Idle, WaitingFor = "SimHost, Brain (CGF)" });
        Assert.Equal("SimHost, Brain (CGF)", topic.WaitingFor);
        Assert.Equal("SimHost, Brain (CGF)", ClusterFactsIngress.StateOf(topic).WaitingFor);

        Assert.Equal(string.Empty, ClusterFactsIngress.StateOf(new ClusterStateTopic { CurrentState = NedClusterState.Idle }).WaitingFor);   // an older sender's sample
        Assert.Equal(string.Empty, Hrot.Network.Orchestration.ClusterOpMasterTranslator.StateTopicOf(new ClusterStateTransitionedEvent()).WaitingFor);
    }

    /// <summary>⭐ CE-3345 (R-3073) — the cluster ERROR crosses the cluster-state topic both ways through the ONE encoder / decoder pair; an older sender's omission reads as "no error".
    /// 🔴 Mutation: <c>StateTopicOf</c> or <c>StateOf</c> not mapping <c>ErrorReason</c> ⇒ red.</summary>
    [Fact]
    public void CE3345_TheClusterStateBroadcast_CarriesTheClusterError_BothWays()
    {
        var topic = Hrot.Network.Orchestration.ClusterOpMasterTranslator.StateTopicOf(new ClusterStateTransitionedEvent
            { NewStateId = Fdp.Toolkit.Orchestration.ClusterState.Degraded, ErrorReason = "Brain (CGF, node 3) lost at sim 00:12:31" });
        Assert.Equal("Brain (CGF, node 3) lost at sim 00:12:31", topic.ErrorReason);
        Assert.Equal("Brain (CGF, node 3) lost at sim 00:12:31", ClusterFactsIngress.StateOf(topic).ErrorReason);
        Assert.Equal(string.Empty, ClusterFactsIngress.StateOf(new ClusterStateTopic { CurrentState = NedClusterState.Idle }).ErrorReason);   // an older sender's sample
        Assert.Equal(string.Empty, Hrot.Network.Orchestration.ClusterOpMasterTranslator.StateTopicOf(new ClusterStateTransitionedEvent()).ErrorReason);
    }

    /// <summary>
    /// ⭐⭐ CE-3344 (R-3069, R-4006) — the master's IN-SYNC set (carried by the durable context topic) reaches a node's bus as <c>InSyncSetChangedEvent</c> through the ONE facts ingress, so the node's
    /// <c>ClusterView</c> knows whether THIS node is in sync; an unchanged or blank value publishes nothing (the echo of the master's own write on a shared bus ends after one round).
    /// 🔴 Mutation: the context reader not forwarded to the ingress (<c>contextReader</c> dropped) ⇒ no event ⇒ red.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void CE3344_TheMastersInSyncSet_ReachesTheNodesBus_AndAnUnchangedOneIsNotRepublished()
    {
        using var participant     = new DdsParticipant(TestDomain);
        using var cmdReader       = new DdsReader<NodeOpCommand>(participant);
        using var statusWriter    = new DdsWriter<NodeOpStatus>(participant);
        using var heartbeatWriter = new DdsWriter<NodeHeartbeat>(participant);
        using var contextReader   = new DdsReader<OrchestratorContextTopic>(participant);
        using var contextWriter   = new DdsWriter<OrchestratorContextTopic>(participant);

        var bus = new FdpEventBus();
        var translator = new NodeOpSlaveTranslator(cmdReader, statusWriter, heartbeatWriter, bus, nodeId: 5, contextReader: contextReader);
        Thread.Sleep(400);

        contextWriter.Write(new OrchestratorContextTopic { ScenarioId = "s", RequiredNodeIdsJson = "" });   // the scenario half only: no set yet
        Thread.Sleep(300);
        translator.Tick(); bus.SwapBuffers();
        Assert.Empty(bus.ReadManaged<InSyncSetChangedEvent>());

        contextWriter.Write(new OrchestratorContextTopic { ScenarioId = "s", RequiredNodeIdsJson = "[5,6]" });
        Thread.Sleep(300);
        translator.Tick(); bus.SwapBuffers();
        Assert.Equal(new[] { 5, 6 }, Assert.Single(bus.ReadManaged<InSyncSetChangedEvent>()).NodeIds);

        contextWriter.Write(new OrchestratorContextTopic { ScenarioId = "t", RequiredNodeIdsJson = "[5,6]" });   // the scenario moved, the set did not
        Thread.Sleep(300);
        translator.Tick(); bus.SwapBuffers();
        Assert.Empty(bus.ReadManaged<InSyncSetChangedEvent>());
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

    /// <summary>
    /// ⭐⭐ <c>CE-3345</c> success 7 (AQ110 A) — the node's phase, step and reason cross the heartbeat topic both ways through the ONE mapping (<c>NodeHeartbeatWire</c>: the slave's egress, the master's ingress and the observer's read it), and are written
    /// to the real DDS sample by the slave's translator; an older sender's omission reads as <c>Ready</c> with no step; a phase value this build does not know (a newer peer) reads as <c>Ready</c>.
    /// 🔴 Mutations (each compiles): <c>ToTopic</c> not writing <c>Phase</c> / <c>Step</c> / <c>Reason</c> ⇒ the DDS cells red; <c>ToEvent</c> not reading them ⇒ the round-trip cells red; the range check removed ⇒ the "unknown phase" cell reds.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void CE3345_TheHeartbeat_CarriesPhaseStepAndReason_BothWays_AndOverDds()
    {
        // the mapping, both halves
        var loading = new NodeHeartbeatEvent { NodeId = 7, LocalStateId = (int)NedClusterState.LoadingLive, SubsystemName = "CGF", Phase = NodePhase.Loading, Step = "Terrain" };
        var failed  = new NodeHeartbeatEvent { NodeId = 7, SubsystemName = "CGF", Phase = NodePhase.Failed, Reason = "cancelled by the user on node 7" };
        Assert.Equal((2, "Terrain", string.Empty), (NodeHeartbeatWire.ToTopic(loading).Phase, NodeHeartbeatWire.ToTopic(loading).Step, NodeHeartbeatWire.ToTopic(loading).Reason));
        var back = NodeHeartbeatWire.ToEvent(NodeHeartbeatWire.ToTopic(loading));
        Assert.Equal((NodePhase.Loading, "Terrain", (string?)null, 7, "CGF"), (back.Phase, back.Step, back.Reason, back.NodeId, back.SubsystemName));
        back = NodeHeartbeatWire.ToEvent(NodeHeartbeatWire.ToTopic(failed));
        Assert.Equal((NodePhase.Failed, (string?)null, "cancelled by the user on node 7"), (back.Phase, back.Step, back.Reason));
        Assert.Equal((NodePhase.Ready, (string?)null), (NodeHeartbeatWire.ToEvent(new NodeHeartbeat { NodeId = 1 }).Phase, NodeHeartbeatWire.ToEvent(new NodeHeartbeat { NodeId = 1 }).Step));   // an older sender
        Assert.Equal(NodePhase.Ready, NodeHeartbeatWire.ToEvent(new NodeHeartbeat { NodeId = 1, Phase = 99 }).Phase);                                                                                    // a newer peer's phase

        // the slave's egress writes them to the real DDS sample
        using var participant      = new DdsParticipant(TestDomain);
        using var cmdReader        = new DdsReader<NodeOpCommand>(participant);
        using var statusWriter     = new DdsWriter<NodeOpStatus>(participant);
        using var heartbeatWriter  = new DdsWriter<NodeHeartbeat>(participant);
        using var heartbeatReader  = new DdsReader<NodeHeartbeat>(participant);
        var bus        = new FdpEventBus();
        var translator = new NodeOpSlaveTranslator(cmdReader, statusWriter, heartbeatWriter, bus, 7);
        Thread.Sleep(400);
        bus.PublishManaged(loading);
        bus.SwapBuffers();
        translator.Tick();

        var hbs = new List<NodeHeartbeat>();
        var deadline = DateTime.UtcNow.AddSeconds(4);
        while (DateTime.UtcNow < deadline && hbs.Count == 0)
        {
            using var scope = heartbeatReader.Take();
            foreach (var s in scope) if (s.IsValid) hbs.Add(s.Data);
            if (hbs.Count == 0) Thread.Sleep(30);
        }
        var sample = Assert.Single(hbs);
        Assert.Equal((2, "Terrain", NedClusterState.LoadingLive), (sample.Phase, sample.Step, sample.LocalClusterState));
    }

    /// <summary>
    /// ⭐ <c>CE-3345</c> success 7 — <c>ClusterPlayReady</c> and <c>LoadProgress</c> cross the cluster-state topic both ways through the ONE encoder / decoder pair; an older sender's omission reads as "not play-ready, nothing loading".
    /// 🔴 Mutation: <c>StateTopicOf</c> or <c>StateOf</c> not mapping either field ⇒ red.
    /// </summary>
    [Fact]
    public void CE3345_TheClusterStateBroadcast_CarriesClusterPlayReadyAndLoadProgress_BothWays()
    {
        var topic = Hrot.Network.Orchestration.ClusterOpMasterTranslator.StateTopicOf(new ClusterStateTransitionedEvent
            { NewStateId = Fdp.Toolkit.Orchestration.ClusterState.OperatingLive, ClusterPlayReady = true, LoadProgress = "CGF (node 2): Terrain" });
        Assert.True(topic.ClusterPlayReady);
        Assert.Equal("CGF (node 2): Terrain", topic.LoadProgress);
        var seen = ClusterFactsIngress.StateOf(topic);
        Assert.True(seen.ClusterPlayReady);
        Assert.Equal("CGF (node 2): Terrain", seen.LoadProgress);

        var older = ClusterFactsIngress.StateOf(new ClusterStateTopic { CurrentState = NedClusterState.Idle });   // an older sender's sample
        Assert.False(older.ClusterPlayReady);
        Assert.Equal(string.Empty, older.LoadProgress);
        Assert.Equal(string.Empty, Hrot.Network.Orchestration.ClusterOpMasterTranslator.StateTopicOf(new ClusterStateTransitionedEvent()).LoadProgress);

        // and a failed load's REASON reaches every node's op status as the plain text (the master writes the string as it is; the node's ingress keeps text that is not JSON)
        var id = Guid.NewGuid();
        var opStatus = ClusterFactsIngress.OpCompletedOf(new ClusterOpStatus { RequestId = id, StatusCode = (int)OrchestrationStatusCode.Failure, ResultJson = "cancelled by the user on node 2" });
        Assert.Equal((id, OrchestrationStatusCode.Failure, "cancelled by the user on node 2"), (opStatus.RequestId, opStatus.StatusCode, (string?)opStatus.ResultPayload));
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

    // ── ⭐⭐⭐ CE-1436 (R-3066, design V1/V2) — ONE ingress of the cluster's facts; monitoring stays with the observer ───────────────────

    private static void DrainBus(FdpEventBus bus) => bus.SwapBuffers();

    /// <summary>
    /// ⭐⭐ CE-1436 condition 1 — the same <c>ClusterStateTopic</c> and <c>ClusterOpStatus</c> samples through a NODE's slave translator and through ExCon's
    /// observer yield IDENTICAL bus events: both own the one <see cref="ClusterFactsIngress"/>, so there is one decoder per fact (state, an unsaved-check
    /// answer, a replay result, a refusal's text). 🔴 Mutation: the slave's op-status reader left out of its ingress (<c>opStatusReader</c> not forwarded) ⇒
    /// the slave bus holds no <c>ClusterOpCompletedEvent</c> ⇒ the equality assertion fails.
    /// </summary>
    [Fact(Timeout = 20_000)]
    public void CE1436_TheSlaveAndTheObserver_FeedTheSameFactsThroughOneIngress()
    {
        using var participant     = new DdsParticipant(TestDomain);
        using var cmdReader       = new DdsReader<NodeOpCommand>(participant);
        using var statusWriter    = new DdsWriter<NodeOpStatus>(participant);
        using var heartbeatWriter = new DdsWriter<NodeHeartbeat>(participant);
        using var stateReader     = new DdsReader<ClusterStateTopic>(participant);
        using var opReader        = new DdsReader<ClusterOpStatus>(participant);
        using var stateWriter     = new DdsWriter<ClusterStateTopic>(participant);
        using var opWriter        = new DdsWriter<ClusterOpStatus>(participant);

        var nodeBus = new FdpEventBus();
        var exBus   = new FdpEventBus();
        var slave   = new NodeOpSlaveTranslator(cmdReader, statusWriter, heartbeatWriter, nodeBus, nodeId: 5,
            clusterStateReader: stateReader, opStatusReader: opReader);
        using var observer = new OrchestrationObserverTranslator(participant, exBus);
        Thread.Sleep(500);

        var exercise = Guid.NewGuid();
        stateWriter.Write(new ClusterStateTopic { CurrentState = NedClusterState.OperatingEdit, ExerciseId = exercise, SessionId = Guid.NewGuid(), RunNotRecorded = 1 });
        var check = Guid.NewGuid(); var replay = Guid.NewGuid(); var refused = Guid.NewGuid();
        opWriter.Write(new ClusterOpStatus { RequestId = check, StatusCode = (int)OrchestrationStatusCode.Success,
            ResultJson = JsonSerializer.Serialize(new UnsavedCheckResult(true, new[] { 4, 9 }), FdpJsonOptionsRegistry.DefaultRelaxed) });
        opWriter.Write(new ClusterOpStatus { RequestId = replay, StatusCode = (int)OrchestrationStatusCode.Success,
            ResultJson = JsonSerializer.Serialize(new ReplayPrepareResult(), FdpJsonOptionsRegistry.DefaultRelaxed) });
        opWriter.Write(new ClusterOpStatus { RequestId = refused, StatusCode = (int)OrchestrationStatusCode.Failure, ResultJson = UnsavedCheckOutcome.NotEditingReason });
        Thread.Sleep(500);

        slave.Tick(); observer.Tick();
        DrainBus(nodeBus); DrainBus(exBus);

        string Show(FdpEventBus bus) => string.Join("|",
            bus.ReadManaged<ClusterStateUpdateEvent>().Select(e => $"S:{e.CurrentState}/{e.ExerciseId}/{e.SessionId != Guid.Empty}/{e.RunNotRecorded}/{e.MasterGone}")
            .Concat(bus.ReadManaged<ClusterOpCompletedEvent>().OrderBy(e => e.RequestId)
                .Select(e => $"O:{e.RequestId}/{e.StatusCode}/{e.ResultPayload?.GetType().Name}/{(e.ResultPayload is UnsavedCheckResult r ? $"{r.Changed}:{string.Join(',', r.ChangedNodes)}" : e.ResultPayload)}")));

        var onNode = Show(nodeBus);
        Assert.Contains($"O:{check}/Success/UnsavedCheckResult/True:4,9", onNode);                 // the check round's answer decoded to its typed result …
        Assert.Contains($"O:{replay}/Success/ReplayPrepareResult/", onNode);                       // … a replay result NOT swallowed by it …
        Assert.Contains($"O:{refused}/Failure/String/{UnsavedCheckOutcome.NotEditingReason}", onNode);   // … and a refusal's text kept as text
        Assert.Equal(onNode, Show(exBus));                                                         // ⭐ identical on both paths — one decoder
    }

    /// <summary>
    /// ⭐⭐ CE-1436 condition 2 (V2's measured hazard, pinned) — a node's slave translator publishes NO monitoring events: another node's heartbeat,
    /// capabilities, node-op status and node-op COMMAND, all on the wire, never appear on its bus — they would drive its own egress / execution (its
    /// heartbeat egress re-sending another node's heartbeat as its own; an <c>ExecuteNodeOpIntent</c> run by the wrong node). The observer, on the
    /// same wire, DOES publish them (the positive control: the samples really crossed). 🔴 Mutations: the slave's command filter
    /// (<c>cmd.TargetNodeId != _nodeId</c>) removed ⇒ an <c>ExecuteNodeOpIntent</c> for node 6 appears; a monitoring publish added to the ingress ⇒ the
    /// heartbeat assertion fails.
    /// </summary>
    [Fact(Timeout = 20_000)]
    public void CE1436_TheSlaveBus_CarriesNoMonitoringEvents_WhileTheObserverDoes()
    {
        using var participant     = new DdsParticipant(TestDomain);
        using var cmdReader       = new DdsReader<NodeOpCommand>(participant);
        using var statusWriter    = new DdsWriter<NodeOpStatus>(participant);
        using var heartbeatWriter = new DdsWriter<NodeHeartbeat>(participant);
        using var stateReader     = new DdsReader<ClusterStateTopic>(participant);
        using var opReader        = new DdsReader<ClusterOpStatus>(participant);
        using var otherHeartbeat  = new DdsWriter<NodeHeartbeat>(participant);
        using var otherCaps       = new DdsWriter<NodeCapabilitiesTopic>(participant);
        using var otherStatus     = new DdsWriter<NodeOpStatus>(participant);
        using var otherCommand    = new DdsWriter<NodeOpCommand>(participant);

        var nodeBus = new FdpEventBus();
        var exBus   = new FdpEventBus();
        var slave   = new NodeOpSlaveTranslator(cmdReader, statusWriter, heartbeatWriter, nodeBus, nodeId: 5,
            clusterStateReader: stateReader, opStatusReader: opReader);
        using var observer = new OrchestrationObserverTranslator(participant, exBus);
        Thread.Sleep(500);

        otherHeartbeat.Write(new NodeHeartbeat { NodeId = 6, SubsystemName = "Other", WallTicksUtc = 1L });
        otherCaps.Write(new NodeCapabilitiesTopic { NodeId = 6, CapabilitiesJson = "[\"fdp.role.brain\"]" });
        otherStatus.Write(new NodeOpStatus { TransactionId = Guid.NewGuid(), Operation = NedNodeOpType.PrepareLive, NodeId = 6, StatusCode = 0 });
        otherCommand.Write(new NodeOpCommand { TargetNodeId = 6, TransactionId = Guid.NewGuid(), Operation = NedNodeOpType.PrepareLive, PayloadJson = "{}" });
        Thread.Sleep(500);

        slave.Tick(); observer.Tick();
        DrainBus(nodeBus); DrainBus(exBus);

        // positive control: the samples reached the observer's bus
        Assert.Contains(exBus.ReadManaged<NodeHeartbeatEvent>().ToArray(), e => e.NodeId == 6);
        Assert.Contains(exBus.ReadManaged<NodeCapabilitiesEvent>().ToArray(), e => e.NodeId == 6);
        Assert.Contains(exBus.ReadManaged<NodeOpCompletedEvent>().ToArray(), e => e.NodeId == 6);
        Assert.Contains(exBus.ReadManaged<ExecuteNodeOpIntent>().ToArray(), e => e.TargetNodeId == 6);

        // … and none of them reached the node's
        Assert.Empty(nodeBus.ReadManaged<NodeHeartbeatEvent>());
        Assert.Empty(nodeBus.ReadManaged<NodeCapabilitiesEvent>());
        Assert.Empty(nodeBus.ReadManaged<NodeOpCompletedEvent>());
        Assert.Empty(nodeBus.ReadManaged<ExecuteNodeOpIntent>());
    }
}
