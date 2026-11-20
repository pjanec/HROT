using System.Runtime.CompilerServices;
using CarKinem.Road;
using Fdp.Core;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐⭐ <c>CE-1987</c> (R-1058; docs/DESIGN_Thermal_And_Acoustic_Sensing.md §9, C-2) — the ONE surface-class grid of a world: what lies under a point (road · field ·
    /// scrub · forest · water · open ground), built once from the world's terrain and road network and rebuilt when either changes. Read by the cars' off-road rule
    /// (<c>CarKinematicsSystem</c>, ⛔ was its private field) and by the thermal sensor's background (<c>ThermalBackground</c>) — one set of cells for both.
    /// ⚠ Build it from a SERIAL point (a system's own serial start); the grid itself is immutable, so parallel readers share it.
    /// </summary>
    public static class GroundSurfaces
    {
        private sealed class Entry
        {
            public TerrainWorld? World;
            public int Roads = -1;
            public SurfaceClassGrid? Grid;
        }

        private static readonly ConditionalWeakTable<EntityRepository, Entry> s_of = new();

        /// <summary>The grid of <paramref name="repo"/>'s current terrain and roads (null: neither has anything). Builds on the first ask after a change — call it serially.</summary>
        public static SurfaceClassGrid? Of(EntityRepository repo)
        {
            var world = repo.HasSingletonManaged<TerrainWorld>() ? repo.GetSingletonManaged<TerrainWorld>() : null;
            var roads = repo.HasSingleton<global::CarKinem.Road.ZoneEnvironmentData>() ? repo.GetSingleton<global::CarKinem.Road.ZoneEnvironmentData>().RoadNetwork : default;
            int roadCount = roads.Segments.IsCreated ? roads.Segments.Length : 0;
            var e = s_of.GetValue(repo, _ => new Entry());
            lock (e)   // ⚠ only the rare (re)build contends: once per terrain change, never per entity
            {
                if (!ReferenceEquals(e.World, world) || e.Roads != roadCount)
                {
                    e.World = world; e.Roads = roadCount;
                    e.Grid = SurfaceClassGrid.Build(world, in roads);
                }
                return e.Grid;
            }
        }

        /// <summary>The grid as last built for <paramref name="repo"/>, without building (a parallel reader after a serial <see cref="Of"/>); null when none.</summary>
        public static SurfaceClassGrid? Built(EntityRepository repo) => s_of.TryGetValue(repo, out var e) ? e.Grid : null;
    }
}
