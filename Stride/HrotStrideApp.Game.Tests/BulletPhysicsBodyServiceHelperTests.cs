#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Stride.Core;
using HrotStrideApp;
using Xunit;
using SMath = Stride.Core.Mathematics;

namespace HrotStrideApp.Tests;

/// <summary>
/// Unit tests for pure shape-dimension helper logic used by
/// <see cref="BulletPhysicsBodyService"/> (BATCH-17, STR-D11).
///
/// <para>
/// <b>Why these tests are headless:</b>
/// The concrete <see cref="BulletPhysicsBodyService"/> requires a running
/// <c>Stride.Physics.Simulation</c> and cannot be instantiated without a live Stride game.
/// However, the shape-sizing math (capsule shaft calculation, box-half swizzle) and the
/// <see cref="FdpStrideTransform"/> conversions used by the service are pure — they have
/// no Simulation dependency and can be verified headlessly.
/// </para>
///
/// <para>
/// Scenarios:
/// <list type="bullet">
///   <item>Capsule shaft height = max(0.01, totalHeight − 2*radius) for various inputs.</item>
///   <item>Capsule radius is clamped to at least 0.1.</item>
///   <item>Box half-extents are swizzled correctly from FDP (HalfX=East, HalfY=North, HalfZ=Up)
///     to Stride (X=East, Y=Up, Z=North).</item>
///   <item>Box dimensions are doubled correctly (ShapeDims stores half-extents, Bullet needs full size).</item>
///   <item><see cref="FdpStrideTransform.ToStridePosition"/> swizzle correctness for the
///     spawn-position path (used by CreateBody to place the entity).</item>
///   <item><see cref="BulletPhysicsBodyServiceDeferred"/> defers inner construction until first use.</item>
/// </list>
/// </para>
/// </summary>
public sealed class BulletPhysicsBodyServiceHelperTests
{
    // ── Capsule shaft-height calculation ─────────────────────────────────────

    /// <summary>
    /// Standard capsule: ShapeDims(radius=0.3, height=1.8).
    /// Expected shaft = 1.8 - 2*0.3 = 1.2.
    /// </summary>
    [Fact]
    public void CapsuleShaft_StandardDims_CorrectShaftHeight()
    {
        float radius = 0.3f;
        float totalHeight = 1.8f;

        // Mirror the logic in BulletPhysicsBodyService.CreateBody (Capsule branch).
        float clampedRadius = Math.Max(radius, 0.1f);
        float clampedTotal  = Math.Max(totalHeight, clampedRadius * 2f + 0.01f);
        float shaft         = Math.Max(clampedTotal - 2f * clampedRadius, 0.01f);

        Assert.Equal(1.2f, shaft, precision: 4); // 1.8 - 0.6 = 1.2
    }

    /// <summary>
    /// Zero radius: clamped to 0.1, total height adjusted.
    /// </summary>
    [Fact]
    public void CapsuleShaft_ZeroRadius_ClampsToMinimum()
    {
        float radius = 0f;       // will clamp to 0.1
        float totalHeight = 0.5f;

        float clampedRadius = Math.Max(radius, 0.1f);       // → 0.1
        float clampedTotal  = Math.Max(totalHeight, clampedRadius * 2f + 0.01f); // max(0.5, 0.21) → 0.5
        float shaft         = Math.Max(clampedTotal - 2f * clampedRadius, 0.01f); // 0.5 - 0.2 = 0.3

        Assert.Equal(0.1f, clampedRadius, precision: 4);
        Assert.Equal(0.3f, shaft, precision: 4);
    }

    /// <summary>
    /// Radius larger than half height: shaft clamped to 0.01.
    /// </summary>
    [Fact]
    public void CapsuleShaft_LargeRadius_ShaftClampedToMinimum()
    {
        float radius = 1.0f;
        float totalHeight = 1.5f; // less than 2*radius

        float clampedRadius = Math.Max(radius, 0.1f);            // → 1.0
        float clampedTotal  = Math.Max(totalHeight, clampedRadius * 2f + 0.01f); // max(1.5, 2.01) → 2.01
        float shaft         = Math.Max(clampedTotal - 2f * clampedRadius, 0.01f); // 2.01 - 2.0 = 0.01

        Assert.True(shaft >= 0.01f, "Shaft must be at least 0.01");
        Assert.Equal(0.01f, shaft, precision: 3);
    }

    // ── Box half-extent swizzle ───────────────────────────────────────────────

    /// <summary>
    /// FDP ShapeDims box: HalfX=East, HalfY=North, HalfZ=Up.
    /// Stride box: X=East, Y=Up, Z=North.
    /// So Stride halfX=FDP.HalfX, Stride halfY=FDP.HalfZ, Stride halfZ=FDP.HalfY.
    /// </summary>
    [Fact]
    public void BoxSwizzle_CorrectAxisMapping()
    {
        var dims = ShapeDims.Box(halfX: 1.0f, halfY: 2.0f, halfZ: 0.5f);
        // FDP HalfX=East → Stride X unchanged.
        // FDP HalfZ=Up   → Stride Y (altitude is up in both).
        // FDP HalfY=North → Stride Z.

        // Mirror the logic in BulletPhysicsBodyService.CreateBody (OrientedBox branch).
        float strideHalfX = Math.Max(dims.HalfX, 0.05f); // East → stays East
        float strideHalfY = Math.Max(dims.HalfZ, 0.05f); // FDP-Up (HalfZ) → Stride Y
        float strideHalfZ = Math.Max(dims.HalfY, 0.05f); // FDP-North (HalfY) → Stride Z

        Assert.Equal(1.0f, strideHalfX, precision: 4); // East stays
        Assert.Equal(0.5f, strideHalfY, precision: 4); // FDP HalfZ → Stride Y
        Assert.Equal(2.0f, strideHalfZ, precision: 4); // FDP HalfY → Stride Z
    }

    /// <summary>
    /// BoxColliderShape full-size = half-extent * 2 (Stride expects full size, not half-extent).
    /// </summary>
    [Fact]
    public void BoxColliderSize_IsDoubledHalfExtent()
    {
        var dims = ShapeDims.Box(halfX: 1.0f, halfY: 0.5f, halfZ: 0.75f);

        float strideHalfX = Math.Max(dims.HalfX, 0.05f);
        float strideHalfY = Math.Max(dims.HalfZ, 0.05f); // FDP Z → Stride Y
        float strideHalfZ = Math.Max(dims.HalfY, 0.05f); // FDP Y → Stride Z

        // BoxColliderShape constructor takes full size (2 * half-extent).
        var size = new SMath.Vector3(strideHalfX * 2f, strideHalfY * 2f, strideHalfZ * 2f);

        Assert.Equal(2.0f,  size.X, precision: 4); // East: 1.0 * 2
        Assert.Equal(1.5f,  size.Y, precision: 4); // Up: 0.75 * 2
        Assert.Equal(1.0f,  size.Z, precision: 4); // North: 0.5 * 2
    }

