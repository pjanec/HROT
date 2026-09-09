using System;
using Xunit;
using Hrot.MuscleCharacter.Animation.Contracts;

namespace Hrot.Stride.Animation.Tests;

/// <summary>
/// BATCH-16 Fix A: tests for the backend→builder hook — the per-entity accessors
/// (<see cref="StrideAnimationBackend.TryGetLocomotionBlend"/> /
/// <see cref="StrideAnimationBackend.TryGetMontageOverlay"/>) that the live-glue binder pumps
/// into a <see cref="PerEntityBlendTreeBuilder"/>. These assert the accessors return exactly the
/// state the backend computed (the same values <c>Tick</c> pushes to an attached builder), so the
/// GPU skeleton is driven by the proven headless blend logic.
/// </summary>
public class BackendBuilderHookTests
{
    private static StrideAnimationBackend NewBackend()
    {
        var b = new StrideAnimationBackend();
        b.Initialize(new AnimationBackendConfig { MaxEntities = 16, DefaultPlayRate = 1f });
        return b;
    }

    [Fact]
    public void TryGetLocomotionBlend_ReturnsSameWeightsAsLocomotionBlend_ForWalkSpeed()
    {
        var backend = NewBackend();
        var h = backend.RegisterEntity(1, 2002);

        // Drive a clean walk speed (1.5 m/s on one planar axis → pure Walk per LocomotionBlend).
        backend.UpdateLocomotionInputs(h, LocomotionBlend.WalkSpeed, 0f, 0f, isGrounded: true);
        backend.Tick(1f / 60f);

        Assert.True(backend.TryGetLocomotionBlend(h, out var weights, out var normTime));

        // The accessor must equal what the pure mapping computed for that speed.
        var expected = LocomotionBlend.FromSpeed(LocomotionBlend.WalkSpeed);
        Assert.Equal(expected, weights);
        Assert.Equal(1f, weights.Walk, 3);
        Assert.Equal(0f, weights.Idle, 3);
        Assert.Equal(0f, weights.Run, 3);

        // Phase advances into 0..1 after a tick at walk speed.
        Assert.InRange(normTime, 0.0, 1.0);
    }

    [Fact]
    public void TryGetLocomotionBlend_ReturnsRunBlend_ForRunSpeed()
    {
        var backend = NewBackend();
        var h = backend.RegisterEntity(2, 2002);

        backend.UpdateLocomotionInputs(h, RunSpeedAxis(), 0f, 0f, isGrounded: true);
        backend.Tick(1f / 60f);

        Assert.True(backend.TryGetLocomotionBlend(h, out var weights, out _));
        Assert.Equal(LocomotionBlend.FromSpeed(LocomotionBlend.RunSpeed), weights);
        Assert.Equal(1f, weights.Run, 3);
        Assert.Equal(LocomotionClip.Run, weights.UpperClip);
    }

    [Fact]
    public void TryGetLocomotionBlend_ReturnsIdle_WhenAtRest()
    {
        var backend = NewBackend();
        var h = backend.RegisterEntity(3, 2002);

        backend.UpdateLocomotionInputs(h, 0f, 0f, 0f, isGrounded: true);
        backend.Tick(1f / 60f);

        Assert.True(backend.TryGetLocomotionBlend(h, out var weights, out _));
        Assert.Equal(1f, weights.Idle, 3);
        Assert.Equal(0f, weights.Walk, 3);
    }

    [Fact]
    public void TryGetLocomotionBlend_FalseForStaleHandle()
    {
        var backend = NewBackend();
        var h = backend.RegisterEntity(4, 2002);
        backend.UnregisterEntity(h);

        Assert.False(backend.TryGetLocomotionBlend(h, out var weights, out var t));
        Assert.Equal(default, weights);
        Assert.Equal(0.0, t);
    }

    [Fact]
    public void TryGetMontageOverlay_ZeroWeight_WhenNoMontageActive()
    {
        var backend = NewBackend();
        var h = backend.RegisterEntity(5, 2002);
        backend.Tick(1f / 60f);

        Assert.True(backend.TryGetMontageOverlay(h, out _, out var weight, out _));
        Assert.Equal(0f, weight);
    }

    [Fact]
    public void TryGetMontageOverlay_ReportsActiveSlotZeroMontage_AfterPlay()
    {
        var backend = NewBackend();
        var h = backend.RegisterEntity(6, 2002);

        int montageHash = 1234;
        backend.PlayMontageOnSlot(h, new PlayMontageParams
        {
            MontageId = montageHash,
            PlayRate = 1f,
            BlendInTime = 0.1f,
            BlendOutTime = 0.1f,
            StartSectionIndex = 0,
        });
        // Tick past the blend-in so the overlay weight ramps up but the montage is still active.
        backend.Tick(0.12f);

        Assert.True(backend.TryGetMontageOverlay(h, out var hash, out var weight, out var normTime));
        Assert.Equal(montageHash, hash);
        Assert.True(weight > 0f, $"overlay weight should be > 0 while montage active (got {weight}).");
        Assert.InRange(normTime, 0.0, 1.0);

        // Cross-check: equals what QuerySlotState reports (single source of truth).
        var slot0 = backend.QuerySlotState(h, 0);
        Assert.True(slot0.IsActive);
        Assert.Equal(slot0.BlendWeight, weight, 4);
    }

    [Fact]
    public void TryGetMontageOverlay_FalseForStaleHandle()
    {
        var backend = NewBackend();
        var h = backend.RegisterEntity(7, 2002);
        backend.UnregisterEntity(h);

        Assert.False(backend.TryGetMontageOverlay(h, out var hash, out var weight, out var t));
        Assert.Equal(0, hash);
        Assert.Equal(0f, weight);
        Assert.Equal(0.0, t);
    }

    // RunSpeed on a single axis; the planar magnitude is the input the backend blends on.
    private static float RunSpeedAxis() => LocomotionBlend.RunSpeed;
}
