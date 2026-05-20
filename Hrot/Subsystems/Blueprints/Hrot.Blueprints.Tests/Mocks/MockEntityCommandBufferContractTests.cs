using System.Runtime.InteropServices;
using Fdp.Core;
using Hrot.Blueprints.Tests.Mocks;

namespace Hrot.Blueprints.Tests.Mocks;

/// <summary>
/// Contract tests for MockEntityCommandBuffer (TH-002 SC1-SC6 and TH-DD SS4.10).
/// </summary>
public sealed class MockEntityCommandBufferContractTests
{
    // SC1: CreateEntity -- OpCount increments; entity is alive immediately.
    [Fact]
    public void CreateEntity_EagerHandle_EntityAliveAndOpRecorded()
    {
        using var repo = new EntityRepository();
        MockTestComponents.Register(repo);
        var ecb = new MockEntityCommandBuffer(repo);

        var e = ecb.CreateEntity();

        Assert.Equal(1, ecb.OpCount);
        Assert.True(repo.IsAlive(e));
        Assert.False(repo.HasComponent<TestComponent>(e));
    }

    // SC2: AddComponent + Playback -- component appears with correct value after playback.
    [Fact]
    public void AddComponent_ThenPlayback_ComponentAppearsWithCorrectValue()
    {
        using var repo = new EntityRepository();
        MockTestComponents.Register(repo);
        var ecb = new MockEntityCommandBuffer(repo);

        var e = ecb.CreateEntity();
        ecb.AddComponent(e, new TestComponent { Value = 7 });

        Assert.Equal(2, ecb.OpCount);
        Assert.False(repo.HasComponent<TestComponent>(e));

        ecb.Playback(repo);

        Assert.Equal(0, ecb.OpCount);
        Assert.True(repo.HasComponent<TestComponent>(e));
        Assert.Equal(7, repo.GetComponentRO<TestComponent>(e).Value);
    }

    // SC3: AddEmptyComponent<LargeTestStruct> -- all bytes zero after playback.
    [Fact]
    public unsafe void AddEmptyComponent_LargeUnmanaged_DefaultInitsAfterPlayback()
    {
        using var repo = new EntityRepository();
        MockTestComponents.Register(repo);
        var ecb = new MockEntityCommandBuffer(repo);

        var e = ecb.CreateEntity();
        ecb.AddEmptyComponent<LargeTestStruct>(e);
        ecb.Playback(repo);

        Assert.True(repo.HasComponent<LargeTestStruct>(e));

        ref readonly var comp = ref repo.GetComponentRO<LargeTestStruct>(e);
        var bytes = MemoryMarshal.AsBytes(new ReadOnlySpan<LargeTestStruct>(in comp));
        foreach (var b in bytes)
            Assert.Equal(0, b);
    }

    // SC4: DestroyEntity -- entity alive before playback, gone after.
    [Fact]
    public void DestroyEntity_EntityAliveBeforePlayback_GoneAfterPlayback()
    {
        using var repo = new EntityRepository();
        var ecb = new MockEntityCommandBuffer(repo);

        var e = repo.CreateEntity();
        ecb.DestroyEntity(e);

        Assert.True(repo.IsAlive(e));

        ecb.Playback(repo);

        Assert.False(repo.IsAlive(e));
    }

    // SC5: Three sequential SetComponent ops -- last write wins after playback.
    [Fact]
    public void SetComponent_MultipleOps_LastWriteWinsAfterPlayback()
    {
        using var repo = new EntityRepository();
        MockTestComponents.Register(repo);
        var ecb = new MockEntityCommandBuffer(repo);

        var e = repo.CreateEntity();
        repo.AddComponent(e, new TestComponent { Value = 0 });

        ecb.SetComponent(e, new TestComponent { Value = 1 });
        ecb.SetComponent(e, new TestComponent { Value = 2 });
        ecb.SetComponent(e, new TestComponent { Value = 3 });

        ecb.Playback(repo);

        Assert.Equal(3, repo.GetComponentRO<TestComponent>(e).Value);
    }

    // SC6 / TH-DD SS4.10: After Playback, OpCount is zero (ops cleared).
    [Fact]
    public void Playback_ClearsOps_OpCountIsZero()
    {
        using var repo = new EntityRepository();
        MockTestComponents.Register(repo);
        var ecb = new MockEntityCommandBuffer(repo);

        var e = ecb.CreateEntity();
        ecb.AddComponent(e, new TestComponent { Value = 1 });
        Assert.Equal(2, ecb.OpCount);

        ecb.Playback(repo);

        Assert.Equal(0, ecb.OpCount);
    }

    // TH-DD SS4.10: DeadEntityGuard -- op against a destroyed entity is silently ignored.
    [Fact]
    public void SetComponent_AfterDestroyedDirectly_IsIgnoredOnPlayback()
    {
        using var repo = new EntityRepository();
        MockTestComponents.Register(repo);
        var ecb = new MockEntityCommandBuffer(repo);

        var e = repo.CreateEntity();
        repo.AddComponent(e, new TestComponent { Value = 5 });

        // Queue a set op, then destroy the entity directly (outside ECB).
        ecb.SetComponent(e, new TestComponent { Value = 99 });
        repo.DestroyEntity(e);

        // Playback must not throw even though entity is dead.
        ecb.Playback(repo);

        Assert.False(repo.IsAlive(e));
    }
}
