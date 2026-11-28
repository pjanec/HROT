using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Devices;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Replication.Components;
using Hrot.AI.Behaviors.Brains;
using Hrot.AI.Behaviors.Devices.DroneStation;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.Player.Tests.Gizmos
{
    /// <summary>
    /// ⭐⭐ Simulated devices SD-S2d (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13c D-111, mockup <c>devices-hud-drone.svg</c> panels 12–15) — the drone
    /// GROUND STATION's OSD projector through the production path (<see cref="StatelessGizmoRegistry"/> + <see cref="StatelessGizmoSystem"/>, as the rangefinder's
    /// rails): nothing for an un-operated station, a node without the brain's authority or another device type; per mockup panel — flying (12), battery low (13),
    /// LINK LOST + the failsafe (14), paused with a queued press (15) — the texts, colours and anchors; the chips through <see cref="HudPanel"/> (one PanelButton
    /// each, the element's index); 0 B per steady frame; and every number invariant under cs-CZ. 🔴 Red-proof (compiles): the "commands hidden while lost" return
    /// removed from <c>Draw</c> ⇒ <see cref="SDS2d_LinkLost_Jammed_DarkensShowsTheReasonAndTheFailsafe_GreysAndHoldsTheReadouts_HidesTheCommands"/> fails.
    /// </summary>
    public sealed class DroneStationHudGizmoTests : IDisposable
    {
        private const int NetId = 4343;
        private const float K = DroneStationHudGizmo.K;
        private readonly EntityRepository _w = new();
        private readonly DebugPrimitiveBuffer _buf = new(4096);
        private readonly StatelessGizmoSystem _sys;
        private readonly Entity _d, _drone;

        public DroneStationHudGizmoTests()
        {
            _w.RegisterComponent<BehaviorState>();
            _w.RegisterComponent<DeviceInfo>();
            _w.RegisterComponent<PossessedBy>();
            _w.RegisterComponent<NetworkIdentity>();
            _w.RegisterComponent<SimTransform>();
            _w.RegisterComponent<SimVelocity>();
            _w.RegisterComponent<DroneLink>();
            _w.RegisterComponent<DroneBattery>();
            BlueprintTierTable.RegisterAll(_w);
            _w.SetSingleton(new GlobalTime { DeltaTime = 1f / 60f, TotalTime = 100 });
            var registry = new StatelessGizmoRegistry();
            var attr = typeof(DroneStationHudGizmo).GetCustomAttribute<GizmoProjectorAttribute>()!;
            registry.Register(new DroneStationHudGizmo(), attr.RequiredComponents);
            _sys = new StatelessGizmoSystem(registry, _buf);

            _d = _w.CreateEntity();
            _w.AddComponent(_d, new BehaviorState());
            _w.SetAuthority<BehaviorState>(_d, true);
            _w.AddComponent(_d, new DeviceInfo { DeviceType = 642, Behavior = new FixedString64(BehaviorNames.DeviceDroneStation) });
            _w.AddComponent(_d, new NetworkIdentity { Value = NetId });
            _w.AddComponent(_d, new PossessedBy { StationNodeId = (StationId)5, Mode = PossessionMode.Operated, Epoch = 1 });

            _drone = _w.CreateEntity();
            Drone(new Vector3(0, 180, 42), 24f, new Vector3(0, 8.4f, 0), quality: 1f, battery: 0.78f, phase: PilotPhase.Manual);
            UnitMemory.Ref<RemotePilotState>(_w, _drone) = new RemotePilotState { Ready = 1, Home = Vector3.Zero, Phase = PilotPhase.Manual };
        }

        public void Dispose() => _w.Dispose();

        private void Drone(Vector3 at, float headingDeg, Vector3 velocity, float quality, float battery, PilotPhase phase, bool jammed = false, bool lost = false)
        {
            var rot = WorldRotation.FromHeadingDeg(headingDeg);
            Set(new SimTransform { Position = at, Rotation = rot });
            _w.SetAuthority<SimTransform>(_drone, true);
            Set(new SimVelocity { Linear = velocity });
            Set(new DroneLink { Quality = quality, Jammed = jammed ? (byte)1 : (byte)0, Lost = lost || jammed ? (byte)1 : (byte)0, Phase = phase });
            Set(new DroneBattery { Remaining01 = battery });
        }

        private void Set<T>(T v) where T : unmanaged
        {
            if (_w.HasComponent<T>(_drone)) _w.SetComponent(_drone, v); else _w.AddComponent(_drone, v);
        }

        private void Station(DroneStationMode mode, RemoteMode command, byte queued = 0, float pitchDeg = -30f)
            => UnitMemory.Ref<DroneStationState>(_w, _d) = new DroneStationState
            {
                Mode = mode, Power = 1, Command = command, Queued = queued, CameraPitch = pitchDeg * MathF.PI / 180f, Drone = _drone,
            };

        private DebugPrimitive[] Frame()
        {
            _buf.EndFrame(0f);
            _sys.Execute(_w, 0f);
            var frame = _buf.GetFrame();
            for (int i = 0; i < frame.Length; i++)
                if (HudScopes.IsScope(in frame[i]))
                {
                    Assert.Equal(NetId, frame[i].HudScopeSource);
                    return frame.Slice(i + 1, frame[i].HudScopeCount).ToArray();
                }
            Assert.Equal(0, frame.Length);
            return Array.Empty<DebugPrimitive>();
        }

        /// <summary>A link bar: 5 px wide, in the strip x 480 … 508 (mockup px).</summary>
        private static bool IsBar(in DebugPrimitive p) => p.BoxCenterX > 480 * K && p.BoxCenterX < 510 * K && Math.Abs(p.BoxExtentX * 2 - 5 * K) < 0.01f;

        private static List<string> Texts(DebugPrimitive[] run) => run.Where(p => p.Shape == DebugPrimitiveShape.Text).Select(p => p.TextContent.ToString()).ToList();

        private static DebugPrimitive TextOf(DebugPrimitive[] run, string text)
        {
            foreach (var p in run) if (p.Shape == DebugPrimitiveShape.Text && p.TextContent.ToString() == text) return p;
            throw new Xunit.Sdk.XunitException($"no text '{text}' in the HUD run: [{string.Join(" | ", Texts(run))}]");
        }

        [Fact]
        public void SDS2d_TheRule_IsDeviceInfoAndPossessedBy_AHudOnTheDeviceHudLayer()
        {
            var attr = typeof(DroneStationHudGizmo).GetCustomAttribute<GizmoProjectorAttribute>()!;
            Assert.Equal(new[] { typeof(DeviceInfo), typeof(PossessedBy) }, attr.RequiredComponents);
            Assert.True(attr.Hud);
            Station(DroneStationMode.Flying, RemoteMode.Fly);
            var run = Frame();
            Assert.NotEmpty(run);
            foreach (var p in run)
            {
                Assert.Equal(DebugTraceLayers.DeviceHud, p.DebugLayer);
                Assert.Equal(CoordinateSpace.ViewCanvas, p.Space);
            }
        }

        [Fact]
        public void SDS2d_AnUnOperatedStation_NoAuthority_OrAnotherType_DrawsNothing()
        {
            Station(DroneStationMode.Flying, RemoteMode.Fly);
            Assert.NotEmpty(Frame());
            _w.SetAuthority<BehaviorState>(_d, false);
            Assert.Empty(Frame());                                                          // a ghost of the station on another node
            _w.SetAuthority<BehaviorState>(_d, true);
            _w.SetComponent(_d, new DeviceInfo { DeviceType = 640, Behavior = new FixedString64(BehaviorNames.DeviceRangefinder) });
            Assert.Empty(Frame());                                                          // another device type
            _w.SetComponent(_d, new DeviceInfo { DeviceType = 642, Behavior = new FixedString64(BehaviorNames.DeviceDroneStation) });
            _w.RemoveComponent<PossessedBy>(_d);
            Assert.Empty(Frame());                                                          // nobody operates it
            Assert.Equal(0, _sys.HudRunsDrawn);
        }

        /// <summary>Panel 12 — flying by hand: MANUAL (green) + ARMED, 78 % green, four bars, the heading 024 under the caret, ALT · SPD · home, the gimbal at −30°,
        /// the centre mark, the three command chips (none lit), no warning.</summary>
        [Fact]
        public void SDS2d_Flying_ShowsTheModeBatteryLinkHeadingGimbalReadouts_AndTheCommandChips()
        {
            Station(DroneStationMode.Flying, RemoteMode.Fly);
            var run = Frame();
            var texts = Texts(run);
            foreach (var t in new[] { "MANUAL", "ARMED", "78 %", "024", "ALT", "42 m", "SPD", "8.4 m/s", "180 m", "CAM", "0°", "-90°", "N", "TAKEOFF", "LAND", "RTH" })
                Assert.Contains(t, texts);
            foreach (var t in new[] { "LINK LOST", "BATTERY LOW — RETURN HOME", "queued" }) Assert.DoesNotContain(t, texts);
            Assert.Equal(DroneStationHudGizmo.Good, TextOf(run, "MANUAL").Color);
            Assert.Equal(DroneStationHudGizmo.Good, TextOf(run, "78 %").Color);
            // anchors, mockup px × K
            var mode = TextOf(run, "MANUAL");
            Assert.Equal(31 * K, mode.TextY, 1);
            Assert.True(mode.TextX < 80 * K, "the mode chip is top-left");
            var pct = TextOf(run, "78 %");
            Assert.Equal(ViewCanvasAlign.Right, ViewCanvas.AlignOf(in pct));
            Assert.Equal((640 - 14) * K, pct.TextX, 1);
            var hdg = TextOf(run, "024");
            Assert.Equal(320 * K, hdg.TextX, 1);
            Assert.Equal(DroneStationHudGizmo.Gold, hdg.Color);
            Assert.Equal((360 - 20) * K, TextOf(run, "42 m").TextY, 1);
            // the link bars: four lit green
            int lit = run.Count(p => p.Shape == DebugPrimitiveShape.Box2D && p.FillColor.Equals(DroneStationHudGizmo.Good) && IsBar(p));
            Assert.Equal(4, lit);
            // the gimbal marker at −30°: a third of the way down the scale (92 … 252)
            var marker = run.Single(p => p.Shape == DebugPrimitiveShape.FilledTriangle && p.Color.Equals(DroneStationHudGizmo.Gold) && p.TriC.X < 30 * K);
            Assert.Equal((92 + 160f / 3f) * K, marker.TriC.Y, 1);
            // the chips: one PanelButton per command, the element index of TAKEOFF / LAND / RTH, right-aligned at 626, none lit
            var buttons = run.Where(p => PanelButtons.Is(p)).ToArray();
            Assert.Equal(new byte[] { DroneStationChannels.Rth, DroneStationChannels.Land, DroneStationChannels.TakeOff }, buttons.Select(b => b.PanelElement).ToArray());
            Assert.All(buttons, b => Assert.Equal((byte)Fdp.Toolkit.Tkb.Domain.DeviceElementKind.Button, b.PanelKind));
            Assert.Equal((640 - 14) * K, buttons.Max(b => b.PanelX + b.PanelW), 1);
            Assert.All(buttons, b => Assert.Equal((360 - 66) * K, b.PanelY, 1));
            Assert.DoesNotContain(buttons, b => b.PanelState == PanelButtonState.Lit);
            Assert.Equal(PanelButtonState.Disabled, buttons.Single(b => b.PanelElement == DroneStationChannels.TakeOff).PanelState);   // flying: TAKEOFF does nothing
        }

        /// <summary>Panel 13 — battery low: 12 % red and blinking by frame count, the banner, RTH (amber) and its chip lit; at 0 % BATTERY EMPTY.</summary>
        [Fact]
        public void SDS2d_BatteryLow_RedBlinking_TheBanner_RthLit_AndEmptyAtZero()
        {
            Drone(new Vector3(0, 640, 38), 204f, new Vector3(0, -11.2f, 0), 0.75f, 0.12f, PilotPhase.ReturnHome);
            Station(DroneStationMode.Returning, RemoteMode.ReturnHome);
            _w.SetSingleton(new GlobalTime { DeltaTime = 1f / 60f, FrameNumber = 0 });
            var run = Frame();
            Assert.Equal(DroneStationHudGizmo.Bad, TextOf(run, "12 %").Color);
            var banner = TextOf(run, "BATTERY LOW — RETURN HOME");
            Assert.Equal(DroneStationHudGizmo.Bad, banner.Color);
            Assert.Equal(107 * K, banner.TextY, 1);
            Assert.Equal(DroneStationHudGizmo.Warn, TextOf(run, "RTH").Color);                   // the mode chip (amber)
            Assert.Equal(PanelButtonState.Lit, run.Where(p => PanelButtons.Is(p)).Single(b => b.PanelElement == DroneStationChannels.Rth).PanelState);
            Assert.Contains("640 m", Texts(run));
            Assert.Contains(run, p => p.Shape == DebugPrimitiveShape.Box2D && p.FillColor.Equals(DroneStationHudGizmo.Bad) && p.BoxExtentX * 2 < 15 * K);   // the battery glyph's red quarter
            // the blink: the other half dims the red (by frame count)
            _w.SetSingleton(new GlobalTime { DeltaTime = 1f / 60f, FrameNumber = DroneStationHudGizmo.BlinkFrames });
            Assert.True(TextOf(Frame(), "12 %").Color.A < 255, "the battery blinks");
            // empty
            Drone(new Vector3(0, 640, 0), 204f, Vector3.Zero, 0.75f, 0f, PilotPhase.BatteryDown);
            Station(DroneStationMode.Landing, RemoteMode.ReturnHome);
            run = Frame();
            Assert.Contains("BATTERY EMPTY", Texts(run));
            Assert.Contains("0 %", Texts(run));
        }

        /// <summary>Panel 14 — the link lost to a jammer: the picture darkened, the camera-slash glyph, LINK LOST + JAMMED, the failsafe's countdown and steps, the
        /// FAILSAFE chip red, the bars 0, the readouts greyed at the LAST LINKED values, no gimbal, no centre, no commands.</summary>
        [Fact]
        public void SDS2d_LinkLost_Jammed_DarkensShowsTheReasonAndTheFailsafe_GreysAndHoldsTheReadouts_HidesTheCommands()
        {
            Drone(new Vector3(0, 1240, 51), 312f, Vector3.Zero, 1f, 0.64f, PilotPhase.Manual);
            Station(DroneStationMode.Flying, RemoteMode.Fly);
            Frame();                                                                            // linked: the readouts are carried
            Drone(new Vector3(0, 1300, 80), 10f, new Vector3(0, 5, 0), 0f, 0.64f, PilotPhase.FailsafeHover, jammed: true);
            UnitMemory.Ref<RemotePilotState>(_w, _drone) = new RemotePilotState { Ready = 1, Phase = PilotPhase.FailsafeHover, FailsafeUntil = (SimInstant)103 };
            _w.SetSingleton(new GlobalTime { DeltaTime = 1f / 60f, TotalTime = 100.2 });
            Station(DroneStationMode.Failsafe, RemoteMode.Fly);
            var run = Frame();
            var texts = Texts(run);
            foreach (var t in new[] { "LINK LOST", "JAMMED", "FAILSAFE", "FAILSAFE: hover 3 s", "return home", "land", "64 %" }) Assert.Contains(t, texts);
            Assert.Equal(DroneStationHudGizmo.Bad, TextOf(run, "LINK LOST").Color);
            Assert.Equal(DroneStationHudGizmo.Bad, TextOf(run, "FAILSAFE").Color);
            Assert.Equal(DroneStationHudGizmo.Warn, TextOf(run, "JAMMED").Color);
            // the readouts: greyed, the last values the link carried (51 m, 1240 m, 312°) — not the drone's true ones now
            Assert.Equal(DroneStationHudGizmo.Label, TextOf(run, "51 m").Color);
            Assert.Contains("1240 m", texts);
            Assert.Contains("312", texts);
            Assert.DoesNotContain("80 m", texts);
            // the picture darkened: the first primitive, past the canvas
            var dark = run[0];
            Assert.Equal(DebugPrimitiveShape.Box2D, dark.Shape);
            Assert.True(dark.FillColor.A > 140 && dark.FillColor.R == 0, "a dark full-canvas cover");
            Assert.True(dark.BoxExtentX * 2 > ViewCanvas.Width, "it covers the whole view");
            // the commands, the gimbal and the centre are gone
            Assert.DoesNotContain(run, p => PanelButtons.Is(p));
            foreach (var t in new[] { "TAKEOFF", "CAM", "-30°" }) Assert.DoesNotContain(t, texts);
            Assert.Equal(0, run.Count(p => p.Shape == DebugPrimitiveShape.Box2D && p.FillColor.Equals(DroneStationHudGizmo.Good) && IsBar(p)));   // no bar lit
            // out of range: the other reason
            Drone(new Vector3(0, 6000, 80), 10f, Vector3.Zero, 0f, 0.64f, PilotPhase.FailsafeReturn, lost: true);
            Assert.Contains("OUT OF RANGE", Texts(Frame()));
            Assert.Contains("FAILSAFE: hover", Texts(Frame()));
        }

        /// <summary>Panel 15 — paused: LAND pressed in the pause is QUEUED — its chip lit, framed gold and tagged "queued"; the gimbal at −45°; SPD 0.0 (nothing moves).</summary>
        [Fact]
        public void SDS2d_Paused_AQueuedPress_IsShownQueued_TheGimbalTilts_AndNothingMoves()
        {
            _w.SetSingleton(new GlobalTime { DeltaTime = 0f, TotalTime = 100 });                 // paused
            Station(DroneStationMode.Flying, RemoteMode.Fly, queued: DroneStationChannels.Bit(DroneStationChannels.Land), pitchDeg: -45f);
            var run = Frame();
            var queued = TextOf(run, "queued");
            Assert.Equal(DroneStationHudGizmo.Gold, queued.Color);
            var land = run.Where(p => PanelButtons.Is(p)).Single(b => b.PanelElement == DroneStationChannels.Land);
            Assert.Equal(PanelButtonState.Lit, land.PanelState);
            Assert.Equal((land.PanelX + land.PanelW / 2f), queued.TextX, 1);                     // over the LAND chip
            Assert.Contains(run, p => p.Shape == DebugPrimitiveShape.Box2D && p.Color.Equals(DroneStationHudGizmo.Gold) && p.FillColor.A == 0);   // the gold frame
            Assert.Contains("0.0 m/s", Texts(run));
            Assert.Contains("78 %", Texts(run));
            var marker = run.Single(p => p.Shape == DebugPrimitiveShape.FilledTriangle && p.Color.Equals(DroneStationHudGizmo.Gold) && p.TriC.X < 30 * K);
            Assert.Equal((92 + 80f) * K, marker.TriC.Y, 1);
        }

        [Fact]
        public void SDS2d_APoweredOffStation_ShowsOnlyOff()
        {
            UnitMemory.Ref<DroneStationState>(_w, _d) = new DroneStationState { Drone = _drone };
            var texts = Texts(Frame());
            Assert.Equal(new[] { "OFF" }, texts);
        }

        [Fact]
        public void SDS2d_EveryNumber_IsLanguageNeutral_UnderCsCz()
        {
            var before = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("cs-CZ");
            try
            {
                Drone(new Vector3(0, 1234, 42.4f), 24f, new Vector3(0, 12.35f, 0), 1f, 0.78f, PilotPhase.Manual);
                Station(DroneStationMode.Flying, RemoteMode.Fly);
                var texts = Texts(Frame());
                Assert.Contains("12.4 m/s", texts);
                Assert.Contains("1234 m", texts);
                Assert.DoesNotContain(texts, t => t.Contains(','));
            }
            finally { CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = before; }
        }

        [Fact]
        public void SDS2d_ASteadyFrame_AllocatesNothing()
        {
            Station(DroneStationMode.Returning, RemoteMode.ReturnHome, queued: DroneStationChannels.Bit(DroneStationChannels.Land));
            Set(new DroneBattery { Remaining01 = 0.12f });
            for (int i = 0; i < 300; i++) { _w.SetSingleton(new GlobalTime { DeltaTime = 1f / 60f, FrameNumber = i }); Frame(); }   // warm (JIT tiering)
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 30; i++)
            {
                _w.SetSingleton(new GlobalTime { DeltaTime = 1f / 60f, FrameNumber = 300 + i });
                _buf.EndFrame(0f);
                _sys.Execute(_w, 0f);
            }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal(1, _sys.HudRunsDrawn);
            // and while the link is lost (the failsafe's countdown)
            Set(new DroneLink { Lost = 1, Jammed = 1, Phase = PilotPhase.FailsafeHover });
            for (int i = 0; i < 300; i++) Frame();
            before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 30; i++) { _buf.EndFrame(0f); _sys.Execute(_w, 0f); }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }
}
