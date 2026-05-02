using System.Collections.Generic;
using System;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Network.Cyclone.Services;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.DER;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Replication.Patching;
using Hrot.Common;
using Hrot.Common.Abstractions;
using Hrot.Common.Infrastructure;
using Hrot.Core.Network;
using Hrot.Map.Common;
using Hrot.Network.NED.CGF;
using Hrot.Network.NED.ExCon;
using Hrot.Network.NED.SimHost;
using Hrot.Network.Replication;
using Hrot.Network.Routing;
using Hrot.NED.Descriptors;
using Hrot.NED.Descriptors.Orchestration;
using Hrot.SimHost;
using Fdp.Toolkit.Replication.Abstractions;
using Hrot.Map.Common.Replication.Egress;
using NetworkEntityMap = Fdp.Toolkit.Replication.Services.NetworkEntityMap;

namespace Hrot.Network.NED.Factory;

/// <summary>
/// Implements <see cref="INetworkFactory"/> using NED (Network Exchange Description)
/// DDS protocols for simulation data exchange.
/// </summary>
public sealed class NedNetworkFactory : INetworkFactory
{
    private readonly DdsParticipant?      _participant;
    private readonly NetworkEntityMap     _entityMap;
    private readonly IGeographicTransform _geoTransform;
    private readonly FdpEventBus          _eventBus;
    // World bus used by replication-module ingress translators so they publish ECS commands
    // (SpawnEntityCommand, DestroyEntityCommand, UpdateEntityCommand) directly onto the ECS
    // world bus where NetworkSpawningSystem can consume them.  Null when not wired via
    // ConfigureForNode(HrotNodeContext,...); falls back to _eventBus in that case.
    private readonly FdpEventBus?         _worldBus;
    private readonly int                  _localNodeId;
    private readonly NodeRole             _role;
    private readonly ITkbDatabase?        _tkbDb;
    private readonly EntityLifecycleModule? _lifecycleModule;
    private readonly BehaviorRegistry?    _behaviorRegistry;

    public NedNetworkFactory(
        DdsParticipant?       participant,
        NetworkEntityMap      entityMap,
        IGeographicTransform  geoTransform,
        FdpEventBus           eventBus,
        int                   localNodeId,
        NodeRole              role,
        ITkbDatabase?         tkbDb            = null,
        EntityLifecycleModule? lifecycleModule  = null,
        BehaviorRegistry?     behaviorRegistry = null,
        FdpEventBus?          worldBus          = null)
    {
        _participant      = participant;
        _entityMap        = entityMap;
        _geoTransform     = geoTransform;
        _eventBus         = eventBus;
        _worldBus         = worldBus;
        _localNodeId      = localNodeId;
        _role             = role;
        _tkbDb            = tkbDb;
        _lifecycleModule  = lifecycleModule;
        _behaviorRegistry = behaviorRegistry;
    }

    /// <inheritdoc/>
    public DdsParticipant? Participant => _participant;

    /// <inheritdoc/>
    public IReplicationModule CreateReplicationModule()
        => new NedReplicationModule(
               participant:       _participant,
               role:              _role,
               entityMap:         _entityMap,
               geoTransform:      _geoTransform,
               // Use world bus when available so ingress translators publish ECS commands
               // (DestroyEntityCommand, UpdateEntityCommand) to the same bus that
               // NetworkSpawningSystem reads from.  Falls back to _eventBus in contexts
               // where no world bus was provided (e.g. standalone tests).
               eventBus:          _worldBus ?? _eventBus,
               localNodeId:       _localNodeId,
               domainId:          0,
               tkbDb:             _tkbDb,
               lifecycleModule:   _lifecycleModule,
               behaviorRegistry:  _behaviorRegistry);

    /// <inheritdoc/>
    public ICommandGateway CreateCommandGateway()
    {
        if (_participant == null) return new NullCommandGateway();
        return new Hrot.Map.Common.Commands.NedCommandGateway(_participant, _localNodeId);
    }

