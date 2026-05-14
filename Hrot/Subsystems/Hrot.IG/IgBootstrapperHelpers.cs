using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Services;

namespace Hrot.IG;

/// <summary>
/// Handles <see cref="DestroyEntityCommand"/> events (published when SimHost sends
/// EntityMaster DISPOSE) by unregistering and destroying the local ghost entity.
/// Replaces <see cref="Fdp.Toolkit.NetworkSpawning.Systems.SpawningModule"/> so the IG no longer
/// acts as an authoritative spawner and thus avoids duplicate local entities.
/// </summary>
[UpdateInPhase(SystemPhase.PostSimulation)]
internal sealed class GhostDestructionSystem : IEcsModuleSystem
{
    private readonly NetworkEntityMap _entityMap;

    public GhostDestructionSystem(NetworkEntityMap entityMap)
    {
        _entityMap = entityMap;
    }

    public void Execute(ISimulationView view, float dt)
    {
        var world = view as EntityRepository;
        if (world == null) return;

        foreach (var cmd in view.ReadManagedEvents<DestroyEntityCommand>())
        {
            if (_entityMap.TryGetEntity(cmd.NetworkId, out var entity))
            {
                _entityMap.Unregister(cmd.NetworkId, view.Tick);
                if (world.IsAlive(entity))
                    world.DestroyEntity(entity);
            }
        }
    }
}

// IEcsModule wrapper that routes UnitHierarchySystem into the Simulation phase slot.
// RegisterGlobalSystem rejects SystemPhase.Simulation; it must be registered via RegisterModule.
internal sealed class IgUnitHierarchyModule : IEcsModule
{
    private readonly IEcsModuleSystem _system;
    public string Name => "IgUnitHierarchy";
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();
    public IgUnitHierarchyModule(IEcsModuleSystem system) => _system = system;
    public void RegisterSystems(ISystemRegistry registry) => registry.RegisterSystem(_system);
    public void Tick(ISimulationView view, float deltaTime) { }
}
