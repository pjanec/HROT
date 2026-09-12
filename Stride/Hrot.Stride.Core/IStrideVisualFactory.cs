using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Stride.Core;

/// <summary>
/// Testable factory seam: all Stride-side visual/primitive creation is routed through
/// this interface so the <see cref="StrideVisualBindingSystem"/> — which owns the
/// descriptor-resolution, model-vs-procedural selection, shape-sizing, Scale/Offset,
/// swizzled placement, and create/destroy reconciliation logic — can be exercised
/// headlessly with a recording fake.
///
/// <para>
/// The concrete GPU implementation (<c>StrideVisualFactory</c> in <c>HrotStrideApp.Game</c>)
/// calls <c>Content.Load&lt;Model&gt;(url)</c>, attaches <c>ModelComponent</c> /
/// <c>AnimationComponent</c>, and creates procedural primitive meshes; it requires a
/// <see cref="Stride.Graphics.GraphicsDevice"/> and cannot run headlessly.
/// </para>
///
/// <para>
/// <b>Threading invariant:</b> all calls on this interface happen on the single
/// host thread (design §8.3). No implementation may create secondary threads
/// or dispatch work asynchronously.
/// </para>
/// </summary>
public interface IStrideVisualFactory
{
    /// <summary>
    /// Creates a model-based visual: loads the asset at <paramref name="modelRef"/>,
    /// optionally wires a skeleton for <paramref name="skeletonRef"/> (skinned/animated),
    /// applies <paramref name="scale"/> and <paramref name="offsetFdp"/> (FDP-space local
    /// offset from the physics-body origin), and places the visual at the swizzled position
    /// derived from <paramref name="initialPose"/>.
    /// </summary>
    /// <param name="modelRef">Stride asset URL of the Model (non-empty).</param>
    /// <param name="skeletonRef">Stride asset URL of the Skeleton; empty for rigid models.</param>
    /// <param name="scale">Uniform scale (1 = as authored).</param>
    /// <param name="offsetFdp">Render-model local offset from the body origin in FDP coordinates.</param>
    /// <param name="initialPose">Entity's initial <see cref="SimTransform"/> (FDP-space).</param>
    /// <returns>
    /// An opaque handle that identifies this visual inside the factory.
    /// Passed back to <see cref="UpdatePose"/> and <see cref="Destroy"/>.
    /// </returns>
    object CreateModelVisual(
        string modelRef,
        string skeletonRef,
        float scale,
        Vector3 offsetFdp,
        in SimTransform initialPose);

    /// <summary>
    /// Creates a procedural primitive visual matching <paramref name="kind"/> (capsule, box, …),
    /// applies <paramref name="scale"/> and <paramref name="offsetFdp"/>, and places it at the
    /// swizzled position derived from <paramref name="initialPose"/>.
    /// </summary>
    /// <param name="kind">Collision shape kind (determines the primitive type).</param>
    /// <param name="dims">Resolved shape dimensions (all "0 =&gt; default" rules already applied).</param>
    /// <param name="scale">Uniform scale (1 = as authored).</param>
    /// <param name="offsetFdp">Render-model local offset from the body origin in FDP coordinates.</param>
    /// <param name="initialPose">Entity's initial <see cref="SimTransform"/> (FDP-space).</param>
    /// <returns>An opaque handle identifying this visual.</returns>
    object CreateProceduralVisual(
        CollisionShapeKind kind,
        ShapeDims dims,
        float scale,
        Vector3 offsetFdp,
        in SimTransform initialPose);

    /// <summary>
    /// Updates the world-space placement of an existing visual handle to match
    /// <paramref name="pose"/> (FDP-space <see cref="SimTransform"/>). The factory is
    /// responsible for the FDP→Stride swizzle via
    /// <see cref="FdpStrideTransform.ToStridePosition"/> /
    /// <see cref="FdpStrideTransform.ToStrideRotation"/>.
    /// </summary>
    /// <param name="visualHandle">Handle returned by a prior Create call.</param>
    /// <param name="pose">Current entity transform in FDP world space.</param>
    void UpdatePose(object visualHandle, in SimTransform pose);

    /// <summary>
    /// Destroys the visual associated with <paramref name="visualHandle"/> and releases
    /// all Stride-side resources. The handle must not be used after this call.
    /// </summary>
    void Destroy(object visualHandle);
}
