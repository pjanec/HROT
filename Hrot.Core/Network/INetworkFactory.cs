using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using FDP.Toolkit.DER;
using Hrot.Common;
using Hrot.Common.Abstractions;
using Hrot.Common.Infrastructure;

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
    IIgNetworkAdapter CreateIgNetworkAdapter(DdsParticipant? participant, long nodeId = 0);

    /// <summary>
    /// Creates DDS ingress handlers for ExCon (map-click, selection, entity lifecycle ACKs,
    /// map command ACKs, entity master/descriptor bridging handlers).
    /// </summary>
    IEnumerable<IIngressHandler> CreateExConIngressHandlers(
        DdsParticipant?                   participant,
        long                              localNodeId,
        IDerRepo                          repo,
        Action<MapClickEventDto>          onMapClick,
        Action<SelectionChangedEventDto>  onSelectionChanged,
        Action<EntityLifecycleAckDto>     onEntityLifecycleAck,
        Action<MapCommandAckDto>          onMapCommandAck);

    /// <summary>
    /// Returns a new factory instance configured for a specific node context (participant, role, etc.).
    /// Used by subsystems that build their own <see cref="HrotNodeContext"/> and need a properly-wired
    /// factory for the participant/entityMap produced by the HrotNodeBuilder.
    /// </summary>
    INetworkFactory ConfigureForNode(HrotNodeContext context, NodeRole role, FDP.Toolkit.Behavior.DoctrineRegistry? doctrineRegistry = null);

    /// <summary>
    /// Returns a new factory instance configured with the given DDS participant and node ID.
    /// Used by subsystems that create their own participant directly (e.g. ExCon).
    /// </summary>
    INetworkFactory ConfigureForNode(DdsParticipant? participant, int nodeId, NodeRole role);

    /// <summary>
    /// Protocol-specific ordinal for the "WorldPos" (geo-spatial position) descriptor,
    /// used when calling <c>SmartEgressUtil.MarkDirty</c> from domain code.
    /// Returns 0 for protocols that do not use this descriptor.
    /// </summary>
    long WorldPosDescriptorId { get; }
}
