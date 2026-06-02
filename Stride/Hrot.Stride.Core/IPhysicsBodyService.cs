#nullable enable
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Tkb.Domain;
using SMath = Stride.Core.Mathematics;

namespace Hrot.Stride.Core;

/// <summary>
/// Seam interface for Bullet body lifecycle and motor operations (STR-P1-T2, STR-P1-T3, STR-P1-T4).
///
/// <para>
/// Mirrors the <see cref="IStrideVisualFactory"/> seam used by
/// <see cref="StrideVisualBindingSystem"/> (BATCH-03): all Stride/Bullet body operations
/// are routed through this interface so that <see cref="PhysicsBodyLifecycleSystem"/>,
/// <see cref="BulletCharacterMotor"/>, and <see cref="KinematicVehicleMotor"/> —
/// which own the authority-keyed lifecycle and motor logic — can be exercised headlessly
/// with a recording fake, while the concrete Bullet implementation lives in
/// <c>HrotStrideApp.Game</c> where a running <c>Stride.Physics.Simulation</c> is available.
/// </para>
///
/// <para>
/// <b>Why this seam is needed:</b>
/// The Stride <c>Simulation</c> constructor and the Add/Remove body methods are all
/// <c>internal</c> to <c>Stride.Physics</c>; they are owned by <c>PhysicsProcessor</c>
/// and cannot be instantiated headlessly without a running Stride <c>Scene</c> +
/// <c>Game</c>. A direct test against <c>Simulation</c> is therefore not possible in the
/// test harness.
/// </para>
///
/// <para>
/// <b>Shape source:</b> the body creation method receives a <see cref="CollisionShapeKind"/>
/// and <see cref="ShapeDims"/> that were already resolved by
/// <see cref="StrideVisualBindingSystem"/> and stored in the entity's
/// <see cref="StrideVisualReference"/>. The lifecycle system does NOT re-resolve the TKB
/// descriptor (design §5.6).
/// </para>
///
/// <para>
/// <b>Threading invariant:</b> all calls happen on the single host thread (design §8.3).
/// </para>
/// </summary>
public interface IPhysicsBodyService
{
    // ── Body lifecycle (STR-P1-T2) ─────────────────────────────────────────────

    /// <summary>
    /// Creates a Bullet collision body for the given entity and returns an opaque handle
    /// that uniquely identifies the body inside the service.
    ///
    /// <para>
    /// The concrete implementation maps <see cref="CollisionShapeKind"/>:
    /// <list type="bullet">
    ///   <item><see cref="CollisionShapeKind.Capsule"/> → <c>CapsuleColliderShape</c> + <c>CharacterComponent</c>.</item>
    ///   <item><see cref="CollisionShapeKind.OrientedBox"/> → <c>BoxColliderShape</c> + <c>RigidbodyComponent</c>.</item>
    ///   <item>Other kinds → best-effort fallback (see concrete impl).</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="entity">The FDP entity for which the body is created.</param>
    /// <param name="shapeKind">Collision shape kind (from <see cref="StrideVisualReference"/>).</param>
    /// <param name="dims">Resolved shape dimensions (all "0 =&gt; default" rules applied).</param>
    /// <param name="initialPose">Entity's current <see cref="SimTransform"/> in FDP world-space.</param>
    /// <returns>
    /// An opaque handle used by <see cref="RemoveBody"/>.
    /// The caller stores it in a <see cref="PhysicsBodyReference"/>.
    /// </returns>
    object CreateBody(
        Entity             entity,
        CollisionShapeKind shapeKind,
        ShapeDims          dims,
        in SimTransform    initialPose);

    /// <summary>
    /// Removes and disposes the Bullet body identified by <paramref name="bodyHandle"/>.
    /// The handle must not be used after this call.
    /// </summary>
    /// <param name="bodyHandle">Handle returned by <see cref="CreateBody"/>.</param>
    void RemoveBody(object bodyHandle);

