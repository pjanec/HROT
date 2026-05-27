using System;
using System.Numerics;
using Fdp.Toolkit.Navigation.Fake;

namespace Fdp.Toolkit.Navigation
{
    /// <summary>
    /// Provides 3-D path planning for entities that move through volumetric space
    /// (e.g., aircraft, drones). Implementations must be thread-safe.
    /// </summary>
    public interface IVolumetricPathProvider
    {
        /// <summary>
        /// Plans a 3-D path from <paramref name="from"/> to <paramref name="to"/> and writes
        /// the waypoints into <paramref name="waypoints"/>.
        /// Returns the number of waypoints written, or 0 if no path was found.
        /// </summary>
        int PlanPath(Vector3 from, Vector3 to, Span<NavWaypoint> waypoints);

        /// <summary>
        /// Returns a monotone version counter that increments whenever the volumetric
        /// space geometry is rebuilt.
        /// </summary>
        uint QueryVersion();

        /// <summary>
        /// Returns true if <paramref name="position"/> is within the flyable volume
        /// (altitude bounds respected, not inside a no-fly zone).
        /// Default implementation throws <see cref="NotSupportedException"/>.
        /// </summary>
        bool IsFlyable(Vector3 position) => throw new NotSupportedException(
            "IsFlyable is not supported by this IVolumetricPathProvider implementation.");

        /// <summary>
        /// Returns true if a flyable path exists from <paramref name="from"/> to
        /// <paramref name="to"/> within the constraints of <paramref name="profile"/>.
        /// <paramref name="maxCost"/> is an optional cost ceiling (ignored if 0).
        /// Default implementation throws <see cref="NotSupportedException"/>.
        /// </summary>
        bool PathExists(Vector3 from, Vector3 to, FlyProfile profile, float maxCost = 0f)
            => throw new NotSupportedException(
                "PathExists(FlyProfile) is not supported by this IVolumetricPathProvider implementation.");

        /// <summary>
        /// Returns the version counter scoped to the volumetric region described by
        /// <paramref name="region"/>. Useful for partial cache invalidation.
        /// Default implementation throws <see cref="NotSupportedException"/>.
        /// </summary>
        uint QueryVersion(BoundingBox3D region) => throw new NotSupportedException(
            "QueryVersion(BoundingBox3D) is not supported by this IVolumetricPathProvider implementation.");
    }
}

