using Hrot.MuscleCharacter.Animation.Contracts;
using Hrot.MuscleCharacter.Animation.Stride;
using Xunit;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.MuscleCharacter.Animation.Stride.Tests;

/// <summary>
/// ANC-P8-03: StrideBackendSmokeTest suite.
/// Validates boot, tick, handle lifecycle, notify path, and transform mapping.
/// Scope is smoke/stability; not a re-run of the eight AI-behavior scenarios.
/// </summary>
public class StrideBackendSmokeTests
{
    // -----------------------------------------------------------------------
    // ANC-P8-01: backend construction + initialization
    // -----------------------------------------------------------------------

    [Fact]
    public void Backend_Construction_Succeeds()
    {
        var backend = new StrideAnimationBackend();
        Assert.NotNull(backend);
    }

    [Fact]
    public void Backend_Initialize_Succeeds()
    {
        var backend = new StrideAnimationBackend();
        var config = new AnimationBackendConfig
        {
            MaxEntities = 64,
            MaxNotifyEvents = 128,
            DefaultBlendInTime = 0.25f,
            DefaultBlendOutTime = 0.15f,
            DefaultPlayRate = 1f,
        };
        backend.Initialize(in config);
        // No crash; backend remains operational.
        Assert.True(backend.SnapshotMetrics().ActiveEntityCount == 0);
    }

    // -----------------------------------------------------------------------
    // ANC-P8-01: per-entity registration lifecycle
    // -----------------------------------------------------------------------

    [Fact]
    public void RegisterEntity_ReturnsValidHandle()
    {
        var backend = new StrideAnimationBackend();
        var handle = backend.RegisterEntity(1u, 0L);
        Assert.NotEqual(0xFFFFFFFFu, handle.Index);
        Assert.NotEqual(0u, handle.Generation);
        Assert.True(handle.IsValid);
    }

    [Fact]
    public void TryResolve_WithValidHandle_ReturnsTrue()
    {
        var backend = new StrideAnimationBackend();
        var handle = backend.RegisterEntity(1u, 0L);
        Assert.True(backend.TryResolve(handle, out _));
    }

    [Fact]
    public void TryResolve_WithStaleHandle_ReturnsFalse()
    {
        var backend = new StrideAnimationBackend();
        var handle = backend.RegisterEntity(1u, 0L);
        backend.UnregisterEntity(handle);
        Assert.False(backend.TryResolve(handle, out _));
    }

    [Fact]
    public void UnregisterEntity_FollowedByReregister_BumpsGeneration()
    {
        var backend = new StrideAnimationBackend();
        var h1 = backend.RegisterEntity(1u, 0L);
        uint gen1 = h1.Generation;
        backend.UnregisterEntity(h1);

        var h2 = backend.RegisterEntity(2u, 0L);
        Assert.NotEqual(gen1, h2.Generation);
        Assert.False(backend.TryResolve(h1, out _), "stale handle must not resolve after re-use");
        Assert.True(backend.TryResolve(h2, out _));
    }

    [Fact]
    public void UnregisterEntity_WithStaleHandle_IsNoop()
    {
        var backend = new StrideAnimationBackend();
        var handle = backend.RegisterEntity(1u, 0L);
        backend.UnregisterEntity(handle);
        // Second unregister with the same (now stale) handle must not crash.
        backend.UnregisterEntity(handle);
    }

    [Fact]
    public void MultipleEntities_AllResolveCorrectly()
    {
        var backend = new StrideAnimationBackend();
        var h1 = backend.RegisterEntity(10u, 0L);
        var h2 = backend.RegisterEntity(20u, 0L);
        var h3 = backend.RegisterEntity(30u, 0L);

        Assert.True(backend.TryResolve(h1, out nint s1));
        Assert.True(backend.TryResolve(h2, out nint s2));
        Assert.True(backend.TryResolve(h3, out nint s3));
        Assert.NotEqual(s1, s2);
        Assert.NotEqual(s2, s3);
    }

    [Fact]
    public void SnapshotMetrics_ReflectsActiveEntityCount()
    {
        var backend = new StrideAnimationBackend();
        backend.RegisterEntity(1u, 0L);
        backend.RegisterEntity(2u, 0L);
        var metrics = backend.SnapshotMetrics();
        Assert.Equal(2, metrics.ActiveEntityCount);
    }

    // -----------------------------------------------------------------------
    // ANC-P8-01: slot play/tick/query progression
    // -----------------------------------------------------------------------

    [Fact]
    public void PlayMontageOnSlot_Succeeds()
    {
        var backend = new StrideAnimationBackend();
        var handle = backend.RegisterEntity(1u, 0L);
        var @params = new PlayMontageParams { MontageId = 42, PlayRate = 1.0f };
        backend.PlayMontageOnSlot(handle, in @params);
    }

