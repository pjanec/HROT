using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Toolkit.World;

namespace Fdp.Toolkit.Terrain
{
    public static partial class TerrainWorldMesh
    {
        /// <summary>⭐ CE-3423 L-1 — the chunk side the grid aims at (m); the real side is a whole number of ground cells.</summary>
        public const float TargetChunkSize = 256f;

        /// <summary>⭐ CE-3423 L-2 — the coarsest ground cell a level may have (m).</summary>
        public const float MaxLevelCell = 16f;

        /// <summary>
        /// ⭐ CE-3423 (docs/DESIGN_Map_3D_Mode.md §6ah L-1, L-2) — the chunk grid over the ground layout: a chunk is a whole number of the soup's ground cells, so its level 0
        /// is exactly the soup's cells there. Levels while the coarse cell stays ≤ <see cref="MaxLevelCell"/> (at least one).
        /// </summary>
        public static TerrainChunkLayout ChunkLayout(TerrainWorld world) => Chunks.For(world).Layout;

        /// <summary>
        /// ⭐ CE-3423 — one chunk's GROUND at <paramref name="level"/>: level 0 = the soup's own cells (<c>AddGroundCell</c>, the same cut and forest tag); a coarser level
        /// samples the height at cell × 2^level and cuts only under water (L-3). Every chunk hangs a skirt down its interior edges (L-4), tagged as ground with
        /// <see cref="SkirtMaterial"/>, so two levels side by side leave no crack.
        /// </summary>
        public static void BuildChunkGround(TerrainWorld world, int cx, int cy, int level, List<Vector3> v, List<int> t, List<TerrainTriangleTag> g)
        {
            var ch = Chunks.For(world);
            var ix = RegionIndex.For(world);
            var (gmin, gmax, cell, nx, ny) = ix.Layout;
            int per = ch.CellsPerChunk;
            int x0 = cx * per, y0 = cy * per, x1 = Math.Min(nx, x0 + per), y1 = Math.Min(ny, y0 + per);
            if (x0 >= x1 || y0 >= y1) return;
            level = Math.Clamp(level, 0, ch.Layout.Levels - 1);
            if (level == 0)
            {
                for (int iy = y0; iy < y1; iy++)
                    for (int jx = x0; jx < x1; jx++)
                        AddGroundCell(world, gmin, gmax, cell, jx, iy, ix.Bins, v, t, g);
            }
            else
            {
                int step = 1 << level;
                for (int iy = y0; iy < y1; iy += step)
                    for (int jx = x0; jx < x1; jx += step)
                        AddCoarseCell(world, gmin, gmax, cell, jx, iy, Math.Min(step, x1 - jx), Math.Min(step, y1 - iy), ix.Bins, v, t, g);
            }
            AddSkirts(world, gmin, gmax, cell, x0, y0, x1, y1, 1 << level, nx, ny, v, t, g);
        }

        /// <summary>The material tag of a chunk's skirt (ground, never seen from above).</summary>
        public const string SkirtMaterial = "skirt";

        private static void AddCoarseCell(TerrainWorld world, Vector2 min, Vector2 max, float cell, int ix, int iy, int sx, int sy, GroundBins bins,
                                          List<Vector3> v, List<int> t, List<TerrainTriangleTag> g)
        {
            float x0 = min.X + (ix * cell), y0 = min.Y + (iy * cell);
            float x1 = MathF.Min(min.X + ((ix + sx) * cell), max.X), y1 = MathF.Min(min.Y + ((iy + sy) * cell), max.Y);
            var centre = new Vector2((x0 + x1) * 0.5f, (y0 + y1) * 0.5f);
            if (bins.InWater(centre)) return;
            int b = v.Count;
            v.Add(new Vector3(x0, y0, world.GroundHeightAt(x0, y0)));
            v.Add(new Vector3(x1, y0, world.GroundHeightAt(x1, y0)));
            v.Add(new Vector3(x1, y1, world.GroundHeightAt(x1, y1)));
            v.Add(new Vector3(x0, y1, world.GroundHeightAt(x0, y1)));
            t.Add(b); t.Add(b + 1); t.Add(b + 2);
            t.Add(b); t.Add(b + 2); t.Add(b + 3);
            var tag = new TerrainTriangleTag(TerrainTriangleKind.Ground, bins.InForest(centre) ? ForestMaterial : null);
            g.Add(tag); g.Add(tag);
        }

