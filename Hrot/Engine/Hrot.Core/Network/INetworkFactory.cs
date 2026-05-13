using System;
using System.Collections.Generic;
using CarKinem.Trajectory;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.DER;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Modules;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.NetworkSpawning;
using Hrot.Common;
using Hrot.Common.Abstractions;
using Hrot.Common.Infrastructure;

namespace Hrot.Core.Network;

/// <summary>
/// Factory that creates all protocol-specific network infrastructure for a simulation node.
/// Implemented by Hrot.Network.NED (NedNetworkFactory) and Hrot.Network.BDC (BdcNetworkFactory).
/// </summary>
public interface INetworkFactory : IGizmoNetworkFactory
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
    ISimHostPathfindingTranslators CreateSimHostPathfindingTranslators(TrajectoryPoolManager? trajectoryPool = null);

    /// <summary>Creates the perception network translators for the given node role.</summary>
    ISimHostPerceptionTranslators CreateSimHostPerceptionTranslators(GhostCreationSystem? ghostCreationSystem = null);

    /// <summary>
    /// Creates the DDS-backed ECS systems for processing attribute/descriptor update requests.
    /// Returns empty list when no participant is available (offline / no-DDS mode).
    /// These systems must be added to the pre-kernel SystemGroup that runs before the main tick.
    /// </summary>
    IReadOnlyList<IEcsModuleSystem> CreateSimHostAttributeUpdateSystems();

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
    INetworkFactory ConfigureForNode(HrotNodeContext context, NodeRole role, Fdp.Toolkit.Behavior.BehaviorRegistry? behaviorRegistry = null);

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
    /// <see cref="ConfigureForNode(HrotNodeContext, NodeRole, Fdp.Toolkit.Behavior.BehaviorRegistry)"/>.
    /// </summary>
    ICgfEntityLifecycleAdapters? CreateCgfEntityLifecycleAdapters();

    /// <summary>
    /// The DDS participant owned by this factory instance.
    /// Null when the factory was created without a participant (headless / unit-test mode).
    /// Subsystems that need a participant should prefer this over calling
    /// HrotEnvironment.CreateParticipant directly.
    /// </summary>
    new DdsParticipant? Participant { get; }

    /// <summary>
    /// Protocol-specific ordinal for the "WorldPos" (geo-spatial position) descriptor,
    /// used when calling <c>SmartEgressUtil.MarkDirty</c> from domain code.
    /// Returns 0 for protocols that do not use this descriptor.
    /// </summary>
    long WorldPosDescriptorId { get; }

    /// <summary>
    /// Protocol-specific ordinal for the "NavigationStatus" descriptor,
    /// used in split-authority routing from domain code.
    /// Returns 0 for protocols that do not use this descriptor.
    /// </summary>
    long NavigationStatusDescriptorId { get; }

    /// <summary>
    /// Creates the orchestrator master-side DDS translators (ClusterOp, NodeOp, heartbeat).
    /// All created DDS resources are owned by the returned translator and released on Dispose().
    /// Returns a no-op translator when there is no DDS participant (headless / test mode).
    /// No domain types (ClusterMaster, etc.) are accepted; integration is via bus events only.
    /// </summary>
    IOrchestrationTranslator CreateOrchestratorTranslators(FdpEventBus bus, int nodeId);

    /// <summary>
    /// Creates and starts the hosted DDS ID allocator server background thread.
    /// The caller owns the returned handle; Dispose() blocks via Thread.Join to guarantee
    /// clean teardown before the shared DdsParticipant is destroyed.
    /// Returns a no-op IDisposable when there is no DDS participant.
    /// </summary>
    IDisposable CreateIdAllocatorServer();

    /// <summary>
    /// Creates a network ID allocator client for the given logical client identifier.
    /// Returns a <c>DdsIdAllocator</c> when a live DDS participant is available; otherwise
    /// returns a local <see cref="SequentialIdAllocator"/> for offline/headless environments.
    /// </summary>
    /// <param name="clientId">Human-readable logical name used in DDS discovery (e.g. "SimHostAllocator").</param>
    /// <param name="skipRoutingWait">
    /// When <c>true</c>, skips the publication-match wait for server discovery.
    /// Use for ghost nodes (e.g. IG) that do not require an authoritative allocator.
    /// </param>
    INetworkIdAllocator CreateIdAllocator(string clientId, bool skipRoutingWait = false);

    /// <summary>
    /// Creates the master-side time-sync DDS translators (time-mode broadcast,
    /// lockstep barrier, master NTP sync). Absorbs _timeModeTranslator, _lockstepTranslator,
    /// and _masterTimeSyncTranslator. Returns a no-op implementation when there is no
    /// DDS participant.
    /// </summary>
    IMasterTimeTranslators CreateMasterTimeTranslators(FdpEventBus bus, int nodeId);

    /// <summary>
    /// Creates the slave-side orchestration translator (NodeOpCommand ingress,
    /// NodeOpStatus + NodeHeartbeat egress).
    /// Returns a no-op translator when there is no DDS participant.
    /// </summary>
    ISlaveOrchestrationTranslator CreateSlaveOrchestratorTranslators(FdpEventBus bus, int nodeId);

    /// <summary>
    /// Creates the cluster observer translator (ClusterStateTopic, AssetInventoryTopic ingress).
    /// Returns a no-op translator when there is no DDS participant.
    /// </summary>
    IOrchestrationObserver CreateOrchestrationObserver(FdpEventBus bus);
    /// <summary>
    /// Creates the gizmo interaction network translators (ingress and/or egress) for
    /// remote UI interaction streaming. Each translator carries a <see cref="TranslatorDirection"/>
    /// flag so the caller can route it to the appropriate
    /// <c>CycloneNetworkIngressSystem</c> or <c>CycloneEgressSystem</c>.
    /// Returns an empty list when the protocol does not support gizmo streaming.
    /// </summary>
    /// <param name="interactionBus">The isolated per-node interaction event bus.</param>
    /// <param name="localNodeId">The local node id used to stamp outgoing batches.</param>
    /// <param name="headless">
    /// When <c>true</c>, creates an ingress translator (node receives UI events from a remote viewer).
    /// When <c>false</c>, creates an egress translator (node forwards locally-generated UI events).
    /// </param>
    new IReadOnlyList<INetworkTranslator> CreateGizmoTranslators(FdpEventBus interactionBus, long localNodeId, bool headless);

    /// <summary>
    /// Creates the ECS system that publishes the gizmo primitive buffer to the network each frame.
    /// Returns <c>null</c> when the protocol does not support gizmo streaming.
    /// </summary>
    new IEcsModuleSystem? CreateGizmoPublisherSystem(DebugPrimitiveBuffer buffer, long localNodeId);}
