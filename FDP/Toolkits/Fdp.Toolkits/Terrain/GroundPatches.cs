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

        /// <summary>
        /// ⭐⭐ <c>CE-4592</c> (P-5, G-24 / G-29; 📄 §4b G-24, §9.12) — a scenario's ground change RE-LEVELS the road corridors it touched, with the import's profile
        /// (<see cref="Profile"/>: smoothed along, level across, pinned at both ends to the ground there now — so two edges meeting at a junction agree) on the CHANGED
        /// ground; the <see cref="Skirt"/> blends back to it; a post inside an island (a house + its margin) or deeper than <see cref="MaxDepth"/> is kept. A ditch
        /// strip under the corridor is therefore filled back: the ditch stops at the road (G-29). Null / none ⇒ <paramref name="grid"/> itself. + − × ÷ √ only.
        /// </summary>
        public static TerrainHeightGrid? Relevel(TerrainHeightGrid? grid, IReadOnlyList<RoadCorridor> corridors, IReadOnlyList<Vector2[]>? islands)
        {
            if (grid == null || corridors.Count == 0) return grid;
            var g = grid;
            foreach (var c in corridors)
            {
                if (c.Line.Length < 2) continue;
                var cur = g;
                var road = c with { StartZ = cur.Sample(c.Line[0].X, c.Line[0].Y), EndZ = cur.Sample(c.Line[^1].X, c.Line[^1].Y) };
                var (sAt, prof) = Profile(road, cur.Sample);
                var origin = cur.Origin; float cell = cur.CellSize;
                var (c0, r0, c1, r1) = Box(road.Line, road.HalfWidth + Skirt, cur.Cols, cur.Rows, origin, cell);
                var lo = new Vector2(origin.X + c0 * cell, origin.Y + r0 * cell) - new Vector2(GroundShapes.IslandMargin);
                var hi = new Vector2(origin.X + c1 * cell, origin.Y + r1 * cell) + new Vector2(GroundShapes.IslandMargin);
                List<Vector2[]>? near = null;   // only the houses this corridor's box reaches (a large terrain has thousands)
                if (islands != null)
                    foreach (var h in islands)
                    {
                        if (h.Length < 3) continue;
                        var hmin = new Vector2(float.MaxValue); var hmax = new Vector2(float.MinValue);
                        foreach (var q in h) { hmin = Vector2.Min(hmin, q); hmax = Vector2.Max(hmax, q); }
                        if (hmax.X >= lo.X && hmin.X <= hi.X && hmax.Y >= lo.Y && hmin.Y <= hi.Y) (near ??= new List<Vector2[]>()).Add(h);
                    }
                g = cur.Patched(c0, r0, c1, r1, (col, row, z) =>
                {
                    var p = new Vector2(origin.X + col * cell, origin.Y + row * cell);
                    float d = Nearest(road.Line, p, out float s);
                    if (d >= road.HalfWidth + Skirt) return z;
                    if (near != null)
                        foreach (var h in near)
                            if (PolygonMath.Contains(h, p) || GroundShapes.DistanceTo(h, p) <= GroundShapes.IslandMargin) return z;
                    float level = Interp(sAt, prof, s);
                    if (MathF.Abs(level - z) > MaxDepth) return z;
                    if (d <= road.HalfWidth) return level;
                    float t = (d - road.HalfWidth) / Skirt, sm = t * t * (3f - 2f * t);
                    return level + (z - level) * sm;
                });
            }
            return g;
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

namespace Fdp.Toolkit.Terrain
{
    /// <summary>⭐ <c>CE-4592</c> (P-5, G-14) — what a ground shape does inside its polygon.</summary>
    public enum GroundShapeMode : byte
    {
        /// <summary>The ground at <see cref="GroundShape.Value"/> (cut and fill).</summary>
        Level = 0,
        /// <summary>The natural ground + <see cref="GroundShape.Value"/>.</summary>
        Raise = 1,
        /// <summary>The natural ground − <see cref="GroundShape.Value"/>.</summary>
        Lower = 2,
        /// <summary>⭐ CE-4593 (P-6, G-26) — a DITCH / foxhole strip: the natural ground − <see cref="GroundShape.Value"/> with STEEP banks (a skirt of 0.6 × the depth + 0.3 m — infantry's 60° walls, G-38).</summary>
        Ditch = 3,
    }

    /// <summary>⭐ <c>CE-4592</c> — one authored ground shape: its mode, polygon (terrain-local), value and DRAW ORDER (the newest wins where shapes overlap, V-28).</summary>
    public readonly record struct GroundShape(GroundShapeMode Mode, Vector2[] Polygon, float Value, uint Order, string Label)
    {
        /// <summary>⭐ CE-4593 (G-28, D-2) — an OBSTACLE-class shape (a ditch): a navmesh area every vehicle keeps out of.</summary>
        public bool Obstacle { get; init; }
    }

    /// <summary>
    /// ⭐ <c>CE-4593</c> (P-6; 📄 docs/DESIGN_Ground_Patches_And_Road_Blockage.md §4c G-26…G-28, §4c-1 G-36…G-38) — the ditch PRESETS: a ditch 2 m wide × 1 m deep, an anti-tank
    /// ditch 4 m × 2 m, a foxhole 2 m long × 1.5 m wide × 1 m deep (a click and a heading). A shape's own depth (its <c>Pad</c>) overrides the preset's; the width is the
    /// preset's (R-5027: no width field until the sibling component).
    /// </summary>
    public static class DitchPresets
    {
        public static (float Width, float Depth, bool Obstacle)? Of(string mode) => mode switch
        {
            "ditch" => (2f, 1f, true),
            "at-ditch" => (4f, 2f, true),
            "foxhole" => (1.5f, 1f, false),
            _ => null,
        };

        /// <summary>The foxhole's length along its heading (m).</summary>
        public const float FoxholeLength = 2f;

        /// <summary>The quads of a ditch <paramref name="width"/> wide along <paramref name="line"/> (one per segment, squared ends a half width past each joint so they overlap).</summary>
        public static List<Vector2[]> Strip(IReadOnlyList<Vector2> line, float width)
        {
            var quads = new List<Vector2[]>();
            float h = width * 0.5f;
            for (int i = 0; i + 1 < line.Count; i++)
            {
                var a = line[i]; var b = line[i + 1];
                var d = b - a; float len = d.Length();
                if (len < 1e-3f) continue;
                d /= len;
                var n = new Vector2(-d.Y, d.X) * h;
                var a2 = i > 0 ? a - d * h : a; var b2 = i + 2 < line.Count ? b + d * h : b;
                quads.Add(new[] { a2 + n, a2 - n, b2 - n, b2 + n });
            }
            return quads;
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4592</c> (P-5; 📄 docs/DESIGN_Ground_Patches_And_Road_Blockage.md §4a G-14, G-18, §9.3 V-28) — THE ground-shape rule, beside the pads' (<see cref="GroundPatches"/>,
    /// which only cut): a shape LEVELS, RAISES or LOWERS the ground inside its polygon and blends back to the natural ground over a skirt (G-3's walkable rule: at least
    /// <see cref="GroundPatches.MinSkirt"/>, 1.5 × the largest change); shapes apply in DRAW ORDER (the newest wins where they overlap — V-28: the author sees which); every
    /// building footprint (+ <see cref="IslandMargin"/>) is an ISLAND the shape leaves alone (it never pushes the ground into a house — G-18). Returns a new grid sharing every
    /// untouched tile. + − × ÷ √ only (V-10).
    /// </summary>
    public static class GroundShapes
    {
        /// <summary>The margin round a building a shape leaves at the natural ground (m).</summary>
        public const float IslandMargin = 1f;

        public static GroundShapeMode ModeOf(string mode, string where) => mode switch
        {
            "level" => GroundShapeMode.Level,
            "raise" or "" => GroundShapeMode.Raise,
            "lower" => GroundShapeMode.Lower,
            "ditch" or "at-ditch" or "foxhole" => GroundShapeMode.Ditch,   // ⭐ CE-4593 (P-6)
            _ => throw new ArgumentException($"[Terrain] {where}: unknown ground mode '{mode}' (level, raise, lower, ditch, at-ditch, foxhole)."),
        };

        /// <summary>The target height a shape gives a post whose natural ground is <paramref name="natural"/>.</summary>
        public static float Target(in GroundShape s, float natural) => s.Mode switch
        {
            GroundShapeMode.Level => s.Value,
            GroundShapeMode.Raise => natural + s.Value,
            _ => natural - s.Value,   // lower, ditch
        };

        /// <summary>The shape's skirt over <paramref name="grid"/>: ≥ 4 m and 1.5 × the largest change inside it.</summary>
        public static float SkirtOf(in GroundShape s, TerrainHeightGrid grid)
        {
            if (s.Mode == GroundShapeMode.Ditch) return 0.6f * MathF.Abs(s.Value) + 0.3f;   // ⭐ CE-4593 — steep banks
            if (s.Mode != GroundShapeMode.Level) return MathF.Max(GroundPatches.MinSkirt, GroundPatches.SkirtPerMetreOfCut * MathF.Abs(s.Value));
            var (min, max) = Box(s.Polygon);
            float change = 0f;
            for (float y = min.Y; y <= max.Y; y += grid.CellSize)
                for (float x = min.X; x <= max.X; x += grid.CellSize)
                    if (PolygonMath.Contains(s.Polygon, new Vector2(x, y))) change = MathF.Max(change, MathF.Abs(grid.Sample(x, y) - s.Value));
            return MathF.Max(GroundPatches.MinSkirt, GroundPatches.SkirtPerMetreOfCut * change);
        }

        /// <summary>A NEW grid with <paramref name="shapes"/> applied in draw order (null / none ⇒ <paramref name="grid"/> itself).</summary>
        public static TerrainHeightGrid? Apply(TerrainHeightGrid? grid, IReadOnlyList<GroundShape> shapes, IReadOnlyList<Vector2[]>? islands)
        {
            if (grid == null || shapes.Count == 0) return grid;
            var ordered = new List<GroundShape>(shapes);
            ordered.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Label, b.Label));
            var skirts = new float[ordered.Count];
            var min = new Vector2(float.MaxValue); var max = new Vector2(float.MinValue);
            for (int i = 0; i < ordered.Count; i++)
            {
                skirts[i] = SkirtOf(ordered[i], grid);
                var (a, b) = Box(ordered[i].Polygon);
                min = Vector2.Min(min, a - new Vector2(skirts[i])); max = Vector2.Max(max, b + new Vector2(skirts[i]));
            }
            var origin = grid.Origin; float cell = grid.CellSize;
            int c0 = (int)MathF.Floor((min.X - origin.X) / cell), r0 = (int)MathF.Floor((min.Y - origin.Y) / cell);
            int c1 = (int)MathF.Ceiling((max.X - origin.X) / cell), r1 = (int)MathF.Ceiling((max.Y - origin.Y) / cell);
            var near = new List<Vector2[]>();
            if (islands != null)
                foreach (var f in islands)
                {
                    var (a, b) = Box(f);
                    if (b.X + IslandMargin >= min.X && a.X - IslandMargin <= max.X && b.Y + IslandMargin >= min.Y && a.Y - IslandMargin <= max.Y) near.Add(f);
                }
            return grid.Patched(c0, r0, c1, r1, (c, r, z) =>
            {
                var p = new Vector2(origin.X + c * cell, origin.Y + r * cell);
                foreach (var f in near)
                    if (PolygonMath.Contains(f, p) || DistanceToPolygon(f, p) <= IslandMargin) return z;   // an island: the house's ground stays
                float natural = z, h = z;
                for (int i = 0; i < ordered.Count; i++)
                {
                    var s = ordered[i];
                    float w = Weight(s.Polygon, p, skirts[i]);
                    if (w <= 0f) continue;
                    h = h + (Target(in s, natural) - h) * w;   // newest last: it overrides where it reaches fully
                }
                return h;
            });
        }

        /// <summary>The distance from <paramref name="p"/> to <paramref name="poly"/>'s edges (0 on an edge).</summary>
        public static float DistanceTo(Vector2[] poly, Vector2 p) => DistanceToPolygon(poly, p);

        /// <summary>
        /// ⭐ <c>CE-4593</c> (P-6, G-29) — a ditch NEVER cuts a road corridor: every ditch / foxhole strip that reaches a corridor (its half-width + the ditch's banks) is cut
        /// into 1 m slices and the slices that reach are dropped — so neither its height nor its vehicle-closing area crosses the road. A strip clear of every
        /// corridor stays whole; any other shape is returned as it is (the corridor re-level, <see cref="RoadCorridors.Relevel"/>, keeps the road level under it).
        /// </summary>
        public static IReadOnlyList<GroundShape> OffCorridors(IReadOnlyList<GroundShape> shapes, IReadOnlyList<RoadCorridor> corridors)
        {
            if (corridors.Count == 0 || shapes.Count == 0) return shapes;
            List<GroundShape>? kept = null;
            for (int i = 0; i < shapes.Count; i++)
            {
                var s = shapes[i];
                if (s.Mode != GroundShapeMode.Ditch || s.Polygon.Length != 4 || !Reaches(s.Polygon, s, corridors)) { kept?.Add(s); continue; }
                if (kept == null) { kept = new List<GroundShape>(shapes.Count); for (int k = 0; k < i; k++) kept.Add(shapes[k]); }
                var q = s.Polygon;
                float len = Vector2.Distance(q[0], q[3]);
                int n = Math.Max(1, (int)MathF.Ceiling(len));
                for (int k = 0; k < n; k++)
                {
                    float t0 = k / (float)n, t1 = (k + 1) / (float)n;
                    var slice = new[] { Vector2.Lerp(q[0], q[3], t0), Vector2.Lerp(q[1], q[2], t0), Vector2.Lerp(q[1], q[2], t1), Vector2.Lerp(q[0], q[3], t1) };
                    if (!Reaches(slice, s, corridors)) kept.Add(s with { Polygon = slice });
                }
            }
            return (IReadOnlyList<GroundShape>?)kept ?? shapes;
        }

        private static bool Reaches(Vector2[] quad, in GroundShape s, IReadOnlyList<RoadCorridor> corridors)
        {
            float half = 0.5f * MathF.Max(Vector2.Distance(quad[0], quad[1]), Vector2.Distance(quad[0], quad[3])), bank = 0.6f * MathF.Abs(s.Value) + 0.3f;
            var centre = (quad[0] + quad[2]) * 0.5f;
            foreach (var c in corridors)
                if (c.Line.Length >= 2 && RoadCorridors.Nearest(c.Line, centre, out _) < c.HalfWidth + bank + half) return true;
            return false;
        }

        /// <summary>⭐ CE-4592 (G-19) — a road grade above this (a fraction) is warned about: SUMO ignores slope, but our off-road vehicles meet the navmesh's limit.</summary>
        public const float GradeWarning = 0.15f;

        /// <summary>
        /// ⭐⭐ <c>CE-4592</c> (P-5; 📄 §4a G-19) — STEEPNESS IS SHOWN, NOT ENFORCED: the steepest grade (rise ÷ run) along any road segment of <paramref name="roads"/> that passes
        /// within <paramref name="shape"/>'s reach (its polygon + skirt), measured on <paramref name="world"/>'s ground (the merged world WITH the shape) every
        /// <paramref name="stepMetres"/> m. 0 when no road passes. The Details read-out shows it and warns over <see cref="GradeWarning"/>.
        /// </summary>
        public static float SteepestRoadGrade(TerrainWorld world, in global::CarKinem.Road.RoadNetworkBlob roads, in GroundShape shape, float stepMetres = 2f)
        {
            if (!roads.Segments.IsCreated || world.Height == null) return 0f;
            float skirt = SkirtOf(shape, world.Height);
            var (min, max) = Box(shape.Polygon);
            min -= new Vector2(skirt); max += new Vector2(skirt);
            float steepest = 0f;
            for (int i = 0; i < roads.Segments.Length; i++)
            {
                var seg = roads.Segments[i];
                var lo = Vector2.Min(seg.P0, seg.P1); var hi = Vector2.Max(seg.P0, seg.P1);
                if (hi.X < min.X || lo.X > max.X || hi.Y < min.Y || lo.Y > max.Y) continue;
                float length = MathF.Max(seg.Length, Vector2.Distance(seg.P0, seg.P1));
                int n = Math.Max(1, (int)MathF.Ceiling(length / stepMetres));
                var prev = global::CarKinem.Road.RoadGraphNavigator.EvaluateHermite(0f, seg.P0, seg.T0, seg.P1, seg.T1);
                float prevZ = world.GroundHeightAt(prev.X, prev.Y);
                for (int k = 1; k <= n; k++)
                {
                    var p = global::CarKinem.Road.RoadGraphNavigator.EvaluateHermite((float)k / n, seg.P0, seg.T0, seg.P1, seg.T1);
                    float z = world.GroundHeightAt(p.X, p.Y);
                    float run = Vector2.Distance(p, prev);
                    bool inReach = p.X >= min.X && p.X <= max.X && p.Y >= min.Y && p.Y <= max.Y;
                    if (run > 1e-3f && inReach) steepest = MathF.Max(steepest, MathF.Abs(z - prevZ) / run);
                    prev = p; prevZ = z;
                }
            }
            return steepest;
        }

        /// <summary>1 inside the polygon, a smoothstep down to 0 over <paramref name="skirt"/> outside it.</summary>
        private static float Weight(Vector2[] poly, Vector2 p, float skirt)
        {
            if (PolygonMath.Contains(poly, p)) return 1f;
            float d = DistanceToPolygon(poly, p);
            if (d >= skirt) return 0f;
            float t = 1f - d / skirt;
            return t * t * (3f - 2f * t);
        }

        private static float DistanceToPolygon(Vector2[] poly, Vector2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < poly.Length; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Length];
                var ab = b - a; float l2 = ab.LengthSquared();
                float t = l2 > 1e-9f ? Math.Clamp(Vector2.Dot(p - a, ab) / l2, 0f, 1f) : 0f;
                best = MathF.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return best;
        }

        private static (Vector2 Min, Vector2 Max) Box(Vector2[] poly)
        {
            var min = new Vector2(float.MaxValue); var max = new Vector2(float.MinValue);
            foreach (var v in poly) { min = Vector2.Min(min, v); max = Vector2.Max(max, v); }
            return (min, max);
        }
    }
}
