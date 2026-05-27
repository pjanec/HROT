using System;

using Fdp.Core;

namespace Fdp.Toolkit.Navigation
{
    /// <summary>
    /// Read-only access to stored path data. Implemented by both the Muscle-side authoritative
    /// pool (<c>MusclePathRegistry</c>) and the Brain-side cache (<c>BrainPathRegistry</c>),
    /// as well as the all-in-one <c>SharedPathRegistry</c>.
    /// </summary>
    [ComponentId(GlobalComponentIds.IPathRegistry)]
    public interface IPathRegistry
    {
        /// <summary>Returns true if a path for <paramref name="routeHandle"/> is currently stored.</summary>
        bool IsCached(int routeHandle);

        /// <summary>
        /// Tries to retrieve a lightweight summary for <paramref name="routeHandle"/>.
        /// Returns false if no entry exists.
        /// </summary>
        bool TryGetSummary(int routeHandle, out PathSummary summary);

        /// <summary>
        /// Copies the full waypoint sequence for <paramref name="routeHandle"/> into <paramref name="dest"/>.
        /// <paramref name="count"/> receives the number of waypoints written.
        /// Returns false if no entry exists or <paramref name="dest"/> is too small.
        /// </summary>
        bool TryGetWaypoints(int routeHandle, Span<NavWaypoint> dest, out int count);

        /// <summary>
        /// Copies a sub-range of the waypoint sequence starting at <paramref name="startSegment"/>
        /// for up to <paramref name="maxCount"/> waypoints into <paramref name="dest"/>.
        /// <paramref name="actualCount"/> receives the number of waypoints written.
        /// Returns false if no entry exists.
        /// </summary>
        bool TryGetWaypointsSlice(int routeHandle, int startSegment, int maxCount,
                                   Span<NavWaypoint> dest, out int actualCount);
    }

    /// <summary>
    /// Lightweight summary of a stored path entry.
    /// </summary>
    public struct PathSummary
    {
        /// <summary>Stable handle for this path, matching the <c>RouteHandle</c> in the registry.</summary>
        public int RouteHandle;

        /// <summary>Total arc-length in metres.</summary>
        public float TotalDistanceMeters;

        /// <summary>Number of waypoints in the stored path.</summary>
        public int WaypointCount;

        /// <summary>Navmesh version when the path was planned, for staleness detection.</summary>
        public uint NavmeshVersionAtPlan;

        /// <summary>
        /// Backend that produced this path.
        /// 0 = Navmesh, 1 = RoadGraph, 2 = Spliced, 3 = Volumetric.
        /// </summary>
        public byte PrimaryBackend;

        /// <summary>Bitmask flags. Bit 0: HasOffMeshLinks.</summary>
        public byte Flags;

        /// <summary>Number of times this path entry has been replaced in place (replan counter).</summary>
        public byte ReplanCount;
    }
}
