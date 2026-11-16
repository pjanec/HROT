using System;
using System.Linq;
using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Physics.Math;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Tests.Motion
{
    /// <summary>
    /// ⭐⭐ <c>CE-4622</c> K-1 (docs/designs/vehicle-tactics/DESIGN_Checkpoint_And_Ramming.md RM-1, KV-4) — <see cref="VehicleImpactSystem"/>, the ground twin of
    /// <see cref="DroneImpactSystem"/>: a vehicle whose front reaches a person this frame strikes him with a Blunt hit of a Health fraction that rises with the
    /// closing speed and the vehicle's mass, with a seeded spread; riders, a slow nudge, a body beside the hull and a repeat inside 2 s never.
    /// </summary>
    public sealed class VehicleImpactSystemTests : IDisposable
    {
        private readonly EntityRepository _w = new();
        private const long CarType = 9851, TruckType = 9852;
        private const float Dt = 0.1f;

        public VehicleImpactSystemTests()
        {
            _w.RegisterComponent<SimTransform>();
            _w.RegisterComponent<SimVelocity>();
            _w.RegisterComponent<VehicleState>();
            _w.RegisterComponent<VehicleParams>();
            _w.RegisterComponent<Health>();
            _w.RegisterComponent<TkbIdentity>();
            _w.RegisterComponent<IsEmbarkedTag>();
            _w.RegisterEvent<DamageAssessedEvent>();
            var db = new TkbDatabase();
            var car = new TkbTemplate("Car", CarType); car.AddDescriptor(new VehicleParametersDto { Mass = 1500f }); db.Register(car);
            var truck = new TkbTemplate("Truck", TruckType); truck.AddDescriptor(new VehicleParametersDto { Mass = 6000f }); db.Register(truck);
            _w.SetSingletonManaged<ITkbDatabase>(db);
            _w.SetSimulationTime(10f);
        }

        public void Dispose() => _w.Dispose();

        private Entity Vehicle(float speed, long type = CarType)
        {
            var v = _w.CreateEntity();
            _w.AddComponent(v, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });   // facing +X
            _w.AddComponent(v, new VehicleParams { Class = VehicleClass.PersonalCar, Length = 4.5f, Width = 1.8f });
            _w.AddComponent(v, new VehicleState { Speed = speed });
            _w.AddComponent(v, new TkbIdentity { TkbType = (Fdp.Core.TkbTypeId)type });
            return v;
        }

        private Entity Man(Vector2 at)
        {
            var m = _w.CreateEntity();
            _w.AddComponent(m, new SimTransform { Position = new Vector3(at, 0f), Rotation = Quaternion.Identity });
            _w.AddComponent(m, new VehicleParams { Class = VehicleClass.Pedestrian, Length = 0.5f, Width = 0.5f });
            _w.AddComponent(m, new Health { Current = 100f, Max = 100f });
            return m;
        }

        private DamageAssessedEvent[] Frame(VehicleImpactSystem sys)
        {
            sys.Execute(_w, Dt);
            _w.Bus.SwapBuffers();
            return _w.Bus.Read<DamageAssessedEvent>().ToArray();
        }

        /// <summary>⭐ The bands: nothing under 2 m/s; the faster, the worse (10–35 % · 35–75 % · 75–110 % · 120 %); a heavier hull hurts more; the spread moves it ±15 %.
        /// ✅ Red-proof: the mass term dropped ⇒ the truck no worse than the car ⇒ red.</summary>
        [Fact]
        public void K1_TheHarm_RisesWithClosingSpeedAndMass_InBands()
        {
            Assert.Equal(0f, VehicleImpactSystem.HarmFraction(1.5f, 1500f, 0.5f));
            float a = VehicleImpactSystem.HarmFraction(3f, 1500f, 0.5f), b = VehicleImpactSystem.HarmFraction(7f, 1500f, 0.5f),
                  c = VehicleImpactSystem.HarmFraction(12f, 1500f, 0.5f), d = VehicleImpactSystem.HarmFraction(20f, 1500f, 0.5f);
            Assert.InRange(a, 0.10f, 0.35f);
            Assert.InRange(b, 0.35f, 0.75f);
            Assert.InRange(c, 0.75f, 1.10f);
            Assert.True(d >= 1.0f, $"a car at 20 m/s kills: {d}");
            Assert.True(a < b && b < c && c < d);
            Assert.True(VehicleImpactSystem.HarmFraction(5f, 6000f, 0.5f) > VehicleImpactSystem.HarmFraction(5f, 1500f, 0.5f), "a truck hurts more than a car");
            float lo = VehicleImpactSystem.HarmFraction(7f, 1500f, 0f), hi = VehicleImpactSystem.HarmFraction(7f, 1500f, 0.999f);
            Assert.InRange(hi / lo, 1.30f, 1.36f);                                                     // ±15 %
        }

        /// <summary>⭐⭐ A man struck at rising closing speeds falls in rising Health bands (a Blunt hit, the knock-down, the car its source); the seeded spread
        /// is the same for the same pair and moment. ✅ Red-proof: the swept test dropped (only the hull's current box) ⇒ a man the front reached this frame
        /// is missed ⇒ red.</summary>
        [Theory]
        [InlineData(4f, 0.10f, 0.35f)]
        [InlineData(8f, 0.35f, 0.75f)]
        [InlineData(14f, 0.75f, 1.30f)]
        public void K1_AManStruck_FallsInTheBandOfTheClosingSpeed(float speed, float lo, float hi)
        {
            var car = Vehicle(speed);
            var man = Man(new Vector2(2.25f - speed * Dt * 0.5f, 0f));   // the front reached him during this frame (half way along its travel)
            var hits = Frame(new VehicleImpactSystem());
            var hit = Assert.Single(hits);
            Assert.Equal(man, hit.HitEntity);
            Assert.Equal(car, hit.Source);
            Assert.Equal(HitEffect.Blunt, hit.Effect);
            Assert.InRange(hit.TotalDamage / 100f, lo, hi);
        }

        /// <summary>⭐ Never: a rider (embarked), a car creeping at 1.5 m/s, a man beside the hull, a man behind it, the same man again within 2 s.
        /// ✅ Red-proof: the rider exemption dropped ⇒ red.</summary>
        [Fact]
        public void K1_RidersASlowNudgeABystander_AndARepeat_AreNotStruck()
        {
            var car = Vehicle(8f);
            var rider = Man(new Vector2(2.4f, 0f));
            _w.AddComponent(rider, new IsEmbarkedTag { VehicleEntity = car });
            Man(new Vector2(0f, 3f));                                    // beside the hull
            Man(new Vector2(-4f, 0f));                                   // behind it
            Assert.Empty(Frame(new VehicleImpactSystem()));

            var slow = Vehicle(1.5f); _w.GetComponentRW<SimTransform>(slow).Position = new Vector3(50f, 0f, 0f);
            Man(new Vector2(52.3f, 0f));
            Assert.Empty(Frame(new VehicleImpactSystem()));

            var sys = new VehicleImpactSystem();
            var fast = Vehicle(8f); _w.GetComponentRW<SimTransform>(fast).Position = new Vector3(100f, 0f, 0f);
            Man(new Vector2(102.4f, 0f));
            Assert.Single(Frame(sys));
            _w.SetSimulationTime(11f);
            Assert.Empty(Frame(sys));                                   // the same pair 1 s later: pushed through, not struck again
            _w.SetSimulationTime(12.5f);
            Assert.Single(Frame(sys));                                  // after 2 s it can strike again
        }

        /// <summary>⭐ The shared swept primitive: the front's swept strip, grown by the radius; the hull's sides and what it already covered are not met.</summary>
        [Fact]
        public void K1_TheSweptFront_MeetsOnlyWhatTheFrontReachedThisFrame()
        {
            var c = Vector2.Zero; var f = Vector2.UnitX;
            Assert.True(Intersection2D.SweptFrontMeetsCircle(c, f, 2f, 1f, 1f, new Vector2(1.5f, 0f), 0.3f, out _));    // reached this frame
            Assert.True(Intersection2D.SweptFrontMeetsCircle(c, f, 2f, 1f, 1f, new Vector2(2.2f, 0.5f), 0.3f, out _));   // just at the front corner
            Assert.False(Intersection2D.SweptFrontMeetsCircle(c, f, 2f, 1f, 1f, new Vector2(0f, 0f), 0.3f, out _));      // already under the hull
            Assert.False(Intersection2D.SweptFrontMeetsCircle(c, f, 2f, 1f, 1f, new Vector2(1.5f, 1.5f), 0.3f, out _));  // beside it
            Assert.False(Intersection2D.SweptFrontMeetsCircle(c, f, 2f, 1f, 1f, new Vector2(3f, 0f), 0.3f, out _));      // not reached yet
        }
    }
}
