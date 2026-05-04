using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.Collections;

namespace Fdp.Toolkit.Navigation
{
    // ── Path result struct ────────────────────────────────────────────────────────

    /// <summary>
    /// Computed path result written by the Muscle (Navigation Solver) tier.
    /// Stored in <see cref="PathfindingBatchData.Results"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PathResult
    {
        /// <summary>Echoes <see cref="PathRequest.RequestId"/> of the originating request.</summary>
        public long  RequestId;

        /// <summary><c>true</c> if a valid path was found; <c>false</c> if the goal is unreachable.</summary>
        public bool  IsReachable;

        /// <summary>Total arc-length of the computed path (metres), or 0 if unreachable.</summary>
        public float TotalDistanceMeters;

        /// <summary>
        /// Handle into the shared route/waypoint store. -1 if unreachable or not yet populated.
        /// </summary>
        public int   RouteHandle;

        /// <summary>
        /// Propagated from <see cref="PathRequest.SourceNodeId"/> by <c>PathfindingSolverSystem</c>.
        /// Used by the egress translator to route the result back to the originating Brain.
        /// </summary>
        public int   SourceNodeId;
    }

    // ── Singleton ECS component ───────────────────────────────────────────────────

    /// <summary>
    /// Zero-allocation singleton ECS component that holds the ring buffer of pathfinding results.
    /// Requests are submitted as <see cref="PathfindingRequestEvent"/> events via <see cref="FdpEventBus"/>;
    /// results are written here by <c>PathfindingResultMaterializationSystem</c> after the solver resolves them.
    /// Indexed by <c>requestId % DefaultCapacity</c> (modulo ring buffer).
    /// </summary>
    [ComponentId(GlobalComponentIds.PathfindingBatchData)]
    public struct PathfindingBatchData
    {
        /// <summary>Default pre-allocated capacity for concurrent pathfinding results.</summary>
        public const int DefaultCapacity = 64;

        /// <summary>Ring-buffer of results written by <c>PathfindingResultMaterializationSystem</c>.
        /// Indexed via <c>requestId % DefaultCapacity</c>.</summary>
        public NativeArray<PathResult>  Results;
    }
}
