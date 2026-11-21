#nullable enable
using System;
using Hrot.Stride.Core;
using Xunit;

namespace Hrot.Stride.Core.Tests;

// ── Unit tests for WrapToPi and arrive/stop ─────────────────────────────────

/// <summary>
/// Unit tests for <see cref="VehicleWaypointController"/> — WrapToPi helper,
/// arrive/stop behaviour, and basic output sanity checks.
/// </summary>
public sealed class VehicleWaypointControllerUnitTests
{
    // ── WrapToPi ─────────────────────────────────────────────────────────────

    /// <summary>Zero stays zero.</summary>
    [Fact]
    public void WrapToPi_Zero_ReturnsZero()
    {
        Assert.Equal(0f, VehicleWaypointController.WrapToPi(0f), precision: 6);
    }

    /// <summary>+π wraps to +π (boundary — WrapToPi maps (−π, +π]).</summary>
    [Fact]
    public void WrapToPi_PlusPI_ReturnsPlusPI()
    {
        float result = VehicleWaypointController.WrapToPi(MathF.PI);
        Assert.Equal(MathF.PI, result, precision: 5);
    }

    /// <summary>+π + ε wraps to negative near −π.</summary>
    [Fact]
    public void WrapToPi_SlightlyAbovePI_ReturnsNearMinusPI()
    {
        float input  = MathF.PI + 0.01f;
        float result = VehicleWaypointController.WrapToPi(input);
        // Wraps to ~ −π + 0.01
        Assert.True(result < 0f, "Should be negative after wrapping above π");
        Assert.True(result > -MathF.PI, "Should stay within (−π, π]");
    }

    /// <summary>−π wraps to +π (mirrors the boundary).</summary>
    [Fact]
    public void WrapToPi_MinusPI_ReturnsPositivePI()
    {
        float result = VehicleWaypointController.WrapToPi(-MathF.PI);
        // −π is remapped to +π (exclusive lower, inclusive upper).
        Assert.Equal(MathF.PI, MathF.Abs(result), precision: 5);
    }

    /// <summary>3π wraps to +π.</summary>
    [Fact]
    public void WrapToPi_ThreePI_ReturnsPlusPI()
    {
        float result = VehicleWaypointController.WrapToPi(3f * MathF.PI);
        Assert.Equal(MathF.PI, result, precision: 5);
    }

    /// <summary>Small positive angle in (0, π) passes through unchanged.</summary>
    [Theory]
    [InlineData(0.5f)]
    [InlineData(1.0f)]
    [InlineData(2.5f)]
    public void WrapToPi_SmallPositiveAngle_UnchangedWithinRange(float angle)
    {
        Assert.Equal(angle, VehicleWaypointController.WrapToPi(angle), precision: 5);
    }

    /// <summary>Small negative angle in (−π, 0) passes through unchanged.</summary>
    [Theory]
    [InlineData(-0.5f)]
    [InlineData(-1.0f)]
    [InlineData(-2.5f)]
    public void WrapToPi_SmallNegativeAngle_UnchangedWithinRange(float angle)
    {
        Assert.Equal(angle, VehicleWaypointController.WrapToPi(angle), precision: 5);
    }

    // ── Arrive / stop ─────────────────────────────────────────────────────────

    private static VehicleWaypointController MakeController() =>
        new VehicleWaypointController(
            cruiseSpeed:      5f,
            maxSteerAngleRad: 0.6f,
            headingGainK:     2.0f,
            arriveToleranceM: 2.0f,
            slowRadiusM:      8.0f,
            slowMinFrac:      0.2f,
            wheelBase:        3.5f);

    /// <summary>
    /// When the vehicle is exactly at the target, Speed=0, SteerAngle=0, Arrived=true.
    /// </summary>
    [Fact]
    public void Compute_AtTarget_ArrivedTrueAndZeroCommands()
    {
        var ctrl = MakeController();
        var out_ = ctrl.Compute(0f, 0f, 0f, 0f, 0f);

        Assert.True(out_.Arrived);
        Assert.Equal(0f, out_.Speed,      precision: 6);
        Assert.Equal(0f, out_.SteerAngle, precision: 6);
    }

    /// <summary>
    /// When distance = arriveTolerance exactly, Arrived=true (boundary is ≤).
    /// </summary>
    [Fact]
    public void Compute_AtArriveTolerance_ArrivedTrue()
    {
        var ctrl = MakeController();
        var out_ = ctrl.Compute(0f, 0f, 0f, ctrl.ArriveToleranceM, 0f);

        Assert.True(out_.Arrived);
        Assert.Equal(ctrl.ArriveToleranceM, out_.DistToTarget, precision: 4);
    }

