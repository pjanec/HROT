#nullable enable
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Stride.Core;

/// <summary>
/// Shadow component that records the binding between an FDP entity and its
/// Bullet physics body (STR-P1-T2), and carries the motor's post-collision velocity
/// for the reverse-sync (STR-P1-T4, design §6.1 velocity invariant).
///
/// <para>
/// Stored in a parallel <c>Dictionary&lt;Entity, PhysicsBodyReference&gt;</c>
/// managed by <see cref="PhysicsBodyLifecycleSystem"/>, mirroring how
/// <see cref="StrideVisualReference"/> is stored by
/// <see cref="StrideVisualBindingSystem"/>.  Bullet-side objects cannot be
/// blitted into a fixed-size ECS component slot.
/// </para>
///
/// <para>
/// The handle is an opaque token returned by
/// <see cref="IPhysicsBodyService.CreateBody"/>; the lifecycle system passes
/// it back to <see cref="IPhysicsBodyService.RemoveBody"/> on teardown.
/// </para>
///
/// <para>
/// <b>Post-collision velocity channel (STR-P1-T4):</b>
/// <see cref="KinematicVehicleMotor"/> writes <see cref="PostCollisionLinearVelocityFdp"/>
/// and <see cref="PostCollisionAngularVelocityFdp"/> after each frame's kinematic move.
/// <c>BulletReverseSyncSystem</c> (STR-P1-T5) reads these fields to populate
/// <see cref="SimVelocity"/> for kinematic bodies, satisfying the velocity invariant:
/// a fully blocked move yields exactly zero velocity in both fields.
/// Both fields are in FDP world space (right-handed, X=East, Y=North, Z=Up).
/// </para>
/// </summary>
public sealed class PhysicsBodyReference
{
    /// <summary>
    /// Opaque handle returned by <see cref="IPhysicsBodyService.CreateBody"/>.
    /// </summary>
    public object BodyHandle { get; }

    /// <summary>
    /// Collision shape kind that was used to create the body.
    /// Stored for diagnostics / test assertions.
    /// </summary>
    public CollisionShapeKind ShapeKind { get; }

    /// <summary>
    /// Resolved shape dimensions that were used to create the body.
    /// Stored for diagnostics / test assertions.
    /// </summary>
    public ShapeDims Dims { get; }

    // ── Post-collision velocity channel (written by KinematicVehicleMotor) ───

    /// <summary>
    /// Post-collision linear velocity in FDP world space (X=East, Y=North, Z=Up), m/s.
    ///
    /// <para>
    /// Written by <see cref="KinematicVehicleMotor"/> each frame after the kinematic move:
    /// <c>vel = actualDeltaFdp / dt</c>. Zero on a fully blocked move.
    /// Read by <c>BulletReverseSyncSystem</c> (STR-P1-T5) to write <see cref="SimVelocity.Linear"/>.
    /// For dynamic rigid bodies the reverse-sync reads <c>RigidbodyComponent.LinearVelocity</c>
    /// instead; this field is only relevant for kinematic bodies.
    /// </para>
    /// </summary>
    public Vector3 PostCollisionLinearVelocityFdp { get; set; }

    /// <summary>
    /// Post-collision angular velocity in FDP world space (X=East, Y=North, Z=Up), rad/s.
    ///
    /// <para>
    /// Written by <see cref="KinematicVehicleMotor"/> each frame: derived from the executed
    /// rotation delta divided by dt. Zero on a fully blocked move.
    /// Read by <c>BulletReverseSyncSystem</c> (STR-P1-T5) to write <see cref="SimVelocity.Angular"/>.
    /// </para>
    /// </summary>
    public Vector3 PostCollisionAngularVelocityFdp { get; set; }

    /// <summary>
    /// Initialises a new reference from the service's create result.
    /// Post-collision velocity fields start at zero (body is newly created, not yet moved).
    /// </summary>
    public PhysicsBodyReference(object bodyHandle, CollisionShapeKind shapeKind, ShapeDims dims)
    {
        BodyHandle = bodyHandle;
        ShapeKind  = shapeKind;
        Dims       = dims;
    }
}