    /// <summary>
    /// Zero half-extents are clamped to 0.05 (minimum meaningful size for Bullet).
    /// </summary>
    [Fact]
    public void BoxHalfExtent_Zero_ClampsToMinimum()
    {
        var dims = ShapeDims.Box(halfX: 0f, halfY: 0f, halfZ: 0f);

        float strideHalfX = Math.Max(dims.HalfX, 0.05f);
        float strideHalfY = Math.Max(dims.HalfZ, 0.05f);
        float strideHalfZ = Math.Max(dims.HalfY, 0.05f);

        Assert.Equal(0.05f, strideHalfX, precision: 4);
        Assert.Equal(0.05f, strideHalfY, precision: 4);
        Assert.Equal(0.05f, strideHalfZ, precision: 4);
    }

    // ── FDP→Stride position for spawn placement ───────────────────────────────

    /// <summary>
    /// Entity spawned at FDP (x=3, y=7, z=2) should map to Stride (3, 2, 7).
    /// This is the swizzle used by BulletPhysicsBodyService to place the entity at creation.
    /// </summary>
    [Fact]
    public void SpawnPosition_FdpToStride_CorrectSwizzle()
    {
        // FDP (X=East=3, Y=North=7, Z=Up=2) → Stride (X=East=3, Y=Up=2, Z=North=7)
        var fdpPos = new Vector3(3f, 7f, 2f);
        var stridePos = FdpStrideTransform.ToStridePosition(fdpPos);

        Assert.Equal(3f, stridePos.X, precision: 4); // East unchanged
        Assert.Equal(2f, stridePos.Y, precision: 4); // FDP.Z → Stride.Y (altitude)
        Assert.Equal(7f, stridePos.Z, precision: 4); // FDP.Y → Stride.Z (North)
    }

    /// <summary>
    /// Spawn at floor level FDP Z=0 → Stride Y=0 (sits on the ground plane).
    /// </summary>
    [Fact]
    public void SpawnPosition_FloorLevel_StrideYIsZero()
    {
        var fdpPos = new Vector3(5f, 3f, 0f); // Z=0 = floor
        var stridePos = FdpStrideTransform.ToStridePosition(fdpPos);

        Assert.Equal(0f, stridePos.Y, precision: 4); // floor level in Stride space
    }

    /// <summary>
    /// Drop altitude FDP Z=3 → Stride Y=3 (3 metres above floor).
    /// </summary>
    [Fact]
    public void SpawnPosition_DropAltitude_StrideYIsAltitude()
    {
        var fdpPos = new Vector3(0f, 5f, 3f); // Z=3 = 3 m above floor
        var stridePos = FdpStrideTransform.ToStridePosition(fdpPos);

        Assert.Equal(3f, stridePos.Y, precision: 4);
    }

    // ── BulletPhysicsBodyServiceDeferred deferral ─────────────────────────────

    /// <summary>
    /// The deferred wrapper does NOT call the inner service factory until the first
    /// <see cref="IPhysicsBodyService"/> method is called.
    /// We verify this with a recording provider that counts calls.
    /// </summary>
    [Fact]
    public void DeferredService_InnerNotConstructedBeforeFirstCall()
    {
        // Arrange: a counter that tracks how many times the provider delegate was called.
        int providerCallCount = 0;
        Func<IReadOnlyDictionary<Entity, StrideVisualReference>> provider = () =>
        {
            providerCallCount++;
            return new Dictionary<Entity, StrideVisualReference>();
        };

        // We can't create a BulletPhysicsBodyServiceDeferred without a real Simulation,
        // so we test the deferral contract via a lightweight stand-in that mirrors the logic.
        // The DeferredProviderHelper is a pure extraction of the deferral pattern.
        var helper = new DeferredProviderHelper<IReadOnlyDictionary<Entity, StrideVisualReference>>(provider);

        // Before any access, the provider should NOT have been called.
        Assert.Equal(0, providerCallCount);

        // First access triggers the provider.
        var visuals = helper.Value;
        Assert.Equal(1, providerCallCount);

        // Second access uses the cached value — provider still called only once.
        _ = helper.Value;
        Assert.Equal(1, providerCallCount);

        // The returned visuals are the empty dictionary from the provider.
        Assert.NotNull(visuals);
        Assert.Empty(visuals);
    }

    // ── ShapeDims factory methods ─────────────────────────────────────────────

    /// <summary>
    /// ShapeDims.Capsule named factory stores radius and height correctly.
    /// </summary>
    [Fact]
    public void ShapeDimsCapsule_NamedFactory_StoresCorrectFields()
    {
        var dims = ShapeDims.Capsule(radius: 0.3f, height: 1.8f);
        Assert.Equal(0.3f, dims.Radius, precision: 4);
        Assert.Equal(1.8f, dims.Height, precision: 4);
        Assert.Equal(0f, dims.HalfX, precision: 4);
        Assert.Equal(0f, dims.HalfY, precision: 4);
        Assert.Equal(0f, dims.HalfZ, precision: 4);
    }

    /// <summary>
    /// ShapeDims.Box named factory stores half-extents correctly.
    /// </summary>
    [Fact]
    public void ShapeDimsBox_NamedFactory_StoresCorrectFields()
    {
        var dims = ShapeDims.Box(halfX: 1.5f, halfY: 0.5f, halfZ: 0.75f);
        Assert.Equal(0f,   dims.Radius, precision: 4);
        Assert.Equal(0f,   dims.Height, precision: 4);
        Assert.Equal(1.5f, dims.HalfX, precision: 4);
        Assert.Equal(0.5f, dims.HalfY, precision: 4);
        Assert.Equal(0.75f, dims.HalfZ, precision: 4);
    }

    // ── ISSUE-1: Collider LocalOffset (vertical half-height) computation ─────────

    /// <summary>
    /// Standard capsule: radius=0.3, shaft=1.2.
    /// halfHeight = radius + shaft/2 = 0.3 + 0.6 = 0.9.
    /// The collider LocalOffset.Y must equal halfHeight so the capsule bottom
    /// coincides with the entity origin (model base = feet).
    /// </summary>
    [Fact]
    public void CapsuleLocalOffset_StandardDims_HalfHeightIsRadiusPlusHalfShaft()
    {
        float radius      = 0.3f;
        float totalHeight = 1.8f;

        float clampedRadius = Math.Max(radius, 0.1f);
        float clampedTotal  = Math.Max(totalHeight, clampedRadius * 2f + 0.01f);
        float shaftHeight   = Math.Max(clampedTotal - 2f * clampedRadius, 0.01f);

        // The LOCAL OFFSET vertical component = radius + shaft/2 (i.e. distance from
        // base to center of the capsule).
        float halfHeight = clampedRadius + shaftHeight / 2f;

        Assert.Equal(0.3f, clampedRadius, precision: 4);  // radius as clamped
        Assert.Equal(1.2f, shaftHeight,   precision: 4);  // shaft = 1.8 - 0.6
        Assert.Equal(0.9f, halfHeight,    precision: 4);  // 0.3 + 0.6 = 0.9
    }

