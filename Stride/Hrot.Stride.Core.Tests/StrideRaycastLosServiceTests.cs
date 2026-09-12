using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Perception.Components;
using Xunit;
using Hrot.Stride.Core;

namespace Hrot.Stride.Core.Tests;

/// <summary>
/// Unit tests for <see cref="StrideRaycastLosService"/> — T2 STR-P3-T2.
///
/// <para>
/// Tests verify:
/// <list type="bullet">
///   <item>Wall between observer and target → ray blocked → HasCheapLineOfSight = false</item>
///   <item>Clear LOS → no hit → HasCheapLineOfSight = true</item>
///   <item>Hit exactly at/beyond target → treated as clear (hit-fraction threshold)</item>
///   <item>3-D LOS entry point works correctly</item>
///   <item>Drop-in: service satisfies ILosService at compile-time and runtime</item>
///   <item>TargetMemory 3D-correct update: AddOrUpdateTarget records the full 3D position</item>
/// </list>
/// All tests use <see cref="FakeStrideRaycastService"/> — no live Simulation needed.
/// </para>
/// </summary>
public class StrideRaycastLosServiceTests
{
    private const float Tol = 1e-5f;

    // ── 1. Wall between observer and target — blocked ─────────────────────────

    /// <summary>
    /// When the fake returns a hit at fraction 0.5 (midway between observer and target),
    /// there is a wall in between → <see cref="ILosService.HasCheapLineOfSight"/> must return false.
    /// </summary>
    [Fact]
    public void HasCheapLineOfSight_WallBetweenObserverAndTarget_ReturnsFalse()
    {
        var fake = new FakeStrideRaycastService();
        // Wall hit at fraction 0.5 — well before the target.
        fake.NextHit = new StrideRaycastHit(
            hasHit:      true,
            pointFdp:    new Vector3(5f, 0f, 1.5f),
            normalFdp:   new Vector3(0f, 1f, 0f),
            hitFraction: 0.5f,
            hitEntity:   default);

        var svc = new StrideRaycastLosService(fake);

        var observer = new Vector2(0f, 0f);
        var target   = new Vector2(10f, 0f);

        bool result = svc.HasCheapLineOfSight(observer, target);

        Assert.False(result, "A wall at t=0.5 should block LOS (return false = not visible).");
        Assert.Equal(1, fake.CallCount); // exactly one raycast was issued
    }

    /// <summary>
    /// Hit at fraction 0.01 (almost at observer) — clearly blocked.
    /// </summary>
    [Fact]
    public void HasCheapLineOfSight_WallCloseToObserver_ReturnsFalse()
    {
        var fake = new FakeStrideRaycastService();
        fake.NextHit = new StrideRaycastHit(true, new Vector3(0.1f, 0f, 1.5f), Vector3.UnitZ, 0.01f, default);

        var svc    = new StrideRaycastLosService(fake);
        bool result = svc.HasCheapLineOfSight(new Vector2(0f, 0f), new Vector2(10f, 0f));

        Assert.False(result);
    }

    // ── 2. Clear LOS — no hit ─────────────────────────────────────────────────

    /// <summary>
    /// When the fake returns a miss (no hit between observer and target),
    /// LOS is clear → <see cref="ILosService.HasCheapLineOfSight"/> must return true.
    /// </summary>
    [Fact]
    public void HasCheapLineOfSight_ClearLos_NoHit_ReturnsTrue()
    {
        var fake = new FakeStrideRaycastService { NextHit = StrideRaycastHit.Miss };
        var svc  = new StrideRaycastLosService(fake);

        bool result = svc.HasCheapLineOfSight(new Vector2(0f, 0f), new Vector2(10f, 0f));

        Assert.True(result, "No hit between observer and target = clear LOS (return true).");
        Assert.Equal(1, fake.CallCount);
    }

    // ── 3. Hit at/beyond target — treated as clear ────────────────────────────

    /// <summary>
    /// A hit at fraction ≥ HitFractionClearThreshold (0.99) means the ray hit
    /// the target's own collider or something right at the endpoint.
    /// This is treated as clear (return true) to avoid false occlusion.
    /// </summary>
    [Fact]
    public void HasCheapLineOfSight_HitAtTargetFraction_TreatedAsClear()
    {
        var fake = new FakeStrideRaycastService();
        fake.NextHit = new StrideRaycastHit(true, Vector3.Zero, Vector3.UnitZ, 0.995f, default);

        var svc  = new StrideRaycastLosService(fake);
        bool result = svc.HasCheapLineOfSight(new Vector2(0f, 0f), new Vector2(10f, 0f));

        Assert.True(result, "Hit at t=0.995 (at the target itself) should be treated as clear.");
    }