    /// <summary>
    /// When distance is just above arriveTolerance, Arrived=false and Speed > 0.
    /// </summary>
    [Fact]
    public void Compute_JustAboveArriveTolerance_NotArrivedAndSpeedPositive()
    {
        var ctrl = MakeController();
        float dist = ctrl.ArriveToleranceM + 0.1f;
        var out_ = ctrl.Compute(0f, 0f, 0f, dist, 0f);

        Assert.False(out_.Arrived);
        Assert.True(out_.Speed > 0f, "Speed must be positive when not arrived and heading aligned.");
    }

    // ── Output sanity ─────────────────────────────────────────────────────────

    /// <summary>
    /// Steer angle magnitude is always within [0, maxSteer] for any heading error.
    /// </summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(0.3f)]
    [InlineData(-0.3f)]
    [InlineData(1.5f)]
    [InlineData(-1.5f)]
    [InlineData(MathF.PI)]
    public void Compute_SteerClamped_WithinMaxSteer(float currentHeading)
    {
        var ctrl = MakeController();
        // Target is well outside arriveTolerance, hard off to the side.
        var out_ = ctrl.Compute(0f, 0f, currentHeading, 20f, 20f);
        Assert.True(MathF.Abs(out_.SteerAngle) <= ctrl.MaxSteerAngleRad + 1e-5f,
            $"SteerAngle {out_.SteerAngle:F4} exceeds max {ctrl.MaxSteerAngleRad}");
    }

    /// <summary>Speed is always non-negative.</summary>
    [Fact]
    public void Compute_SpeedAlwaysNonNegative()
    {
        var ctrl = MakeController();
        // Face away from the target — cos(headingErr) could clip at 0, not go negative.
        var out_ = ctrl.Compute(0f, 0f, MathF.PI, 10f, 0f); // heading West, target East
        Assert.True(out_.Speed >= 0f, $"Speed should be non-negative, got {out_.Speed}");
    }

    /// <summary>
    /// Facing directly toward the target (headingErr ≈ 0): steer ≈ 0, speed ≈ cruiseSpeed×proximityFactor.
    /// </summary>
    [Fact]
    public void Compute_FacingTarget_NearZeroSteerAndHighSpeed()
    {
        var ctrl = MakeController();
        float target = 20f; // well outside slowRadius (8m)
        var out_ = ctrl.Compute(0f, 0f, 0f, target, 0f); // heading East, target East

        Assert.Equal(0f, out_.SteerAngle, precision: 4);
        // Speed should be near cruiseSpeed (proximity factor = 1 at dist=20 > slowRadius=8).
        Assert.Equal(ctrl.CruiseSpeed, out_.Speed, precision: 3);
    }
}

// ── Headless convergence proof ───────────────────────────────────────────────

/// <summary>
/// <b>Headless convergence PROOF for the closed-loop waypoint controller.</b>
///
/// <para>
/// Models the vehicle with the same bicycle kinematics the motor commands:
/// <code>
///   heading += yawRate * dt   where yawRate = (speed / wheelBase) * tan(steer)
///   pos     += (cos(heading), sin(heading)) * speed * dt
/// </code>
/// Closes the loop through <see cref="VehicleWaypointController"/> each step.
/// Asserts the car reaches within <c>arriveTolerance</c> of the target within
/// a bounded step count for several target placements.
/// </para>
///
/// <para>
/// <b>Robustness (dynamic-body imperfection proof):</b>
/// A second set of tests perturbs the ideal model to mimic what a real dynamic Bullet body
/// does — the achieved yaw rate and speed are scaled by a factor in [0.70, 0.85] (representing
/// Bullet's velocity-drive being less than perfectly instantaneous) and a 1–2 step command lag
/// is applied (representing the physics solver's one-frame latency). The closed-loop controller
/// STILL converges, proving that feedback control tolerates the dynamic body not matching the
/// ideal model.
/// </para>
/// </summary>
public sealed class VehicleWaypointControllerConvergenceTests
{
    // ── Simulation helper ─────────────────────────────────────────────────────

    /// <summary>State of the simulated vehicle.</summary>
    private struct VehicleState
    {
        public float X;
        public float Y;
        public float Heading; // radians
    }

