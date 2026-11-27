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
using Hrot.AI.Behaviors.Devices.Rangefinder;
using Hrot.CGF.Configuration;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ Simulated devices S1c (docs/designs/devices/DESIGN_Simulated_Devices.md §3.1 D-8 / D-57, §3.3 R7-11, §3.5 R-4300, §3.7, §3.8) — THE LASER
    /// RANGEFINDER's logic: a press on RANGE (its <see cref="DeviceInput"/> press counter moving) turns the laser on; the spot that answers that
    /// request writes <see cref="RangefinderState.LastRange"/> and the panel, and turns the laser off; a re-read frame never presses twice; a new
    /// operator epoch re-bases the counters (no press fires); in pause (dt = 0) the mode and the menu still step. The last rail runs it end to end
    /// — the production registry, the device tick, the muscle's <see cref="LaserSpotSystem"/> — and pins 0 B per steady frame.
    /// </summary>
    public sealed class RangefinderBehaviorTests
    {
        // ── the node, ticked directly ──────────────────────────────────────────────────────────────────────────────────────────────

        private sealed class Node : IDisposable
        {
            public readonly EntityRepository W = new();
            public readonly Entity D;
            public RangefinderBehavior.RangefinderParams P;
            public RangefinderBehavior.RangefinderWork Ws;
            private uint _seq;

            public Node()
            {
                SimHostComponentRegistry.RegisterAll(W);
                CognitiveComponentRegistry.RegisterAll(W);
                BlueprintTierTable.RegisterAll(W);
                D = W.CreateEntity();
                W.AddComponent(D, new BehaviorState());
                W.AddComponent(D, new DeviceInfo { DeviceType = 77, Behavior = new FixedString64(BehaviorNames.DeviceRangefinder) });
                var bb = default(RangefinderBehavior.RangefinderBlackboard);
                RangefinderBehavior.Resolve(new NoParams(), ref bb, W, D);
                P = bb.Params;
            }

            public RangefinderState State => UnitMemory.Get<RangefinderState>(W, D);
            public LaserPointing Laser => W.HasComponent<LaserPointing>(D) ? W.GetComponentRO<LaserPointing>(D) : default;
            public DevicePanel Panel => W.HasComponent<DevicePanel>(D) ? W.GetComponentRO<DevicePanel>(D) : default;

            /// <summary>The operator's frame: the press counters as the station holds them (a counter moving = a press).</summary>
            public void Input(byte range = 0, byte mode = 0, byte menu = 0, byte select = 0)
            {
                var c = new DeviceControl();
                c.Presses[RangefinderChannels.Range] = range; c.Presses[RangefinderChannels.Mode] = mode;
                c.Presses[RangefinderChannels.Menu] = menu; c.Presses[RangefinderChannels.Select] = select;
                var input = new DeviceInput { Seq = ++_seq, Control = c };
                if (W.HasComponent<DeviceInput>(D)) W.SetComponent(D, input); else W.AddComponent(D, input);
            }

            public void Operated(uint epoch)
            {
                var row = new PossessedBy { StationNodeId = (StationId)5, Mode = PossessionMode.Operated, Epoch = epoch };
                if (W.HasComponent<PossessedBy>(D)) W.SetComponent(D, row); else W.AddComponent(D, row);
            }

            /// <summary>The muscle's answer to request <paramref name="seq"/>: a return at <paramref name="range"/> m (or none).</summary>
            public void Answer(uint seq, float? range, Entity hit = default, bool preview = false)
            {
                var s = new LaserSpot { PointingSeq = seq, Preview = preview ? (byte)1 : (byte)0 };
                if (range is { } r) { s.Points[0] = new LaserSpotPoint { Point = new Vector3(r, 0f, 0f), Range = r, Strength = 0.5f, Hit = hit, Part = -1 }; s.Count = 1; }
                if (W.HasComponent<LaserSpot>(D)) W.SetComponent(D, s); else W.AddComponent(D, s);
            }

            public void Tick() => Assert.Equal(Fbt.NodeStatus.Running, RangefinderBehavior.Tick(ref P, ref Ws, D, W));

            public void Dispose() => W.Dispose();
        }

        [Fact]
        public void S1c_ARangePress_TurnsTheLaserOn_TheAnsweringSpotWritesTheRangeAndThePanel_AndTheLaserGoesOff()
        {
            using var n = new Node();
            n.Operated(1);
            n.Input(range: 0);
            n.Tick();                                         // based: nothing pressed yet
            Assert.Equal(0, n.Laser.On);
            Assert.Equal("— m", n.Panel.Text[RangefinderPanel.TextRange].ToString());

            n.Input(range: 1);                                // RANGE pressed
            n.Tick();
            Assert.Equal(1, n.Laser.On);
            uint seq = n.Laser.Seq;
            Assert.NotEqual(0u, seq);
            Assert.Equal(RangefinderPanel.LampLaser, n.Panel.LampBits & RangefinderPanel.LampLaser);

            n.Answer(seq - 1, 999f);                          // a spot left from an EARLIER request: ignored
            n.Tick();
            Assert.Equal(0, n.State.HasRange);
            Assert.Equal(1, n.Laser.On);

            var marker = n.W.CreateEntity();
            n.Answer(seq, 452.4f, marker);                    // the answer to THIS request
            n.Tick();
            Assert.Equal(1, n.State.HasRange);
            Assert.Equal(452.4f, n.State.LastRange, 3);
            Assert.Equal(marker, n.State.LastHit);
            Assert.Equal(0, n.Laser.On);                      // SINGLE: one measurement
            Assert.Equal("452 m", n.Panel.Text[RangefinderPanel.TextRange].ToString());
            Assert.Equal(452.4f, n.Panel.Values[RangefinderPanel.ValueRange], 3);
            Assert.Equal(RangefinderPanel.LampRange, n.Panel.LampBits & (RangefinderPanel.LampRange | RangefinderPanel.LampLaser));
        }

        [Fact]
        public void S1c_AReReadFrame_NeverPressesTwice_AndThePanelIsWrittenOnlyOnChange()
        {
            using var n = new Node();
            n.Input(range: 3);
            n.Tick();                                         // based on 3
            n.Input(range: 4);
            n.Tick();
            uint seq = n.Laser.Seq;
            uint revision = n.Panel.Revision;
            for (int i = 0; i < 5; i++) n.Tick();             // the same frame read again and again
            Assert.Equal(seq, n.Laser.Seq);
            Assert.Equal(revision, n.Panel.Revision);
        }

        [Fact]
        public void S1c_ANewOperatorEpoch_ReBasesThePressCounters_NoPressFires()
        {
            using var n = new Node();
            n.Operated(1);
            n.Input(range: 2, mode: 2);
            n.Tick();
            Assert.Equal(0, n.Laser.On);
            n.Operated(2);                                    // another station took the device: its counters are its own
            n.Input(range: 9, mode: 7);
            n.Tick();
            Assert.Equal(0, n.Laser.On);
            Assert.Equal(RangefinderMode.Single, n.State.Mode);
            Assert.Equal(0u, n.Laser.Seq);
            n.Input(range: 10, mode: 7);                      // and his next press fires
            n.Tick();
            Assert.Equal(1, n.Laser.On);
        }

        [Fact]
        public void S1c_InputAppearingAfterNone_ReBases_NoPhantomPress()
        {
            using var n = new Node();
            n.Tick();                                         // no operator frame at all
            n.Input(range: 5, menu: 2);                       // the first frame of a new station arrives already counted
            n.Tick();
            Assert.Equal(0, n.Laser.On);
            Assert.Equal(0, n.State.MenuOpen);
        }

        [Fact]
        public void S1c_ModeMenuAndSelect_StepTheState_AndThePanelFollows()
        {
            using var n = new Node();
            n.Input();
            n.Tick();
            n.Input(mode: 1);
            n.Tick();
            Assert.Equal(RangefinderMode.Continuous, n.State.Mode);
            Assert.Equal("CONTINUOUS", n.Panel.Text[RangefinderPanel.TextMode].ToString());
            n.Input(mode: 1, menu: 1);                        // MENU opens on MODE
            n.Tick();
            Assert.Equal(1, n.State.MenuOpen);
            Assert.Equal(RangefinderMenuItem.Mode, n.State.Menu);
            Assert.Equal("MENU ▸ MODE", n.Panel.Text[RangefinderPanel.TextMenu].ToString());
            n.Input(mode: 1, menu: 2, select: 1);             // cursor → POWER, SELECT steps it
            n.Tick();
            Assert.Equal(RangefinderMenuItem.Power, n.State.Menu);
            Assert.Equal(RangefinderPower.High, n.State.Power);
            Assert.Equal("POWER HIGH", n.Panel.Text[RangefinderPanel.TextPower].ToString());
            n.Input(mode: 1, menu: 3, select: 2);             // cursor → EXIT, SELECT closes
            n.Tick();
            Assert.Equal(0, n.State.MenuOpen);
            Assert.Equal(0u, n.Panel.LampBits & RangefinderPanel.LampMenu);
            n.Input(mode: 1, menu: 3, select: 2, range: 1);   // CONTINUOUS: ranging on, at HIGH power
            n.Tick();
            Assert.Equal(1, n.Laser.On);
            Assert.Equal(RangefinderBehavior.HighPower, n.Laser.Power);
            n.Answer(n.Laser.Seq, 300f);
            n.Tick();
            Assert.Equal(1, n.Laser.On);                      // CONTINUOUS keeps ranging
            Assert.Equal(300f, n.State.LastRange);
            n.Input(mode: 1, menu: 3, select: 2, range: 2);   // RANGE again stops it
            n.Tick();
            Assert.Equal(0, n.Laser.On);
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
            public readonly Entity D, Target;
            private uint _seq;

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

                D = W.CreateEntity();
                W.AddComponent(D, new SimTransform { Position = new Vector3(0f, 0f, 1.6f), Rotation = Quaternion.Identity });
                W.AddComponent(D, new BehaviorState());
                W.AddComponent(D, new DeviceInfo { DeviceType = 77, Behavior = new FixedString64(BehaviorNames.DeviceRangefinder) });

                Target = W.CreateEntity();   // a marker 450 m east
                W.AddComponent(Target, new SimTransform { Position = new Vector3(450f, 0f, 0f), Rotation = Quaternion.Identity });
                W.AddComponent(Target, new PhysicsCollider { Radius = 0.5f, Height = 2f, CollisionLayer = 1 });
                Grid.Add(Target, new Vector2(450f, 0f));
                W.SetSingleton(new SpatialGridData { Grid = Grid });
            }

            public void Press(byte range, byte mode = 0)
            {
                var c = new DeviceControl();
                c.Presses[RangefinderChannels.Range] = range; c.Presses[RangefinderChannels.Mode] = mode;
                var input = new DeviceInput { Seq = ++_seq, Control = c };
                if (W.HasComponent<DeviceInput>(D)) W.SetComponent(D, input); else W.AddComponent(D, input);
            }

            /// <summary>One frame of the device's node: the device tick (Simulation), then the muscle's laser (PostSimulation).</summary>
            public void Frame(float dt)
            {
                W.Bus.SwapBuffers();
                Devices.Execute(W, dt);
                Laser.Execute(W, dt);
            }

            public RangefinderState State => UnitMemory.Get<RangefinderState>(W, D);

            public void Dispose() { Grid.Dispose(); W.Dispose(); }
        }

        [Fact]
        public void S1c_EndToEnd_InPause_ARangePressMeasuresTheMarker_AsAPreview_AndTheModeSteps()
        {
            using var r = new Rig();
            r.Press(0);
            for (int i = 0; i < 3; i++) r.Frame(0f);          // a session that boots paused: the device starts, bases its counters
            Assert.Equal(1, r.Devices.Starts);
            r.Press(1);
            for (int i = 0; i < 3; i++) r.Frame(0f);          // device: laser on → muscle: the spot → device: the range
            var s = r.State;
            Assert.Equal(1, s.HasRange);
            Assert.InRange(s.LastRange, 449f, 450f);
            Assert.Equal(r.Target, s.LastHit);
            Assert.Equal(1, s.RangePreview);                   // measured in pause: a preview
            Assert.Equal(0, r.W.GetComponentRO<LaserPointing>(r.D).On);
            Assert.Equal(((int)MathF.Round(s.LastRange)).ToString(System.Globalization.CultureInfo.InvariantCulture) + " m",
                         r.W.GetComponentRO<DevicePanel>(r.D).Text[RangefinderPanel.TextRange].ToString());
            r.Press(1, mode: 1);                               // still paused: MODE steps
            r.Frame(0f);
            Assert.Equal(RangefinderMode.Continuous, r.State.Mode);
        }

        [Fact]
        public void S1c_EndToEnd_ASteadyFrame_AllocatesNothing()
        {
            using var r = new Rig();
            r.Press(0);
            for (int i = 0; i < 3; i++) r.Frame(0f);
            r.Press(1, mode: 1);                               // CONTINUOUS ranging: the laser stays on, the muscle traces every frame
            r.Frame(0f);
            for (int i = 0; i < 30; i++) r.Frame(i % 2 == 0 ? 0.016f : 0f);   // warm (JIT tiering, as the S1a rail)
            Assert.Equal(1, r.W.GetComponentRO<LaserPointing>(r.D).On);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++) r.Frame(i % 2 == 0 ? 0.016f : 0f);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal(1, r.State.HasRange);
        }
    }
}
