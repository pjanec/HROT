using System;
using System.Numerics;

namespace GizmoMap.Contracts.Symbology
{
    /// <summary>
    /// Where <see cref="SymbolGeometry"/> draws: screen pixels around the symbol's centre (x right, y DOWN). Colours are packed RGBA (0xRRGGBBAA). One sink per
    /// back end — raylib for the map and the card, ImGui for the panels — so every place draws the SAME geometry.
    /// </summary>
    public interface ISymbolSink
    {
        /// <summary>A closed polygon, filled (alpha 0 = no fill) and outlined (width 0 = no outline).</summary>
        void Polygon(ReadOnlySpan<Vector2> points, uint fill, uint outline, float outlinePx);
        void Line(Vector2 a, Vector2 b, uint colour, float px);
        /// <summary>An ellipse (a circle when rx = ry), filled and / or outlined.</summary>
        void Ellipse(Vector2 centre, float rx, float ry, uint fill, uint outline, float outlinePx);
        /// <summary>A short text centred at <paramref name="centre"/>.</summary>
        void Text(Vector2 centre, string text, float sizePx, uint colour);
    }

    /// <summary>
    /// ⭐ <c>CE-8608</c> (DESIGN_Stochastic_Unit_UI §6 S-5; DESIGN_Unit_Symbols) — the APP-6 / 2525 land-unit symbol as VECTORS, composed from its parts: the FRAME by
    /// affiliation in the standard's pale fill with a black outline, the ROLE icon inside, the MOBILITY indicator under it, ONE secondary mark (a small white box,
    /// lower right), the ECHELON marks above (drawn as shapes, never glyphs — no font needed). Sized by the frame's height in pixels. ⭐ The ONE drawing: the map,
    /// the card and ImGui all call this through their sink. Allocation-free.
    /// </summary>
    public static class SymbolGeometry
    {
        public const uint FriendFill = 0x80E0FFFF, HostileFill = 0xFF8080FF, NeutralFill = 0xAAFFAAFF, UnknownFill = 0xFFFF80FF, Ink = 0x000000FF, White = 0xFFFFFFFF;

        public static uint FillOf(SymbolAffiliation a) => a switch
        {
            SymbolAffiliation.Friend => FriendFill, SymbolAffiliation.Hostile => HostileFill, SymbolAffiliation.Neutral => NeutralFill, _ => UnknownFill,
        };

        /// <summary>The symbol's extent around its centre for a frame of <paramref name="h"/> pixels: half width, the top (echelon marks included) and the bottom.</summary>
        public static (float HalfWidth, float Top, float Bottom) Extent(in SymbolCode s, float h)
        {
            var (bx, by, top, bottom) = Frame(s.Affiliation, h);
            float t = s.Echelon == SymbolEchelon.NoEchelon || h < 15f ? top : top - h * 0.45f;
            float b = s.Mobility == SymbolMobility.NoMobility ? bottom : bottom + h * 0.35f;
            return (MathF.Max(bx, h * 0.75f), t, b);
        }

        // the frame's inner half box (where the icon lives) and its top / bottom, by affiliation
        private static (float Bx, float By, float Top, float Bottom) Frame(SymbolAffiliation a, float h) => a switch
        {
            SymbolAffiliation.Friend => (h * 0.75f, h * 0.5f, -h * 0.5f, h * 0.5f),
            SymbolAffiliation.Hostile => (h * 0.4f, h * 0.4f, -h * 0.8f, h * 0.8f),
            SymbolAffiliation.Neutral => (h * 0.5f + 2f, h * 0.5f + 2f, -h * 0.5f - 2f, h * 0.5f + 2f),
            _ => (h * 0.36f, h * 0.36f, -h * 0.72f, h * 0.72f),
        };

