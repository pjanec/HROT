using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Lifecycle;
using Hrot.Common;
using Hrot.Common.Abstractions;
using Hrot.Core.Network;
using Hrot.Map.Common;
using Hrot.Network.NED.SimHost;
using Hrot.Network.Replication;
using NetworkEntityMap = FDP.Toolkit.Replication.Services.NetworkEntityMap;

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
    private readonly int                  _localNodeId;
    private readonly NodeRole             _role;
    private readonly ITkbDatabase?        _tkbDb;
    private readonly EntityLifecycleModule? _lifecycleModule;
    private readonly DoctrineRegistry?    _doctrineRegistry;

    public NedNetworkFactory(
        DdsParticipant?       participant,
        NetworkEntityMap      entityMap,
        IGeographicTransform  geoTransform,
        FdpEventBus           eventBus,
        int                   localNodeId,
        NodeRole              role,
        ITkbDatabase?         tkbDb            = null,
        EntityLifecycleModule? lifecycleModule  = null,
        DoctrineRegistry?     doctrineRegistry = null)
    {
        _participant      = participant;
        _entityMap        = entityMap;
        _geoTransform     = geoTransform;
        _eventBus         = eventBus;
        _localNodeId      = localNodeId;
        _role             = role;
        _tkbDb            = tkbDb;
        _lifecycleModule  = lifecycleModule;
        _doctrineRegistry = doctrineRegistry;
    }

    /// <inheritdoc/>
    public IReplicationModule CreateReplicationModule()
        => new NedReplicationModule(
               participant:       _participant,
               role:              _role,
               entityMap:         _entityMap,
               geoTransform:      _geoTransform,
               eventBus:          _eventBus,
               localNodeId:       _localNodeId,
               domainId:          0,
               tkbDb:             _tkbDb,
               lifecycleModule:   _lifecycleModule,
               doctrineRegistry:  _doctrineRegistry);

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
    public ISimHostPathfindingTranslators CreateSimHostPathfindingTranslators()
    {
        if (_participant == null) return new NullSimHostPathfindingTranslators();
        return new NedSimHostPathfindingTranslators(_participant, _entityMap, _geoTransform, _role);
    }

    /// <inheritdoc/>
    public ISimHostPerceptionTranslators CreateSimHostPerceptionTranslators()
    {
        if (_participant == null) return new NullSimHostPerceptionTranslators();
        return new NedSimHostPerceptionTranslators(_participant, _entityMap, _geoTransform, _role);
    }

    /// <inheritdoc/>
    public IIgTranslators CreateIgTranslators()
        => new Hrot.Network.NED.IG.NedIgTranslators();
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
    public void RegisterOn(ModuleHost.Core.ModuleHostKernel kernel) { }
    public void Dispose() { }
}

/// <summary>No-op stub for ISimHostPathfindingTranslators.</summary>
internal sealed class NullSimHostPathfindingTranslators : ISimHostPathfindingTranslators
{
    public void RegisterOn(ModuleHost.Core.ModuleHostKernel kernel) { }
    public void Dispose() { }
}

/// <summary>No-op stub for ISimHostPerceptionTranslators.</summary>
internal sealed class NullSimHostPerceptionTranslators : ISimHostPerceptionTranslators
{
    public void RegisterOn(ModuleHost.Core.ModuleHostKernel kernel) { }
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
