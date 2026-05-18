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
    /// Instructs the executor to navigate to a fixed 2-D destination.
    /// </summary>
    /// <remarks>
    /// <b>Size:</b> Vector2 (8 bytes) + float (4) + float (4) + byte (1) + padding (3) = 20 bytes.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct MoveToParams
    {
        /// <summary>Target position on the XY ground plane (metres).</summary>
        public Vector2 Destination;

        /// <summary>Distance (metres) from <see cref="Destination"/> that counts as arrival.</summary>
        public float ArrivalRadius;

        /// <summary>Desired travel speed (m/s).</summary>
        public float Speed;

        /// <summary>
        /// When 1, the muscle tier is allowed to drive in reverse to reach the destination.
        /// Forwarded by <c>MoveToExecutor</c> into <see cref="NavigationIntent"/> and
        /// applied to <c>NavState.ReverseAllowed</c> by <c>NavigationIntentBridgeSystem</c>.
        /// </summary>
        public byte ReverseAllowed;

        // 3 bytes of explicit padding to keep the struct naturally aligned.
        private byte _pad0;
        private byte _pad1;
        private byte _pad2;
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
}
