using System;
using System.Globalization;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Devices;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Motion;
using Hrot.AI.Behaviors.Brains;
using Hrot.Map.Definitions.Behavior;
// the gizmo primitive's string (GizmoMap.Contracts), not Fdp.Core's
using HudText = Fdp.Toolkit.Diagnostics.Gizmos.FixedString32;

namespace Hrot.AI.Behaviors.Devices.DroneStation
{
    /// <summary>
    /// ⭐⭐⭐ Simulated devices SD-S2d (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13c D-111, mockup <c>mockups/devices-hud-drone.svg</c> panels 12–15)
    /// — THE DRONE GROUND STATION's ON-SCREEN DISPLAY: one stateless projector on the BRAIN (as the rangefinder's and the binoculars'), run by
    /// <c>StatelessGizmoSystem</c> inside a <c>HudScope</c> for the station device's network id, so only a view BOUND to the station draws it (the view rides
    /// the drone's camera — <see cref="DeviceView"/>, D-109). Keyed as every device HUD (<see cref="DeviceHuds.IsVisible"/>).
    /// <para>It draws the mode chip (green = a good link, amber RTH / a weak link, red FAILSAFE / NO LINK) and ARMED; the battery % with its glyph (green
    /// &gt; 30, amber ≤ 30, red ≤ 15 and blinking by frame count); the link bars (0–4 from <see cref="DroneLink.Quality"/>); the heading tape with the drone's
    /// heading under a gold caret; the gimbal pitch scale (0° … −90°) with the camera's pitch; the centre mark; ALT above ground · SPD · ⌂ distance home; the
    /// TAKEOFF / LAND / RTH chips through <see cref="HudPanel.Button"/> (the active one lit, one a press waits on in pause framed gold and tagged
    /// <c>queued</c>, R-4300); the BATTERY LOW — RETURN HOME banner (BATTERY EMPTY at 0); and LINK LOST — the picture darkened, the glyph, the reason
    /// (JAMMED / OUT OF RANGE), the failsafe's countdown and next steps, the readouts greyed at the last values the link carried, the commands hidden.</para>
    /// <para>⭐ What it reads: the station's own <see cref="DroneStationState"/> (unit memory) and its drone's components — <see cref="DroneLink"/>,
    /// <see cref="DroneBattery"/>, its <see cref="SimTransform"/>, its velocity and its <see cref="RemotePilotState"/> (the launch point, the failsafe's end).
    /// ⭐ A read of ANOTHER entity is fine here: a projector runs in the serial PostSimulation gizmo pass, never inside a parallel phase (R-4212 binds the
    /// phases' writers and readers, not the presentation pass). It writes nothing.</para>
    /// <para>⭐ 0 B per steady frame (R-2084): every constant text built once; a number's text is formatted on the stack and kept until the number changes.
    /// Every number is invariant (R-4221).</para>
    /// <para>Layout: the mockup's own numbers (a 640 × 360 frame) scaled to the canvas (<see cref="K"/> = 2). ⚠ The gizmo font has no FontAwesome glyphs (it is
    /// Roboto — <c>GizmoFont</c>), so the battery, the house, the warning and the camera-slash glyphs are drawn as shapes in the glyph's own box, as the
    /// rangefinder's ✓ and ▸ are.</para>
    /// </summary>
    [GizmoProjector(typeof(DeviceInfo), typeof(PossessedBy), Hud = true)]
    public sealed class DroneStationHudGizmo : IStatelessGizmo, IGizmoVisibilityPolicy
    {
        /// <summary>The mockup frame in its own pixels; canvas = mockup × <see cref="K"/>.</summary>
        public const float MockupWidth = 640f, MockupHeight = 360f;

        /// <summary>Mockup pixels → canvas pixels (2).</summary>
        public const float K = ViewCanvas.Width / MockupWidth;

        private const byte L = DebugTraceLayers.DeviceHud;

        // ── the mockup's colours (mockup_kit C.GREEN / C.AMBER / C.RED, the sheet's greys) ───────────────────────────────────────────────
        public static readonly Rgba32 White = new(255, 255, 255), Gold = new(255, 215, 0), Good = new(91, 208, 106), Warn = new(242, 192, 51), Bad = new(230, 85, 85);
        public static readonly Rgba32 Label = new(160, 160, 170), Scale = new(200, 200, 208), TapeText = new(224, 224, 232), BarOff = new(90, 90, 100);
        private static readonly Rgba32 Halo = new(0, 0, 0, 153), ChipFill = new(0, 0, 0, 178), TapeFill = new(0, 0, 0, 115), Black = new(0, 0, 0);
        private static readonly Rgba32 Dark = new(0, 0, 0, 158), BannerFill = new(58, 26, 26, 217);

        /// <summary>The battery's colour bands (% left): amber at or below <see cref="BatteryAmberPct"/>, red (and blinking) at or below <see cref="BatteryLowPct"/>.</summary>
        public const int BatteryAmberPct = 30, BatteryLowPct = 15;

