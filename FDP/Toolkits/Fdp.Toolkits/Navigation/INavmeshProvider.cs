using System;
using System.Numerics;
using Fdp.Core;

namespace Fdp.Toolkit.Navigation
{
    /// <summary>
    /// Navmesh query interface consumed by EQS tests, generators, and navigation systems.
    /// ⭐ All coordinates are in the engine's <b>Z-up</b> 3-D world space — X east, Y north, Z up (R-182,
    /// DESIGN_Terrain_World W7). ⛔ Never pass Z = 0 meaning "on the ground" (R-248, CE-1034 H3): the search box around a query
    /// point is only a few metres tall, so on a hill a point at 0 finds nothing — pass the ground height there
    /// (<c>IWorldQuery.GroundHeightAt</c>) or a real Z. An implementation whose solver is Y-up (DotRecast) converts INSIDE itself,
    /// for every input and every output; callers never swizzle.
    /// </summary>
    [ComponentId(GlobalComponentIds.INavmeshProvider)]
    public interface INavmeshProvider
    {
        /// <summary>Returns true if <paramref name="position"/> projects onto a walkable navmesh polygon.</summary>
        bool IsWalkable(Vector3 position, uint layerMask = 0xFFFFFFFF);

        /// <summary>
        /// Projects <paramref name="position"/> onto the nearest walkable navmesh surface.
        /// Returns true and writes the snapped point into <paramref name="snapped"/> on success.
        /// </summary>
        bool ProjectToNavmesh(Vector3 position, out Vector3 snapped, uint layerMask = 0xFFFFFFFF);

        /// <summary>
        /// Samples reachable points within <paramref name="radius"/> of <paramref name="center"/>.
        /// Returns the number of points written into <paramref name="results"/>.
        /// </summary>
        int SampleNavmeshPoints(Vector3 center, float radius, Span<Vector3> results, uint layerMask = 0xFFFFFFFF);

        /// <summary>Returns true if a walkable path exists between <paramref name="from"/> and <paramref name="to"/>.</summary>
        bool PathExists(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF);

        /// <summary>
        /// Returns the traversal cost of the shortest path from <paramref name="from"/> to <paramref name="to"/>,
        /// or <see cref="float.MaxValue"/> when no path exists.
        /// </summary>
        float PathCost(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF);

        /// <summary>
        /// Returns a monotone version counter that increments whenever the navmesh is rebuilt.
        /// Callers can cache path results until the version changes.
        /// </summary>
        uint QueryVersion();

        /// <summary>
        /// Plans a path from <paramref name="from"/> to <paramref name="to"/> and writes the waypoints
        /// (including start and end) into <paramref name="waypoints"/>.
        /// Returns the number of waypoints written, or 0 if no path was found.
        /// </summary>
        int PlanPath(Vector3 from, Vector3 to, Span<NavWaypoint> waypoints, uint layerMask = 0xFFFFFFFF);

        // ── ⭐ R-219 — the same queries judged by the door states of the CALLER's view ──────────────────────────────────────
        //   A background solver passes the table built from its snapshot (Fdp.Toolkit.Terrain.DoorStates.Of(view, terrain)), so a
        //   path or reachability answer sees the doors of the tick it runs on. Defaults ignore doors (implementations without them).

        /// <inheritdoc cref="PlanPath(Vector3, Vector3, Span{NavWaypoint}, uint)"/>
        int PlanPath(Vector3 from, Vector3 to, Span<NavWaypoint> waypoints, uint layerMask, Fdp.Toolkit.Terrain.DoorStates? doors)
            => PlanPath(from, to, waypoints, layerMask);

        /// <inheritdoc cref="PathExists(Vector3, Vector3, uint)"/>
        bool PathExists(Vector3 from, Vector3 to, uint layerMask, Fdp.Toolkit.Terrain.DoorStates? doors)
            => PathExists(from, to, layerMask);

        /// <inheritdoc cref="PathCost(Vector3, Vector3, uint)"/>
        float PathCost(Vector3 from, Vector3 to, uint layerMask, Fdp.Toolkit.Terrain.DoorStates? doors)
            => PathCost(from, to, layerMask);
    }
}
