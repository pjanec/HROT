using System;
using System.Collections.Generic;
using System.Numerics;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ The small 2-D/3-D polygon kit the terrain world is built on — point-in-polygon, ear-clipping
    /// triangulation, segment-vs-polygon overlap and segment-vs-triangle crossing. All in the engine's
    /// Z-up space (X east, Y north, Z up).
    ///
    /// <para>⚠ Two private point-in-polygon copies already exist (<c>EntitiesInAreaGenerator</c> in SimHost,
    /// <c>FakeNavmeshProvider</c> in the navigation fake). This is the shared one; it does not replace them
    /// in this slice (both are pinned by their own rails).</para>
    /// 📄 docs/DESIGN_Terrain_World.md §3.
    /// </summary>
    public static class PolygonMath
    {
        /// <summary>Twice the signed area; positive when <paramref name="poly"/> is counter-clockwise.</summary>
        public static float SignedArea2(IReadOnlyList<Vector2> poly)
        {
            float a = 0f;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                a += (poly[j].X * poly[i].Y) - (poly[i].X * poly[j].Y);
            return a;
        }

        /// <summary>Crossing-number point-in-polygon (simple polygons, either winding).</summary>
        public static bool Contains(IReadOnlyList<Vector2> poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                var a = poly[i];
                var b = poly[j];
                if ((a.Y > p.Y) != (b.Y > p.Y))
                {
                    float x = a.X + ((p.Y - a.Y) / (b.Y - a.Y) * (b.X - a.X));
                    if (p.X < x) inside = !inside;
                }
            }
            return inside;
        }

        /// <summary>
        /// Ear-clipping triangulation of a simple polygon (no holes). Returns index triples into
        /// <paramref name="poly"/>. A degenerate polygon (&lt; 3 points or zero area) yields no triangles.
        /// </summary>
        public static int[] Triangulate(IReadOnlyList<Vector2> poly)
        {
            int n = poly.Count;
            if (n < 3) return Array.Empty<int>();

            var idx = new List<int>(n);
            bool ccw = SignedArea2(poly) > 0f;
            for (int i = 0; i < n; i++) idx.Add(ccw ? i : n - 1 - i);

            var result = new List<int>((n - 2) * 3);
            int guard = 0;
            while (idx.Count > 3 && guard++ < n * n)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int ia = idx[(i + idx.Count - 1) % idx.Count];
                    int ib = idx[i];
                    int ic = idx[(i + 1) % idx.Count];
                    var a = poly[ia];
                    var b = poly[ib];
                    var c = poly[ic];
                    if (Cross(b - a, c - b) <= 0f) continue;   // reflex or collinear

                    bool anyInside = false;
                    for (int k = 0; k < idx.Count; k++)
                    {
                        int ip = idx[k];
                        if (ip == ia || ip == ib || ip == ic) continue;
                        if (PointInTriangle(poly[ip], a, b, c)) { anyInside = true; break; }
                    }
                    if (anyInside) continue;

                    result.Add(ia); result.Add(ib); result.Add(ic);
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) break;   // self-intersecting input — return what was clipped so far
            }
            if (idx.Count == 3)
            {
                result.Add(idx[0]); result.Add(idx[1]); result.Add(idx[2]);
            }
            return result.ToArray();
        }

        /// <summary>
        /// The parameter intervals <c>[t0, t1] ⊆ [0, 1]</c> over which the 2-D segment <paramref name="a"/>→
        /// <paramref name="b"/> lies inside <paramref name="poly"/>. Empty when it never enters.
        /// </summary>
        public static List<(float T0, float T1)> InsideIntervals(IReadOnlyList<Vector2> poly, Vector2 a, Vector2 b)
        {
            var ts = new List<float> { 0f, 1f };
            var d = b - a;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                if (SegmentIntersection(a, d, poly[j], poly[i] - poly[j], out float t))
                    ts.Add(t);
            }
            ts.Sort();

            var intervals = new List<(float, float)>();
            for (int k = 0; k + 1 < ts.Count; k++)
            {
                float t0 = ts[k];
                float t1 = ts[k + 1];
                if (t1 - t0 < 1e-6f) continue;
                if (Contains(poly, a + (d * ((t0 + t1) * 0.5f))))
                    intervals.Add((t0, t1));
            }
            return intervals;
        }

        /// <summary>
        /// Möller–Trumbore: true when the open segment <paramref name="p0"/>→<paramref name="p1"/> crosses
        /// triangle (<paramref name="v0"/>, <paramref name="v1"/>, <paramref name="v2"/>) strictly between its
        /// end points (so a segment that merely starts or ends ON a floor does not count as blocked by it).
        /// </summary>
        public static bool SegmentCrossesTriangle(Vector3 p0, Vector3 p1, Vector3 v0, Vector3 v1, Vector3 v2)
        {
            const float eps = 1e-6f;
            var dir = p1 - p0;
            var e1 = v1 - v0;
            var e2 = v2 - v0;
            var h = Vector3.Cross(dir, e2);
            float det = Vector3.Dot(e1, h);
            if (MathF.Abs(det) < eps) return false;   // parallel
            float inv = 1f / det;
            var s = p0 - v0;
            float u = inv * Vector3.Dot(s, h);
            if (u < 0f || u > 1f) return false;
            var q = Vector3.Cross(s, e1);
            float v = inv * Vector3.Dot(dir, q);
            if (v < 0f || u + v > 1f) return false;
            float t = inv * Vector3.Dot(e2, q);
            return t > 1e-4f && t < 1f - 1e-4f;
        }

        /// <summary>
        /// Barycentric height of (<paramref name="x"/>, <paramref name="y"/>) on triangle
        /// (<paramref name="a"/>, <paramref name="b"/>, <paramref name="c"/>), or <c>null</c> when the point is
        /// outside it in plan view.
        /// </summary>
        public static float? HeightOnTriangle(float x, float y, Vector3 a, Vector3 b, Vector3 c)
        {
            float d = ((b.Y - c.Y) * (a.X - c.X)) + ((c.X - b.X) * (a.Y - c.Y));
            if (MathF.Abs(d) < 1e-9f) return null;
            float l1 = (((b.Y - c.Y) * (x - c.X)) + ((c.X - b.X) * (y - c.Y))) / d;
            float l2 = (((c.Y - a.Y) * (x - c.X)) + ((a.X - c.X) * (y - c.Y))) / d;
            float l3 = 1f - l1 - l2;
            const float eps = -1e-5f;
            if (l1 < eps || l2 < eps || l3 < eps) return null;
            return (l1 * a.Z) + (l2 * b.Z) + (l3 * c.Z);
        }

        private static float Cross(Vector2 a, Vector2 b) => (a.X * b.Y) - (a.Y * b.X);

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(b - a, p - a);
            float d2 = Cross(c - b, p - b);
            float d3 = Cross(a - c, p - c);
            bool neg = d1 < 0f || d2 < 0f || d3 < 0f;
            bool pos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(neg && pos);
        }

        private static bool SegmentIntersection(Vector2 p, Vector2 r, Vector2 q, Vector2 s, out float t)
        {
            t = 0f;
            float rxs = Cross(r, s);
            if (MathF.Abs(rxs) < 1e-9f) return false;
            var qp = q - p;
            t = Cross(qp, s) / rxs;
            float u = Cross(qp, r) / rxs;
            return t > 0f && t < 1f && u >= 0f && u <= 1f;
        }
    }
}