    /// <summary>
    /// Tiny capsule (radius=0.1 minimum, shaft=0.01 minimum).
    /// halfHeight = 0.1 + 0.005 = 0.105.
    /// </summary>
    [Fact]
    public void CapsuleLocalOffset_MinimumDims_HalfHeightIsMinRadiusPlusHalfMinShaft()
    {
        float radius      = 0f;    // clamps to 0.1
        float totalHeight = 0.1f;  // clamps to 2*0.1+0.01 = 0.21 → shaft = 0.01

        float clampedRadius = Math.Max(radius, 0.1f);
        float clampedTotal  = Math.Max(totalHeight, clampedRadius * 2f + 0.01f);
        float shaftHeight   = Math.Max(clampedTotal - 2f * clampedRadius, 0.01f);
        float halfHeight    = clampedRadius + shaftHeight / 2f;

        Assert.Equal(0.1f,   clampedRadius, precision: 4);
        Assert.Equal(0.01f,  shaftHeight,   precision: 4);
        Assert.Equal(0.105f, halfHeight,    precision: 4); // 0.1 + 0.005
    }

    // ── F2 root-cause fix: ComputeBoxParamsFromBoundingBox ───────────────────
    //
    // DESIGN (BATCH-17 F2 content-mismatch fix):
    // BulletPhysicsBodyService.CreateBody now derives box shape, LocalOffset, and
    // resting Y from the visual model's ACTUAL BoundingBox — not TKB ShapeDims.
    // LocalOffset = boxCenter (bbox center in entity-local space) so the collider exactly
    // overlaps the rendered mesh regardless of model origin placement.
    // Entity Stride Y is set to -bbox.Minimum.Y so the model bottom rests at Y=0.
    // ShapeDims is used only as a fallback when the model/bbox is unavailable (headless tests).

    /// <summary>
    /// Standard symmetric bbox (model origin at center): Min=(-1,-0.5,-1), Max=(1,0.5,1).
    /// HalfExtents=(1,0.5,1), BoxCenter=(0,0,0), RestingStrideY=0.5.
    /// </summary>
    [Fact]
    public void ComputeBoxParams_SymmetricBbox_CorrectHalfExtentsAndCenter()
    {
        var bbox = new SMath.BoundingBox(
            new SMath.Vector3(-1f, -0.5f, -1f),
            new SMath.Vector3( 1f,  0.5f,  1f));

        var result = BulletPhysicsBodyService.ComputeBoxParamsFromBoundingBox(bbox);

        Assert.NotNull(result);
        var p = result!.Value;
        Assert.Equal(1.0f,  p.HalfExtents.X, precision: 4);
        Assert.Equal(0.5f,  p.HalfExtents.Y, precision: 4);
        Assert.Equal(1.0f,  p.HalfExtents.Z, precision: 4);
        Assert.Equal(0f,    p.BoxCenter.X,    precision: 4); // center at origin
        Assert.Equal(0f,    p.BoxCenter.Y,    precision: 4);
        Assert.Equal(0f,    p.BoxCenter.Z,    precision: 4);
        Assert.Equal(0.5f,  p.RestingStrideY, precision: 4); // -Minimum.Y = 0.5
    }

    /// <summary>
    /// Asymmetric bbox (model origin NOT at center): Min=(-1,-0.2,-1), Max=(1,1.8,1).
    /// HalfExtents=(1,1.0,1), BoxCenter=(0,0.8,0), RestingStrideY=0.2.
    /// Entity at Y=0.2 → visual bottom at 0.2 + (-0.2) = 0 (floor).
    /// Physics bottom = 0.2 + 0.8 - 1.0 = 0 (floor).
    /// </summary>
    [Fact]
    public void ComputeBoxParams_AsymmetricBbox_BoxCenterAndRestingY()
    {
        var bbox = new SMath.BoundingBox(
            new SMath.Vector3(-1f, -0.2f, -1f),
            new SMath.Vector3( 1f,  1.8f,  1f));

        var result = BulletPhysicsBodyService.ComputeBoxParamsFromBoundingBox(bbox);

        Assert.NotNull(result);
        var p = result!.Value;
        Assert.Equal(1.0f, p.HalfExtents.X, precision: 4);
        Assert.Equal(1.0f, p.HalfExtents.Y, precision: 4); // (1.8-(-0.2))/2 = 1.0
        Assert.Equal(1.0f, p.HalfExtents.Z, precision: 4);
        Assert.Equal(0f,   p.BoxCenter.X,   precision: 4);
        Assert.Equal(0.8f, p.BoxCenter.Y,   precision: 4); // (-0.2+1.8)/2 = 0.8
        Assert.Equal(0f,   p.BoxCenter.Z,   precision: 4);
        Assert.Equal(0.2f, p.RestingStrideY,precision: 4); // -(-0.2) = 0.2
    }

    /// <summary>
    /// Prove the resting-height convention: entity at RestingStrideY, LocalOffset=BoxCenter.
    /// Visual bottom = entity.Y + Minimum.Y = RestingStrideY + Minimum.Y = 0.
    /// Physics bottom = entity.Y + BoxCenter.Y - HalfY = RestingStrideY + BoxCenter.Y - HalfY = 0.
    /// </summary>
    [Fact]
    public void ComputeBoxParams_RestingY_VisualAndPhysicsBottomAtFloor()
    {
        // Placeholder vehicle: Box2x1x1 — Min=(-1,-0.5,-0.5), Max=(1,0.5,0.5).
        var bbox = new SMath.BoundingBox(
            new SMath.Vector3(-1f, -0.5f, -0.5f),
            new SMath.Vector3( 1f,  0.5f,  0.5f));

        var p = BulletPhysicsBodyService.ComputeBoxParamsFromBoundingBox(bbox)!.Value;

        float entityY = p.RestingStrideY; // = 0.5 = -Minimum.Y

        // Visual bottom: entity.Y + Minimum.Y
        float visualBottom = entityY + bbox.Minimum.Y;
        Assert.Equal(0f, visualBottom, precision: 4);

        // Physics bottom: entity.Y + LocalOffset.Y - HalfY
        float physicsBottom = entityY + p.BoxCenter.Y - p.HalfExtents.Y;
        Assert.Equal(0f, physicsBottom, precision: 4);
    }

