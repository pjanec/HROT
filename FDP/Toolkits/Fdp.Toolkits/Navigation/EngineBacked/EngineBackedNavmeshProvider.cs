using System;
using System.Numerics;
using Fdp.Core;

namespace Fdp.Toolkit.Navigation.EngineBacked
{
    /// <summary>
    /// Direct-line placeholder navmesh provider for engine-backed scenarios.
    /// All walkability queries return true; <c>PlanPath</c> returns a straight two-waypoint path.
    /// </summary>
    public sealed class EngineBackedNavmeshProvider : INavmeshProvider
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
            // Return the center point as the only sample for simplicity.
            if (results.Length > 0)
            {
                results[0] = center;
                return 1;
            }
            return 0;
        }

        /// <inheritdoc/>
        public bool PathExists(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF) => true;

        /// <inheritdoc/>
        public float PathCost(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF)
            => Vector3.Distance(from, to);

        /// <inheritdoc/>
        public uint QueryVersion() => 1;

        /// <inheritdoc/>
        public int PlanPath(Vector3 from, Vector3 to, Span<NavWaypoint> waypoints, uint layerMask = 0xFFFFFFFF)
        {
            if (waypoints.Length < 2) return 0;
            waypoints[0] = new NavWaypoint
            {
                Position  = from,
                Traversal = TraversalKind.Walk,
                Surface   = SurfaceType.Generic,
            };
            waypoints[1] = new NavWaypoint
            {
                Position  = to,
                Traversal = TraversalKind.Walk,
                Surface   = SurfaceType.Generic,
            };
            return 2;
        }
    }
}
