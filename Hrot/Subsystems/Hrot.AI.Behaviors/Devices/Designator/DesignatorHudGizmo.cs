using System;
using System.Globalization;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Devices;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Hrot.Map.Definitions.Behavior;
// the gizmo primitive's string (GizmoMap.Contracts), not Fdp.Core's
using HudText = Fdp.Toolkit.Diagnostics.Gizmos.FixedString32;

namespace Hrot.AI.Behaviors.Devices.Designator
{
    /// <summary>
    /// ⭐⭐⭐ Simulated devices SD-S5c (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13d D-113 – D-115 / D-121, mockup <c>mockups/devices-hud-designator.svg</c>
    /// panels 16–19) — THE LASER TARGET DESIGNATOR's HUD: one stateless projector on the BRAIN (as every device HUD), run by <c>StatelessGizmoSystem</c> inside a
    /// <c>HudScope</c> for the device's network id, so only a view BOUND to it draws it; keyed by <see cref="DeviceHuds.IsVisible"/>.
    /// <para>It draws the window (the outside darkened, corner brackets), the reticle (red while lasing), the name, STATE (STANDBY green · LASING red · COOLING
    /// amber · OFF grey), CODE (gold), the LASE lamp (lit and blinking by frame count while lasing), the lase timer bar (draining: the seconds left of
    /// <c>LaseSeconds</c>) or the cool-down bar (filling: the share cooled, the seconds left), RANGE (<c>— m</c> / <c>1 240 m</c>), the chips POWER · LASE · CODE ·
    /// RANGE through <see cref="HudPanel.Button"/> (lit = lasing / a lase queued; greyed = refused now — CODE outside STANDBY, LASE while cooling, all but POWER
    /// while off), <c>queued</c> over LASE when a lase waits for the first live frame (D-115), and the received-target cue through the one <see cref="HudCue"/>
    /// (SD-S5b). Reads <see cref="DesignatorState"/> (<c>UnitMemory.GetInView</c>) and the sim time; writes nothing.</para>
    /// <para>⭐ 0 B per steady frame: constant texts built once, each number formatted on the stack and kept until it changes; every number invariant (R-4221).
    /// Layout: the mockup's own numbers (a 640 × 360 frame) × <see cref="K"/> on the <see cref="ViewCanvas"/>.</para>
    /// </summary>
    [GizmoProjector(typeof(DeviceInfo), typeof(PossessedBy), Hud = true)]
    public sealed class DesignatorHudGizmo : IStatelessGizmo, IGizmoVisibilityPolicy
    {
        /// <summary>The mockup frame in its own pixels; canvas = mockup × <see cref="K"/>.</summary>
        public const float MockupWidth = 640f, MockupHeight = 360f;

        /// <summary>Mockup pixels → canvas pixels (2).</summary>
        public const float K = ViewCanvas.Width / MockupWidth;

        /// <summary>The window's inset (mockup px): the view is darkened outside it, the brackets sit on its corners.</summary>
        public const float Inset = 26f;

        /// <summary>Frames per half blink of the lamp while lasing.</summary>
        public const int BlinkFrames = 15;

        private const byte L = DebugTraceLayers.DeviceHud;

        // ── the mockup's colours (the sheet's whites / greys, gold, the laser red, mockup_kit C.GREEN / C.AMBER) ─────────────────────────────
        public static readonly Rgba32 White = new(255, 255, 255), Gold = new(255, 215, 0), Laser = new(255, 59, 48), Good = new(91, 208, 106), Warn = new(242, 192, 51);
        public static readonly Rgba32 Title = new(200, 200, 208), Label = new(160, 160, 170), BarRim = new(138, 138, 150), LampOff = new(58, 58, 68);
        private static readonly Rgba32 Halo = new(0, 0, 0, 153), Mask = new(0, 0, 0, 140), Black = new(0, 0, 0), BarFill = new(0, 0, 0, 153);

        private static readonly HudText Name = new("LASER DESIGNATOR"), StateLabel = new("STATE"), CodeLabel = new("CODE"), LaseWord = new("LASE"),
            CoolWord = new("COOL"), RangeLabel = new("RANGE"), NoRange = new("— m"), QueuedText = new("queued");

