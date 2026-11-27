using System;
using System.Collections.Generic;
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
using Fdp.Toolkit.Replication.Components;
using Hrot.AI.Behaviors.Devices.Rangefinder;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.Player.Tests.Gizmos
{
    /// <summary>
    /// ⭐⭐ Simulated devices S1d-2 (docs/designs/devices/DESIGN_Simulated_Devices.md §3.6 D-21 / R6-5 / R7-3, mockup <c>devices-hud.svg</c> panels 2–5) —
    /// the RANGEFINDER's HUD projector through the production path (<see cref="StatelessGizmoRegistry"/> + <see cref="StatelessGizmoSystem"/>, as
    /// <c>HillAttackGizmoTests</c>' projector): nothing for an un-operated device, another station's row, a device of another type or a node without the
    /// brain's authority; the mockup's texts per state (idle · ranged · menu open · paused preview), laid out at the mockup's anchors; one HudScope run per
    /// device keyed by its network id; and 0 B per steady frame. 🔴 Red-proof (compiles): the authority test removed from <c>IsEntityVisible</c> ⇒
    /// <see cref="S1d2_NothingWithoutTheBrainsAuthority_NorForAnotherDeviceType_NorAnotherMode"/> fails.
    /// </summary>
    public sealed class RangefinderHudGizmoTests : IDisposable
    {
        private const int NetId = 4242;
        private readonly EntityRepository _w = new();
        private readonly DebugPrimitiveBuffer _buf = new(4096);
        private readonly StatelessGizmoSystem _sys;
        private readonly Entity _d;

        public RangefinderHudGizmoTests()
        {
            _w.RegisterComponent<BehaviorState>();
            _w.RegisterComponent<DeviceInfo>();
            _w.RegisterComponent<PossessedBy>();
            _w.RegisterComponent<NetworkIdentity>();
            BlueprintTierTable.RegisterAll(_w);
            _w.SetSingleton(new GlobalTime());
            var registry = new StatelessGizmoRegistry();
            var attr = typeof(RangefinderHudGizmo).GetCustomAttribute<GizmoProjectorAttribute>()!;
            registry.Register(new RangefinderHudGizmo(), attr.RequiredComponents);
            _sys = new StatelessGizmoSystem(registry, _buf);

            _d = _w.CreateEntity();
            _w.AddComponent(_d, new BehaviorState());
            _w.SetAuthority<BehaviorState>(_d, true);
            _w.AddComponent(_d, new DeviceInfo { DeviceType = 77, Behavior = new FixedString64(BehaviorNames.DeviceRangefinder) });
            _w.AddComponent(_d, new NetworkIdentity { Value = NetId });
            _w.AddComponent(_d, new PossessedBy { StationNodeId = (StationId)5, Mode = PossessionMode.Operated, Epoch = 1 });
        }

        public void Dispose() => _w.Dispose();

        private void State(RangefinderState s) => UnitMemory.Ref<RangefinderState>(_w, _d) = s;

        /// <summary>One frame; the HUD run of the device (empty when none).</summary>
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
            Assert.Equal(0, frame.Length);   // no scope ⇒ nothing at all (never an unscoped HUD)
            return Array.Empty<DebugPrimitive>();
        }

        private static List<string> Texts(DebugPrimitive[] run)
        {
            var l = new List<string>();
            foreach (var p in run) if (p.Shape == DebugPrimitiveShape.Text) l.Add(p.TextContent.ToString());
            return l;
        }

        private static DebugPrimitive TextOf(DebugPrimitive[] run, string text)
        {
            foreach (var p in run) if (p.Shape == DebugPrimitiveShape.Text && p.TextContent.ToString() == text) return p;
            throw new Xunit.Sdk.XunitException($"no text '{text}' in the HUD run");
        }

        [Fact]
        public void S1d2_TheRule_IsDeviceInfoAndPossessedBy_AHudOnTheDeviceHudLayer()
        {
            var attr = typeof(RangefinderHudGizmo).GetCustomAttribute<GizmoProjectorAttribute>()!;
            Assert.Equal(new[] { typeof(DeviceInfo), typeof(PossessedBy) }, attr.RequiredComponents);
            Assert.True(attr.Hud);
            Assert.Null(typeof(RangefinderHudGizmo).GetProperty(nameof(IStatelessGizmo.Inputs)));   // no G5 inputs: re-run every frame
            foreach (var p in Frame())
            {
                Assert.Equal(DebugTraceLayers.DeviceHud, p.DebugLayer);
                Assert.Equal(CoordinateSpace.ViewCanvas, p.Space);
            }
        }

        [Fact]
        public void S1d2_AnUnOperatedDevice_DrawsNothing()
        {
            _w.RemoveComponent<PossessedBy>(_d);
            Assert.Empty(Frame());
            Assert.Equal(0, _sys.HudRunsDrawn);
        }

        [Fact]
        public void S1d2_NothingWithoutTheBrainsAuthority_NorForAnotherDeviceType_NorAnotherMode()
        {
            Assert.NotEmpty(Frame());
            _w.SetAuthority<BehaviorState>(_d, false);                                     // a ghost of the device on another node
            Assert.Empty(Frame());
            _w.SetAuthority<BehaviorState>(_d, true);
            _w.SetComponent(_d, new DeviceInfo { DeviceType = 78, Behavior = new FixedString64("DeviceBinoculars") });
            Assert.Empty(Frame());                                                          // another device type: not this HUD
            _w.SetComponent(_d, new DeviceInfo { DeviceType = 77, Behavior = new FixedString64(BehaviorNames.DeviceRangefinder) });
            _w.SetComponent(_d, new PossessedBy { StationNodeId = (StationId)5, Mode = PossessionMode.Fps, Epoch = 1 });
            Assert.Empty(Frame());                                                          // a row that is not Operated
        }

        [Fact]
        public void S1d2_Idle_ShowsTheNameModePowerTheDarkLamp_ADashRange_AndTheMenuLineOnMode()
        {
            State(new RangefinderState());
            var run = Frame();
            var texts = Texts(run);
            foreach (var t in new[] { "LASER RANGEFINDER", "MODE", "SINGLE", "POWER", "LOW", "LASER", "LAST RANGE", "— m", "EXIT" }) Assert.Contains(t, texts);
            Assert.DoesNotContain("PREVIEW", texts);
            Assert.DoesNotContain("MENU", texts);
            Assert.Equal(RangefinderHudGizmo.Label, TextOf(run, "LASER").Color);         // the lamp's label greyed while off
            // the menu chips: right-aligned at the mockup's x = 446, MODE highlighted (the cursor rests on it)
            float right = 0f; int highlighted = 0;
            foreach (var p in run)
                if (p.Shape == DebugPrimitiveShape.Box2D && Math.Abs(p.BoxCenterY - (259 - 27) * RangefinderHudGizmo.K) < 0.5f)
                {
                    right = Math.Max(right, p.BoxCenterX + p.BoxExtentX);
                    if (p.FillColor.B == 173) highlighted++;
                }
            Assert.Equal((460 - 14) * RangefinderHudGizmo.K, right, 1);
            Assert.Equal(1, highlighted);
        }

        [Fact]
        public void S1d2_Ranged_ShowsTheRangeCentredBelow_AndTheLampLit()
        {
            State(new RangefinderState { HasRange = 1, LastRange = 452.4f, Ranging = 1 });
            var run = Frame();
            var range = TextOf(run, "452 m");
            Assert.Equal(ViewCanvasAlign.Centre, ViewCanvas.AlignOf(in range));
            Assert.Equal(ViewCanvas.Width / 2f, range.TextX, 1);                          // bottom-centre
            Assert.Equal((259 - 28) * RangefinderHudGizmo.K, range.TextY, 1);
            Assert.Equal(RangefinderHudGizmo.Gold, range.Color);
            Assert.Equal(RangefinderHudGizmo.Laser, TextOf(run, "LASER").Color);
            int lit = 0;
            foreach (var p in run) if (p.Shape == DebugPrimitiveShape.Sphere && p.FillColor.R == 255 && p.FillColor.G == 59) lit++;
            Assert.Equal(2, lit);                                                           // the glow and the lamp
            Assert.DoesNotContain("PREVIEW", Texts(run));
        }

        [Fact]
        public void S1d2_TheLamp_BlinksByFrameCount_WhileRanging()
        {
            State(new RangefinderState { Ranging = 1 });
            int Lit()
            {
                int n = 0;
                foreach (var p in Frame()) if (p.Shape == DebugPrimitiveShape.Sphere && p.FillColor.R == 255 && p.FillColor.G == 59) n++;
                return n;
            }
            _w.SetSingleton(new GlobalTime { FrameNumber = 0 });
            Assert.Equal(2, Lit());
            _w.SetSingleton(new GlobalTime { FrameNumber = RangefinderHudGizmo.BlinkFrames });
            Assert.Equal(0, Lit());
            _w.SetSingleton(new GlobalTime { FrameNumber = 2 * RangefinderHudGizmo.BlinkFrames });
            Assert.Equal(2, Lit());
        }

        [Fact]
        public void S1d2_MenuOpen_ShowsThePopupWithTheModes_ACheckOnTheActiveOne_AndTheCursorOnTheOther()
        {
            State(new RangefinderState { MenuOpen = 1, Menu = RangefinderMenuItem.Mode, Mode = RangefinderMode.Single });
            var run = Frame();
            var texts = Texts(run);
            Assert.Contains("MENU", texts);                                                // "MENU ▸ MODE": the ▸ is a triangle (the font has none)
            Assert.Equal(3, texts.FindAll(t => t == "MODE").Count);                        // the status label, the popup title and the chip
            Assert.Contains("CONTINUOUS", texts);
            Assert.Equal(2, texts.FindAll(t => t == "SINGLE").Count);                     // the status line and the popup row
            int checks = 0, golds = 0;
            foreach (var p in run)
            {
                if (p.Shape == DebugPrimitiveShape.Line && p.Color.Equals(RangefinderHudGizmo.Check)) checks++;
                if (p.Shape == DebugPrimitiveShape.Box2D && p.Color.Equals(RangefinderHudGizmo.Gold)) golds++;
            }
            Assert.Equal(2, checks);                                                        // ✓ (two strokes) on SINGLE
            Assert.Equal(1, golds);                                                         // the MODE chip framed: the menu is open
            var popupCursorRow = TextOf(run, "CONTINUOUS");
            Assert.Equal(GizmoTextStyle.HaloBold, popupCursorRow.TextStyle);              // the row SELECT would apply

            State(new RangefinderState { MenuOpen = 1, Menu = RangefinderMenuItem.Power, Power = RangefinderPower.High });
            texts = Texts(Frame());
            Assert.Equal(3, texts.FindAll(t => t == "POWER").Count);                      // the status label, the popup title and the chip
            Assert.Contains("HIGH", texts);
        }

        [Fact]
        public void S1d2_ARangeMeasuredInPause_IsTaggedPreview()
        {
            State(new RangefinderState { Mode = RangefinderMode.Continuous, HasRange = 1, LastRange = 452f, RangePreview = 1, Ranging = 1 });
            var run = Frame();
            var texts = Texts(run);
            Assert.Contains("PREVIEW", texts);
            Assert.Contains("CONTINUOUS", texts);
            Assert.Contains("452 m", texts);
            Assert.Equal(RangefinderHudGizmo.Pending, TextOf(run, "PREVIEW").Color);
            Assert.True(TextOf(run, "PREVIEW").TextX > TextOf(run, "452 m").TextX, "the tag sits right of the range");
        }

        [Fact]
        public void S1d2_ASteadyFrame_AllocatesNothing()
        {
            State(new RangefinderState { MenuOpen = 1, HasRange = 1, LastRange = 452.4f, RangePreview = 1, Ranging = 1 });
            for (int i = 0; i < 300; i++) { _w.SetSingleton(new GlobalTime { FrameNumber = i }); Frame(); }   // warm (JIT tiering)
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 30; i++)
            {
                _w.SetSingleton(new GlobalTime { FrameNumber = 300 + i });
                _buf.EndFrame(0f);
                _sys.Execute(_w, 0f);
            }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal(1, _sys.HudRunsDrawn);
        }
    }
}
