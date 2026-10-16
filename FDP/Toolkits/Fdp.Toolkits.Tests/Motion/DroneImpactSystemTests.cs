using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Tests.Motion
{
    /// <summary>
    /// ⭐⭐ M7 (docs/designs/vehicle-tactics/DESIGN_FPV_Drone.md §2, §3, §5) — <see cref="DroneImpactSystem"/>: a diving drone meets a tank's
    /// hull and publishes ITS WARHEAD's detonation (munition, penetration, damage, and the travel — descending, so the existing chain reads
    /// the Top face) and a lethal hit on itself; it stops there. A drone that passes beside or over a hull meets nothing.
    /// </summary>
    public sealed class DroneImpactSystemTests : IDisposable
    {
        private readonly EntityRepository _w = new();
        private const long DroneType = 9403, WarheadType = 9416;
        private const float Dt = 1f / 60f;

        public DroneImpactSystemTests()
        {
            _w.RegisterComponent<SimTransform>();
            _w.RegisterComponent<SimVelocity>();
            _w.RegisterComponent<FlightParams>();
            _w.RegisterComponent<FlightState>();
            _w.RegisterComponent<PhysicsCollider>();
            _w.RegisterComponent<Health>();
            _w.RegisterComponent<TkbIdentity>();
            _w.RegisterEvent<DetonationNotification>();
            _w.RegisterEvent<DamageAssessedEvent>();
            var db = new TkbDatabase();
            var warhead = new TkbTemplate("FPV warhead", WarheadType);
            warhead.AddDescriptor(new AmmoWeaponBallisticsDto { WeaponGuid = 0, PenetrationMm = 400, Damage = 450 });
            warhead.AddDescriptor(new WarheadDto { Kind = WarheadKind.ShapedCharge, FragmentRadiusM = 3, FragmentPenetrationMm = 10, FragmentDamage = 30 });
            db.Register(warhead);
            var drone = new TkbTemplate("FPV", DroneType);
            drone.AddDescriptor(new WeaponSuiteDto { Mounts = { new WeaponMountDto { AmmoGuid = WarheadType, DamagePerHit = 450, Range = 0.8f, InitialAmmunition = 1 } } });
            db.Register(drone);
            _w.SetSingletonManaged<ITkbDatabase>(db);
        }

        public void Dispose() => _w.Dispose();

        private Entity Tank(Vector3 at)
        {
            var t = _w.CreateEntity();
            _w.AddComponent(t, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _w.AddComponent(t, new PhysicsCollider { Radius = 2f, Height = 2.4f });
            _w.AddComponent(t, new Health { Current = 2500, Max = 2500 });
            return t;
        }

        private Entity Drone(Vector3 at, Vector3 velocity, bool munition = true)
        {
            var d = _w.CreateEntity();
            _w.AddComponent(d, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _w.AddComponent(d, new SimVelocity { Linear = velocity });
            _w.AddComponent(d, FlightParams.From(new FlightParamsDto { DetonatesOnImpact = munition }));
            _w.AddComponent(d, new FlightState { Airborne = 1 });
            _w.AddComponent(d, new PhysicsCollider { Radius = 0.3f, Height = 0.3f });
            _w.AddComponent(d, new Health { Current = 10, Max = 10 });
            _w.AddComponent(d, new TkbIdentity { TkbType = DroneType });
            _w.SetAuthority<SimTransform>(d, true);
            return d;
        }

        private (DetonationNotification[] Detonations, DamageAssessedEvent[] Damage) Step()
        {
            new DroneImpactSystem().Execute(_w, Dt);
            _w.Bus.SwapBuffers();
            return (_w.Bus.Read<DetonationNotification>().ToArray(), _w.Bus.Read<DamageAssessedEvent>().ToArray());
        }

        /// <summary>⭐⭐ M7 — a 60° dive onto a tank: the segment of this frame enters the roof band ⇒ the warhead's detonation on the TANK
        /// (6016-like: 400 mm, 450), travelling DOWN (≥ 45° — the Top face in DamageCalculation), a lethal hit on the drone, and it stops.</summary>
        [Fact]
        public void M7_ADive_OnATank_HitsItsRoof_AndKillsTheDrone()
        {
            var tank = Tank(new Vector3(100, 0, 0));
            var v = new Vector3(15, 0, -26);   // 30 m/s at 60° down
            var drone = Drone(new Vector3(100, 0, 2.4f + 0.2f), v);   // this frame it crossed into the roof band

            var (dets, dmg) = Step();
            var det = Assert.Single(dets);
            Assert.Equal((drone, tank, WarheadType, 400f, 450f), (det.Shooter, det.Target, det.Ammo, det.Penetration, det.Damage));
            float down = MathF.Atan2(-det.TravelZ, MathF.Sqrt(det.TravelX * det.TravelX + det.TravelY * det.TravelY)) * 180f / MathF.PI;
            Assert.True(down >= 45f, $"the travel dives at {down}°");
            Assert.Contains(dmg, d => d.HitEntity == drone && d.TotalDamage >= 10f);
            Assert.Equal(0, _w.GetComponentRO<FlightState>(drone).Airborne);
            Assert.Equal(Vector3.Zero, _w.GetComponentRO<SimVelocity>(drone).Linear);

            Assert.Empty(Step().Detonations);   // spent: once
        }

        /// <summary>⭐ R-3007 — an aircraft that is NOT a munition (an A-10 with its bombs on the first mount, a helicopter) crashes into
        /// the same tank: it is destroyed and comes to rest, but nothing bursts — its weapons do not go off, the tank is not hit.</summary>
        [Fact]
        public void R3007_AnAircraftThatIsNotAMunition_Crashes_WithoutABurst()
        {
            var tank = Tank(new Vector3(100, 0, 0));
            var crash = Drone(new Vector3(100, 0, 2.4f + 0.2f), new Vector3(15, 0, -26), munition: false);

            var (dets, dmg) = Step();
            Assert.Empty(dets);
            Assert.Contains(dmg, d => d.HitEntity == crash && d.TotalDamage >= 10f);   // destroyed
            Assert.DoesNotContain(dmg, d => d.HitEntity == tank);
            Assert.Equal(0, _w.GetComponentRO<FlightState>(crash).Airborne);
            Assert.Equal(Vector3.Zero, _w.GetComponentRO<SimVelocity>(crash).Linear);
        }

        /// <summary>⭐ M7 — a drone flying past beside a hull, or over it above its roof, meets nothing.</summary>
        /// <summary>
        /// ⭐ <c>CE-2173</c> slice 6d, found live (`ua-air-attack` ⑤, `2026-10-15`) — a carrier never meets its OWN riders: the doors-off UH-60
        /// moving at 70 m/s swept its segment through its door gunners' and M134s' colliders (they sit inside it) and "crashed" into them —
        /// Health 800 → 0 the moment it moved, its riders dead with it. The rule is WS-3's <c>LineOfFire.RidesWith</c> (a carrier and its riders
        /// do not hide or hit each other). A body that does NOT ride with it, at the same spot, is still met.
        /// ✅ Red-proof: the rider test removed ⇒ the helicopter is dealt lethal damage ⇒ red.
        /// </summary>
        [Fact]
        public void S6d_ACarrierInFlight_NeverMeetsItsOwnRiders_AStrangerThereIsStillMet()
        {
            _w.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
            var heli = Drone(new Vector3(0f, 0f, 100f), new Vector3(70f, 0f, 0f), munition: false);
            _w.GetComponentRW<PhysicsCollider>(heli) = new PhysicsCollider { Radius = 1.5f, Height = 3.5f };
            _w.GetComponentRW<Health>(heli) = new Health { Current = 800, Max = 800 };
            Entity Body(bool rider)
            {
                var b = _w.CreateEntity();
                _w.AddComponent(b, new SimTransform { Position = new Vector3(-0.5f, 0.3f, 99.5f), Rotation = Quaternion.Identity });   // on the swept segment, inside the cabin
                _w.AddComponent(b, new PhysicsCollider { Radius = 0.4f, Height = 1.8f });
                if (rider) _w.AddComponent(b, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = heli, Seat = 8, Exposed = 1 });
                return b;
            }
            var gunner = Body(rider: true);
            var (_, damage) = Step();
            Assert.DoesNotContain(damage, d => d.HitEntity == heli);

            _w.RemoveComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(gunner);   // the same body, no longer aboard
            var (_, met) = Step();
            Assert.Contains(met, d => d.HitEntity == heli);
        }

        [Fact]
        public void M7_ADroneFlyingPastOrOver_MeetsNothing()
        {
            Tank(new Vector3(100, 0, 0));
            Drone(new Vector3(100, 6, 1.5f), new Vector3(30, 0, 0));    // 6 m to the side
            Drone(new Vector3(100, 0, 8f), new Vector3(30, 0, 0));      // 8 m up, the roof at 2.4
            var (dets, dmg) = Step();
            Assert.Empty(dets);
            Assert.Empty(dmg);
        }
    
        /// <summary>A walkable SLAB — how the terrain models a deck (Exposure Cycle §3, V3: a prism has nothing under it) — at
        /// <paramref name="z"/> over x, y ∈ [90, 110].</summary>
        private void Deck(float z)
            => _w.SetSingletonManaged(new Fdp.Toolkit.Terrain.TerrainWorld
            {
                BoundsMin = new Vector2(-200, -200), BoundsMax = new Vector2(200, 200),
                Walkables = new[]
                {
                    new Fdp.Toolkit.Terrain.TerrainWalkable
                    {
                        Kind = Fdp.Toolkit.Terrain.TerrainWalkableKind.Slab,
                        Vertices = new[] { new Vector3(90, -10, z), new Vector3(110, -10, z), new Vector3(110, 10, z), new Vector3(90, 10, z) },
                        Triangles = new[] { 0, 1, 2, 0, 2, 3 },
                        Min = new Vector2(90, -10), Max = new Vector2(110, 10), MinZ = z, MaxZ = z,
                    },
                },
            });

        /// <summary>
        /// ⭐⭐ <c>C2</c> (handoff 2; FPV §4) — a drone diving at a tank parked UNDER A DECK meets the deck, not the hull: its warhead goes
        /// off on the slab (no target), the tank is untouched, and the drone stops on the deck. ⭐ Two shapes: the frame that crosses the
        /// deck stays above the hull's band (a normal 60 Hz dive); and ONE long frame whose segment crosses the deck AND enters the hull's
        /// band behind it — the deck is first along the segment, so it wins.
        /// </summary>
        [Theory]
        [InlineData(1f / 60f)]
        [InlineData(0.1f)]
        public void M7_ADive_UnderADeck_MeetsTheSlab(float dt)
        {
            Deck(4.5f);
            var tank = Tank(new Vector3(100, 0, 0));                          // roof 2.4, under the 4.5 m deck
            var v = new Vector3(15, 0, -26);                                    // 60° dive
            var drone = Drone(new Vector3(100, 0, 6f), v);
            DetonationNotification[] dets = Array.Empty<DetonationNotification>();
            for (int k = 0; k < 120 && dets.Length == 0; k++)
            {
                ref var tf = ref _w.GetComponentRW<SimTransform>(drone);
                if (_w.GetComponentRO<FlightState>(drone).Airborne == 0) break;
                tf.Position += v * dt;                                          // the air module's step (moved first, as in the pack)
                new DroneImpactSystem().Execute(_w, dt);
                _w.Bus.SwapBuffers();
                dets = _w.Bus.Read<DetonationNotification>().ToArray();
            }
            var det = Assert.Single(dets);
            Assert.True(det.Target.IsNull, $"the warhead went off on {det.Target}, not on the deck");
            Assert.InRange(det.HitZ, 4.0f, 5.0f);
            Assert.Equal(0, _w.GetComponentRO<FlightState>(drone).Airborne);
            Assert.True(_w.GetComponentRO<SimTransform>(drone).Position.Z > 4.0f, "the wreck rests on the deck, not under it");
            Assert.Equal(2500f, _w.GetComponentRO<Health>(tank).Current);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2214</c> (live <c>ua-drone-shelter</c>) — a LOW deck: test-town's garage slab at 3.0 m over an M1's 2.44 m roof. The hull's
        /// band, grown by the drone's 0.8 m contact radius, reaches 3.24 m — ABOVE the slab — so a drone coming down onto the slab's top
        /// entered it first and the tank under the deck took the full direct hit. Now the deck is met: no target, the tank untouched.
        /// ✅ Red-proof (mutant): drop the through-terrain test ⇒ the warhead goes off on the tank ⇒ red.
        /// </summary>
        [Fact]
        public void CE2214_ADive_OntoALowDeck_JustAboveTheHull_MeetsTheSlab_NotTheTankUnderIt()
        {
            Deck(3.0f);
            var tank = _w.CreateEntity();
            _w.AddComponent(tank, new SimTransform { Position = new Vector3(100, 0, 0), Rotation = Quaternion.Identity });
            _w.AddComponent(tank, new PhysicsCollider { Radius = 2f, Height = 2.44f });
            _w.AddComponent(tank, new Health { Current = 3000, Max = 3000 });
            var v = new Vector3(15, 0, -26);                                    // 60° dive
            var drone = Drone(new Vector3(97, 0, 8f), v);
            DetonationNotification[] dets = Array.Empty<DetonationNotification>();
            for (int k = 0; k < 120 && dets.Length == 0; k++)
            {
                ref var tf = ref _w.GetComponentRW<SimTransform>(drone);
                if (_w.GetComponentRO<FlightState>(drone).Airborne == 0) break;
                tf.Position += v * Dt;
                new DroneImpactSystem().Execute(_w, Dt);
                _w.Bus.SwapBuffers();
                dets = _w.Bus.Read<DetonationNotification>().ToArray();
            }
            var det = Assert.Single(dets);
            Assert.True(det.Target.IsNull, $"the warhead went off on {det.Target}, not on the deck");
            Assert.InRange(det.HitZ, 2.9f, 3.3f);
            Assert.Equal(3000f, _w.GetComponentRO<Health>(tank).Current);
        }
}
}
