using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Kernel;
using Fdp.Kernel.Collections;

namespace Fdp.Toolkit.Navigation
{
    // ── Path request / result structs ─────────────────────────────────────────────

    /// <summary>
    /// A single pathfinding request submitted by a Brain-tier entity.
    /// Stored in <see cref="PathfindingBatchData.Requests"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PathRequest
    {
        /// <summary>Monotonically increasing identifier echoed in the matching <see cref="PathResult"/>.</summary>
        public long    RequestId;

        /// <summary>Start position in FDP Cartesian metres. Translators convert to/from WGS-84 when publishing.</summary>
        public Vector3 Start;

        /// <summary>Goal position in FDP Cartesian metres.</summary>
        public Vector3 End;

        /// <summary>Mobility type: 0 = Wheeled, 1 = Tracked, 2 = Infantry.</summary>
        public byte    MobilityProfile;
    }

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
    }

    // ── Singleton ECS component ───────────────────────────────────────────────────

    /// <summary>
    /// Zero-allocation singleton ECS component that batches pathfinding requests and results
    /// each frame, mirroring the existing <c>RaycastBatchData</c> pattern.
    ///
    /// <para>Registered by <c>NavigationComponentRegistry.RegisterAll</c> (or
    /// <c>SimHostComponentRegistry.RegisterAll</c>) via
    /// <c>world.RegisterSingleton&lt;PathfindingBatchData&gt;</c>.</para>
    /// </summary>
    [ComponentId(GlobalComponentIds.PathfindingBatchData)]
    public struct PathfindingBatchData
    {
        /// <summary>Default pre-allocated capacity for requests and results per frame.</summary>
        public const int DefaultCapacity = 64;

        /// <summary>Number of valid request entries in <see cref="Requests"/> for the current frame.</summary>
        public int Count;

        /// <summary>Pre-allocated request buffer. Length == <see cref="DefaultCapacity"/>.</summary>
        public NativeArray<PathRequest> Requests;

        /// <summary>Pre-allocated result buffer. Length == <see cref="DefaultCapacity"/>.</summary>
        public NativeArray<PathResult>  Results;
    }
}
