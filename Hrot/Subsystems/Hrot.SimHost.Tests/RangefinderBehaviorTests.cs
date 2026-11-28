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
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Fdp.Toolkit.Replication.Components;
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

        /// <summary>
        /// ⭐⭐ S1e-1 (§3.1 R7-4 / D-26) — the two DEVICE TYPES the catalogue defines name the behaviour each runs and its channels by NAME in the order
        /// the behaviour reads them (the wire is the index), and their optic: ONE fact, pinned here, so a renamed / reordered channel or a moved zoom step
        /// cannot leave the TKB (what an adapter or a scenario presses by name) and the logic disagreeing.
        /// </summary>
        [Fact]
        public void S1e1_TheDeviceTypes_NameTheirBehaviour_TheirChannelsInWireOrder_AndTheirOptic()
        {
            var tkb = Hrot.Map.Common.HrotEnvironment.CreateTkb();
            var rf = tkb.GetByType(Hrot.Map.Common.TkbEntityTypes.Device_LaserRangefinder).GetDescriptor<Fdp.Toolkit.Tkb.Domain.DeviceDefDto>()!;
            Assert.Equal(BehaviorNames.DeviceRangefinder, rf.Behavior);
            Assert.Equal(RangefinderChannels.Count, rf.Channels.Count);
            Assert.Equal(RangefinderChannels.Range, rf.ChannelOf("RANGE"));
            Assert.Equal(RangefinderChannels.Mode, rf.ChannelOf("MODE"));
            Assert.Equal(RangefinderChannels.Menu, rf.ChannelOf("MENU"));
            Assert.Equal(RangefinderChannels.Select, rf.ChannelOf("select"));   // by name, any case
            Assert.Equal(new[] { RangefinderBehavior.Magnification }, rf.Magnifications);

            var bn = tkb.GetByType(Hrot.Map.Common.TkbEntityTypes.Device_Binoculars).GetDescriptor<Fdp.Toolkit.Tkb.Domain.DeviceDefDto>()!;
            Assert.Equal(BehaviorNames.DeviceBinoculars, bn.Behavior);
            Assert.Equal(Hrot.AI.Behaviors.Devices.Binoculars.BinocularsChannels.ZoomIn, bn.ChannelOf("ZOOM IN"));
            Assert.Equal(Hrot.AI.Behaviors.Devices.Binoculars.BinocularsChannels.ZoomOut, bn.ChannelOf("ZOOM OUT"));
            Assert.Equal(Hrot.AI.Behaviors.Devices.Binoculars.BinocularsZoom.Steps.ToArray(), bn.Magnifications);
            Assert.Equal(Hrot.AI.Behaviors.Devices.Binoculars.BinocularsZoom.DefaultStep, bn.DefaultStep);

            // the observer HMMWV's device station: operated by its commander seat, accepting only a device, starting with the rangefinder
            var seats = tkb.GetByType(Hrot.Map.Common.TkbEntityTypes.Truck_HMMWV_Observer).GetDescriptor<Fdp.Toolkit.Tkb.Domain.CarrierDefDto>()!.Seats;
            Assert.Equal(Fdp.Toolkit.Behavior.Components.SeatRole.Commander, seats[6].Role);
            Assert.Equal(Fdp.Toolkit.Behavior.Components.SeatRole.Station, seats[7].Role);
            Assert.Equal(6, seats[7].OperatedBy);
            Assert.Equal(Fdp.Toolkit.Tkb.Domain.MountKinds.Device, seats[7].Accepts);
            Assert.Equal(Hrot.Map.Common.TkbEntityTypes.Device_LaserRangefinder, seats[7].StartWith);
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
            // ⭐ S1d-2 — the HUD half (when asked): the brain's gizmo system with the rangefinder's projector, and the man who carries the device
            public readonly DebugPrimitiveBuffer? Buffer;
            public readonly StatelessGizmoSystem? Gizmos;
            public const int NetId = 4242;

            public Rig(bool hud = false)
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
                if (!hud) return;

                // the operator: a man possessed by station 5 carries the device (S1b-1's walk writes its Operated row)
                if (!W.IsComponentTypeRegistered<NetworkIdentity>()) W.RegisterComponent<NetworkIdentity>();
                W.AddComponent(D, new NetworkIdentity { Value = NetId });
                W.SetAuthority<BehaviorState>(D, true);                       // this node runs the device's brain
                var man = W.CreateEntity();
                W.AddComponent(man, new SimTransform { Position = new Vector3(0f, 0f, 0f), Rotation = Quaternion.Identity });
                W.AddComponent(man, new PersonCarrier());
                W.AddComponent(man, new PassengerBuffer());
                W.AddComponent(man, new PossessedBy { StationNodeId = (StationId)5, Mode = PossessionMode.Fps, Epoch = 1 });
                W.GetComponentRW<PassengerBuffer>(man).Passengers[4] = D;
                W.AddComponent(D, new IsEmbarkedTag { VehicleEntity = man, Seat = 4 });
                Buffer = new DebugPrimitiveBuffer(4096);
                var registry = new StatelessGizmoRegistry();
                registry.Register(new RangefinderHudGizmo(), new[] { typeof(DeviceInfo), typeof(PossessedBy) });
                Gizmos = new StatelessGizmoSystem(registry, Buffer);
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
                if (Gizmos == null) return;
                Buffer!.EndFrame(dt);                                        // the HUD projector (PostSimulation, after the muscle)
                Gizmos.Execute(W, dt);
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

        /// <summary>
        /// ⭐⭐ Simulated devices S1d-2 (§3.6 D-41 / R3-4 / R7-13) — END TO END, HUD: a carried rangefinder (the walk writes its Operated row), RANGE
        /// pressed ⇒ the brain's gizmo buffer holds ONE HudScope run for the device's network id whose text is the measured range; a view BOUND to the
        /// device keeps the run, an unbound one (the plain eye) does not; and nothing else appended inside the scope (R7-13, <c>ForeignAppendsInScope</c>).
        /// Run under <c>HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1</c> too (the device tick as the parallel phase).
        /// </summary>
        [Fact]
        public void S1d2_EndToEnd_ARangePress_PutsTheRangeInTheDevicesHudRun_ABoundViewKeepsIt_AnUnboundOneDoesNot()
        {
            using var r = new Rig(hud: true);
            for (int i = 0; i < 3; i++) r.Frame(0f);                          // the device starts, the walk writes its row (a clean input)
            Assert.Equal(PossessionMode.Operated, r.W.GetComponentRO<PossessedBy>(r.D).Mode);
            r.Press(0);
            r.Frame(0f);                                                       // based
            r.Press(1);
            for (int i = 0; i < 3; i++) r.Frame(0f);                          // laser → spot → range, paused (a preview)
            var st = r.State;
            Assert.Equal(1, st.HasRange);

            var frame = r.Buffer!.GetFrame();
            int scopes = 0, at = -1;
            for (int i = 0; i < frame.Length; i++)
                if (HudScopes.IsScope(in frame[i])) { scopes++; at = i; Assert.Equal(Rig.NetId, frame[i].HudScopeSource); }
            Assert.Equal(1, scopes);
            var run = frame.Slice(at + 1, frame[at].HudScopeCount);
            string expected = RangefinderHudGizmo.RangeText(st.LastRange).ToString();
            Assert.Matches(@"^4[45]\d m$", expected);
            bool hasRange = false, hasPreview = false;
            foreach (ref readonly var p in run)
            {
                Assert.Equal(CoordinateSpace.ViewCanvas, p.Space);
                if (p.Shape == DebugPrimitiveShape.Text && p.TextContent.ToString() == expected) hasRange = true;
                if (p.Shape == DebugPrimitiveShape.Text && p.TextContent.ToString() == "PREVIEW") hasPreview = true;
            }
            Assert.True(hasRange, $"the HUD run shows the range '{expected}'");
            Assert.True(hasPreview, "measured in pause: tagged PREVIEW");
            Assert.Equal(0, r.Buffer.ForeignAppendsInScope);                 // R7-13: one writer while the scope was open

            DebugPrimitive[] scratch = Array.Empty<DebugPrimitive>();
            var bound = HudScopes.Keep(frame, Rig.NetId, ref scratch);
            Assert.Equal(run.Length, bound.Length);                            // the bound view keeps the whole run (marker removed)
            var unbound = HudScopes.Keep(frame, HudScopes.None, ref scratch);
            foreach (ref readonly var p in unbound) Assert.NotEqual(CoordinateSpace.ViewCanvas, p.Space);
            var other = HudScopes.Keep(frame, Rig.NetId + 1, ref scratch);
            foreach (ref readonly var p in other) Assert.NotEqual(CoordinateSpace.ViewCanvas, p.Space);
        }

        /// <summary>
        /// ⭐⭐ Simulated devices S1d-3 (§3.1 R5-13 / D-48 — S1c AS BUILT ⑧'s deferral) — END TO END, the LOOK: the station fills the device frame's
        /// orientation with the trainee's look (relative to the device's mount); the rangefinder writes its own <c>DevicePose</c> from it, so the muscle's
        /// laser points where he looks — RANGE while looking NORTH measures the man 300 m north, not the marker 450 m east it points at with no look.
        /// Run under <c>HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1</c> too. 🔴 Red-proof (compiles): the rangefinder's <c>DeviceLooks.Follow</c> removed ⇒ the
        /// east marker answers ⇒ red.
        /// </summary>
        [Fact]
        public void S1d3_EndToEnd_TheRangefinderRangesTheBodyTheTraineeLooksAt()
        {
            using var r = new Rig();
            var north = r.W.CreateEntity();
            r.W.AddComponent(north, new SimTransform { Position = new Vector3(0f, 300f, 0f), Rotation = Quaternion.Identity });
            r.W.AddComponent(north, new PhysicsCollider { Radius = 0.5f, Height = 2f, CollisionLayer = 1 });
            r.Grid.Add(north, new Vector2(0f, 300f));
            r.W.SetSingleton(new SpatialGridData { Grid = r.Grid });
            uint seq = 100;
            void Look(float yaw, byte range)
            {
                var c = new DeviceControl { Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw), Trackers = DeviceControl.OrientationPresent };
                c.Presses[RangefinderChannels.Range] = range;
                var input = new DeviceInput { Seq = ++seq, Control = c };
                if (r.W.HasComponent<DeviceInput>(r.D)) r.W.SetComponent(r.D, input); else r.W.AddComponent(r.D, input);
            }
            Look(MathF.PI / 2f, 0);
            for (int i = 0; i < 3; i++) r.Frame(0f);                                // started, based, and turned north (its own pose)
            var fwd = Vector3.Transform(Vector3.UnitX, r.W.GetComponentRO<DevicePose>(r.D).Orientation);
            Assert.True(fwd.Y > 0.99f, $"the device points north, where he looks ({fwd})");
            Assert.Equal(RangefinderBehavior.FovDegrees, r.W.GetComponentRO<DevicePose>(r.D).FovDegrees, 4);   // its fixed magnification's field
            Look(MathF.PI / 2f, 1);                                                  // RANGE
            for (int i = 0; i < 3; i++) r.Frame(0f);
            var s = r.State;
            Assert.Equal(1, s.HasRange);
            Assert.Equal(north, s.LastHit);                                          // the man he looks at
            Assert.InRange(s.LastRange, 299f, 300f);
            Look(0f, 2);                                                             // look east again, RANGE: the marker
            for (int i = 0; i < 3; i++) r.Frame(0f);
            Assert.Equal(r.Target, r.State.LastHit);
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
