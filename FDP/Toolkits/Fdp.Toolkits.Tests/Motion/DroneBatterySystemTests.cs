using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Tests.Motion
{
    /// <summary>
    /// ⭐⭐ SD-S2b (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13c D-108) — <see cref="DroneBattery"/>, the drone's ONE battery: a drone type gets it FULL at
    /// creation (a helicopter does not; an authored charge is kept), <see cref="DroneBatterySystem"/> drains it ONLY while airborne by dt ÷ endurance, never below 0,
    /// nothing on a frame that does not advance (pause), only the batteries this node owns when gated, and a steady frame allocates nothing.
    /// </summary>
    public sealed class DroneBatterySystemTests : IDisposable
    {
        private readonly EntityRepository _w = new();
        private const float Dt = 1f / 60f;

        public DroneBatterySystemTests()
        {
            _w.RegisterComponent<SimTransform>();
            _w.RegisterComponent<FlightParams>();
            _w.RegisterComponent<FlightState>();
            _w.RegisterComponent<NavigationIntent>();
            _w.RegisterComponent<NavigationStatus>();
            _w.RegisterComponent<PhysicsCollider>();
            _w.RegisterComponent<DroneBattery>();
        }

        public void Dispose() => _w.Dispose();

        private static DISEntityType Dis(byte kind, byte domain, byte category) => new() { Kind = kind, Domain = domain, Category = category };

        private Entity Drone(float endurance, float z = 50f, byte airborne = 1, float charge = 1f)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = new Vector3(0f, 0f, z), Rotation = Quaternion.Identity });
            _w.AddComponent(e, FlightParams.From(new FlightParamsDto { EnduranceSeconds = endurance }));
            _w.AddComponent(e, new FlightState { Airborne = airborne });
            _w.AddComponent(e, new DroneBattery { Remaining01 = charge });
            return e;
        }

        private float Charge(Entity e) => _w.GetComponentRO<DroneBattery>(e).Remaining01;

        /// <summary>
        /// The flight translator gives a DRONE type (SISO's unmanned air category, 1.2.*.50, with an endurance) its battery, full; a helicopter (1.2.*.20, a 7 200 s
        /// endurance) gets none; a battery already authored on the entity (a scenario's 3 %) is kept. 🔴 Red-proof (compiles): the translator's line dropped ⇒ no battery ⇒ red.
        /// </summary>
        [Fact]
        public void SDS2b_ADroneTypeStartsFull_AHelicopterHasNone_AnAuthoredChargeIsKept()
        {
            var t = new FlightTkbTranslator();
            Assert.Contains(typeof(DroneBattery), t.GetProducedComponents());
            var drone = new TkbTemplate("drone", 9501) { DisType = Dis(1, 2, 50) };
            drone.AddDescriptor(new FlightParamsDto { EnduranceSeconds = 1800f });
            var heli = new TkbTemplate("helicopter", 9502) { DisType = Dis(1, 2, 20) };
            heli.AddDescriptor(new FlightParamsDto { EnduranceSeconds = 7200f });

            var d = _w.CreateEntity();
            t.Inject(_w, d, drone);
            Assert.Equal(1f, Charge(d));
            var h = _w.CreateEntity();
            t.Inject(_w, h, heli);
            Assert.False(_w.HasComponent<DroneBattery>(h));
            var authored = _w.CreateEntity();
            _w.AddComponent(authored, new DroneBattery { Remaining01 = 0.03f });
            t.Inject(_w, authored, drone);
            Assert.Equal(0.03f, Charge(authored));

            Assert.True(DroneBatteries.IsUnmannedAir(Dis(1, 2, 50)));
            Assert.False(DroneBatteries.IsUnmannedAir(Dis(1, 1, 50)));                // a land vehicle of category 50 is no drone
            Assert.False(DroneBatteries.IsBatteryDrone(Dis(1, 2, 50), new FlightParamsDto { EnduranceSeconds = 0f }));
        }

        /// <summary>
        /// ⭐⭐ It drains only while AIRBORNE — dt ÷ endurance a frame (60 s of a 600 s battery = 10 %) — a drone on the ground keeps its charge, and an empty one stays at 0 (never
        /// below). 🔴 Red-proof (compiles): the airborne check dropped ⇒ the grounded drone drains ⇒ red.
        /// </summary>
        [Fact]
        public void SDS2b_ItDrainsOnlyWhileAirborne_ByDtOverEndurance_NeverBelowZero()
        {
            var flying = Drone(600f);
            var landed = Drone(600f, z: 0f, airborne: 0);
            var nearlyEmpty = Drone(600f, charge: 0.001f);
            var sys = new DroneBatterySystem();
            for (int i = 0; i < 60 * 60; i++) sys.Execute(_w, Dt);
            Assert.Equal(0.9f, Charge(flying), 3);
            Assert.Equal(1f, Charge(landed));
            Assert.Equal(0f, Charge(nearlyEmpty));
            Assert.True(DroneBatteries.Empty(_w, nearlyEmpty));
            Assert.False(DroneBatteries.Empty(_w, flying));
            Assert.Equal(1f, DroneBatteries.Remaining(_w, _w.CreateEntity()));         // no battery: never empty
        }

        /// <summary>
        /// ⭐⭐ Frozen in pause (🔒 user `2026-11-27`: "drone battery must freeze"): the system is <see cref="TickPolicy.WhenTimeAdvances"/> — off a frame that does not advance —
        /// and a dt of 0 moves nothing even if it is called. Gated on authority, a battery this node does not own is left alone.
        /// 🔴 Red-proof (compiles): the attribute removed ⇒ red.
        /// </summary>
        [Fact]
        public void SDS2b_ItIsFrozenInPause_AndDrainsOnlyTheBatteriesThisNodeOwns()
        {
            var attr = (TickModeAttribute?)Attribute.GetCustomAttribute(typeof(DroneBatterySystem), typeof(TickModeAttribute));
            Assert.NotNull(attr);
            Assert.Equal(TickPolicy.WhenTimeAdvances, attr!.Policy);
            var flying = Drone(600f);
            var sys = new DroneBatterySystem();
            for (int i = 0; i < 600; i++) sys.Execute(_w, 0f);
            Assert.Equal(1f, Charge(flying));

            var gated = new DroneBatterySystem(gateOnAuthority: true);
            _w.SetAuthority<DroneBattery>(flying, false);
            gated.Execute(_w, 10f);
            Assert.Equal(1f, Charge(flying));
            _w.SetAuthority<DroneBattery>(flying, true);
            gated.Execute(_w, 60f);
            Assert.Equal(0.9f, Charge(flying), 4);
        }

        /// <summary>⛔ R-2084 — a steady frame (drones flying, one landed, one empty) allocates nothing. 🔴 Red-proof: the query built per frame ⇒ bytes &gt; 0.</summary>
        [Fact]
        public void SDS2b_ASteadyFrame_AllocatesNothing()
        {
            for (int i = 0; i < 8; i++) Drone(600f);
            Drone(600f, z: 0f, airborne: 0);
            Drone(600f, charge: 0f);
            var sys = new DroneBatterySystem();
            for (int i = 0; i < 5; i++) sys.Execute(_w, Dt);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 500; i++) sys.Execute(_w, Dt);
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }
}
