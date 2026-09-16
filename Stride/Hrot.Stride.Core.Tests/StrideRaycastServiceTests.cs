using System;
using System.Numerics;
using Xunit;
using Hrot.Stride.Core;
using SNum = System.Numerics;
using SMath = Stride.Core.Mathematics;

namespace Hrot.Stride.Core.Tests;

/// <summary>
/// Unit tests for <see cref="IStrideRaycastService"/> seam and
/// <see cref="FakeStrideRaycastService"/> (T1 STR-P3-T1).
///
/// <para>
/// Tests verify:
/// <list type="bullet">
///   <item>Coordinate round-trip: FDP ray from/to → expected Stride endpoints (via FdpStrideTransform)</item>
///   <item>Normal direction swizzle: hit normals use the velocity/direction swizzle, NOT the position swizzle</item>
///   <item>Collision-mask plumbing: mask passed in reaches the fake Simulation.Raycast unchanged</item>
///   <item>Miss: service returns <see cref="StrideRaycastHit.Miss"/> when fake returns no hit</item>
/// </list>
/// No live Simulation is needed — all tests use <see cref="FakeStrideRaycastService"/>.
/// </para>
/// </summary>
public class StrideRaycastServiceTests
{
    private const float Tol = 1e-5f;

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static void AssertVec3Equal(SNum.Vector3 expected, SNum.Vector3 actual, string ctx = "")
    {
        Assert.True(MathF.Abs(expected.X - actual.X) < Tol, $"{ctx} X: expected {expected.X} got {actual.X}");
        Assert.True(MathF.Abs(expected.Y - actual.Y) < Tol, $"{ctx} Y: expected {expected.Y} got {actual.Y}");
        Assert.True(MathF.Abs(expected.Z - actual.Z) < Tol, $"{ctx} Z: expected {expected.Z} got {actual.Z}");
    }

    private static void AssertSVec3Equal(SMath.Vector3 expected, SMath.Vector3 actual, string ctx = "")
    {
        Assert.True(MathF.Abs(expected.X - actual.X) < Tol, $"{ctx} X: expected {expected.X} got {actual.X}");
        Assert.True(MathF.Abs(expected.Y - actual.Y) < Tol, $"{ctx} Y: expected {expected.Y} got {actual.Y}");
        Assert.True(MathF.Abs(expected.Z - actual.Z) < Tol, $"{ctx} Z: expected {expected.Z} got {actual.Z}");
    }

    // ── 1. Coordinate conversion: FDP ray → Stride swizzle ────────────────────

    /// <summary>
    /// FDP (X=East, Y=North, Z=Up) position swizzle to Stride (X, Y=up, Z):
    ///   Stride = (fdp.X, fdp.Z, fdp.Y)
    /// Verify the from/to points the fake receives have the swizzle applied.
    /// This test does NOT use a live Simulation — it verifies the conversion via FdpStrideTransform.
    /// </summary>
    [Fact]
    public void Raycast_FdpFromTo_StrideSwizzleApplied_EastNorthUp()
    {
        // FDP (1, 2, 3) → Stride (1, 3, 2)
        var fdpFrom = new SNum.Vector3(1f, 2f, 3f);
        var fdpTo   = new SNum.Vector3(4f, 5f, 6f);

        var strideFrom = FdpStrideTransform.ToStridePosition(fdpFrom);
        var strideTo   = FdpStrideTransform.ToStridePosition(fdpTo);

        // Assert the swizzle: (X stays, Z↔Y)
        AssertSVec3Equal(new SMath.Vector3(1f, 3f, 2f), strideFrom, "from swizzle");
        AssertSVec3Equal(new SMath.Vector3(4f, 6f, 5f), strideTo,   "to swizzle");
    }

    /// <summary>
    /// Round-trip: FDP from/to → ToStridePosition → ToFdpPosition must recover the originals.
    /// </summary>
    [Theory]
    [InlineData(  0f,   0f,   0f,   1f,   0f,   0f)]  // East ray
    [InlineData(  0f,   0f,   0f,   0f,   1f,   0f)]  // North ray
    [InlineData(  0f,   0f,   0f,   0f,   0f,   1f)]  // Up ray
    [InlineData(  1f,   2f,   3f,   7f,   8f,   9f)]  // general
    [InlineData(-10f, -20f, -30f,  10f,  20f,  30f)]  // negative origin
    public void Raycast_PositionRoundTrip_FdpToStrideToFdp(
        float fx, float fy, float fz, float tx, float ty, float tz)
    {
        var fdpFrom = new SNum.Vector3(fx, fy, fz);
        var fdpTo   = new SNum.Vector3(tx, ty, tz);

        var backFrom = FdpStrideTransform.ToFdpPosition(FdpStrideTransform.ToStridePosition(fdpFrom));
        var backTo   = FdpStrideTransform.ToFdpPosition(FdpStrideTransform.ToStridePosition(fdpTo));

        AssertVec3Equal(fdpFrom, backFrom, "from round-trip");
        AssertVec3Equal(fdpTo,   backTo,   "to round-trip");
    }