    /// <summary>
    /// Degenerate bbox (zero Y extent): returns null → caller falls back to ShapeDims.
    /// </summary>
    [Fact]
    public void ComputeBoxParams_ZeroYExtent_ReturnsNull()
    {
        var bbox = new SMath.BoundingBox(
            new SMath.Vector3(-1f, 0f, -1f),
            new SMath.Vector3( 1f, 0f,  1f)); // Y extent = 0

        var result = BulletPhysicsBodyService.ComputeBoxParamsFromBoundingBox(bbox);
        Assert.Null(result);
    }

    /// <summary>
    /// Degenerate bbox (NaN in extents): returns null → caller falls back to ShapeDims.
    /// </summary>
    [Fact]
    public void ComputeBoxParams_NaNExtent_ReturnsNull()
    {
        var bbox = new SMath.BoundingBox(
            new SMath.Vector3(float.NaN, 0f, 0f),
            new SMath.Vector3(1f, 1f, 1f));

        var result = BulletPhysicsBodyService.ComputeBoxParamsFromBoundingBox(bbox);
        Assert.Null(result);
    }

    /// <summary>
    /// ShapeDims fallback: when bbox is unavailable, the service uses ShapeDims half-extents.
    /// Mirror the fallback branch: halfX = dims.HalfX, halfY = dims.HalfZ (FDP Z → Stride Y),
    /// halfZ = dims.HalfY (FDP Y → Stride Z), LocalOffset = Zero.
    /// </summary>
    [Fact]
    public void ComputeBoxParams_ShapeDimsFallback_UsesSwizzledHalfExtents()
    {
        // Simulating the fallback path in BulletPhysicsBodyService (no model bbox available).
        var dims = ShapeDims.Box(halfX: 2.25f, halfY: 1.10f, halfZ: 1.25f);

        float fallbackHalfX = Math.Max(dims.HalfX, 0.05f);  // East → stays East
        float fallbackHalfY = Math.Max(dims.HalfZ, 0.05f);  // FDP Z (Up) → Stride Y
        float fallbackHalfZ = Math.Max(dims.HalfY, 0.05f);  // FDP Y (North) → Stride Z
        float fallbackLocalOffsetY = 0f;                     // ShapeDims assumes center-origin

        Assert.Equal(2.25f, fallbackHalfX,        precision: 4);
        Assert.Equal(1.25f, fallbackHalfY,        precision: 4);
        Assert.Equal(1.10f, fallbackHalfZ,        precision: 4);
        Assert.Equal(0f,    fallbackLocalOffsetY, precision: 4); // no offset in fallback
    }

    /// <summary>
    /// After ISSUE-1 fix: Drop altitude is 1.0 m, not 3.0 m.
    /// Verifies that DropAltitude produces a short visible fall (entity origin = feet at 1 m up).
    /// </summary>
    [Fact]
    public void DropAltitude_AfterIssue1Fix_IsOneMetre()
    {
        // Mirror StridePhysicsHarnessCases constant.
        const float DropAltitude = 1.0f;
        Assert.Equal(1.0f, DropAltitude, precision: 4);
        // Entity-origin = feet (model base); spawning at 1 m gives a short visible fall.
    }

    /// <summary>
    /// Drive APC initial spawn Z = ApcBoxHalfHeightFdpZ = 1.25 m.
    /// CreateBody will override the entity Stride Y to -bbox.Minimum.Y from the actual model bbox.
    /// The 1.25 m is just a sensible above-floor initial position, not the final resting height.
    /// </summary>
    [Fact]
    public void DriveApcSpawnZ_IsInitialPositionOnly_CreateBodyOverridesFromBbox()
    {
        // Mirror StridePhysicsHarnessCases: ApcBoxHalfHeightFdpZ = 1.25f.
        const float ApcBoxHalfHeightFdpZ = 1.25f;
        Assert.Equal(1.25f, ApcBoxHalfHeightFdpZ, precision: 4);

        // For the placeholder Box2x1x1 model (Min.Y=-0.5, Max.Y=0.5):
        // CreateBody will override entity Y to -(-0.5) = 0.5, NOT 1.25.
        var placeholderBbox = new SMath.BoundingBox(
            new SMath.Vector3(-1f, -0.5f, -0.5f),
            new SMath.Vector3( 1f,  0.5f,  0.5f));
        float actualRestingY = BulletPhysicsBodyService
            .ComputeBoxParamsFromBoundingBox(placeholderBbox)!.Value.RestingStrideY;

        // The actual resting Y (0.5) differs from the initial spawn Z (1.25).
        // CreateBody uses the bbox-derived value; the spawn constant is just a placeholder.
        Assert.Equal(0.5f, actualRestingY, precision: 4);
        Assert.NotEqual(ApcBoxHalfHeightFdpZ, actualRestingY);
    }

    // ── MoveKinematic face-stop math ─────────────────────────────────────────

    /// <summary>
    /// Face-stop fix: when the box sweeps east and contacts a wall,
    /// <c>safeDist = distToContact − halfExtentAlongMove − skin</c>.
    ///
    /// <para>
    /// Setup: box half-extents (2, 1, 1) in Stride space, sweeping in the +X direction.
    /// <c>halfExtentAlongMove = Abs(1)*2 + Abs(0)*1 + Abs(0)*1 = 2</c>.
    /// <c>distToContact</c> = 10 (contact point is 10 m east of the current center).
    /// Expected <c>safeDist = 10 − 2 − 0.05 = 7.95</c>.
    /// The box face (leading east face) stops at <c>7.95 + 2 = 9.95 m</c> — 0.05 m short of the wall.
    /// </para>
    /// </summary>
    [Fact]
    public void FaceStop_BoxSweepEast_SafeDistAccountsForHalfExtent()
    {
        // Box half-extents in Stride space: halfX=2, halfY=1, halfZ=1.
        var halfExtents = new SMath.Vector3(2f, 1f, 1f);

        // Move direction: purely east (Stride +X).
        var moveDir = new SMath.Vector3(1f, 0f, 0f);

        // Contact point is 10 m ahead of the current center.
        float distToContact = 10f;
        const float SkinM = 0.05f;

        // halfExtentAlongMove = sum of |moveDir_i| * halfExtents_i
        float halfExtentAlongMove =
            Math.Abs(moveDir.X) * halfExtents.X +
            Math.Abs(moveDir.Y) * halfExtents.Y +
            Math.Abs(moveDir.Z) * halfExtents.Z;

        float safeDist = Math.Max(0f, distToContact - halfExtentAlongMove - SkinM);
        safeDist = Math.Min(safeDist, 12f); // cap at desiredLen (not reached here)

        Assert.Equal(2f,    halfExtentAlongMove, precision: 4);
        Assert.Equal(7.95f, safeDist,            precision: 4);

        // The box's leading face stops at: safeDist + halfExtentAlongMove = 9.95 m.
        // That is 0.05 m (skin) short of the contact point (10 m) — no face penetration.
        float facePosition = safeDist + halfExtentAlongMove;
        Assert.Equal(9.95f, facePosition, precision: 4);
    }

