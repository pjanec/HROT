using System;
using System.Globalization;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Devices;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Hrot.Map.Definitions.Behavior;
// the gizmo primitive's string (GizmoMap.Contracts), not Fdp.Core's
using HudText = Fdp.Toolkit.Diagnostics.Gizmos.FixedString32;

namespace Hrot.AI.Behaviors.Devices.Rangefinder
{
    /// <summary>
    /// ⭐⭐⭐ Simulated devices S1d-2 (docs/designs/devices/DESIGN_Simulated_Devices.md §3.6 D-21 / R6-5 / R7-3 / R2-10, mockup
    /// <c>mockups/devices-hud.svg</c> panels 2–5) — THE LASER RANGEFINDER's HUD: one stateless projector on the BRAIN (as <c>HillAttackGizmo</c>), run by
    /// <c>StatelessGizmoSystem</c>, which opens a <c>HudScope</c> for the device's network id around each draw (D-41) — so only a view BOUND to this device
    /// draws it. It reads the device's own <see cref="RangefinderState"/> (<c>UnitMemory.GetInView</c>) and draws on the view's HUD canvas
    /// (<see cref="CoordinateSpace.ViewCanvas"/>, 16:9, fitted into the view): the window with corner brackets, the gapped reticle, the name, MODE, POWER,
    /// the LASER lamp (blinking by frame count while ranging), LAST RANGE (<c>— m</c> / <c>452 m</c>, with PREVIEW when measured in pause), the menu line
    /// with its cursor and — while the menu is open — its popup with ✓ on the active value.
    /// <para>Keying (R6-5 / R7-3): the rule requires <see cref="DeviceInfo"/> + <see cref="PossessedBy"/> (an un-operated device draws nothing); the
    /// per-entity policy (this class — the HUD family's policy is fixed by the registry, <c>GizmoProjectorAttribute.Hud</c>) tests the device TYPE (its
    /// behaviour is the rangefinder's), the row is <c>Operated</c>, and this node holds <c>BehaviorState</c>'s authority (the brain that runs it). No G5
    /// inputs: re-run each frame. ⭐ 0 B per steady frame (R-2084): constant texts built once, the range formatted into a stack span.</para>
    /// <para>Layout: the mockup's own numbers (a 460 × 259 frame) scaled to the canvas (<see cref="K"/>), so the drawn HUD can be laid over the mockup.</para>
    /// </summary>
    [GizmoProjector(typeof(DeviceInfo), typeof(PossessedBy), Hud = true)]
    public sealed class RangefinderHudGizmo : IStatelessGizmo, IGizmoVisibilityPolicy
    {
        /// <summary>The mockup frame's width in its own pixels; canvas = mockup × <see cref="K"/>.</summary>
        public const float MockupWidth = 460f;

        /// <summary>Mockup pixels → canvas pixels.</summary>
        public const float K = ViewCanvas.Width / MockupWidth;

        private const byte L = DebugTraceLayers.DeviceHud;

        // ── the mockup's colours ─────────────────────────────────────────────────────────────────────────────────────────────────────
        public static readonly Rgba32 White = new(255, 255, 255), Gold = new(255, 215, 0), Laser = new(255, 59, 48);
        public static readonly Rgba32 Title = new(200, 200, 208), Label = new(160, 160, 170), Idle = new(184, 184, 192), Rim = new(138, 138, 150);
        public static readonly Rgba32 Pending = new(242, 192, 51), Check = new(91, 208, 106), Cursor = new(51, 105, 173);
        private static readonly Rgba32 Halo = new(0, 0, 0, 153), Mask = new(0, 0, 0, 199), LampOff = new(42, 42, 46), Black = new(0, 0, 0);

        private static readonly HudText Name = new("LASER RANGEFINDER"), ModeLabel = new("MODE"), PowerLabel = new("POWER"), LaserLabel = new("LASER"),
            LastRange = new("LAST RANGE"), NoRange = new("— m"), PreviewTag = new("PREVIEW"), Single = new("SINGLE"), Continuous = new("CONTINUOUS"),
            Low = new("LOW"), High = new("HIGH"), Exit = new("EXIT");

        private static readonly HudText[] MenuItems = { new("MODE"), new("POWER"), new("EXIT") };
        private static readonly float[] MenuWidths = { TextWidth("MODE", 11f) + 14f, TextWidth("POWER", 11f) + 14f, TextWidth("EXIT", 11f) + 14f };
        private static readonly HudText MenuWord = new("MENU");
        private static readonly float MenuWordWidth = TextWidth("MENU", 10.5f);
        private static readonly FixedString64 Behavior = new(BehaviorNames.DeviceRangefinder);

