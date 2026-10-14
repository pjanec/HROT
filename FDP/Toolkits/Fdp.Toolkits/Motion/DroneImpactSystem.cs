using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
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
            var flyers = repo.Query().With<FlightState>().With<FlightParams>().With<SimTransform>().WithOwned<SimTransform>().With<SimVelocity>().Build();
            foreach (var d in flyers)
            {
                if (repo.GetComponentRO<FlightState>(d).Airborne == 0) continue;
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

                // ── the terrain: a wall or a roof between the two points, or the ground under it ──
                bool ground = false; float groundZ = float.NegativeInfinity;
                if (hit.IsNull && world != null)
                {
                    groundZ = world.GroundHeightAt(p1.X, p1.Y);
                    ground = p1.Z - contact <= groundZ || world.SightBlocked(p0, p1);
                }
                if (hit.IsNull && !ground) continue;

                var at = hit.IsNull ? new Vector3(p1.X, p1.Y, MathF.Max(p1.Z, groundZ)) : p0 + (p1 - p0) * Math.Clamp(bestT, 0f, 1f);
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
    }
}