        /// <summary>Frames per half blink of the low battery.</summary>
        public const int BlinkFrames = 15;

        /// <summary>The heading tape: degrees either side, canvas px per degree (mockup px × K).</summary>
        public const float TapeHalfDeg = 60f, TapePxPerDeg = 2.1f;

        private static readonly HudText Armed = new("ARMED"), Disarmed = new("DISARMED"), AltLabel = new("ALT"), SpdLabel = new("SPD"), Cam = new("CAM"),
            LinkLostText = new("LINK LOST"), JammedText = new("JAMMED"), OutOfRange = new("OUT OF RANGE"), NoDrone = new("NO DRONE"),
            BatteryLowText = new("BATTERY LOW — RETURN HOME"), BatteryEmptyText = new("BATTERY EMPTY"), QueuedText = new("queued"),
            ReturnHomeStep = new("return home"), LandStep = new("land"), FailsafeLanding = new("FAILSAFE: landing"), FailsafeHoverDone = new("FAILSAFE: hover");

        /// <summary>The chip's word for each <see cref="DroneStationMode"/> (the mockup: MANUAL while the sticks fly it, RTH while returning).</summary>
        private static readonly string[] ModeNames = { "OFF", "NO LINK", "STANDBY", "STANDBY", "TAKEOFF", "MANUAL", "HOVER", "LAND", "RTH", "LANDED", "FAILSAFE" };
        private static readonly HudText[] ModeTexts = Build(ModeNames);
        private static readonly float[] ModeWidths = Widths(ModeNames, 13f, 26f);

        private static readonly HudText[] TapeLabels = BuildTape();
        private static readonly HudText[] GimbalLabels = { new("0°"), new("-30°"), new("-60°"), new("-90°") };
        private static readonly FixedString64 Behavior = new(BehaviorNames.DeviceDroneStation);

        private static Vector2 P(float x, float y) => new(x * K, y * K);

        private static HudText[] Build(string[] names)
        {
            var t = new HudText[names.Length];
            for (int i = 0; i < names.Length; i++) t[i] = new HudText(names[i]);
            return t;
        }

        private static float[] Widths(string[] names, float size, float pad)
        {
            var w = new float[names.Length];
            for (int i = 0; i < names.Length; i++) w[i] = TextWidth(names[i], size) + pad;
            return w;
        }

        private static HudText[] BuildTape()
        {
            var t = new HudText[12];
            for (int i = 0; i < 12; i++)
            {
                int a = i * 30;
                t[i] = new HudText(a switch { 0 => "N", 90 => "E", 180 => "S", 270 => "W", _ => a.ToString(CultureInfo.InvariantCulture) });
            }
            return t;
        }

        // ── keying ───────────────────────────────────────────────────────────────────────────────────────────────────────────────────
        public bool IsGloballyEnabled(ISimulationView view) => true;

        /// <summary>R7-3 — the device TYPE (its behaviour is the ground station's), operated, and this node holds its brain's authority (the one rule).</summary>
        public bool IsEntityVisible(ISimulationView view, Entity entity) => DeviceHuds.IsVisible(view, entity, in Behavior);

        // ── what one frame shows (the drone's telemetry, read once) ───────────────────────────────────────────────────────────────────
        /// <summary>The OSD's readouts of one frame: the station's state joined to its drone's (public for the rails).</summary>
        public struct Telemetry
        {
            public bool HasDrone, Lost, Jammed, HasBattery;
            public float Quality, Battery01, AltM, SpeedMps, HomeM, HeadingDeg;
            public PilotPhase Phase;
            public double FailsafeLeft;
        }

        /// <summary>The telemetry of <paramref name="st"/>'s drone in <paramref name="view"/> (a lost link when it has none).</summary>
        public static Telemetry Read(ISimulationView view, in DroneStationState st)
        {
            var t = new Telemetry { Lost = true };
            if (view is not EntityRepository w) return t;
            var drone = st.Drone;
            if (drone.IsNull || !w.IsAlive(drone)) return t;
            t.HasDrone = true;
            var link = RemoteLinks.Read(w, drone);
            t.Lost = link.Lost != 0; t.Jammed = link.Jammed != 0; t.Quality = link.Quality; t.Phase = link.Phase;
            t.HasBattery = w.IsComponentTypeRegistered<DroneBattery>() && w.HasComponent<DroneBattery>(drone);
            t.Battery01 = t.HasBattery ? w.GetComponentRO<DroneBattery>(drone).Remaining01 : 1f;
            if (w.HasComponent<SimTransform>(drone))
            {
                ref readonly var tf = ref w.GetComponentRO<SimTransform>(drone);
                t.HeadingDeg = WorldRotation.ToHeadingDeg(tf.Rotation);
                float ground = Fdp.Toolkit.World.WorldQuery.Of(w)?.GroundHeightAt(tf.Position.X, tf.Position.Y) ?? 0f;
                t.AltM = MathF.Max(0f, tf.Position.Z - ground);
                var pilot = UnitMemory.GetInView<RemotePilotState>(view, drone);
                t.HomeM = pilot.Ready != 0 ? Vector2.Distance(new Vector2(tf.Position.X, tf.Position.Y), new Vector2(pilot.Home.X, pilot.Home.Y)) : 0f;
                double now = w.HasSingleton<GlobalTime>() ? w.GetSingleton<GlobalTime>().TotalTime : 0d;
                t.FailsafeLeft = Math.Max(0d, pilot.FailsafeUntil.Seconds - now);
            }
            // ⭐ paused: nothing moves (the velocity component keeps the last frame's value; the OSD says what the world does — panel 15's SPD 0)
            if (Fdp.Toolkit.Replication.Attributes.FormAttributes.TimeAdvances(w) && Fdp.Toolkit.Replication.ReplicatedMotion.Of(w, drone, out _, out var v))
                t.SpeedMps = v.Length();
            return t;
        }