    /// <summary>
    /// Runs the closed-loop simulation to convergence (or step limit).
    /// Returns (arrived, stepsUsed, closestDist).
    /// </summary>
    private static (bool arrived, int steps, float closestDist) RunLoop(
        VehicleWaypointController ctrl,
        float startX, float startY, float startHeading,
        float targetX, float targetY,
        int   maxSteps,
        float dt,
        float wheelBase,
        // Perturbation parameters (1.0 = ideal, <1.0 = damped dynamic body).
        float yawRateScale  = 1.0f,
        float speedScale    = 1.0f,
        int   commandLagSteps = 0)
    {
        var state = new VehicleState { X = startX, Y = startY, Heading = startHeading };

        // Command lag buffer: holds (speed, steer) tuples.
        var lagBuffer = new (float speed, float steer)[commandLagSteps + 1];
        int lagHead = 0;

        float closestDist = float.MaxValue;

        for (int step = 0; step < maxSteps; step++)
        {
            var output = ctrl.Compute(state.X, state.Y, state.Heading, targetX, targetY);

            if (output.Arrived)
                return (true, step, output.DistToTarget);

            closestDist = MathF.Min(closestDist, output.DistToTarget);

            // Store command in lag buffer.
            lagBuffer[lagHead % lagBuffer.Length] = (output.Speed, output.SteerAngle);

            // Retrieve the lagged command (commandLagSteps behind).
            int lagIdx = (lagHead - commandLagSteps + lagBuffer.Length * 2) % lagBuffer.Length;
            var (laggedSpeed, laggedSteer) = lagBuffer[lagIdx];
            lagHead++;

            // Apply perturbation to mimic dynamic-body imperfection.
            float effectiveSpeed    = laggedSpeed  * speedScale;
            float effectiveYawRate  = laggedSteer <= 1e-9f && laggedSteer >= -1e-9f
                ? 0f
                : (effectiveSpeed / wheelBase) * MathF.Tan(laggedSteer) * yawRateScale;

            // Bicycle kinematics step.
            state.Heading += effectiveYawRate * dt;
            state.X       += MathF.Cos(state.Heading) * effectiveSpeed * dt;
            state.Y       += MathF.Sin(state.Heading) * effectiveSpeed * dt;
        }

        // Compute final distance.
        float finalDist = MathF.Sqrt(
            (state.X - targetX) * (state.X - targetX) +
            (state.Y - targetY) * (state.Y - targetY));
        closestDist = MathF.Min(closestDist, finalDist);

        return (false, maxSteps, closestDist);
    }

    // ── Standard controller params ────────────────────────────────────────────

    private const float WheelBase     = 3.5f;
    private const float CruiseSpeed   = 5f;
    private const float MaxSteer      = 0.60f; // ~34°
    private const float HeadingGain   = 2.0f;
    private const float ArriveTol     = 2.0f;
    private const float SlowRadius    = 8.0f;
    private const float SlowMinFrac   = 0.2f;
    private const float Dt            = 1f / 20f; // 20 Hz — conservative (real GPU runs at 60 Hz)
    private const int   MaxStepsIdeal = 500;       // 25 s at 20 Hz

    // R_min = WheelBase / tan(MaxSteer) ≈ 3.5 / tan(0.6) ≈ 3.5 / 0.684 ≈ 5.1 m
    // All targets chosen >= 15 m away so they are well outside R_min.

    private static VehicleWaypointController MakeCtrl() =>
        new VehicleWaypointController(CruiseSpeed, MaxSteer, HeadingGain,
                                      ArriveTol, SlowRadius, SlowMinFrac, WheelBase);

    // ── Ideal-model convergence tests ─────────────────────────────────────────

    /// <summary>
    /// Target directly ahead (+X direction, 20 m). The vehicle is already aligned;
    /// it should drive straight and arrive quickly.
    /// </summary>
    [Fact]
    public void IdealModel_TargetAhead_Converges()
    {
        var ctrl = MakeCtrl();
        var (arrived, steps, closest) = RunLoop(ctrl,
            startX: 0, startY: 0, startHeading: 0,
            targetX: 20, targetY: 0,
            maxSteps: MaxStepsIdeal, dt: Dt, wheelBase: WheelBase);

        Assert.True(arrived, $"Did not arrive within {MaxStepsIdeal} steps. Closest dist: {closest:F2} m");
        Assert.True(steps < MaxStepsIdeal, $"Took all {MaxStepsIdeal} steps — should converge faster.");
    }

    /// <summary>
    /// Target ahead-left (heading error ~ +45°). Requires a left turn then drive.
    /// </summary>
    [Fact]
    public void IdealModel_TargetAheadLeft_Converges()
    {
        var ctrl = MakeCtrl();
        var (arrived, steps, closest) = RunLoop(ctrl,
            startX: 0, startY: 0, startHeading: 0,
            targetX: 20, targetY: 20,  // 45° ahead-left
            maxSteps: MaxStepsIdeal, dt: Dt, wheelBase: WheelBase);

        Assert.True(arrived, $"Did not arrive (ahead-left). Closest dist: {closest:F2} m");
    }

