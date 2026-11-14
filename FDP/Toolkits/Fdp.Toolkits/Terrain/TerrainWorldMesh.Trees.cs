using System;
using System.Collections.Generic;
using System.Numerics;

namespace Fdp.Toolkit.Terrain
{
    public static partial class TerrainWorldMesh
    {
        /// <summary>⭐ CE-4377 (DESIGN_Map_3D_Mode §6ai T-4) — the material tags of the drawn trees (presentation only: no prism, no sight or fire).</summary>
        public const string CrownMaterial = "tree-crown", CrownDarkMaterial = "tree-crown-dark", TrunkMaterial = "tree-trunk", CanopyMaterial = "canopy";

        /// <summary>T-1 — a lone tree's crown centre and radius as shares of its trunk height.</summary>
        public const float CrownCentreShare = 0.72f, CrownRadiusShare = 0.3f;

        /// <summary>
        /// ⭐ CE-4377 (§6ai T-1 … T-3) — one chunk's trees. <paramref name="near"/>: every lone tree's crown + one tree (a 4-sided trunk and a crown) per
        /// <c>density × cell area</c> of each wood cell whose centre lies in the chunk, placed by a hash of the cell. Far: the lone crowns + the wood as a CANOPY sheet
        /// (corners at the ground + the cell's canopy) with walls down to the crown base where the neighbour cell is not wood. Deterministic.
        /// </summary>
        public static void BuildChunkTrees(TerrainWorld world, int cx, int cy, bool near, List<Vector3> v, List<int> t, List<TerrainTriangleTag> g)
        {
            var ch = Chunks.For(world);
            int k = (cy * ch.Layout.Nx) + cx;
            if ((uint)k >= (uint)ch.Prisms.Length) return;
            foreach (int p in ch.Prisms[k])
            {
                var pr = world.Prisms[p];
                if (pr.Kind != TerrainPrismKind.Tree) continue;
                float h = pr.TopZ - pr.BaseZ;
                var c = (pr.Min + pr.Max) * 0.5f;
                AddCrown(new Vector3(c, pr.BaseZ + (CrownCentreShare * h)), CrownRadiusShare * h, Hash(p, 0, 7) < 0.5f ? CrownMaterial : CrownDarkMaterial, v, t, g);
            }
            if (world.Woods is not { } woods) return;

            var min = ch.Layout.ChunkMin(cx, cy); var max = ch.Layout.ChunkMax(cx, cy);
            float cell = TerrainWoods.Cell;
            var o = woods.Origin;
            int c0 = Math.Max(0, (int)MathF.Ceiling(((min.X - o.X) / cell) - 0.5f)), c1 = Math.Min(woods.Columns - 1, (int)MathF.Ceiling(((max.X - o.X) / cell) - 0.5f) - 1);
            int r0 = Math.Max(0, (int)MathF.Ceiling(((min.Y - o.Y) / cell) - 0.5f)), r1 = Math.Min(woods.Rows - 1, (int)MathF.Ceiling(((max.Y - o.Y) / cell) - 0.5f) - 1);
            for (int r = r0; r <= r1; r++)
                for (int c = c0; c <= c1; c++)
                {
                    var (density, canopy) = woods.CellAt(c, r);
                    if (density <= 0f || canopy <= 0f) continue;
                    float x0 = o.X + (c * cell), y0 = o.Y + (r * cell);
                    if (near) AddWoodTrees(world, c, r, x0, y0, cell, density, canopy, v, t, g);
                    else AddCanopyCell(world, woods, c, r, x0, y0, cell, canopy, v, t, g);
                }
        }

        private static void AddWoodTrees(TerrainWorld world, int c, int r, float x0, float y0, float cell, float density, float canopy,
                                         List<Vector3> v, List<int> t, List<TerrainTriangleTag> g)
        {
            int n = (int)MathF.Round(density * cell * cell / 10000f);
            for (int i = 0; i < n; i++)
            {
                float x = x0 + (cell * (0.08f + (0.84f * Hash(c, r, (i * 3) + 1))));
                float y = y0 + (cell * (0.08f + (0.84f * Hash(c, r, (i * 3) + 2))));
                float h = canopy * (0.8f + (0.4f * Hash(c, r, (i * 3) + 3)));
                float z = world.GroundHeightAt(x, y);
                AddTrunk(new Vector3(x, y, z), 0.18f, h * 0.55f, v, t, g);
                AddCrown(new Vector3(x, y, z + (0.68f * h)), 0.3f * h, Hash(c, r, (i * 3) + 4) < 0.5f ? CrownMaterial : CrownDarkMaterial, v, t, g);
            }
        }