    [Fact]
    public void PlayMontageOnSlot_MakesSlotActive()
    {
        var backend = new StrideAnimationBackend();
        var handle = backend.RegisterEntity(1u, 0L);
        Assert.False(backend.IsAnySlotActive(handle));

        var @params = new PlayMontageParams { MontageId = 7, PlayRate = 1.0f };
        backend.PlayMontageOnSlot(handle, in @params);

        Assert.True(backend.IsAnySlotActive(handle));
    }

    [Fact]
    public void Tick_DoesNotCrash_WithActiveSlot()
    {
        var backend = new StrideAnimationBackend();
        var handle = backend.RegisterEntity(1u, 0L);
        var @params = new PlayMontageParams { MontageId = 1, PlayRate = 1.0f };
        backend.PlayMontageOnSlot(handle, in @params);

        for (int i = 0; i < 30; i++)
            backend.Tick(0.016f);
    }

    [Fact]
    public void Tick_WithMultipleEntities_DoesNotCrash()
    {
        var backend = new StrideAnimationBackend();
        var h1 = backend.RegisterEntity(1u, 0L);
        var h2 = backend.RegisterEntity(2u, 0L);
        var @params = new PlayMontageParams { MontageId = 5, PlayRate = 1.0f };
        backend.PlayMontageOnSlot(h1, in @params);
        backend.PlayMontageOnSlot(h2, in @params);

        for (int i = 0; i < 60; i++)
            backend.Tick(0.016f);
    }

    [Fact]
    public void Slot_BecomesInactive_AfterNaturalDuration()
    {
        var backend = new StrideAnimationBackend();
        var handle = backend.RegisterEntity(1u, 0L);
        var @params = new PlayMontageParams { MontageId = 9, PlayRate = 1.0f };
        backend.PlayMontageOnSlot(handle, in @params);

        Assert.True(backend.IsAnySlotActive(handle));

        // Default montage duration is 1.0 second; tick for 1.1 seconds.
        for (int i = 0; i < 70; i++)
            backend.Tick(1.0f / 60f); // ~1.17 s total

        Assert.False(backend.IsAnySlotActive(handle));
    }

    [Fact]
    public void StopMontageOnSlot_ClearsActiveSlot()
    {
        var backend = new StrideAnimationBackend();
        var handle = backend.RegisterEntity(1u, 0L);
        var @params = new PlayMontageParams { MontageId = 3, PlayRate = 1.0f };
        backend.PlayMontageOnSlot(handle, in @params);
        Assert.True(backend.IsAnySlotActive(handle));

        var stop = new StopMontageParams { BlendOutTime = 0.0f };
        backend.StopMontageOnSlot(handle, in stop);
        Assert.False(backend.IsAnySlotActive(handle));
    }

    [Fact]
    public void PlayMontageOnSlot_WithStaleHandle_IsNoop()
    {
        var backend = new StrideAnimationBackend();
        var handle = backend.RegisterEntity(1u, 0L);
        backend.UnregisterEntity(handle);

        var @params = new PlayMontageParams { MontageId = 1, PlayRate = 1.0f };
        backend.PlayMontageOnSlot(handle, in @params);
        // Must not crash.
    }

    // -----------------------------------------------------------------------
    // ANC-P8-02: marker/notify smoke (DD-1 §15.4)
    // -----------------------------------------------------------------------

    [Fact]
    public void MarkerNotify_IsNotFired_BeforeMarkerTime()
    {
        var backend = new StrideAnimationBackend();
        var montageId = new MontageAssetId { Hash = 200 };
        backend.RegisterMontageMarkers(montageId, new[]
        {
            new MontageMarker
            {
                TimeSeconds = 0.5f,
                Kind = AnimNotifyCategory.Generic,
                MarkerHash = 0xABCD,
            },
        });

        var handle = backend.RegisterEntity(1u, 0L);
        backend.PlayMontageOnSlot(handle, new PlayMontageParams
        {
            MontageId = 200,
            PlayRate = 1.0f,
        });

        // Tick for 0.4 s -- marker at 0.5 s not yet crossed.
        backend.Tick(0.2f);
        backend.Tick(0.2f);

        var buf = new RawNotifyEvent[8];
        int count = backend.DrainNotifies(handle, buf.AsSpan());
        Assert.Equal(0, count);
    }

