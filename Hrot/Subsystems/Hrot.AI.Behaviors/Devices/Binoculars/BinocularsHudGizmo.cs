using System;
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

namespace Hrot.AI.Behaviors.Devices.Binoculars
{
    /// <summary>
    /// ⭐⭐⭐ Simulated devices S1d-3 (docs/designs/devices/DESIGN_Simulated_Devices.md §3.10 D-42 / D-75, §3.6 D-21 / R6-5 / R7-3, mockup
    /// <c>mockups/devices-hud.svg</c> panel 1) — THE BINOCULARS' HUD: one stateless projector on the BRAIN (as the rangefinder's), run by
    /// <c>StatelessGizmoSystem</c> inside a <c>HudScope</c> for the device's network id, so only a view BOUND to it draws it. It replaces the station's
    /// CE-1219 overlay (D-42): the TWO-EYEPIECE frame (the view darkened outside two overlapping circles), the MIL RETICLE true to the zoom step's field of
    /// view (5-mil ticks, a number every 10 — the scale moves with the zoom: <see cref="PxPerMil"/>), the ZOOM STEP list with the current one lit, the
    /// binoculars mark and the name. Reads <see cref="BinocularsState"/> (<c>UnitMemory.GetInView</c>); keyed and filtered as every device HUD
    /// (<see cref="DeviceHuds.IsVisible"/>). ⭐ 0 B per steady frame: every text built once.
    /// <para>Layout: the mockup's own numbers (a 460 × 259 frame) scaled to the canvas (<see cref="K"/>), as the rangefinder's.</para>
    /// </summary>
    [GizmoProjector(typeof(DeviceInfo), typeof(PossessedBy), Hud = true)]
    public sealed class BinocularsHudGizmo : IStatelessGizmo, IGizmoVisibilityPolicy
    {
        /// <summary>The mockup frame in its own pixels; canvas = mockup × <see cref="K"/>.</summary>
        public const float MockupWidth = 460f, MockupHeight = 259f;

        /// <summary>Mockup pixels → canvas pixels.</summary>
        public const float K = ViewCanvas.Width / MockupWidth;

        /// <summary>The eyepieces: radius (mockup px) and their centres' offset either side of the middle (× radius) — CE-1219's own proportions.</summary>
        public const float EyeRadius = MockupHeight * 0.42f, EyeOffset = 0.55f;

        /// <summary>The vertical strips the frame's dark area is cut into (each eyepiece edge is a polyline through them) — spaced by the cosine, so the
        /// steep outer edges get the narrow strips (no flat cut where the circle turns vertical); an even count puts one edge on the cusp.</summary>
        public const int MaskStrips = 56;

        /// <summary>⚠ As built (S1d-3, the mockup moved with it): the name sits in the cusp between the eyepieces (baseline 227, not the rim's 247 — the
        /// station's unit lines, unchanged context, cover the bottom rim), and the zoom list starts at 132 (not 108 — under the station's VIEW label,
        /// which panel 6 puts under the lamp, y 94…114).</summary>
        public const float NameBaseline = MockupHeight - 32f, StepsBaseline = 132f;

        private const byte L = DebugTraceLayers.DeviceHud;

        public static readonly Rgba32 White = new(255, 255, 255), Gold = new(255, 215, 0), Teal = new(62, 198, 176);
        public static readonly Rgba32 Name = new(200, 200, 208), Number = new(224, 224, 232), StepOff = new(128, 128, 136);
        private static readonly Rgba32 Halo = new(0, 0, 0, 153), Mask = new(0, 0, 0, 237);

        private static readonly HudText NameText = new("BINOCULARS");
        private static readonly HudText[] StepTexts = BuildSteps();
        private static readonly HudText[] Numbers = { new("10"), new("20"), new("30"), new("40") };
        private static readonly FixedString64 Behavior = new(BehaviorNames.DeviceBinoculars);

        private static HudText[] BuildSteps()
        {
            var steps = BinocularsZoom.Steps;
            var texts = new HudText[steps.Length];
            for (int i = 0; i < steps.Length; i++) texts[i] = new HudText(((int)steps[i]).ToString(System.Globalization.CultureInfo.InvariantCulture) + "×");
            return texts;
        }

        private static Vector2 P(float x, float y) => new(x * K, y * K);

        /// <summary>One milliradian in MOCKUP px at <paramref name="fovDegrees"/> over the frame's height (the mockup: 1.48 px at 10°).</summary>
        public static float PxPerMil(float fovDegrees) => MockupHeight / (fovDegrees * MathF.PI / 180f * 1000f);

        // ── keying ───────────────────────────────────────────────────────────────────────────────────────────────────────────────────
        public bool IsGloballyEnabled(ISimulationView view) => true;

        public bool IsEntityVisible(ISimulationView view, Entity entity) => DeviceHuds.IsVisible(view, entity, in Behavior);

