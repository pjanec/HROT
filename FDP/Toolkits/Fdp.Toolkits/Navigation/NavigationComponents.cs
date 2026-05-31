using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;

// DB-MOD1-23: NavigationIntent and NavigationStatus moved from Fdp.Core/CoreComponents/NavigationComponents.cs
// into this thin contracts assembly so that both FDP.Toolkit.Navigation and FDP.Toolkit.CarKinem can
// reference them without creating a circular assembly dependency.

namespace Fdp.Toolkit.Navigation
{
    // ── Engine-side enums ────────────────────────────────────────────────────
    /// <summary>
    /// Engine-side navigation mode.  Carried by <see cref="NavigationIntent"/>
    /// and written by <c>MoveToExecutor</c> on the Brain side.
    /// </summary>
    /// <remarks>
    /// <c>None = 0</c> means the component is inactive.  A zero-initialised
    /// <see cref="NavigationIntent"/> struct is therefore always idle by default.
    /// </remarks>
    public enum NavigationMode : byte
    {
        /// <summary>No active navigation command (idle / not yet assigned).</summary>
        None = 0,

        /// <summary>Drive directly to <see cref="NavigationIntent.FinalDestination"/>.</summary>
        DirectPoint = 1,

        /// <summary>Follow a pre-computed route.</summary>
        FollowRoute = 2,

        /// <summary>Join and maintain a formation slot.</summary>
        JoinFormation = 3,

        /// <summary>Navigate to a target road-graph node using the road network.</summary>
        RoadGraph = 4,
    }

    /// <summary>
    /// Engine-side navigation result.  Carried by <see cref="NavigationStatus"/>
    /// and written by the Muscle layer (<c>NavigationExecutionSystem</c>).
    /// </summary>
    /// <remarks>
    /// <c>InProgress = 0</c> means the command is actively being executed.
    /// A zero-initialised <see cref="NavigationStatus"/> is therefore always
    /// in-progress by default, matching the uninitialised state.
    /// </remarks>
    public enum NavigationResult : byte
    {
        /// <summary>Command received and execution ongoing.</summary>
        InProgress = 0,

        /// <summary>Entity arrived within <see cref="NavigationIntent.ArrivalRadius"/>.</summary>
        Arrived = 1,

        /// <summary>Execution failed — entity is blocked and cannot progress.</summary>
        FailedBlocked = 2,

        /// <summary>Execution failed — destination is unreachable.</summary>
        FailedUnreachable = 3,

        /// <summary>Path found and route handle allocated; executor may transition to FollowPath.</summary>
        PathFound = 4,

        /// <summary>Solver could not find any path to the destination.</summary>
        NoPath = 5,

        /// <summary>Execution failed — the requested navmesh layer is not loaded.</summary>
        FailedNoLayer = 6,

        /// <summary>Execution failed — the supplied route handle is invalid or expired.</summary>
        FailedInvalidHandle = 7,
    }

    /// <summary>
    /// Execution phase of the active navigation command, written by the Muscle tier.
    /// </summary>
    public enum NavigationPhase : byte
    {
        /// <summary>No active command (idle).</summary>
        Idle = 0,

        /// <summary>Path request sent to solver; awaiting reply.</summary>
        AwaitingPath = 1,

        /// <summary>Path received; entity is following the corridor.</summary>
        Following = 2,

        /// <summary>Paused at a waypoint, awaiting traversal action (e.g., door open).</summary>
        AwaitingTraversal = 3,

        /// <summary>Destination reached; command finalised.</summary>
        Completed = 4,
    }

    /// <summary>
    /// How an agent traverses the segment leading to a waypoint.
    /// </summary>
    public enum TraversalKind : byte
    {
        /// <summary>Standard ground locomotion.</summary>
        Walk = 0,

        /// <summary>Agent must jump to reach the waypoint.</summary>
        Jump = 1,

        /// <summary>Agent climbs a ladder or wall section.</summary>
        Climb = 2,

        /// <summary>Agent passes through a door or gate.</summary>
        Door = 3,

        /// <summary>Agent uses aerial locomotion.</summary>
        Fly = 4,
    }

    /// <summary>
    /// Surface type at a waypoint.
    /// </summary>
    public enum SurfaceType : byte
    {
        /// <summary>No specific surface type (generic ground).</summary>
        Generic = 0,

        /// <summary>Paved road.</summary>
        Road = 1,

        /// <summary>Natural terrain (grass, dirt, sand, etc.).</summary>
        Terrain = 2,

        /// <summary>Water surface or underwater.</summary>
        Water = 3,

        /// <summary>Indoor flooring.</summary>
        Indoor = 4,
    }

    /// <summary>
    /// Pathfinding backend used to compute a route.
    /// </summary>
    public enum NavigationBackend : byte
    {
        /// <summary>System selects the most appropriate backend automatically.</summary>
        Auto = 0,

