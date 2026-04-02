using System;
using System.Collections.Generic;
using System.Threading;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Toolkit.Orchestration;
using FDP.Toolkit.Orchestration.Handlers;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Orchestrator.Translators;
using NedNodeOpType  = Hrot.NED.Descriptors.Orchestration.NodeOpType;
using NedClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;
using FdpNodeOpType  = FDP.Toolkit.Orchestration.NodeOpType;
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
            NodeId          = 3,
            StatusCode      = 0,
            IsParticipating = true,
            ResultJson      = string.Empty,
        });

        Thread.Sleep(300);
        translator.Tick();
        bus.SwapBuffers();

        var events = bus.ConsumeManaged<NodeOpCompletedEvent>();
        Assert.Single(events);
        Assert.Equal(3, events[0].NodeId);
        Assert.Null(events[0].ResultPayload);
    }
}
