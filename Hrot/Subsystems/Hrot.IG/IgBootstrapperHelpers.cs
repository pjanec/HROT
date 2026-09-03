using Fdp.ModuleHost.Abstractions;

namespace Hrot.IG;

// ⛔⛔⛔ GhostDestructionSystem was DELETED here on 2026-09-03 (CE-144, option A).
//
// It consumed DestroyEntityCommand and destroyed the entity immediately, WITHOUT the ELM. Measured
// consequence: the wire dispose is written by CycloneNetworkCleanupSystem (phase Export) on a
// DestructionOrder event, and only EntityLifecycleModule.BeginDestruction publishes that — so a destroy
// that skipped the ELM emitted no dispose sample at all, and peers kept the instance forever for any
// entity IG owned. It failed silently and in the worse direction.
//
// It was written when IG had no NetworkSpawningSystem ("so the IG no longer acts as an authoritative
// spawner"); IG now composes EntityCreationPack and schedules the shared spawn system, whose
// ProcessDestroy is the ONE consumer on every host. Map un-registration is handled by the shared
// DisposalMonitoringSystem, which is what this system was doing eagerly.
//
// 📄 docs/DESIGN_Entity_Creation_Unification.md §3.4c.

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