        private static readonly HudText[] PhaseTexts = { new("OFF"), new("STANDBY"), new("LASING"), new("COOLING") };
        private static readonly FixedString64 Behavior = new(BehaviorNames.DeviceDesignator);

        private static Vector2 P(float x, float y) => new(x * K, y * K);

        /// <summary>The colour of a phase's STATE text.</summary>
        public static Rgba32 PhaseColour(DesignatorPhase phase)
            => phase switch { DesignatorPhase.Standby => Good, DesignatorPhase.Lasing => Laser, DesignatorPhase.Cooling => Warn, _ => Label };

        // ── keying ───────────────────────────────────────────────────────────────────────────────────────────────────────────────────
        public bool IsGloballyEnabled(ISimulationView view) => true;

        /// <summary>R7-3 — the device TYPE (its behaviour is the designator's), operated, and this node holds its brain's authority (the one rule).</summary>
        public bool IsEntityVisible(ISimulationView view, Entity entity) => DeviceHuds.IsVisible(view, entity, in Behavior);

        // ── the texts kept until their number changes ──────────────────────────────────────────────────────────────────────────────
        private struct Cached
        {
            public int Key;
            public bool Set;
            public HudText Text;
        }

        private Cached _code, _timer, _range;
        private readonly HudCueSender _sender = new();

        // ── the HUD ──────────────────────────────────────────────────────────────────────────────────────────────────────────────────
        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            var st = UnitMemory.GetInView<DesignatorState>(view, entity);
            var w = view as EntityRepository;
            long frame = w != null && w.HasSingleton<GlobalTime>() ? w.GetSingleton<GlobalTime>().FrameNumber : 0;
            double now = w != null ? Fdp.Toolkit.Combat.HitModel.Now(w) : 0d;
            bool lasing = st.Phase == DesignatorPhase.Lasing, off = st.Phase == DesignatorPhase.Off;

            DrawWindow(draw);
            DrawReticle(draw, lasing ? Laser : White);

            // status, top-left
            draw.EmitRaw(ViewCanvas.Text(40 * K, 50 * K, Name, Title, 12 * K, L, style: GizmoTextStyle.HaloBold));
            draw.EmitRaw(ViewCanvas.Text(40 * K, 70 * K, StateLabel, Label, 11 * K, L));
            draw.EmitRaw(ViewCanvas.Text(92 * K, 70 * K, PhaseTexts[(int)st.Phase & 3], PhaseColour(st.Phase), 13 * K, L, style: GizmoTextStyle.HaloBold));
            if (!off)
            {
                draw.EmitRaw(ViewCanvas.Text(40 * K, 89 * K, CodeLabel, Label, 11 * K, L));
                draw.EmitRaw(ViewCanvas.Text(92 * K, 89 * K, CodeText(st.Code), Gold, 13 * K, L, style: GizmoTextStyle.HaloBold));
            }

            DrawLamp(draw, lasing && (frame / BlinkFrames) % 2 == 0, lasing);

            // the timer under CODE: the lase draining, the cool-down filling
            if (lasing)
            {
                double left = st.LaseLeft(now);
                Timer(draw, LaseWord, st.LaseSeconds > 0f ? (float)(left / st.LaseSeconds) : 0f, Laser, Seconds(left));
            }
            else if (st.Phase == DesignatorPhase.Cooling)
            {
                double left = st.CoolLeft(now);
                Timer(draw, CoolWord, st.CoolSeconds > 0f ? 1f - (float)(left / st.CoolSeconds) : 1f, Warn, Seconds(left));
            }

            // RANGE, bottom-centre
            if (!off)
            {
                const float cx = MockupWidth / 2f;
                draw.EmitRaw(ViewCanvas.Text(cx * K, (MockupHeight - 116) * K, RangeLabel, Title, 11 * K, L, ViewCanvasAlign.Centre));
                draw.EmitRaw(ViewCanvas.Text(cx * K, (MockupHeight - 90) * K, st.HasRange != 0 ? RangeOf(st.LastRange) : NoRange, Gold, 24 * K, L,
                                             ViewCanvasAlign.Centre, GizmoTextStyle.HaloBold));
            }

            DrawChips(draw, view, entity, in st);
            if (!off && st.HasTarget != 0 && w != null) DrawCue(draw, w, entity, in st);
        }

