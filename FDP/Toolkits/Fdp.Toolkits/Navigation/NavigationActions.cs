using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Navigation
{
    // ────────────────────────────────────────────────────────────────────────────
    // Navigation action parameter structs
    //
    // ALL structs are:
    //   • unmanaged  — no GC allocation, safe for stackalloc / NativeArray
    //   • [StructLayout(LayoutKind.Sequential)]  — deterministic field ordering
    //   • ≤ 32 bytes  — fit within the LocomotionChannel payload limit
    //
    // Phase 0 convention for positions:
    //   MoveToParams.Destination and FleeParams-derived positions are Vector2 (XY ground plane).
    //   When a BTree/HSM node populates these, it must project the 3-D world target:
    //       new Vector2(target.SimTransform.Position.X, target.SimTransform.Position.Y)
    //   No struct changes are required; this is purely a usage convention.
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parameters for the <see cref="NavigationConstants.ActionIdMoveTo"/> action.
    /// Instructs the executor to navigate to a fixed destination (Sim Z-up).
    /// </summary>
    /// <remarks>
    /// <b>Size:</b> Vector3 (12) + float (4) + float (4) + int (4) + uint (4) + 4×byte (4) = 32 bytes.
    /// The destination was widened to <see cref="Vector3"/> for the 3D Cognitive Spatial Awareness
    /// promotion (P3D-302); the four trailing single-byte fields are packed into the reclaimed
    /// padding so the struct stays at exactly <see cref="BehaviorConstants.ActionParamsByteSize"/>
    /// (32) bytes and still fits the LocomotionChannel <c>Params</c> slot.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct MoveToParams
    {
        /// <summary>Target position (metres, Sim Z-up). Z carried; steering remains 2D-projected.</summary>
        public Vector3 Destination;

        /// <summary>Distance (metres) from <see cref="Destination"/> that counts as arrival.</summary>
        public float ArrivalRadius;

        /// <summary>Desired travel speed (m/s).</summary>
        public float Speed;

        /// <summary>
        /// Pre-allocated route handle (0 = fire-and-forget; solver allocates its own).
        /// When non-zero, the Muscle reuses this handle on replan.
        /// </summary>
        public int RouteHandle;

        /// <summary>Navmesh layer mask. 0xFFFFFFFF = all layers.</summary>
        public uint LayerMask;

        /// <summary>
        /// When 1, the muscle tier is allowed to drive in reverse to reach the destination.
        /// Forwarded by <c>MoveToExecutor</c> into <see cref="NavigationIntent"/> and
        /// applied to <c>NavState.ReverseAllowed</c> by <c>NavigationIntentBridgeSystem</c>.
        /// </summary>
        public byte ReverseAllowed;

        /// <summary>
        /// Behavioural flags for the MoveTo action.
        /// Bit 0: AllowReplan — Muscle is allowed to internally replan on frustration.
        /// Bit 4: AutoSendPathOnReplan — Each internal replan also fires
        ///         <see cref="NavigationPathDetailsResponseEvent"/> with IsAutoRefresh=true.
        /// </summary>
        public byte Flags;

        /// <summary>
        /// Maximum number of internal Muscle replans before hard failure (0 = use
        /// <see cref="NavigationConstants.DefaultMaxReplans"/>).
        /// </summary>
        public byte MaxReplans;

        /// <summary>Force a specific backend (0 = Auto, 1 = NavMesh, 2 = RoadGraph, 3 = Volumetric).</summary>
        public byte BackendForce;
    }

    /// <summary>
    /// Parameters for the <see cref="NavigationConstants.ActionIdFlee"/> action.
    /// Instructs the executor to move away from the given threat entity.
    /// </summary>
    /// <remarks>
    /// <b>Size:</b> Entity (8 bytes) + float (4) + float (4) = 16 bytes.
    /// <b>IMPORTANT:</b> <see cref="Threat"/> stores the full <see cref="Entity"/> handle
    /// (Index + Generation) — never a raw <c>int</c> index — to preserve generational safety.
    /// If the threat entity is destroyed mid-flee, <c>FleeExecutor</c> must detect the stale
    /// handle via <c>view.IsAlive(Threat)</c> and report Failure.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct FleeParams
    {
        /// <summary>
        /// The entity to flee from.
        /// Full <see cref="Entity"/> handle (Index + Generation).
        /// Check <c>view.IsAlive(Threat)</c> before accessing position.
        /// </summary>
        public Entity Threat;

        /// <summary>Distance (metres) from the threat that is considered safe.</summary>
        public float SafeDistance;

        /// <summary>Desired travel speed (m/s) while fleeing.</summary>
        public float Speed;
    }

    /// <summary>
    /// Per-tick state maintained by the flee executor between replan intervals.
    /// </summary>
    /// <remarks>
    /// <b>Size:</b> uint (4 bytes) = 4 bytes.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct FleeState
    {
        /// <summary>Simulation tick on which the next destination replan should occur.</summary>
        public uint NextReplanTick;
    }

    /// <summary>
    /// Parameters for the <see cref="NavigationConstants.ActionIdFollowRoute"/> action.
    /// Instructs the executor to follow a pre-computed trajectory from the trajectory pool.
    /// </summary>
    /// <remarks>
    /// <b>Size:</b> int (4 bytes) + byte (1 byte) + 3 bytes implicit padding = 8 bytes.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct FollowRouteParams
    {
        /// <summary>ID of the trajectory in the trajectory pool to follow.</summary>
        public int TrajectoryId;

        /// <summary>Non-zero if the route should loop back to the start on completion.</summary>
        public byte IsLooped;

        // 3 bytes of implicit Sequential padding; struct total = 8 bytes (int + byte + 3 pad).
    }

    /// <summary>
    /// Parameters for the <see cref="NavigationConstants.ActionIdFollowRoadGraph"/> action.
    /// Instructs the executor to navigate to the given road graph node.
    /// </summary>
    /// <remarks>
    /// <b>Size:</b> int (4 bytes) + float (4 bytes) = 8 bytes.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct FollowRoadGraphParams
    {
        /// <summary>Index of the target node in the road network graph.</summary>
        public int TargetNodeId;

        /// <summary>Desired travel speed (m/s) while traversing the road graph.</summary>
        public float Speed;
    }

    /// <summary>
    /// Parameters for the <see cref="NavigationConstants.ActionIdPlanRoute"/> action.
    /// Requests the nav subsystem v2 solver to plan a path and return a route handle.
    /// </summary>
    /// <remarks>
    /// <b>Size:</b> Vector3 (12) + float (4) + float (4) + uint (4) + float (4) + byte (1) + byte (1) + 2 pad = 32 bytes.
    /// Destination widened to <see cref="Vector3"/> (Sim Z-up) for the 3D promotion (P3D-302); the
    /// reserved trailing word is reclaimed so the struct stays at 32 bytes.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct PlanRouteParams
    {
        /// <summary>Target position (metres, Sim Z-up). Z carried; steering remains 2D-projected.</summary>
        public Vector3 Destination;

        /// <summary>Distance (metres) from <see cref="Destination"/> that counts as arrival.</summary>
        public float ArrivalRadius;

        /// <summary>Desired travel speed (m/s).</summary>
        public float Speed;

        /// <summary>Navmesh layer mask. 0xFFFFFFFF = all layers.</summary>
        public uint LayerMask;

        /// <summary>Maximum path cost; 0 = unlimited.</summary>
        public float MaxCost;

        /// <summary>Force a specific backend (0 = Auto, 1 = NavMesh, 2 = RoadGraph, 3 = Volumetric).</summary>
        public byte BackendForce;

        /// <summary>When 1, populates <c>NavigationPathDetailsBuffer</c> with full waypoint data.</summary>
        public byte IncludeFullPathDetails;

        // 2 bytes of explicit padding to reach 32 bytes total.
        private byte _pad0;
        private byte _pad1;
    }

    /// <summary>
    /// Parameters for the <see cref="NavigationConstants.ActionIdFollowPath"/> action.
    /// Instructs the executor to follow a previously planned path by route handle.
    /// </summary>
    /// <remarks>
    /// <b>Size:</b> int (4) + byte (1) + 3 pad + float (4) + float (4) + ulong (8) + ulong (8) = 32 bytes.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct FollowPathParams
    {
        /// <summary>Route handle returned by a prior <see cref="NavigationConstants.ActionIdPlanRoute"/> action.</summary>
        public int RouteHandle;

        /// <summary>When 1, the muscle tier is allowed to drive in reverse.</summary>
        public byte ReverseAllowed;

        // 3 bytes of explicit padding.
        private byte _pad0;
        private byte _pad1;
        private byte _pad2;

        /// <summary>Desired travel speed (m/s).</summary>
        public float Speed;

        /// <summary>Distance (metres) from the final waypoint that counts as arrival.</summary>
        public float ArrivalRadius;

        // 16 bytes reserved for future use.
        private ulong _reserved0;
        private ulong _reserved1;
    }

    /// <summary>
    /// Parameters for the <see cref="NavigationConstants.ActionIdFetchPathDetails"/> action.
    /// Requests detailed waypoint data for a route into <c>NavigationPathDetailsBuffer</c>.
    /// </summary>
    /// <remarks>
    /// <b>Size:</b> int (4) + byte (1) + 3 pad + ulong (8) + ulong (8) + ulong (8) = 32 bytes.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct FetchPathDetailsParams
    {
        /// <summary>Route handle to fetch details for.</summary>
        public int RouteHandle;

        /// <summary>When 1, the fetch is non-blocking; the buffer is populated asynchronously.</summary>
        public byte NonBlocking;

        // 3 bytes of explicit padding.
        private byte _pad0;
        private byte _pad1;
        private byte _pad2;

        // 24 bytes reserved for future use.
        private ulong _reserved0;
        private ulong _reserved1;
        private ulong _reserved2;
    }

    /// <summary>
    /// Parameters for the <see cref="NavigationConstants.ActionIdReleasePath"/> action.
    /// Releases a route handle back to the solver pool.
    /// </summary>
    /// <remarks>
    /// <b>Size:</b> int (4) + uint (4) + ulong (8) + ulong (8) + ulong (8) = 32 bytes.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct ReleasePathParams
    {
        /// <summary>Route handle to release.</summary>
        public int RouteHandle;

        // 4 bytes of explicit padding.
        private uint _pad0;

        // 24 bytes reserved for future use.
        private ulong _reserved0;
        private ulong _reserved1;
        private ulong _reserved2;
    }
}
