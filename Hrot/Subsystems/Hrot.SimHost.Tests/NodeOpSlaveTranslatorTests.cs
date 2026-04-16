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
}
