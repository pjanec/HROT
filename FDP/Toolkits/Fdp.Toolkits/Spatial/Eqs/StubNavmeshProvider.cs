using System;
using System.Numerics;
using Fdp.Toolkit.Navigation;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// Phase 4 stub navmesh provider (NAV-P0-T3).
    /// IsWalkable / PathExists: always true.
    /// PathCost: flat-earth Euclidean distance in the XZ plane (ignores Y).
    /// SampleNavmeshPoints: returns a 3x3 grid of points within radius.
    /// PlanPath: returns two waypoints (start + end).
    /// ProjectToNavmesh: returns the position unchanged.
    /// </summary>
    public sealed class StubNavmeshProvider : INavmeshProvider
    {
        /// <inheritdoc/>
        public bool IsWalkable(Vector3 position, uint layerMask = 0xFFFFFFFF) => true;

        /// <inheritdoc/>
        public bool ProjectToNavmesh(Vector3 position, out Vector3 snapped, uint layerMask = 0xFFFFFFFF)
        {
            snapped = position;
            return true;
        }

        /// <inheritdoc/>
        public int SampleNavmeshPoints(Vector3 center, float radius, Span<Vector3> results, uint layerMask = 0xFFFFFFFF)
        {
            // Stub: return a 3x3 grid of sample points within the radius (XZ plane).
            int count = 0;
            float step = radius / 2f;
            for (float dx = -step; dx <= step && count < results.Length; dx += step)
            {
                for (float dz = -step; dz <= step && count < results.Length; dz += step)
                {
                    float distXZ = MathF.Sqrt(dx * dx + dz * dz);
                    if (distXZ <= radius)
                        results[count++] = new Vector3(center.X + dx, center.Y, center.Z + dz);
                }
            }
            return count;
        }

        /// <inheritdoc/>
        public bool PathExists(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF) => true;

        /// <inheritdoc/>
        public float PathCost(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF)
        {
            // Flat-earth: use XZ distance only (ignore Y altitude difference).
            float dx = from.X - to.X;
            float dz = from.Z - to.Z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        /// <inheritdoc/>
        public uint QueryVersion() => 1;

        /// <inheritdoc/>
        public int PlanPath(Vector3 from, Vector3 to, Span<NavWaypoint> waypoints, uint layerMask = 0xFFFFFFFF)
        {
            if (waypoints.Length >= 2)
            {
                waypoints[0] = new NavWaypoint { Position = from };
                waypoints[1] = new NavWaypoint { Position = to };
                return 2;
            }
            return 0;
        }
    }
}
