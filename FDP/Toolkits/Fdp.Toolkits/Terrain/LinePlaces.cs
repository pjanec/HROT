using System;
using System.Collections.Generic;
using System.Numerics;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ CE-5007 (S-2, docs/DESIGN_Terrain_Editor_UX.md §5 TE-4 / TE-13) — the ONE placement routine of the expander: places along a POLYLINE (the recipe <c>group</c> element's even <c>line</c>, the row feature's spaced trees) and
    /// the seeded rejection sampling inside a POLYGON (the scatter's items, the group feature's trees). No second algorithm: the recipe group element, <c>Scatter</c> and the Row / Group kinds all call these.
    /// </summary>
    public static partial class FeatureExpander
    {
        /// <summary>
        /// <paramref name="count"/> places spread EVENLY along the polyline <paramref name="pts"/> — place <c>i</c> at <c>(i + 0.5) / count</c> of its length (a first and last half a gap from the ends) — each with the local direction in degrees.
        /// On ONE segment the position is exactly <c>a + (b − a) · ((i + 0.5) / count)</c> (the recipe group element's historic formula, byte for byte).
        /// </summary>
        internal static List<(Vector2 P, float Deg)> LinePlacesEven(IReadOnlyList<Vector2> pts, int count)
        {
            var fractions = new float[Math.Max(count, 0)];
            for (int i = 0; i < fractions.Length; i++) fractions[i] = (i + 0.5f) / count;
            return LinePlacesAt(pts, fractions);
        }

        /// <summary>
        /// Places every <paramref name="spacing"/> metres along the polyline, the first half a spacing from its start (distance <c>spacing · (i + 0.5)</c> for every <c>i</c> that lies before the end), at most <paramref name="cap"/> of them.
        /// </summary>
        internal static List<(Vector2 P, float Deg)> LinePlacesSpaced(IReadOnlyList<Vector2> pts, float spacing, int cap)
        {
            float total = 0f;
            for (int i = 1; i < pts.Count; i++) total += (pts[i] - pts[i - 1]).Length();
            var fractions = new List<float>();
            if (spacing > 0f && total > 0f)
                for (int i = 0; i < cap; i++)
                {
                    float dist = spacing * (i + 0.5f);
                    if (dist >= total) break;
                    fractions.Add(dist / total);
                }
            return LinePlacesAt(pts, fractions.ToArray());
        }

        /// <summary>The places at each <paramref name="fractions"/> entry (0..1 of the polyline's length, ascending), with the direction of the segment each lies on.</summary>
        private static List<(Vector2 P, float Deg)> LinePlacesAt(IReadOnlyList<Vector2> pts, float[] fractions)
        {
            var places = new List<(Vector2 P, float Deg)>(fractions.Length);
            if (pts.Count < 2) return places;
            if (pts.Count == 2)
            {
                var a = pts[0]; var d = pts[1] - a; float deg = MathF.Atan2(d.Y, d.X) * 180f / MathF.PI;
                foreach (float f in fractions) places.Add((a + d * f, deg));
                return places;
            }
            float total = 0f;
            for (int i = 1; i < pts.Count; i++) total += (pts[i] - pts[i - 1]).Length();
            int seg = 1; float before = 0f;
            foreach (float f in fractions)
            {
                float dist = f * total;
                while (seg < pts.Count - 1 && before + (pts[seg] - pts[seg - 1]).Length() < dist) { before += (pts[seg] - pts[seg - 1]).Length(); seg++; }
                var s0 = pts[seg - 1]; var d = pts[seg] - s0; float len = d.Length();
                float u = len > 0f ? Math.Clamp((dist - before) / len, 0f, 1f) : 0f;
                places.Add((s0 + d * u, MathF.Atan2(d.Y, d.X) * 180f / MathF.PI));
            }
            return places;
        }

        /// <summary>
        /// A polygon prepared for seeded rejection sampling (the scatter's and the group feature's): the closing duplicate point dropped, its box and its area.
        /// </summary>
        internal readonly struct AreaSampler
        {
            private readonly List<Vector2> _poly;
            private readonly Vector2 _min, _max;
            public AreaSampler(IReadOnlyList<Vector2> points)
            {
                var poly = new List<Vector2>(points);
                if (poly.Count > 1 && poly[0] == poly[^1]) poly.RemoveAt(poly.Count - 1);
                _poly = poly; (_min, _max) = Box(poly);
                Area = MathF.Abs(PolygonMath.SignedArea2(poly)) / 2f;
            }

            /// <summary>The polygon's area in square metres.</summary>
            public float Area { get; }

            /// <summary>
            /// One place inside the polygon and out of the base's water, or null: <see cref="Attempts"/> draws pairs are ALWAYS made, whether or not a place was found earlier, so the sequence does not depend on the base.
            /// </summary>
            public Vector2? Next(ref Rng rng, TerrainWorld baseWorld)
            {
                Vector2? place = null;
                for (int a = 0; a < Attempts; a++)
                {
                    var p = new Vector2(Round(_min.X + rng.NextFloat() * (_max.X - _min.X)), Round(_min.Y + rng.NextFloat() * (_max.Y - _min.Y)));
                    if (place == null && PolygonMath.Contains(_poly, p) && !InWater(baseWorld, p, new[] { p })) place = p;
                }
                return place;
            }
        }
    }
}