    /// <inheritdoc/>
    public IExConEgressWriters CreateExConEgressWriters()
    {
        if (_participant == null) return new NullExConEgressWriters();
        return new Hrot.Network.NED.ExCon.NedExConEgressWriters(_participant);
    }

    /// <inheritdoc/>
    public ITimeControlGateway CreateTimeControlGateway()
    {
        if (_participant == null) return new NullTimeControlGateway();
        return new Hrot.Network.NED.ExCon.NedTimeControlGateway(_participant);
    }

    /// <inheritdoc/>
    public ISimHostMissionSender CreateSimHostMissionSender()
    {
        if (_participant == null) return new NullSimHostMissionSender();
        return new NedSimHostMissionSender(_participant);
    }

    /// <inheritdoc/>
    public ISimHostAuxiliaryTranslators CreateSimHostAuxiliaryTranslators()
    {
        if (_participant == null) return new NullSimHostAuxiliaryTranslators();
        return new NedSimHostAuxiliaryTranslators(
            _participant, _entityMap, _eventBus, _localNodeId, _role);
    }

    /// <inheritdoc/>
    public IReadOnlyList<Fdp.ModuleHost.Abstractions.IEcsModuleSystem> CreateSimHostAttributeUpdateSystems()
    {
        if (_participant == null) return System.Array.Empty<Fdp.ModuleHost.Abstractions.IEcsModuleSystem>();
        var jsonAttributeCompiler = Hrot.SimHost.AttributeCompilerFactory.Build(_geoTransform);
        return new Fdp.ModuleHost.Abstractions.IEcsModuleSystem[]
        {
            new Hrot.Map.Common.Systems.UpdateEntityAttributeRequestSystem(
                _participant, _entityMap, _geoTransform, jsonAttributeCompiler),
            new Hrot.Map.Common.Replication.Ingress.UpdateEntityDescriptorRequestSystem(
                _participant, _entityMap, _geoTransform),
        };
    }

    /// <inheritdoc/>
    public ISimHostPathfindingTranslators CreateSimHostPathfindingTranslators(CarKinem.Trajectory.TrajectoryPoolManager? trajectoryPool = null)
    {
        if (_participant == null) return new NullSimHostPathfindingTranslators();
        return new NedSimHostPathfindingTranslators(_participant, _entityMap, _geoTransform, _role, trajectoryPool, _localNodeId);
    }

    /// <inheritdoc/>
    public ISimHostPerceptionTranslators CreateSimHostPerceptionTranslators(GhostCreationSystem? ghostCreationSystem = null)
    {
        if (_participant == null) return new NullSimHostPerceptionTranslators();
        return new NedSimHostPerceptionTranslators(_participant, _entityMap, _geoTransform, _role, _localNodeId, ghostCreationSystem);
    }

    /// <inheritdoc/>
    public IIgTranslators CreateIgTranslators()
        => new Hrot.Network.NED.IG.NedIgTranslators();

    /// <inheritdoc/>
    public IIgNetworkAdapter CreateIgNetworkAdapter(CycloneDDS.Runtime.DdsParticipant? participant, long nodeId = 0)
        => participant == null
           ? (IIgNetworkAdapter)Hrot.Core.Network.NullIgNetworkAdapter.Instance
           : new Hrot.Network.NED.IG.NedIgNetworkAdapter(participant, nodeId);