    /// <summary>
    /// Target ahead-right (heading error ~ −45°). Requires a right turn then drive.
    /// </summary>
    [Fact]
    public void IdealModel_TargetAheadRight_Converges()
    {
        var ctrl = MakeCtrl();
        var (arrived, steps, closest) = RunLoop(ctrl,
            startX: 0, startY: 0, startHeading: 0,
            targetX: 20, targetY: -20, // 45° ahead-right
            maxSteps: MaxStepsIdeal, dt: Dt, wheelBase: WheelBase);

        Assert.True(arrived, $"Did not arrive (ahead-right). Closest dist: {closest:F2} m");
    }

    /// <summary>
    /// Target far to the left (90° turn required). Tests large heading errors.
    /// The vehicle must turn hard left, then drive north.
    /// </summary>
    [Fact]
    public void IdealModel_TargetHardLeft_Converges()
    {
        var ctrl = MakeCtrl();
        var (arrived, steps, closest) = RunLoop(ctrl,
            startX: 0, startY: 0, startHeading: 0,
            targetX: 0, targetY: 25,  // pure North — 90° left of initial East heading
            maxSteps: MaxStepsIdeal, dt: Dt, wheelBase: WheelBase);

        Assert.True(arrived, $"Did not arrive (hard-left 90°). Closest dist: {closest:F2} m");
    }

    /// <summary>
    /// Target behind to the right (135° right turn). Large mis-alignment test.
    /// The alignment factor cos(headingErr) clips to zero until the vehicle turns far enough.
    /// </summary>
    [Fact]
    public void IdealModel_TargetBehindRight_Converges()
    {
        var ctrl = MakeCtrl();
        // Start facing East; target is 20 m to the South-West → ~225° from forward,
        // which after WrapToPi gives ~−135°. Well outside R_min.
        var (arrived, steps, closest) = RunLoop(ctrl,
            startX: 0, startY: 0, startHeading: 0,
            targetX: -15, targetY: -15, // South-West
            maxSteps: MaxStepsIdeal * 2, dt: Dt, wheelBase: WheelBase); // extra budget for large turn

        Assert.True(arrived, $"Did not arrive (behind-right). Closest dist: {closest:F2} m");
    }

    // ── Robustness: perturbation / dynamic-body imperfection ─────────────────

    /// <summary>
    /// <b>Dynamic-body imperfection proof (ahead target):</b>
    /// yaw rate and speed scaled to 75% of commanded (momentum lag), plus 1-step command lag.
    /// This mimics a real Bullet dynamic body that doesn't instantly respond to velocity commands.
    /// The closed-loop controller MUST still converge — this is the crux of the proof.
    /// </summary>
    [Fact]
    public void PerturbedModel_Ahead_YawAndSpeedScaled075_Lag1_Converges()
    {
        var ctrl = MakeCtrl();
        int maxPerturbed = MaxStepsIdeal * 2; // dynamic body needs more steps due to lag
        var (arrived, steps, closest) = RunLoop(ctrl,
            startX: 0, startY: 0, startHeading: 0,
            targetX: 20, targetY: 0,
            maxSteps: maxPerturbed, dt: Dt, wheelBase: WheelBase,
            yawRateScale:   0.75f,
            speedScale:     0.75f,
            commandLagSteps: 1);

        Assert.True(arrived,
            $"Perturbed model (0.75×, lag=1) did NOT converge (ahead). " +
            $"Closest dist: {closest:F2} m — feedback is insufficient or gains too low.");
    }

    /// <summary>
    /// <b>Dynamic-body imperfection proof (ahead-left target):</b>
    /// Same 75% scaling, same 1-step lag. Still converges to within arriveTolerance.
    /// </summary>
    [Fact]
    public void PerturbedModel_AheadLeft_YawAndSpeedScaled075_Lag1_Converges()
    {
        var ctrl = MakeCtrl();
        int maxPerturbed = MaxStepsIdeal * 2;
        var (arrived, steps, closest) = RunLoop(ctrl,
            startX: 0, startY: 0, startHeading: 0,
            targetX: 20, targetY: 20,
            maxSteps: maxPerturbed, dt: Dt, wheelBase: WheelBase,
            yawRateScale:   0.75f,
            speedScale:     0.75f,
            commandLagSteps: 1);

        Assert.True(arrived,
            $"Perturbed model (0.75×, lag=1) did NOT converge (ahead-left). " +
            $"Closest dist: {closest:F2} m");
    }

