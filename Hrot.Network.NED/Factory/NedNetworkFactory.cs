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
        // NedCommandGateway moved from Hrot.Map.Common to Hrot.Network.NED
        // Return null implementation until full wiring is done in TASK-P4-001
        return new NullCommandGateway();
    }

    /// <inheritdoc/>
    public IExConEgressWriters CreateExConEgressWriters()
    {
        // Return null implementation until full wiring is done in TASK-P4-001
        return new NullExConEgressWriters();
    }
}

/// <summary>No-op stub for ICommandGateway until TASK-P4-001 wires the real implementation.</summary>
internal sealed class NullCommandGateway : ICommandGateway
{
    public Task<int> CreateEntityAsync(CreateEntityCommand cmd, CancellationToken ct = default)
        => Task.FromResult(0);
    public Task SendUpdateDescriptorAsync(UpdateEntityDescriptorCommand cmd, CancellationToken ct = default)
        => Task.CompletedTask;
    public Task SendMissionControlRequestAsync(MissionControlCommand cmd, CancellationToken ct = default)
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
    public void Dispose() { }
}