        // ── the held readouts (the last values the link carried) and the number texts, kept until they change ─────────────────────────
        private Entity _heldFor = Entity.Null;
        private float _heldAlt, _heldSpd, _heldHome, _heldHdg;
        private Cached _alt, _spd, _home, _hdg, _pct, _left;

        private struct Cached
        {
            public int Key;
            public bool Set;
            public HudText Text;
        }

        // ── the HUD ──────────────────────────────────────────────────────────────────────────────────────────────────────────────────
        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            var st = UnitMemory.GetInView<DroneStationState>(view, entity);
            long frame = view is EntityRepository fw && fw.HasSingleton<GlobalTime>() ? fw.GetSingleton<GlobalTime>().FrameNumber : 0;
            if (st.Power == 0 || st.Mode == DroneStationMode.Off)
            {
                ModeChip(draw, DroneStationMode.Off, Label, armed: false, showArmed: false);
                return;
            }
            var t = Read(view, in st);

            // the readouts: live while the link holds, the last values it carried while it is lost
            if (!t.Lost || _heldFor != entity)
            {
                _heldFor = entity; _heldAlt = t.AltM; _heldSpd = t.SpeedMps; _heldHome = t.HomeM; _heldHdg = t.HeadingDeg;
            }
            float alt = t.Lost ? _heldAlt : t.AltM, spd = t.Lost ? _heldSpd : t.SpeedMps, home = t.Lost ? _heldHome : t.HomeM, hdg = t.Lost ? _heldHdg : t.HeadingDeg;

            if (t.Lost) DrawDark(draw);   // panel 14: the picture darkened, under everything else

            HeadingTape(draw, hdg);
            bool armed = st.Command is RemoteMode.Armed or RemoteMode.Fly or RemoteMode.Land or RemoteMode.ReturnHome;
            ModeChip(draw, st.Mode, ModeColour(st.Mode, in t), armed, showArmed: true);
            if (t.HasBattery) Battery(draw, t.Battery01, frame);
            LinkBars(draw, in t);
            var readout = t.Lost ? Label : White;
            Readouts(draw, alt, spd, home, readout);

            if (t.Lost) { LinkLost(draw, in t); return; }   // the gimbal, the centre and the commands are hidden while the link is lost

            Gimbal(draw, st.CameraPitch * (180f / MathF.PI));
            Centre(draw);
            if (t.HasBattery && Percent(t.Battery01) <= BatteryLowPct) Banner(draw, t.Battery01 <= 0f ? BatteryEmptyText : BatteryLowText);
            Commands(draw, view, entity, in st);
        }

        /// <summary>The battery's whole percent (0 … 100).</summary>
        public static int Percent(float remaining01) => (int)MathF.Round(Math.Clamp(remaining01, 0f, 1f) * 100f);

        /// <summary>The chip's colour: grey OFF, red FAILSAFE / NO LINK, amber RTH or a weak link, green otherwise (a good link).</summary>
        public static Rgba32 ModeColour(DroneStationMode mode, in Telemetry t)
            => mode switch
            {
                DroneStationMode.Off => Label,
                DroneStationMode.Failsafe or DroneStationMode.NoLink => Bad,
                DroneStationMode.Returning => Warn,
                _ => t.Lost ? Bad : t.Quality < 0.5f ? Warn : Good,
            };

        /// <summary>The link bars lit (0 … 4) for <paramref name="t"/>: 0 when lost, else the quality in quarters (rounded up).</summary>
        public static int Bars(in Telemetry t) => t.Lost ? 0 : Math.Clamp((int)MathF.Ceiling(t.Quality * 4f - 1e-4f), 0, 4);

        // ── the pieces, in the mockup's numbers ───────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Panel 14 — the picture darkened (62 %), past the canvas so a letterboxed view is dark to its edges.</summary>
        private static void DrawDark(IDebugDrawBuilder draw)
        {
            const float Far = 4000f;
            draw.EmitRaw(ViewCanvas.Rect(P(-Far, -Far), P(MockupWidth + 2 * Far, MockupHeight + 2 * Far), Dark, default, 0f, L));
        }