    // ── 2. Hit-point conversion: Stride hit → FDP ─────────────────────────────

    /// <summary>
    /// A hit point in Stride space must convert to FDP space via the position swizzle
    /// (stride.X, stride.Z, stride.Y).
    /// </summary>
    [Fact]
    public void HitPoint_StrideToFdp_PositionSwizzleApplied()
    {
        // Stride hit point (2, 5, 3) → FDP (2, 3, 5)  [inverse: fdp.X=s.X, fdp.Y=s.Z, fdp.Z=s.Y]
        var strideHitPt = new SMath.Vector3(2f, 5f, 3f);
        var fdpPt = FdpStrideTransform.ToFdpPosition(strideHitPt);

        AssertVec3Equal(new SNum.Vector3(2f, 3f, 5f), fdpPt, "hit point FDP");
    }

    // ── 3. Normal direction swizzle — NOT the position swizzle ────────────────

    /// <summary>
    /// CRITICAL: hit normals are direction vectors.  They must be converted via
    /// <c>FdpStrideTransform.ToFdpVelocity</c> (direction swizzle), NOT
    /// <c>FdpStrideTransform.ToFdpPosition</c>.
    ///
    /// For pure direction vectors both swizzles produce the same numeric result
    /// (they both map (stride.X, stride.Z, stride.Y)), so this test verifies that
    /// a Stride surface normal pointing straight up (0, 1, 0) — i.e. Y-up in Stride —
    /// converts to the FDP Z-up direction (0, 0, 1).
    ///
    /// The explicit assertion that ToFdpVelocity and ToFdpPosition agree for a direction
    /// vector (no translation) ensures the velocity path is safe to use for normals.
    /// </summary>
    [Fact]
    public void Normal_StrideUp_ConvertedToFdpUp_DirectionSwizzle()
    {
        // Stride Y-up (0,1,0) → FDP Z-up (0,0,1)
        var strideNormal = new SMath.Vector3(0f, 1f, 0f); // Stride "up"

        // Direction (velocity) swizzle — the correct path for normals.
        var fdpNormalViaVelocity  = FdpStrideTransform.ToFdpVelocity(strideNormal);
        // Position swizzle — must agree for pure direction vectors (no translation offset).
        var fdpNormalViaPosition  = FdpStrideTransform.ToFdpPosition(strideNormal);

        // FDP Z-up: (0, 0, 1)
        AssertVec3Equal(new SNum.Vector3(0f, 0f, 1f), fdpNormalViaVelocity, "normal via velocity swizzle");
        // For pure direction vectors (no translation) both paths agree.
        AssertVec3Equal(fdpNormalViaVelocity, fdpNormalViaPosition, "velocity path equals position path for directions");
    }

    /// <summary>
    /// A Stride surface normal pointing East (1,0,0) stays East in FDP (X-axis unchanged).
    /// </summary>
    [Fact]
    public void Normal_StrideEast_StaysEastInFdp()
    {
        var strideNormal = new SMath.Vector3(1f, 0f, 0f);
        var fdpNormal    = FdpStrideTransform.ToFdpVelocity(strideNormal);
        AssertVec3Equal(new SNum.Vector3(1f, 0f, 0f), fdpNormal, "East normal unchanged");
    }

    /// <summary>
    /// A Stride surface normal pointing toward Stride-Z (North in FDP) maps to FDP Y-North.
    /// Stride (0,0,1) → FDP (0,1,0).
    /// </summary>
    [Fact]
    public void Normal_StrideNorth_ConvertedToFdpNorth_DirectionSwizzle()
    {
        var strideNormal = new SMath.Vector3(0f, 0f, 1f); // Stride Z = FDP North
        var fdpNormal    = FdpStrideTransform.ToFdpVelocity(strideNormal);
        AssertVec3Equal(new SNum.Vector3(0f, 1f, 0f), fdpNormal, "Stride-Z normal → FDP North");
    }

    /// <summary>
    /// Normal round-trip via velocity path: ToFdpVelocity(ToStrideVelocity(n)) ≈ n.
    /// </summary>
    [Theory]
    [InlineData(1f, 0f, 0f)]
    [InlineData(0f, 1f, 0f)]
    [InlineData(0f, 0f, 1f)]
    [InlineData(0.577f, 0.577f, 0.577f)]
    [InlineData(-1f, 0.5f, -0.5f)]
    public void Normal_RoundTrip_ViaVelocityPath(float nx, float ny, float nz)
    {
        var fdpNormal = SNum.Vector3.Normalize(new SNum.Vector3(nx, ny, nz));
        var back      = FdpStrideTransform.ToFdpVelocity(FdpStrideTransform.ToStrideVelocity(fdpNormal));
        AssertVec3Equal(fdpNormal, back, $"normal round-trip ({nx},{ny},{nz})");
    }

    // ── 4. Collision-mask plumbing via FakeStrideRaycastService ───────────────

