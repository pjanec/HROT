using Fdp.Core;
using Hrot.Blueprints.Tests.Mocks;

namespace Hrot.Blueprints.Tests.Mocks;

/// <summary>
/// Contract tests for MockSimulationView (TH-001 SC1-SC5 and TH-DD SS3.9).
/// </summary>
public sealed class MockSimulationViewContractTests
{
    // SC1: GetComponentRO returns a reference into chunk memory (not a copy).
    [Fact]
    public void GetComponentRO_ReturnsRefIntoChunkMemory()
    {
        using var repo = new EntityRepository();
        MockTestComponents.Register(repo);
        var ecb = new MockEntityCommandBuffer(repo);
        var view = new MockSimulationView(repo, ecb);

        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new TestComponent { Value = 42 });

        ref readonly var compRef = ref view.GetComponentRO<TestComponent>(entity);
        int firstRead = compRef.Value;

        // Write via RW path -- same chunk memory should be reflected through the RO ref.
        ref var rw = ref repo.GetComponentRW<TestComponent>(entity);
        rw.Value = 99;

        Assert.Equal(42, firstRead);
        Assert.Equal(99, compRef.Value);
    }

    // SC2: AdvanceTime accumulates correctly across multiple calls.
    [Fact]
    public void AdvanceTime_AccumulatesCorrectly()
    {
        using var repo = new EntityRepository();
        var ecb = new MockEntityCommandBuffer(repo);
        var view = new MockSimulationView(repo, ecb);

        view.AdvanceTime(0.016f);
        view.AdvanceTime(0.016f);
        view.AdvanceTime(0.016f);

        Assert.Equal(3u, view.Tick);
        Assert.Equal(0.016f, view.DeltaTime, precision: 4);
        Assert.True(MathF.Abs(view.Time - 0.048f) < 0.0001f);
    }

    // SC3: ReadEvents delegates to bus -- after Publish + SwapBuffers, events are visible.
    [Fact]
    public void ReadEvents_AfterPublishAndSwap_ReturnsPublishedEvent()
    {
        using var repo = new EntityRepository();
        var ecb = new MockEntityCommandBuffer(repo);
        var view = new MockSimulationView(repo, ecb);

        repo.Bus.Publish(new TestEvent { Value = 7 });
        repo.Bus.SwapBuffers();

        var events = view.ReadEvents<TestEvent>();

        Assert.Equal(1, events.Length);
        Assert.Equal(7, events[0].Value);
    }

    // SC4: GetCommandBuffer returns the same instance passed at construction.
    [Fact]
    public void GetCommandBuffer_ReturnsSameInstance()
    {
        using var repo = new EntityRepository();
        var ecb = new MockEntityCommandBuffer(repo);
        var view = new MockSimulationView(repo, ecb);

        Assert.Same(ecb, view.GetCommandBuffer());
    }

    // SC5 / TH-DD SS3.9: After second Publish+Swap (no new publish), ReadEvents returns empty.
    [Fact]
    public void ReadEvents_AfterSwapWithNoPublish_ReturnsEmpty()
    {
        using var repo = new EntityRepository();
        var ecb = new MockEntityCommandBuffer(repo);
        var view = new MockSimulationView(repo, ecb);

        // First frame: publish and swap.
        repo.Bus.Publish(new TestEvent { Value = 1 });
        repo.Bus.SwapBuffers();

        // Consume first frame events.
        Assert.Equal(1, view.ReadEvents<TestEvent>().Length);

        // Second frame: swap with no new publish.
        repo.Bus.SwapBuffers();

        Assert.Equal(0, view.ReadEvents<TestEvent>().Length);
    }

    // TH-DD SS3.9: No SetSingleton method exists on MockSimulationView.
    [Fact]
    public void MockView_DoesNotExposeDirectSingletonSetter()
    {
        var methods = typeof(MockSimulationView)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        Assert.DoesNotContain(methods, m => m.Name == "SetSingleton");
    }
}