    [Fact]
    public void MarkerNotify_IsFired_AfterMarkerTime()
    {
        var backend = new StrideAnimationBackend();
        var montageId = new MontageAssetId { Hash = 100 };
        backend.RegisterMontageMarkers(montageId, new[]
        {
            new MontageMarker
            {
                TimeSeconds = 0.1f,
                Kind = AnimNotifyCategory.Generic,
                MarkerHash = 0xDEAD,
                PayloadFloat = 3.14f,
                PayloadUint = 7u,
            },
        });

        var handle = backend.RegisterEntity(1u, 0L);
        backend.PlayMontageOnSlot(handle, new PlayMontageParams
        {
            MontageId = 100,
            PlayRate = 1.0f,
        });

        // Tick 0.05 s: not yet crossed.
        backend.Tick(0.05f);
        var buf1 = new RawNotifyEvent[8];
        Assert.Equal(0, backend.DrainNotifies(handle, buf1.AsSpan()));

        // Tick another 0.1 s: crosses 0.1 s marker.
        backend.Tick(0.1f);
        var buf2 = new RawNotifyEvent[8];
        int count = backend.DrainNotifies(handle, buf2.AsSpan());

        Assert.Equal(1, count);
        Assert.Equal(AnimNotifyCategory.Generic, buf2[0].Kind);
        Assert.Equal(0xDEADu, buf2[0].MarkerHash);
        Assert.Equal(0.1f, buf2[0].TimeSeconds);
        Assert.Equal(3.14f, buf2[0].PayloadFloat);
        Assert.Equal(7u, buf2[0].PayloadUint);
    }

    [Fact]
    public void MarkerNotify_FiredOnce_NotDuplicated()
    {
        var backend = new StrideAnimationBackend();
        var montageId = new MontageAssetId { Hash = 300 };
        backend.RegisterMontageMarkers(montageId, new[]
        {
            new MontageMarker
            {
                TimeSeconds = 0.05f,
                Kind = AnimNotifyCategory.HitWindowOpened,
                MarkerHash = 0x1234,
            },
        });

        var handle = backend.RegisterEntity(1u, 0L);
        backend.PlayMontageOnSlot(handle, new PlayMontageParams
        {
            MontageId = 300,
            PlayRate = 1.0f,
        });

        // First tick crosses the marker.
        backend.Tick(0.1f);
        var buf1 = new RawNotifyEvent[8];
        int first = backend.DrainNotifies(handle, buf1.AsSpan());
        Assert.Equal(1, first);

        // Further ticks must NOT re-fire.
        backend.Tick(0.1f);
        backend.Tick(0.1f);
        var buf2 = new RawNotifyEvent[8];
        int second = backend.DrainNotifies(handle, buf2.AsSpan());
        Assert.Equal(0, second);
    }

    [Fact]
    public void GlobalDrainNotifies_AggregatesAcrossEntities()
    {
        var backend = new StrideAnimationBackend();
        var montageId = new MontageAssetId { Hash = 400 };
        backend.RegisterMontageMarkers(montageId, new[]
        {
            new MontageMarker
            {
                TimeSeconds = 0.05f,
                Kind = AnimNotifyCategory.Generic,
                MarkerHash = 0x1111,
            },
        });

        var h1 = backend.RegisterEntity(1u, 0L);
        var h2 = backend.RegisterEntity(2u, 0L);

        backend.PlayMontageOnSlot(h1, new PlayMontageParams { MontageId = 400, PlayRate = 1.0f });
        backend.PlayMontageOnSlot(h2, new PlayMontageParams { MontageId = 400, PlayRate = 1.0f });

        backend.Tick(0.1f); // crosses marker for both entities

        var buf = new RawNotifyEvent[16];
        int count = backend.DrainNotifies(buf.AsSpan());
        Assert.Equal(2, count);
    }

    [Fact]
    public void DrainNotifies_AfterDrain_IsEmpty()
    {
        var backend = new StrideAnimationBackend();
        var montageId = new MontageAssetId { Hash = 500 };
        backend.RegisterMontageMarkers(montageId, new[]
        {
            new MontageMarker { TimeSeconds = 0.05f, Kind = AnimNotifyCategory.Generic, MarkerHash = 0x55 },
        });

        var handle = backend.RegisterEntity(1u, 0L);
        backend.PlayMontageOnSlot(handle, new PlayMontageParams { MontageId = 500, PlayRate = 1.0f });
        backend.Tick(0.1f);

        var buf = new RawNotifyEvent[8];
        backend.DrainNotifies(buf.AsSpan()); // first drain

        int second = backend.DrainNotifies(buf.AsSpan()); // should be empty
        Assert.Equal(0, second);
    }

    // -----------------------------------------------------------------------
    // ANC-P8-02: transform / target resolution smoke
    // -----------------------------------------------------------------------

    [Fact]
    public void SetEntityTransform_DoesNotCrash()
    {
        var backend = new StrideAnimationBackend();
        var handle = backend.RegisterEntity(1u, 0L);
        // Apply a world-space transform; backend must process without crash.
        backend.SetEntityTransform(handle, 10f, 0f, 20f, 1.57f);
        backend.Tick(0.016f);
        Assert.True(backend.TryResolve(handle, out _));
    }