    /// <inheritdoc/>
    public IEnumerable<IIngressHandler> CreateExConIngressHandlers(
        DdsParticipant?                   participant,
        long                              localNodeId,
        IDerRepo                          repo,
        Action<MapClickEventDto>          onMapClick,
        Action<SelectionChangedEventDto>  onSelectionChanged,
        Action<EntityLifecycleAckDto>     onEntityLifecycleAck,
        Action<MapCommandAckDto>          onMapCommandAck)
    {
        if (participant == null)
            yield break;

        yield return new NedMapClickIngressHandler(participant, onMapClick, localNodeId: localNodeId);
        yield return new NedSelectionChangedIngressHandler(participant, onSelectionChanged);
        yield return new NedEntityLifecycleAckIngressHandler(participant, onEntityLifecycleAck, localNodeId: localNodeId);
        yield return new NedMapCommandAckIngressHandler(participant, onMapCommandAck, localNodeId: localNodeId);
        yield return new MasterIngressHandler<EntityMaster>(
            participant, repo, "EntityMaster",
            master => master.EntityId, master => master.TkbType, localNodeId: localNodeId);
        yield return new DescriptorIngressHandler<WorldPos>(participant, repo, "GeoSpatial", d => d.EntityId);
        yield return new NedEntityInfoBridgingHandler(participant, repo);
        yield return new DescriptorIngressHandler<EntityDamage>(participant, repo, "EntityDamage", d => d.EntityId);
        yield return new NedEntityMissionBridgingHandler(participant, repo);
        yield return new NedMapOverlayBridgingHandler(participant, repo);
        yield return new DescriptorIngressHandler<MapRoute>(participant, repo, "MapRoute", d => d.EntityId);
    }

    /// <inheritdoc/>
    public INetworkFactory ConfigureForNode(HrotNodeContext context, NodeRole role, BehaviorRegistry? behaviorRegistry = null)
    {
        EntityLifecycleModule? elm = null;
        foreach (var m in context.BaseModules)
        {
            if (m is EntityLifecycleModule e) { elm = e; break; }
        }

        return new NedNetworkFactory(
            participant:      context.Participant,
            entityMap:        context.EntityMap,
            geoTransform:     (IGeographicTransform)(context.GeoTransform ?? HrotEnvironment.CreateGeoTransform()),
            eventBus:         context.EventBus,
            localNodeId:      context.NodeId,
            role:             role,
            tkbDb:            context.TkbDb,
            lifecycleModule:  elm,
            behaviorRegistry: behaviorRegistry ?? _behaviorRegistry,
            worldBus:         context.World.Bus);
    }

    /// <inheritdoc/>
    public INetworkFactory ConfigureForNode(DdsParticipant? participant, int nodeId, NodeRole role)
    {
        return new NedNetworkFactory(
            participant:      participant,
            entityMap:        _entityMap,
            geoTransform:     _geoTransform,
            eventBus:         _eventBus,
            localNodeId:      nodeId,
            role:             role,
            tkbDb:            _tkbDb,
            lifecycleModule:  _lifecycleModule,
            behaviorRegistry: _behaviorRegistry);
    }

    /// <inheritdoc/>
    public IReadOnlyList<IDescriptorTranslator> CreateIgEgressTranslators(
        DdsParticipant participant,
        FdpEventBus bus,
        IGeographicTransform geoTransform,
        long nodeId)
    {
        return new IDescriptorTranslator[]
        {
            new SpawnEntityCommandEgressTranslator(participant, bus, geoTransform, nodeId),
            new UpdateEntityCommandEgressTranslator(participant, bus, _entityMap, geoTransform, nodeId),
            new DestroyEntityCommandEgressTranslator(participant, bus, nodeId),
        };
    }

    /// <inheritdoc/>
    public ICgfEntityLifecycleAdapters? CreateCgfEntityLifecycleAdapters()
    {
        if (_participant == null) return null;

        var clusterCache    = new SimpleClusterStateCache();
        var heartbeatReader = new DdsReader<NodeHeartbeat>(_participant);

        return new NedCgfEntityLifecycleAdapters(
            requestSource:     new NedEntityCreationRequestSource(_participant, _geoTransform),
            deleteSource:      new NedEntityDeletionRequestSource(_participant),
            ackSink:           new NedEntityAckSink(_participant),
            ownershipStrategy: new BrainMuscleOwnershipStrategy(clusterCache),
            jsonCompiler:      AttributeCompilerFactory.Build(_geoTransform),
            clusterCache:      clusterCache,
            heartbeatReader:   heartbeatReader);
    }

