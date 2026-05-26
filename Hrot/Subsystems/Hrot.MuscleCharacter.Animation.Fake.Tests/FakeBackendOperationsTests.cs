using Hrot.MuscleCharacter.Animation.Contracts;
using Xunit;

namespace Hrot.MuscleCharacter.Animation.Fake.Tests;

public class FakeBackendOperationsTests
{
    [Fact]
    public void RegisterEntity_ReturnsValidHandle()
    {
        var backend = new FakeAnimationBackend();
        var handle = backend.RegisterEntity(1, 0);
        Assert.NotEqual(0u, handle.Index);
        Assert.NotEqual(0u, handle.Generation);
    }

    [Fact]
    public void TryResolve_WithValidHandle_ReturnsTrue()
    {
        var backend = new FakeAnimationBackend();
        var handle = backend.RegisterEntity(1, 0);
        Assert.True(backend.TryResolve(handle, out _));
    }

    [Fact]
    public void TryResolve_WithStaleHandle_ReturnsFalse()
    {
        var backend = new FakeAnimationBackend();
        var handle = backend.RegisterEntity(1, 0);
        backend.UnregisterEntity(handle);
        Assert.False(backend.TryResolve(handle, out _));
    }

    [Fact]
    public void UnregisterEntity_FollowedByRegister_BumpsGeneration()
    {
        var backend = new FakeAnimationBackend();
        var handle1 = backend.RegisterEntity(1, 0);
        var gen1 = handle1.Generation;
        backend.UnregisterEntity(handle1);
        var handle2 = backend.RegisterEntity(2, 0);
        Assert.NotEqual(gen1, handle2.Generation);
    }

    [Fact]
    public void PlayMontageOnSlot_Succeeds()
    {
        var backend = new FakeAnimationBackend();
        var handle = backend.RegisterEntity(1, 0);
        var @params = new PlayMontageParams { MontageId = 42, PlayRate = 1.0f };
        backend.PlayMontageOnSlot(handle, in @params);
    }

    [Fact]
    public void Tick_Succeeds()
    {
        var backend = new FakeAnimationBackend();
        backend.RegisterEntity(1, 0);
        backend.Tick(0.016f);
    }

    [Fact]
    public void SnapshotMetrics_Succeeds()
    {
        var backend = new FakeAnimationBackend();
        backend.RegisterEntity(1, 0);
        backend.RegisterEntity(2, 0);
        var metrics = backend.SnapshotMetrics();
        Assert.Equal(2, metrics.ActiveEntityCount);
    }

    [Fact]
    public void DrainNotifies_Succeeds()
    {
        var backend = new FakeAnimationBackend();
        var buffer = new RawNotifyEvent[16];
        var count = backend.DrainNotifies(buffer.AsSpan());
        Assert.Equal(0, count);
    }

    [Fact]
    public void SetAimTargetPoint_Succeeds()
    {
        var backend = new FakeAnimationBackend();
        var handle = backend.RegisterEntity(1, 0);
        var @params = new LookAtPointParams { WorldPointX = 1, WorldPointY = 2, WorldPointZ = 3 };
        backend.SetAimTargetPoint(handle, in @params);
    }

    [Fact]
    public void SetAimTargetEntity_Succeeds()
    {
        var backend = new FakeAnimationBackend();
        var handle = backend.RegisterEntity(1, 0);
        var @params = new LookAtEntityParams { TargetEntityId = 2 };
        backend.SetAimTargetEntity(handle, in @params);
    }

    [Fact]
    public void ReleaseAim_Succeeds()
    {
        var backend = new FakeAnimationBackend();
        var handle = backend.RegisterEntity(1, 0);
        var @params = new ReleaseLookParams { BlendOutTime = 0.2f };
        backend.ReleaseAim(handle, in @params);
    }

    [Fact]
    public void RequestStanceChange_Succeeds()
    {
        var backend = new FakeAnimationBackend();
        var handle = backend.RegisterEntity(1, 0);
        backend.RequestStanceChange(handle, 1, 0.5f);
    }

    [Fact]
    public void StopMontageOnSlot_Succeeds()
    {
        var backend = new FakeAnimationBackend();
        var handle = backend.RegisterEntity(1, 0);
        var @params = new StopMontageParams { BlendOutTime = 0.2f };
        backend.StopMontageOnSlot(handle, in @params);
    }

    [Fact]
    public void MultipleEntities_AllResolveCorrectly()
    {
        var backend = new FakeAnimationBackend();
        var h1 = backend.RegisterEntity(1, 0);
        var h2 = backend.RegisterEntity(2, 0);
        var h3 = backend.RegisterEntity(3, 0);
        Assert.True(backend.TryResolve(h1, out var s1));
        Assert.True(backend.TryResolve(h2, out var s2));
        Assert.True(backend.TryResolve(h3, out var s3));
        Assert.NotEqual(s1, s2);
        Assert.NotEqual(s2, s3);
    }

    [Fact]
    public void Tick_WithMultipleEntities_Succeeds()
    {
        var backend = new FakeAnimationBackend();
        backend.RegisterEntity(1, 0);
        backend.RegisterEntity(2, 0);
        for (int i = 0; i < 60; i++)
            backend.Tick(0.016f);
    }
}
