using System;
using System.Numerics;
using Xunit;
using SNum = System.Numerics;
using SMath = Stride.Core.Mathematics;

namespace Hrot.Stride.Core.Tests;

/// <summary>
/// Behavioral tests for <see cref="FdpStrideTransform"/> — the coordinate seam
/// between FDP (right-handed, X=East, Y=North, Z=Up) and Stride (Y-up, left-handed).
/// All tests assert real numeric values per the DEV-GUIDE test quality contract.
/// (BATCH-01 STR-P0-T4)
/// </summary>
public class FdpStrideTransformTests
{
    private const float Tol = 1e-5f;

    // ── Helpers ────────────────────────────────────────────────────────────

    private static void AssertVec3Equal(SNum.Vector3 expected, SNum.Vector3 actual, string context = "")
    {
        Assert.True(MathF.Abs(expected.X - actual.X) < Tol,
            $"{context} X: expected {expected.X}, got {actual.X}");
        Assert.True(MathF.Abs(expected.Y - actual.Y) < Tol,
            $"{context} Y: expected {expected.Y}, got {actual.Y}");
        Assert.True(MathF.Abs(expected.Z - actual.Z) < Tol,
            $"{context} Z: expected {expected.Z}, got {actual.Z}");
    }

    private static void AssertSVec3Equal(SMath.Vector3 expected, SMath.Vector3 actual, string context = "")
    {
        Assert.True(MathF.Abs(expected.X - actual.X) < Tol,
            $"{context} X: expected {expected.X}, got {actual.X}");
        Assert.True(MathF.Abs(expected.Y - actual.Y) < Tol,
            $"{context} Y: expected {expected.Y}, got {actual.Y}");
        Assert.True(MathF.Abs(expected.Z - actual.Z) < Tol,
            $"{context} Z: expected {expected.Z}, got {actual.Z}");
    }

    /// <summary>Compare quaternions by transformed-vector equality (handles double cover).</summary>
    private static void AssertQuatEquivalent(SNum.Quaternion expected, SNum.Quaternion actual, string context = "")
    {
        // Two quaternions are equivalent if |dot| ≈ 1 (same rotation, possibly negated).
        float dot = MathF.Abs(SNum.Quaternion.Dot(expected, actual));
        Assert.True(dot > 1.0f - Tol,
            $"{context} quaternion not equivalent: |dot|={dot:F8} (expected ≈1)");
    }

    // ── 1. Position round-trip ─────────────────────────────────────────────

    [Theory]
    [InlineData(  0f,   0f,   0f)]
    [InlineData(  1f,   0f,   0f)]  // Unit East
    [InlineData(  0f,   1f,   0f)]  // Unit North
    [InlineData(  0f,   0f,   1f)]  // Unit Up
    [InlineData( -3f,   5f,  -7f)]  // Negatives
    [InlineData(100f, 200f, 300f)]  // Large
    [InlineData(  1.23456789f, -9.87654321f, 42.0f)]
    public void Position_RoundTrip_FdpToStrideToFdp(float x, float y, float z)
    {
        var fdp    = new SNum.Vector3(x, y, z);
        var stride = FdpStrideTransform.ToStridePosition(fdp);
        var back   = FdpStrideTransform.ToFdpPosition(stride);
        AssertVec3Equal(fdp, back, $"roundtrip ({x},{y},{z})");
    }

    // ── 2. Exact axis mapping ──────────────────────────────────────────────

    [Fact]
    public void Position_ExactAxisMapping_FdpEastMapsToStrideX()
    {
        // FDP East (1,0,0) → Stride (1,0,0): East axis unchanged.
        var result = FdpStrideTransform.ToStridePosition(new SNum.Vector3(1, 0, 0));
        AssertSVec3Equal(new SMath.Vector3(1, 0, 0), result, "East");
    }

    [Fact]
    public void Position_ExactAxisMapping_FdpNorthMapsToStrideZ()
    {
        // FDP North (0,1,0) → Stride (0,0,1): North becomes Stride Z.
        var result = FdpStrideTransform.ToStridePosition(new SNum.Vector3(0, 1, 0));
        AssertSVec3Equal(new SMath.Vector3(0, 0, 1), result, "North");
    }

