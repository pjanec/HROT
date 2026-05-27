using System;
using System.Numerics;
using Fdp.Toolkit.Navigation.Fake;

namespace Fdp.Toolkit.Navigation.EngineBacked
{
    /// <summary>
    /// Direct-line 3D volumetric path provider for engine-backed scenarios.
    /// <c>PlanPath</c> returns two waypoints (start and end). All positions are flyable.
    /// </summary>
    public sealed class EngineBackedVolumetricPathProvider : IVolumetricPathProvider
    {
        /// <inheritdoc/>
        public int PlanPath(Vector3 from, Vector3 to, Span<NavWaypoint> waypoints)
        {
            if (waypoints.Length < 2) return 0;
            waypoints[0] = new NavWaypoint
            {
                Position  = from,
                Traversal = TraversalKind.Fly,
                Surface   = SurfaceType.Generic,
            };
            waypoints[1] = new NavWaypoint
            {
                Position  = to,
                Traversal = TraversalKind.Fly,
                Surface   = SurfaceType.Generic,
            };
            return 2;
        }

        /// <inheritdoc/>
        public uint QueryVersion() => 1;

        /// <inheritdoc/>
        public bool IsFlyable(Vector3 position) => true;

        /// <inheritdoc/>
        public bool PathExists(Vector3 from, Vector3 to, FlyProfile profile, float maxCost = 0f) => true;
    }
}
