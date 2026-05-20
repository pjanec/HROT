using System.Runtime.InteropServices;
using Fdp.Core;
using Hrot.Blueprints.Tests.Mocks;

namespace Hrot.Blueprints.Tests.Mocks;

/// <summary>
/// Mock contract enforcement tests (TH-007 / TH-DD SS8.3).
/// All tests use MockSimulationView + MockEntityCommandBuffer directly
/// because BlueprintTestFixture (TH-003) is implemented in a later batch.
/// Test 6 (TierUpgrade) is skipped until BlueprintMaintenanceSystem exists (BATCH-04).
/// </summary>
public sealed class MockContractTests
{
    // 1. Entity alive before ECB playback, gone after.
    [Fact]
    public void IsAlive_AfterEcbDestroy_RemainsTrueUntilPlayback()
    {
        using var repo = new EntityRepository();
        var ecb = new MockEntityCommandBuffer(repo);

        var e = repo.CreateEntity();
        ecb.DestroyEntity(e);

        Assert.True(repo.IsAlive(e));

        ecb.Playback(repo);

        Assert.False(repo.IsAlive(e));
    }

    // 2. GetComponentRO returns ref into chunk memory (same memory as RW path).
    [Fact]
    public void GetComponentRO_ReturnsRefIntoChunkMemory()
    {
        using var repo = new EntityRepository();
        MockTestComponents.Register(repo);
        var ecb = new MockEntityCommandBuffer(repo);
        var view = new MockSimulationView(repo, ecb);

        var e = repo.CreateEntity();
        repo.AddComponent(e, new TestComponent { Value = 1 });

        ref readonly var roRef = ref view.GetComponentRO<TestComponent>(e);

        ref var rw = ref repo.GetComponentRW<TestComponent>(e);
        rw.Value = 99;

        Assert.Equal(99, roRef.Value);
    }

    // 3. ReadEvents -- patched (Patch 1): publish via bus, swap, then read via view.
    [Fact]
    public void ReadEvents_SameListThroughoutTick()
    {
        using var repo = new EntityRepository();
        var ecb = new MockEntityCommandBuffer(repo);
        var view = new MockSimulationView(repo, ecb);

        repo.Bus.Publish(new TestEvent { Value = 42 });
        repo.Bus.SwapBuffers();

        var span1 = view.ReadEvents<TestEvent>();
        var span2 = view.ReadEvents<TestEvent>();

        Assert.Equal(1, span1.Length);
        Assert.Equal(1, span2.Length);
        Assert.Equal(42, span1[0].Value);
        Assert.Equal(42, span2[0].Value);
    }

    // 4. No SetSingleton method on MockSimulationView (reflection check).
    [Fact]
    public void MockView_DoesNotExposeDirectSingletonSetter()
    {
        var methods = typeof(MockSimulationView)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        Assert.DoesNotContain(methods, m => m.Name == "SetSingleton");
    }

    // 5. Insertion-order playback: last SetComponent value wins.
    [Fact]
    public void Playback_PreservesInsertionOrder()
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

    // 6. TierUpgrade -- requires BlueprintMaintenanceSystem (implemented in BATCH-04).
    [Fact(Skip = "Requires BlueprintMaintenanceSystem (BATCH-04)")]
    public void TierUpgrade_HappensInBeforeSync_NotInSimulation()
    {
        // Will be implemented when BlueprintMaintenanceSystem is available.
    }

    // 7. AddEmptyComponent with large struct -- all bytes zero after playback.
    [Fact]
    public unsafe void AddEmptyComponent_LargeUnmanaged_DefaultInitsAfterPlayback()
    {
        using var repo = new EntityRepository();
        MockTestComponents.Register(repo);
        var ecb = new MockEntityCommandBuffer(repo);

        var e = repo.CreateEntity();
        ecb.AddEmptyComponent<LargeTestStruct>(e);
        ecb.Playback(repo);

        Assert.True(repo.HasComponent<LargeTestStruct>(e));

        ref readonly var comp = ref repo.GetComponentRO<LargeTestStruct>(e);
        var bytes = MemoryMarshal.AsBytes(new ReadOnlySpan<LargeTestStruct>(in comp));
        foreach (var b in bytes)
            Assert.Equal(0, b);
    }

    // 8. CreateEntity -- real handle returned immediately (before any Playback).
    [Fact]
    public void CreateEntity_ReturnsRealHandleImmediately()
    {
        using var repo = new EntityRepository();
        var ecb = new MockEntityCommandBuffer(repo);

        var e = ecb.CreateEntity();

        Assert.True(repo.IsAlive(e));
    }
}
