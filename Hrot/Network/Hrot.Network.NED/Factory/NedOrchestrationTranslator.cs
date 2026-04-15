using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.NED.Messages;

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
    private readonly Hrot.Network.Orchestration.ClusterOpMasterTranslator _clusterOpTranslator;
    private readonly Hrot.Network.Orchestration.NodeOpMasterTranslator    _nodeOpTranslator;
    // DDS readers/writers owned by this translator:
    private readonly DdsReader<ClusterOpRequest>                       _sysOpRequestReader;
    private readonly DdsWriter<ClusterOpStatus>                        _sysOpStatusWriter;
    private readonly DdsReader<NodeOpStatus>                           _nodeOpStatusReader;
    private readonly DdsWriter<AssetInventoryTopic>                    _inventoryWriter;
    private readonly DdsWriter<SystemStateTopic>                       _stateWriter;
    // Per-node command writers, created on demand and cached.
    private readonly Dictionary<int, DdsWriter<NodeOpCommand>>         _commandWriters = new();
    private readonly DdsParticipant                                    _participant;
    private bool _disposed;

    public NedOrchestrationTranslator(DdsParticipant participant, FdpEventBus bus)
    {
        _participant         = participant ?? throw new ArgumentNullException(nameof(participant));
        _bus                 = bus         ?? throw new ArgumentNullException(nameof(bus));
        _heartbeatReader     = new DdsReader<NodeHeartbeat>(_participant);
        _sysOpRequestReader  = new DdsReader<ClusterOpRequest>(_participant);
        _sysOpStatusWriter   = new DdsWriter<ClusterOpStatus>(_participant);
        _nodeOpStatusReader  = new DdsReader<NodeOpStatus>(_participant);
        _inventoryWriter     = new DdsWriter<AssetInventoryTopic>(_participant);
        _stateWriter         = new DdsWriter<SystemStateTopic>(_participant);
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
            });
        }

        _clusterOpTranslator.Tick();
        _nodeOpTranslator.Tick();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _heartbeatReader.Dispose();
        _sysOpRequestReader.Dispose();
        _sysOpStatusWriter.Dispose();
        _nodeOpStatusReader.Dispose();
        _inventoryWriter.Dispose();
        _stateWriter.Dispose();
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
