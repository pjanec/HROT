#nullable enable
using System;

namespace Hrot.Stride.Core;

/// <summary>
/// Pure, dependency-free go-to-goal steering controller for a dynamic-rigidbody vehicle
/// (BATCH-17 closed-loop steerability proof).
///
/// <para>
/// Operates in <b>FDP world space</b>: X = East, Y = North, Z = Up (unused here — all
/// motion is treated as horizontal). Heading is measured as the angle (rad) of the
/// vehicle's forward direction projected onto the XY plane, defined as
/// <c>atan2(forward.Y, forward.X)</c> where <c>forward = UnitX rotated by SimTransform.Rotation</c>.
/// </para>
///
/// <para>
/// <b>Go-to-goal law:</b>
/// <list type="number">
///   <item>Compute <c>toTarget = target − pos</c>; distance <c>dist = |toTarget|</c>.</item>
///   <item>If <c>dist ≤ arriveTolerance</c> → output <c>{Speed:0, Steer:0, Arrived:true}</c>.</item>
///   <item>Desired heading <c>ψ_d = atan2(toTarget.Y, toTarget.X)</c>.</item>
///   <item>Heading error <c>e = WrapToPi(ψ_d − currentHeading)</c>.</item>
///   <item>Steer <c>δ = Clamp(K·e, −maxSteer, +maxSteer)</c>.</item>
///   <item>Speed scaled by alignment: <c>v *= Max(slowMinFrac, Max(0, cos(e)))</c>
///         (prevents driving hard while mis-aligned, avoiding wide overshoot; the
///         <c>slowMinFrac</c> floor keeps enough forward motion to yaw even at
///         heading errors ≥ 90°, since bicycle yaw rate = speed/L·tan(δ)).</item>
///   <item>Speed scaled by proximity: <c>v *= Clamp(dist/slowRadius, slowMinFrac, 1)</c>
///         (ease-in near goal; clamp lower end avoids complete stop before arrival).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Minimum-turning-radius note:</b>
/// The bicycle model has a minimum turning radius <c>R_min = wheelBase / tan(maxSteer)</c>.
/// Targets strictly inside the circle of radius <c>R_min</c> centered on the vehicle require a
/// multi-maneuver approach (reverse, K-turn, etc.) and are out of scope. <see cref="Compute"/>
/// will still produce a steering command, but the vehicle may orbit rather than converge. All
/// waypoints in the proof cases are chosen to be at least <c>2×R_min</c> from the spawn.
/// </para>
///
/// <para>
/// <b>Determinism:</b> all operations are pure floating-point arithmetic — no randomness,
/// no I/O, no Stride or FDP dependencies. Safe to use in headless unit tests.
/// </para>
/// </summary>
public sealed class VehicleWaypointController
{
    // ── Parameters ──────────────────────────────────────────────────────────

    /// <summary>Desired cruise speed (m/s). Scaled down by alignment and proximity.</summary>
    public float CruiseSpeed { get; }

    /// <summary>Maximum steer angle magnitude (radians). Default ~35°.</summary>
    public float MaxSteerAngleRad { get; }

    /// <summary>
    /// Proportional heading gain (steer = K × headingError).
    /// Higher values respond faster but may oscillate on dynamic bodies.
    /// </summary>
    public float HeadingGainK { get; }

    /// <summary>
    /// Distance (m) within which the controller declares arrival and commands Speed=0.
    /// </summary>
    public float ArriveToleranceM { get; }

    /// <summary>
    /// Distance (m) at which speed begins ramping down toward the goal.
    /// Must be greater than <see cref="ArriveToleranceM"/>.
    /// </summary>
    public float SlowRadiusM { get; }

    /// <summary>
    /// Minimum speed fraction applied in the proximity slow-down zone (0–1).
    /// Prevents the speed from going all the way to zero before reaching the arrive
    /// tolerance — ensures the vehicle keeps creeping forward at low speed.
    /// </summary>
    public float SlowMinFrac { get; }

    /// <summary>Wheelbase (m), used only for documentation / R_min calculation.</summary>
    public float WheelBase { get; }

    /// <summary>
    /// Minimum turning radius (m): <c>R_min = wheelBase / tan(maxSteer)</c>.
    /// Targets strictly inside this radius require multi-maneuver and are out of scope.
    /// </summary>
    public float MinTurningRadiusM =>
        WheelBase / MathF.Tan(MathF.Max(MaxSteerAngleRad, 1e-4f));

    // ── Constructor ──────────────────────────────────────────────────────────