    [Fact]
    public void Position_ExactAxisMapping_FdpUpMapsToStrideY()
    {
        // FDP Up (0,0,1) → Stride (0,1,0): Altitude becomes Stride Y (up).
        var result = FdpStrideTransform.ToStridePosition(new SNum.Vector3(0, 0, 1));
        AssertSVec3Equal(new SMath.Vector3(0, 1, 0), result, "Up");
    }

    [Fact]
    public void Position_ExactComponents_FdpXYZMapsToStrideXZY()
    {
        // FDP (1,2,3) → Stride (1,3,2)
        var result = FdpStrideTransform.ToStridePosition(new SNum.Vector3(1, 2, 3));
        AssertSVec3Equal(new SMath.Vector3(1, 3, 2), result, "(1,2,3)");
    }

    [Fact]
    public void Position_ExactComponents_NegativeValues()
    {
        // FDP (-4,-5,-6) → Stride (-4,-6,-5)
        var result = FdpStrideTransform.ToStridePosition(new SNum.Vector3(-4, -5, -6));
        AssertSVec3Equal(new SMath.Vector3(-4, -6, -5), result, "negatives");
    }

    // ── 3. Rotation homomorphism (handedness-proving) ─────────────────────

    /// <summary>
    /// The homomorphism property:
    ///   ToStridePosition(Transform(v, q)) ≈ Transform(ToStridePosition(v), ToStrideRotation(q))
    ///
    /// This test MUST FAIL if the handedness flip (sign negation) is omitted — a pure
    /// axis-relabel without sign flip breaks it for non-axis-aligned rotations.
    /// </summary>
    [Theory]
    [InlineData(0f, 90f, 0f, 1f, 0f, 0f)]  // yaw+90° applied to East vector
    [InlineData(45f, 0f, 0f, 0f, 1f, 0f)]  // pitch+45° applied to North vector
    [InlineData(90f, 45f, 0f, 1f, 1f, 0f)] // combined: exercises handedness
    [InlineData(30f, 60f, 0f, 0f, 0f, 1f)] // combined applied to Up vector
    public void Rotation_Homomorphism_PreservesTransformUnderSwizzle(
        float yawDeg, float pitchDeg, float rollDeg,
        float vx, float vy, float vz)
    {
        var fdpQ = MakeFdpQuaternion(yawDeg, pitchDeg, rollDeg);
        var fdpV = new SNum.Vector3(vx, vy, vz);

        // Left side: transform in FDP, then convert to Stride.
        var transformedFdp  = SNum.Vector3.Transform(fdpV, fdpQ);
        var leftSide        = FdpStrideTransform.ToStridePosition(transformedFdp);

        // Right side: convert both to Stride, then transform there.
        var strideQ         = FdpStrideTransform.ToStrideRotation(fdpQ);
        var strideV         = FdpStrideTransform.ToStridePosition(fdpV);
        var rightSide = SMath.Vector3.Transform(strideV, strideQ);

        Assert.True(
            MathF.Abs(leftSide.X - rightSide.X) < Tol &&
            MathF.Abs(leftSide.Y - rightSide.Y) < Tol &&
            MathF.Abs(leftSide.Z - rightSide.Z) < Tol,
            $"Homomorphism failed for yaw={yawDeg}°, pitch={pitchDeg}°, roll={rollDeg}°, " +
            $"v=({vx},{vy},{vz}): left={leftSide}, right={rightSide}");
    }

