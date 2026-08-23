using System;
using Xunit;
using Hrot.MuscleCharacter.Animation.Contracts;

namespace Hrot.Stride.Animation.Tests;

/// <summary>
/// Behavioral tests for the real <see cref="StrideAnimationBackend"/> (STR-P4-T1).
/// These exercise the <b>headless, testable</b> half of the seam — registration,
/// the speed→idle/walk/run blend weights, the montage slot state machine, notify
/// draining, and stance transitions — without a <c>GraphicsDevice</c> or any Stride
/// <c>AnimationComponent</c>. The GPU-bound pose application via
/// <see cref="PerEntityBlendTreeBuilder"/> is verified by the human run + BATCH-14.
/// </summary>
public class StrideAnimationBackendBehaviorTests
{
    private const int Slot0 = 0;

    // ── Registration lifecycle ──────────────────────────────────────────────

    [Fact]
    public void RegisterEntity_ReturnsValidResolvableHandle()
    {
        var backend = new StrideAnimationBackend();
        var handle = backend.RegisterEntity(entityId: 42, characterDefHandle: 2002);

        Assert.True(handle.IsValid);
        Assert.True(backend.TryResolve(handle, out var state));
        Assert.Equal((nint)42, state);
    }

    [Fact]
    public void UnregisterEntity_InvalidatesHandle()
    {
        var backend = new StrideAnimationBackend();
        var handle = backend.RegisterEntity(7, 2002);
        Assert.True(backend.TryResolve(handle, out _));

        backend.UnregisterEntity(handle);

        Assert.False(backend.TryResolve(handle, out _));
    }

    [Fact]
    public void StaleHandle_DoesNotResolveToReusedSlot()
    {
        var backend = new StrideAnimationBackend();
        var first = backend.RegisterEntity(7, 2002);
        backend.UnregisterEntity(first);

        // Reuse the freed slot for a different entity.
        var second = backend.RegisterEntity(99, 2003);

        // Same index, different generation — old handle must not resolve.
        Assert.Equal(first.Index, second.Index);
        Assert.NotEqual(first.Generation, second.Generation);
        Assert.False(backend.TryResolve(first, out _));
        Assert.True(backend.TryResolve(second, out var state));
        Assert.Equal((nint)99, state);
    }

    [Fact]
    public void RegisterEntity_PoolExhaustion_Throws()
    {
        var backend = new StrideAnimationBackend(maxEntities: 2);
        backend.RegisterEntity(1, 0);
        backend.RegisterEntity(2, 0);
        Assert.Throws<InvalidOperationException>(() => backend.RegisterEntity(3, 0));
    }

    // ── Locomotion blend weights by speed threshold (the core behavior) ─────

    [Theory]
    // speed,          idle,  walk,  run
    [InlineData(0.0f, 1.0f, 0.0f, 0.0f)]   // stationary → pure idle
    [InlineData(0.1f, 1.0f, 0.0f, 0.0f)]   // at idle threshold → still pure idle
    [InlineData(1.5f, 0.0f, 1.0f, 0.0f)]   // at walk speed → pure walk
    [InlineData(4.0f, 0.0f, 0.0f, 1.0f)]   // at run speed → pure run
    [InlineData(6.0f, 0.0f, 0.0f, 1.0f)]   // beyond run speed → clamped pure run
    public void LocomotionBlend_EndpointSpeeds_ProduceExactWeights(
        float speed, float expectedIdle, float expectedWalk, float expectedRun)
    {
        var w = LocomotionBlend.FromSpeed(speed);
        Assert.Equal(expectedIdle, w.Idle, 4);
        Assert.Equal(expectedWalk, w.Walk, 4);
        Assert.Equal(expectedRun, w.Run, 4);
        // Weights always normalize to 1.
        Assert.Equal(1.0f, w.Idle + w.Walk + w.Run, 4);
    }

