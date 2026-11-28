using System;
using System.Numerics;
using CarKinem.Spatial;
using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Devices;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Hrot.AI.Behaviors.Devices.Designator;
using Hrot.AI.Behaviors.Devices.Rangefinder;
using Hrot.CGF.Configuration;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ Simulated devices SD-S5c (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13d D-113 – D-115) — THE LASER TARGET DESIGNATOR's logic: the type's data
    /// (TKB 643: POWER · LASE · CODE · RANGE in wire order, 10×, 60 s / 30 s, code 1688); the PRF code's stepping (1111 … 1788, each digit's range); the procedure
    /// per press and phase (POWER, CODE only in STANDBY, LASE ⇄, the duty limit ⇒ COOLING ⇒ STANDBY, LASE refused while cooling); the beam (band 2 + its code while
    /// lasing, uncoded for a RANGE pulse); the PAUSE (R-4300: a LASE is queued for the first live frame, pressed again un-queued; a lase running into a pause keeps
    /// its laser on, its spot a Preview — never a designation — and its timer stands); and END TO END through the production registry, the device tick and the
    /// muscle's laser: the lase meets the marker and is a live designation, a paused frame is not; the rangefinder's SEND reaches the linked designator; 0 B per
    /// steady frame. The end-to-end rails run under <c>HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1</c> too. 🔴 Red-proof (compiles): the duty-limit branch removed from
    /// <c>DesignatorBehavior.Tick</c> ⇒ <see cref="SDS5c_TheProcedure_PowerCodeLase_TheDutyLimitCools_AndLaseAgain"/> fails.
    /// </summary>
    public sealed class DesignatorBehaviorTests
    {
        private const float Dt = 1f / 60f;

        // ── the device, ticked directly ────────────────────────────────────────────────────────────────────────────────────────────────

        private sealed class Node : IDisposable
        {
            public readonly EntityRepository W = new();
            public readonly Entity D;
            public DesignatorBehavior.DesignatorParams P;
            public DesignatorBehavior.DesignatorWork Ws;
            public double Time = 100;
            private readonly byte[] _presses = new byte[DesignatorChannels.Count];

            public Node()
            {
                SimHostComponentRegistry.RegisterAll(W);
                CognitiveComponentRegistry.RegisterAll(W);
                BlueprintTierTable.RegisterAll(W);
                D = W.CreateEntity();
                W.AddComponent(D, new BehaviorState());
                W.AddComponent(D, new DeviceInfo { DeviceType = 77, Behavior = new FixedString64(BehaviorNames.DeviceDesignator) });
                var bb = default(DesignatorBehavior.DesignatorBlackboard);
                DesignatorBehavior.Resolve(new NoParams(), ref bb, W, D);
                P = bb.Params;
                Input();
                Tick();   // the first read BASES the press counters (R7-11) — no press fires on it
            }

            public DesignatorState State => UnitMemory.Get<DesignatorState>(W, D);
            public LaserPointing Laser => W.HasComponent<LaserPointing>(D) ? W.GetComponentRO<LaserPointing>(D) : default;

            /// <summary>Presses <paramref name="channel"/> <paramref name="times"/> (the station's counter moving).</summary>
            public void Press(int channel, int times = 1) { _presses[channel] += (byte)times; Input(); }

            private void Input()
            {
                var c = new DeviceControl();
                for (int i = 0; i < _presses.Length; i++) c.Presses[i] = _presses[i];
                var input = new DeviceInput { Control = c };
                if (W.HasComponent<DeviceInput>(D)) W.SetComponent(D, input); else W.AddComponent(D, input);
            }

            /// <summary>One frame of <paramref name="dt"/> (0 = paused: sim time stands).</summary>
            public void Tick(float dt = Dt)
            {
                Time += dt;
                W.SetSingleton(new GlobalTime { TotalTime = Time, DeltaTime = dt });
                Assert.Equal(Fbt.NodeStatus.Running, DesignatorBehavior.Tick(ref P, ref Ws, D, W));
            }

            /// <summary>Live frames for <paramref name="seconds"/> of sim time.</summary>
            public void Run(double seconds) { for (int i = 0, n = (int)Math.Round(seconds / Dt); i < n; i++) Tick(); }

            /// <summary>The muscle's answer to the current request: a return at <paramref name="range"/> m.</summary>
            public void Answer(float range, bool preview = false)
            {
                var s = new LaserSpot { PointingSeq = Laser.Seq, Preview = preview ? (byte)1 : (byte)0, Count = 1 };
                s.Points[0] = new LaserSpotPoint { Point = new Vector3(range, 0f, 0f), Range = range, Strength = 0.5f, Part = -1 };
                if (W.HasComponent<LaserSpot>(D)) W.SetComponent(D, s); else W.AddComponent(D, s);
            }

            public void Dispose() => W.Dispose();
        }

        [Fact]
        public void SDS5c_TheType_NamesItsBehaviour_ItsButtonsInWireOrder_ItsOptic_AndItsDutyCycle()
        {
            var tkb = Hrot.Map.Common.HrotEnvironment.CreateTkb();
            var t = tkb.GetByType(Hrot.Map.Common.TkbEntityTypes.Device_LaserDesignator)!;
            var dev = t.GetDescriptor<Fdp.Toolkit.Tkb.Domain.DeviceDefDto>()!;
            Assert.Equal(BehaviorNames.DeviceDesignator, dev.Behavior);
            Assert.Equal(DesignatorChannels.Count, dev.Elements.Count);
            Assert.Equal(DesignatorChannels.Power, dev.ButtonOf("POWER"));
            Assert.Equal(DesignatorChannels.Lase, dev.ButtonOf("LASE"));
            Assert.Equal(DesignatorChannels.Code, dev.ButtonOf("CODE"));
            Assert.Equal(DesignatorChannels.Range, dev.ButtonOf("RANGE"));
            Assert.Equal(new[] { "P", "L", "C", "R" }, dev.Elements.ConvertAll(e => e.Hotkey).ToArray());   // the mockup's key boxes (D-124)
            Assert.Equal(new[] { DesignatorBehavior.Magnification }, dev.Magnifications);
            Assert.Contains("TargetLocation", dev.Messages);
            var duty = t.GetDescriptor<Fdp.Toolkit.Tkb.Domain.LaserDesignatorDto>()!;
            Assert.Equal(60f, duty.LaseSeconds);
            Assert.Equal(30f, duty.CoolSeconds);
            Assert.Equal((ushort)1688, duty.DefaultCode);

            // the resolver takes the TYPE's data
            using var w = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(w);
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            w.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
            var custom = new Fdp.Interfaces.TkbTemplate("Short designator", 9999);
            custom.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.LaserDesignatorDto { LaseSeconds = 5f, CoolSeconds = 2f, DefaultCode = 1511 });
            db.Register(custom);
            var d = w.CreateEntity();
            w.AddComponent(d, new DeviceInfo { DeviceType = 9999, Behavior = new FixedString64(BehaviorNames.DeviceDesignator) });
            var bb = default(DesignatorBehavior.DesignatorBlackboard);
            DesignatorBehavior.Resolve(new NoParams(), ref bb, w, d);
            Assert.Equal(5f, bb.Params.LaseSeconds);
            Assert.Equal(2f, bb.Params.CoolSeconds);
            Assert.Equal((ushort)1511, bb.Params.DefaultCode);
        }

        [Fact]
        public void SDS5c_TheCode_StepsLikeAnOdometer_OverEachDigitsProvenRange_AndWraps()
        {
            Assert.Equal((ushort)1112, DesignatorCodes.Next(1111));
            Assert.Equal((ushort)1121, DesignatorCodes.Next(1118));       // the last digit 1–8
            Assert.Equal((ushort)1211, DesignatorCodes.Next(1188));       // the third 1–8
            Assert.Equal((ushort)1711, DesignatorCodes.Next(1688));
            Assert.Equal((ushort)1111, DesignatorCodes.Next(1788));       // the second 1–7: wraps
            Assert.Equal(DesignatorCodes.First, DesignatorCodes.Next(1999));
            int n = 0, c = DesignatorCodes.First;
            do { Assert.True(DesignatorCodes.IsValid(c), $"{c}"); c = DesignatorCodes.Next(c); n++; } while (c != DesignatorCodes.First);
            Assert.Equal(7 * 8 * 8, n);                                    // every valid code, once
        }

        [Fact]
        public void SDS5c_TheProcedure_PowerCodeLase_TheDutyLimitCools_AndLaseAgain()
        {
            using var n = new Node();
            n.Tick();
            Assert.Equal(DesignatorPhase.Off, n.State.Phase);
            Assert.Equal((ushort)1688, n.State.Code);                      // the type's default (a bare rail: the resolver's)
            n.Press(DesignatorChannels.Code); n.Tick();
            Assert.Equal((ushort)1688, n.State.Code);                      // OFF: CODE does nothing
            n.Press(DesignatorChannels.Lase); n.Tick();
            Assert.Equal(DesignatorPhase.Off, n.State.Phase);              // OFF: LASE does nothing

            n.Press(DesignatorChannels.Power); n.Tick();
            Assert.Equal(DesignatorPhase.Standby, n.State.Phase);
            n.Press(DesignatorChannels.Code, 2); n.Tick();
            Assert.Equal((ushort)1712, n.State.Code);                      // STANDBY: two steps
            Assert.Equal(0, n.Laser.On);

            n.Press(DesignatorChannels.Lase); n.Tick();
            Assert.Equal(DesignatorPhase.Lasing, n.State.Phase);
            var laser = n.Laser;
            Assert.Equal(1, laser.On);
            Assert.Equal(LaserBands.Designator, laser.Band);
            Assert.Equal((ushort)1712, laser.Code);                        // D-114 — the code rides the beam
            n.Press(DesignatorChannels.Code); n.Tick();
            Assert.Equal((ushort)1712, n.State.Code);                      // LASING: CODE refused
            n.Answer(1240f); n.Tick();
            Assert.Equal(1240f, n.State.LastRange);                        // the lase ranges continuously
            Assert.Equal(1, n.State.Designating);
            Assert.Equal(60.0 - 2 * Dt, n.State.LaseLeft(n.Time), 3);   // two live frames since the lase began

            n.Run(60.0);                                                   // past the duty limit
            Assert.Equal(DesignatorPhase.Cooling, n.State.Phase);
            Assert.Equal(0, n.Laser.On);
            Assert.Equal(0, n.Laser.Code);
            Assert.Equal(0, n.State.Designating);
            n.Press(DesignatorChannels.Lase); n.Tick();
            Assert.Equal(DesignatorPhase.Cooling, n.State.Phase);          // COOLING: LASE refused
            n.Press(DesignatorChannels.Code); n.Tick();
            Assert.Equal((ushort)1712, n.State.Code);                      // COOLING: CODE refused
            Assert.InRange(n.State.CoolLeft(n.Time), 20.0, 30.0);

            n.Run(30.0);
            Assert.Equal(DesignatorPhase.Standby, n.State.Phase);
            n.Press(DesignatorChannels.Lase); n.Tick();
            Assert.Equal(DesignatorPhase.Lasing, n.State.Phase);           // LASE again
            n.Press(DesignatorChannels.Lase); n.Tick();
            Assert.Equal(DesignatorPhase.Standby, n.State.Phase);          // and stopped by hand: STANDBY, no cooling
            Assert.Equal(0, n.Laser.On);
            n.Press(DesignatorChannels.Power); n.Tick();
            Assert.Equal(DesignatorPhase.Off, n.State.Phase);
        }

        [Fact]
        public void SDS5c_ARangePulse_IsOneUncodedMeasurement()
        {
            using var n = new Node();
            n.Press(DesignatorChannels.Power); n.Tick();
            uint seq = n.Laser.Seq;
            n.Press(DesignatorChannels.Range); n.Tick();
            Assert.Equal(1, n.Laser.On);
            Assert.Equal(0, n.Laser.Code);                                 // a ranging pulse is no designation
            Assert.Equal(seq + 1, n.Laser.Seq);
            Assert.Equal(1, n.State.Ranging);
            n.Answer(452f); n.Tick();
            Assert.Equal(1, n.State.HasRange);
            Assert.Equal(452f, n.State.LastRange);
            Assert.Equal(0, n.State.Ranging);
            Assert.Equal(0, n.Laser.On);                                   // one measurement: off
            Assert.Equal(0, n.State.Designating);
            Assert.Equal(DesignatorPhase.Standby, n.State.Phase);
        }

        [Fact]
        public void SDS5c_InPause_ALaseIsQueued_ThenStartsOnTheFirstLiveFrame_AndPressedAgainIsUnQueued()
        {
            using var n = new Node();
            n.Press(DesignatorChannels.Power); n.Tick();
            n.Press(DesignatorChannels.Lase); n.Tick(0f);                  // pressed in a pause
            Assert.Equal(DesignatorPhase.Standby, n.State.Phase);
            Assert.Equal(1, n.State.Queued);
            Assert.Equal(0, n.Laser.On);                                   // ⛔ no designation in pause
            for (int i = 0; i < 10; i++) n.Tick(0f);
            Assert.Equal(0, n.Laser.On);
            Assert.Equal(1, n.State.Queued);
            n.Press(DesignatorChannels.Code); n.Tick(0f);
            Assert.Equal((ushort)1711, n.State.Code);                      // the device's own state still changes in pause
            n.Tick();                                                      // the first live frame
            Assert.Equal(DesignatorPhase.Lasing, n.State.Phase);
            Assert.Equal(0, n.State.Queued);
            Assert.Equal(1, n.Laser.On);
            Assert.Equal((ushort)1711, n.Laser.Code);

            n.Press(DesignatorChannels.Lase); n.Tick();                    // stop
            n.Press(DesignatorChannels.Lase); n.Tick(0f);                  // queued …
            n.Press(DesignatorChannels.Lase); n.Tick(0f);                  // … and pressed again: un-queued
            Assert.Equal(0, n.State.Queued);
            n.Tick();
            Assert.Equal(DesignatorPhase.Standby, n.State.Phase);
            Assert.Equal(0, n.Laser.On);
        }

        [Fact]
        public void SDS5c_ALaseRunningIntoAPause_KeepsItsLaser_IsNoDesignation_AndItsTimerStandsStill()
        {
            using var n = new Node();
            n.Press(DesignatorChannels.Power); n.Tick();
            n.Press(DesignatorChannels.Lase); n.Tick();
            n.Answer(800f); n.Run(10.0);
            Assert.Equal(1, n.State.Designating);
            double left = n.State.LaseLeft(n.Time);
            for (int i = 0; i < 300; i++) n.Tick(0f);                      // a long pause
            Assert.Equal(DesignatorPhase.Lasing, n.State.Phase);
            Assert.Equal(1, n.Laser.On);                                   // the laser stays on — the muscle writes its spot Preview
            Assert.Equal(0, n.State.Designating);                          // ⛔ never a designation on a paused frame (even before the spot turns Preview)
            Assert.Equal(left, n.State.LaseLeft(n.Time), 9);               // the timer stands
            n.Answer(800f, preview: true); n.Tick(0f);
            Assert.Equal(1, n.State.RangePreview);
            Assert.Equal(0, n.State.Designating);
            n.Answer(800f); n.Tick();
            Assert.Equal(1, n.State.Designating);                          // live again
        }

        // ── end to end: the production registry, the device tick, the muscle ───────────────────────────────────────────────────────────

        private sealed class Rig : IDisposable
        {
            public readonly EntityRepository W = new();
            public readonly BehaviorRegistry Registry = new();
            public readonly BehaviorIngressSystem Ingress;
            public readonly DeviceTickSystem Devices;
            public readonly LaserSpotSystem Laser = new();
            public SpatialHashGrid Grid;
            public readonly Entity D, R, Target;
            public double Time = 50;
            private readonly byte[] _presses = new byte[DesignatorChannels.Count];
            private byte _range, _send;

            public Rig()
            {
                SimHostComponentRegistry.RegisterAll(W);
                CognitiveComponentRegistry.RegisterAll(W);
                BlueprintTierTable.RegisterAll(W);
                CgfBehaviorSetup.LoadFromAiAssembly(Registry);
                Ingress = new BehaviorIngressSystem(Registry);
                Devices = new DeviceTickSystem(Registry, Ingress);
                Grid = SpatialHashGrid.Create(100, 100, 20f, 1000, Allocator.Persistent, originX: -1000f, originY: -1000f);
                Grid.Clear();
                D = DeviceOf(BehaviorNames.DeviceDesignator, 643);
                R = DeviceOf(BehaviorNames.DeviceRangefinder, 640);
                W.AddComponent(R, DeviceLinks.To(EntityRef.Of(W, D)));     // the rangefinder sends to the designator
                W.AddComponent(D, DeviceLinks.To(EntityRef.Of(W, R)));     // paired back
                Target = W.CreateEntity();   // a marker 450 m east
                W.AddComponent(Target, new SimTransform { Position = new Vector3(450f, 0f, 0f), Rotation = Quaternion.Identity });
                W.AddComponent(Target, new PhysicsCollider { Radius = 0.5f, Height = 2f, CollisionLayer = 1 });
                Grid.Add(Target, new Vector2(450f, 0f));
                W.SetSingleton(new SpatialGridData { Grid = Grid });
                Press();
            }

            private Entity DeviceOf(string behavior, long type)
            {
                var d = W.CreateEntity();
                W.AddComponent(d, new SimTransform { Position = new Vector3(0f, 0f, 1.6f), Rotation = Quaternion.Identity });
                W.AddComponent(d, new BehaviorState());
                W.SetAuthority<BehaviorState>(d, true);
                W.AddComponent(d, new DeviceInfo { DeviceType = type, Behavior = new FixedString64(behavior) });
                W.AddComponent(d, new NetworkIdentity { Value = 7100 + d.Index });
                return d;
            }

            public void Press(int channel = -1, int times = 1)
            {
                if (channel >= 0) _presses[channel] += (byte)times;
                var c = new DeviceControl();
                for (int i = 0; i < _presses.Length; i++) c.Presses[i] = _presses[i];
                Set(D, new DeviceInput { Control = c });
                var rc = new DeviceControl();
                rc.Presses[RangefinderChannels.Range] = _range; rc.Presses[RangefinderChannels.Send] = _send;
                Set(R, new DeviceInput { Control = rc });
            }

            public void RangefinderPress(bool range = false, bool send = false)
            {
                if (range) _range++;
                if (send) _send++;
                Press();
            }

            private void Set(Entity e, DeviceInput v) { if (W.HasComponent<DeviceInput>(e)) W.SetComponent(e, v); else W.AddComponent(e, v); }

            /// <summary>One frame of the device's node (as the kernel): a new tick, the device tick (Simulation), then the muscle's laser (PostSimulation).</summary>
            public void Frame(float dt = Dt)
            {
                Time += dt;
                W.SetSingleton(new GlobalTime { TotalTime = Time, DeltaTime = dt });
                W.Tick();
                W.Bus.SwapBuffers();
                Devices.Execute(W, dt);
                Laser.Execute(W, dt);
            }

            public DesignatorState State => UnitMemory.Get<DesignatorState>(W, D);
            public LaserSpot Spot => W.HasComponent<LaserSpot>(D) ? W.GetComponentRO<LaserSpot>(D) : default;

            public void Dispose() { Grid.Dispose(); W.Dispose(); }
        }

        [Fact]
        public void SDS5c_EndToEnd_TheLaseMeetsTheMarker_ALiveDesignation_APausedFrameIsAPreview_AndTheQueuedLaseStartsLive()
        {
            using var r = new Rig();
            for (int i = 0; i < 3; i++) r.Frame();                                    // started, based
            Assert.Equal(1, r.Devices.Starts >= 2 ? 1 : 0);
            r.Press(DesignatorChannels.Power);
            r.Frame();
            r.Press(DesignatorChannels.Lase);
            r.Frame(0f);                                                              // in a pause: queued
            for (int i = 0; i < 5; i++) r.Frame(0f);
            Assert.Equal(1, r.State.Queued);
            Assert.False(r.W.HasComponent<LaserSpot>(r.D) && r.Spot.Count > 0, "no spot while the lase waits");
            for (int i = 0; i < 3; i++) r.Frame();                                    // live: lase → the muscle's spot → the device reads it
            var st = r.State;
            Assert.Equal(DesignatorPhase.Lasing, st.Phase);
            Assert.Equal(1, st.Designating);
            Assert.Equal(r.Target, st.LastHit);
            Assert.InRange(st.LastRange, 449f, 450f);
            Assert.Equal(0, r.Spot.Preview);
            Assert.Equal((ushort)1688, r.W.GetComponentRO<LaserPointing>(r.D).Code);
            r.Frame(0f); r.Frame(0f);                                                 // a lase running into a pause
            Assert.Equal(1, r.Spot.Preview);                                          // the muscle writes it Preview
            Assert.Equal(0, r.State.Designating);
            Assert.Equal(1, r.State.RangePreview);
            Assert.Equal(DesignatorPhase.Lasing, r.State.Phase);
        }

        [Fact]
        public void SDS5c_EndToEnd_TheRangefindersSend_ReachesTheLinkedDesignator()
        {
            using var r = new Rig();
            for (int i = 0; i < 3; i++) r.Frame();
            r.Press(DesignatorChannels.Power);
            r.RangefinderPress(range: true);
            for (int i = 0; i < 3; i++) r.Frame();
            Assert.Equal(0, r.State.HasTarget);
            r.RangefinderPress(send: true);
            r.Frame();
            Assert.Equal(0, r.State.HasTarget);                                       // never in the sending tick
            r.Frame();
            var st = r.State;
            Assert.Equal(1, st.HasTarget);
            Assert.InRange(st.Target.Point.X, 449f, 450f);
            Assert.Equal(640, st.Target.FromType);
            Assert.Equal(EntityRef.Of(r.W, r.R), st.TargetFrom);
        }

        [Fact]
        public void SDS5c_EndToEnd_ASteadyLasingFrame_AllocatesNothing()
        {
            using var r = new Rig();
            for (int i = 0; i < 3; i++) r.Frame();
            r.Press(DesignatorChannels.Power); r.Frame();
            r.Press(DesignatorChannels.Lase);
            for (int i = 0; i < 60; i++) r.Frame(i % 2 == 0 ? Dt : 0f);               // warm (JIT tiering)
            Assert.Equal(DesignatorPhase.Lasing, r.State.Phase);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++) r.Frame(i % 2 == 0 ? Dt : 0f);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal(1, r.State.HasRange);
        }
    }
}
