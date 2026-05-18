using System.Runtime.InteropServices;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Physics.Components;

namespace Fdp.Toolkit.Physics
{
    /// <summary>
    /// Unmanaged ECS event published by Brain-tier systems to request a raycast solve.
    /// Consumed by <c>RaycastSolverSystem</c> running on a frame-synced background thread
    /// (parallel to the main thread, result available by next tick).
    /// </summary>
    [EventId(2030)]
    [StructLayout(LayoutKind.Sequential)]
    public struct RaycastRequestEvent
    {
        /// <summary>World-space start point.</summary>
        public Vector3 Start;
        /// <summary>World-space end point.</summary>
        public Vector3 End;
        /// <summary>Packed ray identifier (see <see cref="PhysicsConstants.PackBulletRayId"/> and PackLosRayId).</summary>
        public long RayId;
        /// <summary>Entity to ignore during the cast (e.g. the shooter).</summary>
        public Entity IgnoreEntity;
        /// <summary>For LOS rays: the observer entity.</summary>
        public Entity Observer;
        /// <summary>For LOS rays: the target entity.</summary>
        public Entity Target;
        /// <summary>Layer bitmask; entity is hit only if (LayerMask &amp; CollisionLayer) != 0.</summary>
        public int LayerMask;
        /// <summary>Originating Brain node ID for routing responses back.</summary>
        public int SourceNodeId;
    }

    /// <summary>
    /// Unmanaged ECS event published by <c>RaycastSolverSystem</c> (via <see cref="IEntityCommandBuffer"/>)
    /// once a raycast has been resolved.
    /// Consumed by <c>RaycastResultMaterializationSystem</c> (main-thread) and by network egress translators.
    /// </summary>
    [EventId(2031)]
    [StructLayout(LayoutKind.Sequential)]
    public struct RaycastResultEvent
    {
        /// <summary>The resolved hit data, including <see cref="RaycastHit.RayId"/> for correlation.</summary>
        public RaycastHit Hit;
    }
}