        // ── the HUD ──────────────────────────────────────────────────────────────────────────────────────────────────────────────────
        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            var st = UnitMemory.GetInView<BinocularsState>(view, entity);
            int step = st.Ready != 0 ? st.Zoom : BinocularsZoom.DefaultStep;
            DrawFrame(draw);
            DrawReticle(draw, BinocularsZoom.FovDegrees(step));
            const float cx = MockupWidth / 2f;
            draw.EmitRaw(ViewCanvas.Text(cx * K, NameBaseline * K, NameText, Name, 11.5f * K, L, ViewCanvasAlign.Centre, GizmoTextStyle.HaloBold));
            for (int i = 0; i < StepTexts.Length; i++)   // the zoom step list, the right rim: the current one lit
            {
                bool on = i == step;
                draw.EmitRaw(ViewCanvas.Text((MockupWidth - 14) * K, (StepsBaseline + i * 18) * K, StepTexts[i], on ? Gold : StepOff, (on ? 14f : 11f) * K, L,
                                             ViewCanvasAlign.Right, on ? GizmoTextStyle.HaloBold : GizmoTextStyle.Halo));
            }
            DrawMark(draw, MockupWidth - 36, 78);
            if (st.HasTarget != 0 && view is EntityRepository w) DrawCue(draw, w, entity, in st, BinocularsZoom.FovDegrees(step));
        }

        // ── ⭐⭐ SD-S5b (§3.13d D-119, mockup devices-hud-link.svg panels 25 / 26) — the RECEIVED-TARGET cue, through the one helper (HudCue) ───────────────

        /// <summary>The ellipse the cue's arrow rides on (mockup px half-axes, inside the eyepieces: across to the outer rims, up and down to the cusp).</summary>
        public const float CueEdgeX = 140f, CueEdgeY = 76f;

        private long _senderType = long.MinValue;
        private bool _senderLinked;
        private HudText _senderText;

        /// <summary>"from LASER RANGEFINDER (linked)" — the sender's TKB name, built once per (type, linked) (allocates then, never per frame).</summary>
        private HudText SenderText(EntityRepository w, Entity entity, in BinocularsState st)
        {
            bool linked = DeviceMessages.IsLinked(w, entity, st.TargetFrom);
            if (_senderType == st.Target.FromType && _senderLinked == linked) return _senderText;
            string name = "DEVICE";
            if (w.HasSingletonManaged<Fdp.Interfaces.ITkbDatabase>() && w.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>() is { } db
                && db.TryGetByType(st.Target.FromType, out var t) && t != null && !string.IsNullOrEmpty(t.Name)) name = t.Name.ToUpperInvariant();
            _senderText = new HudText("from " + name + (linked ? " (linked)" : ""));
            _senderType = st.Target.FromType; _senderLinked = linked;
            return _senderText;
        }

        private void DrawCue(IDebugDrawBuilder draw, EntityRepository w, Entity entity, in BinocularsState st, float fovDegrees)
        {
            if (!DevicePoses.TryWorld(w, entity, out var at, out var rotation)) return;
            if (!HudCue.Offset(at, rotation, st.Target.Point, out float bearing, out float elevation)) return;
            float range = Vector3.Distance(at, st.Target.Point);
            HudCue.Target(draw, P(MockupWidth / 2f, MockupHeight / 2f), new Vector2(CueEdgeX, CueEdgeY) * K, ViewCanvas.Height / MathF.Max(fovDegrees, 0.1f),
                          bearing, elevation, HudCue.OffsetText(bearing), HudCue.RangeText(range), SenderText(w, entity, in st), K, L);
        }

        /// <summary>The eyepiece frame: everything outside the UNION of the two circles darkened — past the canvas too, so a letterboxed view is dark to
        /// its edges. Outside the circles' span two big boxes; across it, per strip, the area above and below the union's edge (two triangles each).</summary>
        private static void DrawFrame(IDebugDrawBuilder draw)
        {
            const float Far = 4000f, cy = MockupHeight / 2f;
            float r = EyeRadius, cl = MockupWidth / 2f - r * EyeOffset, cr = MockupWidth / 2f + r * EyeOffset;
            float x0 = cl - r, x1 = cr + r;
            draw.EmitRaw(ViewCanvas.Rect(P(-Far, -Far), P(x0 + Far, 2 * Far), Mask, default, 0f, L));                   // left of the span
            draw.EmitRaw(ViewCanvas.Rect(P(x1, -Far), P(Far, 2 * Far), Mask, default, 0f, L));                          // right of it
            float prevX = x0, prevH = 0f;
            for (int i = 1; i <= MaskStrips; i++)
            {
                float x = (x0 + x1) * 0.5f - (x1 - x0) * 0.5f * MathF.Cos(MathF.PI * i / MaskStrips);
                float h = MathF.Max(Half(x - cl, r), Half(x - cr, r));
                // above: (prevX, -Far) (x, -Far) (x, cy - h) (prevX, cy - prevH)
                draw.EmitRaw(ViewCanvas.Triangle(P(prevX, -Far), P(x, -Far), P(x, cy - h), Mask, L));
                draw.EmitRaw(ViewCanvas.Triangle(P(prevX, -Far), P(x, cy - h), P(prevX, cy - prevH), Mask, L));
                // below
                draw.EmitRaw(ViewCanvas.Triangle(P(prevX, cy + prevH), P(x, cy + h), P(x, Far), Mask, L));
                draw.EmitRaw(ViewCanvas.Triangle(P(prevX, cy + prevH), P(x, Far), P(prevX, Far), Mask, L));
                prevX = x; prevH = h;
            }
        }