        /// <summary>Frames per half blink of the lamp while ranging.</summary>
        public const int BlinkFrames = 15;

        private static Vector2 P(float x, float y) => new(x * K, y * K);

        // ── keying ───────────────────────────────────────────────────────────────────────────────────────────────────────────────────
        public bool IsGloballyEnabled(ISimulationView view) => true;

        /// <summary>R7-3 — the device TYPE (its behaviour is the rangefinder's), operated, and this node holds its brain's authority.</summary>
        public bool IsEntityVisible(ISimulationView view, Entity entity) => DeviceHuds.IsVisible(view, entity, in Behavior);   // ⭐ S1d-3 — the one rule

        // ── the HUD ──────────────────────────────────────────────────────────────────────────────────────────────────────────────────
        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            var st = UnitMemory.GetInView<RangefinderState>(view, entity);
            long frame = view is EntityRepository w && w.HasSingleton<GlobalTime>() ? w.GetSingleton<GlobalTime>().FrameNumber : 0;

            DrawWindow(draw);
            DrawReticle(draw);

            // status, top-left
            draw.EmitRaw(ViewCanvas.Text(24 * K, 82 * K, Name, Title, 11 * K, L, style: GizmoTextStyle.HaloBold));
            draw.EmitRaw(ViewCanvas.Text(24 * K, 98 * K, ModeLabel, Label, 11 * K, L));
            draw.EmitRaw(ViewCanvas.Text(62 * K, 98 * K, st.Mode == RangefinderMode.Continuous ? Continuous : Single, White, 12 * K, L, style: GizmoTextStyle.HaloBold));
            draw.EmitRaw(ViewCanvas.Text(24 * K, 114 * K, PowerLabel, Label, 11 * K, L));
            draw.EmitRaw(ViewCanvas.Text(70 * K, 114 * K, st.Power == RangefinderPower.High ? High : Low, White, 12 * K, L, style: GizmoTextStyle.HaloBold));

            // the lamp, top-right: on while ranging, blinking by frame count (R6-4 — a visual blink is the projector's)
            bool lit = st.Ranging != 0 && (frame / BlinkFrames) % 2 == 0;
            DrawLamp(draw, lit, st.Ranging != 0);

            // the last range, bottom-centre
            float cx = MockupWidth / 2f;
            draw.EmitRaw(ViewCanvas.Text(cx * K, (259 - 56) * K, LastRange, Label, 10.5f * K, L, ViewCanvasAlign.Centre));
            draw.EmitRaw(ViewCanvas.Text(cx * K, (259 - 28) * K, st.HasRange != 0 ? RangeText(st.LastRange) : NoRange, Gold, 24 * K, L, ViewCanvasAlign.Centre, GizmoTextStyle.HaloBold));
            if (st.HasRange != 0 && st.RangePreview != 0)
            {
                float w0 = TextWidth("PREVIEW", 10f) + 12f;
                draw.EmitRaw(ViewCanvas.Rect(P(cx + 50, 259 - 52), new Vector2(w0, 15) * K, new Rgba32(0, 0, 0, 178), Pending, 1f * K, L));
                draw.EmitRaw(ViewCanvas.Text((cx + 50 + w0 / 2) * K, (259 - 41) * K, PreviewTag, Pending, 10 * K, L, ViewCanvasAlign.Centre, GizmoTextStyle.Plain));
            }

            // the menu line, bottom-right: the cursor on the open menu's item (MODE while closed); framed gold while open
            bool open = st.MenuOpen != 0;
            int cur = open ? (int)st.Menu : 0;
            float xx = MockupWidth - 14f;
            for (int i = MenuItems.Length - 1; i >= 0; i--)
            {
                float wi = MenuWidths[i];
                xx -= wi;
                bool sel = i == cur;
                draw.EmitRaw(ViewCanvas.Rect(P(xx, 259 - 36), new Vector2(wi, 18) * K, sel ? new Rgba32(51, 105, 173, 242) : new Rgba32(0, 0, 0, 140),
                                             sel && open ? Gold : default, 1.2f * K, L));
                draw.EmitRaw(ViewCanvas.Text((xx + wi / 2) * K, (259 - 23.5f) * K, MenuItems[i], sel ? White : Idle, 11 * K, L, ViewCanvasAlign.Centre,
                                             sel ? GizmoTextStyle.HaloBold : GizmoTextStyle.Halo));
                xx -= 5f;
            }

            if (open) DrawPopup(draw, in st);
        }

