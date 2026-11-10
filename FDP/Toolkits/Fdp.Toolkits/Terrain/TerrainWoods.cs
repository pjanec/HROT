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
            metres = 0f;
            var d = b - a;
            float len = d.Length();
            if (len < 1e-4f) return 1f;
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
            float tIn = 0f, loss = 0f;
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
                        float trunk = Below(z0, z1, ground + h * CrownBase) * (tOut - tIn);
                        float all = Below(z0, z1, ground + h) * (tOut - tIn);
                        float k = density / 100f * len;
                        metres += all * k;
                        loss += (trunk * LossPerMetre + (all - trunk) * CrownLossPerMetre) * k;
                    }
                }
                if (tOut >= 1f || (c == cEnd && r == rEnd)) break;
                if (tMaxX < tMaxY) { tIn = tMaxX; tMaxX += tDeltaX; c += stepC; }
                else { tIn = tMaxY; tMaxY += tDeltaY; r += stepR; }
            }
            return loss <= 0f ? 1f : MathF.Exp(-loss);
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