    /// <summary>
    /// Constructs the controller with the given parameters.
    /// </summary>
    /// <param name="cruiseSpeed">Desired cruise speed (m/s). Must be &gt; 0.</param>
    /// <param name="maxSteerAngleRad">Maximum steer angle magnitude (radians). Must be &gt; 0.</param>
    /// <param name="headingGainK">Proportional heading gain. Typical values: 1.5–3.0.</param>
    /// <param name="arriveToleranceM">Arrival distance threshold (m). Must be &gt; 0.</param>
    /// <param name="slowRadiusM">
    /// Distance at which speed begins ramping down (m).
    /// Must be ≥ <paramref name="arriveToleranceM"/>.
    /// </param>
    /// <param name="slowMinFrac">
    /// Minimum speed fraction in slow zone (0–1, exclusive of 0).
    /// Default 0.2 (20% cruise speed minimum when near goal).
    /// </param>
    /// <param name="wheelBase">Wheelbase (m) for documentation/R_min. Must be &gt; 0.</param>
    public VehicleWaypointController(
        float cruiseSpeed,
        float maxSteerAngleRad,
        float headingGainK,
        float arriveToleranceM,
        float slowRadiusM,
        float slowMinFrac = 0.2f,
        float wheelBase   = 3.5f)
    {
        if (cruiseSpeed       <= 0f) throw new ArgumentOutOfRangeException(nameof(cruiseSpeed));
        if (maxSteerAngleRad  <= 0f) throw new ArgumentOutOfRangeException(nameof(maxSteerAngleRad));
        if (headingGainK      <= 0f) throw new ArgumentOutOfRangeException(nameof(headingGainK));
        if (arriveToleranceM  <= 0f) throw new ArgumentOutOfRangeException(nameof(arriveToleranceM));
        if (slowRadiusM       < arriveToleranceM)
            throw new ArgumentOutOfRangeException(nameof(slowRadiusM),
                "slowRadiusM must be >= arriveToleranceM");
        if (slowMinFrac is < 0f or > 1f)
            throw new ArgumentOutOfRangeException(nameof(slowMinFrac));
        if (wheelBase <= 0f) throw new ArgumentOutOfRangeException(nameof(wheelBase));

        CruiseSpeed      = cruiseSpeed;
        MaxSteerAngleRad = maxSteerAngleRad;
        HeadingGainK     = headingGainK;
        ArriveToleranceM = arriveToleranceM;
        SlowRadiusM      = slowRadiusM;
        SlowMinFrac      = slowMinFrac;
        WheelBase        = wheelBase;
    }

    // ── Output ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Output of a single <see cref="Compute"/> call.
    /// </summary>
    /// <param name="Speed">Commanded forward speed (m/s, ≥ 0).</param>
    /// <param name="SteerAngle">
    /// Commanded steer angle (radians). Positive = left-turn (FDP convention: CCW around Z).
    /// </param>
    /// <param name="Arrived">
    /// <c>true</c> when <c>dist ≤ arriveTolerance</c>. Speed and SteerAngle are both 0 when Arrived.
    /// </param>
    /// <param name="DistToTarget">Current Euclidean distance to the target (m).</param>
    /// <param name="HeadingErrorRad">
    /// Signed heading error (rad), in (−π, +π]. Positive = target is to the left.
    /// </param>
    public readonly record struct Output(
        float Speed,
        float SteerAngle,
        bool  Arrived,
        float DistToTarget,
        float HeadingErrorRad);

    // ── Core algorithm ───────────────────────────────────────────────────────

    /// <summary>
    /// Computes the commanded <see cref="Output"/> for one control step.
    /// </summary>
    /// <param name="posX">Current vehicle X position (FDP East, m).</param>
    /// <param name="posY">Current vehicle Y position (FDP North, m).</param>
    /// <param name="currentHeadingRad">
    /// Current vehicle heading (rad): <c>atan2(forward.Y, forward.X)</c>
    /// where <c>forward = Vector3.UnitX rotated by SimTransform.Rotation</c>.
    /// </param>
    /// <param name="targetX">Target X position (m).</param>
    /// <param name="targetY">Target Y position (m).</param>
    public Output Compute(
        float posX,
        float posY,
        float currentHeadingRad,
        float targetX,
        float targetY)
    {
        float dx   = targetX - posX;
        float dy   = targetY - posY;
        float dist = MathF.Sqrt(dx * dx + dy * dy);

        // ── Arrival check ────────────────────────────────────────────────────
        if (dist <= ArriveToleranceM)
            return new Output(0f, 0f, Arrived: true, DistToTarget: dist, HeadingErrorRad: 0f);

        // ── Desired heading ──────────────────────────────────────────────────
        float desiredHeading = MathF.Atan2(dy, dx);
        float headingErr     = WrapToPi(desiredHeading - currentHeadingRad);

        // ── Steer command (proportional) ─────────────────────────────────────
        float steer = MathF.Max(-MaxSteerAngleRad,
                          MathF.Min( MaxSteerAngleRad, HeadingGainK * headingErr));

        // ── Speed scaling ────────────────────────────────────────────────────
        // 1. Alignment factor: reduces speed when mis-aligned so the vehicle turns toward
        //    the target before accelerating hard (prevents wide overshoot).
        //    Uses Max(slowMinFrac, Max(0, cos(e))): the floor ensures a minimum creep speed
        //    even when heading error ≥ 90° — without it, cos(e) = 0 makes speed = 0, which
        //    also makes yawRate = 0 (bicycle model: ω = v/L·tan(δ)), so the vehicle never
        //    turns. The slowMinFrac floor keeps the car moving just enough to yaw.
        float alignFactor = MathF.Max(SlowMinFrac, MathF.Max(0f, MathF.Cos(headingErr)));

        // 2. Proximity factor: ramps speed from slowMinFrac to 1.0 as dist grows from 0
        //    to slowRadius. Clamp ensures we never exceed 1.0 far from goal.
        float proximityFactor = MathF.Max(SlowMinFrac,
                                    MathF.Min(1f, dist / SlowRadiusM));

        float speed = CruiseSpeed * alignFactor * proximityFactor;

        return new Output(speed, steer, Arrived: false,
                          DistToTarget: dist, HeadingErrorRad: headingErr);
    }

    // ── Static helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Wraps <paramref name="angle"/> (radians) to the interval (−π, +π].
    /// </summary>
    public static float WrapToPi(float angle)
    {
        // Shift into [0, 2π), then shift back to (−π, +π].
        const float TwoPi = 2f * MathF.PI;
        angle = angle % TwoPi;
        if (angle >  MathF.PI) angle -= TwoPi;
        if (angle <= -MathF.PI) angle += TwoPi;
        return angle;
    }
}