    [Fact]
    public void LocomotionBlend_IdleToWalk_UsesSqrtSkewTowardWalk()
    {
        // Midpoint of the idle→walk leg: t = 0.5, factor = sqrt(0.5) ≈ 0.7071.
        float mid = (LocomotionBlend.IdleSpeed + LocomotionBlend.WalkSpeed) / 2f;
        var w = LocomotionBlend.FromSpeed(mid);

        float expectedFactor = MathF.Sqrt(0.5f);
        Assert.Equal(expectedFactor, w.Factor, 4);
        Assert.Equal(LocomotionClip.Idle, w.LowerClip);
        Assert.Equal(LocomotionClip.Walk, w.UpperClip);
        Assert.Equal(expectedFactor, w.Walk, 4);  // skewed toward walk (>0.5)
        Assert.True(w.Walk > 0.5f, "sqrt skew must bias the half-speed blend toward Walk");
        Assert.Equal(1f - expectedFactor, w.Idle, 4);
        Assert.Equal(0f, w.Run, 4);
    }

    [Fact]
    public void LocomotionBlend_WalkToRun_IsLinear()
    {
        // Midpoint of the walk→run leg: u = 0.5 (linear, no skew).
        float mid = (LocomotionBlend.WalkSpeed + LocomotionBlend.RunSpeed) / 2f;
        var w = LocomotionBlend.FromSpeed(mid);

        Assert.Equal(0.5f, w.Factor, 4);
        Assert.Equal(LocomotionClip.Walk, w.LowerClip);
        Assert.Equal(LocomotionClip.Run, w.UpperClip);
        Assert.Equal(0.5f, w.Walk, 4);
        Assert.Equal(0.5f, w.Run, 4);
        Assert.Equal(0f, w.Idle, 4);
    }

    [Fact]
    public void UpdateLocomotionInputs_DerivesBlendFromPlanarVelocity()
    {
        var backend = new StrideAnimationBackend();
        var handle = backend.RegisterEntity(1, 2002);

        // 3-4-5 triangle: vel (2.4, 3.2) → speed 4.0 = run speed → pure run.
        backend.UpdateLocomotionInputs(handle, horizontalVelX: 2.4f, horizontalVelZ: 3.2f, verticalVelocity: 0f, isGrounded: true);

        var w = backend.QueryLocomotion(handle);
        Assert.Equal(0f, w.Idle, 4);
        Assert.Equal(0f, w.Walk, 4);
        Assert.Equal(1f, w.Run, 4);
    }

    [Fact]
    public void UpdateLocomotionInputs_StationaryStaysIdle()
    {
        var backend = new StrideAnimationBackend();
        var handle = backend.RegisterEntity(1, 2002);

        backend.UpdateLocomotionInputs(handle, 0f, 0f, 0f, true);

        var w = backend.QueryLocomotion(handle);
        Assert.Equal(1f, w.Idle, 4);
    }

    // ── Montage slot state machine ──────────────────────────────────────────

    [Fact]
    public void PlayMontageOnSlot_ActivatesSlotZero()
    {
        var backend = new StrideAnimationBackend();
        var handle = backend.RegisterEntity(1, 2002);

        Assert.False(backend.IsAnySlotActive(handle));

        backend.PlayMontageOnSlot(handle, new PlayMontageParams
        {
            MontageId = 1234, PlayRate = 1f, BlendInTime = 0.1f, BlendOutTime = 0.1f,
        });

        Assert.True(backend.IsAnySlotActive(handle));
        var slot = backend.QuerySlotState(handle, Slot0);
        Assert.True(slot.IsActive);
        Assert.Equal(1234, slot.MontageHash);
    }

    [Fact]
    public void Montage_BlendIn_RampsWeightFromZeroToOne()
    {
        var backend = new StrideAnimationBackend();
        var handle = backend.RegisterEntity(1, 2002);

        // Register a 2s clip via a marker so the duration is generous, blend-in 0.5s.
        backend.RegisterMontageMarkers(55, (1.9f, AnimNotifyCategory.Generic, 0u, 0f, 0u));
        backend.PlayMontageOnSlot(handle, new PlayMontageParams
        {
            MontageId = 55, PlayRate = 1f, BlendInTime = 0.5f, BlendOutTime = 0.1f,
        });

        backend.Tick(0.25f); // halfway through blend-in
        var slot = backend.QuerySlotState(handle, Slot0);
        Assert.Equal(0.5f, slot.BlendWeight, 3);

        backend.Tick(0.30f); // now past blend-in → full weight
        slot = backend.QuerySlotState(handle, Slot0);
        Assert.Equal(1f, slot.BlendWeight, 3);
    }

