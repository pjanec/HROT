using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Perception.Signatures;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Combat.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4660</c> (thermal drivers TD-S4, TD-16; docs/designs/thermal/DESIGN_Thermal_Drivers.md §4g) — fire as SIMULATION state on the Health owner:
    /// a destroyed fuelled hull catches fire ONCE (at its death crossing), a wreck loaded already dead never does, and a fire burns the bodies inside it
    /// in whole steps — a man outside it, or one whose Health another node owns, loses nothing.
    /// </summary>
    public sealed class BurnSystemTests : IDisposable
    {
        private const long Tank = 100, Man = 200;
        private readonly EntityRepository _w = new();
        private readonly BurnSystem _sys = new();
        private double _t;

        public BurnSystemTests()
        {
            _w.RegisterComponent<SimTransform>();
            _w.RegisterComponent<Health>();
            _w.RegisterComponent<TkbIdentity>();
            _w.RegisterComponent<FireState>();
            _w.RegisterComponent<Locomotion>();
            _w.RegisterComponent<NetworkAuthority>();
            _w.RegisterComponent<global::CarKinem.Core.VehicleParams>();
            var db = new TkbDatabase();
            var tank = new TkbTemplate("Tank", Tank);
            tank.AddDescriptor(new LocomotionDto { Class = LocomotionClass.Tracked });   // ⇒ ThermalDefaults: burns 30 min
            db.Register(tank);
            var man = new TkbTemplate("Man", Man);
            man.AddDescriptor(new LocomotionDto { Class = LocomotionClass.Pedestrian });   // a body: never lit, burns in a fire
            db.Register(man);
            _w.SetSingletonManaged<ITkbDatabase>(db);
            _w.SetSingletonUnmanaged(new GlobalTime { TotalTime = 0 });
        }

        public void Dispose() => _w.Dispose();

        private Entity Put(long type, Vector3 at, float hp, LocomotionClass loco)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _w.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)type });
            _w.AddComponent(e, new Health { Current = hp, Max = 100f });
            _w.AddComponent(e, new Locomotion { Class = loco });
            if (loco == LocomotionClass.Tracked) _w.AddComponent(e, new global::CarKinem.Core.VehicleParams { Length = 7f, Width = 3.6f });
            return e;
        }

        private void Run(double seconds)
        {
            for (int i = 0, n = (int)Math.Round(seconds / 0.1); i < n; i++)
            {
                _t += 0.1;
                _w.SetSingletonUnmanaged(new GlobalTime { TotalTime = _t });
                _sys.Execute(_w, 0.1f);
            }
        }

        /// <summary>
        /// ⭐ A tank seen alive then dead catches fire ONCE — one spot on the hull, reaching past it, burning its type's 30 min; a tank loaded already
        /// dead (a cold, burnt-out wreck) is never lit. 🔴 Red-proof: drop the crossing (light whenever dead) ⇒ the loaded wreck burns ⇒ red.
        /// </summary>
        [Fact]
        public void CE4660_ADestroyedHull_CatchesFireOnce_AWreckLoadedDeadNever()
        {
            var tank = Put(Tank, Vector3.Zero, 100f, LocomotionClass.Tracked);
            var cold = Put(Tank, new Vector3(100, 0, 0), 0f, LocomotionClass.Tracked);
            Run(0.5);
            Assert.False(_w.HasComponent<FireState>(tank));
            _w.GetComponentRW<Health>(tank).Current = 0f;
            Run(0.5);
            var fire = _w.GetComponentRO<FireState>(tank);
            Assert.Equal(1, fire.Count);
            var spot = fire.Get(0);
            Assert.Equal(1800f, spot.Seconds);
            Assert.Equal(3.5f + BurnSystem.ReachM, spot.Radius, 3);
            Assert.True(fire.IsBurning(_t));
            Assert.False(_w.HasComponent<FireState>(cold));
            Run(5);
            Assert.Equal(1L, _sys.Ignited);
        }

        /// <summary>
        /// ⭐ A man inside the burning spot loses <see cref="BurnSystem.BurnPerSecond"/> of his health per whole second (one write a step), a man 20 m
        /// away nothing; and a man whose Health another node owns is left to that node. 🔴 Red-proof: drop the radius test ⇒ the far man burns ⇒ red.
        /// </summary>
        [Fact]
        public void CE4660_AFireBurnsTheBodiesInsideIt_InWholeSteps_OnTheirHealthOwner()
        {
            var tank = Put(Tank, Vector3.Zero, 100f, LocomotionClass.Tracked);
            var near = Put(Man, new Vector3(3f, 1f, 0f), 100f, LocomotionClass.Pedestrian);
            var far = Put(Man, new Vector3(20f, 0f, 0f), 100f, LocomotionClass.Pedestrian);
            var remote = Put(Man, new Vector3(-3f, 0f, 0f), 100f, LocomotionClass.Pedestrian);
            _w.AddComponent(remote, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));   // his Health is another node's
            Run(0.5);
            _w.GetComponentRW<Health>(tank).Current = 0f;
            Run(3.0);
            float hp = _w.GetComponentRO<Health>(near).Current;
            Assert.True(hp < 100f && hp % 10f == 0f, $"whole steps of 10: {hp}");
            Assert.InRange(hp, 60f, 80f);
            Assert.Equal(100f, _w.GetComponentRO<Health>(far).Current);
            Assert.Equal(100f, _w.GetComponentRO<Health>(remote).Current);
            Run(10.0);
            Assert.Equal(0f, _w.GetComponentRO<Health>(near).Current);   // burned to death
        }
    }
}