        private static float Half(float dx, float r) => MathF.Abs(dx) >= r ? 0f : MathF.Sqrt(r * r - dx * dx);

        /// <summary>A line with the mockup's dark halo under it (2.2 px wider).</summary>
        private static void HLine(IDebugDrawBuilder draw, float x1, float y1, float x2, float y2, float w = 1.2f)
        {
            draw.EmitRaw(ViewCanvas.Line(P(x1, y1), P(x2, y2), Halo, (w + 2.2f) * K, L));
            draw.EmitRaw(ViewCanvas.Line(P(x1, y1), P(x2, y2), White, w * K, L));
        }

        /// <summary>The mil reticle: a cross with a gap out to 58 mil, a tick every 5 mil (longer every 10), the numbers 10…40 down the vertical and 20 /
        /// 40 along the horizontal (right), and the centre dot — at the step's scale.</summary>
        private static void DrawReticle(IDebugDrawBuilder draw, float fovDegrees)
        {
            const float cx = MockupWidth / 2f, cy = MockupHeight / 2f;
            float px = PxPerMil(fovDegrees);
            // the cross reaches 58 mil, but never past the eyepieces: across, their outer edge; up and down, the cusp where they meet
            float across = MathF.Min(58f * px, EyeRadius * (1f + EyeOffset) - 6f);
            float upDown = MathF.Min(58f * px, EyeRadius * MathF.Sqrt(1f - EyeOffset * EyeOffset) - 4f);
            for (int s = -1; s <= 1; s += 2)
            {
                HLine(draw, cx + s * 6, cy, cx + s * across, cy);
                HLine(draw, cx, cy + s * 6, cx, cy + s * upDown);
            }
            for (int m = 5; m <= 50; m += 5)
            {
                float d = m * px;
                if (d > across) break;
                bool big = m % 10 == 0;
                float half = big ? 4f : 2.5f;
                for (int s = -1; s <= 1; s += 2)
                {
                    HLine(draw, cx + s * d, cy - half, cx + s * d, cy + half, 1f);
                    if (d <= upDown) HLine(draw, cx - half, cy + s * d, cx + half, cy + s * d, 1f);
                }
                if (big && m <= 40 && d <= upDown)
                {
                    var n = Numbers[m / 10 - 1];
                    draw.EmitRaw(ViewCanvas.Text((cx + 8) * K, (cy + d + 3) * K, n, Number, 8.5f * K, L, style: GizmoTextStyle.Halo));
                    if (m % 20 == 0) draw.EmitRaw(ViewCanvas.Text((cx + d) * K, (cy - 7) * K, n, Number, 8.5f * K, L, ViewCanvasAlign.Centre, GizmoTextStyle.Halo));
                }
            }
            draw.EmitRaw(ViewCanvas.Disc(P(cx, cy), 1.5f * K, White, default, 0f, L));
        }

        /// <summary>The binoculars mark in teal (the mockup's FA binoculars at its top-left (x, y), 15 px; the gizmo font has no icon glyphs, so it is
        /// drawn: two barrels and the bridge).</summary>
        private static void DrawMark(IDebugDrawBuilder draw, float x, float y)
        {
            draw.EmitRaw(ViewCanvas.Rect(P(x + 1.5f, y + 2f), new Vector2(4.5f, 7f) * K, Teal, default, 0f, L));    // the eyepieces
            draw.EmitRaw(ViewCanvas.Rect(P(x + 9f, y + 2f), new Vector2(4.5f, 7f) * K, Teal, default, 0f, L));
            draw.EmitRaw(ViewCanvas.Rect(P(x + 5.5f, y + 5f), new Vector2(4f, 3f) * K, Teal, default, 0f, L));      // the bridge
            draw.EmitRaw(ViewCanvas.Disc(P(x + 3.6f, y + 11.5f), 3.2f * K, Teal, default, 0f, L));                  // the objectives
            draw.EmitRaw(ViewCanvas.Disc(P(x + 11.4f, y + 11.5f), 3.2f * K, Teal, default, 0f, L));
        }
    }
}