    [Fact]
    public void SetEntityTransform_WithStaleHandle_IsNoop()
    {
        var backend = new StrideAnimationBackend();
        var handle = backend.RegisterEntity(1u, 0L);
        backend.UnregisterEntity(handle);
        // Must not crash.
        backend.SetEntityTransform(handle, 1f, 2f, 3f, 0f);
    }

    [Fact]
    public void Tick_WithTransformAndActiveSlot_DoesNotCrash()
    {
        var backend = new StrideAnimationBackend();
        var handle = backend.RegisterEntity(1u, 0L);
        backend.SetEntityTransform(handle, 5f, 0f, 5f, 0f);
        backend.PlayMontageOnSlot(handle, new PlayMontageParams { MontageId = 1, PlayRate = 1f });

        for (int i = 0; i < 10; i++)
        {
            backend.SetEntityTransform(handle, 5f + i * 0.1f, 0f, 5f, 0f);
            backend.Tick(0.016f);
        }

        Assert.True(backend.IsAnySlotActive(handle));
    }

    // -----------------------------------------------------------------------
    // ANC-P8-01 / ANC-P8-03: additional operations (aim, stance)
    // -----------------------------------------------------------------------

    [Fact]
    public void SetAimTargetPoint_Succeeds()
    {
        var backend = new StrideAnimationBackend();
        var handle = backend.RegisterEntity(1u, 0L);
        backend.SetAimTargetPoint(handle, new LookAtPointParams
        {
            WorldPointX = 1f,
            WorldPointY = 0f,
            WorldPointZ = 3f,
            BlendInTime = 0.2f,
        });
        backend.Tick(0.016f);
    }

    [Fact]
    public void SetAimTargetEntity_Succeeds()
    {
        var backend = new StrideAnimationBackend();
        var handle = backend.RegisterEntity(1u, 0L);
        backend.SetAimTargetEntity(handle, new LookAtEntityParams
        {
            TargetEntityId = 99u,
            BlendInTime = 0.15f,
        });
        backend.Tick(0.016f);
    }

    [Fact]
    public void ReleaseAim_AfterSetAim_DoesNotCrash()
    {
        var backend = new StrideAnimationBackend();
        var handle = backend.RegisterEntity(1u, 0L);
        backend.SetAimTargetPoint(handle, new LookAtPointParams
        {
            WorldPointX = 0f, WorldPointY = 0f, WorldPointZ = 1f, BlendInTime = 0.1f,
        });
        backend.Tick(0.1f);
        backend.ReleaseAim(handle, new ReleaseLookParams { BlendOutTime = 0.2f });
        backend.Tick(0.3f);
    }

    [Fact]
    public void RequestStanceChange_Succeeds()
    {
        var backend = new StrideAnimationBackend();
        var handle = backend.RegisterEntity(1u, 0L);
        backend.RequestStanceChange(handle, targetStance: 1, blendDurationSeconds: 0.5f);
        backend.Tick(0.3f);

        // Transition not yet complete.
        Assert.True(backend.GetCurrentStance(handle, out byte mid));
        Assert.Equal(0, mid); // still in source stance

        backend.Tick(0.3f); // total > 0.5 s, transition complete
        Assert.True(backend.GetCurrentStance(handle, out byte final));
        Assert.Equal(1, final);
    }

    [Fact]
    public void GetCurrentStance_WithStaleHandle_ReturnsFalse()
    {
        var backend = new StrideAnimationBackend();
        var handle = backend.RegisterEntity(1u, 0L);
        backend.UnregisterEntity(handle);
        Assert.False(backend.GetCurrentStance(handle, out _));
    }

    // -----------------------------------------------------------------------
    // ANC-P8-03: metrics
    // -----------------------------------------------------------------------

    [Fact]
    public void SnapshotMetrics_ReflectsActiveSlots()
    {
        var backend = new StrideAnimationBackend();
        var h1 = backend.RegisterEntity(1u, 0L);
        var h2 = backend.RegisterEntity(2u, 0L);
        backend.PlayMontageOnSlot(h1, new PlayMontageParams { MontageId = 1, PlayRate = 1f });
        backend.PlayMontageOnSlot(h2, new PlayMontageParams { MontageId = 2, PlayRate = 1f });

        var m = backend.SnapshotMetrics();
        Assert.Equal(2, m.ActiveEntityCount);
        Assert.Equal(2, m.TotalActiveSlotsCount);
    }

    [Fact]
    public void SnapshotMetrics_LastTickMs_IsNonnegative()
    {
        var backend = new StrideAnimationBackend();
        backend.RegisterEntity(1u, 0L);
        backend.Tick(0.016f);
        Assert.True(backend.SnapshotMetrics().LastTickMs >= 0f);
    }
}
