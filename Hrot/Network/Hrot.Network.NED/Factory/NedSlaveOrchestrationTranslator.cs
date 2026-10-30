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

    /// <summary>⭐⭐ P6 part 2 (Q91 §4-E2) — every node receives files sent over DDS into its own staging root (R-4000).</summary>
    private readonly Hrot.Network.NED.Transfer.DdsFileReceiver _fileReceiver;

    public NedSlaveOrchestrationTranslator(DdsParticipant participant, FdpEventBus bus, int nodeId)
    {
        if (participant == null) throw new ArgumentNullException(nameof(participant));
        if (bus == null)         throw new ArgumentNullException(nameof(bus));

        _nodeOpTranslator = new NodeOpSlaveTranslator(
            commandReader:      new DdsReader<NodeOpCommand>(participant),
            statusWriter:       new DdsWriter<NodeOpStatus>(participant),
            heartbeatWriter:    new DdsWriter<NodeHeartbeat>(participant),
            bus:                bus,
            nodeId:             nodeId,
            capabilitiesWriter: new DdsWriter<NodeCapabilitiesTopic>(participant),   // CE-285 (C-cap)
            clusterStateReader: new DdsReader<ClusterStateTopic>(participant),       // ⭐ C2 — the session this node follows
            inventoryReader:    new DdsReader<AssetInventoryTopic>(participant),     // ⭐ R-3055 — what the cluster can replay
            transportCapabilities: ShareToken(nodeId),                               // ⭐ P6 N5 — how the master reaches this node
            sessionReader:      new DdsReader<ReplaySessionTopic>(participant),      // ⭐ CE-3329 — the replay's session layer
            opStatusReader:     new DdsReader<ClusterOpStatus>(participant),         // ⭐ CE-1436 — how the master's ops ended (the check round's answer), on every node
            contextReader:      new DdsReader<OrchestratorContextTopic>(participant)); // ⭐ CE-3344 — the master's in-sync set, so the node's ClusterView knows whether it is in sync

        _fileReceiver = new Hrot.Network.NED.Transfer.DdsFileReceiver(participant, nodeId, NodeRoot(nodeId));

        _egressTranslator = new ClusterOpEgressTranslator(bus, participant);
    }

    /// <inheritdoc/>
    public void Tick()
    {
        _nodeOpTranslator.Tick();
        _egressTranslator.Tick();
        _fileReceiver.Tick();
    }

    private static string NodeRoot(int nodeId)
        => Fdp.Toolkit.Orchestration.OrchestrationConstants.GetNodeStagingRoot(nodeId);

    /// <summary>⭐ The share this node advertises: its own staging root, unless the deployment says otherwise (a remote Player: none).</summary>
    private static string[] ShareToken(int nodeId)
    {
        // ⭐ this node runs a DDS receiver (below) — say so; and its share, unless the deployment says none
        var share = Fdp.Toolkit.Orchestration.Transfer.TransferTokens.ForThisProcess(NodeRoot(nodeId));
        return share != null
            ? new[] { Fdp.Toolkit.Orchestration.Transfer.TransferTokens.DdsReceiver, share }
            : new[] { Fdp.Toolkit.Orchestration.Transfer.TransferTokens.DdsReceiver };
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _nodeOpTranslator.Dispose();
        _egressTranslator.Dispose();
        _fileReceiver.Dispose();
    }
}
