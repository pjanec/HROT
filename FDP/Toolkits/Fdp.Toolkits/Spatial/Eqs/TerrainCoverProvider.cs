using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Terrain;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// ⭐ Cover points generated from the terrain world's buildings and walls — design §10.3's "auto-computed" cover database
    /// (docs/designs/eqs-2/EQS_Design_v1.3_final.md §19.5). Built once per terrain load by <c>TerrainResidency.Commit</c>.
    /// <list type="bullet">
    ///   <item>Along every prism edge, a point every <see cref="Spacing"/> m, <see cref="StandOff"/> m out from the wall,
    ///     facing it (the direction of protection).</item>
    ///   <item>Stance from the wall's height: ≥ 1.5 m stand · ≥ 0.9 m crouch · ≥ 0.45 m prone · lower is no cover.</item>
    ///   <item>Z = the surface the point stands on; a point that lands inside another solid is dropped.</item>
    ///   <item>Radius queries go through a <see cref="CellSize"/>-m bucket grid and return the NEAREST points when more match
    ///     than fit. Immutable after build ⇒ safe to read from the EQS solver's background thread.</item>
    /// </list>
    /// </summary>
    public sealed class TerrainCoverProvider : ICoverProvider
    {
        public const float Spacing = 2.5f;
        public const float StandOff = 0.75f;
        public const float CellSize = 10f;

        private readonly CoverPoint[] _points;
        private readonly Dictionary<(int, int), int[]> _cells;

        private TerrainCoverProvider(CoverPoint[] points)
        {
            _points = points;
            var buckets = new Dictionary<(int, int), List<int>>();
            for (int i = 0; i < points.Length; i++)
            {
                var key = Cell(points[i].PositionX, points[i].PositionY);
                if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<int>();
                list.Add(i);
            }
            _cells = new Dictionary<(int, int), int[]>(buckets.Count);
            foreach (var (k, v) in buckets) _cells[k] = v.ToArray();
        }

        /// <summary>Every cover point (diagnostics, tests).</summary>
        public IReadOnlyList<CoverPoint> Points => _points;

        /// <summary>Builds the cover database for <paramref name="world"/>.</summary>
        public static TerrainCoverProvider Build(TerrainWorld world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            var points = new List<CoverPoint>();
            foreach (var prism in world.Prisms)
            {
                byte stance;
                if (prism.Height >= 1.5f) stance = 2;
                else if (prism.Height >= 0.9f) stance = 1;
                else if (prism.Height >= 0.45f) stance = 0;
                else continue;

                var fp = prism.Footprint;
                bool ccw = PolygonMath.SignedArea2(fp) > 0f;
                for (int e = 0; e < fp.Length; e++)
                {
                    var a = fp[e];
                    var b = fp[(e + 1) % fp.Length];
                    var edge = b - a;
                    float len = edge.Length();
                    if (len < 1e-3f) continue;
                    var dir = edge / len;
                    // Outward normal: right of the edge for a counter-clockwise footprint.
                    var outward = ccw ? new Vector2(dir.Y, -dir.X) : new Vector2(-dir.Y, dir.X);
                    int n = Math.Max(1, (int)(len / Spacing));
                    for (int k = 0; k < n; k++)
                    {
                        var p = a + (dir * ((k + 0.5f) * len / n)) + (outward * StandOff);
                        if (EqsTerrainSight.InsideSolid(world, p)) continue;
                        points.Add(new CoverPoint
                        {
                            PositionX = p.X,
                            PositionY = p.Y,
                            PositionZ = world.SurfaceZ(p.X, p.Y, prism.BaseZ),
                            DirectionX = -outward.X,
                            DirectionY = -outward.Y,
                            Quality = 1f,
                            StanceHeight = stance,
                        });
                    }
                }
            }
            return new TerrainCoverProvider(points.ToArray());
        }

        /// <inheritdoc/>
        public int GetCoverPointsInRadius(Vector2 center, float radius, Span<CoverPoint> results)
        {
            if (results.Length == 0 || radius <= 0f) return 0;
            Span<float> dist = results.Length <= 256 ? stackalloc float[results.Length] : new float[results.Length];
            int count = 0;
            float r2 = radius * radius;
            var (x0, y0) = Cell(center.X - radius, center.Y - radius);
            var (x1, y1) = Cell(center.X + radius, center.Y + radius);
            for (int cx = x0; cx <= x1; cx++)
                for (int cy = y0; cy <= y1; cy++)
                {
                    if (!_cells.TryGetValue((cx, cy), out var idx)) continue;
                    foreach (int i in idx)
                    {
                        float d2 = Vector2.DistanceSquared(center, new Vector2(_points[i].PositionX, _points[i].PositionY));
                        if (d2 > r2) continue;
                        if (count < results.Length) { results[count] = _points[i]; dist[count] = d2; count++; continue; }
                        // Full: replace the farthest kept point when this one is nearer.
                        int far = 0;
                        for (int j = 1; j < count; j++) if (dist[j] > dist[far]) far = j;
                        if (d2 < dist[far]) { results[far] = _points[i]; dist[far] = d2; }
                    }
                }
            return count;
        }

        private static (int, int) Cell(float x, float y) => ((int)MathF.Floor(x / CellSize), (int)MathF.Floor(y / CellSize));
    }
}
