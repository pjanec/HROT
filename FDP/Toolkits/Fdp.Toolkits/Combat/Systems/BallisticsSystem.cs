using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Contracts;
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

            // ⭐ R-217 — the resident terrain the rounds fly through (immutable, swapped by reference); none ⇒ no terrain, as before.
            var terrain = repo.HasSingletonManaged<global::Fdp.Toolkit.Terrain.TerrainWorld>()
                ? repo.GetSingletonManaged<global::Fdp.Toolkit.Terrain.TerrainWorld>() : null;
            // ⭐ R-219 — the doors as THIS view sees them (built once per tick from the door entities)
            var doors = terrain != null && terrain.Doors.Count > 0 ? global::Fdp.Toolkit.Terrain.DoorStates.Of(view, terrain) : null;

            var shots = ShotLog.Peek(repo);   // ⭐ T-4 — rounds fired through FireProcessingSystem have a record

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
                var shot = shots?.Of(entity);

                // ── ⭐ Stage 6 (CE-1032, W-4) — a warhead round lying where it landed waits for its time fuze, then bursts.
                if ((proj.Warhead & WarheadRound.Landed) != 0)
                {
                    proj.FuzeRemaining -= deltaTime;
                    if (proj.FuzeRemaining <= 0f)
                    {
                        Detonate(repo, in proj, repo.GetComponent<SimTransform>(entity).Position, shot, currentTick);
                        repo.DestroyEntity(entity);
                    }
                    continue;
                }

                // ── 0. ⭐ R-217 — a round stopped by the terrain stays frozen at the wall for a grace period, so the raycasts of its
                //   last segments (three ticks in flight) still resolve — a unit IN FRONT of the wall is still struck — then goes.
                if (proj.StoppedTick != 0)
                {
                    if (currentTick - proj.StoppedTick >= CombatConstants.StoppedRoundGraceTicks)
                    {
                        if (shot != null && (proj.Warhead & WarheadRound.Spent) == 0)
                            ShotLog.EndCarried(shot, ShotOutcome.StoppedByTerrain, currentTick, proj.PreviousPosition, terrain, in proj, doors);
                        repo.DestroyEntity(entity);
                    }
                    continue;
                }

                // ── 1. Lifetime check ────────────────────────────────────────────
                // Unsigned subtraction handles tick-counter wrap correctly.
                // ⭐ CE-1032 (W-4) — a time fuze running out in flight bursts the round where it is.
                if ((proj.Warhead & WarheadRound.TimeFuze) != 0)
                {
                    proj.FuzeRemaining -= deltaTime;
                    if (proj.FuzeRemaining <= 0f)
                    {
                        Detonate(repo, in proj, repo.GetComponent<SimTransform>(entity).Position, shot, currentTick);
                        repo.DestroyEntity(entity);
                        continue;
                    }
                }

                // ⭐ CE-1032 (W-8) — an arc round falls: gravity on its velocity (LinearKinematicsSystem integrates it).
                if ((proj.Warhead & WarheadRound.Arc) != 0 && repo.HasComponent<SimVelocity>(entity))
                    repo.GetComponentRW<SimVelocity>(entity).Linear.Z -= CombatConstants.Gravity * deltaTime;

                uint lifetime = (proj.Warhead & (WarheadRound.Arc | WarheadRound.TimeFuze)) != 0
                    ? CombatConstants.ArcRoundLifetimeTicks : CombatConstants.BulletLifetimeTicks;
                if (currentTick - proj.SpawnTick >= lifetime)
                {
                    if (shot != null) ShotLog.EndCarried(shot, ShotOutcome.Expired, currentTick, repo.GetComponent<SimTransform>(entity).Position, terrain, in proj, doors);
                    repo.DestroyEntity(entity);
                    continue;   // do NOT submit a raycast for a just-destroyed bullet
                }

                // ── 2. Submit swept-segment raycast via event bus ─────────────────
                var tf = repo.GetComponent<SimTransform>(entity);

                // ── 2a. ⭐⭐ Buildings §3d P2 (R-217) — the segment through the TERRAIN: walls, fences and floors resist by the armour
                //   rule (the round's FRONT values, carried segment by segment); a crossing the round cannot pass ENDS the segment
                //   there — no unit beyond it is struck — and freezes the round at the wall. The damage a unit takes is NOT decided
                //   here: HitResolutionSystem carries the round from its muzzle to the unit, whenever the raycast resolves.
                var end = tf.Position;
                if (terrain != null)
                {
                    if ((proj.TerrainFlags & 1) == 0)
                    {
                        proj.Muzzle = proj.PreviousPosition; proj.FrontDamage = proj.Damage; proj.FrontPenetration = proj.Penetration;
                        proj.TerrainFlags |= 1;
                    }
                    bool stopped = TerrainPenetration.Carry(terrain, proj.PreviousPosition, tf.Position, ref proj.FrontDamage, ref proj.FrontPenetration, out float stopT, null, doors);
                    if (stopped)
                    {
                        end = System.Numerics.Vector3.Lerp(proj.PreviousPosition, tf.Position, stopT);
                        proj.StoppedTick = currentTick == 0 ? 1u : currentTick;
                        ref var at = ref repo.GetComponentRW<SimTransform>(entity);
                        at.Position = end;
                        if (repo.HasComponent<SimVelocity>(entity)) repo.GetComponentRW<SimVelocity>(entity).Linear = System.Numerics.Vector3.Zero;
                    }
                }

                // ── 2b′. ⭐⭐ Stage 6 (CE-1032, W-3/W-4) — a WARHEAD round that stops (on the terrain, or on the flat ground — which the
                //   terrain query does not count) does not just end: on an impact fuze it BURSTS there, a little back along its flight
                //   so the surface it struck stands between the burst and what is behind it (a roof over a room); on a time fuze it
                //   LANDS and waits. Its last raycast still goes out (a unit in the way is struck first — a contact burst).
                if ((proj.Warhead & WarheadRound.Area) != 0)
                {
                    float groundZ = terrain?.GroundZ ?? 0f;
                    bool stoppedHere = proj.StoppedTick == (currentTick == 0 ? 1u : currentTick);
                    if (!stoppedHere && end.Z < groundZ && proj.PreviousPosition.Z >= groundZ)
                    {
                        float t = (proj.PreviousPosition.Z - groundZ) / (proj.PreviousPosition.Z - end.Z);
                        end = System.Numerics.Vector3.Lerp(proj.PreviousPosition, end, t);
                        proj.StoppedTick = currentTick == 0 ? 1u : currentTick;
                        ref var at = ref repo.GetComponentRW<SimTransform>(entity);
                        at.Position = end;
                        if (repo.HasComponent<SimVelocity>(entity)) repo.GetComponentRW<SimVelocity>(entity).Linear = System.Numerics.Vector3.Zero;
                        stoppedHere = true;
                    }
                    if (stoppedHere)
                    {
                        var back = proj.PreviousPosition - end;
                        var burst = back.LengthSquared() > 1e-8f
                            ? end + System.Numerics.Vector3.Normalize(back) * AreaEffect.BurstStandOffMetres : end;
                        if ((proj.Warhead & WarheadRound.TimeFuze) != 0)
                        {
                            proj.Warhead |= WarheadRound.Landed;
                            repo.GetComponentRW<SimTransform>(entity).Position = burst;
                        }
                        else
                        {
                            Detonate(repo, in proj, burst, shot, currentTick);
                            proj.Warhead |= WarheadRound.Spent;
                        }
                    }
                }

                cmd.PublishEvent(new RaycastRequestEvent
                {
                    Start        = proj.PreviousPosition,
                    End          = end,
                    RayId        = PhysicsConstants.PackBulletRayId(entity.Index),
                    LayerMask    = ~CombatConstants.BulletCollisionLayer,  // hit everything except other bullets
                    IgnoreEntity = proj.Shooter,
                });

                // ── 2b. ⭐ CE-3064 (R-206) — near misses along the same swept segment ──
                if (nearMissGrid.HasValue) ReportNearMisses(repo, cmd, nearMissGrid.Value, ref proj, proj.PreviousPosition, end);

                // ── 3. Update PreviousPosition ───────────────────────────────────
                // Record the bullet's current position so the next frame's raycast
                // sweeps the correct segment (after LinearKinematicsSystem advances it).
                proj.PreviousPosition = end;
            }
        }

        /// <summary>
        /// ⭐ Stage 6 (<c>CE-1032</c>, W-3) — an off-target burst: a <see cref="DetonationNotification"/> with no target, carrying the
        /// munition (the area effect reads its warhead from it, next frame's Simulation) — and the end of the shot's record.
        /// </summary>
        private static void Detonate(EntityRepository repo, in BallisticProjectile proj, System.Numerics.Vector3 at, ShotRecord? shot, uint tick)
        {
            repo.Bus.Publish(new DetonationNotification
            {
                Shooter = proj.Shooter, Target = Entity.Null, HitX = at.X, HitY = at.Y, HitZ = at.Z, Ammo = proj.Ammo,
            });
            if (shot != null) ShotLog.End(shot, ShotOutcome.Detonated, tick, at);
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
