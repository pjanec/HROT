using System.Collections.Generic;
using System.Numerics;
using CarKinem.Spatial;
using Fdp.Toolkit.Perception;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ <b>W9 — does the terrain fit the fixed spatial grids?</b> (docs/DESIGN_Terrain_World.md §7.2 W9).
    /// <para>⚠ The perception grid covers [0, 1000) and the collider grid [-750, 750) on both axes, and both are
    /// value-type grids allocated at composition, long before a terrain loads, so they cannot simply be resized at
    /// commit. An entity outside a grid is silently missing from sight and avoidance. Slice 1 makes that LOUD: the
    /// terrain commit reports every grid the world's bounds leave. Sizing the grids from the bounds is CE-3018.</para>
    /// </summary>
    public static class TerrainGridCoverage
    {
        /// <summary>The perception grid's world extent.</summary>
        public static (Vector2 Min, Vector2 Max) PerceptionGrid => (
            Vector2.Zero,
            new Vector2(PerceptionConstants.LocalGridWidth  * PerceptionConstants.LocalGridCellSize,
                        PerceptionConstants.LocalGridHeight * PerceptionConstants.LocalGridCellSize));

        /// <summary>The collider (avoidance) grid's world extent.</summary>
        public static (Vector2 Min, Vector2 Max) ColliderGrid => (
            new Vector2(SpatialHashConstants.OriginX, SpatialHashConstants.OriginY),
            new Vector2(SpatialHashConstants.OriginX + (SpatialHashConstants.GridWidth  * SpatialHashConstants.CellSizeMeters),
                        SpatialHashConstants.OriginY + (SpatialHashConstants.GridHeight * SpatialHashConstants.CellSizeMeters)));

        /// <summary>One line per grid the world's bounds leave; empty when the world fits both.</summary>
        public static IReadOnlyList<string> Problems(TerrainWorld world)
        {
            var problems = new List<string>();
            Check("perception", PerceptionGrid, world, problems);
            Check("collider", ColliderGrid, world, problems);
            return problems;
        }

        private static void Check(string name, (Vector2 Min, Vector2 Max) grid, TerrainWorld world, List<string> problems)
        {
            if (world.BoundsMin.X >= grid.Min.X && world.BoundsMin.Y >= grid.Min.Y
                && world.BoundsMax.X <= grid.Max.X && world.BoundsMax.Y <= grid.Max.Y) return;
            problems.Add(
                $"terrain bounds [{world.BoundsMin.X},{world.BoundsMin.Y}]..[{world.BoundsMax.X},{world.BoundsMax.Y}] leave the "
              + $"{name} grid [{grid.Min.X},{grid.Min.Y}]..[{grid.Max.X},{grid.Max.Y}] — entities outside it are invisible "
              + $"to {(name == "perception" ? "sight" : "collision avoidance")} (CE-3018).");
        }
    }
}
