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

        private Entity Drone(Vector3 at, Vector3 velocity)
        {
            var d = _w.CreateEntity();
            _w.AddComponent(d, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _w.AddComponent(d, new SimVelocity { Linear = velocity });
            _w.AddComponent(d, FlightParams.From(new FlightParamsDto()));
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

        /// <summary>⭐ M7 — a drone flying past beside a hull, or over it above its roof, meets nothing.</summary>
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
    }
}