        /// <summary>The mode chip, top-left (14, 14), and ARMED / DISARMED right of it.</summary>
        private static void ModeChip(IDebugDrawBuilder draw, DroneStationMode mode, Rgba32 c, bool armed, bool showArmed)
        {
            int m = (int)mode < ModeTexts.Length ? (int)mode : 0;
            float w = ModeWidths[m];
            draw.EmitRaw(ViewCanvas.Rect(P(14, 14), new Vector2(w, 24) * K, ChipFill, c, 1.4f * K, L));
            draw.EmitRaw(ViewCanvas.Text((14 + w / 2) * K, 31 * K, ModeTexts[m], c, 13 * K, L, ViewCanvasAlign.Centre, GizmoTextStyle.HaloBold));
            if (showArmed)
                draw.EmitRaw(ViewCanvas.Text((22 + w) * K, 31 * K, armed ? Armed : Disarmed, armed ? Warn : Label, 11 * K, L, style: GizmoTextStyle.HaloBold));
        }

        /// <summary>The battery, top-right: the glyph at (548, 18) 16 px and "78 %" right-aligned at 626 — red and blinking at or below 15 %.</summary>
        private void Battery(IDebugDrawBuilder draw, float remaining01, long frame)
        {
            int pct = Percent(remaining01);
            var c = pct > BatteryAmberPct ? Good : pct > BatteryLowPct ? Warn : Bad;
            if (pct <= BatteryLowPct && (frame / BlinkFrames) % 2 == 1) c = new Rgba32(c.R, c.G, c.B, 77);   // the blink's dim half (still legible)
            BatteryGlyph(draw, MockupWidth - 92, 18, 16, pct > BatteryAmberPct ? 1f : 0.25f, c);
            draw.EmitRaw(ViewCanvas.Text((MockupWidth - 14) * K, 32 * K, Text(ref _pct, pct, " %"), c, 14 * K, L, ViewCanvasAlign.Right, GizmoTextStyle.HaloBold));
        }

        /// <summary>FA battery-full / battery-quarter as shapes in the glyph's box (x, y top-left, <paramref name="sz"/> em): the body, its charge, the nub.</summary>
        private static void BatteryGlyph(IDebugDrawBuilder draw, float x, float y, float sz, float level, Rgba32 c)
        {
            float u = sz / 512f;   // FA units → mockup px
            draw.EmitRaw(ViewCanvas.Rect(P(x + 16 * u, y + 80 * u), new Vector2(496 * u, 352 * u) * K, new Rgba32(0, 0, 0, 120), c, 40 * u * K, L));
            draw.EmitRaw(ViewCanvas.Rect(P(x + 96 * u, y + 160 * u), new Vector2(336 * level * u, 192 * u) * K, c, default, 0f, L));
            draw.EmitRaw(ViewCanvas.Rect(P(x + 512 * u, y + 192 * u), new Vector2(64 * u, 128 * u) * K, c, default, 0f, L));
        }

        /// <summary>The link bars, left of the battery (x 480 + 7 i, bottoms at 34): lit up to <see cref="Bars"/>, green, amber at one bar, red at none.</summary>
        private static void LinkBars(IDebugDrawBuilder draw, in Telemetry t)
        {
            int bars = Bars(in t);
            var c = bars >= 2 ? Good : bars == 1 ? Warn : Bad;
            for (int i = 0; i < 4; i++)
            {
                float h = 5 + i * 4;
                draw.EmitRaw(ViewCanvas.Rect(P(MockupWidth - 160 + i * 7, 34 - h), new Vector2(5, h) * K, i < bars ? c : BarOff, default, 0f, L));
            }
        }

        /// <summary>The heading tape, top-centre: ±60° at 2.1 px/°, a tall tick and its label every 30°, the heading under the gold caret.</summary>
        private void HeadingTape(IDebugDrawBuilder draw, float hdg)
        {
            const float cx = MockupWidth / 2f;
            int h = ((int)MathF.Round(hdg) % 360 + 360) % 360;
            draw.EmitRaw(ViewCanvas.Rect(P(cx - 130, 12), new Vector2(260, 26) * K, TapeFill, default, 0f, L));
            for (int a = h - 60 - (h % 5); a <= h + 60; a += 5)
            {
                int d = a - h;
                if (Math.Abs(d) > 60) continue;
                float px = cx + d * TapePxPerDeg;
                int wrapped = ((a % 360) + 360) % 360;
                bool big = wrapped % 30 == 0;
                HLine(draw, px, 38, px, big ? 28 : 33, White, 1f);
                if (big) draw.EmitRaw(ViewCanvas.Text(px * K, 25 * K, TapeLabels[wrapped / 30], TapeText, 10 * K, L, ViewCanvasAlign.Centre));
            }
            draw.EmitRaw(ViewCanvas.Triangle(P(cx - 5, 44), P(cx + 5, 44), P(cx, 38), Gold, L));
            draw.EmitRaw(ViewCanvas.Text(cx * K, 58 * K, Heading(ref _hdg, h), Gold, 11 * K, L, ViewCanvasAlign.Centre, GizmoTextStyle.HaloBold));
        }