        /// <summary>L-4 — a strip down each INTERIOR edge of the chunk (not the terrain's outer edge, where the apron meets it), sampled at the level's step.</summary>
        private static void AddSkirts(TerrainWorld world, Vector2 min, Vector2 max, float cell, int x0, int y0, int x1, int y1, int step, int nx, int ny,
                                      List<Vector3> v, List<int> t, List<TerrainTriangleTag> g)
        {
            float depth = (2 * step * cell) + 1f;
            float X(int i) => MathF.Min(min.X + (i * cell), max.X);
            float Y(int i) => MathF.Min(min.Y + (i * cell), max.Y);
            if (x0 > 0) for (int i = y0; i < y1; i += step) Skirt(world, new Vector2(X(x0), Y(i)), new Vector2(X(x0), Y(Math.Min(i + step, y1))), depth, v, t, g);
            if (x1 < nx) for (int i = y0; i < y1; i += step) Skirt(world, new Vector2(X(x1), Y(i)), new Vector2(X(x1), Y(Math.Min(i + step, y1))), depth, v, t, g);
            if (y0 > 0) for (int i = x0; i < x1; i += step) Skirt(world, new Vector2(X(i), Y(y0)), new Vector2(X(Math.Min(i + step, x1)), Y(y0)), depth, v, t, g);
            if (y1 < ny) for (int i = x0; i < x1; i += step) Skirt(world, new Vector2(X(i), Y(y1)), new Vector2(X(Math.Min(i + step, x1)), Y(y1)), depth, v, t, g);
        }

        private static void Skirt(TerrainWorld world, Vector2 a, Vector2 c, float depth, List<Vector3> v, List<int> t, List<TerrainTriangleTag> g)
        {
            float za = world.GroundHeightAt(a.X, a.Y), zc = world.GroundHeightAt(c.X, c.Y);
            int b = v.Count;
            v.Add(new Vector3(a, za)); v.Add(new Vector3(c, zc)); v.Add(new Vector3(c, zc - depth)); v.Add(new Vector3(a, za - depth));
            t.Add(b); t.Add(b + 1); t.Add(b + 2);
            t.Add(b); t.Add(b + 2); t.Add(b + 3);
            var tag = new TerrainTriangleTag(TerrainTriangleKind.Ground, SkirtMaterial);
            g.Add(tag); g.Add(tag);
        }

        /// <summary>
        /// ⭐ CE-3423 L-5 — what stands in one chunk: the prisms, walkables and water surfaces whose box CENTRE lies in it (each drawn once, by the soup's own builders).
        /// Water is appended last, as flat triangles (<see cref="AppendWater"/>); <paramref name="waterTriangles"/> = how many of the triangles at the end are water.
        /// </summary>
        public static void BuildChunkObjects(TerrainWorld world, int cx, int cy, List<Vector3> v, List<int> t, List<TerrainTriangleTag> g, out int waterTriangles)
            => BuildChunkObjects(world, cx, cy, null, v, t, g, out waterTriangles);