    // ── Character motor (STR-P1-T3) ────────────────────────────────────────────

    /// <summary>
    /// Sets the desired horizontal velocity on a Bullet <c>CharacterComponent</c> body.
    ///
    /// <para>
    /// In the concrete implementation this calls
    /// <c>CharacterComponent.SetVelocity(velocity)</c> where <paramref name="velocity"/>
    /// is already in Stride world space (converted by <c>BulletCharacterMotor</c> via
    /// <c>FdpStrideTransform.ToStrideVelocity</c>).
    /// </para>
    /// </summary>
    /// <param name="bodyHandle">Handle of a body created with <c>CollisionShapeKind.Capsule</c>.</param>
    /// <param name="velocity">Desired velocity in Stride world space (Y-up, left-handed).</param>
    void SetCharacterVelocity(object bodyHandle, SMath.Vector3 velocity);

    /// <summary>
    /// Triggers a jump on a Bullet <c>CharacterComponent</c> body.
    ///
    /// <para>
    /// In the concrete implementation this calls <c>CharacterComponent.Jump()</c>.
    /// Only called when <see cref="IsGrounded"/> is true (the motor applies the gate).
    /// </para>
    /// </summary>
    /// <param name="bodyHandle">Handle of a body created with <c>CollisionShapeKind.Capsule</c>.</param>
    void Jump(object bodyHandle);

    /// <summary>
    /// Returns whether the character body is currently resting on a surface.
    ///
    /// <para>
    /// In the concrete implementation this reads <c>CharacterComponent.IsGrounded</c>.
    /// </para>
    /// </summary>
    /// <param name="bodyHandle">Handle of a body created with <c>CollisionShapeKind.Capsule</c>.</param>
    /// <returns><see langword="true"/> when the character is on the ground.</returns>
    bool IsGrounded(object bodyHandle);

    // ── Reverse-sync body state (STR-P1-T5) ────────────────────────────────────

    /// <summary>
    /// Returns the current physics state of the body identified by
    /// <paramref name="bodyHandle"/> (STR-P1-T5).
    ///
    /// <para>
    /// Called once per frame by <c>BulletReverseSyncSystem</c> for every
    /// locally-owned entity after the <c>PhysicsProcessor</c> has stepped the simulation.
    /// </para>
    ///
    /// <para>
    /// <b>Dynamic body</b> (<c>IsKinematic = false</c>):
    /// <see cref="BodyState.LinearVelocity"/> and <see cref="BodyState.AngularVelocity"/>
    /// come from <c>RigidbodyComponent.LinearVelocity</c> /
    /// <c>RigidbodyComponent.AngularVelocity</c>.
    /// A collision-arrested dynamic body reports zero velocity here, so the reverse-sync
    /// writes exactly zero <see cref="SimVelocity"/> — satisfying the velocity invariant
    /// (design §6.1) without any extra zeroing logic in the caller.
    /// </para>
    ///
    /// <para>
    /// <b>Kinematic body</b> (<c>IsKinematic = true</c>):
    /// The Bullet solver does not compute a velocity for kinematic bodies.
    /// <see cref="BodyState.LinearVelocity"/> and <see cref="BodyState.AngularVelocity"/>
    /// are set to zero in the return value; the reverse-sync instead reads
    /// <see cref="PhysicsBodyReference.PostCollisionLinearVelocityFdp"/> /
    /// <see cref="PhysicsBodyReference.PostCollisionAngularVelocityFdp"/>, which the motor
    /// (STR-P1-T3/T4) computed and stored after the kinematic move.
    /// </para>
    /// </summary>
    /// <param name="bodyHandle">Handle returned by <see cref="CreateBody"/>.</param>
    /// <returns>Current pose and velocity of the body in Stride world space.</returns>
    BodyState GetBodyState(object bodyHandle);

