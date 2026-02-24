using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Kernel;
using Fdp.Kernel.Collections;

namespace FDP.Toolkit.Physics.Components
{
    // ── PhysicsCollider ───────────────────────────────────────────────────────────

    /// <summary>
    /// Bounding-circle collider for broadphase and raycast intersection tests.
    /// Used by <see cref="Systems.RaycastSolverSystem"/> to check which entities
    /// a ray can hit.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PhysicsCollider
    {
        /// <summary>Radius of the bounding circle (metres). Used by Intersection2D.RaycastCircle.</summary>
        public float Radius;

        /// <summary>
        /// Layer bitmask. Rays only hit this entity if (request.LayerMask &amp; CollisionLayer) != 0.
        /// </summary>
        public int CollisionLayer;
    }

    // ── RaycastRequest ────────────────────────────────────────────────────────────

    /// <summary>
    /// One ray submitted to the <see cref="Systems.RaycastSolverSystem"/> for resolution.
    /// Added to <see cref="RaycastBatchData.Requests"/> each frame before the solver runs.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RaycastRequest
    {
        /// <summary>World-space start point (XYZ; Z is elevation).</summary>
        public Vector3 Start;

        /// <summary>World-space end point (XYZ; Z is elevation).</summary>
        public Vector3 End;

        /// <summary>
        /// Packed uint64 ray identifier.
        /// LOS ray:    <c>high 32 = ObserverIndex | low 32 = TargetIndex</c> (bit 63 = 0).
        /// Bullet ray: <c>low 31 bits = BulletEntityIndex</c>                (bit 63 = 1).
        /// See <see cref="PhysicsConstants.PackLosRayId"/> and <see cref="PhysicsConstants.PackBulletRayId"/>.
        /// </summary>
        public long RayId;

        /// <summary>
        /// Full <see cref="Fdp.Kernel.Entity"/> handle of the entity to ignore during this cast
        /// (e.g. the shooter or the LOS observer that submitted the ray).  When no entity should
        /// be excluded, leave this field at its default value; <see cref="Fdp.Kernel.Entity.Null"/>
        /// has <c>IsNull == true</c>, which the solver uses as the "no ignore" sentinel.
        /// </summary>
        /// <remarks>
        /// Using the full handle (Index + Generation) rather than a bare index means a re-used
        /// entity slot is never accidentally skipped.
        /// </remarks>
        public Entity IgnoreEntity;

        /// <summary>
        /// Layer bitmask. The ray only interacts with entities whose
        /// <see cref="PhysicsCollider.CollisionLayer"/> has at least one shared bit.
        /// </summary>
        public int LayerMask;
    }

    // ── RaycastHit ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Result of resolving one <see cref="RaycastRequest"/>.
    /// Written by <see cref="Systems.RaycastSolverSystem"/> and consumed by
    /// <see cref="Systems.HitResolutionSystem"/> in the same frame.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RaycastHit
    {
        /// <summary>Hit parameter ∈ [0,1] along <c>Start→End</c>. Undefined when <see cref="HasHit"/> == 0.</summary>
        public float T;

        /// <summary>The entity that was hit. Undefined when <see cref="HasHit"/> == 0.</summary>
        public Entity HitEntity;

        /// <summary>Mirrors <see cref="RaycastRequest.RayId"/> for correlation with the original request.</summary>
        public long RayId;

        /// <summary>0 = miss, 1 = hit.</summary>
        public byte HasHit;
    }

    // ── RaycastBatchData ──────────────────────────────────────────────────────────

    /// <summary>
    /// Singleton. Pre-allocated on module init. Filled each frame by upstream systems
    /// (e.g. <c>LosRequestBatchingSystem</c>, Combat bullet systems) and resolved by
    /// <see cref="Systems.RaycastSolverSystem"/>. Reset (Count = 0) by
    /// <see cref="Systems.HitResolutionSystem"/> after all hits are dispatched.
    /// </summary>
    public struct RaycastBatchData
    {
        /// <summary>Number of valid entries in <see cref="Requests"/> and <see cref="Hits"/> this frame.</summary>
        public int Count;

        /// <summary>
        /// Pre-allocated request array.
        /// Length == <see cref="PhysicsConstants.RaycastBatchCapacity"/>.
        /// Allocated with <c>Allocator.Persistent</c>; owned by <see cref="PhysicsToolkitModule"/>.
        /// </summary>
        public NativeArray<RaycastRequest> Requests;

        /// <summary>
        /// Pre-allocated hit-result array, parallel to <see cref="Requests"/>.
        /// Length == <see cref="PhysicsConstants.RaycastBatchCapacity"/>.
        /// Allocated with <c>Allocator.Persistent</c>; owned by <see cref="PhysicsToolkitModule"/>.
        /// </summary>
        public NativeArray<RaycastHit> Hits;
    }
}