        /// <summary>The window: the view darkened outside a rectangle inset 10 (past the canvas, so a letterboxed view is dark to its edges), and the
        /// four corner brackets.</summary>
        private static void DrawWindow(IDebugDrawBuilder draw)
        {
            const float Far = 4000f, In = 10f, Wm = MockupWidth, Hm = 259f;   // mockup px
            draw.EmitRaw(ViewCanvas.Rect(P(-Far, -Far), P(Wm + 2 * Far, Far + In), Mask, default, 0f, L));                 // above
            draw.EmitRaw(ViewCanvas.Rect(P(-Far, Hm - In), P(Wm + 2 * Far, Far + In), Mask, default, 0f, L));              // below
            draw.EmitRaw(ViewCanvas.Rect(P(-Far, In), P(Far + In, Hm - 2 * In), Mask, default, 0f, L));                    // left
            draw.EmitRaw(ViewCanvas.Rect(P(Wm - In, In), P(Far + In, Hm - 2 * In), Mask, default, 0f, L));                 // right
            Bracket(draw, 14, 14, 1, 1); Bracket(draw, Wm - 14, 14, -1, 1); Bracket(draw, 14, Hm - 14, 1, -1); Bracket(draw, Wm - 14, Hm - 14, -1, -1);
        }

        private static void Bracket(IDebugDrawBuilder draw, float a, float b, float dx, float dy)
        {
            HLine(draw, a, b, a + 16 * dx, b, 1.4f);
            HLine(draw, a, b, a, b + 16 * dy, 1.4f);
        }

        /// <summary>A line with the mockup's dark halo under it (2.2 px wider, as the station's HudText draws over the scene).</summary>
        private static void HLine(IDebugDrawBuilder draw, float x1, float y1, float x2, float y2, float w = 1.2f)
        {
            draw.EmitRaw(ViewCanvas.Line(P(x1, y1), P(x2, y2), Halo, (w + 2.2f) * K, L));
            draw.EmitRaw(ViewCanvas.Line(P(x1, y1), P(x2, y2), White, w * K, L));
        }

        /// <summary>The gapped cross, the short ticks every 7.5 px and the centre dot.</summary>
        private static void DrawReticle(IDebugDrawBuilder draw)
        {
            const float cx = MockupWidth / 2f, cy = 259f / 2f;
            for (int s = -1; s <= 1; s += 2)
            {
                HLine(draw, cx + s * 6, cy, cx + s * 44, cy);
                HLine(draw, cx, cy + s * 6, cx, cy + s * 44);
            }
            for (int m = 1; m <= 5; m++)
            {
                float d = 6 + m * 7.5f;
                for (int s = -1; s <= 1; s += 2)
                {
                    HLine(draw, cx + s * d, cy - 2.5f, cx + s * d, cy + 2.5f, 1f);
                    HLine(draw, cx - 2.5f, cy + s * d, cx + 2.5f, cy + s * d, 1f);
                }
            }
            draw.EmitRaw(ViewCanvas.Disc(P(cx, cy), 1.6f * K, White, Black, 0.8f * K, L));
        }

        private static void DrawLamp(IDebugDrawBuilder draw, bool lit, bool ranging)
        {
            var at = P(MockupWidth - 24, 82);
            if (lit)
            {
                // ⭐ S1d-3 — the glow over a dark halo disc: the S1d-2 render showed a dark-red SQUARE behind the dot — a dark window of the SCENE seen
                //   through the 28 % glow, not the HUD; the halo (as every HUD text and line carries) makes the glow one round, scene-independent ring
                draw.EmitRaw(ViewCanvas.Disc(at, 9 * K, new Rgba32(0, 0, 0, 230), default, 0f, L));
                draw.EmitRaw(ViewCanvas.Disc(at, 9 * K, new Rgba32(255, 59, 48, 71), default, 0f, L));
                draw.EmitRaw(ViewCanvas.Disc(at, 5 * K, Laser, Black, 1f * K, L));
            }
            else draw.EmitRaw(ViewCanvas.Disc(at, 5 * K, LampOff, Rim, 1.2f * K, L));
            draw.EmitRaw(ViewCanvas.Text((MockupWidth - 36) * K, 86 * K, LaserLabel, ranging ? Laser : Label, 11 * K, L, ViewCanvasAlign.Right, GizmoTextStyle.HaloBold));
        }