        /// <summary>The gimbal pitch scale, left edge (x 22, 0° at y 92 … −90° at 252): ticks every 30°, the gold marker at the camera's pitch, CAM under it.</summary>
        private static void Gimbal(IDebugDrawBuilder draw, float pitchDeg)
        {
            const float gx = 22, gy0 = 92, gy1 = 252;
            HLine(draw, gx, gy0, gx, gy1, Scale, 1.2f);
            for (int i = 0; i < 4; i++)
            {
                float py = gy0 + (i * 30f / 90f) * (gy1 - gy0);
                HLine(draw, gx - 4, py, gx + 4, py, Scale, 1f);
                draw.EmitRaw(ViewCanvas.Text((gx + 9) * K, (py + 4) * K, GimbalLabels[i], Scale, 9.5f * K, L));
            }
            float y = gy0 + (Math.Clamp(-pitchDeg, 0f, 90f) / 90f) * (gy1 - gy0);
            draw.EmitRaw(ViewCanvas.Triangle(P(gx - 9, y - 5), P(gx - 9, y + 5), P(gx - 2, y), Gold, L));
            draw.EmitRaw(ViewCanvas.Text((gx - 6) * K, (gy1 + 18) * K, Cam, Label, 9.5f * K, L));
        }

        /// <summary>The centre mark: three arms (left, right, down) and the dot.</summary>
        private static void Centre(IDebugDrawBuilder draw)
        {
            const float cx = MockupWidth / 2f, cy = MockupHeight / 2f;
            HLine(draw, cx - 6, cy, cx - 20, cy, White, 1.4f);
            HLine(draw, cx + 6, cy, cx + 20, cy, White, 1.4f);
            HLine(draw, cx, cy + 6, cx, cy + 20, White, 1.4f);
            draw.EmitRaw(ViewCanvas.Disc(P(cx, cy), 1.6f * K, White, default, 0f, L));
        }

        /// <summary>ALT · SPD · ⌂ distance home, bottom-centre (baseline 340).</summary>
        private void Readouts(IDebugDrawBuilder draw, float alt, float spd, float home, Rgba32 c)
        {
            const float cx = MockupWidth / 2f, yb = MockupHeight - 20;
            draw.EmitRaw(ViewCanvas.Text((cx - 150) * K, yb * K, AltLabel, Label, 10.5f * K, L, ViewCanvasAlign.Right));
            draw.EmitRaw(ViewCanvas.Text((cx - 145) * K, yb * K, Text(ref _alt, (int)MathF.Round(alt), " m"), c, 15 * K, L, style: GizmoTextStyle.HaloBold));
            draw.EmitRaw(ViewCanvas.Text((cx - 20) * K, yb * K, SpdLabel, Label, 10.5f * K, L, ViewCanvasAlign.Right));
            draw.EmitRaw(ViewCanvas.Text((cx - 15) * K, yb * K, Speed(ref _spd, spd), c, 15 * K, L, style: GizmoTextStyle.HaloBold));
            HouseGlyph(draw, cx + 98, yb - 13, 13, Label);
            draw.EmitRaw(ViewCanvas.Text((cx + 118) * K, yb * K, Text(ref _home, (int)MathF.Round(home), " m"), c, 15 * K, L, style: GizmoTextStyle.HaloBold));
        }

        /// <summary>FA house as shapes in its box (x, y top-left): the roof and the body with its door.</summary>
        private static void HouseGlyph(IDebugDrawBuilder draw, float x, float y, float sz, Rgba32 c)
        {
            float u = sz / 512f;
            draw.EmitRaw(ViewCanvas.Triangle(P(x + 288 * u, y + 40 * u), P(x + 560 * u, y + 270 * u), P(x + 16 * u, y + 270 * u), c, L));
            draw.EmitRaw(ViewCanvas.Rect(P(x + 96 * u, y + 250 * u), new Vector2(384 * u, 230 * u) * K, c, default, 0f, L));
            draw.EmitRaw(ViewCanvas.Rect(P(x + 240 * u, y + 330 * u), new Vector2(96 * u, 150 * u) * K, Black, default, 0f, L));
        }

        /// <summary>The BATTERY LOW / EMPTY banner under the heading tape (170, 86, 300 × 30) with the warning glyph.</summary>
        private static void Banner(IDebugDrawBuilder draw, in HudText text)
        {
            const float cx = MockupWidth / 2f;
            draw.EmitRaw(ViewCanvas.Rect(P(cx - 150, 86), new Vector2(300, 30) * K, BannerFill, Bad, 1.4f * K, L));
            WarningGlyph(draw, cx - 136, 93, 15, Bad);
            draw.EmitRaw(ViewCanvas.Text((cx + 10) * K, 107 * K, text, Bad, 13 * K, L, ViewCanvasAlign.Centre, GizmoTextStyle.HaloBold));
        }

