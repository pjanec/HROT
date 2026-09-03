using System;
using Fdp.Core;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Replication.Systems
{
    /// <summary>
    /// ⭐⭐ <b>Keeps <see cref="NetworkEntityMap"/> in step with the world — the ONE place either pruning
    /// happens.</b> Registered by every replicating host *(<c>NedReplicationModule</c>,
    /// <c>ReplicationLogicModule</c>)*.
    ///
    /// <para>⭐ <b>Two prunes, one tick, deliberately.</b> <see cref="NetworkEntityMap.PruneDeadEntities"/>
    /// moves the ids of destroyed entities into the graveyard;
    /// <see cref="NetworkEntityMap.PruneGraveyard"/> retires them once they are older than the map's
    /// graveyard window. ⛔ Splitting them across two systems is how the second one ends up with no caller
    /// at all — which is exactly what had happened: <c>PruneGraveyard</c> had <b>zero production callers</b>
    /// until <c>2026-09-03</c>, so the graveyard list <b>only ever grew</b>.</para>
    ///
    /// <para>⚠ <b>Stated honestly: the payoff today is BOUNDED MEMORY, not corrected behaviour.</b> 📐
    /// <c>NetworkEntityMap.IsGraveyard</c> has <b>zero production readers</b> (tests only), so nothing
    /// observes the window — ⛔ which is also why the clock mismatch in <c>PruneDeadEntities</c> had never
    /// bitten, and why it is fixed in the same change rather than left for the first reader to discover.</para>
    ///
    /// <para>📄 <c>docs/DESIGN_Entity_Creation_Unification.md</c> §3.4c ③–④.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public class DisposalMonitoringSystem : IEcsModuleSystem
    {
        private readonly NetworkEntityMap _entityMap;

        public DisposalMonitoringSystem(NetworkEntityMap entityMap)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        public void Execute(ISimulationView view, float dt)
        {
            // Main-thread PostSimulation: view is the live EntityRepository.
            if (view is EntityRepository repo)
            {
                // Order matters: entities that died THIS frame must reach the graveyard before it is
                // aged, or they would be retired a full window early on the following tick.
                _entityMap.PruneDeadEntities(repo);
                _entityMap.PruneGraveyard(repo.SimulationTick);
            }
        }
    }
}
