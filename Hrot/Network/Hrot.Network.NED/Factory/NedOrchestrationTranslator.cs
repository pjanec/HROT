using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.NED.Messages;
using Fdp.Core.Serialization;

namespace Hrot.Network.NED.Factory;

/// <summary>
/// Composite DDS orchestration translator for the master (Orchestrator) node.
/// Owns all DDS readers/writers required by <see cref="Hrot.Network.Orchestration.ClusterOpMasterTranslator"/>
/// and <see cref="Hrot.Network.Orchestration.NodeOpMasterTranslator"/>, plus the heartbeat bridge.
/// Created and owned by <see cref="NedNetworkFactory.CreateOrchestratorTranslators"/>.
/// </summary>
internal sealed class NedOrchestrationTranslator : Hrot.Core.Network.IOrchestrationTranslator
{
    private readonly FdpEventBus                                       _bus;
    private readonly DdsReader<NodeHeartbeat>                          _heartbeatReader;
    private readonly DdsReader<NodeCapabilitiesTopic>                  _capabilitiesReader;   // CE-285 (C-cap)
    private readonly Hrot.Network.Orchestration.ClusterOpMasterTranslator _clusterOpTranslator;
    private readonly Hrot.Network.Orchestration.NodeOpMasterTranslator    _nodeOpTranslator;
    // DDS readers/writers owned by this translator:
    private readonly DdsReader<ClusterOpRequest>                       _sysOpRequestReader;
    private readonly DdsWriter<ClusterOpStatus>                        _sysOpStatusWriter;
    private readonly DdsReader<NodeOpStatus>                           _nodeOpStatusReader;
    private readonly DdsWriter<AssetInventoryTopic>                    _inventoryWriter;
    private readonly DdsWriter<ClusterStateTopic>                       _stateWriter;
    // Per-node command writers, created on demand and cached.
    private readonly Dictionary<int, DdsWriter<NodeOpCommand>>         _commandWriters = new();
    // ⭐ Q86 §4-B — the Context Plane (mgmt-1 §1.2): written from the bus event, never by the handler itself.
    private readonly DdsWriter<OrchestratorContextTopic>               _contextWriter;
    // ⭐ C1 (Q91 §4-O) — node-id requests in, grants out.
    private readonly DdsReader<NodeIdRequest>                           _nodeIdRequestReader;
    private readonly DdsWriter<NodeIdGrant>                             _nodeIdGrantWriter;
    private readonly DdsParticipant                                    _participant;
    private string _lastContextScenarioId = string.Empty;
    private string _lastInSyncJson = string.Empty;   // ⭐ C6 — empty = not published yet ⇒ nodes filter nothing
    private bool _disposed;

    public NedOrchestrationTranslator(DdsParticipant participant, FdpEventBus bus)
    {
        _participant         = participant ?? throw new ArgumentNullException(nameof(participant));
        _bus                 = bus         ?? throw new ArgumentNullException(nameof(bus));
        _heartbeatReader     = new DdsReader<NodeHeartbeat>(_participant);
        _capabilitiesReader  = new DdsReader<NodeCapabilitiesTopic>(_participant);
        _sysOpRequestReader  = new DdsReader<ClusterOpRequest>(_participant);
        _sysOpStatusWriter   = new DdsWriter<ClusterOpStatus>(_participant);
        _nodeOpStatusReader  = new DdsReader<NodeOpStatus>(_participant);
        _inventoryWriter     = new DdsWriter<AssetInventoryTopic>(_participant);
        _stateWriter         = new DdsWriter<ClusterStateTopic>(_participant);
        _contextWriter       = new DdsWriter<OrchestratorContextTopic>(_participant);
        _nodeIdRequestReader = new DdsReader<NodeIdRequest>(_participant);
        _nodeIdGrantWriter   = new DdsWriter<NodeIdGrant>(_participant);
        _clusterOpTranslator = new Hrot.Network.Orchestration.ClusterOpMasterTranslator(
            _sysOpRequestReader, _sysOpStatusWriter, _bus, null, _inventoryWriter, _stateWriter);
        _nodeOpTranslator    = new Hrot.Network.Orchestration.NodeOpMasterTranslator(
            GetOrCreateCommandWriter, _nodeOpStatusReader, _bus);
    }

