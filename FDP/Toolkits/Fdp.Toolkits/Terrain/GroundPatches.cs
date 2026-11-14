using System;
using System.Collections.Generic;
using System.Numerics;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>What a <see cref="GroundPatch"/> does to the ground.</summary>
    public enum GroundPatchMode : byte
    {
        /// <summary>⭐ G-2 / G-3 — a house's PAD: the ground under the footprint is CUT down to <see cref="GroundPatch.Z"/> (never filled), a skirt
        /// blends it up to the natural ground; the cut is capped at <see cref="GroundPatch.MaxCut"/>.</summary>
        Pad = 0,
    }

    /// <summary>
    /// ⭐ <c>CE-4403</c> (P-1; 📄 docs/DESIGN_Ground_Patches_And_Road_Blockage.md §3.1, §4 G-1…G-4, §9.2 V-16) — one change to the ground, in the terrain's
    /// local metres: a polygon, the level it sets, the skirt that blends it into the natural ground and the deepest cut it may make. Immutable data; the
    /// rule that applies it is <see cref="GroundPatches"/> — ONE function for import, load and runtime (G-1).
    /// </summary>
    public readonly record struct GroundPatch(GroundPatchMode Mode, Vector2[] Polygon, float Z, float Skirt, float MaxCut)
    {
        /// <summary>The plan box the patch can change: the polygon's box grown by the skirt.</summary>
        public (Vector2 Min, Vector2 Max) Bounds
        {
            get
            {
                var min = new Vector2(float.MaxValue); var max = new Vector2(float.MinValue);
                foreach (var p in Polygon) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
                var s = new Vector2(Skirt);
                return (min - s, max + s);
            }
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4403</c> (P-1; 📄 docs/DESIGN_Ground_Patches_And_Road_Blockage.md §4 G-1…G-4, §9.2 V-16, R-4228) — THE ground-patch rule. A patch only
    /// LOWERS the ground (a pad cuts, never fills — G-3), so several patches combine by the minimum: commutative, the same result in any order, on every
    /// node. Two entry points over ONE per-post rule (<see cref="Target"/>): <see cref="ApplyTo"/> rewrites a row-major height array in place (the importer,
    /// before the store is written — thousands of houses, no tile copies) and <see cref="Apply"/> returns a new grid sharing every untouched tile
    /// (<see cref="TerrainHeightGrid.Patched"/> — a scenario pad, a crater).
    /// </summary>
    public static class GroundPatches
    {
        /// <summary>G-3 — the pad's skirt at least this wide (m).</summary>
        public const float MinSkirt = 4f;

        /// <summary>G-3 — the deepest a pad cuts into a slope (m): a steeper site stays partly cut into it.</summary>
        public const float DefaultMaxCut = 3f;

        /// <summary>⭐ V-16 — a skirt is at least this many times the cut it blends out: the smoothstep's steepest slope is 1.5 × depth ÷ width, so 1.5 keeps
        /// it at ≤ 45° (infantry walk 60°). ⛔ A 4 m skirt under a 3 m cut was 48° — and 72° under a 6 m corridor.</summary>
        public const float SkirtPerMetreOfCut = 1.5f;

        /// <summary>
        /// ⭐ G-2 / G-3 — the PAD of a house standing at <paramref name="baseZ"/> on <paramref name="footprint"/>: its level is the house's own base (the lowest
        /// ground under it — TH-C, so no house, door or saved scenario moves), its skirt wide enough to stay walkable for the deepest cut under the footprint
        /// (<paramref name="ground"/>, capped by <see cref="DefaultMaxCut"/>).
        /// </summary>
        public static GroundPatch PadOf(IReadOnlyList<Vector2> footprint, float baseZ, Func<float, float, float> ground)
        {
            var poly = new Vector2[footprint.Count];
            float highest = baseZ;
            for (int i = 0; i < poly.Length; i++) { poly[i] = footprint[i]; highest = MathF.Max(highest, ground(poly[i].X, poly[i].Y)); }
            float cut = MathF.Min(highest - baseZ, DefaultMaxCut);
            return new GroundPatch(GroundPatchMode.Pad, poly, baseZ, MathF.Max(MinSkirt, SkirtPerMetreOfCut * cut), DefaultMaxCut);
        }

        /// <summary>
        /// The height <paramref name="patch"/> asks for at (<paramref name="x"/>, <paramref name="y"/>) where the ground is <paramref name="z"/> — never above
        /// <paramref name="z"/>. Inside the polygon: the pad level, but no deeper than <see cref="GroundPatch.MaxCut"/>; in the skirt: a smoothstep from it to the
        /// natural ground; beyond: <paramref name="z"/>. Pure, allocation-free: + − × ÷ and √ only (V-10 — the same answer on every machine).
        /// </summary>
        public static float Target(in GroundPatch patch, float x, float y, float z)
        {
            if (z <= patch.Z) return z;   // never fill
            float d = BreachRule.DistanceToFootprint(patch.Polygon, new Vector2(x, y));
            if (d >= patch.Skirt) return z;
            float floor = MathF.Max(patch.Z, z - patch.MaxCut);
            if (d <= 0f) return floor;
            float t = d / patch.Skirt;
            float s = t * t * (3f - 2f * t);   // smoothstep: 0 at the footprint, 1 at the skirt's edge
            return MathF.Min(z, floor + (z - floor) * s);
        }

        /// <summary>
        /// Applies <paramref name="patches"/> IN PLACE to the row-major heights <paramref name="z"/> (<paramref name="cols"/> × <paramref name="rows"/>, row 0 =
        /// south, post (c, r) at <paramref name="origin"/> + (c, r) × <paramref name="cell"/>). Each patch visits only the posts of its box. Returns the posts
        /// lowered.
        /// </summary>
        public static long ApplyTo(float[] z, int cols, int rows, Vector2 origin, float cell, IReadOnlyList<GroundPatch> patches)
        {
            // ⭐ every patch is measured against the NATURAL ground, never against one already applied: min over the patches is then order-free
            //   (📌 measured: applied one after another, a skirt blended toward the ground the other pad had already cut — 0.11 m apart by order)
            var natural = patches.Count > 1 ? (float[])z.Clone() : z;   // 4 B a post (a dictionary of the touched posts cost ~6×)
            long changed = 0;
            for (int k = 0; k < patches.Count; k++)
            {
                var p = patches[k];
                var (c0, r0, c1, r1) = Posts(p, cols, rows, origin, cell);
                for (int r = r0; r <= r1; r++)
                {
                    float y = origin.Y + r * cell;
                    for (int c = c0; c <= c1; c++)
                    {
                        long i = (long)r * cols + c;
                        float n = natural[i];
                        float t = Target(in p, origin.X + c * cell, y, n);
                        if (t < z[i]) { if (z[i] == n) changed++; z[i] = t; }
                    }
                }
            }
            return changed;
        }

        /// <summary>A NEW grid with <paramref name="patches"/> applied; <paramref name="grid"/> is unchanged and shares every tile no patch touches (G-6).</summary>
        public static TerrainHeightGrid Apply(TerrainHeightGrid grid, IReadOnlyList<GroundPatch> patches)
        {
            var g = grid;
            var origin = grid.Origin; float cell = grid.CellSize;
            for (int k = 0; k < patches.Count; k++)
            {
                var p = patches[k];
                var (c0, r0, c1, r1) = Posts(p, g.Cols, g.Rows, g.Origin, g.CellSize);
                if (c0 > c1 || r0 > r1) continue;
                // against the NATURAL ground (the grid passed in), the minimum with what earlier patches made — order-free, as ApplyTo
                g = g.Patched(c0, r0, c1, r1, (c, r, z) => MathF.Min(z, Target(in p, origin.X + c * cell, origin.Y + r * cell, grid[c, r])));
            }
            return g;
        }

        private static (int C0, int R0, int C1, int R1) Posts(in GroundPatch p, int cols, int rows, Vector2 origin, float cell)
        {
            var (min, max) = p.Bounds;
            int c0 = Math.Max(0, (int)MathF.Floor((min.X - origin.X) / cell)), r0 = Math.Max(0, (int)MathF.Floor((min.Y - origin.Y) / cell));
            int c1 = Math.Min(cols - 1, (int)MathF.Ceiling((max.X - origin.X) / cell)), r1 = Math.Min(rows - 1, (int)MathF.Ceiling((max.Y - origin.Y) / cell));
            return (c0, r0, c1, r1);
        }
    }
}