        // ── the chips through HudPanel (§3.13e D-122 / D-128): POWER · LASE · CODE · RANGE, right-aligned at x 606 ─────────────────────────

        /// <summary>The chip row (mockup px): its right edge, top, height (the one <see cref="HudPanel"/> look at 22 px, centred in the mockup's 26-px row) and the
        /// gap between chips.</summary>
        public const float ChipRight = MockupWidth - 34f, ChipY = MockupHeight - 64f, ChipH = 22f, ChipGap = 6f;

        /// <summary>A chip's width (mockup px): the mockup's — its label at 11.5 px + 34 (the label's margin and the key box).</summary>
        public static float ChipWidth(string name) => Rangefinder.RangefinderHudGizmo.TextWidth(name, 11.5f) + 34f;

        private sealed class Chips
        {
            public long Type = long.MinValue;
            public readonly HudText[] Labels = new HudText[DesignatorChannels.Count];
            public readonly HudText[] Badges = new HudText[DesignatorChannels.Count];
            public readonly ushort[] Keys = new ushort[DesignatorChannels.Count];
            public readonly float[] Widths = new float[DesignatorChannels.Count];
        }

        private static readonly string[] DefaultNames = { "POWER", "LASE", "CODE", "RANGE" };   // with no TKB type (a bare rail): the channel's own name
        private Chips _chips = new();

