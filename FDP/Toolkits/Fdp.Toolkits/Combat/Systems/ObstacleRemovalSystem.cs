using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Terrain;

namespace Fdp.Toolkit.Combat.Systems
{
    /// <summary>
    /// ⭐⭐ <c>CE-6003</c> (city battle O-1; 📄 docs/DESIGN_Peek_And_Fire.md §9.9) — a destructible STATIC OBSTACLE whose <see cref="Health"/> is
    /// gone is REMOVED, by the node that owns that Health (<see cref="HealthOwnership.AppliesHere"/>), through the ONE teardown: a
    /// <c>DestroyEntityCommand</c> (Entity Creation §3.4c — the ELM tears it down over frames, its dispose removes every replica; a node that does not
    /// own the master forwards it as a <c>DeleteEntityRequest</c>). An offline world (no network identity) destroys it through the command buffer.
    /// Every node's <c>StaticObstacleBakeSystem</c> then sees the obstacle set change and rebakes world + cover + navmesh tiles without it.
    /// <para>⛔ Obstacles ONLY: a dead body or a dead vehicle stays in the world (the CE-267 revert, user ruling <c>2026-09-13</c>) — an obstacle is
    /// terrain, and its removal is mandatory (user, <c>2026-11-13</c>, D-6: <i>"removal mandatory (even as disappearing)"</i>).</para>
    /// <para>⭐ The parallel contract (CLAUDE.md "BEHAVIOUR CODE RUNS IN PARALLEL"): an obstacle changes only ITSELF (it asks for its own teardown —
    /// a command, never a direct world delete mid-iteration), no statics, no shared scratch, no randomness, no clock; a cached query and a counter are
    /// its only state. ⭐ Steady state allocates 0 B (no LINQ, a struct query enumerator). In <c>CoreInfrastructureCapabilities.HealthApplication</c>,
    /// right after <c>HealthApplicationSystem</c> on every live-simulation node.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class ObstacleRemovalSystem : IEcsModuleSystem
    {
        private EntityRepository? _for;
        private EntityQuery? _query;

        /// <summary>Removals this node asked for (a rail and a diagnostics counter read it).</summary>
        public long Removed { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;   // a replay: the recording owns the world (the removal is in it)
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<StaticObstacle>() || !repo.IsComponentTypeRegistered<Health>()) return;
            if (!ReferenceEquals(repo, _for) || _query == null)
            {
                _query = repo.Query().With<StaticObstacle>().With<Health>().Build();   // Active only: a Constructing obstacle is not in the simulation yet (R-243)
                _for = repo;
            }
            foreach (var e in _query)
            {
                if (repo.GetComponentRO<Health>(e).Current > 0f || !HealthOwnership.AppliesHere(repo, e)) continue;
                Remove(repo, view, e);
                Removed++;
            }
        }

        private static void Remove(EntityRepository repo, ISimulationView view, Entity e)
        {
            if (repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkIdentity>()
                && repo.HasComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(e)
                && repo.Bus.IsRegisteredManaged<Fdp.Toolkit.NetworkSpawning.Events.DestroyEntityCommand>())
            {
                repo.Bus.PublishManaged(new Fdp.Toolkit.NetworkSpawning.Events.DestroyEntityCommand
                {
                    NetworkId = repo.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkIdentity>(e).Value,
                    Reason    = "Obstacle destroyed",
                });
                // ⭐ marked TearDown at once, as the teardown's own first step does (NetworkSpawningSystem.ProcessDestroy): it leaves this system's
                //   query (asked ONCE) and the obstacle bake's set (only Active / Constructing are terrain) from the next frame
                view.GetCommandBuffer().SetLifecycleState(e, EntityLifecycle.TearDown);
                return;
            }
            view.GetCommandBuffer().DestroyEntity(e);
        }
    }
}
