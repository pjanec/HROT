using System.Runtime.InteropServices;
using System.Numerics;
using Fdp.Core;

namespace Fdp.Toolkit.Navigation
{
    /// <summary>
    /// Unmanaged ECS event published by Brain-tier BTree nodes to request a pathfinding solve.
    /// Consumed by <c>PathfindingSolverSystem</c> running at 10 Hz on a background thread.
    /// The <see cref="FdpEventBus"/> EventAccumulator buffers these events across frames so
    /// no requests are lost between slow solver ticks.
    /// </summary>
    [EventId(2032)]
    [StructLayout(LayoutKind.Sequential)]
    public struct PathfindingRequestEvent
    {
        /// <summary>Stable request identifier echoed in the matching <see cref="PathfindingResultEvent"/>.</summary>
        public long RequestId;
        /// <summary>Start position in FDP Cartesian metres.</summary>
        public Vector3 Start;
        /// <summary>Goal position in FDP Cartesian metres.</summary>
        public Vector3 End;
        /// <summary>Mobility type: 0 = Wheeled, 1 = Tracked, 2 = Infantry, 3 = Naval, 4 = Flying.</summary>
        public byte MobilityProfile;
        /// <summary>Force a specific backend (0 = Auto); see <see cref="NavigationBackend"/>.</summary>
        public NavigationBackend BackendForce;
        /// <summary>Padding to maintain natural alignment.</summary>
        public byte _pad1;
        public byte _pad2;
        /// <summary>Originating Brain node ID for routing responses back.</summary>
        public int SourceNodeId;
        /// <summary>Brain-allocated route handle (0 = anonymous; solver allocates its own handle).</summary>
        public int RouteHandle;
        /// <summary>Navmesh layer filter bitmask (0 = default layer).</summary>
        public int NavLayerMask;
        /// <summary>Maximum path cost; 0 = unlimited.</summary>
        public float MaxCost;
        /// <summary>Navmesh version at time of request, used for staleness detection.</summary>
        public int NavmeshVersionAtRequest;
    }

    /// <summary>
    /// Unmanaged ECS event published by <c>PathfindingSolverSystem</c> (via <see cref="IEntityCommandBuffer"/>)
    /// once a pathfinding request has been resolved.
    /// Consumed by <c>PathfindingResultMaterializationSystem</c> running synchronously on the main thread
    /// and by <c>PathResponseSolverEgressTranslator</c> for DDS forwarding in distributed deployments.
    /// </summary>
    [EventId(2033)]
    [StructLayout(LayoutKind.Sequential)]
    public struct PathfindingResultEvent
    {
        /// <summary>Echoed request identifier for correlation.</summary>
        public long RequestId;
        /// <summary><c>true</c> if a valid path was found.</summary>
        public bool IsReachable;
        /// <summary>Padding to maintain natural alignment.</summary>
        public byte _pad0;
        public byte _pad1;
        public byte _pad2;
        /// <summary>Total arc-length in metres, or 0 if unreachable.</summary>
        public float TotalDistanceMeters;
        /// <summary>Handle into the shared route/waypoint store; echoed from the request. -1 if unreachable.</summary>
        public int RouteHandle;
        /// <summary>Originating Brain node ID, propagated from the request.</summary>
        public int SourceNodeId;
        /// <summary>Navmesh version when the path was planned, for staleness detection.</summary>
        public int NavmeshVersionAtPlan;
        /// <summary>Reason the request failed; <see cref="NavigationFailureReason.NoFailure"/> on success.</summary>
        public NavigationFailureReason FailureReason;
        /// <summary>Backend that produced this path.</summary>
        public NavigationBackend PrimaryBackend;
        /// <summary>Padding to maintain natural alignment.</summary>
        public byte _pad3;
        public byte _pad4;
    }

    /// <summary>
    /// Fired by <c>PathfindingResultMaterializationSystem</c> when a reachable
    /// <c>MoveTo</c> path result is committed and the agent begins corridor-following.
    /// Consumed by executors and egress translators.
    /// </summary>
    [EventId(2034)]
    [StructLayout(LayoutKind.Sequential)]
    public struct MoveStartedEvent
    {
        /// <summary>Echoed from the originating <see cref="PathfindingRequestEvent"/>.</summary>
        public long RequestId;
        /// <summary>Handle of the corridor being followed.</summary>
        public int RouteHandle;
        /// <summary>Originating Brain node ID, propagated from the request.</summary>
        public int SourceNodeId;
    }

    /// <summary>
    /// Published by <see cref="Systems.OffMeshLinkDetectionSystem"/> when an entity begins
    /// an off-mesh traversal (jump, climb, door, fly).
    /// Downstream animation systems listen for this to trigger the appropriate montage.
    /// (EventId = 2035)
    /// </summary>
    [EventId(2035)]
    [StructLayout(LayoutKind.Sequential)]
    public struct OffMeshTraversalStartedEvent
    {
        /// <summary>The entity beginning the traversal.</summary>
        public Entity Target;

        /// <summary>World-space position of the off-mesh link start point.</summary>
        public System.Numerics.Vector3 LinkWorldPos;