    [Fact]
    public void StopMontageOnSlot_ForcesBlendOutWindow()
    {
        var backend = new StrideAnimationBackend();
        var handle = backend.RegisterEntity(1, 2002);

        backend.RegisterMontageMarkers(55, (1.9f, AnimNotifyCategory.Generic, 0u, 0f, 0u)); // 2s clip
        backend.PlayMontageOnSlot(handle, new PlayMontageParams
        {
            MontageId = 55, PlayRate = 1f, BlendInTime = 0.1f, BlendOutTime = 0.1f,
        });
        backend.Tick(0.5f);
        Assert.False(backend.IsAnySlotInBlendOut(handle));

        backend.StopMontageOnSlot(handle, new StopMontageParams { BlendOutTime = 0.2f });

        Assert.True(backend.IsAnySlotInBlendOut(handle));
        Assert.True(backend.IsAnySlotActive(handle));
    }

    [Fact]
    public void Montage_NaturalCompletion_DeactivatesSlot()
    {
        var backend = new StrideAnimationBackend();
        var handle = backend.RegisterEntity(1, 2002);

        // No markers → default 1.0s duration.
        backend.PlayMontageOnSlot(handle, new PlayMontageParams
        {
            MontageId = 7, PlayRate = 1f, BlendInTime = 0.1f, BlendOutTime = 0.1f,
        });
        Assert.True(backend.IsAnySlotActive(handle));

        backend.Tick(1.1f); // past the 1.0s duration

        Assert.False(backend.IsAnySlotActive(handle));
    }

    [Fact]
    public void PlayRate_ScalesPlaybackSpeed()
    {
        var backend = new StrideAnimationBackend();
        var handle = backend.RegisterEntity(1, 2002);

        backend.PlayMontageOnSlot(handle, new PlayMontageParams
        {
            MontageId = 7, PlayRate = 2f, BlendInTime = 0f, BlendOutTime = 0f,
        });

        backend.Tick(0.6f); // 0.6 * 2 = 1.2s elapsed > 1.0s duration → complete
        Assert.False(backend.IsAnySlotActive(handle));
    }

    // ── Notify draining ─────────────────────────────────────────────────────

    [Fact]
    public void Montage_NotifyMarker_FiresOnceWhenPlayheadCrosses()
    {
        var backend = new StrideAnimationBackend();
        var handle = backend.RegisterEntity(1, 2002);

        backend.RegisterMontageMarkers(99,
            (0.5f, AnimNotifyCategory.HitWindowOpened, 0xABCDu, 1.5f, 3u));
        backend.PlayMontageOnSlot(handle, new PlayMontageParams
        {
            MontageId = 99, PlayRate = 1f, BlendInTime = 0f, BlendOutTime = 0f,
        });

        backend.Tick(0.3f); // before the marker
        Span<RawNotifyEvent> buf = stackalloc RawNotifyEvent[8];
        Assert.Equal(0, backend.DrainNotifies(handle, buf));

        backend.Tick(0.3f); // now elapsed 0.6 > 0.5 → fires once
        int n = backend.DrainNotifies(handle, buf);
        Assert.Equal(1, n);
        Assert.Equal(AnimNotifyCategory.HitWindowOpened, buf[0].Kind);
        Assert.Equal(0xABCDu, buf[0].MarkerHash);
        Assert.Equal(1.5f, buf[0].PayloadFloat, 3);
        Assert.Equal(3u, buf[0].PayloadUint);

        backend.Tick(0.1f); // does not re-fire
        Assert.Equal(0, backend.DrainNotifies(handle, buf));
    }

