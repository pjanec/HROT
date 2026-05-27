using System;

namespace Fdp.Toolkit.Navigation.Fake
{
    /// <summary>
    /// All-in-one path registry for single-process (non-DDS) deployments.
    /// Forwards every call to the underlying <see cref="MusclePathRegistry"/> so both
    /// Muscle and Brain read the same pool without a separate Brain-side cache.
    /// </summary>
    public sealed class SharedPathRegistry : IPathRegistry
    {
        private readonly MusclePathRegistry _muscle;

        /// <param name="muscle">
        /// The authoritative pool. If null, a new <see cref="MusclePathRegistry"/> is created.
        /// </param>
        public SharedPathRegistry(MusclePathRegistry? muscle = null)
        {
            _muscle = muscle ?? new MusclePathRegistry();
        }

        /// <summary>Returns the underlying <see cref="MusclePathRegistry"/> for direct inspection.</summary>
        public MusclePathRegistry Muscle => _muscle;

        // ── IPathRegistry ────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public bool IsCached(int routeHandle)
            => _muscle.IsCached(routeHandle);

        /// <inheritdoc/>
        public bool TryGetSummary(int routeHandle, out PathSummary summary)
            => _muscle.TryGetSummary(routeHandle, out summary);

        /// <inheritdoc/>
        public bool TryGetWaypoints(int routeHandle, Span<NavWaypoint> dest, out int count)
            => _muscle.TryGetWaypoints(routeHandle, dest, out count);

        /// <inheritdoc/>
        public bool TryGetWaypointsSlice(int routeHandle, int startSegment, int maxCount,
                                          Span<NavWaypoint> dest, out int actualCount)
            => _muscle.TryGetWaypointsSlice(routeHandle, startSegment, maxCount, dest, out actualCount);
    }
}