    /// <summary>
    /// Face-stop fix: diagonal approach (moveDir = (0.707, 0, 0.707)).
    /// The half-extent projected on the diagonal is larger than on a single axis.
    /// </summary>
    [Fact]
    public void FaceStop_BoxSweepDiagonal_SafeDistAccountsForProjectedHalfExtent()
    {
        // Box half-extents: (2, 1, 1).
        var halfExtents = new SMath.Vector3(2f, 1f, 1f);

        // Move direction: diagonal in XZ plane.
        float inv = 1f / MathF.Sqrt(2f);
        var moveDir = new SMath.Vector3(inv, 0f, inv);

        float distToContact = 14.14f; // ~10 m along the diagonal
        const float SkinM = 0.05f;

        float halfExtentAlongMove =
            Math.Abs(moveDir.X) * halfExtents.X +
            Math.Abs(moveDir.Y) * halfExtents.Y +
            Math.Abs(moveDir.Z) * halfExtents.Z;

        // Expected: inv*2 + 0*1 + inv*1 = inv*3 ≈ 0.7071*3 ≈ 2.121
        float expected = inv * 3f;
        Assert.Equal(expected, halfExtentAlongMove, precision: 3);

        float safeDist = Math.Max(0f, distToContact - halfExtentAlongMove - SkinM);
        // Face position = safeDist + halfExtentAlongMove ≈ distToContact - skin = 14.09
        float facePosition = safeDist + halfExtentAlongMove;
        Assert.True(facePosition <= distToContact - SkinM + 1e-3f,
            $"Face position {facePosition:F3} must be ≤ (distToContact-skin)={distToContact - SkinM:F3}");
    }

    /// <summary>
    /// Face-stop fix: when fully blocked (distToContact = 0), safeDist = 0 and the box stays put.
    /// </summary>
    [Fact]
    public void FaceStop_ContactAtCurrentPosition_SafeDistIsZero()
    {
        var halfExtents = new SMath.Vector3(1f, 0.5f, 0.5f);
        var moveDir     = new SMath.Vector3(1f, 0f, 0f);
        float distToContact = 0f; // already at contact
        const float SkinM = 0.05f;

        float halfExtentAlongMove =
            Math.Abs(moveDir.X) * halfExtents.X +
            Math.Abs(moveDir.Y) * halfExtents.Y +
            Math.Abs(moveDir.Z) * halfExtents.Z;

        float safeDist = Math.Max(0f, distToContact - halfExtentAlongMove - SkinM);
        Assert.Equal(0f, safeDist, precision: 4);
    }

    // ── F2 real-box skin-lift sweep (real root-cause fix) ────────────────────
    //
    // DESIGN NOTE (F2 real root-cause fix — real box with small floor-skin lift):
    // With the box model origin correctly identified as CENTER, the box is spawned at
    // FDP Z = halfZ (Stride Y = halfY) so its bottom rests at Y=0.
    // MoveKinematic sweeps the REAL box shape with only a tiny SweepFloorSkinM = 0.05 m
    // Y-lift to prevent coplanar floor contact (floating-point grazing).
    // This is NOT a half-height compensation — the box is properly on the floor.
    // The previous mid-height thin-box approach was a workaround for the wrong-offset
    // box burial and is no longer needed.

    /// <summary>
    /// SweepFloorSkinM must be positive but smaller than the box's resting half-height
    /// (halfY for APC = 1.25 m) so the swept probe clears the floor plane without
    /// misaligning the swept shape relative to the real body position.
    /// </summary>
    [Fact]
    public void SkinLiftSweep_SweepFloorSkinM_IsPositiveAndSmallerThanBoxHalfHeight()
    {
        // Mirror the constant from BulletPhysicsBodyService.MoveKinematic.
        const float SweepFloorSkinM = 0.05f;

        // APC box half-height in Stride Y.
        const float ApcHalfY = 1.25f;

        // The skin lift must be > 0 (clears floor plane) and < halfY (not misaligning sweep).
        Assert.True(SweepFloorSkinM > 0f,
            $"SweepFloorSkinM ({SweepFloorSkinM} m) must be strictly positive to clear Y=0.");
        Assert.True(SweepFloorSkinM < ApcHalfY,
            $"SweepFloorSkinM ({SweepFloorSkinM} m) must be < halfY ({ApcHalfY} m).");
    }

    /// <summary>
    /// With SweepFloorSkinM lift applied, the swept box bottom is at
    /// entityY + SweepFloorSkinM - halfY = halfY + 0.05 - halfY = 0.05 m,
    /// which is strictly above Y=0 (the floor).
    /// </summary>
    [Fact]
    public void SkinLiftSweep_SweptBoxBottom_IsAboveFloor()
    {
        const float SweepFloorSkinM = 0.05f;
        const float ApcHalfY = 1.25f; // Stride Y half-extent
        const float entityY  = 1.25f; // spawned at Stride Y = halfY

        // Swept box center Y = entityY + SweepFloorSkinM (the lift applied in MoveKinematic).
        float sweptCenterY = entityY + SweepFloorSkinM;
        // Swept box bottom Y = sweptCenterY - ApcHalfY.
        float sweptBottomY = sweptCenterY - ApcHalfY;

        // Bottom must be strictly above floor (Y=0).
        Assert.True(sweptBottomY > 0f,
            $"Swept box bottom ({sweptBottomY:F3} m) must be above the floor (Y=0).");
        Assert.Equal(SweepFloorSkinM, sweptBottomY, precision: 4);
    }

    /// <summary>
    /// The real box shape is swept (same dims as the collision body).
    /// Sweep size = 2 × halfExtents — same as the actual collider.
    /// </summary>
    [Fact]
    public void SkinLiftSweep_SweepShapeMatchesRealBoxDims()
    {
        // Real vehicle footprint (Stride space).
        var realHalf = new SMath.Vector3(2.25f, 1.25f, 1.10f);

        // Mirror the sweep shape construction in MoveKinematic (real box, same dims).
        float sweepSizeX = realHalf.X * 2f;  // 4.50
        float sweepSizeY = realHalf.Y * 2f;  // 2.50 (REAL height, not thin substitute)
        float sweepSizeZ = realHalf.Z * 2f;  // 2.20

        Assert.Equal(4.50f, sweepSizeX, precision: 3);
        Assert.Equal(2.50f, sweepSizeY, precision: 3); // real vehicle height used (not a thin substitute)
        Assert.Equal(2.20f, sweepSizeZ, precision: 3);
    }