    [Fact]
    public void Footsteps_EmitWhileMovingGrounded()
    {
        var backend = new StrideAnimationBackend();
        var handle = backend.RegisterEntity(1, 2002);

        // 2 m/s, one stride (0.9m) crosses at dt = 0.46s.
        backend.UpdateLocomotionInputs(handle, 2f, 0f, 0f, isGrounded: true);
        backend.Tick(0.46f);

        Span<RawNotifyEvent> buf = stackalloc RawNotifyEvent[8];
        int n = backend.DrainNotifies(handle, buf);
        Assert.True(n >= 1);
        Assert.Equal(AnimNotifyCategory.Footstep, buf[0].Kind);
    }

    [Fact]
    public void Footsteps_DoNotEmitWhenAirborne()
    {
        var backend = new StrideAnimationBackend();
        var handle = backend.RegisterEntity(1, 2002);

        backend.UpdateLocomotionInputs(handle, 2f, 0f, 0f, isGrounded: false);
        backend.Tick(1.0f);

        Span<RawNotifyEvent> buf = stackalloc RawNotifyEvent[8];
        Assert.Equal(0, backend.DrainNotifies(handle, buf));
    }

    // ── Stance transitions ──────────────────────────────────────────────────

    [Fact]
    public void RequestStanceChange_CompletesAfterBlendDuration()
    {
        var backend = new StrideAnimationBackend();
        var handle = backend.RegisterEntity(1, 2002);

        Assert.True(backend.GetCurrentStance(handle, out byte before));
        Assert.Equal(0, before); // Standing

        backend.RequestStanceChange(handle, targetStance: 1, blendDurationSeconds: 0.4f); // → Crouched

        backend.Tick(0.2f); // mid-transition: stance not yet committed
        backend.GetCurrentStance(handle, out byte mid);
        Assert.Equal(0, mid);

        backend.Tick(0.3f); // total 0.5 > 0.4 → committed
        backend.GetCurrentStance(handle, out byte after);
        Assert.Equal(1, after);
    }

    // ── Metrics + contract completeness ─────────────────────────────────────

    [Fact]
    public void SnapshotMetrics_ReflectsActiveEntitiesAndSlots()
    {
        var backend = new StrideAnimationBackend();
        var h1 = backend.RegisterEntity(1, 2002);
        var h2 = backend.RegisterEntity(2, 2003);
        backend.PlayMontageOnSlot(h1, new PlayMontageParams { MontageId = 7, PlayRate = 1f });

        var m = backend.SnapshotMetrics();
        Assert.Equal(2, m.ActiveEntityCount);
        Assert.Equal(1, m.TotalActiveSlotsCount);

        backend.UnregisterEntity(h2);
        Assert.Equal(1, backend.SnapshotMetrics().ActiveEntityCount);
    }

    [Fact]
    public void NoInterfaceMethod_ThrowsNotImplemented()
    {
        // Exercise every IAnimationBackend member on a fresh backend; none may throw
        // NotImplementedException (the P0 stub is gone).
        IAnimationBackend backend = new StrideAnimationBackend();
        var handle = backend.RegisterEntity(1, 2002);

        var ex = Record.Exception(() =>
        {
            backend.PlayMontageOnSlot(handle, default);
            backend.CrossfadeMontageOnSlot(handle, default);
            backend.StopMontageOnSlot(handle, default);
            backend.SetAimTargetPoint(handle, default);
            backend.SetAimTargetEntity(handle, default);
            backend.ReleaseAim(handle, default);
            backend.RequestStanceChange(handle, 1, 0.3f);
            backend.Tick(0.016f);
            Span<RawNotifyEvent> buf = stackalloc RawNotifyEvent[4];
            backend.DrainNotifies(buf);
            backend.DrainNotifies(handle, buf);
            backend.GetCurrentStance(handle, out _);
            backend.SnapshotMetrics();
            backend.IsAnySlotActive(handle);
            backend.IsAnySlotInBlendOut(handle);
            backend.TryResolve(handle, out _);
            backend.UnregisterEntity(handle);
        });

        Assert.Null(ex);
    }
}
