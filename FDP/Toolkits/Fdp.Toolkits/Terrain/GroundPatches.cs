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
            => ApplyTo(z, cols, rows, origin, cell, patches, null);

        /// <inheritdoc cref="ApplyTo(float[], int, int, Vector2, float, IReadOnlyList{GroundPatch})"/>
        /// <param name="locked">⭐ G-24 — posts no patch may change (a road corridor's), or null.</param>
        public static long ApplyTo(float[] z, int cols, int rows, Vector2 origin, float cell, IReadOnlyList<GroundPatch> patches, bool[]? locked)
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
                        if (locked != null && locked[i]) continue;   // ⭐ G-24 — a road corridor is never altered by a later patch
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

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ <c>CE-4403</c> (P-1 part 2; 📄 docs/DESIGN_Ground_Patches_And_Road_Blockage.md §4b G-21…G-25, D-3, R-4231, R-4234) — one road EDGE's corridor:
    /// its reference line (terrain-local), the half-width it levels (all its lanes + a 1 m shoulder) and its two end heights (the junctions' — V-22).
    /// </summary>
    public readonly record struct RoadCorridor(Vector2[] Line, float HalfWidth, float StartZ, float EndZ);

    /// <summary>What <see cref="RoadCorridors.Level"/> did (the importer's report).</summary>
    public sealed class CorridorReport
    {
        public int Corridors, Junctions;
        public long PostsLevelled, PostsKeptNatural, PostsUnderHouses;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4403</c> (P-1 part 2; 📄 §4b G-21…G-25, §9.2 V-16, V-22, §9.4 D-3; R-4231, R-4234) — roads LEVEL the ground under them at import: per edge
    /// the ground along its line SMOOTHED over <see cref="SmoothMetres"/> (the DEM's ripple out, the grade kept), pinned to its junctions at both ends (V-22),
    /// LEVEL ACROSS its half-width; a <see cref="Skirt"/> blends it into the natural ground (a cut uphill, a fill downhill); a cut or fill deeper than
    /// <see cref="MaxDepth"/> keeps the natural ground (G-23 — and the guard against filling the valley under a bridge the net does not mark, V-15); a junction
    /// outline is flat at its height. Where corridors overlap the NEAREST road wins (a junction over every corridor). ⭐ D-3 — a post inside a house footprint
    /// is never levelled (the road keeps its natural cross-slope there; counted, never an error). Returns the LOCK mask (G-24): the posts a road levelled,
    /// which no later patch (a pad's skirt) may change. In place, + − × ÷ √ only.
    /// </summary>
    public static class RoadCorridors
    {
        /// <summary>G-21 — the profile's smoothing window (m).</summary>
        public const float SmoothMetres = 30f;
        /// <summary>G-21 — the corridor's skirt (m); widened to keep the bank ≤ 45° (V-16).</summary>
        public const float Skirt = 3f;
        /// <summary>G-23 — the deepest cut / highest fill a corridor makes (m).</summary>
        public const float MaxDepth = 6f;
        /// <summary>G-21 — the shoulder beyond the lanes (m).</summary>
        public const float Shoulder = 1f;

        public static bool[] Level(float[] z, int cols, int rows, Vector2 origin, float cell, IReadOnlyList<RoadCorridor> corridors,
                                   IReadOnlyList<(Vector2[] Polygon, float Z)> junctions, IReadOnlyList<Vector2[]> houses, CorridorReport report)
        {
            var natural = (float[])z.Clone();
            float Ground(float x, float y)
            {
                int c = Math.Clamp((int)MathF.Round((x - origin.X) / cell), 0, cols - 1), r = Math.Clamp((int)MathF.Round((y - origin.Y) / cell), 0, rows - 1);
                return natural[(long)r * cols + c];
            }
            var best = new float[z.Length];             // the distance to the road that decided the post (+∞ none, −1 a junction)
            Array.Fill(best, float.PositiveInfinity);
            var locked = new bool[z.Length];
            var underHouse = HouseMask(cols, rows, origin, cell, houses);

            foreach (var (poly, jz) in junctions)
            {
                if (poly.Length < 3) continue;
                report.Junctions++;
                var (c0, r0, c1, r1) = Box(poly, 0f, cols, rows, origin, cell);
                for (int r = r0; r <= r1; r++)
                    for (int c = c0; c <= c1; c++)
                    {
                        long i = (long)r * cols + c;
                        var p = new Vector2(origin.X + c * cell, origin.Y + r * cell);
                        if (!PolygonMath.Contains(poly, p)) continue;
                        if (underHouse[i]) { report.PostsUnderHouses++; continue; }
                        if (MathF.Abs(jz - natural[i]) > MaxDepth) { report.PostsKeptNatural++; continue; }
                        z[i] = jz; best[i] = -1f; locked[i] = true;
                    }
            }

            foreach (var road in corridors)
            {
                if (road.Line.Length < 2) continue;
                report.Corridors++;
                var (sAt, prof) = Profile(road, Ground);
                float depth = 0f;
                for (int k = 0; k < sAt.Length; k++) depth = MathF.Max(depth, MathF.Abs(prof[k] - Ground(PointAt(road.Line, sAt[k]).X, PointAt(road.Line, sAt[k]).Y)));
                float skirt = MathF.Max(Skirt, GroundPatches.SkirtPerMetreOfCut * MathF.Min(depth, MaxDepth));
                var (c0, r0, c1, r1) = Box(road.Line, road.HalfWidth + skirt, cols, rows, origin, cell);
                for (int r = r0; r <= r1; r++)
                    for (int c = c0; c <= c1; c++)
                    {
                        long i = (long)r * cols + c;
                        var p = new Vector2(origin.X + c * cell, origin.Y + r * cell);
                        float d = Nearest(road.Line, p, out float s);
                        if (d >= road.HalfWidth + skirt || d >= best[i]) continue;
                        float level = Interp(sAt, prof, s);
                        float n = natural[i];
                        if (underHouse[i]) { if (d <= road.HalfWidth) report.PostsUnderHouses++; continue; }
                        if (MathF.Abs(level - n) > MaxDepth) { if (d <= road.HalfWidth) report.PostsKeptNatural++; continue; }
                        if (d <= road.HalfWidth)
                        {
                            z[i] = level; locked[i] = true;
                        }
                        else
                        {
                            float t = (d - road.HalfWidth) / skirt, sm = t * t * (3f - 2f * t);
                            z[i] = level + (n - level) * sm; locked[i] = false;
                        }
                        best[i] = d;
                    }
            }
            for (long i = 0; i < z.Length; i++) if (locked[i]) report.PostsLevelled++;
            return locked;
        }

        /// <summary>The smoothed, end-pinned profile along <paramref name="road"/>'s line, sampled every metre: (arc length, z).</summary>
        internal static (float[] S, float[] Z) Profile(in RoadCorridor road, Func<float, float, float> ground)
        {
            float total = 0f;
            for (int i = 0; i + 1 < road.Line.Length; i++) total += Vector2.Distance(road.Line[i], road.Line[i + 1]);
            int n = Math.Max(2, (int)MathF.Ceiling(total) + 1);
            var s = new float[n]; var raw = new float[n];
            for (int k = 0; k < n; k++)
            {
                s[k] = total * k / (n - 1);
                var p = PointAt(road.Line, s[k]);
                raw[k] = ground(p.X, p.Y);
            }
            // a moving average over SmoothMetres (prefix sums), then the ends pulled to the junctions over half the window (V-22)
            var prefix = new double[n + 1];
            for (int k = 0; k < n; k++) prefix[k + 1] = prefix[k] + raw[k];
            int half = Math.Max(1, (int)(SmoothMetres / 2f * (n - 1) / MathF.Max(total, 1e-3f)));
            var z = new float[n];
            for (int k = 0; k < n; k++)
            {
                int a = Math.Max(0, k - half), b = Math.Min(n - 1, k + half);
                z[k] = (float)((prefix[b + 1] - prefix[a]) / (b - a + 1));
            }
            float ramp = MathF.Min(SmoothMetres / 2f, total / 2f);
            float e0 = road.StartZ - z[0], e1 = road.EndZ - z[n - 1];
            for (int k = 0; k < n; k++)
            {
                float w0 = ramp > 0f ? MathF.Max(0f, 1f - s[k] / ramp) : 0f, w1 = ramp > 0f ? MathF.Max(0f, 1f - (total - s[k]) / ramp) : 0f;
                z[k] += e0 * w0 + e1 * w1;
            }
            return (s, z);
        }

        private static float Interp(float[] s, float[] z, float at)
        {
            if (at <= s[0]) return z[0];
            if (at >= s[^1]) return z[^1];
            float step = s[1] - s[0];
            int k = Math.Min(s.Length - 2, (int)(at / step));
            float t = (at - s[k]) / MathF.Max(s[k + 1] - s[k], 1e-6f);
            return z[k] + (z[k + 1] - z[k]) * t;
        }

        internal static Vector2 PointAt(Vector2[] line, float s)
        {
            for (int i = 0; i + 1 < line.Length; i++)
            {
                float len = Vector2.Distance(line[i], line[i + 1]);
                if (s <= len || i + 2 == line.Length) return len > 1e-6f ? line[i] + (line[i + 1] - line[i]) * MathF.Min(1f, s / len) : line[i];
                s -= len;
            }
            return line[^1];
        }

        /// <summary>The distance from <paramref name="p"/> to the polyline and the arc length <paramref name="s"/> of its nearest point.</summary>
        internal static float Nearest(Vector2[] line, Vector2 p, out float s)
        {
            float best = float.MaxValue, acc = 0f; s = 0f;
            for (int i = 0; i + 1 < line.Length; i++)
            {
                var a = line[i]; var ab = line[i + 1] - a;
                float len2 = ab.LengthSquared(), len = MathF.Sqrt(len2);
                float t = len2 > 1e-12f ? Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f) : 0f;
                float d = Vector2.Distance(p, a + ab * t);
                if (d < best) { best = d; s = acc + t * len; }
                acc += len;
            }
            return best;
        }

        private static bool[] HouseMask(int cols, int rows, Vector2 origin, float cell, IReadOnlyList<Vector2[]> houses)
        {
            var m = new bool[(long)cols * rows];
            foreach (var h in houses)
            {
                if (h.Length < 3) continue;
                var (c0, r0, c1, r1) = Box(h, 0f, cols, rows, origin, cell);
                for (int r = r0; r <= r1; r++)
                    for (int c = c0; c <= c1; c++)
                        if (PolygonMath.Contains(h, new Vector2(origin.X + c * cell, origin.Y + r * cell))) m[(long)r * cols + c] = true;
            }
            return m;
        }

        private static (int C0, int R0, int C1, int R1) Box(Vector2[] pts, float grow, int cols, int rows, Vector2 origin, float cell)
        {
            var min = new Vector2(float.MaxValue); var max = new Vector2(float.MinValue);
            foreach (var p in pts) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            min -= new Vector2(grow); max += new Vector2(grow);
            return (Math.Max(0, (int)MathF.Floor((min.X - origin.X) / cell)), Math.Max(0, (int)MathF.Floor((min.Y - origin.Y) / cell)),
                    Math.Min(cols - 1, (int)MathF.Ceiling((max.X - origin.X) / cell)), Math.Min(rows - 1, (int)MathF.Ceiling((max.Y - origin.Y) / cell)));
        }
    }
}