        private static void AddCanopyCell(TerrainWorld world, TerrainWoods woods, int c, int r, float x0, float y0, float cell, float canopy,
                                          List<Vector3> v, List<int> t, List<TerrainTriangleTag> g)
        {
            float x1 = x0 + cell, y1 = y0 + cell;
            Vector3 Top(float x, float y) => new(x, y, world.GroundHeightAt(x, y) + canopy);
            Vector3 Base(float x, float y) => new(x, y, world.GroundHeightAt(x, y) + (canopy * TerrainWoods.CrownBase));
            var tag = new TerrainTriangleTag(TerrainTriangleKind.Roof, CanopyMaterial);
            int b = v.Count;
            v.Add(Top(x0, y0)); v.Add(Top(x1, y0)); v.Add(Top(x1, y1)); v.Add(Top(x0, y1));
            AddUp(v, t, b, b + 1, b + 2); AddUp(v, t, b, b + 2, b + 3);
            g.Add(tag); g.Add(tag);
            // the wood's edge: a wall from the crown base to the canopy where the neighbour is open
            if (woods.CellAt(c - 1, r).Density <= 0f) Edge(Base(x0, y1), Base(x0, y0), Top(x0, y0), Top(x0, y1), v, t, g);
            if (woods.CellAt(c + 1, r).Density <= 0f) Edge(Base(x1, y0), Base(x1, y1), Top(x1, y1), Top(x1, y0), v, t, g);
            if (woods.CellAt(c, r - 1).Density <= 0f) Edge(Base(x0, y0), Base(x1, y0), Top(x1, y0), Top(x0, y0), v, t, g);
            if (woods.CellAt(c, r + 1).Density <= 0f) Edge(Base(x1, y1), Base(x0, y1), Top(x0, y1), Top(x1, y1), v, t, g);
        }

        private static void Edge(Vector3 a, Vector3 b, Vector3 c, Vector3 d, List<Vector3> v, List<int> t, List<TerrainTriangleTag> g)
        {
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
            var tag = new TerrainTriangleTag(TerrainTriangleKind.Wall, CanopyMaterial);
            g.Add(tag); g.Add(tag);
        }

        /// <summary>T-1 — a 16-triangle crown: an apex, two rings of four (the lower turned 45°), a bottom point; slightly taller than wide.</summary>
        private static void AddCrown(Vector3 centre, float radius, string material, List<Vector3> v, List<int> t, List<TerrainTriangleTag> g)
        {
            int b = v.Count;
            v.Add(centre + new Vector3(0f, 0f, radius * 1.15f));                       // apex
            for (int i = 0; i < 4; i++) { float a = i * MathF.PI / 2f; v.Add(centre + new Vector3(MathF.Cos(a) * radius * 0.9f, MathF.Sin(a) * radius * 0.9f, radius * 0.35f)); }
            for (int i = 0; i < 4; i++) { float a = (i * MathF.PI / 2f) + (MathF.PI / 4f); v.Add(centre + new Vector3(MathF.Cos(a) * radius, MathF.Sin(a) * radius, -radius * 0.25f)); }
            v.Add(centre + new Vector3(0f, 0f, -radius * 0.8f));                        // bottom
            var tag = new TerrainTriangleTag(TerrainTriangleKind.Wall, material);
            for (int i = 0; i < 4; i++)
            {
                int u0 = b + 1 + i, u1 = b + 1 + ((i + 1) % 4), l0 = b + 5 + i, lm = b + 5 + ((i + 3) % 4);
                Out(v, t, centre, b, u0, u1); g.Add(tag);                                // apex fan
                Out(v, t, centre, u0, l0, u1); g.Add(tag);                               // the band
                Out(v, t, centre, u0, lm, l0); g.Add(tag);
                Out(v, t, centre, b + 9, b + 5 + ((i + 1) % 4), l0); g.Add(tag);         // bottom fan
            }
        }

        /// <summary>A 4-sided trunk (8 triangles, no caps).</summary>
        private static void AddTrunk(Vector3 foot, float radius, float height, List<Vector3> v, List<int> t, List<TerrainTriangleTag> g)
        {
            var tag = new TerrainTriangleTag(TerrainTriangleKind.Wall, TrunkMaterial);
            for (int i = 0; i < 4; i++)
            {
                float a0 = i * MathF.PI / 2f, a1 = (i + 1) * MathF.PI / 2f;
                var p0 = foot + new Vector3(MathF.Cos(a0) * radius, MathF.Sin(a0) * radius, 0f);
                var p1 = foot + new Vector3(MathF.Cos(a1) * radius, MathF.Sin(a1) * radius, 0f);
                int b = v.Count;
                v.Add(p0); v.Add(p1); v.Add(p1 + new Vector3(0, 0, height)); v.Add(p0 + new Vector3(0, 0, height));
                t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3);
                g.Add(tag); g.Add(tag);
            }
        }

        /// <summary>Adds the triangle wound so its normal points AWAY from <paramref name="centre"/>.</summary>
        private static void Out(List<Vector3> v, List<int> t, Vector3 centre, int i0, int i1, int i2)
        {
            var n = Vector3.Cross(v[i1] - v[i0], v[i2] - v[i0]);
            var mid = (v[i0] + v[i1] + v[i2]) / 3f;
            t.Add(i0);
            if (Vector3.Dot(n, mid - centre) >= 0f) { t.Add(i1); t.Add(i2); } else { t.Add(i2); t.Add(i1); }
        }

        /// <summary>A deterministic 0…1 from three integers (the placement never changes between builds).</summary>
        private static float Hash(int a, int b, int c)
        {
            unchecked
            {
                uint h = (uint)a * 73856093u ^ (uint)b * 19349663u ^ (uint)c * 83492791u;
                h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }
    }
}