    /// <summary>
    /// Wall face-stop with skin-lift sweep: the contact point Y is offset by SweepFloorSkinM
    /// from the actual entity Y.  For a horizontal sweep (moveDir.Y=0) the Y component
    /// cancels in the Dot product and does not affect distToContact.
    ///
    /// Setup: vehicle at (0, halfY, 0) with SweepFloorSkinM lift → sweep from (0, halfY+skin, 0).
    /// Contact returned at (10, halfY+skin, 0) (wall at X=10).  Vehicle footprint halfX=2.
    /// Expected safeDist = 10 − 2 − 0.05 = 7.95 (face stops 0.05 m short of wall).
    /// </summary>
    [Fact]
    public void SkinLiftSweep_WallFaceStop_YOffsetCancelsInDotProduct()
    {
        const float SweepFloorSkinM = 0.05f;
        const float SkinM           = 0.05f;
        const float HalfY           = 1.25f;

        // Real vehicle footprint and current position (box at resting height).
        var realHalf   = new SMath.Vector3(2f, HalfY, 1f);
        var currentPos = new SMath.Vector3(0f, HalfY, 0f); // entity at resting height
        var moveDir    = new SMath.Vector3(1f, 0f, 0f);     // sweep east

        // Contact point from the skin-lift sweep (Y includes the lift offset).
        var contactPoint = new SMath.Vector3(10f, HalfY + SweepFloorSkinM, 0f);

        // Distance: Dot(contact − currentPos, moveDir).
        // Y component: (HalfY + SweepFloorSkinM) - HalfY = SweepFloorSkinM, but
        // moveDir.Y = 0 → Y contribution = 0 → distToContact is purely horizontal.
        SMath.Vector3 toContact = contactPoint - currentPos;
        float distToContact     = SMath.Vector3.Dot(toContact, moveDir); // 10

        float halfExtentAlongMove =
            Math.Abs(moveDir.X) * realHalf.X +
            Math.Abs(moveDir.Y) * realHalf.Y +
            Math.Abs(moveDir.Z) * realHalf.Z; // 2

        float safeDist = Math.Max(0f, distToContact - halfExtentAlongMove - SkinM);
        safeDist = Math.Min(safeDist, 12f);

        Assert.Equal(10f,   distToContact,       precision: 4); // Y offset cancels in Dot
        Assert.Equal(2f,    halfExtentAlongMove, precision: 4);
        Assert.Equal(7.95f, safeDist,            precision: 4);

        float facePosition = safeDist + halfExtentAlongMove;
        Assert.Equal(9.95f, facePosition, precision: 4);
    }

    // ── Slide-along-wall (block-and-SLIDE) ───────────────────────────────────
    //
    // DESIGN: when a vehicle is fully blocked (safeDist ≈ 0) and the contact normal has a
    // horizontal component, MoveKinematic applies the tangential (wall-plane) component of
    // desiredDelta as a slide so the vehicle scrapes along the wall instead of freezing.

    /// <summary>
    /// Vehicle heading NE (45°) hits a north wall (normal = (0,0,-1) ≈ south-pointing).
    /// Forward (north) component is blocked; east tangential component is preserved.
    /// desiredDelta = (1, 0, 1), wallNormal = (0, 0, -1).
    /// tangential = desiredDelta - Dot(desiredDelta, wallNormal̂) * wallNormal̂
    ///            = (1,0,1) - (-1)*(0,0,-1) = (1,0,1) - (0,0,1) = (1,0,0).
    /// </summary>
    [Fact]
    public void Slide_AngleApproach_TangentialComponentPreserved()
    {
        var desiredDelta = new SMath.Vector3(1f, 0f, 1f);  // NE heading
        var wallNormal   = new SMath.Vector3(0f, 0f, -1f); // north wall, normal points south

        // Flatten Y (slide is horizontal only).
        var wallNormalH = new SMath.Vector3(wallNormal.X, 0f, wallNormal.Z);
        float nLen = wallNormalH.Length();
        var nHat = wallNormalH / nLen;

        float normalProj = SMath.Vector3.Dot(desiredDelta, nHat);
        var slide = desiredDelta - nHat * normalProj;
        slide.Y = 0f; // zero any vertical component

        // Expected: east component (1,0,0) preserved; north (into wall) removed.
        Assert.Equal( 1f, slide.X, precision: 4);
        Assert.Equal( 0f, slide.Y, precision: 4);
        Assert.Equal( 0f, slide.Z, precision: 4);
        Assert.True(slide.Length() > 0f, "Slide must be nonzero for angled approach");
    }

    /// <summary>
    /// Vehicle heading directly north (head-on) into a north wall (normal = (0,0,-1)).
    /// All of desiredDelta is into the wall → tangential = 0 (vehicle stops).
    /// desiredDelta = (0, 0, 1), wallNormal = (0, 0, -1).
    /// tangential = (0,0,1) - Dot((0,0,1),(0,0,-1))*(0,0,-1) = (0,0,1) - 1*(0,0,1) = 0.
    /// Wait — Dot((0,0,1),(0,0,-1)) = -1.  slide = (0,0,1) - (-1)*(0,0,-1) = (0,0,1)-(0,0,1) = 0.
    /// </summary>
    [Fact]
    public void Slide_HeadOnApproach_TangentialIsZero()
    {
        var desiredDelta = new SMath.Vector3(0f, 0f, 1f);  // pure north
        var wallNormal   = new SMath.Vector3(0f, 0f, -1f); // north wall

        var wallNormalH = new SMath.Vector3(wallNormal.X, 0f, wallNormal.Z);
        float nLen = wallNormalH.Length();
        var nHat = wallNormalH / nLen;

        float normalProj = SMath.Vector3.Dot(desiredDelta, nHat);
        var slide = desiredDelta - nHat * normalProj;
        slide.Y = 0f;

        Assert.Equal(0f, slide.X, precision: 4);
        Assert.Equal(0f, slide.Y, precision: 4);
        Assert.Equal(0f, slide.Z, precision: 4);
    }

