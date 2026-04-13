using Hrot.Common.Abstractions;

namespace Hrot.Core.Network;

/// <summary>
/// Factory that creates all protocol-specific network infrastructure for a simulation node.
/// Implemented by Hrot.Network.NED (NedNetworkFactory) and Hrot.Network.BDC (BdcNetworkFactory).
/// </summary>
public interface INetworkFactory
{
    /// <summary>Creates the replication module that synchronises entity state over the network.</summary>
    IReplicationModule CreateReplicationModule();

    /// <summary>Creates the command gateway for sending mission control commands.</summary>
    ICommandGateway CreateCommandGateway();

    /// <summary>Creates the egress writers for ExCon-originated entity lifecycle commands.</summary>
    IExConEgressWriters CreateExConEgressWriters();

    /// <summary>Creates the time-control gateway for ExCon-originated time control commands.</summary>
    ITimeControlGateway CreateTimeControlGateway();

    /// <summary>
    /// Creates the SimHost-side mission-control sender used by the visualization layer.
    /// </summary>
    ISimHostMissionSender CreateSimHostMissionSender();

    /// <summary>
    /// Creates the SimHost auxiliary translator set (time-sync, combat, mission-control).
    /// The returned object is cast to the concrete type by ClusterRunner callers that have
    /// access to <c>IDescriptorTranslator</c>.
    /// </summary>
    ISimHostAuxiliaryTranslators CreateSimHostAuxiliaryTranslators();

    /// <summary>Creates the pathfinding network translators for the given node role.</summary>
    ISimHostPathfindingTranslators CreateSimHostPathfindingTranslators();

    /// <summary>Creates the perception network translators for the given node role.</summary>
    ISimHostPerceptionTranslators CreateSimHostPerceptionTranslators();

    /// <summary>Creates the IG-specific DDS ingress translator provider.</summary>
    IIgTranslators CreateIgTranslators();

    /// <summary>
    /// Creates the IG network adapter wrapping all DDS writers and readers for the IG.
    /// Pass <c>null</c> for <paramref name="participant"/> in headless/offline mode.
    /// </summary>
    IIgNetworkAdapter CreateIgNetworkAdapter(CycloneDDS.Runtime.DdsParticipant? participant, long nodeId = 0);
}