    /// <inheritdoc/>
    public void Tick()
    {
        // Heartbeat bridge: DDS NodeHeartbeat -> bus NodeHeartbeatEvent.
        using var hbScope = _heartbeatReader.Take();
        foreach (var sample in hbScope)
        {
            if (!sample.IsValid) continue;
            _bus.PublishManaged(new NodeHeartbeatEvent
            {
                NodeId        = sample.Data.NodeId,
                LocalStateId  = (int)sample.Data.LocalClusterState,
                WallTicksUtc  = sample.Data.WallTicksUtc,
                SubsystemName = sample.Data.SubsystemName ?? string.Empty,
                SessionId     = sample.Data.SessionId,   // ⭐ C2 — the session the node follows
                // CE-286 (C-roles): roles are derived from the NodeCapabilities tokens, not the heartbeat.
            });
        }

        // Capabilities bridge: durable DDS NodeCapabilities -> bus NodeCapabilitiesEvent (CE-285).
        using (var capScope = _capabilitiesReader.Take())
            foreach (var sample in capScope)
            {
                if (!sample.IsValid) continue;
                _bus.PublishManaged(new NodeCapabilitiesEvent
                {
                    NodeId       = sample.Data.NodeId,
                    Capabilities = DeserializeTokens(sample.Data.CapabilitiesJson),
                });
            }

        // ⭐ C1 — node-id requests: DDS → bus (the master answers on the bus, below).
        using (var idScope = _nodeIdRequestReader.Take())
            foreach (var sample in idScope)
            {
                if (!sample.IsValid) continue;
                _bus.PublishManaged(new NodeIdRequestEvent
                {
                    ComputerName = sample.Data.ComputerName ?? string.Empty,
                    ProcessId    = sample.Data.ProcessId,
                    ForcedId     = sample.Data.ForcedId,
                    PreferredId  = sample.Data.PreferredId,
                });
            }
        foreach (var grant in _bus.ReadManaged<NodeIdGrantEvent>())
            _nodeIdGrantWriter.Write(new NodeIdGrant
            {
                ComputerHash = NodeIdKeys.ComputerHash(grant.ComputerName),
                ProcessId    = grant.ProcessId,
                NodeId       = grant.NodeId,
                SessionId    = grant.SessionId,
                Refused      = grant.Refused,
                Reason       = grant.Reason ?? string.Empty,
            });

        _clusterOpTranslator.Tick();
        _nodeOpTranslator.Tick();

        // Context bridge: bus OrchestratorContextChangedEvent -> DDS OrchestratorContextTopic (Q86 §4-B).
        // ⭐ C6 (Q91 §4-N) — the topic is keyless and carries BOTH the scenario and the master's InSync set, so each write
        //   repeats the last value of the other field.
        bool contextChanged = false;
        foreach (var ctx in _bus.ReadManaged<OrchestratorContextChangedEvent>())
        {
            _lastContextScenarioId = ctx.ScenarioId ?? string.Empty;
            contextChanged = true;
        }
        foreach (var set in _bus.ReadManaged<InSyncSetChangedEvent>())
        {
            _lastInSyncJson = System.Text.Json.JsonSerializer.Serialize(set.NodeIds ?? Array.Empty<int>());
            contextChanged = true;
        }
        if (contextChanged)
            _contextWriter.Write(new OrchestratorContextTopic
            {
                ScenarioId          = _lastContextScenarioId,
                RequiredNodeIdsJson = _lastInSyncJson,
            });
    }

    private static string[] DeserializeTokens(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try { return System.Text.Json.JsonSerializer.Deserialize<string[]>(json, FdpJsonOptionsRegistry.DefaultRelaxed) ?? Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _heartbeatReader.Dispose();
        _capabilitiesReader.Dispose();
        _sysOpRequestReader.Dispose();
        _sysOpStatusWriter.Dispose();
        _nodeOpStatusReader.Dispose();
        _inventoryWriter.Dispose();
        _stateWriter.Dispose();
        _contextWriter.Dispose();
        _nodeIdRequestReader.Dispose();
        _nodeIdGrantWriter.Dispose();
        foreach (var w in _commandWriters.Values)
            w.Dispose();
        _commandWriters.Clear();
    }

    private DdsWriter<NodeOpCommand> GetOrCreateCommandWriter(int nodeId)
    {
        if (!_commandWriters.TryGetValue(nodeId, out var w))
            _commandWriters[nodeId] = w = new DdsWriter<NodeOpCommand>(_participant);
        return w;
    }
}
