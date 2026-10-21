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
    /// something — when it IS a munition (<see cref="FlightParams.DetonatesOnImpact"/>, R-3007; any other aircraft just crashes) — publishes the <see cref="DetonationNotification"/> of its warhead mount (mount 0: its munition's penetration and damage,
    /// <see cref="CombatTkb.RoundOf"/>; the travel = its velocity, so a dive arrives on the Top face) and a lethal
    /// <see cref="DamageAssessedEvent"/> on itself — the rest is the existing damage chain. A DEAD one comes to rest on the ground. Whoever
    /// flies it, AI or an operator. Runs on the node that moves it (after <see cref="AirMotionSystem"/>).
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class DroneImpactSystem : IEcsModuleSystem
    {
        /// <summary>Bodies farther than this from the flyer (plus their own radius) are not tested.</summary>
        public const float NearRadiusM = 10f;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<FlightState>()) return;   // ⭐ CE-3293 S5 (R-3036 rule 8) — no hand pause gate: [TickMode(WhenTimeAdvances)] keeps this system off a frame that does not advance
            var world = WorldQuery.Of(repo);
            bool colliders = repo.IsComponentTypeRegistered<PhysicsCollider>();
            bool rounds = repo.IsComponentTypeRegistered<BallisticProjectile>();
            bool health = repo.IsComponentTypeRegistered<Health>();
            bool statuses = repo.IsComponentTypeRegistered<NavigationStatus>();
            var flyers = repo.Query().With<FlightState>().With<FlightParams>().With<SimTransform>().WithOwned<SimTransform>().With<SimVelocity>().Build();
            foreach (var d in flyers)
            {
                if (repo.GetComponentRO<FlightState>(d).Airborne == 0) continue;
                bool alive = !health || !repo.HasComponent<Health>(d) || repo.GetComponentRO<Health>(d).Current > 0f;
                // ⭐ Air Ops slice 2 — a controlled take-off or landing meets the ground (and whoever waits beside it) on purpose.
                //   ⭐ CE-2184 (AT3) — only a LIVE one: a helicopter killed on its way down keeps its Landing phase (death ends the move,
                //   not the reported phase) and would fall through the ground.
                if (alive && statuses && repo.HasComponent<NavigationStatus>(d) && IsGroundPhase(repo.GetComponentRO<NavigationStatus>(d).Phase)) continue;
                var v = repo.GetComponentRO<SimVelocity>(d).Linear;
                if (v.LengthSquared() < 1e-8f) continue;
                var p1 = repo.GetComponentRO<SimTransform>(d).Position;
                var p0 = p1 - v * deltaTime;
                float contact = repo.GetComponentRO<FlightParams>(d).ContactRadiusM;

                // ── the bodies: the first one the segment enters ──
                float bestT = float.MaxValue; Entity hit = Entity.Null;
                if (colliders)
                    foreach (var b in repo.Query().With<PhysicsCollider>().With<SimTransform>().Build())
                    {
                        if (b == d || (rounds && repo.HasComponent<BallisticProjectile>(b))) continue;
                        // ⭐ CE-2173 slice 6d — never its OWN riders (WS-3's rule): 📐 measured live, the doors-off UH-60 swept through its
                        //   door gunners and M134s the moment it moved and crashed into them (Health 800 → 0, its riders dead with it)
                        if (Fdp.Toolkit.Combat.LineOfFire.RidesWith(repo, d, b)) continue;
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
                // ⭐ CE-2214 — a body met THROUGH the terrain is not met: the band is grown by the contact radius, so under a LOW deck
                //   (test-town's garage, 3.0 m, over an M1's 2.44 m roof + 0.8 m) it pokes up through the slab and a drone touching the
                //   slab's top "entered the hull" first — 📐 live ua-drone-shelter: the warhead burst on the deck and the tank under it took
                //   the full direct hit (3000 → 2550). The hit counts only when nothing solid lies between where the segment entered the
                //   grown band and the hull's own surface; else the drone flies on and meets the slab (the terrain arm, next frame or now)
                if (!hit.IsNull && world != null && MetThroughTerrain(repo, world, hit, p0 + (p1 - p0) * Math.Clamp(bestT, 0f, 1f))) hit = Entity.Null;
                if (!hit.IsNull && terrainT <= bestT) hit = Entity.Null;
                if (hit.IsNull && terrainT == float.MaxValue) continue;

                var at = hit.IsNull
                    ? p0 + (p1 - p0) * terrainT
                    : p0 + (p1 - p0) * Math.Clamp(bestT, 0f, 1f);
                if (hit.IsNull && at.Z < groundZ) at.Z = groundZ;
                if (alive)
                {
                    // ⭐ R-3007 — only a flyer that IS a munition (an FPV drone, Gen.Flight DetonatesOnImpact) bursts its warhead; any
                    //   other aircraft that meets the ground or a body crashes: destroyed, no burst (an A-10 does not set off its bombs)
                    var mount = repo.GetComponentRO<FlightParams>(d).DetonatesOnImpact != 0 ? CombatTkb.MountOf(repo, d, 0) : null;
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

        /// <summary>⭐ CE-2214 — true when the terrain blocks the line from <paramref name="entry"/> (where a swept segment entered a body's
        /// contact-grown band) to the nearest point of the body's own cylinder: a deck, a roof or a wall between them.</summary>
        internal static bool MetThroughTerrain(EntityRepository repo, IWorldQuery world, Entity body, Vector3 entry)
        {
            var c = repo.GetComponentRO<PhysicsCollider>(body);
            var bp = repo.GetComponentRO<SimTransform>(body).Position;
            float top = bp.Z + (c.Height > 0f ? c.Height : 2f * c.Radius);
            var off = new Vector2(entry.X - bp.X, entry.Y - bp.Y);
            if (off.Length() > c.Radius && off.LengthSquared() > 1e-8f) off = Vector2.Normalize(off) * c.Radius;
            var surface = new Vector3(bp.X + off.X, bp.Y + off.Y, Math.Clamp(entry.Z, bp.Z, top));
            return Vector3.DistanceSquared(entry, surface) > 1e-6f && world.SightBlocked(entry, surface);
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