        /// <summary>FA triangle-exclamation as shapes in its box: the triangle and the dark "!".</summary>
        private static void WarningGlyph(IDebugDrawBuilder draw, float x, float y, float sz, Rgba32 c)
        {
            float u = sz / 512f;
            draw.EmitRaw(ViewCanvas.Triangle(P(x + 256 * u, y + 16 * u), P(x + 504 * u, y + 470 * u), P(x + 8 * u, y + 470 * u), c, L));
            draw.EmitRaw(ViewCanvas.Line(P(x + 256 * u, y + 170 * u), P(x + 256 * u, y + 320 * u), Black, 48 * u * K, L));
            draw.EmitRaw(ViewCanvas.Disc(P(x + 256 * u, y + 392 * u), 28 * u * K, Black, default, 0f, L));
        }

        /// <summary>Panel 14 — LINK LOST: the camera-slash glyph, the words, the reason and the failsafe's line, centred.</summary>
        private void LinkLost(IDebugDrawBuilder draw, in Telemetry t)
        {
            const float cx = MockupWidth / 2f, cy = MockupHeight / 2f;
            VideoSlashGlyph(draw, cx - 16, cy - 62, 30, Bad);
            draw.EmitRaw(ViewCanvas.Text(cx * K, (cy - 4) * K, LinkLostText, Bad, 26 * K, L, ViewCanvasAlign.Centre, GizmoTextStyle.HaloBold));
            var reason = !t.HasDrone ? NoDrone : t.Jammed ? JammedText : OutOfRange;
            draw.EmitRaw(ViewCanvas.Text(cx * K, (cy + 20) * K, reason, Warn, 13 * K, L, ViewCanvasAlign.Centre, GizmoTextStyle.HaloBold));
            FailsafeLine(draw, in t, cy + 44);
        }

        /// <summary>The failsafe's steps — <c>FAILSAFE: hover 3 s ▸ return home ▸ land</c> — the step it is in white, the rest in the line's light grey (the mockup's one regular weight). The ▸ is a small
        /// filled triangle (the gizmo font has none). Not drawn when the drone is not flying its failsafe (lost on the ground).</summary>
        private void FailsafeLine(IDebugDrawBuilder draw, in Telemetry t, float baseline)
        {
            int step = t.Phase switch { PilotPhase.FailsafeHover => 0, PilotPhase.FailsafeReturn => 1, PilotPhase.FailsafeLand => 2, _ => -1 };
            if (step < 0) return;
            var first = step == 0 ? Countdown(ref _left, (int)Math.Ceiling(t.FailsafeLeft)) : step == 2 ? FailsafeLanding : FailsafeHoverDone;
            if (step == 2)
            {
                draw.EmitRaw(ViewCanvas.Text(MockupWidth / 2f * K, baseline * K, first, White, 12 * K, L, ViewCanvasAlign.Centre));
                return;
            }
            // ⭐ anchored, not laid out from estimated widths (the S2d render: a width estimate of the capitals left a gap after "3 s"): the middle step centred
            //   where the mockup puts it (365), the ▸ either side of it, the first step RIGHT-aligned before the left ▸, "land" left-aligned after the right one —
            //   so the gaps are the mockup's whatever the font's advances
            draw.EmitRaw(ViewCanvas.Text(FailsafeMid * K, baseline * K, ReturnHomeStep, step == 1 ? White : TapeText, 12 * K, L, ViewCanvasAlign.Centre));
            Step(draw, FailsafeMid - 39f, baseline - 4f);
            Step(draw, FailsafeMid + 39f, baseline - 4f);
            draw.EmitRaw(ViewCanvas.Text((FailsafeMid - 48f) * K, baseline * K, first, step == 0 ? White : TapeText, 12 * K, L, ViewCanvasAlign.Right));
            draw.EmitRaw(ViewCanvas.Text((FailsafeMid + 48f) * K, baseline * K, LandStep, TapeText, 12 * K, L));
        }

        /// <summary>The failsafe line's middle step's centre (mockup px): "return home" where the mockup's one centred line puts it.</summary>
        public const float FailsafeMid = 365f;

        private static void Step(IDebugDrawBuilder draw, float x, float y)
            => draw.EmitRaw(ViewCanvas.Triangle(P(x - 3, y - 4), P(x + 3, y), P(x - 3, y + 4), TapeText, L));

        /// <summary>FA video-slash as shapes in its box (640 × 512 units): the camera body, its lens and the slash with its dark gap.</summary>
        private static void VideoSlashGlyph(IDebugDrawBuilder draw, float x, float y, float sz, Rgba32 c)
        {
            float u = sz / 512f;
            draw.EmitRaw(ViewCanvas.Rect(P(x + 64 * u, y + 112 * u), new Vector2(336 * u, 288 * u) * K, c, default, 0f, L));
            draw.EmitRaw(ViewCanvas.Triangle(P(x + 416 * u, y + 220 * u), P(x + 608 * u, y + 120 * u), P(x + 608 * u, y + 392 * u), c, L));
            draw.EmitRaw(ViewCanvas.Triangle(P(x + 416 * u, y + 220 * u), P(x + 608 * u, y + 392 * u), P(x + 416 * u, y + 292 * u), c, L));
            draw.EmitRaw(ViewCanvas.Line(P(x + 8 * u, y + 24 * u), P(x + 632 * u, y + 488 * u), Black, 88 * u * K, L));
            draw.EmitRaw(ViewCanvas.Line(P(x + 8 * u, y + 24 * u), P(x + 632 * u, y + 488 * u), c, 44 * u * K, L));
        }

