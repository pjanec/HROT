using System;
using System.Collections.Generic;
using System.Numerics;

namespace Fdp.Toolkit.Terrain
{
    public static partial class TerrainWorldMesh
    {
        /// <summary>⭐ CE-4377 (DESIGN_Map_3D_Mode §6ai R-1) — the drawn pitched roof's material tag (its colour and the roof-tile pattern).</summary>
        public const string PitchedRoofMaterial = "roof-pitched";

        /// <summary>
        /// ⭐ CE-4377 R-1 — a pitched roof's CAP: the importer writes a pitched roof as a flat roof + a solid prism from the eaves to the ridge
        /// (DESIGN_Terrain_Import_UTM I-h) — a building prism with no floors, labelled "&lt;id&gt;-roof" (<c>TerrainFolderWriter</c>).
        /// </summary>
        public static bool IsRoofCap(TerrainPrism prism)
            => prism.Kind == TerrainPrismKind.Building && prism.Floors == 0 && prism.Label is { } l && l.EndsWith("-roof", StringComparison.Ordinal);

        /// <summary>
        /// ⭐ CE-4377 R-1 — the cap drawn as a GABLE: the ridge runs along the footprint's longest edge, at the middle of its width; each footprint triangle is split at the
        /// ridge line and its corners lifted to the roof (eaves at the cap's base, the ridge at its top) — a planar slope on each side, on any footprint; the end walls
        /// close it in the house's material. Presentation only: sight and fire keep the world's cap prism.
        /// </summary>
        private static void AddGable(TerrainPrism prism, List<Vector3> v, List<int> t, List<TerrainTriangleTag> g)
        {
            var fp = prism.Footprint;
            int n = fp.Length;
            if (n < 3) { AddPrism(prism, v, t, g); return; }
            string wallMaterial = prism.Material?.Name ?? TerrainMaterialLibrary.DefaultMaterial;
            // the ridge direction: the longest edge
            Vector2 u = Vector2.UnitX; float longest = -1f;
            for (int i = 0; i < n; i++) { var e = fp[(i + 1) % n] - fp[i]; float l = e.LengthSquared(); if (l > longest) { longest = l; u = e; } }
            u = Vector2.Normalize(u);
            var w = new Vector2(-u.Y, u.X);
            float tMin = float.MaxValue, tMax = float.MinValue;
            for (int i = 0; i < n; i++) { float tt = Vector2.Dot(fp[i], w); tMin = MathF.Min(tMin, tt); tMax = MathF.Max(tMax, tt); }
            float t0 = (tMin + tMax) * 0.5f, half = MathF.Max(1e-3f, (tMax - tMin) * 0.5f);
            float baseZ = prism.BaseZ, rise = prism.TopZ - prism.BaseZ;
            Vector3 Lift(Vector2 p) => new(p, baseZ + (rise * MathF.Max(0f, 1f - (MathF.Abs(Vector2.Dot(p, w) - t0) / half))));

            // the slopes: each footprint triangle clipped on each side of the ridge line
            var roofTag = new TerrainTriangleTag(TerrainTriangleKind.Roof, PitchedRoofMaterial);
            Span<Vector2> poly = stackalloc Vector2[8];
            for (int i = 0; i + 2 < prism.Triangles.Length; i += 3)
            {
                var a = fp[prism.Triangles[i]]; var b = fp[prism.Triangles[i + 1]]; var c = fp[prism.Triangles[i + 2]];
                for (int side = -1; side <= 1; side += 2)
                {
                    int m = ClipSide(a, b, c, w, t0, side, poly);
                    for (int k = 1; k + 1 < m; k++)
                    {
                        int q = v.Count;
                        v.Add(Lift(poly[0])); v.Add(Lift(poly[k])); v.Add(Lift(poly[k + 1]));
                        AddUp(v, t, q, q + 1, q + 2);
                        g.Add(roofTag);
                    }
                }
            }
            // the end walls: each footprint edge from the eaves up to the roof line (split where it crosses the ridge), facing out
            var wallTag = new TerrainTriangleTag(TerrainTriangleKind.Wall, wallMaterial);
            bool ccw = PolygonMath.SignedArea2(fp) > 0f;
            for (int i = 0; i < n; i++)
            {
                var a = fp[i]; var c = fp[(i + 1) % n];
                if (!ccw) (a, c) = (c, a);
                float ta = Vector2.Dot(a, w) - t0, tc = Vector2.Dot(c, w) - t0;
                if (ta * tc < 0f) { var mid = Vector2.Lerp(a, c, ta / (ta - tc)); Wall(a, mid); Wall(mid, c); }
                else Wall(a, c);
            }

            void Wall(Vector2 p0, Vector2 p1)
            {
                var top0 = Lift(p0); var top1 = Lift(p1);
                if (top0.Z - baseZ < 1e-3f && top1.Z - baseZ < 1e-3f) return;   // an eaves edge: nothing to close
                int q = v.Count;
                v.Add(new Vector3(p0, baseZ)); v.Add(new Vector3(p1, baseZ)); v.Add(top1); v.Add(top0);
                if (top0.Z - baseZ >= 1e-3f) { t.Add(q); t.Add(q + 1); t.Add(q + 3); g.Add(wallTag); }   // a corner at the eaves has no height: no triangle there
                if (top1.Z - baseZ >= 1e-3f) { t.Add(q + 1); t.Add(q + 2); t.Add(q + 3); g.Add(wallTag); }
            }
        }

        /// <summary>The part of triangle (a, b, c) with <c>side · (p·w − t0) ≥ 0</c> (Sutherland–Hodgman against one line); returns its corner count.</summary>
        private static int ClipSide(Vector2 a, Vector2 b, Vector2 c, Vector2 w, float t0, int side, Span<Vector2> into)
        {
            int m = 0;
            Span<Vector2> tri = stackalloc Vector2[3] { a, b, c };
            for (int i = 0; i < 3; i++)
            {
                var p = tri[i]; var q = tri[(i + 1) % 3];
                float dp = side * (Vector2.Dot(p, w) - t0), dq = side * (Vector2.Dot(q, w) - t0);
                if (dp >= 0f) into[m++] = p;
                if ((dp >= 0f) != (dq >= 0f)) into[m++] = Vector2.Lerp(p, q, dp / (dp - dq));
            }
            return m;
        }
    }
}
