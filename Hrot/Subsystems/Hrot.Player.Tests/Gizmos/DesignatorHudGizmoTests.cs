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
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Hrot.AI.Behaviors.Devices.Designator;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.Player.Tests.Gizmos
{
    /// <summary>
    /// ⭐⭐ Simulated devices SD-S5c (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13d D-113 – D-115 / D-121, mockup <c>devices-hud-designator.svg</c>
    /// panels 16–19) — the LASER TARGET DESIGNATOR's HUD projector through the production path (<see cref="StatelessGizmoRegistry"/> +
    /// <see cref="StatelessGizmoSystem"/>, as the other device HUDs): nothing for an un-operated device, a node without the brain's authority or another type; per
    /// mockup panel — STANDBY with a received target (16), LASING (17), COOLING (18), a lase queued in pause (19) — the texts, colours and anchors; the chips
    /// through <see cref="HudPanel"/> (one PanelButton each, the element's index, their states); OFF; 0 B per steady frame; every number invariant under cs-CZ.
    /// 🔴 Red-proof (compiles): <c>StateOf</c>'s cooling arm for LASE removed ⇒ <see cref="SDS5c_Panel18_Cooling_TheCoolDownBarFills_LaseAndCodeGreyed"/> fails.
    /// </summary>
    public sealed class DesignatorHudGizmoTests : IDisposable
    {
        private const int NetId = 4545;
        private const float K = DesignatorHudGizmo.K;
        private readonly EntityRepository _w = new();
        private readonly DebugPrimitiveBuffer _buf = new(4096);
        private readonly StatelessGizmoSystem _sys;
        private readonly Entity _d, _r;

        public DesignatorHudGizmoTests()
        {
            _w.RegisterComponent<BehaviorState>();
            _w.RegisterComponent<DeviceInfo>();
            _w.RegisterComponent<PossessedBy>();
            _w.RegisterComponent<NetworkIdentity>();
            _w.RegisterComponent<SimTransform>();
            _w.RegisterComponent<DeviceLinks>();
            BlueprintTierTable.RegisterAll(_w);
            _w.SetSingleton(new GlobalTime { DeltaTime = 1f / 60f, TotalTime = 100 });
            _w.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(Hrot.Map.Common.HrotEnvironment.CreateTkb());   // the real type 643: labels, key boxes
            var registry = new StatelessGizmoRegistry();
            var attr = typeof(DesignatorHudGizmo).GetCustomAttribute<GizmoProjectorAttribute>()!;
            registry.Register(new DesignatorHudGizmo(), attr.RequiredComponents);
            _sys = new StatelessGizmoSystem(registry, _buf);

            _d = _w.CreateEntity();
            _w.AddComponent(_d, new BehaviorState());
            _w.SetAuthority<BehaviorState>(_d, true);
            _w.AddComponent(_d, new DeviceInfo { DeviceType = 643, Behavior = new FixedString64(BehaviorNames.DeviceDesignator) });
            _w.AddComponent(_d, new NetworkIdentity { Value = NetId });
            _w.AddComponent(_d, new PossessedBy { StationNodeId = (StationId)5, Mode = PossessionMode.Operated, Epoch = 1 });
            _w.AddComponent(_d, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            _r = _w.CreateEntity();   // the rangefinder it is paired with (the sender)
            _w.AddComponent(_r, new NetworkIdentity { Value = NetId + 1 });
            _w.AddComponent(_d, DeviceLinks.To(EntityRef.Of(_w, _r)));
        }

        public void Dispose() => _w.Dispose();

        private DesignatorState State(DesignatorPhase phase, ushort code = 1688, float? range = null, byte queued = 0, double laseFrom = 0, double coolFrom = 0)
        {
            var st = new DesignatorState
            {
                Phase = phase, Ready = 1, Code = code, Queued = queued, LaseSeconds = 60f, CoolSeconds = 30f, LaseFrom = laseFrom, CoolFrom = coolFrom,
                HasRange = range.HasValue ? (byte)1 : (byte)0, LastRange = range ?? 0f,
            };
            UnitMemory.Ref<DesignatorState>(_w, _d) = st;
            return st;
        }

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

        private static List<string> Texts(DebugPrimitive[] run) => run.Where(p => p.Shape == DebugPrimitiveShape.Text).Select(p => p.TextContent.ToString()).ToList();

        private static DebugPrimitive TextOf(DebugPrimitive[] run, string text)
        {
            foreach (var p in run) if (p.Shape == DebugPrimitiveShape.Text && p.TextContent.ToString() == text) return p;
            throw new Xunit.Sdk.XunitException($"no text '{text}' in the HUD run: [{string.Join(" | ", Texts(run))}]");
        }

        private static DebugPrimitive[] Buttons(DebugPrimitive[] run) => run.Where(p => PanelButtons.Is(p)).ToArray();

        private static PanelButtonState StateOf(DebugPrimitive[] run, int element) => Buttons(run).Single(b => b.PanelElement == element).PanelState;

        /// <summary>The timer bar's fill (the coloured box inside the 150-px rim at x 93).</summary>
        private static DebugPrimitive Bar(DebugPrimitive[] run, Rgba32 c)
            => run.Single(p => p.Shape == DebugPrimitiveShape.Box2D && p.FillColor.Equals(c) && Math.Abs(p.BoxCenterY - 110.5f * K) < 2f);

        [Fact]
        public void SDS5c_TheRule_IsDeviceInfoAndPossessedBy_AHudOnTheDeviceHudLayer()
        {
            var attr = typeof(DesignatorHudGizmo).GetCustomAttribute<GizmoProjectorAttribute>()!;
            Assert.Equal(new[] { typeof(DeviceInfo), typeof(PossessedBy) }, attr.RequiredComponents);
            Assert.True(attr.Hud);
            State(DesignatorPhase.Standby);
            var run = Frame();
            Assert.NotEmpty(run);
            foreach (var p in run)
            {
                Assert.Equal(DebugTraceLayers.DeviceHud, p.DebugLayer);
                Assert.Equal(CoordinateSpace.ViewCanvas, p.Space);
            }
        }

        [Fact]
        public void SDS5c_AnUnOperatedDevice_NoAuthority_OrAnotherType_DrawsNothing()
        {
            State(DesignatorPhase.Standby);
            Assert.NotEmpty(Frame());
            _w.SetAuthority<BehaviorState>(_d, false);
            Assert.Empty(Frame());
            _w.SetAuthority<BehaviorState>(_d, true);
            _w.SetComponent(_d, new DeviceInfo { DeviceType = 640, Behavior = new FixedString64(BehaviorNames.DeviceRangefinder) });
            Assert.Empty(Frame());
            _w.SetComponent(_d, new DeviceInfo { DeviceType = 643, Behavior = new FixedString64(BehaviorNames.DeviceDesignator) });
            _w.RemoveComponent<PossessedBy>(_d);
            Assert.Empty(Frame());
            Assert.Equal(0, _sys.HudRunsDrawn);
        }

        /// <summary>Panel 16 — STANDBY, a target received from the linked rangefinder: STATE green, CODE gold, RANGE "— m", the chips POWER · LASE · CODE · RANGE
        /// with their key boxes (none lit, none greyed), the received-target cue at the window's left edge (amber, its offset, its range, "from LASER RANGEFINDER
        /// (linked)"), no timer, the reticle white.</summary>
        [Fact]
        public void SDS5c_Panel16_Standby_AReceivedTarget_TheCue_TheChipsWithKeys()
        {
            var st = State(DesignatorPhase.Standby);
            // a target 1 240 m away, 14° to the LEFT of the boresight (+x forward, +y left)
            float a = 14f * MathF.PI / 180f;
            st.HasTarget = 1;
            st.Target = new Hrot.AI.Behaviors.Devices.Contracts.TargetLocation { Point = new Vector3(1240f * MathF.Cos(a), 1240f * MathF.Sin(a), 0f), Range = 1240f, FromType = 640 };
            st.TargetFrom = EntityRef.Of(_w, _r);
            UnitMemory.Ref<DesignatorState>(_w, _d) = st;
            var run = Frame();
            var texts = Texts(run);
            foreach (var t in new[] { "LASER DESIGNATOR", "STATE", "STANDBY", "CODE", "1688", "RANGE", "— m", "POWER", "LASE", "P", "L", "C", "R",
                                      "TARGET", "14°", "1 240 m", "from LASER RANGEFINDER (linked)" })
                Assert.Contains(t, texts);
            foreach (var t in new[] { "COOL", "queued", "LASING" }) Assert.DoesNotContain(t, texts);
            Assert.Equal(DesignatorHudGizmo.Good, TextOf(run, "STANDBY").Color);
            Assert.Equal(DesignatorHudGizmo.Gold, TextOf(run, "1688").Color);
            Assert.Equal(92 * K, TextOf(run, "1688").TextX, 1);
            Assert.Equal(89 * K, TextOf(run, "1688").TextY, 1);
            Assert.Equal(70 * K, TextOf(run, "STANDBY").TextY, 1);
            Assert.Equal(40 * K, TextOf(run, "LASER DESIGNATOR").TextX, 1);
            var range = TextOf(run, "— m");
            Assert.Equal(320 * K, range.TextX, 1);
            Assert.Equal((360 - 90) * K, range.TextY, 1);
            Assert.Equal(ViewCanvasAlign.Centre, ViewCanvas.AlignOf(in range));
            Assert.Equal(HudCue.Amber, TextOf(run, "TARGET").Color);
            Assert.True(TextOf(run, "TARGET").TextX < 160 * K, "the cue sits at the window's LEFT edge (the target is to the left)");
            // the chips: one PanelButton per element, right-aligned at 606, in the mockup's row, none lit or greyed
            var buttons = Buttons(run);
            Assert.Equal(new byte[] { 3, 2, 1, 0 }, buttons.Select(b => b.PanelElement).ToArray());
            Assert.Equal((640 - 34) * K, buttons.Max(b => b.PanelX + b.PanelW), 1);
            Assert.All(buttons, b => Assert.Equal((360 - 64) * K, b.PanelY, 1));
            Assert.All(buttons, b => Assert.Equal(PanelButtonState.Normal, b.PanelState));
            var widths = new[] { "POWER", "LASE", "CODE", "RANGE" }.Select(DesignatorHudGizmo.ChipWidth).ToArray();
            for (int i = 0; i < 4; i++)
                Assert.Equal(DesignatorHudGizmo.ChipLeft(i, widths) * K, buttons.Single(b => b.PanelElement == i).PanelX, 1);
            Assert.Equal((ushort)Fdp.Toolkit.Diagnostics.Gizmos.HudPanel.HotkeyCode("L"), buttons.Single(b => b.PanelElement == DesignatorChannels.Lase).PanelHotkey);
        }

        /// <summary>Panel 17 — LASING: STATE red, the lamp lit, the reticle red, the lase timer draining (42 s of 60 ⇒ 0.7 of the bar), RANGE 1 240 m, LASE lit,
        /// CODE greyed.</summary>
        [Fact]
        public void SDS5c_Panel17_Lasing_TheLampAndReticleRed_TheLaseTimerDrains_LaseLit_CodeGreyed()
        {
            State(DesignatorPhase.Lasing, range: 1240f, laseFrom: 100 - 18);   // 18 s in ⇒ 42 s left
            var run = Frame();
            Assert.Equal(DesignatorHudGizmo.Laser, TextOf(run, "LASING").Color);
            Assert.Equal(DesignatorHudGizmo.Laser, TextOf(run, "LASE").Color);   // the lamp's word (the chip's label is the same text, white — the first is the lamp's)
            Assert.Contains("42 s", Texts(run));
            Assert.Equal(DesignatorHudGizmo.Laser, TextOf(run, "42 s").Color);
            Assert.Equal((40 + 58 + 150) * K, TextOf(run, "42 s").TextX, 1);
            var bar = Bar(run, DesignatorHudGizmo.Laser);
            Assert.Equal(148f * 0.7f * K, bar.BoxExtentX * 2, 1);
            Assert.Contains("1 240 m", Texts(run));
            Assert.Contains(run, p => p.Shape == DebugPrimitiveShape.Sphere && p.FillColor.Equals(DesignatorHudGizmo.Laser));            // the lamp lit
            Assert.Contains(run, p => p.Shape == DebugPrimitiveShape.Line && p.Color.Equals(DesignatorHudGizmo.Laser));                // the reticle red
            Assert.Equal(PanelButtonState.Lit, StateOf(run, DesignatorChannels.Lase));
            Assert.Equal(PanelButtonState.Disabled, StateOf(run, DesignatorChannels.Code));
            Assert.Equal(PanelButtonState.Normal, StateOf(run, DesignatorChannels.Range));
            Assert.Equal(PanelButtonState.Normal, StateOf(run, DesignatorChannels.Power));
            // the lamp blinks by frame count
            _w.SetSingleton(new GlobalTime { DeltaTime = 1f / 60f, TotalTime = 100, FrameNumber = DesignatorHudGizmo.BlinkFrames });
            Assert.DoesNotContain(Frame(), p => p.Shape == DebugPrimitiveShape.Sphere && p.FillColor.Equals(DesignatorHudGizmo.Laser));
        }

        /// <summary>Panel 18 — COOLING: STATE amber, the cool-down bar FILLING (11 s of 30 cooled, 19 s left), LASE and CODE greyed, the last range kept.</summary>
        [Fact]
        public void SDS5c_Panel18_Cooling_TheCoolDownBarFills_LaseAndCodeGreyed()
        {
            State(DesignatorPhase.Cooling, range: 1240f, coolFrom: 100 - 11);
            var run = Frame();
            Assert.Equal(DesignatorHudGizmo.Warn, TextOf(run, "COOLING").Color);
            Assert.Contains("COOL", Texts(run));
            Assert.Equal(DesignatorHudGizmo.Warn, TextOf(run, "19 s").Color);
            var bar = Bar(run, DesignatorHudGizmo.Warn);
            Assert.Equal(148f * (11f / 30f) * K, bar.BoxExtentX * 2, 1);
            Assert.Contains("1 240 m", Texts(run));
            Assert.Equal(PanelButtonState.Disabled, StateOf(run, DesignatorChannels.Lase));
            Assert.Equal(PanelButtonState.Disabled, StateOf(run, DesignatorChannels.Code));
            Assert.Equal(PanelButtonState.Normal, StateOf(run, DesignatorChannels.Range));
            Assert.DoesNotContain(run, p => p.Shape == DebugPrimitiveShape.Sphere && p.FillColor.Equals(DesignatorHudGizmo.Laser));       // the lamp off
        }

        /// <summary>Panel 19 — PAUSED, a lase queued: STANDBY, the LASE chip lit with <c>queued</c> over it (gold), no timer, no lamp.</summary>
        [Fact]
        public void SDS5c_Panel19_Paused_ALaseQueued_IsShownQueuedOverTheLitChip()
        {
            _w.SetSingleton(new GlobalTime { DeltaTime = 0f, TotalTime = 100 });
            State(DesignatorPhase.Standby, queued: 1);
            var run = Frame();
            var queued = TextOf(run, "queued");
            Assert.Equal(DesignatorHudGizmo.Gold, queued.Color);
            var lase = Buttons(run).Single(b => b.PanelElement == DesignatorChannels.Lase);
            Assert.Equal(PanelButtonState.Lit, lase.PanelState);
            Assert.Equal(lase.PanelX + lase.PanelW / 2f, queued.TextX, 1);
            Assert.Equal((360 - 74) * K, queued.TextY, 1);
            Assert.Contains("STANDBY", Texts(run));
            Assert.DoesNotContain("LASE", Texts(run).Where(t => t == "COOL"));
            Assert.DoesNotContain(run, p => p.Shape == DebugPrimitiveShape.Sphere && p.FillColor.Equals(DesignatorHudGizmo.Laser));
        }

        [Fact]
        public void SDS5c_Off_ShowsTheStateOff_OnlyPowerEnabled_NoCodeNoRange()
        {
            UnitMemory.Ref<DesignatorState>(_w, _d) = new DesignatorState { Phase = DesignatorPhase.Off, Ready = 1, Code = 1688 };
            var run = Frame();
            var texts = Texts(run);
            Assert.Contains("OFF", texts);
            Assert.DoesNotContain("1688", texts);
            Assert.DoesNotContain("— m", texts);
            Assert.Equal(PanelButtonState.Normal, StateOf(run, DesignatorChannels.Power));
            foreach (int b in new[] { DesignatorChannels.Lase, DesignatorChannels.Code, DesignatorChannels.Range })
                Assert.Equal(PanelButtonState.Disabled, StateOf(run, b));
        }

        [Fact]
        public void SDS5c_EveryNumber_IsLanguageNeutral_UnderCsCz()
        {
            var before = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("cs-CZ");
            try
            {
                State(DesignatorPhase.Lasing, code: 1511, range: 12345.6f, laseFrom: 100 - 0.5);
                var texts = Texts(Frame());
                Assert.Contains("1511", texts);
                Assert.Contains("12 346 m", texts);
                Assert.Contains("60 s", texts);
                Assert.DoesNotContain(texts, t => t.Contains(',') || t.Contains(' '));
            }
            finally { CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = before; }
        }

        [Fact]
        public void SDS5c_ASteadyFrame_AllocatesNothing()
        {
            var st = State(DesignatorPhase.Lasing, range: 1240f, laseFrom: 90);
            st.HasTarget = 1;
            st.Target = new Hrot.AI.Behaviors.Devices.Contracts.TargetLocation { Point = new Vector3(800, 300, 0), Range = 850f, FromType = 640 };
            st.TargetFrom = EntityRef.Of(_w, _r);
            UnitMemory.Ref<DesignatorState>(_w, _d) = st;
            for (int i = 0; i < 300; i++) { _w.SetSingleton(new GlobalTime { DeltaTime = 1f / 60f, TotalTime = 100 + i / 600.0, FrameNumber = i }); Frame(); }   // warm
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 30; i++)
            {
                _w.SetSingleton(new GlobalTime { DeltaTime = 1f / 60f, TotalTime = 100.5 + i / 600.0, FrameNumber = 300 + i });
                _buf.EndFrame(0f);
                _sys.Execute(_w, 0f);
            }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal(1, _sys.HudRunsDrawn);
        }
    }
}