        // ── ⭐ SD-S5v (§3.13e D-122 / D-128) — the command chips through HudPanel: TAKEOFF / LAND / RTH, right-aligned at x 626, y 294 (22 high) ──────────

        /// <summary>The chip row (mockup px): its right edge, top, height and the gap between chips.</summary>
        public const float ChipRight = MockupWidth - 14f, ChipY = MockupHeight - 66f, ChipH = 22f, ChipGap = 6f;

        /// <summary>The buttons the row draws (the station's TKB elements): TAKEOFF, LAND, RTH.</summary>
        public static readonly int[] ChipButtons = { DroneStationChannels.TakeOff, DroneStationChannels.Land, DroneStationChannels.Rth };

        private sealed class Chips
        {
            public long Type = long.MinValue;
            public readonly HudText[] Labels = new HudText[3];
            public readonly HudText[] Badges = new HudText[3];
            public readonly ushort[] Keys = new ushort[3];
            public readonly float[] Widths = new float[3];
        }

        private static readonly string[] DefaultNames = { "TAKEOFF", "LAND", "RTH" };
        private Chips _chips = new();

        /// <summary>The chips' labels and hotkeys from the station's TKB type (D-142 / D-124) — built once per type (allocates then, never per frame). A chip is the
        /// mockup's width (the label + 16) plus its key box.</summary>
        private Chips ChipsOf(ISimulationView view, Entity entity)
        {
            long type = view is EntityRepository w && w.IsComponentTypeRegistered<DeviceInfo>() && w.HasComponent<DeviceInfo>(entity) ? w.GetComponentRO<DeviceInfo>(entity).DeviceType : 0;
            var c = _chips;
            if (c.Type == type) return c;
            c = new Chips { Type = type };
            Fdp.Toolkit.Tkb.Domain.DeviceDefDto? dto = null;
            if (view is EntityRepository rw && rw.HasSingletonManaged<Fdp.Interfaces.ITkbDatabase>()
                && rw.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>() is { } db && db.TryGetByType(type, out var t) && t != null)
                dto = t.GetDescriptor<Fdp.Toolkit.Tkb.Domain.DeviceDefDto>();
            for (int i = 0; i < 3; i++)
            {
                var el = dto?.ElementAt(Fdp.Toolkit.Tkb.Domain.DeviceElementKind.Button, ChipButtons[i]);
                string name = el?.Name ?? DefaultNames[i];
                c.Labels[i] = new HudText(name);
                c.Badges[i] = HudPanel.Badge(el?.Hotkey);
                c.Keys[i] = HudPanel.HotkeyCode(el?.Hotkey);
                float u = ChipH / HudPanel.ReferenceHeight;
                c.Widths[i] = TextWidth(name, 11f) + 16f + (c.Badges[i].IsEmpty ? 0f : (HudPanel.BadgeWidth(c.Badges[i].Length) + 5f) * u);
            }
            _chips = c;
            return c;
        }

        /// <summary>A chip's state: lit when it is the command in force (TAKEOFF while it climbs, LAND, RTH) or a press waits on it in pause; greyed when the
        /// press would do nothing now (the station's own rules, <c>DroneStationBehavior.Apply</c>); else normal.</summary>
        public static PanelButtonState StateOf(in DroneStationState st, int button)
        {
            bool queued = (st.Queued & DroneStationChannels.Bit(button)) != 0;
            if (queued) return PanelButtonState.Lit;
            switch (button)
            {
                case DroneStationChannels.TakeOff:
                    return st.Mode == DroneStationMode.TakeOff ? PanelButtonState.Lit
                         : st.Command is RemoteMode.Armed or RemoteMode.Land or RemoteMode.ReturnHome ? PanelButtonState.Normal : PanelButtonState.Disabled;
                case DroneStationChannels.Land:
                    return st.Command == RemoteMode.Land ? PanelButtonState.Lit
                         : st.Command is RemoteMode.Fly or RemoteMode.ReturnHome ? PanelButtonState.Normal : PanelButtonState.Disabled;
                case DroneStationChannels.Rth:
                    return st.Command == RemoteMode.ReturnHome ? PanelButtonState.Lit
                         : st.Command is RemoteMode.Fly or RemoteMode.Land ? PanelButtonState.Normal : PanelButtonState.Disabled;
            }
            return PanelButtonState.Normal;
        }

