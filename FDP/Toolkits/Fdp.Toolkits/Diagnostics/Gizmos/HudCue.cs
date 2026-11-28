using System;
using System.Globalization;
using System.Numerics;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>
    /// ⭐⭐ Simulated devices SD-S5b (docs/designs/devices/DESIGN_Simulated_Devices.md §3.13d D-119, mockups <c>devices-hud-link.svg</c> panels 25–26 and
    /// <c>devices-hud-designator.svg</c> panel 16) — THE received-target CUE of every device HUD: where a point a LINKED device sent lies from this device's
    /// boresight. Off the view: an amber arrow on the window's edge (an ellipse inside the window) pointing toward it; in the view: an amber diamond on it. Beside
    /// either, three lines — <c>TARGET</c> with the side's small triangle and the bearing offset, the range, and who sent it. Drawn on the view HUD canvas
    /// (<see cref="ViewCanvas"/>) by the device type's own projector, inside its HUD scope; ⭐ ONE helper for the binoculars (SD-S5b) and the designator (SD-S5c) —
    /// ruling 9. 0 B: the caller holds its texts (<see cref="OffsetText"/> / <see cref="RangeText"/> format into the stack).
    /// </summary>
    public static class HudCue
    {
        /// <summary>The cue's amber (the mockup kit's <c>C.AMBER</c>) and the sender line's grey.</summary>
        public static readonly Rgba32 Amber = new(242, 192, 51), Sender = new(160, 160, 170);
        private static readonly Rgba32 Outline = new(0, 0, 0);

        /// <summary>The word of the first line.</summary>
        public static readonly FixedString32 Word = new("TARGET");

        /// <summary>
        /// Where <paramref name="point"/> lies from a device at <paramref name="origin"/> pointing <paramref name="rotation"/> (its world pose — <c>DevicePoses.TryWorld</c>;
        /// forward +X, left +Y, up +Z): the bearing offset (degrees, + = LEFT of the boresight) and the elevation offset (degrees, + = up). False when the point is
        /// at the device.
        /// </summary>
        public static bool Offset(Vector3 origin, Quaternion rotation, Vector3 point, out float bearingDeg, out float elevationDeg)
        {
            bearingDeg = elevationDeg = 0f;
            var d = point - origin;
            if (d.LengthSquared() < 1e-6f) return false;
            var local = Vector3.Transform(d, Quaternion.Inverse(rotation == default ? Quaternion.Identity : Quaternion.Normalize(rotation)));
            bearingDeg = MathF.Atan2(local.Y, local.X) * (180f / MathF.PI);
            elevationDeg = MathF.Atan2(local.Z, MathF.Sqrt(local.X * local.X + local.Y * local.Y)) * (180f / MathF.PI);
            return true;
        }

        /// <summary>
        /// Draws the cue for a target at (<paramref name="bearingDeg"/>, <paramref name="elevationDeg"/>) from the boresight, the view centred at <paramref name="centre"/>
        /// (canvas px) with <paramref name="pxPerDegree"/> (canvas px per degree of the view's field); <paramref name="edge"/> is the half-axes of the ellipse the arrow
        /// rides on (inside the device's window), <paramref name="u"/> the HUD's mockup → canvas scale. Returns true when the target is INSIDE the ellipse (the diamond
        /// drawn), false when the arrow is.
        /// </summary>
        public static bool Target(IDebugDrawBuilder draw, Vector2 centre, Vector2 edge, float pxPerDegree, float bearingDeg, float elevationDeg,
                                  in FixedString32 offsetText, in FixedString32 rangeText, in FixedString32 senderText, float u, byte layer)
        {
            // screen: x right = −bearing, y down = −elevation
            var off = new Vector2(-bearingDeg, -elevationDeg) * pxPerDegree;
            float ex = MathF.Max(edge.X, 1f), ey = MathF.Max(edge.Y, 1f);
            float inside = (off.X / ex) * (off.X / ex) + (off.Y / ey) * (off.Y / ey);
            bool inView = inside <= 1f && MathF.Abs(bearingDeg) < 90f;
            Vector2 at;
            float side;
            if (inView)
            {
                at = centre + off;
                float r = 7f * u;   // a diamond on the point
                draw.EmitRaw(ViewCanvas.Triangle(at + new Vector2(0, -r), at + new Vector2(r, 0), at + new Vector2(0, r), Amber, layer));
                draw.EmitRaw(ViewCanvas.Triangle(at + new Vector2(0, -r), at + new Vector2(0, r), at + new Vector2(-r, 0), Amber, layer));
                draw.EmitRaw(ViewCanvas.Disc(at, 1.6f * u, Outline, default, 0f, layer));
                side = 1f;   // the text to the right of the diamond
                at += new Vector2(12f * u, -4f * u);
            }
            else
            {
                var dir = off.LengthSquared() > 1e-6f ? Vector2.Normalize(off) : new Vector2(bearingDeg >= 0f ? -1f : 1f, 0f);
                if (MathF.Abs(bearingDeg) >= 90f && MathF.Abs(dir.X) < 0.2f) dir = new Vector2(bearingDeg >= 0f ? -1f : 1f, 0f);   // behind: toward the nearer side
                // the arrow's tip on the ellipse, its base 16 px inward (the mockup's 16 × 18 triangle)
                float t = 1f / MathF.Sqrt((dir.X / ex) * (dir.X / ex) + (dir.Y / ey) * (dir.Y / ey));
                var tip = centre + dir * t;
                var normal = new Vector2(-dir.Y, dir.X);
                var b = tip - dir * (16f * u);
                draw.EmitRaw(ViewCanvas.Triangle(tip + dir * (1.2f * u), b + normal * (10.2f * u) - dir * (0.6f * u), b - normal * (10.2f * u) - dir * (0.6f * u), Outline, layer));
                draw.EmitRaw(ViewCanvas.Triangle(tip, b + normal * (9f * u), b - normal * (9f * u), Amber, layer));
                side = dir.X <= 0.35f ? 1f : -1f;   // the text on the window's inner side
                at = b + new Vector2(side * 6f * u, dir.Y > 0.6f ? -40f * u : 22f * u);
            }
            var align = side > 0f ? ViewCanvasAlign.Left : ViewCanvasAlign.Right;
            float wordW = 47f * u;   // "TARGET" at 11 px bold (the mockup kit's advance)
            // line 1: TARGET ◂ 14°  — the font has no ◂ / ▸, so the side's triangle is drawn between the word and the angle
            float x0 = side > 0f ? at.X : at.X - (wordW + 14f * u + 26f * u);
            draw.EmitRaw(ViewCanvas.Text(x0, at.Y, Word, Amber, 11f * u, layer, ViewCanvasAlign.Left, GizmoTextStyle.HaloBold));
            float tx = x0 + wordW + 3f * u, ty = at.Y - 4f * u;
            if (bearingDeg >= 0f) draw.EmitRaw(ViewCanvas.Triangle(new Vector2(tx + 7f * u, ty - 4f * u), new Vector2(tx + 7f * u, ty + 4f * u), new Vector2(tx, ty), Amber, layer));
            else draw.EmitRaw(ViewCanvas.Triangle(new Vector2(tx, ty - 4f * u), new Vector2(tx + 7f * u, ty), new Vector2(tx, ty + 4f * u), Amber, layer));
            draw.EmitRaw(ViewCanvas.Text(tx + 11f * u, at.Y, offsetText, Amber, 11f * u, layer, ViewCanvasAlign.Left, GizmoTextStyle.HaloBold));
            // line 2: the range · line 3: the sender
            draw.EmitRaw(ViewCanvas.Text(at.X, at.Y + 14f * u, rangeText, Amber, 11f * u, layer, align, GizmoTextStyle.HaloBold));
            draw.EmitRaw(ViewCanvas.Text(at.X, at.Y + 28f * u, senderText, Sender, 9.5f * u, layer, align, GizmoTextStyle.Halo));
            return inView;
        }

        /// <summary>"14°" — the bearing offset's size, whole degrees (the side is the triangle), invariant (R-4221). 0 B.</summary>
        public static FixedString32 OffsetText(float bearingDeg)
        {
            Span<char> text = stackalloc char[8];
            int deg = (int)MathF.Round(MathF.Abs(bearingDeg));
            if (!deg.TryFormat(text, out int len, default, CultureInfo.InvariantCulture)) return default;
            text[len++] = '°';
            return new FixedString32(text[..len]);
        }

        /// <summary>"1 240 m" — whole metres, thousands grouped by a space (never a comma — R-4221), as the designator's mockup writes it. 0 B.</summary>
        public static FixedString32 RangeText(float metres)
        {
            Span<char> text = stackalloc char[16];
            int m = (int)MathF.Round(MathF.Max(0f, metres));
            int len = 0;
            if (m >= 1000)
            {
                if (!(m / 1000).TryFormat(text, out len, default, CultureInfo.InvariantCulture)) return default;
                text[len++] = ' ';
                int rest = m % 1000;
                text[len++] = (char)('0' + rest / 100); text[len++] = (char)('0' + rest / 10 % 10); text[len++] = (char)('0' + rest % 10);
            }
            else if (!m.TryFormat(text, out len, default, CultureInfo.InvariantCulture)) return default;
            text[len++] = ' '; text[len++] = 'm';
            return new FixedString32(text[..len]);
        }
    }
}