        /// <summary>Road-graph Dijkstra over <c>RoadNetworkBlob</c>.</summary>
        NavRoadGraph = 1,

        /// <summary>Navmesh A* via <see cref="INavmeshProvider"/>.</summary>
        Navmesh = 2,

        /// <summary>Hybrid: road-graph for macro routing, navmesh for local correction.</summary>
        Hybrid = 3,

        /// <summary>3-D volumetric pathfinding for aerial/sub-surface agents via <c>IVolumetricPathProvider</c>.</summary>
        Volumetric = 4,
    }

    /// <summary>
    /// Reason a pathfinding request failed.  <see cref="NoFailure"/> when the path succeeded.
    /// </summary>
    public enum NavigationFailureReason : byte
    {
        /// <summary>No failure; path was found successfully.</summary>
        NoFailure = 0,

        /// <summary>Destination is not reachable from the origin.</summary>
        Unreachable = 1,

        /// <summary>Solver budget was exceeded before a path was found.</summary>
        Timeout = 2,

        /// <summary>The supplied route handle is invalid or expired.</summary>
        InvalidHandle = 3,

        /// <summary>The backend provider returned an error.</summary>
        ProviderError = 4,
    }

    // ── ECS component structs ────────────────────────────────────────────────

    /// <summary>
    /// CQRS <em>command</em> component — owned by the Brain node.
    /// Written by <c>MoveToExecutor.OnEnter</c>; consumed by the Muscle layer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="FinalDestination"/> is a Cartesian <see cref="Vector3"/> (metres, Sim Z-up;
    /// 3D promotion P3D-302). Altitude is carried for fidelity/translators; vehicle steering
    /// remains 2D-projected (§0.2). Geographic conversion is the translator's responsibility,
    /// never the executor's.
    /// </para>
    /// <para>
    /// <see cref="Mode"/> defaults to <see cref="NavigationMode.None"/> for a
    /// zero-initialised struct, so an entity without an active command is
    /// always idle.
    /// </para>
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(NavigationContractsComponentIds.NavigationIntent)]
    public struct NavigationIntent
    {
        /// <summary>Active navigation mode; <see cref="NavigationMode.None"/> = inactive.</summary>
        public NavigationMode Mode;

        /// <summary>
        /// Behavioural flags for the active navigation command.
        /// Bit 0: AllowReplan. Bit 4: AutoSendPathOnReplan.
        /// Copied from <see cref="MoveToParams.Flags"/> by <c>MoveToExecutor</c>.
        /// </summary>
        public byte Flags;

        /// <summary>
        /// Maximum internal Muscle replans for this command (0 = use
        /// <see cref="NavigationConstants.DefaultMaxReplans"/>).
        /// Copied from <see cref="MoveToParams.MaxReplans"/> by <c>MoveToExecutor</c>.
        /// </summary>
        public byte MaxReplans;

        /// <summary>
        /// When 1, the muscle tier is allowed to drive in reverse to reach the destination.
        /// Written by <c>MoveToExecutor.OnEnter</c> from <see cref="MoveToParams.ReverseAllowed"/>
        /// and applied to <c>NavState.ReverseAllowed</c> by <c>NavigationIntentBridgeSystem</c>.
        /// </summary>
        public byte ReverseAllowed;

        /// <summary>Target position in FDP Cartesian metres (Sim Z-up; Z carried, steering 2D-projected).</summary>
        public Vector3 FinalDestination;

        /// <summary>Desired travel speed (m/s).</summary>
        public float TargetSpeed;

        /// <summary>Distance from <see cref="FinalDestination"/> that counts as arrival (metres).</summary>
        public float ArrivalRadius;

        /// <summary>
        /// Monotonically incremented per new navigation order.
        /// The Muscle layer echoes this value in <see cref="NavigationStatus.IntentId"/>
        /// to allow the Brain to detect stale status reports.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Monotonic-id contract:</b> every call to <c>OnEnter</c> or <c>OnExit</c> in
        /// any navigation executor increments this field before writing the component back.
        /// The increment is also the mechanism used by <c>FollowRouteExecutor</c> to signal
        /// a <em>loop reset</em>: when the route finishes and <c>IsLooped != 0</c>, the
        /// executor increments <c>IntentId</c> again (without changing any other field).
        /// <c>NavigationIntentBridgeSystem</c> detects the new id and resets
        /// <c>NavState.ProgressS</c> to 0, restarting the route from the beginning.
        /// <c>NavigationExecutionSystem</c> detects the new id and resets the
        /// <c>NavigationStatus</c> to <see cref="NavigationResult.InProgress"/>, so the
        /// executor does not see the stale <c>Arrived</c> result from the previous lap.
        /// </para>
        /// <para>
        /// The id wraps at <c>uint.MaxValue + 1 == 0</c> (unchecked arithmetic).  Id 0 is
        /// intentionally valid; the system only checks <em>equality with the last applied
        /// value</em>, so a wrap-around reset is handled correctly.
        /// </para>
        /// </remarks>
        public uint IntentId;

        // BS1-T019: Road-graph target (used when Mode == NavigationMode.RoadGraph).
        /// <summary>Target road-graph node index; populated by <c>FollowRoadGraphExecutor</c>.</summary>
        public int TargetNodeId;

        // BS1-T020: Follow-route fields (used when Mode == NavigationMode.FollowRoute).
        /// <summary>Trajectory pool ID; populated by <c>FollowRouteExecutor</c>.</summary>
        public int TrajectoryId;

        /// <summary>
        /// Route handle allocated by the nav subsystem v2 solver.
        /// 0 = no handle (fire-and-forget mode). Written by <c>MoveToExecutor</c> from
        /// <see cref="MoveToParams.RouteHandle"/> and by <c>PlanRouteExecutor</c>.
        /// </summary>
        public int RouteHandle;

        /// <summary>
        /// Maximum wall-clock time (seconds) the entity is allowed to spend replanning
        /// for this intent. 0 = no time limit (rely on <see cref="MaxReplans"/> only).
        /// Checked by <c>NavigationExecutionSystem</c> before issuing each replan.
        /// Set by <c>MoveToExecutor.OnEnter</c>; defaults to 0 (unlimited).
        /// </summary>
        public float ReplanTimeBudget;
    }

    /// <summary>
    /// CQRS <em>status</em> component — owned by the Muscle node.
    /// Written by <c>NavigationExecutionSystem</c>; observed by <c>MoveToExecutor.Execute</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(NavigationContractsComponentIds.NavigationStatus)]
    [DataPolicy(DataPolicy.NoSave)]
    public struct NavigationStatus
    {
        /// <summary>
        /// Echoes the <see cref="NavigationIntent.IntentId"/> being executed.
        /// When <c>IntentId != intent.IntentId</c> the status is stale and must be ignored.
        /// </summary>
        public uint IntentId;

        /// <summary>
        /// Current result of the active navigation command.
        /// Defaults to <see cref="NavigationResult.InProgress"/> for a zero-initialised struct.
        /// </summary>
        public NavigationResult Result;

        /// <summary>
        /// Arc-length progress along the active route (metres from start).
        /// Mirrors <c>NavState.ProgressS</c> written by the Muscle tier so Brain-only nodes
        /// can read route progress via the CQRS feedback channel without querying NavState.
        /// Populated by <c>NavigationExecutionSystem</c>; mapped to the wire by
        /// <c>NavigationStatusEgressTranslator</c> and <c>NavigationStatusIngressTranslator</c>.
        /// </summary>
        public float ProgressS;

        /// <summary>Current execution phase; written by the Muscle tier.</summary>
        public NavigationPhase Phase;

        /// <summary>
        /// The traversal kind of the off-mesh link currently being traversed.
        /// Walk = 0 (no active off-mesh traversal).
        /// Written by <c>OffMeshLinkDetectionSystem</c>.
        /// </summary>
        public TraversalKind CurrentTraversalKind;

        /// <summary>Result code from the most recent failure (InProgress when no failure has occurred).</summary>
        public NavigationResult LastFailureReason;

        /// <summary>Number of times the path has been replanned for the current intent.</summary>
        public ushort ReplanCount;

        /// <summary>Route handle currently being followed; 0 = none.</summary>
        public int RouteHandle;

        /// <summary>Estimated time to arrival (seconds); 0 = unknown.</summary>
        public float EstimatedTimeRemaining;

        /// <summary>Navmesh version observed when the current path was planned.</summary>
        public uint NavmeshVersionObserved;
    }

    // ── Nav subsystem v2 component structs (NAV-P0-T5) ───────────────────────

    /// <summary>
    /// Agent locomotion profile used by the nav solver to select the correct navmesh layer
    /// and compute physically plausible paths.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(NavigationContractsComponentIds.NavAgentProfile)]
    public struct NavAgentProfile
    {
        /// <summary>Bitfield of navmesh layers this agent can traverse. 0xFFFFFFFF = all layers.</summary>
        public uint PreferredLayerMask;

        /// <summary>Physical radius of the agent capsule (metres). Used for corridor clearance checks.</summary>
        public float AgentRadius;

        /// <summary>Physical height of the agent capsule (metres).</summary>
        public float AgentHeight;

        /// <summary>Maximum traversable slope angle in degrees.</summary>
        public float MaxSlopeDeg;

        /// <summary>
        /// Locomotion profile: 0 = Wheeled/Ground (default), 4 = Flying (routes via volumetric provider).
        /// </summary>
        public byte MobilityProfile;
    }

    /// <summary>
    /// Muscle-owned runtime state for the active navigation corridor.
    /// Destroyed and recreated each time a new route handle is accepted.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(NavigationContractsComponentIds.NavigationCorridorMuscle)]
    [DataPolicy(DataPolicy.NoSave)]
    public struct NavigationCorridorMuscle
    {
        /// <summary>Handle to the active route in the path registry. 0 = no active route.</summary>
        public int RouteHandle;

        /// <summary>Navmesh version when the current path was planned.</summary>
        public uint NavmeshVersion;

        /// <summary>Index of the segment the agent is currently traversing.</summary>
        public int CurrentSegmentIndex;

        /// <summary>Total number of segments in the active path.</summary>
        public int TotalSegmentCount;

        /// <summary>Total arc-length of the planned path (metres).</summary>
        public float TotalDistance;

        /// <summary>Primary backend that produced this path (0=NavMesh, 1=RoadGraph, 2=Volumetric).</summary>
        public byte PrimaryBackend;

        /// <summary>Internal corridor flags (reserved for Muscle use).</summary>
        public byte Flags;

        // 2 bytes of explicit padding.
        private byte _pad0;
        private byte _pad1;
    }

    /// <summary>
    /// A single waypoint in a <see cref="NavigationCorridorPreview"/> buffer.
    /// </summary>
    /// <remarks><b>Size:</b> Vector3 (12) + byte (1) + byte (1) + 2 pad = 16 bytes.</remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct PreviewWaypoint
    {
        /// <summary>World-space position of the waypoint.</summary>
        public System.Numerics.Vector3 Position;

        /// <summary>How the agent traverses the edge leading to this waypoint.</summary>
        public TraversalKind Traversal;

        /// <summary>Surface type at this waypoint.</summary>
        public SurfaceType Surface;

        // 2 bytes of explicit padding.
        private byte _pad0;
        private byte _pad1;
    }

    /// <summary>
    /// Brain-readable look-ahead view of the first 8 waypoints in the active corridor.
    /// Updated by the Muscle tier whenever the corridor advances.
    /// </summary>
    /// <remarks>
    /// <b>Size:</b> uint (4) + int (4) + int (4) + int (4) = 16 bytes header
    /// + 8 x <see cref="PreviewWaypoint"/> (8 x 16 = 128 bytes) = 144 bytes total.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(NavigationContractsComponentIds.NavigationCorridorPreview)]
    public struct NavigationCorridorPreview
    {
        /// <summary>Incremented each time this buffer is refreshed; allows Brain to detect staleness.</summary>
        public uint PreviewVersion;

        /// <summary>Number of valid entries in W0..W7 (0–8).</summary>
        public int WaypointCount;

        /// <summary>Global segment index corresponding to W0 (for stitching into the full corridor).</summary>
        public int GlobalSegmentStart;

        // 4 bytes of explicit padding.
        private int _pad;

        /// <summary>Waypoint 0 (nearest to current position).</summary>
        public PreviewWaypoint W0;
        /// <summary>Waypoint 1.</summary>
        public PreviewWaypoint W1;
        /// <summary>Waypoint 2.</summary>
        public PreviewWaypoint W2;
        /// <summary>Waypoint 3.</summary>
        public PreviewWaypoint W3;
        /// <summary>Waypoint 4.</summary>
        public PreviewWaypoint W4;
        /// <summary>Waypoint 5.</summary>
        public PreviewWaypoint W5;
        /// <summary>Waypoint 6.</summary>
        public PreviewWaypoint W6;
        /// <summary>Waypoint 7 (furthest look-ahead).</summary>
        public PreviewWaypoint W7;
    }

    /// <summary>
    /// Holds a snapshot of full waypoint data fetched from the path registry by
    /// <c>FetchPathDetailsParams</c>. Written by the Muscle tier; read by Brain nodes
    /// that need more than the 8-waypoint preview.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(NavigationContractsComponentIds.NavigationPathDetailsBuffer)]
    public struct NavigationPathDetailsBuffer
    {
        /// <summary>Route handle this buffer corresponds to.</summary>
        public int RouteHandle;

        /// <summary>Replan count at the time of the fetch (for staleness detection).</summary>
        public ushort ReplanCountAtFetch;

        /// <summary>Total number of waypoints in the fetched path.</summary>
        public ushort WaypointCount;

        /// <summary>Total arc-length of the fetched path (metres).</summary>
        public float TotalDistance;
    }

    /// <summary>
    /// Tag component indicating this entity is managed by the Detour crowd simulation.
    /// Presence of this component opts the entity into crowd-avoidance updates.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(NavigationContractsComponentIds.CrowdAgent)]
    public struct CrowdAgent
    {
    }
}