    // ── Dynamic vehicle motor (STR-P1-T4b) ────────────────────────────────────

    /// <summary>
    /// Sets the horizontal (XZ-plane) linear velocity on a dynamic
    /// <c>RigidbodyComponent</c> vehicle body while preserving the current Y
    /// component so Bullet's gravity keeps the body grounded.
    ///
    /// <para>
    /// Concretely: the implementation reads <c>RigidbodyComponent.LinearVelocity.Y</c>,
    /// then sets <c>RigidbodyComponent.LinearVelocity = new Vector3(strideVel.X, currentY, strideVel.Z)</c>.
    /// The body is activated so the solver sees the command even after the body has gone idle.
    /// </para>
    ///
    /// <para>
    /// Because the body is DYNAMIC and this velocity is applied each frame, Bullet's
    /// contact solver still prevents penetration — driving into a wall arrests the
    /// velocity to zero and the body slides naturally.  No sweep logic is needed.
    /// </para>
    /// </summary>
    /// <param name="bodyHandle">Handle of a body created with <c>CollisionShapeKind.OrientedBox</c>.</param>
    /// <param name="strideLinearVel">
    /// Desired XZ velocity in Stride world space (Y-up, left-handed).
    /// Only the X and Z components are applied; Y is preserved from the current solver state.
    /// </param>
    void SetLinearVelocityXZ(object bodyHandle, SMath.Vector3 strideLinearVel);

    /// <summary>
    /// Sets the yaw angular velocity on a dynamic <c>RigidbodyComponent</c> vehicle body.
    ///
    /// <para>
    /// Concretely: sets <c>RigidbodyComponent.AngularVelocity = new Vector3(0, strideYawRateRadPerSec, 0)</c>.
    /// The body is activated.
    /// </para>
    /// </summary>
    /// <param name="bodyHandle">Handle of a body created with <c>CollisionShapeKind.OrientedBox</c>.</param>
    /// <param name="strideYawRateRadPerSec">
    /// Desired yaw rate in Stride space (rad/s, around Stride Y = up axis, positive = CCW
    /// when viewed from above in Stride's left-handed coordinate system).
    /// </param>
    void SetYawRate(object bodyHandle, float strideYawRateRadPerSec);

    // ── Kinematic vehicle motor (STR-P1-T4, legacy — no longer used by vehicles) ──

    /// <summary>
    /// Performs a swept / penetration-tested kinematic move for a vehicle body,
    /// implementing block-or-slide collision response against the static Bullet world.
    ///
    /// <para>
    /// <b>Note (BATCH-17 dynamic-body migration):</b> vehicle bodies are now DYNAMIC
    /// <c>RigidbodyComponent</c>s driven via <see cref="SetLinearVelocityXZ"/> /
    /// <see cref="SetYawRate"/>.  This method remains in the interface for potential
    /// future use (e.g. purely kinematic test fixtures) but is no longer called by
    /// <see cref="KinematicVehicleMotor"/> for live vehicles.
    /// </para>
    ///
    /// <para>
    /// The concrete implementation uses a Bullet sweep test (or
    /// <c>PhysicsProcessor</c>'s kinematic update mechanism) to move the body by
    /// <paramref name="desiredDelta"/> in Stride space, clamping the actual executed
    /// delta on contact. The returned <see cref="KinematicMoveResult.ActualDelta"/>
    /// is the collision-clamped position delta actually applied; zero means fully blocked.
    /// </para>
    ///
    /// <para>
    /// The caller (<see cref="KinematicVehicleMotor"/>) derives post-collision linear and
    /// angular velocity from <see cref="KinematicMoveResult"/> and exposes them for the
    /// reverse-sync (design §6.1 velocity invariant).
    /// </para>
    /// </summary>
    /// <param name="bodyHandle">Handle of the kinematic body to move.</param>
    /// <param name="desiredDelta">Desired position delta in Stride world space (Y-up, left-handed).</param>
    /// <param name="desiredRotDelta">Desired rotation delta in Stride space.</param>
    /// <returns>
    /// A <see cref="KinematicMoveResult"/> carrying the actual (collision-clamped) delta
    /// and rotation delta that were applied.
    /// </returns>
    KinematicMoveResult MoveKinematic(
        object            bodyHandle,
        SMath.Vector3     desiredDelta,
        SMath.Quaternion  desiredRotDelta);
}

