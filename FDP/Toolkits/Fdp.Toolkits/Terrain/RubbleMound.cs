using System;
using System.Collections.Generic;
using System.Numerics;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ CE-3438 (T-7b, docs/DESIGN_Terrain_Authoring.md §18k) — a collapsed structure's rubble mound, CLIPPED TO LAND: the mound is laid on the structure's footprint, but a mound over a <c>surface: water</c> is a causeway across the river
    /// (measured, T-2: the navmesh walked bank to bank over a fallen bridge's mound). ONE function, used by the collision prism (<see cref="BuildingDamageTable"/>), the 3-D heap, the 2-D plan and the derived world — ⛔ no second clip.
    /// <para>A footprint no water surface reaches is the one prism it always was. Otherwise the footprint's LONG axis is walked at <see cref="StepMetres"/> along its centre line: each run of dry samples ≥ <see cref="MinPieceMetres"/>
    /// becomes one piece — the footprint cut by two cross-lines (Sutherland–Hodgman) — so a bridge over a river leaves a mound on each bank and none in the water. (The brief said "drop the footprint triangles whose centroid is in water":
    /// a rectangle is TWO triangles, so a centroid test cannot cut one; the centre-line cut does, and is the same rule at a finer grain.)</para>
    /// </summary>
    public static class RubbleMound
    {
        public const float StepMetres = 0.5f, MinPieceMetres = 1f;

        /// <summary>The prisms of the mound of building <paramref name="building"/> — its whole <paramref name="whole"/> prism when no water is in its way, else one per dry run; empty when it stands wholly in water.</summary>
        public static List<TerrainPrism> Pieces(TerrainWorld world, int building, TerrainPrism whole)
        {
            var result = new List<TerrainPrism>(2);
            var wet = new List<TerrainSurface>(2);
            foreach (var s in world.Surfaces)
                if (s.Type == TerrainSurfaceType.Water && s.Polygon.Length >= 3 && s.Min.X <= whole.Max.X && s.Max.X >= whole.Min.X && s.Min.Y <= whole.Max.Y && s.Max.Y >= whole.Min.Y) wet.Add(s);
            if (wet.Count == 0) { result.Add(whole); return result; }

            var fp = whole.Footprint;
            // the long axis: the footprint's longest edge
            Vector2 u = Vector2.UnitX; float best = -1f;
            for (int i = 0; i < fp.Length; i++)
            {
                var e = fp[(i + 1) % fp.Length] - fp[i]; float l = e.LengthSquared();
                if (l > best) { best = l; u = e; }
            }
            if (best <= 0f) { result.Add(whole); return result; }
            u = Vector2.Normalize(u); var v = new Vector2(-u.Y, u.X);
            float t0 = float.MaxValue, t1 = float.MinValue, vSum = 0f;
            foreach (var p in fp) { float t = Vector2.Dot(p, u); t0 = MathF.Min(t0, t); t1 = MathF.Max(t1, t); vSum += Vector2.Dot(p, v); }
            float vMid = vSum / fp.Length;

            int n = Math.Max(1, (int)MathF.Ceiling((t1 - t0) / StepMetres));
            bool anyWet = false; float runStart = float.NaN;
            var runs = new List<(float A, float B)>(2);
            for (int k = 0; k <= n; k++)
            {
                float t = t0 + (t1 - t0) * k / n;
                var at = u * t + v * vMid;
                bool water = false;
                foreach (var s in wet) if (PolygonMath.Contains(s.Polygon, at)) { water = true; break; }
                anyWet |= water;
                if (!water && float.IsNaN(runStart)) runStart = t;
                if ((water || k == n) && !float.IsNaN(runStart)) { float end = water ? t - (t1 - t0) / n : t; if (end - runStart >= MinPieceMetres || (!anyWet && k == n)) runs.Add((runStart, end)); runStart = float.NaN; }
            }
            if (!anyWet) { result.Add(whole); return result; }   // the water never crossed the centre line: untouched
            foreach (var (a, b) in runs)
            {
                var poly = Clip(Clip(fp, u, a, keepAbove: true), u, b, keepAbove: false);
                if (poly.Length < 3) continue;
                var min = new Vector2(float.MaxValue); var max = new Vector2(float.MinValue);
                foreach (var p in poly) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
                result.Add(new TerrainPrism
                {
                    Kind = whole.Kind, Footprint = poly, BaseZ = whole.BaseZ, TopZ = whole.TopZ, Label = whole.Label, Triangles = PolygonMath.Triangulate(poly),
                    Min = min, Max = max, Material = whole.Material, Building = whole.Building,
                });
            }
            return result;
        }

        /// <summary>Sutherland–Hodgman against one half-plane (<c>dot(p, axis) ≥ at</c> when <paramref name="keepAbove"/>, else <c>≤ at</c>).</summary>
        private static Vector2[] Clip(Vector2[] poly, Vector2 axis, float at, bool keepAbove)
        {
            var o = new List<Vector2>(poly.Length + 2);
            float Side(Vector2 p) => keepAbove ? Vector2.Dot(p, axis) - at : at - Vector2.Dot(p, axis);
            for (int i = 0; i < poly.Length; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Length];
                float sa = Side(a), sb = Side(b);
                if (sa >= 0f) o.Add(a);
                if ((sa >= 0f) != (sb >= 0f)) o.Add(a + (b - a) * (sa / (sa - sb)));
            }
            return o.ToArray();
        }
    }
}
