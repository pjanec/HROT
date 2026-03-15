using System.Numerics;
using System.Runtime.InteropServices;

// ─────────────────────────────────────────────────────────────────────────────
// NavigationIntent and NavigationStatus live in Fdp.Kernel (rather than
// FDP.Toolkit.Navigation) for the same reason HealthData lives here: to
// prevent a circular assembly dependency.
//
//   FDP.Toolkit.Navigation → FDP.Toolkit.CarKinem  (FollowRouteExecutor etc.)
//   FDP.Toolkit.CarKinem   → Fdp.Kernel             (already)
//
// Placing the contracts in Fdp.Kernel lets both toolkits share them without
// either referencing the other.  The C# namespace stays FDP.Toolkit.Navigation
// to match the design document (§3.1.1 and §2.5).
// ─────────────────────────────────────────────────────────────────────────────

// GlobalComponentIds, ComponentIdAttribute, etc. live in the Fdp.Kernel namespace.
// Files in the Fdp.Kernel *project* can use them without a using directive only if
// they are in the same namespace.  Since this file uses a different namespace
// (FDP.Toolkit.Navigation) the explicit using is required.
using Fdp.Kernel;

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
    [ComponentId(GlobalComponentIds.NavigationIntent)]
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
        public uint IntentId;
    }

    /// <summary>
    /// CQRS <em>status</em> component — owned by the Muscle node.
    /// Written by <c>NavigationExecutionSystem</c>; observed by <c>MoveToExecutor.Execute</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.NavigationStatus)]
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
    }
}