        /// <summary>Draws <paramref name="s"/> around <paramref name="c"/> with a frame <paramref name="h"/> pixels high.</summary>
        public static void Build(in SymbolCode s, Vector2 c, float h, ISymbolSink sink)
        {
            float w = MathF.Max(1f, h * 0.07f);
            uint fill = FillOf(s.Affiliation);
            Span<Vector2> p = stackalloc Vector2[4];
            switch (s.Affiliation)
            {
                case SymbolAffiliation.Friend:
                    p[0] = c + new Vector2(-h * 0.75f, -h * 0.5f); p[1] = c + new Vector2(h * 0.75f, -h * 0.5f);
                    p[2] = c + new Vector2(h * 0.75f, h * 0.5f); p[3] = c + new Vector2(-h * 0.75f, h * 0.5f);
                    sink.Polygon(p, fill, Ink, w); break;
                case SymbolAffiliation.Hostile:
                    float r = h * 0.8f;
                    p[0] = c + new Vector2(0, -r); p[1] = c + new Vector2(r, 0); p[2] = c + new Vector2(0, r); p[3] = c + new Vector2(-r, 0);
                    sink.Polygon(p, fill, Ink, w); break;
                case SymbolAffiliation.Neutral:
                    float q = h * 0.5f + 2f;
                    p[0] = c + new Vector2(-q, -q); p[1] = c + new Vector2(q, -q); p[2] = c + new Vector2(q, q); p[3] = c + new Vector2(-q, q);
                    sink.Polygon(p, fill, Ink, w); break;
                default:
                    float u = h * 0.36f;
                    sink.Ellipse(c + new Vector2(0, -u), u, u, fill, Ink, w); sink.Ellipse(c + new Vector2(u, 0), u, u, fill, Ink, w);
                    sink.Ellipse(c + new Vector2(0, u), u, u, fill, Ink, w); sink.Ellipse(c + new Vector2(-u, 0), u, u, fill, Ink, w);
                    sink.Ellipse(c, u * 1.3f, u * 1.3f, fill, 0, 0f);
                    break;
            }
            var (bx, by, top, bottom) = Frame(s.Affiliation, h);
            Icon(s.Role, c, bx, by, w, sink);
            if (s.Mobility != SymbolMobility.NoMobility) Mobility(s.Mobility, c, h, bottom, w, sink);
            if (s.Secondary != SymbolRole.NoRole) Secondary(s.Secondary, c, bx, by, h, sink);
            if (s.Echelon != SymbolEchelon.NoEchelon && h >= 15f) Echelon(s.Echelon, c, top, h, w, sink);
        }

        private static void Icon(SymbolRole role, Vector2 c, float bx, float by, float w, ISymbolSink sink)
        {
            bool cross = role is SymbolRole.Infantry or SymbolRole.Mechanized or SymbolRole.Motorized;
            if (cross)
            {
                sink.Line(c + new Vector2(-bx, -by), c + new Vector2(bx, by), Ink, w);
                sink.Line(c + new Vector2(-bx, by), c + new Vector2(bx, -by), Ink, w);
            }
            switch (role)
            {
                case SymbolRole.Mechanized:
                case SymbolRole.Armour:
                    sink.Ellipse(c, bx * 0.62f, by * 0.55f, 0, Ink, w); break;
                case SymbolRole.Motorized:
                    sink.Line(c + new Vector2(0, -by), c + new Vector2(0, by), Ink, w); break;
                case SymbolRole.Reconnaissance:
                    sink.Line(c + new Vector2(-bx, by), c + new Vector2(bx, -by), Ink, w); break;
                case SymbolRole.Artillery:
                    sink.Ellipse(c, by * 0.38f, by * 0.38f, Ink, 0, 0f); break;
                case SymbolRole.Mortar:
                    sink.Ellipse(c + new Vector2(0, by * 0.35f), by * 0.25f, by * 0.25f, 0, Ink, w);
                    sink.Line(c + new Vector2(0, by * 0.1f), c + new Vector2(0, -by * 0.7f), Ink, w);
                    sink.Line(c + new Vector2(0, -by * 0.7f), c + new Vector2(-by * 0.22f, -by * 0.4f), Ink, w);
                    sink.Line(c + new Vector2(0, -by * 0.7f), c + new Vector2(by * 0.22f, -by * 0.4f), Ink, w); break;
                case SymbolRole.AntiArmour:
                    sink.Line(c + new Vector2(-bx, by), c + new Vector2(0, -by), Ink, w);
                    sink.Line(c + new Vector2(0, -by), c + new Vector2(bx, by), Ink, w); break;
                case SymbolRole.Support:
                    sink.Line(c + new Vector2(-bx, 0), c + new Vector2(bx, 0), Ink, w); break;
            }
        }

