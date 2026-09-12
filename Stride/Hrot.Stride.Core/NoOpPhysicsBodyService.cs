#nullable enable
using Fdp.Core;
using Fdp.Toolkit.Tkb.Domain;
using SMath = Stride.Core.Mathematics;

namespace Hrot.Stride.Core;

/// <summary>
/// No-op implementation of <see cref="IPhysicsBodyService"/> for use in
/// <c>editor_stride</c> until the concrete <c>BulletPhysicsBodyService</c> lands
/// at GPU bring-up (STR-D11).
///
/// <para>
/// All body lifecycle calls are accepted and silently ignored (no Bullet bodies are
/// created or stepped). All state queries return zero / identity / false, meaning:
/// <list type="bullet">
///   <item>Motors run but produce zero velocity (no actual physics movement).</item>
///   <item>The reverse-sync reads zero pose and zero velocity from
///     <see cref="GetBodyState"/>, so all owned entities stay at their initial
///     <see cref="SimTransform"/> position (identity pose, zero velocity).</item>
/// </list>
/// This is acceptable for P1 headless testing because the integration tests that
/// assert the ordering invariant (reverse-sync before <c>Kernel.Update()</c>) use
/// a scripted fake that returns real values; this no-op is used only for the
/// wired-path tests that verify system registration and tick flow.
/// </para>
///
/// <para>
/// <b>STR-D11 obligation:</b> replace with <c>BulletPhysicsBodyService</c>
/// (concrete Bullet implementation) at GPU bring-up when a running
/// <c>Stride.Physics.Simulation</c> is available in <c>HrotStrideApp.Game</c>.
/// </para>
/// </summary>
public sealed class NoOpPhysicsBodyService : IPhysicsBodyService
{
    private int _counter;

    /// <inheritdoc/>
    public object CreateBody(
        Entity entity,
        CollisionShapeKind shapeKind,
        ShapeDims dims,
        in SimTransform initialPose)
        => $"NoOpBody_{++_counter}";

    /// <inheritdoc/>
    public void RemoveBody(object bodyHandle) { /* no-op */ }

    /// <inheritdoc/>
    public void SetCharacterVelocity(object bodyHandle, SMath.Vector3 velocity) { /* no-op */ }

    /// <inheritdoc/>
    public void Jump(object bodyHandle) { /* no-op */ }

    /// <inheritdoc/>
    public bool IsGrounded(object bodyHandle) => false;

    /// <inheritdoc/>
    public void SetLinearVelocityXZ(object bodyHandle, SMath.Vector3 strideLinearVel) { /* no-op */ }

    /// <inheritdoc/>
    public void SetYawRate(object bodyHandle, float strideYawRateRadPerSec) { /* no-op */ }

    /// <inheritdoc/>
    public KinematicMoveResult MoveKinematic(
        object bodyHandle,
        SMath.Vector3 desiredDelta,
        SMath.Quaternion desiredRotDelta)
        => new KinematicMoveResult(SMath.Vector3.Zero, SMath.Quaternion.Identity);

    /// <inheritdoc/>
    /// <remarks>
    /// Returns a zero pose / zero velocity / dynamic state for all bodies.
    /// The reverse-sync will write zero pose (origin) and zero velocity to all
    /// owned entities — they remain at their spawn position with no movement.
    /// The concrete <c>BulletPhysicsBodyService</c> will return the Bullet-resolved
    /// state once GPU bring-up is complete (STR-D11).
    /// </remarks>
    public BodyState GetBodyState(object bodyHandle)
        => new BodyState(
            SMath.Vector3.Zero,
            SMath.Quaternion.Identity,
            SMath.Vector3.Zero,
            SMath.Vector3.Zero,
            IsKinematic: false);
}
