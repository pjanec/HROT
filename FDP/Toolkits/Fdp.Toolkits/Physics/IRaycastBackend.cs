using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Physics.Components;

namespace Fdp.Toolkit.Physics
{
    /// <summary>
    /// Optional injectable backend for <see cref="Systems.RaycastSolverSystem"/>.
    ///
    /// <para>
    /// When registered on <see cref="Systems.RaycastSolverSystem.RaycastBackend"/>, this
    /// backend replaces the default flat spatial-hash + circle-sweep narrow phase with a
    /// real 3-D physics query (e.g. Stride/Bullet via <c>IStrideRaycastService</c> in
    /// <c>Hrot.Stride.Core</c>).
    /// </para>
    ///
    /// <para>
    /// <b>Dependency direction invariant:</b>
    /// This interface lives in <c>Fdp.Toolkits</c>.  <c>Hrot.Stride.Core</c> references
    /// <c>Fdp.Toolkits</c> and provides an adapter that implements this interface using
    /// <c>IStrideRaycastService</c>.  <c>Fdp.Toolkits</c> never references
    /// <c>Hrot.Stride.Core</c> (dependency goes downward only).
    /// </para>
    ///
    /// <para>
    /// <b>Coordinate space:</b>
    /// All input/output coordinates are in FDP world space (right-handed, X=East, Y=North,
    /// Z=Up).  The adapter is responsible for any engine-specific conversion.
    /// </para>
    /// </summary>
    public interface IRaycastBackend
    {
        /// <summary>
        /// Performs a raycast from <paramref name="start"/> to <paramref name="end"/>
        /// in FDP world space and returns the closest hit.
        ///
        /// <para>
        /// The implementation must exclude <paramref name="ignoreEntity"/> from the
        /// results (used to prevent the shooter from blocking their own bullet ray).
        /// </para>
        ///
        /// <para>
        /// <b>Return value semantics:</b>
        /// <list type="bullet">
        ///   <item>
        ///     <see cref="RaycastHit.HasHit"/> == 1 when a surface is hit before the endpoint.
        ///   </item>
        ///   <item>
        ///     <see cref="RaycastHit.T"/> is the hit parameter ∈ [0,1] along
        ///     <paramref name="start"/>→<paramref name="end"/>; the hit point is
        ///     <c>start + T * (end - start)</c>.
        ///   </item>
        ///   <item>
        ///     <see cref="RaycastHit.HitEntity"/> is the FDP entity that was hit, or
        ///     <see cref="Entity.Null"/> for static scene geometry.
        ///   </item>
        ///   <item>
        ///     When <see cref="RaycastHit.HasHit"/> == 0 (miss), only
        ///     <see cref="RaycastHit.RayId"/> is meaningful (copied from
        ///     <paramref name="rayId"/>).
        ///   </item>
        /// </list>
        /// </para>
        /// </summary>
        /// <param name="start">Ray start in FDP world space.</param>
        /// <param name="end">Ray end in FDP world space.</param>
        /// <param name="rayId">Packed ray identifier (echoed into the result for correlation).</param>
        /// <param name="layerMask">
        /// Layer bitmask.  The implementation should respect this mask when filtering
        /// hit candidates.
        /// </param>
        /// <param name="ignoreEntity">
        /// Entity to exclude from hit testing (e.g. the bullet's shooter).
        /// Pass <see cref="Entity.Null"/> when no entity should be ignored.
        /// </param>
        /// <param name="observerEntity">
        /// For LOS rays: the observer entity.  Pass <see cref="Entity.Null"/> for bullet rays.
        /// Echoed into the result.
        /// </param>
        /// <param name="targetEntity">
        /// For LOS rays: the target entity.  Pass <see cref="Entity.Null"/> for bullet rays.
        /// Echoed into the result.
        /// </param>
        /// <returns>
        /// A <see cref="RaycastHit"/> describing the closest hit or a miss.
        /// </returns>
        RaycastHit Raycast(
            Vector3 start,
            Vector3 end,
            long    rayId,
            int     layerMask,
            Entity  ignoreEntity,
            Entity  observerEntity,
            Entity  targetEntity);
    }
}
