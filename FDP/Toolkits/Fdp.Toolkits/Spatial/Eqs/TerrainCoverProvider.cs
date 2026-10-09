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
                // ⭐ Stage 7a (§3l C1) — a BUILDING wall is read by its panel's faces below; its expanded pieces (jambs, sill strips,
                //   lintels) are not: they do not know their storey floor, and a lintel is not cover. Free walls/fences and solid
                //   prisms keep this rule unchanged.
                if (prism.Panel >= 0 && prism.Panel < world.Panels.Count && world.Panels[prism.Panel].Building >= 0) continue;
                PointsAround(world, prism, points);
            }
            foreach (var panel in world.Panels)
                if (panel.Building >= 0) AddPanelFaces(world, panel, points);
            return new TerrainCoverProvider(points.ToArray());
        }

        /// <summary>
        /// The cover points round ONE solid piece: every <see cref="Spacing"/> along each footprint edge, <see cref="StandOff"/> out,
        /// facing the piece, at the stance its height protects (<see cref="StanceFor"/>); none when it is too low to hide anyone.
        /// ⭐ <c>CE-3142</c> (P-7a O5) — the same rule for a standing vehicle's box (<see cref="VehicleCover"/>): one rule, two callers
        /// (R-174). <paramref name="world"/> null ⇒ no terrain to test against (a point inside a solid is then kept).
        /// </summary>
        public static void PointsAround(TerrainWorld? world, TerrainPrism prism, List<CoverPoint> points)
        {
            byte stance = StanceFor(prism.Height);
            if (stance == 255) return;

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
                    if (world != null && EqsTerrainSight.InsideSolid(world, p)) continue;
                    points.Add(new CoverPoint
                    {
                        PositionX = p.X,
                        PositionY = p.Y,
                        PositionZ = world?.SurfaceZ(p.X, p.Y, prism.BaseZ) ?? prism.BaseZ,
                        DirectionX = -outward.X,
                        DirectionY = -outward.Y,
                        Quality = 1f,
                        StanceHeight = stance,
                    });
                }
            }
        }

        /// <summary>How far a point's floor may sit from the panel's storey floor and still count as that floor (m).</summary>
        public const float LevelTolerance = 0.3f;

        /// <summary>A window position needs the eye at least this far above the sill (m).</summary>
        public const float SillClearance = 0.1f;

        /// <summary>Today's rule (EQS §19.5): ≥ 1.5 m stand · ≥ 0.9 crouch · ≥ 0.45 prone · lower is no cover (255).</summary>
        private static byte StanceFor(float height)
            => height >= 1.5f ? (byte)2 : height >= 0.9f ? (byte)1 : height >= 0.45f ? (byte)0 : (byte)255;

        /// <summary>
        /// ⭐ Stage 7a (§3l C4) — the LOWEST stance whose eye clears the sill by <see cref="SillClearance"/> and stays below the head
        /// (eyes from <c>EngineFallbacks</c>: prone 0.35 · crouch 1.1 · stand 1.7 above the floor); 255 when none can fire from it.
        /// </summary>
        public static byte FiringStance(float sillAboveFloor, float headAboveFloor)
        {
            if (Fits(Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.EyeHeightProne)) return 0;
            if (Fits(Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.EyeHeightCrouched)) return 1;
            if (Fits(Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.EyeHeightStanding)) return 2;
            return 255;
            bool Fits(float eye) => eye >= sillAboveFloor + SillClearance && eye <= headAboveFloor - 0.05f;
        }

        /// <summary>
        /// ⭐ Stage 7a (§3l C2–C4) — a building panel by FACE: both faces, a point every <see cref="Spacing"/> m along each solid span,
        /// <see cref="StandOff"/> m out, facing the wall, at the panel's STOREY FLOOR. A door or gap span gives nothing; a window
        /// span is a low wall of its sill's height (both faces) and, on the face inside the building, a window firing position.
        /// A point is kept only where that floor exists (<see cref="LevelTolerance"/>) and nothing solid stands where the unit would.
        /// </summary>
        private static void AddPanelFaces(TerrainWorld world, TerrainWallPanel panel, List<CoverPoint> points)
        {
            float len = panel.Length;
            if (len < 1e-3f) return;
            var dir = (panel.B - panel.A) / len;
            var normal = new Vector2(-dir.Y, dir.X);
            float floor = panel.BaseZ;
            float off = panel.Thickness * 0.5f + StandOff;
            var footprint = panel.Building < world.Buildings.Count ? world.Buildings[panel.Building].Footprint : null;

            var openings = new List<TerrainOpening>(panel.Openings);
            openings.Sort((x, y) => x.At.CompareTo(y.At));
            float cursor = 0f;
            foreach (var o in openings)
            {
                float s = Math.Clamp(o.At, 0f, len), e = Math.Clamp(o.At + o.Width, 0f, len);
                if (s > cursor) SolidSpan(cursor, s);
                if (o.Kind == TerrainOpeningKind.Window) WindowSpan(s, e, o);
                cursor = Math.Max(cursor, e);
            }
            if (len > cursor) SolidSpan(cursor, len);

            void SolidSpan(float a, float b)
            {
                byte stance = StanceFor(panel.TopZ - floor);
                if (stance == 255 || b - a < 1e-3f) return;
                int n = Math.Max(1, (int)((b - a) / Spacing));
                for (int k = 0; k < n; k++)
                {
                    float t = a + (k + 0.5f) * (b - a) / n;
                    Add(t, +1f, stance, CoverKind.Cover);
                    Add(t, -1f, stance, CoverKind.Cover);
                }
            }

            void WindowSpan(float a, float b, TerrainOpening o)
            {
                float mid = (a + b) * 0.5f;
                byte sill = StanceFor(o.SillZ - floor);
                if (sill != 255) { Add(mid, +1f, sill, CoverKind.Cover); Add(mid, -1f, sill, CoverKind.Cover); }
                byte fire = FiringStance(o.SillZ - floor, o.HeadZ - floor);
                if (fire == 255) return;
                bool inPlus = Inside(mid, +1f), inMinus = Inside(mid, -1f);
                if (inPlus) Add(mid, +1f, fire, CoverKind.WindowFiring);
                if (inMinus) Add(mid, -1f, fire, CoverKind.WindowFiring);
            }

            bool Inside(float t, float side)
                => footprint != null && footprint.Length >= 3 && PolygonMath.Contains(footprint, Point(t, side));

            Vector2 Point(float t, float side) => panel.A + dir * t + normal * (side * off);

            void Add(float t, float side, byte stance, CoverKind kind)
            {
                var p = Point(t, side);
                if (!HasFloor(world, p, floor) || Occupied(world, p, floor)) return;
                points.Add(new CoverPoint
                {
                    PositionX = p.X,
                    PositionY = p.Y,
                    PositionZ = floor,
                    DirectionX = -normal.X * side,
                    DirectionY = -normal.Y * side,
                    Quality = 1f,
                    StanceHeight = stance,
                    Kind = kind,
                });
            }
        }

        /// <summary>A walkable level within <see cref="LevelTolerance"/> of <paramref name="floor"/> at <paramref name="p"/>.</summary>
        private static bool HasFloor(TerrainWorld world, Vector2 p, float floor)
        {
            foreach (float level in world.SurfacesAt(p.X, p.Y))
                if (MathF.Abs(level - floor) <= LevelTolerance) return true;
            return false;
        }

        /// <summary>Something solid stands where a unit on <paramref name="floor"/> at <paramref name="p"/> would (another wall's
        /// thickness, a solid block) — the 1 m above the floor.</summary>
        private static bool Occupied(TerrainWorld world, Vector2 p, float floor)
        {
            foreach (var prism in world.Prisms)
            {
                if (p.X < prism.Min.X || p.Y < prism.Min.Y || p.X > prism.Max.X || p.Y > prism.Max.Y) continue;
                if (prism.TopZ <= floor + 0.1f || prism.BaseZ >= floor + 1.0f) continue;
                if (PolygonMath.Contains(prism.Footprint, p)) return true;
            }
            return false;
        }

        /// <inheritdoc/>
        public int GetCoverPointsInRadius(Vector2 center, float radius, Span<CoverPoint> results)
            => GetCoverPointsInRadius(center, radius, results, CoverKind.Cover);

        /// <inheritdoc/>
        public int GetCoverPointsInRadius(Vector2 center, float radius, Span<CoverPoint> results, CoverKind kind)
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
                        if (_points[i].Kind != kind) continue;   // ⭐ Stage 7a — one kind per query
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