        /// <summary>The open menu's popup under the status block: its title, the item's values with ✓ on the active one and the cursor on the value
        /// SELECT would apply (MODE / POWER step), or EXIT.</summary>
        private static void DrawPopup(IDebugDrawBuilder draw, in RangefinderState st)
        {
            const float x0 = 24, y0 = 124, w0 = 150;
            int item = (int)st.Menu;
            int rows = item == (int)RangefinderMenuItem.Exit ? 1 : 2;
            float h0 = 26 + rows * 22 + 4;
            draw.EmitRaw(ViewCanvas.Rect(P(x0, y0), new Vector2(w0, h0) * K, new Rgba32(0, 0, 0, 209), Rim, 1f * K, L));
            // "MENU ▸ MODE" — the gizmo font has no ▸, so the arrow is a small filled triangle between the two words
            float ax = x0 + 8 + MenuWordWidth + 3.5f, ay = y0 + 11.5f;
            draw.EmitRaw(ViewCanvas.Text((x0 + 8) * K, (y0 + 15) * K, MenuWord, Title, 10.5f * K, L, style: GizmoTextStyle.HaloBold));
            draw.EmitRaw(ViewCanvas.Triangle(P(ax, ay - 3.5f), P(ax + 5, ay), P(ax, ay + 3.5f), Title, L));
            draw.EmitRaw(ViewCanvas.Text((ax + 9) * K, (y0 + 15) * K, MenuItems[item], Title, 10.5f * K, L, style: GizmoTextStyle.HaloBold));
            draw.EmitRaw(ViewCanvas.Line(P(x0 + 6, y0 + 21), P(x0 + w0 - 6, y0 + 21), Rim, 0.8f * K, L));
            for (int i = 0; i < rows; i++)
            {
                float ry = y0 + 26 + i * 22;
                HudText name; bool active, cursor;
                if (item == (int)RangefinderMenuItem.Mode) { name = i == 0 ? Single : Continuous; active = (int)st.Mode == i; }
                else if (item == (int)RangefinderMenuItem.Power) { name = i == 0 ? Low : High; active = (int)st.Power == i; }
                else { name = Exit; active = false; }
                cursor = item == (int)RangefinderMenuItem.Exit || !active;   // the value SELECT would step to
                if (cursor) draw.EmitRaw(ViewCanvas.Rect(P(x0 + 4, ry), new Vector2(w0 - 8, 20) * K, Cursor, default, 0f, L));
                draw.EmitRaw(ViewCanvas.Text((x0 + 12) * K, (ry + 14.5f) * K, name, White, 12 * K, L, style: cursor ? GizmoTextStyle.HaloBold : GizmoTextStyle.Halo));
                if (active) CheckMark(draw, x0 + w0 - 24, ry + 4);
                if (cursor)
                {
                    float tx = x0 + w0 - 18, ty = ry + 10;   // "▸" in gold (the gizmo font has no ▸: a filled triangle)
                    draw.EmitRaw(ViewCanvas.Triangle(P(tx, ty - 4.5f), P(tx + 6, ty), P(tx, ty + 4.5f), Gold, L));
                }
            }
        }

        /// <summary>✓ in the mockup's green (FA check; the gizmo font has none): two strokes in a 12 px box.</summary>
        private static void CheckMark(IDebugDrawBuilder draw, float x, float y)
        {
            draw.EmitRaw(ViewCanvas.Line(P(x + 1.5f, y + 6.5f), P(x + 4.5f, y + 9.5f), Check, 2.2f * K, L));
            draw.EmitRaw(ViewCanvas.Line(P(x + 4.5f, y + 9.5f), P(x + 11f, y + 2.5f), Check, 2.2f * K, L));
        }

        /// <summary>"452 m" — formatted on the stack (0 B), invariant (R-4221: never a decimal comma).</summary>
        public static HudText RangeText(float range)
        {
            Span<char> text = stackalloc char[16];
            if (!((int)MathF.Round(range)).TryFormat(text, out int len, default, CultureInfo.InvariantCulture)) return default;
            text[len++] = ' '; text[len++] = 'm';
            return new HudText(text[..len]);
        }

        /// <summary>The mockup kit's Roboto advance estimate (<c>scripts/mockup_kit.py text_width</c>) — the menu chips are sized exactly as drawn there.</summary>
        public static float TextWidth(string s, float size)
        {
            float k = size / 13f, w = 0f;
            foreach (char ch in s)
                w += char.IsUpper(ch) ? 8.6f : ch == ' ' ? 3.6f : "il.,:;|'!".IndexOf(ch) >= 0 ? 4.0f : "mwMW".IndexOf(ch) >= 0 ? 9.6f : 6.9f;
            return w * k;
        }
    }
}
