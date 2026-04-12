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
}