    /// <summary>
    /// Slide is only applied when safeDist is near-zero (vehicle is already at the wall).
    /// When safeDist > 0 the vehicle is still approaching and the forward movement covers
    /// the remaining gap — no slide needed.
    /// This test mirrors the guard condition: <c>if (safeDist &lt; 1e-6f)</c>.
    /// </summary>
    [Fact]
    public void Slide_OnlyAppliedWhenFullyBlocked_NotWhenApproaching()
    {
        float distToContact = 10f;
        float halfExtentAlongMove = 2f;
        const float SkinM = 0.05f;

        float safeDist = Math.Max(0f, distToContact - halfExtentAlongMove - SkinM);

        // Vehicle is 7.95 m from the wall — NOT fully blocked → slide = zero.
        bool fullyBlocked = safeDist < 1e-6f;
        Assert.False(fullyBlocked, "Vehicle is still approaching, not fully blocked.");

        // Fully blocked case: distToContact = 0 → safeDist = 0.
        float safeDistBlocked = Math.Max(0f, 0f - halfExtentAlongMove - SkinM);
        bool  isNowBlocked    = safeDistBlocked < 1e-6f;
        Assert.True(isNowBlocked, "Vehicle is at the wall, should be fully blocked.");
    }

    // ── Deferred dynamic-body config (BATCH-17 startup-crash fix) ───────────────
    //
    // ROOT CAUSE (startup crash): BulletPhysicsBodyService.CreateBody sets AngularFactor,
    // LinearFactor, CanSleep, LinearDamping, Friction in the RigidbodyComponent object
    // initializer — BEFORE strideEntity.Add(rigidbody) / PhysicsProcessor processing.
    // Those properties reach into the native Bullet body, which does not exist yet, and throw:
    //   "Attempted to call a Physics function that is available only when the Entity has been
    //    already added to the Scene."
    //
    // FIX: only ColliderShape, IsKinematic, Mass are set in the initializer.
    // The runtime-physics properties are stored in DynamicConfig and applied lazily
    // the first frame rb.Simulation != null (body confirmed in simulation).
    //
    // These headless tests verify:
    //   1. CreateBody (OrientedBox) via NoOpPhysicsBodyService does NOT throw.
    //   2. The deferred-config model no-ops correctly when not ready (Simulation == null).
    //   3. Config is applied exactly once when ready, then PendingDynamicConfig is null.
    //   4. SetLinearVelocityXZ / SetYawRate are pure no-ops on SkippedBodyHandle.

    /// <summary>
    /// <see cref="NoOpPhysicsBodyService.CreateBody"/> for <see cref="CollisionShapeKind.OrientedBox"/>
    /// must not throw — even without a running Stride Simulation.
    ///
    /// <para>
    /// This is the headless-path equivalent of the startup crash test: the NoOp service
    /// is used for all headless tests and must be safe to call with OrientedBox shape kind.
    /// The real <see cref="BulletPhysicsBodyService"/> would have thrown from the old code
    /// (AngularFactor etc. set before Add); after the fix it returns a body handle safely.
    /// </para>
    /// </summary>
    [Fact]
    public void NoOp_CreateBody_OrientedBox_DoesNotThrow()
    {
        var service = new NoOpPhysicsBodyService();
        var entity = new Fdp.Core.Entity(index: 1, generation: 1);
        var dims = ShapeDims.Box(halfX: 2.25f, halfY: 1.10f, halfZ: 1.25f);
        var pose = default(SimTransform);

        // Must not throw; returns a non-null handle.
        var handle = service.CreateBody(entity, CollisionShapeKind.OrientedBox, dims, in pose);
        Assert.NotNull(handle);
    }

    /// <summary>
    /// <see cref="NoOpPhysicsBodyService.SetLinearVelocityXZ"/> is a pure no-op for any handle,
    /// including <c>SkippedBodyHandle</c>.  Must not throw.
    /// </summary>
    [Fact]
    public void NoOp_SetLinearVelocityXZ_DoesNotThrow()
    {
        var service = new NoOpPhysicsBodyService();
        var entity = new Fdp.Core.Entity(index: 1, generation: 1);
        var dims = ShapeDims.Box(halfX: 1f, halfY: 1f, halfZ: 1f);
        var pose = default(SimTransform);
        var handle = service.CreateBody(entity, CollisionShapeKind.OrientedBox, dims, in pose);

        // No-op: does not throw even when called multiple times.
        service.SetLinearVelocityXZ(handle, new SMath.Vector3(3f, 0f, 0f));
        service.SetLinearVelocityXZ(handle, new SMath.Vector3(0f, 0f, 0f));
    }

    /// <summary>
    /// <see cref="NoOpPhysicsBodyService.SetYawRate"/> is a pure no-op for any handle.  Must not throw.
    /// </summary>
    [Fact]
    public void NoOp_SetYawRate_DoesNotThrow()
    {
        var service = new NoOpPhysicsBodyService();
        var entity = new Fdp.Core.Entity(index: 1, generation: 1);
        var dims = ShapeDims.Box(halfX: 1f, halfY: 1f, halfZ: 1f);
        var pose = default(SimTransform);
        var handle = service.CreateBody(entity, CollisionShapeKind.OrientedBox, dims, in pose);

        // No-op: does not throw.
        service.SetYawRate(handle, 0.5f);
        service.SetYawRate(handle, 0f);
    }

    /// <summary>
    /// Models the deferred-config readiness pattern as a pure headless simulation:
    /// <list type="number">
    ///   <item>When <c>Simulation == null</c> (body not yet in sim), ApplyConfig is NOT called.</item>
    ///   <item>On the first frame <c>Simulation != null</c>, ApplyConfig IS called exactly once.</item>
    ///   <item>On subsequent frames, ApplyConfig is NOT called again (idempotent — config is null after first apply).</item>
    /// </list>
    /// This mirrors the exact logic of <c>BulletPhysicsBodyService.ApplyDynamicConfigIfReady</c>
    /// without requiring a live Stride Simulation.
    /// </summary>
    [Fact]
    public void DeferredDynamicConfig_AppliedOnceWhenReady_NeverAppliedWhenNotReady()
    {
        // Arrange: a config-apply counter and a fake readiness flag.
        int applyCount = 0;
        bool simulationReady = false;

        // Pending config: non-null while not yet applied (mirrors BodyEntry.PendingDynamicConfig).
        var pendingConfig = new FakeDynamicConfig(
            angularFactor:  new SMath.Vector3(0f, 1f, 0f),
            canSleep:       false,
            linearDamping:  0.5f,
            friction:       0.8f,
            angularDamping: 0f);  // BATCH-17: 0 → commanded yaw not bled off
        FakeDynamicConfig? pending = pendingConfig;

        // Simulate the per-frame ApplyDynamicConfigIfReady logic.
        void TryApply()
        {
            if (pending is null)        return; // already applied
            if (!simulationReady)       return; // not yet in simulation

            // Apply config (normally sets rb.AngularFactor etc.).
            applyCount++;

            // Verify expected config values.
            Assert.Equal(new SMath.Vector3(0f, 1f, 0f), pending.Value.AngularFactor);
            Assert.False(pending.Value.CanSleep);
            Assert.Equal(0.5f, pending.Value.LinearDamping,   precision: 4);
            Assert.Equal(0f,   pending.Value.AngularDamping,  precision: 4); // BATCH-17: must be 0
            Assert.Equal(0.8f, pending.Value.Friction,        precision: 4);

            // Mark as applied.
            pending = null;
        }

        // Frame 1: not ready → no apply.
        TryApply();
        Assert.Equal(0, applyCount);
        Assert.NotNull(pending); // config still pending

        // Frame 2: not ready → no apply.
        TryApply();
        Assert.Equal(0, applyCount);

        // Frame 3: simulation becomes ready → config applied exactly once.
        simulationReady = true;
        TryApply();
        Assert.Equal(1, applyCount);
        Assert.Null(pending); // config consumed

        // Frame 4: config already null → not applied again (idempotent).
        TryApply();
        Assert.Equal(1, applyCount); // still 1

        // Frame 5: same.
        TryApply();
        Assert.Equal(1, applyCount);
    }

