using System;
using CycloneDDS.Runtime;
using Fdp.Core;
using Hrot.Common.Orchestration;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.Core.Network;

namespace Hrot.Network.NED.Factory;

/// <summary>
/// Composite DDS slave-side orchestration translator.
/// Owns a <see cref="NodeOpSlaveTranslator"/> (NodeOpCommand ingress + heartbeat/status egress)
/// and a <see cref="ClusterOpEgressTranslator"/> (bus-intent to DDS ClusterOpRequest egress).
/// Created and returned by <see cref="NedNetworkFactory.CreateSlaveOrchestratorTranslators"/>.
/// </summary>
internal sealed class NedSlaveOrchestrationTranslator : ISlaveOrchestrationTranslator
{
    private readonly NodeOpSlaveTranslator       _nodeOpTranslator;
    private readonly ClusterOpEgressTranslator   _egressTranslator;

    public NedSlaveOrchestrationTranslator(DdsParticipant participant, FdpEventBus bus, int nodeId)
    {
        if (participant == null) throw new ArgumentNullException(nameof(participant));
        if (bus == null)         throw new ArgumentNullException(nameof(bus));

        _nodeOpTranslator = new NodeOpSlaveTranslator(
            commandReader:   new DdsReader<NodeOpCommand>(participant),
            statusWriter:    new DdsWriter<NodeOpStatus>(participant),
            heartbeatWriter: new DdsWriter<NodeHeartbeat>(participant),
            bus:             bus,
            nodeId:          nodeId);

        _egressTranslator = new ClusterOpEgressTranslator(bus, participant);
    }

    /// <inheritdoc/>
    public void Tick()
    {
        _nodeOpTranslator.Tick();
        _egressTranslator.Tick();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _nodeOpTranslator.Dispose();
        _egressTranslator.Dispose();
    }
}