    /// <inheritdoc/>
    public long WorldPosDescriptorId => (long)EDescriptorType.dtWorldPos;

    /// <inheritdoc/>
    public long NavigationStatusDescriptorId => (long)EDescriptorType.dtNavigationStatus;

    /// <inheritdoc/>
    public Hrot.Core.Network.IOrchestrationTranslator CreateOrchestratorTranslators(FdpEventBus bus, int nodeId)
        => _participant != null
            ? new NedOrchestrationTranslator(_participant, bus)
            : new Hrot.Core.Network.NullOrchestrationTranslator();

    /// <inheritdoc/>
    public IDisposable CreateIdAllocatorServer()
        => _participant != null
            ? new HostedIdAllocatorServer(_participant)
            : new Hrot.Core.Network.NullDisposable();

    /// <inheritdoc/>
    public INetworkIdAllocator CreateIdAllocator(string clientId, bool skipRoutingWait = false)
    {
        if (_participant == null)
            return new Hrot.Core.Network.SequentialIdAllocator();

        var allocator = new DdsIdAllocator(_participant, clientId);
        if (!skipRoutingWait)
            DdsIdAllocatorHelper.EnsureRouting(_participant, allocator);
        return allocator;
    }

    /// <inheritdoc/>
    public Hrot.Core.Network.IMasterTimeTranslators CreateMasterTimeTranslators(FdpEventBus bus, int nodeId)
        => new NedMasterTimeTranslators(_participant, bus);

    /// <inheritdoc/>
    public Hrot.Core.Network.ISlaveOrchestrationTranslator CreateSlaveOrchestratorTranslators(FdpEventBus bus, int nodeId)
        => _participant != null
            ? new NedSlaveOrchestrationTranslator(_participant, bus, nodeId)
            : new Hrot.Core.Network.NullSlaveOrchestrationTranslator();

    /// <inheritdoc/>
    public Hrot.Core.Network.IOrchestrationObserver CreateOrchestrationObserver(FdpEventBus bus)
        => _participant != null
            ? new NedOrchestrationObserver(_participant, bus)
            : new Hrot.Core.Network.NullOrchestrationObserver();
}

/// <summary>
/// NED-specific entity lifecycle adapters for a CGF (Brain) node.
/// Created by <see cref="NedNetworkFactory.CreateCgfEntityLifecycleAdapters"/>.
/// </summary>
internal sealed class NedCgfEntityLifecycleAdapters : ICgfEntityLifecycleAdapters
{
    private readonly SimpleClusterStateCache   _clusterCache;
    private readonly DdsReader<NodeHeartbeat>  _heartbeatReader;

    public IEntityCreationRequestSource       RequestSource     { get; }
    public IEntityDeletionRequestSource       DeleteSource      { get; }
    public IEntityAckSink                     AckSink           { get; }
    public IOwnershipDistributionStrategy?    OwnershipStrategy { get; }
    public JsonAttributeCompiler?             JsonCompiler      { get; }

    public NedCgfEntityLifecycleAdapters(
        IEntityCreationRequestSource    requestSource,
        IEntityDeletionRequestSource    deleteSource,
        IEntityAckSink                  ackSink,
        IOwnershipDistributionStrategy? ownershipStrategy,
        JsonAttributeCompiler?          jsonCompiler,
        SimpleClusterStateCache         clusterCache,
        DdsReader<NodeHeartbeat>        heartbeatReader)
    {
        RequestSource     = requestSource;
        DeleteSource      = deleteSource;
        AckSink           = ackSink;
        OwnershipStrategy = ownershipStrategy;
        JsonCompiler      = jsonCompiler;
        _clusterCache     = clusterCache;
        _heartbeatReader  = heartbeatReader;
    }

