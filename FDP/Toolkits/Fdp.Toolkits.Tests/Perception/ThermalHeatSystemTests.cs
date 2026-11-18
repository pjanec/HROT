using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Perception.Signatures;
using Xunit;

namespace Fdp.Toolkit.Perception.Tests
{
    /// <summary>
    /// ⭐ <c>CE-3061</c> — heat builds while an entity runs or fires and cools when it stops (R-205,
    /// docs/DESIGN_Thermal_And_Acoustic_Sensing.md §5.2).
    /// </summary>
    public class ThermalHeatSystemTests
    {
        private static (EntityRepository w, Entity e) World()
        {
            var w = new EntityRepository();
            w.RegisterComponent<SimTransform>();
            w.RegisterComponent<ThermalState>();
            w.RegisterEvent<WeaponFireNotification>();
            var e = w.CreateEntity();
            w.AddComponent(e, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            w.AddComponent(e, new ThermalState
            {
                BaseSignature = 0.3f, RunningHeatPerSecond = 0.5f, FiringHeatPerShot = 0.1f, CooldownPerSecond = 0.2f, ReferenceSpeed = 5f,
            });
            return (w, e);
        }

        private static void Step(EntityRepository w, ThermalHeatSystem sys, float dt = 0.1f)
        {
            sys.Execute(w, dt);
            w.Bus.SwapBuffers();
        }

        // ── ⭐ CE-4658 (thermal drivers TD-S2; DESIGN_Thermal_Drivers.md §4e) — HeatSourceSystem: the replicated DECISION and its times, written on a change.
        //    (The CE-3061 integration rails — heat built by running / by each shot — retired with the integration: the heat is now computed by every reader.)

        private sealed class Driven
        {
            public readonly EntityRepository W = new();
            public readonly HeatSourceSystem Sys = new();
            public double T;
            public readonly Entity E;

            public Driven(bool engine, bool crewed = false, HeatSource? authored = null)
            {
                W.RegisterComponent<SimTransform>();
                W.RegisterComponent<ThermalState>();
                W.RegisterComponent<HeatSource>();
                W.RegisterComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>();
                W.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
                W.RegisterEvent<Fdp.Toolkit.Terrain.GroundHeatMarkEvent>();
                W.RegisterComponent<global::CarKinem.Core.VehicleParams>();
                E = W.CreateEntity();
                W.AddComponent(E, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
                var type = engine ? ThermalDefaults.For(Fdp.Toolkit.Tkb.Domain.LocomotionClass.Tracked)! : ThermalDefaults.For(Fdp.Toolkit.Tkb.Domain.LocomotionClass.Pedestrian)!;
                W.AddComponent(E, new ThermalState
                {
                    BaseSignature = type.BaseSignature, RunningHeatPerSecond = type.RunningHeatPerSecond, CooldownPerSecond = type.CooldownPerSecond,
                    ReferenceSpeed = type.ReferenceSpeed, IdleHeat = type.IdleHeat, RunningHeat = type.RunningHeat, HasPowerPlant = type.HasPowerPlant,
                    PowerClass = (byte)type.PowerClass, HotEnd = (byte)type.HotEnd,
                });
                if (crewed) W.AddComponent(E, new Fdp.Toolkit.Behavior.Components.BehaviorState());
                if (engine) W.AddComponent(E, new global::CarKinem.Core.VehicleParams { Length = 6.9f, Width = 3.6f });   // the hull a ground mark takes its footprint from
                if (authored is { } a) W.AddComponent(E, a);
                W.SetSingletonUnmanaged(new GlobalTime { TotalTime = 0 });
            }

            /// <summary>Steps <paramref name="seconds"/> at <paramref name="speed"/> m/s along +X, 0.1 s a frame.</summary>
            public void Run(double seconds, float speed)
            {
                for (int i = 0, n = (int)Math.Round(seconds / 0.1); i < n; i++)
                {
                    T += 0.1;
                    W.SetSingletonUnmanaged(new GlobalTime { TotalTime = T });
                    W.GetComponentRW<SimTransform>(E).Position += new Vector3(speed * 0.1f, 0f, 0f);
                    Sys.Execute(W, 0.1f);
                    W.Bus.SwapBuffers();
                }
            }

            public HeatSource Source => W.GetComponentRO<HeatSource>(E);
        }

        /// <summary>
        /// ⭐ A body: Standing from the start (times −∞, never moved), Moving at the edge it started, Standing again stamped at the moment it STOPPED (not when the
        /// debounce ended) — so a man who just ran reads warmer than one who stood (the calculator), and a minute's rest cools him back.
        /// 🔴 Red-proof: stamp the stop at the debounce's end ⇒ <c>MotionSince</c> is 1 s late ⇒ red.
        /// </summary>
        [Fact]
        public void CE4658_ABody_StandsMovesAndStands_StampedAtItsEdges()
        {
            var d = new Driven(engine: false);
            d.Run(1.0, 0f);
            Assert.Equal(HeatSourceState.Standing, d.Source.State);
            Assert.True(double.IsNegativeInfinity(d.Source.MotionSince.Seconds));

            d.Run(30.0, 3f);                                    // a 30 s run
            Assert.Equal(HeatSourceState.Moving, d.Source.State);
            Assert.Equal(1.1, d.Source.MotionSince.Seconds, 3);

            d.Run(2.0, 0f);
            Assert.Equal(HeatSourceState.Standing, d.Source.State);
            Assert.Equal(31.1, d.Source.MotionSince.Seconds, 3);   // when he stopped

            var type = d.W.GetComponentRO<ThermalState>(d.E);
            var stood = HeatSource.Of(HeatSourceState.Standing, (SimInstant)double.NegativeInfinity, (SimInstant)double.NegativeInfinity);
            Assert.True(ThermalDriversOps.Signature(in type, d.Source, 0f, d.T) > ThermalDriversOps.Signature(in type, stood, 0f, d.T) + 0.2f);
            Assert.Equal(ThermalDriversOps.Signature(in type, stood, 0f, d.T + 400), ThermalDriversOps.Signature(in type, d.Source, 0f, d.T + 400), 3);
        }

        /// <summary>
        /// ⭐ An engine: a crewed one (a brain) starts Idle, runs while it drives, idles again 3 s after it stops — stamped when it stopped; a short pause in
        /// traffic keeps it Running (hysteresis). 🔴 Red-proof: no debounce ⇒ the 1 s pause flips it to Idle ⇒ red.
        /// </summary>
        [Fact]
        public void CE4658_ACrewedEngine_IdlesRunsAndIdles_AShortPauseKeepsItRunning()
        {
            var d = new Driven(engine: true, crewed: true);
            d.Run(1.0, 0f);
            Assert.Equal(HeatSourceState.Idle, d.Source.State);
            d.Run(10.0, 8f);
            Assert.Equal(HeatSourceState.Running, d.Source.State);
            double since = d.Source.Since.Seconds;
            d.Run(1.0, 0f);                                          // a pause in traffic
            d.Run(5.0, 8f);
            Assert.Equal(HeatSourceState.Running, d.Source.State);
            Assert.Equal(since, d.Source.Since.Seconds, 6);           // no new decision
            d.Run(5.0, 0f);
            Assert.Equal(HeatSourceState.Idle, d.Source.State);
            Assert.Equal(17.1, d.Source.Since.Seconds, 3);
        }

        /// <summary>
        /// ⭐ The rule is EDGE-triggered: a tank authored Off (parked, empty — or a crew on silent watch) stays Off while it stands, however long; it runs once it
        /// moves; and a destroyed one is Dead. 🔴 Red-proof: re-deciding from the crew every frame ⇒ the authored Off flips to Idle ⇒ red.
        /// </summary>
        [Fact]
        public void CE4658_AnAuthoredOff_HoldsUntilItMoves_AndADestroyedEngineIsDead()
        {
            var d = new Driven(engine: true, crewed: true, authored: HeatSource.Of(HeatSourceState.Off, SimInstant.Zero, SimInstant.Zero));
            d.Run(60.0, 0f);
            Assert.Equal(HeatSourceState.Off, d.Source.State);
            Assert.True(double.IsNegativeInfinity(d.Source.Since.Seconds));   // times reset at load: off "forever" = cold
            d.Run(2.0, 6f);
            Assert.Equal(HeatSourceState.Running, d.Source.State);
            d.W.AddComponent(d.E, new Fdp.Toolkit.Combat.Components.Health { Current = 0f, Max = 100f });
            d.Run(0.2, 0f);
            Assert.Equal(HeatSourceState.Dead, d.Source.State);
        }

        /// <summary>
        /// ⭐ TD-14: a tank that idled 2 min and drives off hands its stand to terrain's writer — ONE event, its engine time, hot end rear, power class tank; a
        /// stand with the engine off leaves none. 🔴 Red-proof: drop the <c>LeaveStand</c> call ⇒ no event ⇒ red.
        /// </summary>
        [Fact]
        public void CE4658_LeavingAStandItIdledAt_HandsTerrainAWarmMark()
        {
            var d = new Driven(engine: true, crewed: true);
            d.Run(120.0, 0f);
            d.Run(0.1, 8f);                       // the step it leaves (published, then swapped readable)
            var marks = new System.Collections.Generic.List<Fdp.Toolkit.Terrain.GroundHeatMarkEvent>();
            foreach (ref readonly var m in d.W.ReadEvents<Fdp.Toolkit.Terrain.GroundHeatMarkEvent>()) marks.Add(m);
            Assert.Single(marks);
            var mark = marks[0].Mark;
            Assert.InRange(mark.EngineOnSeconds, 119f, 121f);
            Assert.Equal(Fdp.Toolkit.Terrain.HeatMarkEnd.Rear, mark.HotEnd);
            Assert.Equal(Fdp.Toolkit.Terrain.ThermalPowerClass.Tank, mark.PowerClass);

            var off = new Driven(engine: true, crewed: false);
            off.Run(120.0, 0f);
            off.Run(0.1, 8f);
            int n = 0;
            foreach (ref readonly var _ in off.W.ReadEvents<Fdp.Toolkit.Terrain.GroundHeatMarkEvent>()) n++;
            Assert.Equal(0, n);
        }

        /// <summary>⭐ 0 B in the steady state (CLAUDE.md hot path): a standing and a driving entity allocate nothing per frame once tracked.</summary>
        [Fact]
        public void CE4658_TheWriter_AllocatesNothingInTheSteadyState()
        {
            var d = new Driven(engine: true, crewed: true);
            d.Run(1.0, 0f);
            long before = GC.GetAllocatedBytesForCurrentThread();
            d.Run(5.0, 0f);
            d.Run(5.0, 8f);
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        /// <summary>
        /// ⭐ <c>CE-1358</c> (DESIGN_Weapon_Layers §5k) — a hot STATION weapon carried in a vehicle (a door gun on its helicopter) warms the vehicle for the thermal
        /// sensor; the hottest one counts and it goes once the gun cools. ⭐ CE-4659: a unit's OWN mounts are computed from WeaponAmmo's last burst
        /// (<c>WeaponAmmoMirrorSystemTests.CE4659_*</c>), not here. 🔴 Red-proof: drop the station pass ⇒ the door gun's heat is never seen.
        /// </summary>
        [Fact]
        public void CE1358_AHotStationWeapon_WarmsItsVehicle()
        {
            var (w, e) = World();
            w.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponState>();
            w.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
            var sys = new ThermalHeatSystem();

            var doorGun = w.CreateEntity();
            w.AddComponent(doorGun, new Fdp.Toolkit.Combat.Components.WeaponState { Heat = 0.9f });
            w.AddComponent(doorGun, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = e });
            var other = w.CreateEntity();
            w.AddComponent(other, new Fdp.Toolkit.Combat.Components.WeaponState { Heat = 0.5f });
            w.AddComponent(other, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = e });
            Step(w, sys, 0f);
            var t = w.GetComponentRO<ThermalState>(e);
            Assert.Equal(0.9f, t.BarrelHeat, 3);
            Assert.Equal(0.3f + 0.9f * 0.7f, ThermalDriversOps.Signature(in t, default, 0f, 0.0, t.BarrelHeat), 3);   // the barrel is hotter than the cold body

            w.GetComponentRW<Fdp.Toolkit.Combat.Components.WeaponState>(doorGun).Heat = 0f;   // it cooled
            Step(w, sys, 0f);
            Assert.Equal(0.5f, w.GetComponentRO<ThermalState>(e).BarrelHeat, 3);
        }
    }
}
