using System;
using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Toolkit.CarKinem.Systems;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Tests.Motion
{
    /// <summary>
    /// ⭐⭐ CE-2168 (docs/designs/motion-models/DESIGN_Motion_Domains.md MD-C, MD-D, MD-H) + M7 (DESIGN_FPV_Drone.md §2, §5) — the air
    /// domain: <see cref="AirMotionSystem"/> routes an air body to its model through <see cref="MotionModelTable"/>, the first model is
    /// <see cref="RotorcraftModel"/> (autonomous AND manual), a dead flyer falls, and nothing else moves it twice.
    /// </summary>
    public sealed class RotorcraftModelTests : IDisposable
    {
        private readonly EntityRepository _w = new();
        private readonly AirMotionSystem _air = new();
        private const float Dt = 1f / 60f;

        public RotorcraftModelTests()
        {
            _w.RegisterComponent<SimTransform>();
            _w.RegisterComponent<SimVelocity>();
            _w.RegisterComponent<Locomotion>();
            _w.RegisterComponent<FlightParams>();
            _w.RegisterComponent<FlightState>();
            _w.RegisterComponent<NavigationIntent>();
            _w.RegisterComponent<NavigationStatus>();
            _w.RegisterComponent<ManualDrive>();
            _w.RegisterComponent<Health>();
            _w.RegisterComponent<VehicleState>();
        }

        public void Dispose() => _w.Dispose();

        private Entity Drone(Vector3 at, LocomotionClass cls = LocomotionClass.Rotorcraft)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _w.AddComponent(e, new SimVelocity());
            _w.AddComponent(e, new Locomotion { Class = cls });
            _w.AddComponent(e, FlightParams.From(new FlightParamsDto()));   // 30 m/s, climb 8, 120°/s
            _w.AddComponent(e, new FlightState { Airborne = 1 });
            _w.AddComponent(e, new NavigationIntent());
            _w.AddComponent(e, new NavigationStatus());
            _w.AddComponent(e, new Health { Current = 10, Max = 10 });
            _w.SetAuthority<SimTransform>(e, true);   // this node owns its position (the air system moves only owned bodies)
            return e;
        }

        private void Run(int frames) { for (int i = 0; i < frames; i++) _air.Execute(_w, Dt); }

        /// <summary>⭐ M7 — autonomous: it turns toward a 3-D point (≤ 120°/s), climbs (≤ 8 m/s) and flies there at the intent's speed,
        /// then reports Arrived on NavigationStatus (the contract LocomotionMoveTo reads) and hovers.</summary>
        [Fact]
        public void M7_ARotorcraft_FliesToA3DPoint_AndReportsArrival()
        {
            var d = Drone(new Vector3(0, 0, 10));
            _w.SetComponent(d, new NavigationIntent { Mode = NavigationMode.DirectPoint, IntentId = 7, FinalDestination = new Vector3(0, 200, 60), TargetSpeed = 20 });

            Run(30);   // half a second: a 90° turn at 120°/s is not done yet, the climb is capped
            var mid = _w.GetComponent<SimTransform>(d);
            Assert.InRange(mid.Position.Z, 10f, 10f + 8f * 0.5f + 0.01f);
            Assert.InRange(RotorcraftModel.YawOf(mid.Rotation), 0.9f, 1.1f);   // 60° of the 90° turn
            Assert.Equal(NavigationResult.InProgress, _w.GetComponent<NavigationStatus>(d).Result);

            Run(60 * 20);
            var end = _w.GetComponent<SimTransform>(d);
            Assert.True(Vector3.Distance(end.Position, new Vector3(0, 200, 60)) <= RotorcraftModel.DefaultArrivalRadiusM, $"at {end.Position}");
            var status = _w.GetComponent<NavigationStatus>(d);
            Assert.Equal((NavigationResult.Arrived, 7u), (status.Result, status.IntentId));
            Assert.Equal(Vector3.Zero, _w.GetComponent<SimVelocity>(d).Linear);   // it hovers there
        }

        /// <summary>⭐ M7 / MD-D — manual: Vertical climbs, Forward flies along the nose, Yaw turns it (≤ 120°/s), nothing ⇒ hover.</summary>
        [Fact]
        public void M7_ARotorcraft_Hovers_ClimbsAndYaws_UnderManualDrive()
        {
            var d = Drone(new Vector3(0, 0, 20));
            _w.SetComponent(d, new NavigationIntent { Mode = NavigationMode.Manual });
            _w.SetComponent(d, new ManualDrive { Vertical = 1 });
            Run(60);
            Assert.Equal(28f, _w.GetComponent<SimTransform>(d).Position.Z, 1);   // 8 m/s for a second

            _w.SetComponent(d, new ManualDrive { Yaw = MathF.PI / 2 });
            Run(30);
            Assert.Equal(MathF.PI / 3, RotorcraftModel.YawOf(_w.GetComponent<SimTransform>(d).Rotation), 2);   // 60° in half a second
            Run(30);
            Assert.Equal(MathF.PI / 2, RotorcraftModel.YawOf(_w.GetComponent<SimTransform>(d).Rotation), 2);

            var before = _w.GetComponent<SimTransform>(d).Position;
            _w.SetComponent(d, new ManualDrive { Yaw = MathF.PI / 2, Forward = 1 });
            Run(60);
            var after = _w.GetComponent<SimTransform>(d).Position;
            Assert.Equal(30f, after.Y - before.Y, 0);   // along the nose (+Y), at top speed
            Assert.Equal(0f, after.X - before.X, 1);

            _w.SetComponent(d, new ManualDrive { Yaw = MathF.PI / 2 });
            Run(10);
            Assert.Equal(Vector3.Zero, _w.GetComponent<SimVelocity>(d).Linear);   // no axis ⇒ it hovers
        }

        /// <summary>⭐ M7 — a dead rotorcraft falls under gravity whatever it is told.</summary>
        [Fact]
        public void M7_ADeadRotorcraft_Falls()
        {
            var d = Drone(new Vector3(0, 0, 50));
            _w.SetComponent(d, new NavigationIntent { Mode = NavigationMode.DirectPoint, FinalDestination = new Vector3(0, 0, 80), TargetSpeed = 10 });
            _w.SetComponent(d, new Health { Current = 0, Max = 10 });
            Run(60);
            var z = _w.GetComponent<SimTransform>(d).Position.Z;
            Assert.InRange(z, 50f - 0.5f * 9.81f - 0.3f, 50f - 0.5f * 9.81f + 0.3f);
            Assert.True(_w.GetComponent<FlightState>(d).VerticalSpeed < -9f);
        }

        /// <summary>⭐ CE-2168 (MD-C, MD-H) — the air module moves only air bodies; a class whose model is not built fails LOUDLY; and the
        /// projectile integrator leaves a body with a motion model alone (it was moving every SimTransform + SimVelocity body).</summary>
        [Fact]
        public void CE2168_OnlyAirBodies_FailLoudlyForAnUnbuiltClass_AndNoBodyIsIntegratedTwice()
        {
            var ground = Drone(new Vector3(0, 0, 0), LocomotionClass.Wheeled);
            _w.SetComponent(ground, new NavigationIntent { Mode = NavigationMode.DirectPoint, FinalDestination = new Vector3(100, 0, 0), TargetSpeed = 10 });
            Run(10);
            Assert.Equal(Vector3.Zero, _w.GetComponent<SimTransform>(ground).Position);

            var drone = Drone(new Vector3(0, 0, 10));
            _w.SetComponent(drone, new SimVelocity { Linear = new Vector3(5, 0, 0) });
            new LinearKinematicsSystem().Execute(_w, 1f);
            Assert.Equal(new Vector3(0, 0, 10), _w.GetComponent<SimTransform>(drone).Position);

            Drone(new Vector3(0, 0, 100), LocomotionClass.FixedWing);
            Assert.Throws<NotSupportedException>(() => Run(1));
            Assert.Throws<NotSupportedException>(() => MotionModelTable.Model(LocomotionClass.Surface));
            Assert.Null(MotionModelTable.Model(LocomotionClass.Tracked));   // v1: the ground still runs CarKinematics
            Assert.Equal(MotionDomain.Air, Locomotions.DomainOf(LocomotionClass.Rotorcraft));
            Assert.Equal(MotionDomain.Ground, Locomotions.DomainOf(LocomotionClass.Amphibious));
        }
    }
}