        private static void Mobility(SymbolMobility m, Vector2 c, float h, float bottom, float w, ISymbolSink sink)
        {
            float y = c.Y + bottom + h * 0.2f;
            if (m == SymbolMobility.Tracked)
            {
                Span<Vector2> band = stackalloc Vector2[4];
                float hw = h * 0.6f, hh = h * 0.12f;
                band[0] = new Vector2(c.X - hw, y - hh); band[1] = new Vector2(c.X + hw, y - hh); band[2] = new Vector2(c.X + hw, y + hh); band[3] = new Vector2(c.X - hw, y + hh);
                sink.Polygon(band, 0, Ink, w);
            }
            else
            {
                float r = h * 0.11f;
                for (int k = -1; k <= 1; k++) sink.Ellipse(new Vector2(c.X + k * h * 0.4f, y), r, r, 0, Ink, w);
            }
        }

        private static void Secondary(SymbolRole r, Vector2 c, float bx, float by, float h, ISymbolSink sink)
        {
            string mark = SymbolCode.MarkOf(r);
            if (mark.Length == 0) return;
            float bw = h * (0.3f + 0.28f * mark.Length), bh = h * 0.55f;
            var o = c + new Vector2(bx + h * 0.08f, by * 0.2f);
            Span<Vector2> box = stackalloc Vector2[4];
            box[0] = o; box[1] = o + new Vector2(bw, 0); box[2] = o + new Vector2(bw, bh); box[3] = o + new Vector2(0, bh);
            sink.Polygon(box, White, Ink, MathF.Max(1f, h * 0.05f));
            sink.Text(o + new Vector2(bw * 0.5f, bh * 0.5f), mark, h * 0.45f, Ink);
        }

        private static void Echelon(SymbolEchelon e, Vector2 c, float top, float h, float w, ISymbolSink sink)
        {
            float y = c.Y + top - h * 0.22f, r = MathF.Max(1.2f, h * 0.08f), gap = h * 0.26f;
            int dots = e == SymbolEchelon.Squad ? 1 : e == SymbolEchelon.Section ? 2 : e == SymbolEchelon.Platoon ? 3 : 0;
            if (dots > 0)
            {
                for (int i = 0; i < dots; i++) sink.Ellipse(new Vector2(c.X + (i - (dots - 1) * 0.5f) * gap, y), r, r, Ink, 0, 0f);
                return;
            }
            float bar = h * 0.35f;
            int bars = e == SymbolEchelon.Company ? 1 : e == SymbolEchelon.Battalion ? 2 : e == SymbolEchelon.Regiment ? 3 : 0;
            if (bars > 0)
            {
                for (int i = 0; i < bars; i++)
                {
                    float x = c.X + (i - (bars - 1) * 0.5f) * gap * 0.8f;
                    sink.Line(new Vector2(x, y - bar * 0.5f), new Vector2(x, y + bar * 0.5f), Ink, w * 1.2f);
                }
                return;
            }
            int crosses = e == SymbolEchelon.Brigade ? 1 : e == SymbolEchelon.Division ? 2 : 0;
            for (int i = 0; i < crosses; i++)
            {
                float x = c.X + (i - (crosses - 1) * 0.5f) * bar * 1.1f, s = bar * 0.45f;
                sink.Line(new Vector2(x - s, y - s), new Vector2(x + s, y + s), Ink, w * 1.2f);
                sink.Line(new Vector2(x - s, y + s), new Vector2(x + s, y - s), Ink, w * 1.2f);
            }
            if (e == SymbolEchelon.Team)
            {
                sink.Ellipse(new Vector2(c.X, y), bar * 0.4f, bar * 0.4f, 0, Ink, w);
                sink.Line(new Vector2(c.X - bar * 0.55f, y + bar * 0.55f), new Vector2(c.X + bar * 0.55f, y - bar * 0.55f), Ink, w);
            }
        }
    }
}