    /// <summary>
    /// Combined yaw+pitch rotation that specifically exercises the handedness invariant.
    /// A pure axis-relabel (no sign flip) breaks this test.
    /// </summary>
    [Fact]
    public void Rotation_Homomorphism_CombinedYawPitch_HandednessProof()
    {
        // Use a rotation that is NOT aligned with any axis to force handedness failure
        // when the sign flip is absent.
        var fdpQ = MakeFdpQuaternion(yawDeg: 45f, pitchDeg: 30f, rollDeg: 0f);
        var fdpV = new SNum.Vector3(1f, 1f, 0f); // diagonal North-East vector

        var transformedFdp = SNum.Vector3.Transform(fdpV, fdpQ);
        var leftSide       = FdpStrideTransform.ToStridePosition(transformedFdp);

        var strideQ  = FdpStrideTransform.ToStrideRotation(fdpQ);
        var strideV  = FdpStrideTransform.ToStridePosition(fdpV);
        var rightSide = SMath.Vector3.Transform(strideV, strideQ);

        Assert.True(
            MathF.Abs(leftSide.X - rightSide.X) < Tol &&
            MathF.Abs(leftSide.Y - rightSide.Y) < Tol &&
            MathF.Abs(leftSide.Z - rightSide.Z) < Tol,
            $"Handedness proof failed: left={leftSide}, right={rightSide}. " +
            "This test fails when the sign flip is omitted (pure axis-relabel is not enough).");
    }

    // ── 4. Known facing: FDP yaw+90° faces North ──────────────────────────

    /// <summary>
    /// FDP convention: yaw=0 means facing East (+X), yaw=+90° means facing North (+Y).
    ///
    /// Under the swizzle:
    ///   FDP East forward (1,0,0) → Stride East (1,0,0)
    ///   FDP North forward (0,1,0) → Stride (0,0,1)
    ///
    /// So a yaw+90° FDP rotation applied to the unit East vector (FDP forward) should yield
    /// the FDP North vector. Converting that FDP North result to Stride gives (0,0,1).
    ///
    /// Alternatively: the Stride rotation of a yaw+90° FDP rotation should rotate
    /// Stride's East (1,0,0) to Stride's North (0,0,1).
    ///
    /// Forward convention documented here:
    ///   FDP "forward" = +X (East) = yaw=0 heading.
    ///   In Stride, the swizzled East is +X, the swizzled North is +Z.
    /// </summary>
    [Fact]
    public void Rotation_KnownFacing_FdpYaw90_FacesNorth()
    {
        // FDP yaw=+90° rotation (around FDP Z = Up axis): applied to East (1,0,0) → North (0,1,0)
        var fdpYaw90 = SNum.Quaternion.CreateFromAxisAngle(SNum.Vector3.UnitZ, MathF.PI / 2f);
        var fdpForward = new SNum.Vector3(1, 0, 0); // East = FDP forward

        // FDP: rotate forward by yaw+90° should give North
        var fdpResult = SNum.Vector3.Transform(fdpForward, fdpYaw90);
        AssertVec3Equal(new SNum.Vector3(0, 1, 0), fdpResult, "FDP yaw90 forward should be North");

        // Now verify the Stride equivalent:
        // In Stride, our "forward" (FDP East after swizzle) is (1,0,0).
        // Applying ToStrideRotation(fdpYaw90) to Stride East should give Stride North = (0,0,1).
        var strideQ    = FdpStrideTransform.ToStrideRotation(fdpYaw90);
        var strideEast = new SMath.Vector3(1, 0, 0); // Stride East = FDP East (unchanged)
        var strideFacing = SMath.Vector3.Transform(strideEast, strideQ);

        // Stride North (swizzled FDP North Y→Z) is (0,0,1).
        AssertSVec3Equal(new SMath.Vector3(0, 0, 1), strideFacing, "Stride yaw90 result should be (0,0,1) = Stride North");
    }

    // ── 5. Rotation round-trip ─────────────────────────────────────────────

    [Theory]
    [InlineData(  0f,   0f,   0f)]   // identity
    [InlineData( 90f,   0f,   0f)]   // yaw only
    [InlineData(  0f,  45f,   0f)]   // pitch only
    [InlineData(  0f,   0f,  30f)]   // roll only
    [InlineData( 45f,  30f,  15f)]   // combined
    [InlineData(-90f, -45f,  60f)]   // negatives
    public void Rotation_RoundTrip_FdpToStrideToFdp(float yawDeg, float pitchDeg, float rollDeg)
    {
        var q    = MakeFdpQuaternion(yawDeg, pitchDeg, rollDeg);
        var back = FdpStrideTransform.ToFdpRotation(FdpStrideTransform.ToStrideRotation(q));
        AssertQuatEquivalent(q, back, $"roundtrip ({yawDeg},{pitchDeg},{rollDeg})");
    }

