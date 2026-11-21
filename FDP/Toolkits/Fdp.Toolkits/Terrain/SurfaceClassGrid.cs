using System;
using System.Numerics;
using CarKinem.Road;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>⭐ <c>CE-4621</c> MC-2 — what a ground vehicle drives on: the road (1.0), or off it on open ground, scrub or forest (its type's factor).</summary>
    public enum GroundClass : byte
    {
        Open = 0, Road = 1, Scrub = 2, Forest = 3,
        /// <summary>⭐ <c>CE-1987</c> (R-1058) — an authored OPEN surface (a field, a meadow: <see cref="TerrainSurfaceType.Open"/>); a car drives it as <see cref="Open"/>,
        /// the thermal sensor reads it as grass where bare <see cref="Open"/> ground is soil.</summary>
        Field = 4,
        /// <summary>⭐ <c>CE-1987</c> — a water surface; a car's factor as <see cref="Open"/> (water does not stop a car here — unchanged), thermal reads it as water.</summary>
        Water = 5,
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4621</c> MC-2 (docs/designs/vehicle-tactics/DESIGN_Motorcycle.md §2, MV-4) — the SURFACE-CLASS GRID: the ground class under every
    /// <see cref="Cell"/> m, so the one ground integrator asks "what am I driving on?" with ONE array index per vehicle per tick (⛔ never the polygon walk
    /// of <c>TerrainWorld.Surfaces</c>, which allocated). Built ONCE, serially, from the terrain's own sources: the forest from <see cref="TerrainWoods"/> (the
    /// ONE forest source — the same cells sight and fire use), scrub from the scrub surfaces, the road from the road network's carriageways (a road
    /// through a wood is road). Outside the grid: open ground.
    /// <para>⭐ It GENERALISES the woods grid (MV-4): the woods' 25 m cell is too coarse to tell a road from the field beside it, so the class grid is
    /// finer (<see cref="Cell"/> m) and samples the woods at its cell centres. Allocation-free queries; immutable once built (the parallel move only reads it).</para>
    /// </summary>
    [Fdp.Core.NotState("the surface-class grid built once from the static terrain")]
    public sealed class SurfaceClassGrid
    {
        /// <summary>The cell edge (m): fine enough for a carriageway (a lane ~3.5 m), coarse enough for a 10 km map in 4 M bytes.</summary>
        public const float Cell = 5f;
        /// <summary>The most cells built (a larger area widens the cell instead): 64 M bytes.</summary>
        public const long MaxCells = 64L * 1024 * 1024;

        private readonly Vector2 _origin;
        private readonly float _cell;
        private readonly int _nx, _ny;
        private readonly byte[] _class;

        private SurfaceClassGrid(Vector2 origin, float cell, int nx, int ny, byte[] cls) { _origin = origin; _cell = cell; _nx = nx; _ny = ny; _class = cls; }

        public int Columns => _nx;
        public int Rows => _ny;
        public float CellSize => _cell;

        /// <summary>The ground class at (x, y) — <see cref="GroundClass.Open"/> outside the grid. One array index.</summary>
        public GroundClass ClassAt(float x, float y)
        {
            int c = (int)MathF.Floor((x - _origin.X) / _cell), r = (int)MathF.Floor((y - _origin.Y) / _cell);
            return c < 0 || r < 0 || c >= _nx || r >= _ny ? GroundClass.Open : (GroundClass)_class[r * _nx + c];
        }

        /// <summary>
        /// The grid of <paramref name="world"/> (may be null) and <paramref name="roads"/> (may be empty); null when neither has anything — a world with
        /// no terrain and no roads has no surface rule (every type at its road speed, as before CE-4621).
        /// </summary>
        public static SurfaceClassGrid? Build(TerrainWorld? world, in RoadNetworkBlob roads)
        {
            Vector2 lo = new(float.MaxValue), hi = new(float.MinValue);
            bool any = false;
            var woods = world?.Woods;
            if (world != null)
                foreach (var s in world.Surfaces)
                    if (s.Type is TerrainSurfaceType.Forest or TerrainSurfaceType.Scrub or TerrainSurfaceType.Open or TerrainSurfaceType.Water)   // ⭐ CE-1987 — fields and water too
                    { lo = Vector2.Min(lo, s.Min); hi = Vector2.Max(hi, s.Max); any = true; }
            int segments = roads.Segments.IsCreated ? roads.Segments.Length : 0;
            for (int i = 0; i < segments; i++)
            {
                var g = roads.Segments[i];
                float pad = HalfWidth(g) + 50f;   // the Hermite bulge
                lo = Vector2.Min(lo, Vector2.Min(g.P0, g.P1) - new Vector2(pad));
                hi = Vector2.Max(hi, Vector2.Max(g.P0, g.P1) + new Vector2(pad));
                any = true;
            }
            if (!any) return null;

            float cell = Cell;
            while ((long)MathF.Ceiling((hi.X - lo.X) / cell + 1) * (long)MathF.Ceiling((hi.Y - lo.Y) / cell + 1) > MaxCells) cell *= 2f;
            var origin = new Vector2(MathF.Floor(lo.X / cell) * cell, MathF.Floor(lo.Y / cell) * cell);
            int nx = Math.Max(1, (int)MathF.Ceiling((hi.X - origin.X) / cell)), ny = Math.Max(1, (int)MathF.Ceiling((hi.Y - origin.Y) / cell));
            var cls = new byte[nx * ny];

            if (world != null)
            {
                // ⭐ CE-1987 — fields and water first: scrub, a wood and a road over them win
                foreach (var s in world.Surfaces)
                {
                    if (s.Type == TerrainSurfaceType.Open) Stamp(s, origin, cell, nx, ny, cls, GroundClass.Field);
                    else if (s.Type == TerrainSurfaceType.Water) Stamp(s, origin, cell, nx, ny, cls, GroundClass.Water);
                }
                // scrub: a cell whose centre lies in a scrub surface
                foreach (var s in world.Surfaces)
                {
                    if (s.Type != TerrainSurfaceType.Scrub) continue;
                    Stamp(s, origin, cell, nx, ny, cls, GroundClass.Scrub);
                }
                // forest: the ONE forest source — the woods grid, sampled at each cell centre inside the forest surfaces' extent
                if (woods != null)
                    foreach (var s in world.Surfaces)
                    {
                        if (s.Type != TerrainSurfaceType.Forest) continue;
                        Range(s.Min, s.Max, origin, cell, nx, ny, out int c0, out int c1, out int r0, out int r1);
                        for (int r = r0; r <= r1; r++)
                            for (int c = c0; c <= c1; c++)
                            {
                                var p = origin + new Vector2((c + 0.5f) * cell, (r + 0.5f) * cell);
                                if (woods.DensityAt(p.X, p.Y) > 0f) cls[r * nx + c] = (byte)GroundClass.Forest;
                            }
                    }
            }
            // roads last: a road through a wood or a field is road
            for (int i = 0; i < segments; i++)
            {
                var g = roads.Segments[i];
                float half = HalfWidth(g);
                int steps = Math.Max(2, (int)MathF.Ceiling(Math.Max(g.Length, Vector2.Distance(g.P0, g.P1)) / (cell * 0.5f)));
                for (int k = 0; k <= steps; k++)
                {
                    var p = RoadGraphNavigator.EvaluateHermite(k / (float)steps, g.P0, g.T0, g.P1, g.T1);
                    Range(p - new Vector2(half), p + new Vector2(half), origin, cell, nx, ny, out int c0, out int c1, out int r0, out int r1);
                    for (int r = r0; r <= r1; r++)
                        for (int c = c0; c <= c1; c++)
                        {
                            var centre = origin + new Vector2((c + 0.5f) * cell, (r + 0.5f) * cell);
                            if (Vector2.DistanceSquared(centre, p) <= (half + cell * 0.5f) * (half + cell * 0.5f)) cls[r * nx + c] = (byte)GroundClass.Road;
                        }
                }
            }
            return new SurfaceClassGrid(origin, cell, nx, ny, cls);
        }

        /// <summary>A carriageway's half-width: all its lanes (the same rule as <c>VehicleCommandSystem.OnRoadHeight</c>).</summary>
        private static float HalfWidth(in RoadSegment g) => MathF.Max(g.LaneWidth, 2.5f) * Math.Max(1, g.LaneCount) * 0.5f;

        private static void Stamp(TerrainSurface s, Vector2 origin, float cell, int nx, int ny, byte[] cls, GroundClass value)
        {
            Range(s.Min, s.Max, origin, cell, nx, ny, out int c0, out int c1, out int r0, out int r1);
            for (int r = r0; r <= r1; r++)
                for (int c = c0; c <= c1; c++)
                    if (PolygonMath.Contains(s.Polygon, origin + new Vector2((c + 0.5f) * cell, (r + 0.5f) * cell))) cls[r * nx + c] = (byte)value;
        }

        private static void Range(Vector2 min, Vector2 max, Vector2 origin, float cell, int nx, int ny, out int c0, out int c1, out int r0, out int r1)
        {
            c0 = Math.Max(0, (int)MathF.Floor((min.X - origin.X) / cell)); c1 = Math.Min(nx - 1, (int)MathF.Floor((max.X - origin.X) / cell));
            r0 = Math.Max(0, (int)MathF.Floor((min.Y - origin.Y) / cell)); r1 = Math.Min(ny - 1, (int)MathF.Floor((max.Y - origin.Y) / cell));
        }
    }

    /// <summary>
    /// ⭐ <c>CE-4621</c> MC-2 — a type's top-speed factor on a ground class: a road 1.0; off it the type's authored factor (0 = not authored ⇒ 1.0, so
    /// every type authored before CE-4621 is unchanged). The ONE rule the integrator applies.
    /// </summary>
    public static class OffRoad
    {
        /// <summary>The lateral grip off the road (g) — a factored type's curvature limit there (MV-8: a bike's ~0.45 g on dirt).</summary>
        public const float GripG = 0.45f;

        /// <summary>Does this type author any off-road factor? (Only then does the integrator need the surface grid.)</summary>
        public static bool Authored(in global::CarKinem.Core.VehicleParams p) => p.OffRoadOpen > 0f || p.OffRoadScrub > 0f || p.OffRoadForest > 0f;

        /// <summary>The top-speed factor of a type with <paramref name="p"/> on <paramref name="ground"/>.</summary>
        public static float Factor(in global::CarKinem.Core.VehicleParams p, GroundClass ground) => ground switch
        {
            GroundClass.Open or GroundClass.Field or GroundClass.Water => p.OffRoadOpen > 0f ? p.OffRoadOpen : 1f,   // ⭐ CE-1987 — a field and water drive as open ground (unchanged)
            GroundClass.Scrub  => p.OffRoadScrub  > 0f ? p.OffRoadScrub  : 1f,
            GroundClass.Forest => p.OffRoadForest > 0f ? p.OffRoadForest : 1f,
            _ => 1f,
        };
    }
}