        private void Commands(IDebugDrawBuilder draw, ISimulationView view, Entity entity, in DroneStationState st)
        {
            var c = ChipsOf(view, entity);
            float xx = ChipRight;
            for (int i = 2; i >= 0; i--)
            {
                float w = c.Widths[i];
                xx -= w;
                int button = ChipButtons[i];
                HudPanel.Button(draw, Fdp.Toolkit.Tkb.Domain.DeviceElementKind.Button, button, P(xx, ChipY), new Vector2(w, ChipH) * K, c.Labels[i], c.Badges[i],
                                StateOf(in st, button), c.Keys[i], L);
                if ((st.Queued & DroneStationChannels.Bit(button)) != 0)
                {
                    // R-4300 — a mode press made in pause runs on the first live frame: the chip framed gold, "queued" over it
                    draw.EmitRaw(ViewCanvas.Rect(P(xx - 1.5f, ChipY - 1.5f), new Vector2(w + 3f, ChipH + 3f) * K, default, Gold, 1.2f * K, L));
                    draw.EmitRaw(ViewCanvas.Text((xx + w / 2f) * K, (ChipY - 6f) * K, QueuedText, Gold, 10.5f * K, L, ViewCanvasAlign.Centre, GizmoTextStyle.HaloBold));
                }
                xx -= ChipGap;
            }
        }

        /// <summary>The left edge (mockup px) of chip <paramref name="i"/> (0 TAKEOFF, 1 LAND, 2 RTH) for <paramref name="widths"/> (rails).</summary>
        public static float ChipLeft(int i, ReadOnlySpan<float> widths)
        {
            float xx = ChipRight;
            for (int j = 2; j >= i; j--) { xx -= widths[j]; if (j > i) xx -= ChipGap; }
            return xx;
        }

        // ── lines and texts ───────────────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>A line with the mockup's dark halo under it (2.2 px wider).</summary>
        private static void HLine(IDebugDrawBuilder draw, float x1, float y1, float x2, float y2, Rgba32 c, float w)
        {
            draw.EmitRaw(ViewCanvas.Line(P(x1, y1), P(x2, y2), Halo, (w + 2.2f) * K, L));
            draw.EmitRaw(ViewCanvas.Line(P(x1, y1), P(x2, y2), c, w * K, L));
        }

        /// <summary>"<paramref name="value"/><paramref name="suffix"/>", invariant (R-4221), formatted on the stack and kept until the value changes (0 B).</summary>
        private static HudText Text(ref Cached cache, int value, string suffix)
        {
            if (cache.Set && cache.Key == value) return cache.Text;
            Span<char> s = stackalloc char[24];
            if (!value.TryFormat(s, out int len, default, CultureInfo.InvariantCulture)) len = 0;
            for (int i = 0; i < suffix.Length; i++) s[len++] = suffix[i];
            cache = new Cached { Key = value, Set = true, Text = new HudText(s[..len]) };
            return cache.Text;
        }

        /// <summary>"8.4 m/s" — tenths, a decimal POINT whatever the culture (R-4221).</summary>
        private static HudText Speed(ref Cached cache, float mps) => SpeedText(ref cache, (int)MathF.Round(MathF.Max(0f, mps) * 10f));

        private static HudText SpeedText(ref Cached cache, int tenths)
        {
            if (cache.Set && cache.Key == tenths) return cache.Text;
            Span<char> s = stackalloc char[24];
            if (!(tenths / 10).TryFormat(s, out int len, default, CultureInfo.InvariantCulture)) len = 0;
            s[len++] = '.'; s[len++] = (char)('0' + tenths % 10);
            foreach (char ch in " m/s") s[len++] = ch;
            cache = new Cached { Key = tenths, Set = true, Text = new HudText(s[..len]) };
            return cache.Text;
        }

        /// <summary>"024" — the heading, three digits.</summary>
        private static HudText Heading(ref Cached cache, int deg)
        {
            if (cache.Set && cache.Key == deg) return cache.Text;
            Span<char> s = stackalloc char[3];
            s[0] = (char)('0' + deg / 100 % 10); s[1] = (char)('0' + deg / 10 % 10); s[2] = (char)('0' + deg % 10);
            cache = new Cached { Key = deg, Set = true, Text = new HudText(s) };
            return cache.Text;
        }

        /// <summary>"FAILSAFE: hover 3 s" — the hover's whole seconds left.</summary>
        private static HudText Countdown(ref Cached cache, int seconds)
        {
            if (cache.Set && cache.Key == seconds) return cache.Text;
            Span<char> s = stackalloc char[31];
            int len = 0;
            foreach (char ch in "FAILSAFE: hover ") s[len++] = ch;
            if (seconds.TryFormat(s[len..], out int n, default, CultureInfo.InvariantCulture)) len += n;
            s[len++] = ' '; s[len++] = 's';
            cache = new Cached { Key = seconds, Set = true, Text = new HudText(s[..len]) };
            return cache.Text;
        }

        /// <summary>The mockup kit's Roboto advance estimate (<c>scripts/mockup_kit.py text_width</c>) — chips and lines are sized exactly as drawn there.</summary>
        public static float TextWidth(string s, float size) => Rangefinder.RangefinderHudGizmo.TextWidth(s, size);


    }
}
