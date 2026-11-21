using System;
using System.Collections.Generic;
using System.Numerics;

namespace Fdp.Toolkit.Terrain
{
    public static partial class TerrainWorldMesh
    {
        /// <summary>⭐ painted windows on a solid building's walls (presentation only): their material — dark glass in the 3-D layer.</summary>
        public const string PaintedWindowMaterial = "painted-window";

        /// <summary>A solid building gets painted windows from this footprint area (m²) up: garages, sheds and anything smaller stay plain.</summary>
        public const float PaintedWindowsMinArea = 40f;

        public const float WindowWidth = 1.1f, WindowHeight = 1.3f, WindowSill = 0.9f, WindowSpacing = 2.8f, WindowCornerClear = 0.8f;

        /// <summary>True when <paramref name="p"/> is a solid building that is drawn with painted windows: a building prism with storeys, not a roof cap, at least
        /// <see cref="PaintedWindowsMinArea"/> m².</summary>
        public static bool HasPaintedWindows(TerrainPrism p)
            => p.Kind == TerrainPrismKind.Building && p.Panel < 0 && p.Floors > 0 && !IsRoofCap(p) && MathF.Abs(PolygonMath.SignedArea2(p.Footprint)) * 0.5f >= PaintedWindowsMinArea;

        /// <summary>
        /// ⭐ (ui, `2026-11-10`; user: "what about at least fake some in the textures of the walls?") — WINDOWS PAINTED on a solid building's walls: a solid building has no openings
        /// (it is one prism for sight and fire, R-4193), so a big one looked like a blank block. One row per storey (the prism's height split by its <see cref="TerrainPrism.Floors"/>),
        /// the sill <see cref="WindowSill"/> over the storey's floor, <see cref="WindowWidth"/> × <see cref="WindowHeight"/> panes spaced about <see cref="WindowSpacing"/> along each
        /// wall, clear of the corners, 2 cm off the face. Presentation only: the prism is untouched.
        /// </summary>
        public static void AddPaintedWindows(TerrainPrism p, List<Vector3> v, List<int> t, List<TerrainTriangleTag> g)
        {
            var fp = p.Footprint;
            int n = fp.Length;
            if (n < 3 || p.Floors <= 0) return;
            float storey = p.Height / p.Floors;
            if (storey < WindowSill + WindowHeight + 0.2f) return;
            bool ccw = PolygonMath.SignedArea2(fp) > 0f;
            var tag = new TerrainTriangleTag(TerrainTriangleKind.Wall, PaintedWindowMaterial);
            for (int i = 0; i < n; i++)
            {
                var a = fp[i]; var b = fp[(i + 1) % n];
                if (!ccw) (a, b) = (b, a);   // walk CCW: the outward side is to the right
                float len = Vector2.Distance(a, b);
                float usable = len - (2f * WindowCornerClear);
                if (usable < WindowWidth) continue;
                int count = Math.Max(1, (int)MathF.Floor((usable + (WindowSpacing - WindowWidth)) / WindowSpacing));
                var dir = (b - a) / len;
                var outward = new Vector2(dir.Y, -dir.X) * 0.02f;   // right of the CCW walk
                float pitch = usable / count;
                for (int f = 0; f < p.Floors; f++)
                {
                    float z0 = p.BaseZ + (f * storey) + WindowSill, z1 = z0 + WindowHeight;
                    for (int k = 0; k < count; k++)
                    {
                        float c = WindowCornerClear + (pitch * (k + 0.5f));
                        var p0 = a + (dir * (c - (WindowWidth * 0.5f))) + outward; var p1 = a + (dir * (c + (WindowWidth * 0.5f))) + outward;
                        int q = v.Count;
                        v.Add(new Vector3(p0, z0)); v.Add(new Vector3(p1, z0)); v.Add(new Vector3(p1, z1)); v.Add(new Vector3(p0, z1));
                        t.Add(q); t.Add(q + 1); t.Add(q + 2); t.Add(q); t.Add(q + 2); t.Add(q + 3);
                        g.Add(tag); g.Add(tag);
                    }
                }
            }
        }
    }
}
