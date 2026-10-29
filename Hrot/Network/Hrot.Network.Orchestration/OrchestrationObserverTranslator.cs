using System;
using System.Text.Json;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Time.Messages;
using Hrot.NED.Descriptors.Orchestration;
using NedNodeOpType   = Hrot.NED.Descriptors.Orchestration.NodeOpType;
using FdpClusterState = Fdp.Toolkit.Orchestration.ClusterState;
using FdpNodeOpType   = Fdp.Toolkit.Orchestration.NodeOpType;
using Fdp.Core.Serialization;

namespace Hrot.Common.Orchestration;

/// <summary>
/// Anti-Corruption Layer: reads the cluster's MONITORING topics (heartbeats, capabilities, time mode, the node-op command/status mirror) and
/// publishes corresponding CQRS events on an <see cref="FdpEventBus"/> for consumption by <c>ClusterUiCache</c>. ⭐ CE-1436 — the cluster's FACT
/// topics (state, inventory, replay session, op status) come through the shared <see cref="ClusterFactsIngress"/>, the one every node's slave
/// translator owns too (design V1/V2: monitoring stays here because on an acting node those events drive its own egress).
///
/// <para>Constructor: <see cref="OrchestrationObserverTranslator(DdsParticipant, FdpEventBus)"/></para>
/// <para>Call <see cref="Tick"/> once per frame before calling <c>FdpEventBus.SwapBuffers()</c>
/// so that <c>ClusterUiCache.Update()</c> can see the translated events in the same frame.</para>
/// <para>
/// <b>NodeOpCommand messages are forwarded promiscuously</b> (all target nodes, not only the
/// local node) so <c>ClusterUiCache</c> can build a complete 2PC transaction history.
/// </para>
/// </summary>
public sealed class OrchestrationObserverTranslator : IDisposable
{
    private readonly ClusterFactsIngress              _facts;   // ⭐ CE-1436 — the ONE ingress of the cluster's facts (state, inventory, session, op status), shared with every node's slave translator
    private readonly DdsReader<NodeHeartbeat>         _heartbeatReader;
    private readonly DdsReader<SwitchTimeModeWireDto> _timeModeReader;
    private readonly DdsReader<NodeOpCommand>         _nodeOpCmdReader;
    private readonly DdsReader<NodeOpStatus>          _nodeOpStatusReader;
    private readonly DdsReader<NodeCapabilitiesTopic> _capabilitiesReader;   // CE-285 (C-cap)
    private readonly FdpEventBus                      _bus;

    /// <summary>
    /// Creates a new translator that reads from <paramref name="participant"/> and
    /// publishes events to <paramref name="bus"/>.
    /// </summary>
    public OrchestrationObserverTranslator(DdsParticipant participant, FdpEventBus bus)
    {
        _bus                = bus ?? throw new ArgumentNullException(nameof(bus));
        _facts              = ClusterFactsIngress.For(participant, bus);
        _heartbeatReader    = new DdsReader<NodeHeartbeat>(participant);
        _timeModeReader     = new DdsReader<SwitchTimeModeWireDto>(participant);
        _nodeOpCmdReader    = new DdsReader<NodeOpCommand>(participant);
        _nodeOpStatusReader = new DdsReader<NodeOpStatus>(participant);
        _capabilitiesReader = new DdsReader<NodeCapabilitiesTopic>(participant);
    }

    /// <summary>
    /// Polls all DDS topics and publishes the translated CQRS events to the bus.
    /// Call once per frame, before the bus SwapBuffers.
    /// </summary>
    public void Tick()
    {
        _facts.Tick();   // ⭐ CE-1436 — ClusterStateTopic · AssetInventoryTopic · ReplaySessionTopic · ClusterOpStatus, one decoder each

        // NodeHeartbeat → NodeHeartbeatEvent
        using (var l = _heartbeatReader.Take())
            foreach (var s in l)
                if (s.IsValid)
                    _bus.PublishManaged(new NodeHeartbeatEvent
                    {
                        NodeId        = s.Data.NodeId,
                        LocalStateId  = (int)s.Data.LocalClusterState,
                        WallTicksUtc  = s.Data.WallTicksUtc,
                        SubsystemName = s.Data.SubsystemName ?? string.Empty,
                        SessionId     = s.Data.SessionId,   // ⭐ C2
                        // CE-286 (C-roles): roles are derived from the NodeCapabilities tokens, not the heartbeat.
                    });

        // NodeCapabilities → NodeCapabilitiesEvent (CE-285 — durable static attributes, gathered by ClusterMaster)
        using (var l = _capabilitiesReader.Take())
            foreach (var s in l)
                if (s.IsValid)
                    _bus.PublishManaged(new NodeCapabilitiesEvent
                    {
                        NodeId       = s.Data.NodeId,
                        Capabilities = DeserializeStringArray(s.Data.CapabilitiesJson),
                    });

        // SwitchTimeModeWireDto → SwitchTimeModeEvent (unmanaged — use Publish, not PublishManaged)
        using (var l = _timeModeReader.Take())
            foreach (var s in l)
                if (s.IsValid)
                    _bus.Publish(s.Data.ToEvent());

        // NodeOpCommand → ExecuteNodeOpIntent (promiscuous — all target nodes)
        using (var l = _nodeOpCmdReader.Take())
            foreach (var s in l)
                if (s.IsValid)
                {
                    var domainPayload = NodeOpSlaveTranslator.DeserializeNodePayload(
                        s.Data.Operation, s.Data.PayloadJson);
                    _bus.PublishManaged(new ExecuteNodeOpIntent
                    {
                        TransactionId = s.Data.TransactionId,
                        TargetNodeId  = s.Data.TargetNodeId,
                        Operation     = (FdpNodeOpType)(int)s.Data.Operation,
                        DomainPayload = domainPayload,
                    });
                }

        // NodeOpStatus → NodeOpCompletedEvent
        using (var l = _nodeOpStatusReader.Take())
            foreach (var s in l)
                if (s.IsValid)
                    _bus.PublishManaged(new NodeOpCompletedEvent
                    {
                        TransactionId   = s.Data.TransactionId,
                        Operation       = (FdpNodeOpType)(int)s.Data.Operation,
                        NodeId          = s.Data.NodeId,
                        StatusCode      = (OrchestrationStatusCode)s.Data.StatusCode,
                        IsParticipating = s.Data.IsParticipating,
                        ResultPayload   = s.Data.ResultJson,
                    });
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _facts.Dispose();
        _heartbeatReader.Dispose();
        _timeModeReader.Dispose();
        _nodeOpCmdReader.Dispose();
        _nodeOpStatusReader.Dispose();
        _capabilitiesReader.Dispose();
    }

    private static string[] DeserializeStringArray(string? json) => ClusterFactsIngress.DeserializeStringArray(json);
}
