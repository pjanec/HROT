using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Physics.Math;
using Fdp.Toolkit.Physics.Systems;
using Fdp.Toolkit.World;

namespace Fdp.Toolkit.Motion
{
    /// <summary>
    /// ⭐⭐ M7 (docs/designs/vehicle-tactics/DESIGN_FPV_Drone.md §2, §3) — a flyer MEETS what it flies into: each frame its swept segment
    /// (where it was → where it is, from its velocity) against every body's collider — the M1 cylinder (circle + height band) grown by the
    /// flyer's <c>ContactRadiusM</c> — and the terrain (<see cref="IWorldQuery.SightBlocked"/>, the ground). A LIVE flyer that meets
    /// something publishes the <see cref="DetonationNotification"/> of its warhead mount (mount 0: its munition's penetration and damage,
    /// <see cref="CombatTkb.RoundOf"/>; the travel = its velocity, so a dive arrives on the Top face) and a lethal
    /// <see cref="DamageAssessedEvent"/> on itself — the rest is the existing damage chain. A DEAD one comes to rest on the ground. Whoever
    /// flies it, AI or an operator. Runs on the node that moves it (after <see cref="AirMotionSystem"/>).
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public sealed class DroneImpactSystem : IEcsModuleSystem
    {
        /// <summary>Bodies farther than this from the flyer (plus their own radius) are not tested.</summary>
        public const float NearRadiusM = 10f;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<FlightState>() || deltaTime <= 0f) return;
            var world = WorldQuery.Of(repo);
            bool colliders = repo.IsComponentTypeRegistered<PhysicsCollider>();
            bool rounds = repo.IsComponentTypeRegistered<BallisticProjectile>();
            bool health = repo.IsComponentTypeRegistered<Health>();
            bool statuses = repo.IsComponentTypeRegistered<NavigationStatus>();
            var flyers = repo.Query().With<FlightState>().With<FlightParams>().With<SimTransform>().WithOwned<SimTransform>().With<SimVelocity>().Build();
            foreach (var d in flyers)
            {
                if (repo.GetComponentRO<FlightState>(d).Airborne == 0) continue;
                // ⭐ Air Ops slice 2 — a controlled take-off or landing meets the ground (and whoever waits beside it) on purpose
                if (statuses && repo.HasComponent<NavigationStatus>(d) && IsGroundPhase(repo.GetComponentRO<NavigationStatus>(d).Phase)) continue;
                var v = repo.GetComponentRO<SimVelocity>(d).Linear;
                if (v.LengthSquared() < 1e-8f) continue;
                var p1 = repo.GetComponentRO<SimTransform>(d).Position;
                var p0 = p1 - v * deltaTime;
                float contact = repo.GetComponentRO<FlightParams>(d).ContactRadiusM;
                bool alive = !health || !repo.HasComponent<Health>(d) || repo.GetComponentRO<Health>(d).Current > 0f;

                // ── the bodies: the first one the segment enters ──
                float bestT = float.MaxValue; Entity hit = Entity.Null;
                if (colliders)
                    foreach (var b in repo.Query().With<PhysicsCollider>().With<SimTransform>().Build())
                    {
                        if (b == d || (rounds && repo.HasComponent<BallisticProjectile>(b))) continue;
                        var c = repo.GetComponentRO<PhysicsCollider>(b);
                        var bp = repo.GetComponentRO<SimTransform>(b).Position;
                        float r = c.Radius + contact;
                        if (Vector2.Distance(new Vector2(bp.X, bp.Y), new Vector2(p1.X, p1.Y)) > NearRadiusM + r) continue;
                        if (!Intersection2D.SegmentInCircle(new Vector2(p0.X, p0.Y), new Vector2(p1.X, p1.Y), new Vector2(bp.X, bp.Y), r,
                                                            out float tIn, out float tOut)) continue;
                        float top = bp.Z + (c.Height > 0f ? c.Height : 2f * c.Radius);
                        if (!RaycastSolverSystem.ZBandOverlap(p0.Z, p1.Z, bp.Z - contact, top + contact, ref tIn, ref tOut)) continue;
                        if (tIn < bestT) { bestT = tIn; hit = b; }
                    }

                // ── the terrain: a wall, a roof or a deck between the two points, or the ground under it ── ⭐ C2 (handoff 2): whichever
                //   comes FIRST along the segment wins — a deck over a parked tank stops the dive even when the same frame's segment also
                //   enters the hull's band behind it (measured: at a 0.1 s frame the hull won, the deck was flown through).
                float terrainT = float.MaxValue, groundZ = float.NegativeInfinity;
                if (world != null)
                {
                    groundZ = world.GroundHeightAt(p1.X, p1.Y);
                    if (world.SightBlocked(p0, p1)) terrainT = FirstBlocked(world, p0, p1);
                    if (p1.Z - contact <= groundZ)
                    {
                        float drop = p0.Z - p1.Z;
                        float tGround = drop > 1e-6f ? Math.Clamp((p0.Z - contact - groundZ) / drop, 0f, 1f) : 1f;
                        terrainT = MathF.Min(terrainT, tGround);
                    }
                }
                if (!hit.IsNull && terrainT <= bestT) hit = Entity.Null;
                if (hit.IsNull && terrainT == float.MaxValue) continue;

                var at = hit.IsNull
                    ? p0 + (p1 - p0) * terrainT
                    : p0 + (p1 - p0) * Math.Clamp(bestT, 0f, 1f);
                if (hit.IsNull && at.Z < groundZ) at.Z = groundZ;
                if (alive)
                {
                    var mount = CombatTkb.MountOf(repo, d, 0);
                    if (mount != null && mount.AmmoGuid != 0)
                    {
                        long ammo = unchecked((long)mount.AmmoGuid);
                        var (pen, dmg) = CombatTkb.RoundOf(repo, mount, ammo);
                        repo.Bus.Publish(new DetonationNotification
                        {
                            Shooter = d, Target = hit, HitX = at.X, HitY = at.Y, HitZ = at.Z, Penetration = pen, Damage = dmg, Ammo = ammo,
                            TravelX = v.X, TravelY = v.Y, TravelZ = v.Z,
                        });
                    }
                    float max = health && repo.HasComponent<Health>(d) ? MathF.Max(1f, repo.GetComponentRO<Health>(d).Max) : 1f;
                    repo.Bus.Publish(new DamageAssessedEvent { HitEntity = d, TotalDamage = max * 10f });   // spent
                }

                // it stops where it met something (a wreck at rest)
                ref var tf = ref repo.GetComponentRW<SimTransform>(d);
                tf.Position = at;
                repo.GetComponentRW<SimVelocity>(d).Linear = Vector3.Zero;
                ref var fs = ref repo.GetComponentRW<FlightState>(d);
                fs.Airborne = 0;
                fs.VerticalSpeed = 0f;
            }
        }

        /// <summary>The route phases that touch the ground on purpose (<see cref="RotorcraftModel"/>).</summary>
        public static bool IsGroundPhase(NavigationPhase phase)
            => phase is NavigationPhase.TakingOff or NavigationPhase.Landing or NavigationPhase.Landed
                      or NavigationPhase.Unloading or NavigationPhase.Loading;

        /// <summary>The fraction along p0→p1 where the terrain first blocks it (bisected on <see cref="IWorldQuery.SightBlocked"/>, ~2 cm on
        /// a 30 m/s frame); the caller knows the whole segment is blocked.</summary>
        private static float FirstBlocked(IWorldQuery world, Vector3 p0, Vector3 p1)
        {
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 10; i++)
            {
                float mid = 0.5f * (lo + hi);
                if (world.SightBlocked(p0, p0 + (p1 - p0) * mid)) hi = mid; else lo = mid;
            }
            return hi;
        }
    }
}