        /// <summary>The chips' labels and hotkeys from the device's TKB type (D-142 / D-124) — built once per type (allocates then, never per frame).</summary>
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
            for (int i = 0; i < DesignatorChannels.Count; i++)
            {
                var el = dto?.ElementAt(Fdp.Toolkit.Tkb.Domain.DeviceElementKind.Button, i);
                string name = el?.Name ?? DefaultNames[i];
                c.Labels[i] = new HudText(name);
                c.Badges[i] = HudPanel.Badge(el?.Hotkey);
                c.Keys[i] = HudPanel.HotkeyCode(el?.Hotkey);
                c.Widths[i] = ChipWidth(name);
            }
            _chips = c;
            return c;
        }

        /// <summary>A chip's state: lit while LASE is in force (lasing) or a lase waits in a pause; greyed when the device refuses the press now (its own rules,
        /// <see cref="DesignatorBehavior.Tick"/>): all but POWER while off, CODE outside STANDBY, LASE while cooling; else normal.</summary>
        public static PanelButtonState StateOf(in DesignatorState st, int button)
        {
            if (st.Phase == DesignatorPhase.Off) return button == DesignatorChannels.Power ? PanelButtonState.Normal : PanelButtonState.Disabled;
            switch (button)
            {
                case DesignatorChannels.Lase:
                    return st.Phase == DesignatorPhase.Lasing || st.Queued != 0 ? PanelButtonState.Lit
                         : st.Phase == DesignatorPhase.Cooling ? PanelButtonState.Disabled : PanelButtonState.Normal;
                case DesignatorChannels.Code:
                    return st.Phase == DesignatorPhase.Standby ? PanelButtonState.Normal : PanelButtonState.Disabled;
            }
            return PanelButtonState.Normal;
        }

        /// <summary>The left edge (mockup px) of chip <paramref name="i"/> for <paramref name="widths"/> (right-aligned, rails).</summary>
        public static float ChipLeft(int i, ReadOnlySpan<float> widths)
        {
            float xx = ChipRight;
            for (int j = widths.Length - 1; j >= i; j--) { xx -= widths[j]; if (j > i) xx -= ChipGap; }
            return xx;
        }

        private void DrawChips(IDebugDrawBuilder draw, ISimulationView view, Entity entity, in DesignatorState st)
        {
            var c = ChipsOf(view, entity);
            float xx = ChipRight;
            for (int i = DesignatorChannels.Count - 1; i >= 0; i--)
            {
                float wi = c.Widths[i];
                xx -= wi;
                HudPanel.Button(draw, Fdp.Toolkit.Tkb.Domain.DeviceElementKind.Button, i, P(xx, ChipY), new Vector2(wi, ChipH) * K, c.Labels[i], c.Badges[i],
                                StateOf(in st, i), c.Keys[i], L);
                if (i == DesignatorChannels.Lase && st.Queued != 0 && st.Phase == DesignatorPhase.Standby)   // D-115 — a lase waits for the first live frame
                    draw.EmitRaw(ViewCanvas.Text((xx + wi / 2f) * K, (MockupHeight - 74f) * K, QueuedText, Gold, 9.5f * K, L, ViewCanvasAlign.Centre, GizmoTextStyle.HaloBold));
                xx -= ChipGap;
            }
        }

        // ── ⭐ SD-S5b (D-119, panel 16) — the received-target cue through the one helper ─────────────────────────────────────────────────

        /// <summary>The ellipse the cue's arrow rides on (mockup px half-axes, inside the window: its tip at x 70 on the left edge, as panel 16).</summary>
        public const float CueEdgeX = MockupWidth / 2f - 70f, CueEdgeY = 120f;

        private void DrawCue(IDebugDrawBuilder draw, EntityRepository w, Entity entity, in DesignatorState st)
        {
            if (!DevicePoses.TryWorld(w, entity, out var at, out var rotation)) return;
            if (!HudCue.Offset(at, rotation, st.Target.Point, out float bearing, out float elevation)) return;
            float range = Vector3.Distance(at, st.Target.Point);
            HudCue.Target(draw, P(MockupWidth / 2f, MockupHeight / 2f), new Vector2(CueEdgeX, CueEdgeY) * K, ViewCanvas.Height / MathF.Max(DesignatorBehavior.FovDegrees, 0.1f),
                          bearing, elevation, HudCue.OffsetText(bearing), HudCue.RangeText(range), _sender.Of(w, entity, st.Target.FromType, st.TargetFrom), K, L);
        }

        // ── the parts ────────────────────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>The window: the view darkened outside a rectangle inset <see cref="Inset"/> (past the canvas, so a letterboxed view is dark to its edges),
        /// and the four corner brackets (22 px, 1.6 wide, haloed).</summary>
        private static void DrawWindow(IDebugDrawBuilder draw)
        {
            const float Far = 4000f, In = Inset, Wm = MockupWidth, Hm = MockupHeight;
            draw.EmitRaw(ViewCanvas.Rect(P(-Far, -Far), P(Wm + 2 * Far, Far + In), Mask, default, 0f, L));          // above
            draw.EmitRaw(ViewCanvas.Rect(P(-Far, Hm - In), P(Wm + 2 * Far, Far + In), Mask, default, 0f, L));       // below
            draw.EmitRaw(ViewCanvas.Rect(P(-Far, In), P(Far + In, Hm - 2 * In), Mask, default, 0f, L));             // left
            draw.EmitRaw(ViewCanvas.Rect(P(Wm - In, In), P(Far + In, Hm - 2 * In), Mask, default, 0f, L));          // right
            Bracket(draw, In, In, 1, 1); Bracket(draw, Wm - In, In, -1, 1); Bracket(draw, In, Hm - In, 1, -1); Bracket(draw, Wm - In, Hm - In, -1, -1);
        }

        private static void Bracket(IDebugDrawBuilder draw, float a, float b, float dx, float dy)
        {
            HLine(draw, a, b, a + 22 * dx, b, White, 1.6f);
            HLine(draw, a, b, a, b + 22 * dy, White, 1.6f);
        }

        /// <summary>The reticle: four arms from 10 to 46 px off the centre and a 10-px box on it — red while lasing.</summary>
        private static void DrawReticle(IDebugDrawBuilder draw, Rgba32 c)
        {
            const float cx = MockupWidth / 2f, cy = MockupHeight / 2f;
            HLine(draw, cx - 46, cy, cx - 10, cy, c, 1.4f); HLine(draw, cx + 10, cy, cx + 46, cy, c, 1.4f);
            HLine(draw, cx, cy - 46, cx, cy - 10, c, 1.4f); HLine(draw, cx, cy + 10, cx, cy + 46, c, 1.4f);
            HLine(draw, cx - 5, cy - 5, cx + 5, cy - 5, c, 1.4f); HLine(draw, cx + 5, cy - 5, cx + 5, cy + 5, c, 1.4f);
            HLine(draw, cx + 5, cy + 5, cx - 5, cy + 5, c, 1.4f); HLine(draw, cx - 5, cy + 5, cx - 5, cy - 5, c, 1.4f);
        }

        /// <summary>The LASE lamp top-right (its centre at 590, 52) with its word to its left: lit — a glow over a dark halo disc and the red dot; off — a dark dot.</summary>
        private static void DrawLamp(IDebugDrawBuilder draw, bool lit, bool lasing)
        {
            var at = P(MockupWidth - 50, 52);
            if (lit)
            {
                draw.EmitRaw(ViewCanvas.Disc(at, 11 * K, new Rgba32(0, 0, 0, 115), default, 0f, L));
                draw.EmitRaw(ViewCanvas.Disc(at, 10 * K, new Rgba32(255, 59, 48, 71), default, 0f, L));
                draw.EmitRaw(ViewCanvas.Disc(at, 6 * K, Laser, Black, 1.5f * K, L));
            }
            else draw.EmitRaw(ViewCanvas.Disc(at, 6 * K, LampOff, Black, 1.5f * K, L));
            draw.EmitRaw(ViewCanvas.Text((MockupWidth - 62) * K, 57 * K, LaseWord, lasing ? Laser : Label, 12 * K, L, ViewCanvasAlign.Right, GizmoTextStyle.HaloBold));
        }

        /// <summary>The timer under CODE (x 40, y 104): its word, a 150-px bar filled to <paramref name="fraction"/>, the seconds left after it.</summary>
        private static void Timer(IDebugDrawBuilder draw, in HudText word, float fraction, Rgba32 c, in HudText seconds)
        {
            const float bx = 40, by = 104, bw = 150;
            draw.EmitRaw(ViewCanvas.Text(bx * K, (by + 10) * K, word, Label, 10.5f * K, L));
            draw.EmitRaw(ViewCanvas.Rect(P(bx + 52, by + 2), new Vector2(bw, 9) * K, BarFill, BarRim, 0.8f * K, L));
            float f = Math.Clamp(fraction, 0f, 1f);
            if (f > 0f) draw.EmitRaw(ViewCanvas.Rect(P(bx + 53, by + 3), new Vector2((bw - 2) * f, 7) * K, c, default, 0f, L));
            draw.EmitRaw(ViewCanvas.Text((bx + 58 + bw) * K, (by + 11) * K, seconds, c, 11 * K, L, style: GizmoTextStyle.HaloBold));
        }

        /// <summary>A line with the mockup's dark halo under it (2.2 px wider).</summary>
        private static void HLine(IDebugDrawBuilder draw, float x1, float y1, float x2, float y2, Rgba32 c, float w)
        {
            draw.EmitRaw(ViewCanvas.Line(P(x1, y1), P(x2, y2), Halo, (w + 2.2f) * K, L));
            draw.EmitRaw(ViewCanvas.Line(P(x1, y1), P(x2, y2), c, w * K, L));
        }

        // ── numbers: invariant (R-4221), on the stack, kept until they change (0 B) ───────────────────────────────────────────────────

        private HudText CodeText(ushort code)
        {
            if (_code.Set && _code.Key == code) return _code.Text;
            Span<char> s = stackalloc char[8];
            if (!((int)code).TryFormat(s, out int len, default, CultureInfo.InvariantCulture)) len = 0;
            _code = new Cached { Key = code, Set = true, Text = new HudText(s[..len]) };
            return _code.Text;
        }

        /// <summary>"42 s" — the whole seconds left, rounded UP (a timer that shows 0 s has ended).</summary>
        private HudText Seconds(double left)
        {
            int n = (int)Math.Ceiling(Math.Max(0d, left) - 1e-9);
            if (_timer.Set && _timer.Key == n) return _timer.Text;
            Span<char> s = stackalloc char[12];
            if (!n.TryFormat(s, out int len, default, CultureInfo.InvariantCulture)) len = 0;
            s[len++] = ' '; s[len++] = 's';
            _timer = new Cached { Key = n, Set = true, Text = new HudText(s[..len]) };
            return _timer.Text;
        }

        /// <summary>"1 240 m" — the one cue / range format (<see cref="HudCue.RangeText"/>: thousands grouped by a space).</summary>
        private HudText RangeOf(float metres)
        {
            int m = (int)MathF.Round(MathF.Max(0f, metres));
            if (_range.Set && _range.Key == m) return _range.Text;
            _range = new Cached { Key = m, Set = true, Text = HudCue.RangeText(m) };
            return _range.Text;
        }
    }
}
