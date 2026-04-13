using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
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

    /// <summary>
    /// Creates the DDS-backed ECS systems for processing attribute/descriptor update requests.
    /// Returns empty list when no participant is available (offline / no-DDS mode).
    /// These systems must be added to the pre-kernel SystemGroup that runs before the main tick.
    /// </summary>
    IReadOnlyList<ComponentSystem> CreateSimHostAttributeUpdateSystems();

    /// <summary>Creates the IG-specific DDS ingress translator provider.</summary>
    IIgTranslators CreateIgTranslators();

    /// <summary>
    /// Creates the IG network adapter wrapping all DDS writers and readers for the IG.
    /// Pass <c>null</c> for <paramref name="participant"/> in headless/offline mode.
    /// </summary>
    IIgNetworkAdapter CreateIgNetworkAdapter(DdsParticipant? participant, long nodeId = 0);

    /// <summary>
    /// Creates the IG-side egress translators that convert bus events (SpawnEntityCommand,
    /// UpdateEntityCommand, DestroyEntityCommand) into DDS write calls.
    /// Returns an empty collection when the protocol does not support IG egress.
    /// </summary>
    IReadOnlyList<IDescriptorTranslator> CreateIgEgressTranslators(
        DdsParticipant participant,
        FdpEventBus bus,
        IGeographicTransform geoTransform,
        long nodeId);

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
    /// Creates the protocol-specific entity lifecycle adapters required by a CGF (Brain) node.
    /// Returns null when this protocol does not support CGF entity creation
    /// (e.g. BDC or offline factories).
    /// Must be called on a factory instance already configured via
    /// <see cref="ConfigureForNode(HrotNodeContext, NodeRole, FDP.Toolkit.Behavior.DoctrineRegistry)"/>.
    /// </summary>
    ICgfEntityLifecycleAdapters? CreateCgfEntityLifecycleAdapters();

    /// <summary>
    /// The DDS participant owned by this factory instance.
    /// Null when the factory was created without a participant (headless / unit-test mode).
    /// Subsystems that need a participant should prefer this over calling
    /// HrotEnvironment.CreateParticipant directly.
    /// </summary>
    DdsParticipant? Participant { get; }

    /// <summary>
    /// Protocol-specific ordinal for the "WorldPos" (geo-spatial position) descriptor,
    /// used when calling <c>SmartEgressUtil.MarkDirty</c> from domain code.
    /// Returns 0 for protocols that do not use this descriptor.
    /// </summary>
    long WorldPosDescriptorId { get; }
}
