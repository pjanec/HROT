#nullable enable
using System;
using HrotStrideApp;
using Hrot.Stride.Core;
using Xunit;
using SMath = Stride.Core.Mathematics;
using SNum = System.Numerics;

namespace HrotStrideApp.Tests;

/// <summary>
/// Headless unit tests for <see cref="CenterOnEntityCommand"/> camera math (STR-P5-T3, BATCH-23).
///
/// <para>
/// All tests run without a GPU, a window, or any Stride graphics context.
/// The math is pure (position swizzle + offset + look-at quaternion) and testable headlessly.
/// </para>
///
/// <para>
/// BATCH-24 fixes applied here:
/// <list type="bullet">
///   <item><b>Distance doubled:</b> offset (0,2,−3) → (0,4,−6).</item>
///   <item><b>Orientation fixed:</b> Stride cameras look down local −Z.
///     <see cref="CenterOnEntityCommand.RotationFromForward"/> aligns local +Z to the supplied
///     vector, so <c>Compute</c> now passes <c>normalize(camPos−target)</c> (the backward
///     direction) so that −Z points toward the target.</item>
/// </list>
/// All orientation tests now assert that the camera's <b>−Z axis</b> (not +Z) points toward
/// the target — i.e. <c>dot(−localZ, target−camPos) &gt; 0</c>.
/// </para>
/// </summary>
public sealed class CenterOnEntityCommandTests
{
    private const float Tol = 1e-3f;

    // ── B24-CAM-1: FDP origin → Stride camera at (0, 4, -6) ─────────────

    /// <summary>
    /// Entity at FDP (0, 0, 0):
    /// <list type="bullet">
    ///   <item>Stride target = (0, 0, 0).</item>
    ///   <item>Camera = target + CameraOffset = (0, 4, −6).</item>
    /// </list>
    /// </summary>
    [Fact]
    public void Compute_FdpOrigin_CameraAtOffset()
    {
        var fdpPos = SNum.Vector3.Zero;

        CenterOnEntityCommand.Compute(fdpPos, out var camPos, out _);

        // Stride target = FdpStrideTransform.ToStridePosition((0,0,0)) = (0,0,0).
        // Camera offset = (0, +4, −6).
        AssertVec3(new SMath.Vector3(0f, 4f, -6f), camPos, "camPos origin");
    }

    // ── B24-CAM-2: FDP (3, 5, 0) → correct Stride camera position ─────────

    /// <summary>
    /// Entity at FDP (3, 5, 0) — a soldier standing on the ground north-east of origin.
    /// FDP swizzle: Stride = (fdp.X, fdp.Z, fdp.Y) = (3, 0, 5).
    /// Camera = (3, 0, 5) + (0, +4, −6) = (3, 4, −1).
    /// </summary>
    [Fact]
    public void Compute_EntityNorthEast_CameraAtCorrectOffset()
    {
        var fdpPos = new SNum.Vector3(3f, 5f, 0f);

        CenterOnEntityCommand.Compute(fdpPos, out var camPos, out _);

        // Stride target = (3, 0, 5).  Camera = target + (0, +4, −6) = (3, 4, −1).
        AssertVec3(new SMath.Vector3(3f, 4f, -1f), camPos, "camPos NE entity");
    }

    // ── B24-CAM-3: Camera's −Z points TOWARD entity (orientation fix) ──────

    /// <summary>
    /// The rotation returned by <see cref="CenterOnEntityCommand.Compute"/> must make the
    /// camera's local <b>−Z</b> axis point toward the entity (Stride cameras look down −Z).
    ///
    /// <para>
    /// Verify: dot(−localZ_in_world, normalize(target − camPos)) &gt; 0.
    /// </para>
    /// </summary>
    [Fact]
    public void Compute_EntityAtOrigin_NegativeZPointsTowardEntity()
    {
        var fdpPos = SNum.Vector3.Zero; // Stride target = (0, 0, 0)

        CenterOnEntityCommand.Compute(fdpPos, out var camPos, out var camRot);

        // The camera's local +Z in world space:
        var localPlusZ = SMath.Vector3.UnitZ;
        SMath.Vector3.Transform(ref localPlusZ, ref camRot, out var worldPlusZ);

        // Local −Z = negated +Z in world.
        var worldMinusZ = -worldPlusZ;

        // Direction from camera to target.
        var toTarget = SMath.Vector3.Normalize(
            new SMath.Vector3(0f, 0f, 0f) - camPos);  // target is (0,0,0)

        // −Z must point toward the target: dot > 0.
        float d = SMath.Vector3.Dot(worldMinusZ, toTarget);
        Assert.True(d > 0.9f,
            $"Camera −Z must point toward entity: dot={d:F4} (worldMinusZ={worldMinusZ}, toTarget={toTarget})");
    }