        /// <summary>
        /// ⭐ <c>CE-4392</c> (DESIGN_Map_3D_Mode §6aj X-1 / X-2) — the chunk's objects as <paramref name="damage"/> leaves them (null = none, the plain build): a collapsed building's
        /// pieces skipped and its rubble drawn as a HEAP (<see cref="AddHeap"/>) in the chunk of the mound's centre; a breached panel re-cut (<see cref="DamagedBuildings.RecutPanel"/>) in
        /// the chunk of the panel's midpoint. ⛔ The world is not changed.
        /// </summary>
        public static void BuildChunkObjects(TerrainWorld world, int cx, int cy, BuildingDamageTable? damage, List<Vector3> v, List<int> t, List<TerrainTriangleTag> g, out int waterTriangles)
        {
            var ch = Chunks.For(world);
            int k = (cy * ch.Layout.Nx) + cx;
            waterTriangles = 0;
            if ((uint)k >= (uint)ch.Prisms.Length) return;
            foreach (int p in ch.Prisms[k])
            {
                var prism = world.Prisms[p];
                if (damage != null && !DamagedBuildings.Keeps(prism, damage)) continue;
                if (IsRoofCap(prism)) AddGable(prism, v, t, g);   // ⭐ CE-4377 §6ai R-1 — a pitched roof drawn as one (the cap stays the world's for sight and fire)
                else AddPrism(prism, v, t, g);
            }
            if (damage != null)
            {
                var panels = new List<int>();
                DamagedBuildings.BreachedPanels(damage, panels);
                var pieces = new List<TerrainPrism>();
                foreach (int pi in panels)
                {
                    if ((uint)pi >= (uint)world.Panels.Count || !DamagedBuildings.IsRecut(world, damage, pi)) continue;
                    var panel = world.Panels[pi];
                    var mid = (panel.A + panel.B) * 0.5f;
                    if (ch.ChunkOf(mid, mid) != k) continue;
                    pieces.Clear();
                    DamagedBuildings.RecutPanel(world, damage, pi, pieces);
                    foreach (var piece in pieces) AddPrism(piece, v, t, g);
                    foreach (var breach in damage.BreachesOf(pi)) AddScorch(panel, breach, v, t, g);   // ⭐ X-7 — the soot round each breach
                }
                foreach (int b in damage.CollapsedBuildings)
                {
                    if ((uint)b >= (uint)world.Buildings.Count) continue;
                    var rubble = damage.RubbleOf(world, b);
                    if (ch.ChunkOf(rubble.Min, rubble.Max) == k) AddHeap(rubble, v, t, g);
                }
            }
            foreach (int w in ch.Walkables[k])
                if (damage == null || DamagedBuildings.Keeps(world.Walkables[w], damage)) AddWalkable(world.Walkables[w], v, t, g);
            int before = t.Count;
            foreach (int s in ch.Water[k]) AppendWater(world, world.Surfaces[s], v, t);
            waterTriangles = (t.Count - before) / 3;
        }

        /// <summary>
        /// ⭐ CE-1033 S4 (moved here by CE-3423 so the whole build and the chunks share it) — a water surface as flat triangles at the lowest ground height round its edge,
        /// a little below the bank, wound up (+Z).
        /// </summary>
        public static void AppendWater(TerrainWorld world, TerrainSurface s, List<Vector3> v, List<int> idx)
        {
            if (s.Type != TerrainSurfaceType.Water || s.Polygon.Length < 3) return;
            float level = float.MaxValue;
            foreach (var p in s.Polygon) level = MathF.Min(level, world.GroundHeightAt(p.X, p.Y));
            level -= 0.15f;   // a little below the bank
            int b = v.Count;
            foreach (var p in s.Polygon) v.Add(new Vector3(p, level));
            for (int i = 0; i + 2 < s.Triangles.Length; i += 3)
            {
                int i0 = b + s.Triangles[i], i1 = b + s.Triangles[i + 1], i2 = b + s.Triangles[i + 2];
                bool up = Vector3.Cross(v[i1] - v[i0], v[i2] - v[i0]).Z >= 0f;
                idx.Add(i0); idx.Add(up ? i1 : i2); idx.Add(up ? i2 : i1);
            }
        }

        /// <summary>⭐ CE-4392 X-3 — the chunk indices (<c>cy · Nx + cx</c>) whose objects show building <paramref name="building"/>.</summary>
        public static IReadOnlyList<int> ChunksOfBuilding(TerrainWorld world, int building) => Chunks.For(world).OfBuilding(world, building);

        /// <summary>⭐ CE-3423 — the chunk grid of a world and which pieces belong to which chunk (their box centre), built once per world.</summary>
        private sealed class Chunks
        {
            private static readonly ConditionalWeakTable<TerrainWorld, Chunks> Memo = new();
            public static Chunks For(TerrainWorld world) => Memo.GetValue(world, w => new Chunks(w));

            public readonly TerrainChunkLayout Layout;
            public readonly int CellsPerChunk;
            public readonly List<int>[] Prisms, Walkables, Water;