    // ── 6. Velocity swizzle matches position swizzle ───────────────────────

    [Theory]
    [InlineData(1f, 2f, 3f)]
    [InlineData(-5f, 0f, 7f)]
    [InlineData(0f, 0f, 0f)]
    public void Velocity_ToStrideVelocity_SameSwizzleAsToStridePosition(float vx, float vy, float vz)
    {
        var v = new SNum.Vector3(vx, vy, vz);

        var asPosition = FdpStrideTransform.ToStridePosition(v);
        var asVelocity = FdpStrideTransform.ToStrideVelocity(v);

        // Velocity and position use the identical swizzle (no translation term).
        AssertSVec3Equal(asPosition, asVelocity, $"velocity vs position swizzle ({vx},{vy},{vz})");
    }

    [Theory]
    [InlineData(1f, 2f, 3f)]
    [InlineData(-1f, -2f, -3f)]
    [InlineData(0f, 0f, 0f)]
    public void Velocity_RoundTrip_FdpToStrideToFdp(float vx, float vy, float vz)
    {
        var v    = new SNum.Vector3(vx, vy, vz);
        var back = FdpStrideTransform.ToFdpVelocity(FdpStrideTransform.ToStrideVelocity(v));
        AssertVec3Equal(v, back, $"velocity roundtrip ({vx},{vy},{vz})");
    }

    [Fact]
    public void AngularVelocity_KnownInput_SignAndAxisCorrect()
    {
        // FDP angular velocity: [roll, pitch, yaw] around [X, Y, Z].
        // A yaw rate of +1 rad/s in FDP (rotation around FDP Z=Up) should map to
        // a Stride angular velocity around Stride Y=Up of -1 rad/s (sign negated for LH).
        //
        // Stride AngVel layout: the swizzle is (FDP_X→Stride_X, FDP_Z→Stride_Y, FDP_Y→Stride_Z)
        // with sign negation.
        // FDP angvel [0, 0, 1] (pure yaw) → Stride [0, -1, 0] via ToFdpAngularVelocity inverse.
        //
        // Let's test the known conversion both ways:

        // Stride angular velocity (1, 2, 3):
        // ToFdpAngularVelocity = (-1, -3, -2)  [negate all, then swizzle back X→X, Z→Y, Y→Z]
        var strideAng = new SMath.Vector3(1f, 2f, 3f);
        var fdpAng    = FdpStrideTransform.ToFdpAngularVelocity(strideAng);
        AssertVec3Equal(new SNum.Vector3(-1f, -3f, -2f), fdpAng, "angular velocity known value");
    }

    [Fact]
    public void AngularVelocity_ZeroInput_ReturnsZero()
    {
        var fdpAng = FdpStrideTransform.ToFdpAngularVelocity(SMath.Vector3.Zero);
        AssertVec3Equal(SNum.Vector3.Zero, fdpAng, "zero angular velocity");
    }

    // ── Helper: create an FDP quaternion from yaw-pitch-roll (degrees) ────

    /// <summary>
    /// Creates an FDP quaternion using the documented rotation order:
    /// yaw first (around Z), then pitch (around Y), then roll (around X).
    /// Angles in degrees; right-handed.
    ///
    /// <para>FDP convention (SimComponents.cs):
    /// yaw=0 → East (+X), yaw+90 → North (+Y), positive yaw is CCW from above.</para>
    /// </summary>
    private static SNum.Quaternion MakeFdpQuaternion(float yawDeg, float pitchDeg, float rollDeg)
    {
        float toRad = MathF.PI / 180f;
        var yaw   = SNum.Quaternion.CreateFromAxisAngle(SNum.Vector3.UnitZ, yawDeg   * toRad);
        var pitch = SNum.Quaternion.CreateFromAxisAngle(SNum.Vector3.UnitY, pitchDeg * toRad);
        var roll  = SNum.Quaternion.CreateFromAxisAngle(SNum.Vector3.UnitX, rollDeg  * toRad);
        // FDP order: yaw first (Z), then pitch (Y), then roll (X) → multiply right to left.
        return SNum.Quaternion.Normalize(yaw * pitch * roll);
    }
}