    // ── B24-CAM-4: General entity position — orientation + position correct ──

    /// <summary>
    /// Entity at FDP (−3, 7, 2) (elevated position).
    /// Stride target: X=−3, Y=fdp.Z=2, Z=fdp.Y=7 → (−3, 2, 7).
    /// Camera: (−3, 2, 7) + (0, +4, −6) = (−3, 6, 1).
    /// Camera −Z must point toward (−3, 2, 7).
    /// </summary>
    [Fact]
    public void Compute_ElevatedEntity_CameraPositionAndOrientationCorrect()
    {
        var fdpPos = new SNum.Vector3(-3f, 7f, 2f);

        CenterOnEntityCommand.Compute(fdpPos, out var camPos, out var camRot);

        // Position check.
        AssertVec3(new SMath.Vector3(-3f, 6f, 1f), camPos, "camPos elevated entity");

        // −Z orientation: dot(worldMinusZ, normalize(target−cam)) > 0.
        var strideTarget = new SMath.Vector3(-3f, 2f, 7f);
        var toTarget = SMath.Vector3.Normalize(strideTarget - camPos);

        var localPlusZ = SMath.Vector3.UnitZ;
        SMath.Vector3.Transform(ref localPlusZ, ref camRot, out var worldPlusZ);
        var worldMinusZ = -worldPlusZ;

        float d = SMath.Vector3.Dot(worldMinusZ, toTarget);
        Assert.True(d > 0.9f,
            $"Camera −Z must point toward elevated entity: dot={d:F4}");
    }

    // ── B24-CAM-5: RotationFromForward identity (forward = UnitZ) ─────────

    /// <summary>
    /// <see cref="CenterOnEntityCommand.RotationFromForward"/> with the identity forward (0,0,1)
    /// returns the identity quaternion (no rotation needed).
    /// </summary>
    [Fact]
    public void RotationFromForward_UnitZ_ReturnsIdentity()
    {
        var q = CenterOnEntityCommand.RotationFromForward(SMath.Vector3.UnitZ);

        AssertQuat(SMath.Quaternion.Identity, q, "identity forward");
    }

    // ── B24-CAM-6: RotationFromForward opposite direction → 180° around Y ──

    /// <summary>
    /// <see cref="CenterOnEntityCommand.RotationFromForward"/> with (0,0,−1) (camera looks
    /// backward) returns a 180° rotation around Y, not a degenerate quaternion.
    /// After rotation, (0,0,1) should become (0,0,−1).
    /// </summary>
    [Fact]
    public void RotationFromForward_NegativeZ_Rotates180AroundY()
    {
        var q = CenterOnEntityCommand.RotationFromForward(new SMath.Vector3(0f, 0f, -1f));

        // Apply to UnitZ — should get (0,0,−1).
        var fwd = SMath.Vector3.UnitZ;
        SMath.Vector3.Transform(ref fwd, ref q, out var result);
        AssertVec3(new SMath.Vector3(0f, 0f, -1f), result, "180° around Y");
    }

    // ── B24-CAM-7: RotationFromForward normalised output (unit quaternion) ─

    /// <summary>
    /// The quaternion returned by <see cref="CenterOnEntityCommand.RotationFromForward"/> is
    /// always a unit quaternion (|q|=1) regardless of the input direction.
    /// </summary>
    [Theory]
    [InlineData( 0f,  0f,  1f)]   // identity
    [InlineData( 0f,  0f, -1f)]   // opposite
    [InlineData( 1f,  0f,  0f)]   // 90° yaw
    [InlineData(-1f,  0f,  0f)]   // −90° yaw
    [InlineData( 0f,  1f,  0f)]   // pure up
    [InlineData( 0.577f, 0.577f, 0.577f)]  // diagonal (will be normalised by the method)
    public void RotationFromForward_AlwaysReturnsUnitQuaternion(float fx, float fy, float fz)
    {
        // The method expects a normalised forward input — normalise it.
        var fwdRaw = new SMath.Vector3(fx, fy, fz);
        var fwd    = SMath.Vector3.Normalize(fwdRaw);

        var q    = CenterOnEntityCommand.RotationFromForward(fwd);
        float mag = (float)Math.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);

