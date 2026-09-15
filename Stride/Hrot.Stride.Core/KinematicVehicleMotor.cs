#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Tkb.Domain;
using NLog;
using SMath = Stride.Core.Mathematics;

namespace Hrot.Stride.Core;

/// <summary>
/// Pre-physics motor for vehicle entities (STR-P1-T4b, design §6.2 + §6.1).
///
/// <para>
/// Each frame, for every entity that has a <see cref="PhysicsBodyReference"/>,
/// <c>VehicleState</c>, and <c>VehicleParams</c>, this motor:
/// <list type="number">
///   <item>Reads the commanded motion from <c>VehicleState</c> (scalar <c>Speed</c> and
///         <c>SteerAngle</c>) and the entity's current heading from
///         <c>SimTransform.Rotation</c>.</item>
///   <item>Computes the desired linear velocity: <c>desiredVelFdp = forward * speed</c>.</item>
///   <item>Converts the FDP velocity to Stride space via
///         <see cref="FdpStrideTransform.ToStrideVelocity"/>.</item>
///   <item>Calls <see cref="IPhysicsBodyService.SetLinearVelocityXZ"/> to command the
///         velocity to the DYNAMIC Bullet <c>RigidbodyComponent</c>, preserving the Y
///         component (gravity keeps the body grounded).</item>
///   <item>Computes yaw rate from the bicycle model and calls
///         <see cref="IPhysicsBodyService.SetYawRate"/>.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why dynamic (not kinematic) now (BATCH-17 dynamic-body migration):</b>
/// The vehicle body is a DYNAMIC <c>RigidbodyComponent</c>.  Bullet's contact solver
/// handles all wall and floor collisions: driving into a wall arrests the velocity to zero
/// (the solver prevents penetration); the body rests on the floor under gravity without
/// any manual sweep logic.  The old <c>MoveKinematic</c> approach was hand-rolled and kept
/// regressing (the sweep only caught horizontal floor-grazes, not vertical walls).
/// </para>
///
/// <para>
/// <b>Velocity invariant (design §6.1):</b>
/// A collision-arrested dynamic body reports zero linear velocity from the Bullet solver.
/// <c>BulletReverseSyncSystem</c> reads the body's actual <c>LinearVelocity</c> /
/// <c>AngularVelocity</c> via the dynamic branch (<c>IsKinematic = false</c>) and writes
/// exactly zero <see cref="SimVelocity"/> when the body is wall-stopped — satisfying the
/// invariant without any explicit zeroing in this motor.
/// </para>
///
/// <para>
/// <b>Post-collision channel:</b>
/// The <c>PostCollisionLinearVelocityFdp</c> / <c>PostCollisionAngularVelocityFdp</c> fields
/// on <see cref="PhysicsBodyReference"/> are NOT written by this motor for vehicle bodies.
/// The reverse-sync reads the solver's <c>LinearVelocity</c> / <c>AngularVelocity</c>
/// directly (dynamic branch) — there is no need for a separate channel.
/// </para>
///
/// <para>
/// <b>Character-body guard (F1 clobber fix — preserved):</b>
/// <c>VehicleKinematicsTkbTranslator</c> injects <c>VehicleState</c> on EVERY TKB-spawned
/// entity.  Without the guard, this motor would match walking mannequins, command zero
/// velocity via <c>SetLinearVelocityXZ</c>, and silence their physics-driven motion.
/// The guard skips any entity whose body shape is <see cref="CollisionShapeKind.Capsule"/>
/// or that carries <c>CrowdMotorIntent</c>.  Only <see cref="CollisionShapeKind.OrientedBox"/>
/// bodies are genuine dynamic vehicles.
/// </para>
///
/// <para>
/// <b>Phase:</b> <see cref="SystemPhase.Simulation"/> — runs before the physics step (§6.2).
/// </para>
/// </summary>
[UpdateInPhase(SystemPhase.Simulation)]
public sealed class KinematicVehicleMotor
{
    // ── NLog ─────────────────────────────────────────────────────────────────
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    // ── Yaw diagnostic throttle ────────────────────────────────────────────
    // Per-entity elapsed time accumulator. Reset after each log emission.
    // Throttle interval: ~0.5 s.
    private const float YawDiagIntervalSec = 0.5f;
    private readonly Dictionary<Entity, float> _yawDiagAccum = new();

    // ── STR-D21 F7 fix: motor-driven-velocity diagnostic throttle ──────────
    // [VehicleMotor] log confirms the motor receives non-zero speed after the
    // VehicleNavigationIntentSystem pre-motor execution (Step 2b fix).
    private const float MotorDiagIntervalSec = 0.5f;
    private readonly Dictionary<Entity, float> _motorDiagAccum = new();

    private readonly IPhysicsBodyService _bodyService;
    private readonly PhysicsBodyLifecycleSystem _lifecycle;