    /// <inheritdoc/>
    public void PollNetwork()
    {
        using var loan = _heartbeatReader.Take();
        foreach (var sample in loan)
        {
            if (!sample.IsValid) continue;
            _clusterCache.UpdateNode(new NodeCapability
            {
                NodeId             = sample.Data.NodeId,
                Role               = MapSubsystemNameToRole(sample.Data.SubsystemName),
                CpuUsagePercent    = sample.Data.CpuUsagePercent,
                RamUsedBytes       = sample.Data.RamUsedBytes,
                LastSeenUtcSeconds = (double)sample.Data.WallTicksUtc / TimeSpan.TicksPerSecond,
            });
        }
    }

    private static NodeRole MapSubsystemNameToRole(string? name) =>
        name switch
        {
            "SimHost" => NodeRole.MuscleGround,
            "CGF"     => NodeRole.Brain,
            "IG"      => NodeRole.ImageGenerator,
            _         => NodeRole.None,
        };
}

/// <summary>No-op stub for ISimHostMissionSender.</summary>
internal sealed class NullSimHostMissionSender : ISimHostMissionSender
{
    public void SendNavigateToPoint(long id, System.Numerics.Vector2 dest, float speed, float radius) { }
    public void Dispose() { }
}

/// <summary>No-op stub for ISimHostAuxiliaryTranslators.</summary>
internal sealed class NullSimHostAuxiliaryTranslators : ISimHostAuxiliaryTranslators
{
    public void RegisterOn(Fdp.ModuleHost.ModuleHostKernel kernel) { }
    public void Dispose() { }
}

/// <summary>No-op stub for ISimHostPathfindingTranslators.</summary>
internal sealed class NullSimHostPathfindingTranslators : ISimHostPathfindingTranslators
{
    public void RegisterOn(Fdp.ModuleHost.ModuleHostKernel kernel) { }
    public void Dispose() { }
}

/// <summary>No-op stub for ISimHostPerceptionTranslators.</summary>
internal sealed class NullSimHostPerceptionTranslators : ISimHostPerceptionTranslators
{
    public void RegisterOn(Fdp.ModuleHost.ModuleHostKernel kernel) { }
    public void Dispose() { }
}

/// <summary>No-op stub for ICommandGateway until TASK-P4-001 wires the real implementation.</summary>
internal sealed class NullCommandGateway : ICommandGateway
{
    public Task<int> CreateEntityAsync(CreateEntityCommand cmd, CancellationToken ct = default)
        => Task.FromResult(0);
    public Task SendUpdateDescriptorAsync(UpdateEntityDescriptorCommand cmd, CancellationToken ct = default)
        => Task.CompletedTask;
    public Task<MissionCommitResult> SendMissionControlRequestAsync(MissionControlCommand cmd, CancellationToken ct = default)
        => Task.FromResult(new MissionCommitResult { Success = false, ErrorMessage = "No gateway" });
        public Task SendUpdateAttributeAsync(Fdp.Toolkit.Replication.Events.UpdateEntityAttributeCommand cmd, CancellationToken ct = default)
        => Task.CompletedTask;
    public void Dispose() { }
}

/// <summary>No-op stub for IExConEgressWriters until TASK-P4-001 wires the real implementation.</summary>
internal sealed class NullExConEgressWriters : IExConEgressWriters
{
    public void WriteMapConfig(MapConfigDto config) { }
    public void WriteDeleteEntity(int entityId) { }
    public void WriteCreateEntity(CreateEntityCommand cmd) { }
    public void WriteMapCommand(MapCommandDto cmd) { }
    public void PushContextActions(int mapGroupId, System.Collections.Generic.IReadOnlyList<int>? forSelection, string actionsJson) { }
    public void Dispose() { }
}

/// <summary>No-op stub for ITimeControlGateway until TASK-P4-001 wires the real implementation.</summary>
internal sealed class NullTimeControlGateway : ITimeControlGateway
{
    public void RequestPause() { }
    public void RequestResume() { }
    public void RequestStep() { }
    public void SetTimeScale(float scale) { }
}