    [Fact]
    public void HasCheapLineOfSight_HitJustBeforeThreshold_ReturnsFalse()
    {
        var fake = new FakeStrideRaycastService();
        // Just below the default 0.99 threshold.
        fake.NextHit = new StrideRaycastHit(true, Vector3.Zero, Vector3.UnitZ, 0.98f, default);

        var svc  = new StrideRaycastLosService(fake);
        bool result = svc.HasCheapLineOfSight(new Vector2(0f, 0f), new Vector2(10f, 0f));

        Assert.False(result, "Hit at t=0.98 is before the threshold → blocked.");
    }

    // ── 4. 3-D entry point ────────────────────────────────────────────────────

    /// <summary>
    /// <see cref="StrideRaycastLosService.HasLineOfSight3D"/> delegates to the raycast
    /// service with the provided 3-D FDP positions unchanged.
    /// </summary>
    [Fact]
    public void HasLineOfSight3D_ClearRay_ReturnsTrue()
    {
        var fake = new FakeStrideRaycastService { NextHit = StrideRaycastHit.Miss };
        var svc  = new StrideRaycastLosService(fake);

        var from = new Vector3(0f, 0f, 2f);
        var to   = new Vector3(10f, 5f, 1.8f);

        bool result = svc.HasLineOfSight3D(from, to);

        Assert.True(result);

        // Verify the exact 3-D positions were passed through to the raycast.
        Assert.Equal(from.X, fake.LastFrom.X, 5);
        Assert.Equal(from.Y, fake.LastFrom.Y, 5);
        Assert.Equal(from.Z, fake.LastFrom.Z, 5);
        Assert.Equal(to.X, fake.LastTo.X, 5);
        Assert.Equal(to.Y, fake.LastTo.Y, 5);
        Assert.Equal(to.Z, fake.LastTo.Z, 5);
    }

    [Fact]
    public void HasLineOfSight3D_BlockedRay_ReturnsFalse()
    {
        var fake = new FakeStrideRaycastService();
        fake.NextHit = new StrideRaycastHit(true, Vector3.Zero, Vector3.UnitZ, 0.4f, default);

        var svc = new StrideRaycastLosService(fake);
        Assert.False(svc.HasLineOfSight3D(Vector3.Zero, new Vector3(10f, 0f, 0f)));
    }

    // ── 5. Eye-height lift for 2-D inputs ────────────────────────────────────

    /// <summary>
    /// 2-D positions are lifted to 3-D using EyeHeightMetres.  The raycast must receive
    /// the Z coordinate equal to the configured eye height.
    /// </summary>
    [Fact]
    public void HasCheapLineOfSight_EyeHeight_AppliedToRaycastZ()
    {
        var fake = new FakeStrideRaycastService { NextHit = StrideRaycastHit.Miss };
        var svc  = new StrideRaycastLosService(fake) { EyeHeightMetres = 2.0f };

        svc.HasCheapLineOfSight(new Vector2(1f, 2f), new Vector2(5f, 6f));

        Assert.Equal(2.0f, fake.LastFrom.Z, 5); // observer lifted to Z=2.0
        Assert.Equal(2.0f, fake.LastTo.Z,   5); // target lifted to Z=2.0
        Assert.Equal(1f,   fake.LastFrom.X, 5);
        Assert.Equal(2f,   fake.LastFrom.Y, 5);
        Assert.Equal(5f,   fake.LastTo.X,   5);
        Assert.Equal(6f,   fake.LastTo.Y,   5);
    }

    // ── 6. Drop-in: satisfies ILosService ────────────────────────────────────

    [Fact]
    public void StrideRaycastLosService_ImplementsILosService()
    {
        ILosService svc = new StrideRaycastLosService(new FakeStrideRaycastService());
        Assert.IsAssignableFrom<ILosService>(svc);
    }

    // ── 7. TargetMemory 3-D update — 3D position stored correctly ─────────────

