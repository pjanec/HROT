using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Kernel;

// DB-MOD1-23: NavigationIntent and NavigationStatus moved from Fdp.Kernel/CoreComponents/NavigationComponents.cs
// into this thin contracts assembly so that both FDP.Toolkit.Navigation and FDP.Toolkit.CarKinem can
// reference them without creating a circular assembly dependency.

namespace FDP.Toolkit.Navigation
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
    }

    // ── ECS component structs ────────────────────────────────────────────────

    /// <summary>
    /// CQRS <em>command</em> component — owned by the Brain node.
    /// Written by <c>MoveToExecutor.OnEnter</c>; consumed by the Muscle layer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="FinalDestination"/> is always a Cartesian <see cref="Vector2"/>
    /// (metres, FDP flat-earth XY plane).  Geographic conversion is the
    /// translator's responsibility, never the executor's.
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

        // 3 bytes padding (sequential layout; not blittable as a union anyway)
        private byte _pad0;
        private byte _pad1;
        private byte _pad2;

        /// <summary>Target position in FDP Cartesian metres (XY ground plane).</summary>
        public Vector2 FinalDestination;

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
    }

    /// <summary>
    /// CQRS <em>status</em> component — owned by the Muscle node.
    /// Written by <c>NavigationExecutionSystem</c>; observed by <c>MoveToExecutor.Execute</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(NavigationContractsComponentIds.NavigationStatus)]
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
    }
}