/// <summary>
/// Body state returned by <see cref="IPhysicsBodyService.GetBodyState"/> (STR-P1-T5).
///
/// <para>
/// Used by <c>BulletReverseSyncSystem</c> to read the Bullet-resolved pose and velocity
/// each frame.  Dynamic bodies carry live <see cref="LinearVelocity"/> and
/// <see cref="AngularVelocity"/> from the solver; kinematic bodies have an
/// <see cref="IsKinematic"/> flag set and their velocity channel is the motor's
/// post-collision values on <see cref="PhysicsBodyReference"/> instead.
/// </para>
///
/// <para>
/// All values are in <b>Stride world space</b> (Y-up, left-handed).
/// <c>BulletReverseSyncSystem</c> converts them to FDP space via
/// <c>FdpStrideTransform.ToFdp*</c>.
/// </para>
/// </summary>
/// <param name="Position">Body's world position in Stride space.</param>
/// <param name="Rotation">Body's world orientation in Stride space.</param>
/// <param name="LinearVelocity">
/// Solver-resolved linear velocity in Stride space (m/s).
/// Only meaningful for dynamic bodies — read from
/// <c>RigidbodyComponent.LinearVelocity</c>.
/// For kinematic bodies the reverse-sync reads the motor's post-collision channel instead.
/// </param>
/// <param name="AngularVelocity">
/// Solver-resolved angular velocity in Stride space (rad/s).
/// Only meaningful for dynamic bodies.
/// </param>
/// <param name="IsKinematic">
/// <see langword="true"/> when the body is kinematic (character — <c>CharacterComponent</c>).
/// When <see langword="true"/>, the reverse-sync ignores
/// <see cref="LinearVelocity"/> / <see cref="AngularVelocity"/> and reads
/// <see cref="PhysicsBodyReference.PostCollisionLinearVelocityFdp"/> /
/// <see cref="PhysicsBodyReference.PostCollisionAngularVelocityFdp"/> instead.
/// <see langword="false"/> for dynamic bodies (including the DYNAMIC vehicle body —
/// <c>OrientedBox</c> — which reports its Bullet-solved velocity directly).
/// </param>
public readonly record struct BodyState(
    SMath.Vector3    Position,
    SMath.Quaternion Rotation,
    SMath.Vector3    LinearVelocity,
    SMath.Vector3    AngularVelocity,
    bool             IsKinematic);

/// <summary>
/// Result of a <see cref="IPhysicsBodyService.MoveKinematic"/> call (STR-P1-T4).
///
/// <para>
/// The <see cref="ActualDelta"/> is the collision-clamped position delta that was
/// physically applied to the kinematic body. A zero <see cref="ActualDelta"/> means
/// the move was fully blocked. <see cref="KinematicVehicleMotor"/> uses this to compute
/// the post-collision linear velocity: <c>vel = ActualDelta / dt</c>, zeroed on full block.
/// </para>
/// </summary>
/// <param name="ActualDelta">
/// The actual position delta applied (Stride space, Y-up, left-handed). May be shorter
/// than the desired delta (partial block/slide) or zero (fully blocked).
/// </param>
/// <param name="ActualRotDelta">
/// The actual rotation delta applied (Stride space). May differ from the desired rotation
/// on a blocked move.
/// </param>
public readonly record struct KinematicMoveResult(
    SMath.Vector3    ActualDelta,
    SMath.Quaternion ActualRotDelta);
