using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Physics.Systems;
namespace Fdp.Toolkit.Combat.Systems
{
    /// <summary>
    /// Per-frame housekeeping for all live bullet entities.
    /// <para>
    /// <b>Execution phase:</b> <see cref="PostSimulationSystemGroup"/>, <b>after</b>
    /// <c>LinearKinematicsSystem</c> (which advances positions) and before
    /// <c>SpatialHashSystem</c> (which rebuilds the grid).
    /// </para>
    /// <para>
    /// <b>Execution order rationale (Phase 0 Adaptation):</b><br/>
    /// Bullet movement is delegated to <c>LinearKinematicsSystem</c> which runs
    /// <c>pos += vel * dt</c>.  BallisticsSystem must run <em>before</em> that, so:
    /// <list type="number">
    ///   <item>
    ///     The raycast segment <c>Start = PreviousPosition, End = SimTransform.Position</c>
    ///     covers exactly the distance traversed in the <em>previous</em> frame.
    ///   </item>
    ///   <item>
    ///     <c>PreviousPosition</c> is then updated to <c>SimTransform.Position</c> so the
    ///     next frame's segment picks up from the correct starting point.
    ///   </item>
    /// </list>
    /// Required ordering: BallisticsSystem → LinearKinematicsSystem → RaycastSolverSystem.
    /// </para>
    /// <para>
    /// <b>Capacity guard (DEBT-021 pattern):</b> The batch is never written beyond
    /// <see cref="PhysicsConstants.RaycastBatchCapacity"/>; excess bullets are silently
    /// skipped this frame (no crash, no exception).
    /// </para>
    /// <!-- [UpdateBefore(typeof(LinearKinematicsSystem))] — LinearKinematicsSystem is not yet
    ///      defined in a referenceable assembly (Phase 0). Attribute will be added once the class
    ///      is introduced. Ordering is maintained by the host application's registration order. -->
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    // [UpdateAfter(typeof(HitResolutionSystem))] — ordering maintained by array position in CombatModule.
    public class BallisticsSystem : IEcsModuleSystem
    {
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (deltaTime <= 0f) return;

            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(BallisticsSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            uint currentTick = repo.HasSingleton<GlobalTime>()
                ? (uint)repo.GetSingleton<GlobalTime>().FrameNumber
                : 0u;

            var cmd = view.GetCommandBuffer();

            // ⭐ CE-3064 — the unit grid the raycast solver uses; no grid or no NearMissEvent registered ⇒ no near-miss sensing.
            global::CarKinem.Spatial.SpatialHashGrid? nearMissGrid =
                repo.HasSingleton<global::CarKinem.Spatial.SpatialGridData>() && repo.Bus.IsRegistered<Events.NearMissEvent>()
                    ? repo.GetSingleton<global::CarKinem.Spatial.SpatialGridData>().Grid : null;

            var query = repo.Query()
                .With<BallisticProjectile>()
                .With<SimTransform>()
                .WithLifecycle(EntityLifecycle.All)
                .Build();

            foreach (var entity in query)
            {
                if (repo.GetLifecycleState(entity) == EntityLifecycle.TearDown)
                {
                    repo.DestroyEntity(entity);
                    continue;
                }

                ref var proj = ref repo.GetComponentRW<BallisticProjectile>(entity);

                // ── 1. Lifetime check ────────────────────────────────────────────
                // Unsigned subtraction handles tick-counter wrap correctly.
                if (currentTick - proj.SpawnTick >= CombatConstants.BulletLifetimeTicks)
                {
                    repo.DestroyEntity(entity);
                    continue;   // do NOT submit a raycast for a just-destroyed bullet
                }

                // ── 2. Submit swept-segment raycast via event bus ─────────────────
                var tf = repo.GetComponent<SimTransform>(entity);

                cmd.PublishEvent(new RaycastRequestEvent
                {
                    Start        = proj.PreviousPosition,
                    End          = tf.Position,
                    RayId        = PhysicsConstants.PackBulletRayId(entity.Index),
                    LayerMask    = ~CombatConstants.BulletCollisionLayer,  // hit everything except other bullets
                    IgnoreEntity = proj.Shooter,
                });

                // ── 2b. ⭐ CE-3064 (R-206) — near misses along the same swept segment ──
                if (nearMissGrid.HasValue) ReportNearMisses(repo, cmd, nearMissGrid.Value, ref proj, proj.PreviousPosition, tf.Position);

                // ── 3. Update PreviousPosition ───────────────────────────────────
                // Record the bullet's current position so the next frame's raycast
                // sweeps the correct segment (after LinearKinematicsSystem advances it).
                proj.PreviousPosition = tf.Position;
            }
        }

        /// <summary>
        /// ⭐ <c>CE-3064</c> — every unit within <see cref="CombatConstants.NearMissRadius"/> of the round's segment this tick, other
        /// than the shooter and the shooter's own force, gets ONE <see cref="Events.NearMissEvent"/> per pass (a round lingering
        /// near the same unit over two ticks reports it once). A round that HITS the unit also reports a near miss — harmless:
        /// both mean "fired upon".
        /// </summary>
        private static void ReportNearMisses(EntityRepository repo, Fdp.Interfaces.IEntityCommandBuffer cmd, global::CarKinem.Spatial.SpatialHashGrid grid,
                                             ref BallisticProjectile proj, System.Numerics.Vector3 start, System.Numerics.Vector3 end)
        {
            var a = new System.Numerics.Vector2(start.X, start.Y);
            var b = new System.Numerics.Vector2(end.X, end.Y);
            float half = System.Numerics.Vector2.Distance(a, b) * 0.5f;
            int shooterForce = repo.IsAlive(proj.Shooter) && repo.HasComponent<EntityInfo>(proj.Shooter)
                ? (int)repo.GetComponentRO<EntityInfo>(proj.Shooter).ForceId : -1;

            Span<(Entity entity, System.Numerics.Vector2 pos)> near = stackalloc (Entity, System.Numerics.Vector2)[64];
            int n = grid.QueryNeighbors((a + b) * 0.5f, half + CombatConstants.NearMissRadius, near);
            for (int i = 0; i < n; i++)
            {
                var unit = near[i].entity;
                if (unit == proj.Shooter || unit == proj.LastNearMiss || !repo.IsAlive(unit) || !repo.HasComponent<EntityInfo>(unit)) continue;
                if ((int)repo.GetComponentRO<EntityInfo>(unit).ForceId == shooterForce) continue;   // own side's rounds are not "shot at"
                var p = near[i].pos;
                var ab = b - a;
                float t = ab.LengthSquared() > 0f ? Math.Clamp(System.Numerics.Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f) : 0f;
                var closest = a + ab * t;
                if (System.Numerics.Vector2.DistanceSquared(p, closest) > CombatConstants.NearMissRadius * CombatConstants.NearMissRadius) continue;
                float z = start.Z + (end.Z - start.Z) * t;
                cmd.PublishEvent(new Events.NearMissEvent { Unit = unit, X = closest.X, Y = closest.Y, Z = z });
                HitModel.StampUnderFire(repo, unit, HitModel.Now(repo));   // ⭐ AQ85 E — a near miss suppresses (spoils its aim)
                proj.LastNearMiss = unit;
            }
        }
    }
}