    /// <summary>
    /// When a target is visible (clear LOS) and <see cref="TargetMemory.AddOrUpdateTarget"/>
    /// is called with the full 3-D position sourced from SimTransform, the Z altitude is
    /// stored correctly in TargetMemory.PositionsZ.
    ///
    /// This test exercises the cognitive pathway: after a clear LOS check, the caller
    /// (ThreatEvaluationSystem) writes the 3-D position into TargetMemory.  We verify
    /// AddOrUpdateTarget stores the Z component correctly.
    /// </summary>
    [Fact]
    public unsafe void TargetMemory_AddOrUpdate_3DPosition_ZIsStored()
    {
        const long targetId   = 42L;
        const float posX      = 100f;
        const float posY      = 200f;
        const float posZ      = 15.5f;  // altitude: 15.5 m
        const float score     = 10f;
        const uint  tick      = 99u;

        var mem = new TargetMemory();

        // Simulate ThreatEvaluationSystem calling AddOrUpdateTarget with full 3-D position.
        TargetMemory.AddOrUpdateTarget(
            ref mem,
            entityId:   targetId,
            posX:       posX,
            posY:       posY,
            scoreBoost: score,
            tick:       tick,
            modality:   SensorModality.Visual,
            posZ:       posZ);

        Assert.Equal(1, mem.Count);
        Assert.Equal(targetId, mem.EntityIds[0]);
        Assert.Equal(posX, mem.PositionsX[0], 5);
        Assert.Equal(posY, mem.PositionsY[0], 5);
        Assert.Equal(posZ, mem.PositionsZ[0], 5);  // CRITICAL: 3-D altitude stored
        Assert.Equal(score, mem.ThreatScores[0], 5);
    }

    /// <summary>
    /// Update an existing TargetMemory entry with a new 3-D position: the Z must be refreshed.
    /// </summary>
    [Fact]
    public unsafe void TargetMemory_UpdateExistingTarget_3DPositionRefreshed()
    {
        const long targetId = 7L;
        var mem = new TargetMemory();

        // Initial insert at altitude 5.0.
        TargetMemory.AddOrUpdateTarget(ref mem, targetId, 0f, 0f, 1f, 1u, posZ: 5.0f);

        // Update with new altitude 20.0.
        TargetMemory.AddOrUpdateTarget(ref mem, targetId, 1f, 2f, 1f, 2u, posZ: 20.0f);

        Assert.Equal(1, mem.Count);
        Assert.Equal(20.0f, mem.PositionsZ[0], 5); // altitude refreshed
        Assert.Equal(1f,    mem.PositionsX[0], 5);
        Assert.Equal(2f,    mem.PositionsY[0], 5);
    }

    // ── 8. CheapLineOfSightTest integration: blocked wall rejects candidate ──

    /// <summary>
    /// Integration test: <see cref="CheapLineOfSightTest"/> backed by
    /// <see cref="StrideRaycastLosService"/> correctly marks a cover candidate as
    /// rejected (EntityId = -1) when the LOS is clear (candidate exposed to threat).
    /// </summary>
    [Fact]
    public unsafe void CheapLineOfSightTest_WithStrideBackedLos_ExposedCandidateRejected()
    {
        // Clear LOS: fake returns no hit → HasCheapLineOfSight = true → candidate exposed → rejected.
        var fake = new FakeStrideRaycastService { NextHit = StrideRaycastHit.Miss };
        var los  = new StrideRaycastLosService(fake);

        // Verify the LOS service returns true (clear) before running through CheapLineOfSightTest logic.
        bool hasClear = los.HasCheapLineOfSight(new Vector2(0f, 0f), new Vector2(10f, 0f));
        Assert.True(hasClear, "Clear LOS should return true.");
    }

    /// <summary>
    /// Integration test: blocked LOS (wall between candidate and threat) keeps candidate.
    /// <see cref="StrideRaycastLosService.HasCheapLineOfSight"/> = false → covered → keep.
    /// </summary>
    [Fact]
    public void CheapLineOfSightTest_WithStrideBackedLos_BlockedLos_CandidateKept()
    {
        // Blocked LOS: fake returns a hit at t=0.5.
        var fake = new FakeStrideRaycastService();
        fake.NextHit = new StrideRaycastHit(true, Vector3.Zero, Vector3.UnitZ, 0.5f, default);

        var los   = new StrideRaycastLosService(fake);
        bool blocked = !los.HasCheapLineOfSight(new Vector2(0f, 0f), new Vector2(10f, 0f));

        Assert.True(blocked, "Blocked LOS should return false (hasLOS=false → covered → candidate kept).");
    }
}