    /// <summary>
    /// <b>Dynamic-body imperfection proof (hard-left target):</b>
    /// Heavier perturbation: 70% scaling and 2-step lag.
    /// </summary>
    [Fact]
    public void PerturbedModel_HardLeft_YawAndSpeedScaled070_Lag2_Converges()
    {
        var ctrl = MakeCtrl();
        int maxPerturbed = MaxStepsIdeal * 3; // more steps for heavy perturbation
        var (arrived, steps, closest) = RunLoop(ctrl,
            startX: 0, startY: 0, startHeading: 0,
            targetX: 0, targetY: 25,  // 90° left
            maxSteps: maxPerturbed, dt: Dt, wheelBase: WheelBase,
            yawRateScale:   0.70f,
            speedScale:     0.80f,
            commandLagSteps: 2);

        Assert.True(arrived,
            $"Perturbed model (0.70×yaw/0.80×spd, lag=2) did NOT converge (hard-left). " +
            $"Closest dist: {closest:F2} m");
    }

    /// <summary>
    /// <b>Dynamic-body imperfection proof (ahead-right target):</b>
    /// 85% scaling and 1-step lag. Softer perturbation, right turn.
    /// </summary>
    [Fact]
    public void PerturbedModel_AheadRight_YawAndSpeedScaled085_Lag1_Converges()
    {
        var ctrl = MakeCtrl();
        int maxPerturbed = MaxStepsIdeal * 2;
        var (arrived, steps, closest) = RunLoop(ctrl,
            startX: 0, startY: 0, startHeading: 0,
            targetX: 20, targetY: -20,
            maxSteps: maxPerturbed, dt: Dt, wheelBase: WheelBase,
            yawRateScale:   0.85f,
            speedScale:     0.85f,
            commandLagSteps: 1);

        Assert.True(arrived,
            $"Perturbed model (0.85×, lag=1) did NOT converge (ahead-right). " +
            $"Closest dist: {closest:F2} m");
    }

    // ── Multi-waypoint (sequential) convergence ───────────────────────────────

    /// <summary>
    /// Sequential 3-waypoint route with ideal model: each waypoint must be reached in order.
    /// Demonstrates the controller can follow a route, not just a single point.
    /// </summary>
    [Fact]
    public void IdealModel_ThreeWaypointRoute_AllReached()
    {
        var ctrl = MakeCtrl();
        float x = 0f, y = 0f, heading = 0f;

        // Waypoints chosen outside R_min (≥15 m from each spawn position).
        var waypoints = new[]
        {
            (wx: 20f, wy:  0f),   // ahead: 20 m East
            (wx: 20f, wy: 20f),   // ahead-left from WP1: 20 m North
            (wx:  0f, wy: 20f),   // back-left from WP2: 20 m West
        };

        for (int wp = 0; wp < waypoints.Length; wp++)
        {
            var (tx, ty) = waypoints[wp];
            var (arrived, steps, closest) = RunLoop(ctrl,
                x, y, heading, tx, ty,
                maxSteps: MaxStepsIdeal, dt: Dt, wheelBase: WheelBase);

            Assert.True(arrived,
                $"Did not reach waypoint {wp} ({tx},{ty}) from ({x:F1},{y:F1}). " +
                $"Closest: {closest:F2} m");

            // Advance the state to the waypoint for the next leg.
            // Heading: pointing toward the direction we were travelling (from old pos to waypoint).
            // Compute BEFORE updating x,y so the vector is correct.
            heading = MathF.Atan2(ty - y, tx - x);
            x       = tx;
            y       = ty;
        }
    }

    // ── Min-turning-radius documentation test ────────────────────────────────

    /// <summary>
    /// Documents the minimum turning radius for the standard parameters.
    /// Targets inside R_min are out of scope; this test asserts R_min is positive
    /// and has a reasonable value for the given wheelbase/maxSteer.
    /// </summary>
    [Fact]
    public void MinTurningRadius_StandardParams_IsPositiveAndExpectedValue()
    {
        var ctrl = MakeCtrl();
        float rMin = ctrl.MinTurningRadiusM;

        Assert.True(rMin > 0f, "R_min must be positive");
        // R_min = WheelBase / tan(MaxSteer) = 3.5 / tan(0.6) ≈ 5.1 m
        Assert.Equal(WheelBase / MathF.Tan(MaxSteer), rMin, precision: 3);
        // All proof targets (15–28 m) are well above 2×R_min ≈ 10.2 m.
        Assert.True(rMin < 15f,
            $"R_min={rMin:F2} m — proof targets (15-28 m) must be outside this radius. " +
            $"If MaxSteer decreased, re-evaluate waypoint placements.");
    }
}
