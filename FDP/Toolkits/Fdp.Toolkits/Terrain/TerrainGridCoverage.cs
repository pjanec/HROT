using System.Collections.Generic;
using System.Numerics;
using CarKinem.Spatial;
using Fdp.Toolkit.Perception;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ <b>W9 — where the spatial grids sit for a terrain, and whether it fits</b> (docs/DESIGN_Terrain_World.md §4.4).
    /// <para>⭐ CE-3018: the grids REBASE to the resident terrain (<see cref="SpatialGridFit"/>), so this reports the FITTED
    /// placement. ⛔ SUPERSEDED: <i>"the grids are fixed at composition; this warns every grid the world leaves"</i>.
    /// <see cref="Problems"/> stays as the loud check — it can only fire if a fit is impossible.</para>
    /// </summary>
    public static class TerrainGridCoverage
    {
        /// <summary>The perception grid's placement for <paramref name="world"/> (null ⇒ the composition default).</summary>
        public static GridGeometry PerceptionGrid(TerrainWorld? world) => SpatialGridFit.For(
            Vector2.Zero, PerceptionConstants.LocalGridWidth, PerceptionConstants.LocalGridHeight,
            PerceptionConstants.LocalGridCellSize, world);

        /// <summary>The collider (avoidance) grid's placement for <paramref name="world"/>.</summary>
        public static GridGeometry ColliderGrid(TerrainWorld? world) => SpatialGridFit.For(
            new Vector2(SpatialHashConstants.OriginX, SpatialHashConstants.OriginY),
            SpatialHashConstants.GridWidth, SpatialHashConstants.GridHeight, SpatialHashConstants.CellSizeMeters, world);

        /// <summary>One line per grid that, even fitted, leaves part of the world uncovered; empty when both cover it.</summary>
        public static IReadOnlyList<string> Problems(TerrainWorld world)
        {
            var problems = new List<string>();
            Check("perception", PerceptionGrid(world), PerceptionConstants.LocalGridWidth, PerceptionConstants.LocalGridHeight, world, problems);
            Check("collider", ColliderGrid(world), SpatialHashConstants.GridWidth, SpatialHashConstants.GridHeight, world, problems);
            return problems;
        }

        /// <summary>A one-line description of both placements, for the commit log.</summary>
        public static string Describe(TerrainWorld world)
        {
            var p = PerceptionGrid(world);
            var c = ColliderGrid(world);
            return $"perception grid origin ({p.OriginX},{p.OriginY}) cell {p.CellSize:0.##} m; "
                 + $"collider grid origin ({c.OriginX},{c.OriginY}) cell {c.CellSize:0.##} m";
        }

        private static void Check(string name, GridGeometry g, int width, int height, TerrainWorld world, List<string> problems)
        {
            var min = new Vector2(g.OriginX, g.OriginY);
            var max = min + new Vector2(width * g.CellSize, height * g.CellSize);
            if (!(world.BoundsMax.X > world.BoundsMin.X && world.BoundsMax.Y > world.BoundsMin.Y)) return;   // empty world
            if (world.BoundsMin.X >= min.X && world.BoundsMin.Y >= min.Y
                && world.BoundsMax.X <= max.X && world.BoundsMax.Y <= max.Y) return;
            problems.Add(
                $"terrain bounds [{world.BoundsMin.X},{world.BoundsMin.Y}]..[{world.BoundsMax.X},{world.BoundsMax.Y}] leave the "
              + $"fitted {name} grid [{min.X},{min.Y}]..[{max.X},{max.Y}] — entities outside it are invisible "
              + $"to {(name == "perception" ? "sight" : "collision avoidance")} (CE-3018).");
        }
    }
}