    /// <summary>
    /// Constructs the motor.
    /// </summary>
    /// <param name="bodyService">
    /// Physics service — routes velocity-drive calls to the concrete
    /// <c>BulletPhysicsBodyService</c> (or a recording fake in tests).
    /// </param>
    /// <param name="lifecycle">
    /// Lifecycle system — provides the <see cref="PhysicsBodyLifecycleSystem.Bodies"/>
    /// dictionary that maps FDP entities to their <see cref="PhysicsBodyReference"/>.
    /// </param>
    public KinematicVehicleMotor(
        IPhysicsBodyService        bodyService,
        PhysicsBodyLifecycleSystem lifecycle)
    {
        _bodyService = bodyService ?? throw new ArgumentNullException(nameof(bodyService));
        _lifecycle   = lifecycle   ?? throw new ArgumentNullException(nameof(lifecycle));
    }

    /// <summary>
    /// Executes the motor: translates <c>VehicleState</c> commanded motion into a
    /// desired velocity and yaw rate, then commands those to the DYNAMIC Bullet body.
    /// </summary>
    /// <param name="simRunning">
    /// When <see langword="false"/> (paused/edit mode), commands zero velocity to each body
    /// and skips the normal drive path — keeping the body frozen. The deferred dynamic-config /
    /// initial-pose-slam path (driven by SetLinearVelocityXZ) keeps executing while paused.
    /// Defaults to <see langword="true"/> so existing callers compile unchanged.
    /// </param>
    public void Execute(ISimulationView view, float deltaTime, bool simRunning = true)
    {
        if (view is not EntityRepository repo)
            throw new InvalidOperationException(
                $"{nameof(KinematicVehicleMotor)} requires direct EntityRepository access " +
                $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

        if (deltaTime <= 0f)
            return;

        if (!repo.IsComponentTypeRegistered<VehicleState>())
            return;

        var query = repo.Query()
            .With<VehicleState>()
            .With<SimTransform>()
            .WithOwned<SimTransform>()
            .Build();

        foreach (var entity in query)
        {
            // Only drive entities that have a Bullet body.
            if (!_lifecycle.Bodies.TryGetValue(entity, out var bodyRef))
                continue;

            // ── Guard: skip character-controlled bodies (F1 clobber fix) ─────────
            // VehicleKinematicsTkbTranslator adds VehicleState to EVERY TKB-spawned
            // entity once VehicleState is registered. A walking-mannequin entity can
            // carry both CrowdMotorIntent (owned by BulletCharacterMotor) AND
            // VehicleState (Speed=0, injected by the translator).
            //
            // Without this guard, KinematicVehicleMotor would match the mannequin,
            // call SetLinearVelocityXZ(0,0,0), and zero out its physics-driven velocity —
            // silencing the character's motion. With the guard, only genuine OrientedBox
            // vehicle bodies are driven.
            if (bodyRef.ShapeKind == CollisionShapeKind.Capsule)
                continue;
            if (repo.IsComponentTypeRegistered<CrowdMotorIntent>() &&
                repo.HasComponent<CrowdMotorIntent>(entity))
                continue;

            // BATCH-S2-L: when the sim is paused (edit mode, not Continuous), do NOT drive the vehicle.
            // Command zero velocity + zero yaw so a mid-drive body stops and stays put. We still CALL the
            // body service (not skip it) so the deferred dynamic-config / initial-pose-slam path keeps
            // running while paused (it is driven by SetLinearVelocityXZ -> ApplyDynamicConfigIfReady).
            if (!simRunning)
            {
                _bodyService.SetLinearVelocityXZ(bodyRef.BodyHandle, SMath.Vector3.Zero);
                _bodyService.SetYawRate(bodyRef.BodyHandle, 0f);
                continue;
            }

            var vehicleState = repo.GetComponent<VehicleState>(entity);
            var simTf        = repo.GetComponent<SimTransform>(entity);

            // ── Desired linear velocity (FDP space) ───────────────────────────
            // Forward direction is the X-axis of the entity's rotation (FDP convention:
            // X-forward per CarKinematicsSystem.UpdateVehicle "X-forward convention").
            Vector3 forwardFdp   = Vector3.Transform(Vector3.UnitX, simTf.Rotation);
            Vector3 desiredVelFdp = forwardFdp * vehicleState.Speed;

            // ── Desired yaw rate (FDP space) ──────────────────────────────────
            // Bicycle model: ω = (speed / wheelBase) * tan(steerAngle).
            // FDP yaw is rotation around Z (up), positive = CCW / left-turn.
            float yawRateFdp = ComputeYawRate(repo, entity, vehicleState);

            // ── Convert to Stride space ───────────────────────────────────────
            // FDP→Stride velocity swizzle: Stride.X=FDP.X, Stride.Y=FDP.Z, Stride.Z=FDP.Y.
            // For horizontal (XY-plane in FDP / XZ-plane in Stride) vehicle motion,
            // FDP.Z=0 so Stride.Y=0 — SetLinearVelocityXZ will preserve the solver's Y.
            SMath.Vector3 strideLinearVel = FdpStrideTransform.ToStrideVelocity(desiredVelFdp);

            // FDP yaw around Z-up → Stride yaw around Y-up.
            // FDP CCW (positive ωZ) → Stride CCW from above (positive ωY in left-handed frame
            // = same rotation direction). Sign: FDP.Z→Stride.Y with same handedness for
            // angular velocity. FdpStrideTransform.ToFdpAngularVelocity negates Stride→FDP;
            // the inverse (FDP→Stride) also negates:
            //   strideYawRate = -yawRateFdp
            // (FDP is right-handed Z-up; Stride is left-handed Y-up; the angular velocity
            // handedness flip is the same negation used in the reverse-sync).
            float strideYawRate = -yawRateFdp;

            // ── Command velocity to the dynamic Bullet body ───────────────────
            // SetLinearVelocityXZ preserves the current Y (gravity), so the body stays
            // on the floor while moving in XZ. Bullet's solver prevents wall penetration.
            _bodyService.SetLinearVelocityXZ(bodyRef.BodyHandle, strideLinearVel);
            _bodyService.SetYawRate(bodyRef.BodyHandle, strideYawRate);

            // STR-D21 F7 fix diagnostic: log when motor issues non-zero velocity.
            // [VehicleMotor] tag confirms the motor is being called with speed > 0.
            // If VehicleNavSystem ran before the motor (Step 2b fix), this will fire
            // on the first frame after NavigationIntent is set — confirming the fix.
            if (!_motorDiagAccum.TryGetValue(entity, out float mAccum))
                mAccum = 0f;
            mAccum += deltaTime;
            _motorDiagAccum[entity] = mAccum;
            if (mAccum >= MotorDiagIntervalSec && vehicleState.Speed > 0.01f)
            {
                _motorDiagAccum[entity] = 0f;
                var bodyState = _bodyService.GetBodyState(bodyRef.BodyHandle);
                float actualSpd = new System.Numerics.Vector2(
                    bodyState.LinearVelocity.X, bodyState.LinearVelocity.Z).Length();
                Log.Info(
                    "[VehicleMotor] entity #{0} commanded spd={1:F2} m/s " +
                    "strideVel=({2:F2},{3:F2}) actual_body_xz_spd={4:F2} m/s",
                    entity.Index,
                    vehicleState.Speed,
                    strideLinearVel.X, strideLinearVel.Z,
                    actualSpd);
            }

            // ── Commanded-vs-achieved yaw diagnostic (throttled ~0.5 s) ──────────
            // Reads back the body's ACTUAL angular velocity Y from GetBodyState and
            // compares it to the commanded strideYawRate. A ratio near 1.0 proves the
            // floor-friction fix is working; a ratio << 1.0 means yaw resistance persists.
            // Log only when the commanded yaw rate is non-trivial (|yaw| > 0.01 rad/s)
            // so we do not emit spurious 0/0 lines during straight driving.
            // [VehicleYaw] is the tag the GPU operator should grep for to confirm the fix.
            if (!_yawDiagAccum.TryGetValue(entity, out float accum))
                accum = 0f;

            accum += deltaTime;
            _yawDiagAccum[entity] = accum;

            if (accum >= YawDiagIntervalSec && MathF.Abs(strideYawRate) > 0.01f)
            {
                _yawDiagAccum[entity] = 0f;

                // GetBodyState is cheap — reads the Bullet-resolved angular velocity from
                // the entity's transform that is already updated each physics step.
                var bodyState = _bodyService.GetBodyState(bodyRef.BodyHandle);
                float achievedYaw = bodyState.AngularVelocity.Y; // Stride Y = yaw axis

                float ratio = MathF.Abs(strideYawRate) > 1e-6f
                    ? achievedYaw / strideYawRate
                    : float.NaN;

                Log.Info(
                    "[VehicleYaw] entity #{0} commanded={1:F3} rad/s achieved={2:F3} rad/s " +
                    "(ratio={3:P0})",
                    entity.Index,
                    strideYawRate,
                    achievedYaw,
                    float.IsNaN(ratio) ? 0f : ratio);
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Computes the instantaneous yaw rate (rad/s, FDP: positive = left-turn / CCW around Z)
    /// from VehicleState, falling back to zero when no VehicleParams is present.
    /// </summary>
    private static float ComputeYawRate(EntityRepository repo, Entity entity, VehicleState vehicleState)
    {
        if (!repo.HasComponent<VehicleParams>(entity))
            return 0f;

        var @params = repo.GetComponent<VehicleParams>(entity);
        float wheelBase = @params.WheelBase;
        if (MathF.Abs(wheelBase) < 1e-6f)
            return 0f;

        // Bicycle model: ω = (v / L) * tan(δ)
        return (vehicleState.Speed / wheelBase) * MathF.Tan(vehicleState.SteerAngle);
    }
}