        Assert.True(Math.Abs(mag - 1f) < 0.001f,
            $"Quaternion for forward ({fx},{fy},{fz}) must be unit; |q|={mag}");
    }

    // ── B25-CAM-10: Camera local UP (rotation·+Y) has positive dot with world +Y ──

    /// <summary>
    /// The rotation produced by <see cref="CenterOnEntityCommand.Compute"/> must keep the
    /// camera upright — i.e. its local +Y axis (worldUp = rotation·(0,1,0)) must have a
    /// POSITIVE dot product with world +Y (not flipped / rolled ~180°).
    ///
    /// <para>
    /// This is the key assertion for the BATCH-25 Part-A fix: the old shortest-arc formula
    /// produced a ~180° roll (local +Y pointing downward), making the view upside-down.
    /// The new yaw+pitch decomposition guarantees zero roll.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData( 0f,   0f,  0f)]   // origin
    [InlineData( 5f,  10f,  0f)]   // NE ground
    [InlineData(-3f,   7f,  2f)]   // elevated
    [InlineData( 0f,   0f,  3f)]   // elevated origin
    [InlineData(10f, -10f,  1f)]   // south-east
    public void Compute_LocalUpY_IsUprightNotFlipped(float ex, float ey, float ez)
    {
        var fdpPos = new SNum.Vector3(ex, ey, ez);
        CenterOnEntityCommand.Compute(fdpPos, out _, out var camRot);

        // Rotate world +Y by the camera quaternion to get the camera's local +Y in world space.
        var localUp = SMath.Vector3.UnitY;
        SMath.Vector3.Transform(ref localUp, ref camRot, out var worldUp);

        // The camera's local +Y must align with world +Y (positive dot → not flipped/rolled).
        float d = SMath.Vector3.Dot(worldUp, SMath.Vector3.UnitY);
        Assert.True(d > 0f,
            $"FDP({ex},{ey},{ez}): camera local-UP dot world-UP = {d:F4}. " +
            "Negative means the camera is upside-down (roll ~180°). Fix: use yaw+pitch decomposition.");
    }

    // ── B25-CAM-11: RotationFromForward world-UP positive for all directions ─

    /// <summary>
    /// <see cref="CenterOnEntityCommand.RotationFromForward"/> must produce a camera whose
    /// local +Y has a positive dot with world +Y for all non-degenerate forward directions.
    /// </summary>
    [Theory]
    [InlineData( 0f,   0f,   1f)]    // straight ahead (+Z)
    [InlineData( 0f,   0f,  -1f)]    // straight back  (−Z)
    [InlineData( 1f,   0f,   0f)]    // right (+X)
    [InlineData(-1f,   0f,   0f)]    // left  (−X)
    [InlineData( 0f,  -0.5f, 0.866f)]// slight upward pitch
    [InlineData( 0.5f,-0.5f, 0.707f)]// upward-right diagonal
    public void RotationFromForward_WorldUpPositive_NoRoll(float fx, float fy, float fz)
    {
        var fwd = SMath.Vector3.Normalize(new SMath.Vector3(fx, fy, fz));
        var q   = CenterOnEntityCommand.RotationFromForward(fwd);

        var localUp = SMath.Vector3.UnitY;
        SMath.Vector3.Transform(ref localUp, ref q, out var worldUp);

        float d = SMath.Vector3.Dot(worldUp, SMath.Vector3.UnitY);
        Assert.True(d > 0f,
            $"forward=({fx},{fy},{fz}): camera local-UP dot world-UP = {d:F4}. " +
            "Negative means the rotation has a roll component. Fix: yaw+pitch decomposition.");
    }

    // ── B24-CAM-8: FDP position swizzle is correct ────────────────────────

    /// <summary>
    /// Verify the FDP→Stride swizzle used by <see cref="CenterOnEntityCommand.Compute"/>:
    /// FDP (X, Y, Z) maps to Stride (X, Z, Y) — i.e. the entity's altitude (FDP.Z)
    /// maps to Stride.Y and FDP.Y (North) maps to Stride.Z.
    /// </summary>
    [Fact]
    public void Compute_SwizzleVerification_FdpYMapsToStrideZ_FdpZMapsToStrideY()
    {
        // Entity at FDP (0, 10, 5) — 10 m North, 5 m up.
        // Stride target = (0, 5, 10).
        // Camera = (0, 5, 10) + (0, +4, −6) = (0, 9, 4).
        var fdpPos = new SNum.Vector3(0f, 10f, 5f);

        CenterOnEntityCommand.Compute(fdpPos, out var camPos, out _);

        AssertVec3(new SMath.Vector3(0f, 9f, 4f), camPos, "swizzle verification");
    }

    // ── B24-CAM-9: Camera −Z dot toward target > 0 for arbitrary positions ─

    /// <summary>
    /// For several FDP entity positions, verifies that the camera's local −Z axis points
    /// toward the entity with dot product &gt; 0.9 (nearly perfect alignment, since the
    /// offset direction is fixed).
    /// </summary>
    [Theory]
    [InlineData( 0f,   0f,  0f)]   // origin
    [InlineData( 5f,  10f,  0f)]   // NE ground
    [InlineData(-3f,   7f,  2f)]   // elevated
    [InlineData( 0f,   0f,  3f)]   // elevated origin
    [InlineData(10f, -10f,  1f)]   // south-east
    public void Compute_NegativeZ_AlwaysPointsTowardTarget(float ex, float ey, float ez)
    {
        var fdpPos = new SNum.Vector3(ex, ey, ez);
        CenterOnEntityCommand.Compute(fdpPos, out var camPos, out var camRot);

        // Stride target from FDP position.
        var fdpNum = new System.Numerics.Vector3(ex, ey, ez);
        var strideTarget = Hrot.Stride.Core.FdpStrideTransform.ToStridePosition(fdpNum);

        var toTarget = SMath.Vector3.Normalize(strideTarget - camPos);

        var localPlusZ = SMath.Vector3.UnitZ;
        SMath.Vector3.Transform(ref localPlusZ, ref camRot, out var worldPlusZ);
        var worldMinusZ = -worldPlusZ;

        float d = SMath.Vector3.Dot(worldMinusZ, toTarget);
        Assert.True(d > 0.9f,
            $"FDP({ex},{ey},{ez}): camera −Z dot toward target = {d:F4}, expected > 0.9");
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static void AssertVec3(SMath.Vector3 expected, SMath.Vector3 actual, string ctx)
    {
        Assert.True(MathF.Abs(expected.X - actual.X) < Tol, $"{ctx} X: expected {expected.X}, got {actual.X}");
        Assert.True(MathF.Abs(expected.Y - actual.Y) < Tol, $"{ctx} Y: expected {expected.Y}, got {actual.Y}");
        Assert.True(MathF.Abs(expected.Z - actual.Z) < Tol, $"{ctx} Z: expected {expected.Z}, got {actual.Z}");
    }

    private static void AssertQuat(SMath.Quaternion expected, SMath.Quaternion actual, string ctx)
    {
        // Quaternions q and −q represent the same rotation; check both signs.
        bool close =
            (MathF.Abs(expected.X - actual.X) < Tol &&
             MathF.Abs(expected.Y - actual.Y) < Tol &&
             MathF.Abs(expected.Z - actual.Z) < Tol &&
             MathF.Abs(expected.W - actual.W) < Tol)
            ||
            (MathF.Abs(expected.X + actual.X) < Tol &&
             MathF.Abs(expected.Y + actual.Y) < Tol &&
             MathF.Abs(expected.Z + actual.Z) < Tol &&
             MathF.Abs(expected.W + actual.W) < Tol);

        Assert.True(close, $"{ctx}: expected ({expected.X},{expected.Y},{expected.Z},{expected.W}), " +
                           $"got ({actual.X},{actual.Y},{actual.Z},{actual.W})");
    }
}
