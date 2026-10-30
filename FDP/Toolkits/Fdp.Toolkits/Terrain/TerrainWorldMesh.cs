using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;

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
            => Build(world, out verts, out indices, out _, cellSize);

        /// <summary>
        /// ⭐ CE-1033 S4 (docs/DESIGN_Map_3D_Mode.md §4 — "an OPTIONAL per-triangle tag output … verts and indices unchanged, so the
        /// navmesh is unaffected") — the same soup, and for each triangle what it IS: <see cref="TerrainTriangleTag"/> (ground, a
        /// roof, a wall, a floor slab) and its material (a prism's material name; <c>"forest"</c> for ground under a forest surface;
        /// null for plain ground). The 3-D map colours by it; nothing else reads it.
        /// </summary>
        public static void Build(TerrainWorld world, out Vector3[] verts, out int[] indices, out TerrainTriangleTag[] tags, float cellSize = 0f)
        {
            // ⭐ CE-1452 — ONE build per terrain world at the default cell size: the 3-D mesh, its tags and the picker's grid all ask
            //   for the same soup (the build took 45–60 s on demo-city, three times over). The arrays are SHARED — read-only for every
            //   caller (measured: none mutates them). A non-default cell size builds directly, uncached.
            if (cellSize <= 0f)
            {
                var m = Memo.GetValue(world, w => new Lazy<Result>(() => BuildCore(w, DefaultCellSize(w)), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
                verts = m.Verts; indices = m.Indices; tags = m.Tags;
                return;
            }
            var r = BuildCore(world, cellSize);
            verts = r.Verts; indices = r.Indices; tags = r.Tags;
        }

        private sealed record Result(Vector3[] Verts, int[] Indices, TerrainTriangleTag[] Tags);

        private static readonly ConditionalWeakTable<TerrainWorld, Lazy<Result>> Memo = new();
        private static readonly ConditionalWeakTable<TerrainWorld, StrongBox<int>> CoreBuilds = new();

        /// <summary>How many times THIS world's soup has really been built (a rail counts it — the memo must make it one per world).</summary>
        internal static int CoreBuildCount(TerrainWorld world) => CoreBuilds.TryGetValue(world, out var n) ? Volatile.Read(ref n.Value) : 0;

        private static Result BuildCore(TerrainWorld world, float cellSize)
        {
            Interlocked.Increment(ref CoreBuilds.GetOrCreateValue(world).Value);
            var v = new List<Vector3>();
            var t = new List<int>();
            var g = new List<TerrainTriangleTag>();

            AddGround(world, cellSize, v, t, g);
            foreach (var prism in world.Prisms) if (!IsVolumeEffect(prism)) AddPrism(prism, v, t, g);
            foreach (var w in world.Walkables)
            {
                int b = v.Count;
                v.AddRange(w.Vertices);
                for (int i = 0; i + 2 < w.Triangles.Length; i += 3)
                {
                    AddUp(v, t, b + w.Triangles[i], b + w.Triangles[i + 1], b + w.Triangles[i + 2]);
                    g.Add(new TerrainTriangleTag(TerrainTriangleKind.Slab, null));
                }
            }

            return new Result(v.ToArray(), t.ToArray(), g.ToArray());
        }

        /// <summary>The ground grid's layout: its corner, its far corner, its cell and its cell counts — the ONE rule <see cref="AddGround"/> lays the
        /// ground out by and <see cref="BuildApron"/> meets its edge by (same edge vertices ⇒ no crack between them).</summary>
        private static (Vector2 Min, Vector2 Max, float Cell, int Nx, int Ny) GroundLayout(TerrainWorld world, float cell)
        {
            var min = world.BoundsMin;
            var max = world.BoundsMax;
            // ⭐ CE-1034 H1 — with a height grid the cells ARE the grid's cells (origin and size), so the triangles here, the navmesh
            //   baked from them and TerrainWorld.GroundHeightAt describe one surface; without one, the flat cells as before.
            if (world.Height is { } grid)
            {
                cell = grid.CellSize;
                min = new Vector2(MathF.Floor((min.X - grid.Origin.X) / cell) * cell + grid.Origin.X,
                                  MathF.Floor((min.Y - grid.Origin.Y) / cell) * cell + grid.Origin.Y);
            }
            int nx = Math.Max(1, (int)MathF.Ceiling((max.X - min.X) / cell));
            int ny = Math.Max(1, (int)MathF.Ceiling((max.Y - min.Y) / cell));
            return (min, max, cell, nx, ny);
        }

        /// <summary>The material tag of the <see cref="BuildApron"/> ground.</summary>
        public const string ApronMaterial = "apron";

        /// <summary>
        /// ⭐ The APRON — fake ground round the terrain, for DRAWING only. 🔒 User, 2026-10-30: <i>"The empty space around our terrain is now shown really
        /// empty. Which looks extra weird when the cars are placed there and moving there … draw there some fake terrain surface, in some cheap and fast
        /// way"</i> · <i>"there needs to be some basic texture on it so when we move the camera we see that something is moving"</i>.
        /// <para>A ring of quads from the ground grid's edge (its own edge vertices — no crack) out to <paramref name="extent"/> metres, every vertex at
        /// <see cref="TerrainWorld.GroundHeightAt"/> — which CLAMPS to the grid's edge outside it, the height a unit out there is clamped to ⇒ the cars
        /// sit on it by construction. ⛔ Never in <see cref="Build"/>: the navmesh is baked from that soup, and units must not path onto the apron.</para>
        /// </summary>
        public static void BuildApron(TerrainWorld world, out Vector3[] verts, out int[] indices, float cellSize = 0f, float extent = 4000f)
        {
            if (cellSize <= 0f) cellSize = DefaultCellSize(world);
            var (min, max, cell, nx, ny) = GroundLayout(world, cellSize);
            // the edge's coordinates, exactly AddGround's: min + i·cell, the last one clipped to max
            float X(int i) => MathF.Min(min.X + i * cell, max.X);
            float Y(int j) => MathF.Min(min.Y + j * cell, max.Y);
            var v = new List<Vector3>();
            var t = new List<int>();
            Vector3 P(float x, float y) => new(x, y, world.GroundHeightAt(x, y));
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)   // counter-clockwise seen from above
            {
                int s = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                t.Add(s); t.Add(s + 1); t.Add(s + 2);
                t.Add(s); t.Add(s + 2); t.Add(s + 3);
            }
            float x0 = min.X, x1 = max.X, y0 = min.Y, y1 = max.Y, e = extent;
            for (int i = 0; i < nx; i++)
            {
                float a = X(i), b = X(i + 1);
                Quad(P(a, y0 - e), P(b, y0 - e), P(b, y0), P(a, y0));   // south
                Quad(P(a, y1), P(b, y1), P(b, y1 + e), P(a, y1 + e));   // north
            }
            for (int j = 0; j < ny; j++)
            {
                float a = Y(j), b = Y(j + 1);
                Quad(P(x0 - e, a), P(x0, a), P(x0, b), P(x0 - e, b));   // west
                Quad(P(x1, a), P(x1 + e, a), P(x1 + e, b), P(x1, b));   // east
            }
            Quad(P(x0 - e, y0 - e), P(x0, y0 - e), P(x0, y0), P(x0 - e, y0));   // the four corners
            Quad(P(x1, y0 - e), P(x1 + e, y0 - e), P(x1 + e, y0), P(x1, y0));
            Quad(P(x1, y1), P(x1 + e, y1), P(x1 + e, y1 + e), P(x1, y1 + e));
            Quad(P(x0 - e, y1), P(x0, y1), P(x0, y1 + e), P(x0 - e, y1 + e));
            verts = v.ToArray();
            indices = t.ToArray();
        }

        private static void AddGround(TerrainWorld world, float cell, List<Vector3> v, List<int> t, List<TerrainTriangleTag> g)
        {
            var (min, max, cellSize, nx, ny) = GroundLayout(world, cell);
            cell = cellSize;

            for (int iy = 0; iy < ny; iy++)
            for (int ix = 0; ix < nx; ix++)
            {
                float x0 = min.X + (ix * cell), y0 = min.Y + (iy * cell);
                float x1 = MathF.Min(x0 + cell, max.X), y1 = MathF.Min(y0 + cell, max.Y);
                if (Excluded(world, new Vector2((x0 + x1) * 0.5f, (y0 + y1) * 0.5f))) continue;

                int b = v.Count;
                v.Add(new Vector3(x0, y0, world.GroundHeightAt(x0, y0)));
                v.Add(new Vector3(x1, y0, world.GroundHeightAt(x1, y0)));
                v.Add(new Vector3(x1, y1, world.GroundHeightAt(x1, y1)));
                v.Add(new Vector3(x0, y1, world.GroundHeightAt(x0, y1)));
                t.Add(b); t.Add(b + 1); t.Add(b + 2);
                t.Add(b); t.Add(b + 2); t.Add(b + 3);
                var tag = new TerrainTriangleTag(TerrainTriangleKind.Ground,
                    InForest(world, new Vector2((x0 + x1) * 0.5f, (y0 + y1) * 0.5f)) ? ForestMaterial : null);
                g.Add(tag); g.Add(tag);
            }
        }

        /// <summary>The material tag of ground under a forest surface.</summary>
        public const string ForestMaterial = "forest";

        private static bool InForest(TerrainWorld world, Vector2 p)
        {
            foreach (var s in world.Surfaces)
                if (s.Type == TerrainSurfaceType.Forest && InBox(p, s.Min, s.Max) && PolygonMath.Contains(s.Polygon, p)) return true;
            return false;
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

        /// <summary>
        /// ⭐ <c>CE-1165</c> (civilian U-4, docs/DESIGN_Civilians_And_Crowd_Control.md §8 U4-b) — a prism of a material that lets sight through
        /// AND does not block movement (a tear-gas or smoke cloud's obstacle, B1-c) is a VOLUME, not a solid: it is left out of the mesh — so
        /// it neither renders as a block nor enters the navmesh (it blocks nothing) — and its own layer draws it. Sight still reads the
        /// prism itself. ⚠ Every built-in see-through material today (glass, hedge, chain-link fence) blocks movement, so none is affected.
        /// </summary>
        public static bool IsVolumeEffect(TerrainPrism prism) => prism.Material is { SightTransmittance: > 0f, BlocksMovement: false };

        private static void AddPrism(TerrainPrism prism, List<Vector3> v, List<int> t, List<TerrainTriangleTag> g)
        {
            var fp = prism.Footprint;
            int n = fp.Length;
            if (n < 3) return;
            string material = prism.Material?.Name ?? TerrainMaterialLibrary.DefaultMaterial;

            // Roof.
            int roof = v.Count;
            foreach (var p in fp) v.Add(new Vector3(p, prism.TopZ));
            for (int i = 0; i + 2 < prism.Triangles.Length; i += 3)
            {
                AddUp(v, t, roof + prism.Triangles[i], roof + prism.Triangles[i + 1], roof + prism.Triangles[i + 2]);
                g.Add(new TerrainTriangleTag(TerrainTriangleKind.Roof, material));
            }

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
                var wall = new TerrainTriangleTag(TerrainTriangleKind.Wall, material);
                g.Add(wall); g.Add(wall);
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

    /// <summary>⭐ CE-1033 S4 — what a terrain mesh triangle is.</summary>
    public enum TerrainTriangleKind : byte { Ground = 0, Wall = 1, Roof = 2, Slab = 3 }

    /// <summary>⭐ CE-1033 S4 — a terrain mesh triangle's kind and material name (null = plain ground).</summary>
    public readonly record struct TerrainTriangleTag(TerrainTriangleKind Kind, string? Material);
}