        /// <summary>The kind of traversal (Jump, Climb, Door, Fly).</summary>
        public TraversalKind TraversalKind;

        // 3 bytes of explicit padding.
        private byte _pad0;
        private byte _pad1;
        private byte _pad2;
    }

    /// <summary>
    /// Fired by <see cref="Executors.MoveToExecutor"/> when a MoveTo command reaches a
    /// terminal state (Arrived, FailedBlocked, FailedUnreachable, NoPath, FailedInvalidHandle).
    /// </summary>
    [EventId(2036)]
    [StructLayout(LayoutKind.Sequential)]
    public struct MoveCompletedEvent
    {
        /// <summary>The entity whose navigation command completed.</summary>
        public Entity Target;

        /// <summary>Terminal outcome of the navigation command.</summary>
        public NavigationResult Reason;

        // 3 bytes of explicit padding so RouteHandle stays at offset 12.
        private byte _pad0;
        private byte _pad1;
        private byte _pad2;

        /// <summary>Route handle that was active when the command ended; 0 if not applicable.</summary>
        public int RouteHandle;
    }

    /// <summary>
    /// Fired by the progress tracker when a MoveTo is blocked and replanning is attempted.
    /// (Phase 5 emitter; struct defined here for catalog registration.)
    /// </summary>
    [EventId(2037)]
    [StructLayout(LayoutKind.Sequential)]
    public struct MoveBlockedEvent
    {
        /// <summary>The entity that is blocked.</summary>
        public Entity Target;

        /// <summary>Block reason code (reserved for Phase 5).</summary>
        public byte ReasonCode;

        private byte _pad0;
        private byte _pad1;
        private byte _pad2;

        private int _reserved;
    }

    /// <summary>
    /// Fired by the progress tracker when the agent advances past a waypoint segment.
    /// (Phase 5 emitter; struct defined here for catalog registration.)
    /// </summary>
    [EventId(2038)]
    [StructLayout(LayoutKind.Sequential)]
    public struct WaypointReachedEvent
    {
        /// <summary>The entity that reached the waypoint.</summary>
        public Entity Target;

        /// <summary>Zero-based index of the segment that was just completed.</summary>
        public int SegmentIndex;

        private int _reserved;
    }

    /// <summary>
    /// Fired by the progress tracker when the Muscle layer performs an automatic replan.
    /// (Phase 5 emitter; struct defined here for catalog registration.)
    /// </summary>
    [EventId(2039)]
    [StructLayout(LayoutKind.Sequential)]
    public struct PathReplannedEvent
    {
        /// <summary>The entity whose path was replanned.</summary>
        public Entity Target;

        /// <summary>Route handle of the replanned path.</summary>
        public int RouteHandle;

        /// <summary>Running replan count after this replan.</summary>
        public byte ReplanCount;

        private byte _pad0;
        private byte _pad1;
        private byte _pad2;
    }

    /// <summary>
    /// Fired when an entity finishes an off-mesh traversal and resumes normal following.
    /// (Phase 5 emitter; struct defined here for catalog registration.)
    /// </summary>
    [EventId(2040)]
    [StructLayout(LayoutKind.Sequential)]
    public struct OffMeshTraversalEndedEvent
    {
        /// <summary>The entity that completed the off-mesh traversal.</summary>
        public Entity Target;

        /// <summary>The kind of traversal that just ended.</summary>
        public TraversalKind Kind;

        private byte _pad0;
        private byte _pad1;
        private byte _pad2;

        private int _reserved;
    }

    /// <summary>
    /// Published by the Muscle-side path-details system when fresh path details are ready
    /// for ingestion into the Brain-side cache. Consumed by
    /// <see cref="Systems.NavigationPathDetailsUpdateSystem"/>.
    /// </summary>
    [EventId(2041)]
    [StructLayout(LayoutKind.Sequential)]
    public struct NavigationPathDetailsResponseEvent
    {
        /// <summary>The Brain entity that requested the path details.</summary>
        public Entity Target;

        /// <summary>Route handle whose details are ready in the Muscle-side registry.</summary>
        public int RouteHandle;

        /// <summary>Replan count at the time the path was snapshotted.</summary>
        public byte ReplanCount;

        /// <summary>1 = triggered automatically by a replan; 0 = explicit FetchPathDetails command.</summary>
        public byte IsAutoRefresh;

        private byte _pad0;
        private byte _pad1;
    }

    /// <summary>
    /// Emitted by <see cref="Systems.NavigationPathDetailsUpdateSystem"/> after the Brain-side
    /// path cache has been populated for a given entity/handle pair.
    /// </summary>
    [EventId(2042)]
    [StructLayout(LayoutKind.Sequential)]
    public struct NavigationPathDetailsArrivedEvent
    {
        /// <summary>The entity whose Brain-side cache was just updated.</summary>
        public Entity Target;

        /// <summary>Route handle that is now cached.</summary>
        public int RouteHandle;

        /// <summary>1 = this was an auto-refresh; 0 = explicit fetch.</summary>
        public byte IsAutoRefresh;

        private byte _pad0;
        private byte _pad1;
        private byte _pad2;
    }
}
