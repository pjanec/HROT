using System;
using System.Numerics;
using Fdp.Core;

namespace Fdp.Toolkit.Navigation
{
    /// <summary>
    /// Navmesh query interface consumed by EQS tests, generators, and navigation systems.
    /// All coordinates are in 3-D world space; for flat-earth queries map 2D (x, y_north) as
    /// <c>new Vector3(x, 0f, y_north)</c> and extract back via <c>(v.X, v.Z)</c>.
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
    }
}
