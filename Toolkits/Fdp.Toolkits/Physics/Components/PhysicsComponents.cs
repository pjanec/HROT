using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.Collections;

namespace Fdp.Toolkit.Physics.Components
{
    // ── PhysicsCollider ───────────────────────────────────────────────────────────

    /// <summary>
    /// Bounding-circle collider for broadphase and raycast intersection tests.
    /// Used by <see cref="Systems.RaycastSolverSystem"/> to check which entities
    /// a ray can hit.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.PhysicsCollider)]
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
        /// Full <see cref="Fdp.Core.Entity"/> handle of the entity to ignore during this cast
        /// (e.g. the shooter or the LOS observer that submitted the ray).  When no entity should
        /// be excluded, leave this field at its default value; <see cref="Fdp.Core.Entity.Null"/>
        /// has <c>IsNull == true</c>, which the solver uses as the "no ignore" sentinel.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Using the full handle (Index + Generation) rather than a bare index means a re-used
        /// entity slot is never accidentally skipped.
        /// </para>
        /// <para>
        /// <b>Convention for bullet rays (TD-7):</b>
        /// <c>BallisticsSystem</c> populates this field with the shooter entity handle (the entity
        /// that owns <c>BallisticProjectile.Shooter</c>) when submitting a swept-bullet ray.
        /// <c>HitResolutionSystem</c> relies on this convention to recover the shooter's network ID
        /// via <c>NetworkEntityMap.TryGetNetworkId(request.IgnoreEntity, out long shooterNetId)</c>
        /// and embed it in the emitted <see cref="Fdp.Toolkit.Combat.Contracts.DetonationNotification"/>.
        /// Callers that add bullet rays to <see cref="RaycastBatchData"/> <b>must</b> set
        /// <c>IgnoreEntity</c> to the shooter entity, or the shooter ID in
        /// <c>DetonationNotification</c> will be zero (unknown).
        /// </para>
        /// </remarks>
        public Entity IgnoreEntity;

        /// <summary>
        /// For LOS rays: the observer entity (full handle: index + generation).
        /// Zero/Null for bullet rays.
        /// Propagated unchanged to <see cref="RaycastHit.Observer"/> for recovery in
        /// <see cref="Systems.HitResolutionSystem"/> without bit-unpacking from <see cref="RayId"/>.
        /// </summary>
        public Entity Observer;

        /// <summary>
        /// For LOS rays: the target entity (full handle: index + generation).
        /// Zero/Null for bullet rays.
        /// Propagated unchanged to <see cref="RaycastHit.Target"/>.
        /// </summary>
        public Entity Target;

        /// <summary>
        /// Layer bitmask. The ray only interacts with entities whose
        /// <see cref="PhysicsCollider.CollisionLayer"/> has at least one shared bit.
        /// </summary>
        public int LayerMask;
        /// <summary>
        /// Originating Brain node ID stamped by the network ingress translator.
        /// Propagated through the solver to <see cref="RaycastHit.SourceNodeId"/> so that
        /// the egress translator can demultiplex hits back to the requesting Brain node.
        /// 0 = local-only (no distributed routing required).
        /// </summary>
        public int SourceNodeId;    }

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

        /// <summary>
        /// For LOS rays: observer entity propagated from <see cref="RaycastRequest.Observer"/>.
        /// Used by <see cref="Systems.HitResolutionSystem"/> to emit <see cref="Fdp.Toolkit.Perception.Events.TargetVisibleEvent"/>
        /// without bit-unpacking from <see cref="RayId"/>.
        /// </summary>
        public Entity Observer;

        /// <summary>
        /// For LOS rays: target entity propagated from <see cref="RaycastRequest.Target"/>.
        /// </summary>
        public Entity Target;

        /// <summary>0 = miss, 1 = hit.</summary>
        public byte HasHit;
        /// <summary>
        /// Propagated from <see cref="RaycastRequest.SourceNodeId"/> by <c>RaycastSolverSystem</c>.
        /// Used by the egress translator to route the result back to the originating Brain.
        /// </summary>
        public int SourceNodeId;    }

    // ── RaycastBatchData ──────────────────────────────────────────────────────────

    /// <summary>
    /// Singleton. Pre-allocated on module init. Filled each frame by upstream systems
    /// (e.g. <c>LosRequestBatchingSystem</c>, Combat bullet systems) and resolved by
    /// <see cref="Systems.RaycastSolverSystem"/>. Reset (Count = 0) by
    /// <see cref="Systems.HitResolutionSystem"/> after all hits are dispatched.
    /// </summary>
    [ComponentId(GlobalComponentIds.RaycastBatchData)]
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