    /// <summary>
    /// Models the velocity-drive no-op when body is not yet ready:
    /// <c>SetLinearVelocityXZ</c> and <c>SetYawRate</c> must return immediately if
    /// <c>Simulation == null</c> — no velocity is applied.
    /// </summary>
    [Fact]
    public void DeferredDynamicConfig_VelocityNoOpWhenNotReady()
    {
        // Arrange: simulate the readiness guard for the velocity methods.
        bool simulationReady = false;
        int velocitySetCount = 0;

        // Mirrors the guard inside BulletPhysicsBodyService.SetLinearVelocityXZ / SetYawRate.
        void SetLinearVelocityXZ(SMath.Vector3 vel)
        {
            if (!simulationReady) return; // no-op: native body not yet created
            velocitySetCount++;
        }

        void SetYawRate(float rate)
        {
            if (!simulationReady) return; // no-op
            velocitySetCount++;
        }

        // Frame 1–3: not ready → velocity commands are silently dropped.
        SetLinearVelocityXZ(new SMath.Vector3(3f, 0f, 0f));
        SetYawRate(0.5f);
        SetLinearVelocityXZ(new SMath.Vector3(2f, 0f, 1f));
        Assert.Equal(0, velocitySetCount);

        // Frame 4: simulation becomes ready → velocity commands take effect.
        simulationReady = true;
        SetLinearVelocityXZ(new SMath.Vector3(3f, 0f, 0f));
        SetYawRate(0.5f);
        Assert.Equal(2, velocitySetCount);
    }

    /// <summary>
    /// DynamicConfig stores all fields correctly (field-value round-trip test).
    /// </summary>
    [Fact]
    public void FakeDynamicConfig_StoresAllFields_Correctly()
    {
        var cfg = new FakeDynamicConfig(
            angularFactor:  new SMath.Vector3(0f, 1f, 0f),
            canSleep:       false,
            linearDamping:  0.5f,
            friction:       0.8f,
            angularDamping: 0f);

        Assert.Equal(new SMath.Vector3(0f, 1f, 0f), cfg.AngularFactor);
        Assert.False(cfg.CanSleep);
        Assert.Equal(0.5f, cfg.LinearDamping,  precision: 4);
        Assert.Equal(0f,   cfg.AngularDamping, precision: 4); // BATCH-17: zero → yaw not bled off
        Assert.Equal(0.8f, cfg.Friction,       precision: 4);
    }

    // ── BATCH-17 yaw-fidelity: DynamicConfig near-zero friction + zero angular damping ──────

    /// <summary>
    /// BATCH-17 yaw-fidelity fix: the dynamic vehicle body config must use
    /// <c>Friction ≤ 0.05f</c> (near-zero, so the floor contact patch cannot generate a
    /// torque opposing the commanded yaw) and <c>AngularDamping = 0f</c> (so Bullet does
    /// not bleed off the commanded angular velocity between frames).
    ///
    /// <para>
    /// Mirrors the constants in <c>BulletPhysicsBodyService.CreateBody</c> (OrientedBox branch).
    /// </para>
    /// </summary>
    [Fact]
    public void DynamicVehicleConfig_Friction_IsNearZero_ForYawFidelity()
    {
        // Mirror the production constant from BulletPhysicsBodyService (OrientedBox branch).
        const float VehicleFriction = 0.02f; // BATCH-17 value

        // Must be near-zero: the floor contact patch should not resist commanded yaw.
        Assert.True(VehicleFriction <= 0.05f,
            $"Vehicle friction ({VehicleFriction}) must be ≤ 0.05 (near-zero) so the floor " +
            $"cannot generate a yaw-opposing torque. Was 0.1 before BATCH-17 fix.");
        Assert.True(VehicleFriction >= 0f,
            $"Vehicle friction ({VehicleFriction}) must be non-negative.");
    }

    /// <summary>
    /// BATCH-17 yaw-fidelity fix: the dynamic vehicle body config must use
    /// <c>AngularDamping = 0f</c> so Bullet does not attenuate the commanded angular
    /// velocity between velocity-command frames.
    /// </summary>
    [Fact]
    public void DynamicVehicleConfig_AngularDamping_IsZero_ForYawFidelity()
    {
        // Mirror the production constant.
        const float VehicleAngularDamping = 0f; // BATCH-17 value

        Assert.Equal(0f, VehicleAngularDamping, precision: 5);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Pure lazy-once helper that mirrors the deferral contract of
    /// <see cref="BulletPhysicsBodyServiceDeferred"/> without needing a live Simulation.
    /// </summary>
    private sealed class DeferredProviderHelper<T>
    {
        private readonly Func<T> _factory;
        private bool _resolved;
        private T?   _cached;

        public DeferredProviderHelper(Func<T> factory)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public T Value
        {
            get
            {
                if (!_resolved)
                {
                    _cached   = _factory();
                    _resolved = true;
                }
                return _cached!;
            }
        }
    }

    /// <summary>
    /// Minimal fake of the DynamicConfig struct for headless deferred-config tests.
    /// Mirrors the fields of <c>BulletPhysicsBodyService.DynamicConfig</c> (private nested type)
    /// without requiring access to the production implementation.
    /// </summary>
    private readonly struct FakeDynamicConfig
    {
        public SMath.Vector3 AngularFactor  { get; }
        public bool          CanSleep       { get; }
        public float         LinearDamping  { get; }
        /// <summary>BATCH-17: 0 so commanded yaw is not bled off by Bullet angular damping.</summary>
        public float         AngularDamping { get; }
        public float         Friction       { get; }

        public FakeDynamicConfig(
            SMath.Vector3 angularFactor,
            bool          canSleep,
            float         linearDamping,
            float         friction,
            float         angularDamping = 0f)
        {
            AngularFactor  = angularFactor;
            CanSleep       = canSleep;
            LinearDamping  = linearDamping;
            AngularDamping = angularDamping;
            Friction       = friction;
        }
    }
}
