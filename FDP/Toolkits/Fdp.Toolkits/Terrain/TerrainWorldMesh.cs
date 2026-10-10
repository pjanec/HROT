using System;
using System.Collections.Generic;
using System.Numerics;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐⭐ <b>The terrain world as a triangle soup, in ENGINE space (Z-up)</b> — what a navmesh baker voxelises
    /// (docs/DESIGN_Terrain_World.md §3, §4.1). Pure geometry, no navmesh dependency: the Recast-side geometry
    /// source swizzles it to Recast's Y-up INSIDE the Recast implementation (W7).
    /// <list type="bullet">
    ///   <item><b>Ground</b> — a grid of quads over the world bounds at <see cref="TerrainWorld.GroundZ"/>, minus the
    ///     cells whose centre lies inside a building/wall footprint or a water area.</item>
    ///   <item><b>Prisms</b> — the roof (walkable when reachable) and the side walls (vertical, so they block).</item>
    ///   <item><b>Slabs and ramps</b> — their own triangles at their own Z: garage decks and the ramps between them.</item>
    /// </list>
    /// <para>⭐ Every triangle faces UP (+Z normal, counter-clockwise seen from above); side walls face outward.</para>
    /// </summary>
    public static class TerrainWorldMesh
    {
        /// <summary>The ground grid's cell size for a world of the given extent: 2 m, coarser for a large world
        /// so the soup stays near 256×256 cells.</summary>
        public static float DefaultCellSize(TerrainWorld world)
        {
            var size = world.BoundsMax - world.BoundsMin;
            return MathF.Max(2f, MathF.Max(size.X, size.Y) / 256f);
        }

        /// <summary>Builds the soup. <paramref name="verts"/> are engine-space points; <paramref name="indices"/>
        /// three per triangle.</summary>
        public static void Build(TerrainWorld world, out Vector3[] verts, out int[] indices, float cellSize = 0f)
        {
            if (cellSize <= 0f) cellSize = DefaultCellSize(world);
            var v = new List<Vector3>();
            var t = new List<int>();

            AddGround(world, cellSize, v, t);
            foreach (var prism in world.Prisms) AddPrism(prism, v, t);
            foreach (var w in world.Walkables)
            {
                int b = v.Count;
                v.AddRange(w.Vertices);
                for (int i = 0; i + 2 < w.Triangles.Length; i += 3)
                    AddUp(v, t, b + w.Triangles[i], b + w.Triangles[i + 1], b + w.Triangles[i + 2]);
            }

            verts = v.ToArray();
            indices = t.ToArray();
        }

        private static void AddGround(TerrainWorld world, float cell, List<Vector3> v, List<int> t)
        {
            var min = world.BoundsMin;
            var max = world.BoundsMax;
            int nx = Math.Max(1, (int)MathF.Ceiling((max.X - min.X) / cell));
            int ny = Math.Max(1, (int)MathF.Ceiling((max.Y - min.Y) / cell));
            float z = world.GroundZ;

            for (int iy = 0; iy < ny; iy++)
            for (int ix = 0; ix < nx; ix++)
            {
                float x0 = min.X + (ix * cell), y0 = min.Y + (iy * cell);
                float x1 = MathF.Min(x0 + cell, max.X), y1 = MathF.Min(y0 + cell, max.Y);
                if (Excluded(world, new Vector2((x0 + x1) * 0.5f, (y0 + y1) * 0.5f))) continue;

                int b = v.Count;
                v.Add(new Vector3(x0, y0, z));
                v.Add(new Vector3(x1, y0, z));
                v.Add(new Vector3(x1, y1, z));
                v.Add(new Vector3(x0, y1, z));
                t.Add(b); t.Add(b + 1); t.Add(b + 2);
                t.Add(b); t.Add(b + 2); t.Add(b + 3);
            }
        }

        private static bool Excluded(TerrainWorld world, Vector2 p)
        {
            foreach (var prism in world.Prisms)
            {
                // ⭐ Buildings 5d — a WALL PANEL (a building's wall, a fence, a free-standing wall: Panel ≥ 0) is thin, and its own
                //   side faces already block. ⛔ Excluding the ground cell whose CENTRE it covers cut a whole cell (2 m on bt-range)
                //   out of the floor along every inner wall that happened to cross a cell centre — the doorways in it never
                //   connected (House A's hall and front, found by the bt-doors live run, 2026-10-08). Only a SOLID prism (a
                //   block building) excludes the ground: its hollow inside would otherwise bake as walkable floor.
                if (prism.Panel >= 0) continue;
                if (InBox(p, prism.Min, prism.Max) && PolygonMath.Contains(prism.Footprint, p)) return true;
            }
            foreach (var s in world.Surfaces)
                if (s.Type == TerrainSurfaceType.Water && InBox(p, s.Min, s.Max) && PolygonMath.Contains(s.Polygon, p))
                    return true;
            return false;
        }

        private static void AddPrism(TerrainPrism prism, List<Vector3> v, List<int> t)
        {
            var fp = prism.Footprint;
            int n = fp.Length;
            if (n < 3) return;

            // Roof.
            int roof = v.Count;
            foreach (var p in fp) v.Add(new Vector3(p, prism.TopZ));
            for (int i = 0; i + 2 < prism.Triangles.Length; i += 3)
                AddUp(v, t, roof + prism.Triangles[i], roof + prism.Triangles[i + 1], roof + prism.Triangles[i + 2]);

            // Side walls, one quad per edge, facing outward.
            bool ccw = PolygonMath.SignedArea2(fp) > 0f;
            for (int i = 0; i < n; i++)
            {
                var a = fp[i];
                var c = fp[(i + 1) % n];
                if (!ccw) (a, c) = (c, a);          // walk CCW so the outward side is to the right
                int b = v.Count;
                v.Add(new Vector3(a, prism.BaseZ));
                v.Add(new Vector3(c, prism.BaseZ));
                v.Add(new Vector3(c, prism.TopZ));
                v.Add(new Vector3(a, prism.TopZ));
                t.Add(b); t.Add(b + 1); t.Add(b + 2);
                t.Add(b); t.Add(b + 2); t.Add(b + 3);
            }
        }

        /// <summary>Adds a triangle wound so its normal points up (+Z).</summary>
        private static void AddUp(List<Vector3> v, List<int> t, int i0, int i1, int i2)
        {
            var n = Vector3.Cross(v[i1] - v[i0], v[i2] - v[i0]);
            t.Add(i0);
            if (n.Z >= 0f) { t.Add(i1); t.Add(i2); }
            else           { t.Add(i2); t.Add(i1); }
        }

        private static bool InBox(Vector2 p, Vector2 min, Vector2 max)
            => p.X >= min.X && p.X <= max.X && p.Y >= min.Y && p.Y <= max.Y;
    }
}
