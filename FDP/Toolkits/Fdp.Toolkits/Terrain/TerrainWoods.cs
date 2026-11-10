using System;
using System.Numerics;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ <c>CE-4369</c> (L-7, R-4190 T-2 / T-5, docs/DESIGN_Terrain_World.md §11) — the WOODS GRID: the terrain's forest surfaces rasterised
    /// once into <see cref="Cell"/> m cells, each holding the trees per hectare and the canopy top above the ground. A sight line marches it
    /// beside the ground trace and loses visibility per metre it runs BELOW the canopy, scaled by the cell's density — little in the trunk
    /// zone, much in the crown: <c>transmittance = exp(−Σ density/100 · (trunk m · <see cref="LossPerMetre"/> + crown m · <see cref="CrownLossPerMetre"/>))</c>.
    /// From the air a line crosses the crown, so a man under the trees is hidden and one at the wood's edge is seen (T-5).
    /// <para>The individual trees inside a wood are NOT objects (T-1, T-4) — the grid is the wood. Exact edges are ± one cell (R-4190 Q-V8).
    /// Allocation-free queries (R-220); built once per world.</para>
    /// </summary>
    public sealed class TerrainWoods
    {
        /// <summary>The cell edge (m) — the clustering grid of R-4190 T-2.</summary>
        public const float Cell = 25f;
        /// <summary>A forest polygon with no <c>density</c>: trees per hectare (the synthetic / real woods' mean, R-4190 §9.5).</summary>
        public const float DefaultDensity = 115f;
        /// <summary>A forest polygon with no <c>canopy</c>: the canopy top above the ground (m, R-4190 T-7).</summary>
        public const float DefaultCanopy = 15f;
        /// <summary>
        /// The TRUNK zone (the ground up to <see cref="CrownBase"/> of the canopy height): visibility lost per metre per 100 trees / ha
        /// (natural log units). Chosen so an eye-height line through a wood of 115 / ha drops under <see cref="TerrainWorld.SightThreshold"/>
        /// after ≈ 50 m: a man 20 m inside is seen, 100 m inside is not (the L-7 rail). Tunable data.
        /// </summary>
        public const float LossPerMetre = 0.012f;
        /// <summary>The CROWN (from <see cref="CrownBase"/> to the canopy top): far denser — a line from the air crosses it, an eye-height line
        /// does not. Chosen so a man under 115 / ha is hidden from straight above (9 m of crown) but seen at the wood's edge (T-5).</summary>
        public const float CrownLossPerMetre = 0.08f;
        /// <summary>⭐ R-4190 Q-T2 — a tracked vehicle's top speed inside a wood (m/s, 10 km/h). Tunable.</summary>
        public const float TrackedSpeedCap = 10f / 3.6f;
        /// <summary>Where the crown starts, as a fraction of the canopy height (0.4 ⇒ 6 m of a 15 m canopy).</summary>
        public const float CrownBase = 0.4f;

        private readonly Vector2 _origin;
        private readonly int _nx, _ny;
        private readonly float[] _density;   // trees / ha, 0 = no wood
        private readonly float[] _canopy;    // the canopy top above the ground (m)

        private TerrainWoods(Vector2 origin, int nx, int ny, float[] density, float[] canopy)
        { _origin = origin; _nx = nx; _ny = ny; _density = density; _canopy = canopy; }

        /// <summary>Cells across (west → east) and down (south → north).</summary>
        public int Columns => _nx;
        public int Rows => _ny;

        /// <summary>The woods of <paramref name="world"/>: a cell whose centre lies in a forest surface takes that surface's density and
        /// canopy (the last one listed wins where two overlap); null when the world has no forest.</summary>
        public static TerrainWoods? Build(TerrainWorld world)
        {
            Vector2 lo = new(float.MaxValue), hi = new(float.MinValue);
            bool any = false;
            foreach (var s in world.Surfaces)
                if (s.Type == TerrainSurfaceType.Forest) { lo = Vector2.Min(lo, s.Min); hi = Vector2.Max(hi, s.Max); any = true; }
            if (!any) return null;
            var origin = new Vector2(MathF.Floor(lo.X / Cell) * Cell, MathF.Floor(lo.Y / Cell) * Cell);
            int nx = Math.Max(1, (int)MathF.Ceiling((hi.X - origin.X) / Cell)), ny = Math.Max(1, (int)MathF.Ceiling((hi.Y - origin.Y) / Cell));
            var density = new float[nx * ny];
            var canopy = new float[nx * ny];
            foreach (var s in world.Surfaces)
            {
                if (s.Type != TerrainSurfaceType.Forest) continue;
                int c0 = Math.Max(0, (int)MathF.Floor((s.Min.X - origin.X) / Cell)), c1 = Math.Min(nx - 1, (int)MathF.Floor((s.Max.X - origin.X) / Cell));
                int r0 = Math.Max(0, (int)MathF.Floor((s.Min.Y - origin.Y) / Cell)), r1 = Math.Min(ny - 1, (int)MathF.Floor((s.Max.Y - origin.Y) / Cell));
                for (int r = r0; r <= r1; r++)
                    for (int c = c0; c <= c1; c++)
                    {
                        var centre = origin + new Vector2((c + 0.5f) * Cell, (r + 0.5f) * Cell);
                        if (!PolygonMath.Contains(s.Polygon, centre)) continue;
                        density[r * nx + c] = s.Density;
                        canopy[r * nx + c] = s.Canopy;
                    }
            }
            return new TerrainWoods(origin, nx, ny, density, canopy);
        }

        /// <summary>The trees per hectare at (x, y) — 0 outside every wood.</summary>
        public float DensityAt(float x, float y)
        {
            int c = (int)MathF.Floor((x - _origin.X) / Cell), r = (int)MathF.Floor((y - _origin.Y) / Cell);
            return c < 0 || r < 0 || c >= _nx || r >= _ny ? 0f : _density[r * _nx + c];
        }

        /// <summary>
        /// The fraction of sight that passes the woods on <paramref name="a"/> → <paramref name="b"/> (1 = no wood in the way): the line's
        /// shadow walked cell by cell (Amanatides–Woo over the 25 m grid), and in each wooded cell the metres of the line that lie below that
        /// cell's canopy top (the ground at the cell's centre + its canopy height). <paramref name="metres"/>: the density-weighted metres.
        /// </summary>
        public float Transmittance(Vector3 a, Vector3 b, TerrainWorld world, out float metres)
        {
            March(a, b, world, out float trunk, out metres, out _);
            float loss = trunk * LossPerMetre + (metres - trunk) * CrownLossPerMetre;
            return loss <= 0f ? 1f : MathF.Exp(-loss);
        }

        /// <summary>⭐ R-4190 T-6 — the chance a round is stopped per metre of TRUNK zone, per 100 trees / ha: it meets a trunk
        /// (≈ <see cref="TrunkDiameter"/> m wide) about <c>trees per m² × diameter</c> times per metre ⇒ 100 / 10 000 × 0.3. Most rounds pass.</summary>
        public const float TrunkDiameter = 0.3f;
        public const float FireStopPerMetre = 100f / 10000f * TrunkDiameter;

        /// <summary>
        /// ⭐ <c>CE-4369</c> (S4, R-4190 T-6) — the fraction of rounds the woods stop on <paramref name="a"/> → <paramref name="b"/> (0 = no
        /// wood): <c>1 − exp(−Σ density/100 · trunk m · <see cref="FireStopPerMetre"/>)</c>, independent of the round's penetration (a trunk
        /// stops small arms — a lone trunk is an object, this is the wood's many). <paramref name="tEnter"/>: where the line first enters a wood.
        /// </summary>
        public float FireStop(Vector3 a, Vector3 b, TerrainWorld world, out float tEnter)
        {
            March(a, b, world, out float trunk, out _, out tEnter);
            return trunk <= 0f ? 0f : 1f - MathF.Exp(-trunk * FireStopPerMetre);
        }

        // the ONE march both purposes read: density-weighted metres in the trunk zone and below the canopy top, and the first wooded t
        private void March(Vector3 a, Vector3 b, TerrainWorld world, out float trunkMetres, out float allMetres, out float tFirst)
        {
            trunkMetres = 0f; allMetres = 0f; tFirst = 1f;
            var d = b - a;
            float len = d.Length();
            if (len < 1e-4f) return;
            float gx = (a.X - _origin.X) / Cell, gy = (a.Y - _origin.Y) / Cell;
            float ex = (b.X - _origin.X) / Cell, ey = (b.Y - _origin.Y) / Cell;
            float dx = ex - gx, dy = ey - gy;
            int c = (int)MathF.Floor(gx), r = (int)MathF.Floor(gy);
            int cEnd = (int)MathF.Floor(ex), rEnd = (int)MathF.Floor(ey);
            int stepC = dx > 0 ? 1 : -1, stepR = dy > 0 ? 1 : -1;
            float tDeltaX = dx != 0 ? MathF.Abs(1f / dx) : float.PositiveInfinity;
            float tDeltaY = dy != 0 ? MathF.Abs(1f / dy) : float.PositiveInfinity;
            float tMaxX = dx != 0 ? (stepC > 0 ? c + 1 - gx : gx - c) * tDeltaX : float.PositiveInfinity;
            float tMaxY = dy != 0 ? (stepR > 0 ? r + 1 - gy : gy - r) * tDeltaY : float.PositiveInfinity;
            float tIn = 0f;
            for (int guard = Math.Abs(cEnd - c) + Math.Abs(rEnd - r) + 2; guard > 0; guard--)
            {
                float tOut = MathF.Min(MathF.Min(tMaxX, tMaxY), 1f);
                if (c >= 0 && r >= 0 && c < _nx && r < _ny && tOut > tIn)
                {
                    float density = _density[r * _nx + c];
                    if (density > 0f)
                    {
                        float ground = world.GroundHeightAt(_origin.X + (c + 0.5f) * Cell, _origin.Y + (r + 0.5f) * Cell);
                        float h = _canopy[r * _nx + c];
                        float z0 = a.Z + d.Z * tIn, z1 = a.Z + d.Z * tOut;
                        float all = Below(z0, z1, ground + h) * (tOut - tIn);
                        float k = density / 100f * len;
                        if (all > 0f && tFirst >= 1f) tFirst = tIn;
                        trunkMetres += Below(z0, z1, ground + h * CrownBase) * (tOut - tIn) * k;
                        allMetres += all * k;
                    }
                }
                if (tOut >= 1f || (c == cEnd && r == rEnd)) break;
                if (tMaxX < tMaxY) { tIn = tMaxX; tMaxX += tDeltaX; c += stepC; }
                else { tIn = tMaxY; tMaxY += tDeltaY; r += stepR; }
            }
        }

        // ── the IMPORT side: tree points → woods + lone trees (R-4190 T-1 / T-2, UTM §9.6) ──────────────────────────────────────────

        /// <summary>A cell is wood when the trees per hectare over its 3 × 3 neighbourhood reach this (R-4190: ≥ 45 / ha, 3 × 3).</summary>
        public const float MinWoodsDensity = 45f;
        /// <summary>A woods polygon's density is rounded to this many trees / ha, so neighbouring cells of nearly the same density merge.</summary>
        public const float DensityStep = 5f;

        /// <summary>A road piece the woods leave open (terrain-local metres; the half width of the carriageway).</summary>
        public readonly record struct RoadSegment(Vector2 A, Vector2 B, float HalfWidth);

        /// <summary>A woods rectangle on the 25 m grid: its corners and its trees / ha.</summary>
        public readonly record struct WoodsRect(Vector2 Min, Vector2 Max, float Density);

        /// <summary>What <see cref="Cluster"/> made of the points.</summary>
        public sealed class Clustered
        {
            /// <summary>The woods, as grid-aligned rectangles (adjacent cells of one rounded density merged).</summary>
            public System.Collections.Generic.List<WoodsRect> Woods { get; } = new();
            /// <summary>The indices of the trees outside every wood — each becomes a trunk (T-1).</summary>
            public System.Collections.Generic.List<int> LoneTrees { get; } = new();
            /// <summary>Cells that were wood but a road crosses — left open, their trees not kept (they are a wood's, not lone).</summary>
            public int ClearedCells { get; set; }
            public int DroppedTrees { get; set; }
            public int WoodCells { get; set; }
            public int WoodTrees { get; set; }
        }

        /// <summary>
        /// ⭐ <c>CE-4369</c> (L-7) — the importer's tree stage (UTM §9.6): <paramref name="trees"/> counted on the <see cref="Cell"/> grid
        /// aligned to multiples of 25 m, a cell is wood when its 3 × 3 neighbourhood holds ≥ <see cref="MinWoodsDensity"/> trees / ha; a cell
        /// a <paramref name="roads"/> piece comes within its half width of stays OPEN (a car may drive a road through a wood, Q-T1); the trees
        /// of no wood are the lone trees. The rectangles land exactly on the grid <see cref="Build"/> rasterises, so the runtime grid is this one.
        /// </summary>
        public static Clustered Cluster(ReadOnlySpan<Vector2> trees, Vector2 min, Vector2 max, System.Collections.Generic.IReadOnlyList<RoadSegment> roads)
        {
            var origin = new Vector2(MathF.Floor(min.X / Cell) * Cell, MathF.Floor(min.Y / Cell) * Cell);
            int nx = Math.Max(1, (int)MathF.Ceiling((max.X - origin.X) / Cell)), ny = Math.Max(1, (int)MathF.Ceiling((max.Y - origin.Y) / Cell));
            var count = new int[nx * ny];
            var cellOf = new int[trees.Length];
            for (int i = 0; i < trees.Length; i++)
            {
                int c = (int)MathF.Floor((trees[i].X - origin.X) / Cell), r = (int)MathF.Floor((trees[i].Y - origin.Y) / Cell);
                cellOf[i] = c < 0 || r < 0 || c >= nx || r >= ny ? -1 : r * nx + c;
                if (cellOf[i] >= 0) count[cellOf[i]]++;
            }
            const float CellHa = Cell * Cell / 10000f;
            var density = new float[nx * ny];   // > 0 ⇔ wood
            var wasWood = new bool[nx * ny];
            for (int r = 0; r < ny; r++)
                for (int c = 0; c < nx; c++)
                {
                    int sum = 0, cells = 0;
                    for (int dr = -1; dr <= 1; dr++)
                        for (int dc = -1; dc <= 1; dc++)
                        {
                            int rr = r + dr, cc = c + dc;
                            if (rr < 0 || cc < 0 || rr >= ny || cc >= nx) continue;
                            sum += count[rr * nx + cc]; cells++;
                        }
                    float perHa = sum / (cells * CellHa);
                    if (perHa < MinWoodsDensity) continue;
                    wasWood[r * nx + c] = true;
                    density[r * nx + c] = MathF.Max(DensityStep, MathF.Round(perHa / DensityStep) * DensityStep);
                }

            var result = new Clustered();
            foreach (var s in roads)
            {
                var lo = Vector2.Min(s.A, s.B) - new Vector2(s.HalfWidth); var hi = Vector2.Max(s.A, s.B) + new Vector2(s.HalfWidth);
                int c0 = Math.Max(0, (int)MathF.Floor((lo.X - origin.X) / Cell)), c1 = Math.Min(nx - 1, (int)MathF.Floor((hi.X - origin.X) / Cell));
                int r0 = Math.Max(0, (int)MathF.Floor((lo.Y - origin.Y) / Cell)), r1 = Math.Min(ny - 1, (int)MathF.Floor((hi.Y - origin.Y) / Cell));
                for (int r = r0; r <= r1; r++)
                    for (int c = c0; c <= c1; c++)
                    {
                        if (density[r * nx + c] <= 0f) continue;
                        var cmin = origin + new Vector2(c * Cell, r * Cell);
                        if (SegmentRectDistance(s.A, s.B, cmin, cmin + new Vector2(Cell)) > s.HalfWidth) continue;
                        density[r * nx + c] = 0f;
                        result.ClearedCells++;
                    }
            }

            for (int i = 0; i < trees.Length; i++)
            {
                int k = cellOf[i];
                if (k >= 0 && density[k] > 0f) result.WoodTrees++;
                else if (k >= 0 && wasWood[k]) result.DroppedTrees++;
                else result.LoneTrees.Add(i);
            }

            // rectangles: runs along a row of one density, then a run grown upward while the row above has the same run
            var used = new bool[nx * ny];
            for (int r = 0; r < ny; r++)
                for (int c = 0; c < nx; c++)
                {
                    int k = r * nx + c;
                    if (used[k] || density[k] <= 0f) continue;
                    float d = density[k];
                    int c1 = c;
                    while (c1 + 1 < nx && !used[r * nx + c1 + 1] && density[r * nx + c1 + 1] == d) c1++;
                    int r1 = r;
                    while (r1 + 1 < ny && RowRun(density, used, nx, r1 + 1, c, c1, d)) r1++;
                    for (int rr = r; rr <= r1; rr++)
                        for (int cc = c; cc <= c1; cc++) used[rr * nx + cc] = true;
                    result.WoodCells += (r1 - r + 1) * (c1 - c + 1);
                    result.Woods.Add(new WoodsRect(origin + new Vector2(c * Cell, r * Cell), origin + new Vector2((c1 + 1) * Cell, (r1 + 1) * Cell), d));
                }
            return result;
        }

        private static bool RowRun(float[] density, bool[] used, int nx, int r, int c0, int c1, float d)
        {
            for (int c = c0; c <= c1; c++)
                if (used[r * nx + c] || density[r * nx + c] != d) return false;
            return true;
        }

        /// <summary>The distance between the segment <paramref name="a"/>–<paramref name="b"/> and the box (0 when they touch).</summary>
        public static float SegmentRectDistance(Vector2 a, Vector2 b, Vector2 min, Vector2 max)
        {
            if (Inside(a, min, max) || Inside(b, min, max)) return 0f;
            Vector2 c0 = min, c1 = new(max.X, min.Y), c2 = max, c3 = new(min.X, max.Y);
            if (Cross(a, b, c0, c1) || Cross(a, b, c1, c2) || Cross(a, b, c2, c3) || Cross(a, b, c3, c0)) return 0f;
            float best = MathF.Min(MathF.Min(PointSeg(c0, a, b), PointSeg(c1, a, b)), MathF.Min(PointSeg(c2, a, b), PointSeg(c3, a, b)));
            best = MathF.Min(best, Vector2.Distance(a, Vector2.Clamp(a, min, max)));
            return MathF.Min(best, Vector2.Distance(b, Vector2.Clamp(b, min, max)));
        }

        private static bool Inside(Vector2 p, Vector2 min, Vector2 max) => p.X >= min.X && p.X <= max.X && p.Y >= min.Y && p.Y <= max.Y;

        private static float PointSeg(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float l2 = ab.LengthSquared();
            float t = l2 <= 0f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / l2, 0f, 1f);
            return Vector2.Distance(p, a + ab * t);
        }

        private static bool Cross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            static float O(Vector2 p, Vector2 q, Vector2 r) => (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);
            float o1 = O(a, b, c), o2 = O(a, b, d), o3 = O(c, d, a), o4 = O(c, d, b);
            return (o1 > 0) != (o2 > 0) && (o3 > 0) != (o4 > 0);
        }

        /// <summary>The fraction (0..1) of a straight piece running from height <paramref name="z0"/> to <paramref name="z1"/> that lies below <paramref name="level"/>.</summary>
        private static float Below(float z0, float z1, float level)
        {
            if (z0 <= level && z1 <= level) return 1f;
            if (z0 > level && z1 > level) return 0f;
            float f = (level - z0) / (z1 - z0);
            return z0 <= level ? f : 1f - f;
        }
    }
}