            private Chunks(TerrainWorld world)
            {
                var (gmin, _, cell, nx, ny) = RegionIndex.For(world).Layout;
                CellsPerChunk = Math.Max(1, (int)MathF.Round(TargetChunkSize / cell));
                int cnx = (nx + CellsPerChunk - 1) / CellsPerChunk, cny = (ny + CellsPerChunk - 1) / CellsPerChunk;
                int levels = 1;
                while (levels < 4 && cell * (1 << levels) <= MaxLevelCell + 1e-3f && (1 << levels) <= CellsPerChunk) levels++;
                var (_, _) = SoupExtent(world, out float zLo, out float zHi, out bool any);
                if (!any) { zLo = 0f; zHi = 0f; }
                Layout = new TerrainChunkLayout(gmin, CellsPerChunk * cell, cell, cnx, cny, levels, zLo, zHi);
                int n = cnx * cny;
                Prisms = New(n); Walkables = New(n); Water = New(n);
                for (int k = 0; k < world.Prisms.Count; k++)
                    if (!IsVolumeEffect(world.Prisms[k])) Prisms[Of(world.Prisms[k].Min, world.Prisms[k].Max)].Add(k);
                for (int k = 0; k < world.Walkables.Count; k++) Walkables[Of(world.Walkables[k].Min, world.Walkables[k].Max)].Add(k);
                for (int k = 0; k < world.Surfaces.Count; k++)
                    if (world.Surfaces[k].Type == TerrainSurfaceType.Water) Water[Of(world.Surfaces[k].Min, world.Surfaces[k].Max)].Add(k);
            }

            private static List<int>[] New(int n) { var a = new List<int>[n]; for (int i = 0; i < n; i++) a[i] = new List<int>(); return a; }

            /// <summary>The chunk holding a box's centre (the damage drawing's owner rule, CE-4392).</summary>
            public int ChunkOf(Vector2 min, Vector2 max) => Of(min, max);

            /// <summary>⭐ CE-4392 X-3 — the chunks holding a piece of building <paramref name="b"/> (its prisms, walkables, its panels' midpoints, its rubble's centre), built once per world.</summary>
            public IReadOnlyList<int> OfBuilding(TerrainWorld world, int b)
            {
                lock (_byBuildingLock)
                {
                    if (_byBuilding == null)
                    {
                        var map = new Dictionary<int, List<int>>();
                        void Add(int building, int chunk) { if (building < 0) return; if (!map.TryGetValue(building, out var l)) map[building] = l = new List<int>(2); if (!l.Contains(chunk)) l.Add(chunk); }
                        for (int k = 0; k < Prisms.Length; k++) { foreach (int p in Prisms[k]) Add(world.Prisms[p].Building, k); foreach (int w in Walkables[k]) Add(world.Walkables[w].Building, k); }
                        for (int pi = 0; pi < world.Panels.Count; pi++) { var mid = (world.Panels[pi].A + world.Panels[pi].B) * 0.5f; Add(world.Panels[pi].Building, Of(mid, mid)); }
                        for (int bi = 0; bi < world.Buildings.Count; bi++)
                        {
                            var fp = world.Buildings[bi].Footprint;
                            var min = new Vector2(float.MaxValue); var max = new Vector2(float.MinValue);
                            foreach (var q in fp) { min = Vector2.Min(min, q); max = Vector2.Max(max, q); }
                            if (fp.Length > 0) Add(bi, Of(min, max));
                        }
                        _byBuilding = map;
                    }
                    return _byBuilding.TryGetValue(b, out var r) ? r : (IReadOnlyList<int>)System.Array.Empty<int>();
                }
            }
            private readonly object _byBuildingLock = new();
            private Dictionary<int, List<int>>? _byBuilding;

            /// <summary>The chunk holding a box's centre (clamped: a piece outside the grid belongs to the nearest edge chunk).</summary>
            private int Of(Vector2 min, Vector2 max)
            {
                var c = (min + max) * 0.5f;
                int x = Math.Clamp((int)MathF.Floor((c.X - Layout.Origin.X) / Layout.ChunkSize), 0, Layout.Nx - 1);
                int y = Math.Clamp((int)MathF.Floor((c.Y - Layout.Origin.Y) / Layout.ChunkSize), 0, Layout.Ny - 1);
                return (y * Layout.Nx) + x;
            }
        }
    }
}