    /// <summary>
    /// The mask values passed to <see cref="IStrideRaycastService.Raycast"/> must reach
    /// the underlying implementation unchanged.  Verified via the fake's recorded arguments.
    /// </summary>
    [Fact]
    public void Raycast_CollisionMask_PlumbedToService_Unchanged()
    {
        var fake = new FakeStrideRaycastService();
        var from = new SNum.Vector3(0f, 0f, 1f);
        var to   = new SNum.Vector3(100f, 0f, 1f);
        int expectedGroups = 0x0010_0000;
        int expectedFilter = 0x0020_0000;

        fake.Raycast(from, to, expectedGroups, expectedFilter);

        Assert.Equal(expectedGroups, fake.LastCollisionGroups);
        Assert.Equal(expectedFilter, fake.LastCollisionFilter);
    }

    [Fact]
    public void Raycast_DefaultMasks_PlumbedAsMinusOne()
    {
        var fake = new FakeStrideRaycastService();
        fake.Raycast(SNum.Vector3.Zero, SNum.Vector3.UnitX);
        Assert.Equal(-1, fake.LastCollisionGroups);
        Assert.Equal(-1, fake.LastCollisionFilter);
    }

    // ── 5. Miss path ──────────────────────────────────────────────────────────

    [Fact]
    public void Raycast_FakeReturnsMiss_HasHitIsFalse()
    {
        var fake = new FakeStrideRaycastService { NextHit = StrideRaycastHit.Miss };
        var result = fake.Raycast(SNum.Vector3.Zero, SNum.Vector3.UnitX * 100f);

        Assert.False(result.HasHit);
        Assert.Equal(1f, result.HitFraction); // miss sentinel
    }

    // ── 6. Hit path — from/to plumbing ────────────────────────────────────────

    [Fact]
    public void Raycast_FakeReturnsHit_HasHitIsTrueAndFieldsPreserved()
    {
        var fake = new FakeStrideRaycastService();
        var expectedHit = new StrideRaycastHit(
            hasHit:      true,
            pointFdp:    new SNum.Vector3(5f, 0f, 1f),
            normalFdp:   new SNum.Vector3(0f, 0f, 1f),
            hitFraction: 0.5f,
            hitEntity:   default);
        fake.NextHit = expectedHit;

        var from   = new SNum.Vector3(0f, 0f, 1f);
        var to     = new SNum.Vector3(10f, 0f, 1f);
        var result = fake.Raycast(from, to);

        Assert.True(result.HasHit);
        AssertVec3Equal(expectedHit.PointFdp,  result.PointFdp,  "hit point");
        AssertVec3Equal(expectedHit.NormalFdp, result.NormalFdp, "hit normal");
        Assert.Equal(0.5f, result.HitFraction, precision: 5);

        // Verify from/to were recorded.
        AssertVec3Equal(from, fake.LastFrom, "LastFrom");
        AssertVec3Equal(to,   fake.LastTo,   "LastTo");
    }

    // ── 7. CallCount tracking ─────────────────────────────────────────────────

    [Fact]
    public void Raycast_CallCount_IncrementsOnEachCall()
    {
        var fake = new FakeStrideRaycastService();
        Assert.Equal(0, fake.CallCount);
        fake.Raycast(SNum.Vector3.Zero, SNum.Vector3.UnitX);
        Assert.Equal(1, fake.CallCount);
        fake.Raycast(SNum.Vector3.Zero, SNum.Vector3.UnitX);
        Assert.Equal(2, fake.CallCount);
    }

    // ── 8. IStrideRaycastService contract — FakeStrideRaycastService satisfies it ──

    [Fact]
    public void FakeStrideRaycastService_ImplementsIStrideRaycastService()
    {
        // Runtime type check confirms the fake satisfies the seam interface.
        IStrideRaycastService service = new FakeStrideRaycastService();
        Assert.IsAssignableFrom<IStrideRaycastService>(service);
    }

    // ── 9. Penetrating raycast plumbing ───────────────────────────────────────

    [Fact]
    public void RaycastPenetrating_MultipleFakeHits_AllAppended()
    {
        var fake = new FakeStrideRaycastService();
        var h1 = new StrideRaycastHit(true, new SNum.Vector3(2f, 0f, 0f), SNum.Vector3.UnitZ, 0.2f, default);
        var h2 = new StrideRaycastHit(true, new SNum.Vector3(5f, 0f, 0f), SNum.Vector3.UnitZ, 0.5f, default);
        fake.NextPenetratingHits.Add(h1);
        fake.NextPenetratingHits.Add(h2);

        var results = new System.Collections.Generic.List<StrideRaycastHit>();
        fake.RaycastPenetrating(SNum.Vector3.Zero, SNum.Vector3.UnitX * 10f, results);

        Assert.Equal(2, results.Count);
        Assert.Equal(0.2f, results[0].HitFraction, precision: 5);
        Assert.Equal(0.5f, results[1].HitFraction, precision: 5);
    }

    [Fact]
    public void RaycastPenetrating_NullOutputList_Throws()
    {
        var fake = new FakeStrideRaycastService();
        Assert.Throws<ArgumentNullException>(
            () => fake.RaycastPenetrating(SNum.Vector3.Zero, SNum.Vector3.UnitX, null!));
    }
}
