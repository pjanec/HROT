using Hrot.Editor.AiShared.Debug;

namespace Hrot.Editor.AiShared.Tests.Debug;

public sealed class AiTracerCoordinatorTests
{
    // Subclass that counts BeginObservingAssetImpl and EndObservingAssetImpl calls.
    private sealed class CountingCoordinator : AiTracerCoordinator
    {
        public int BeginImplCallCount { get; private set; }
        public int EndImplCallCount { get; private set; }

        protected override void BeginObservingAssetImpl(Guid assetId, TraceLevel level)
            => BeginImplCallCount++;

        protected override void EndObservingAssetImpl(Guid assetId)
            => EndImplCallCount++;
    }

    private static readonly Guid AssetA = Guid.NewGuid();
    private static readonly Guid AssetB = Guid.NewGuid();

    [Fact]
    public void AddObserver_FirstObserver_RefCountIsOne()
    {
        var c = new AiTracerCoordinator();
        c.AddObserver(AssetA, TraceLevel.Lifecycle);

        // Refcount = 1 means removing once makes it unobserved.
        c.RemoveObserver(AssetA);
        Assert.False(c.IsObserving(AssetA));
    }

    [Fact]
    public void AddObserver_SecondObserver_RefCountIsTwo()
    {
        var c = new AiTracerCoordinator();
        c.AddObserver(AssetA, TraceLevel.Lifecycle);
        c.AddObserver(AssetA, TraceLevel.Decisions);

        // Refcount = 2: after one remove it is still observed.
        c.RemoveObserver(AssetA);
        Assert.True(c.IsObserving(AssetA));

        c.RemoveObserver(AssetA);
        Assert.False(c.IsObserving(AssetA));
    }

    [Fact]
    public void RemoveObserver_OnZeroRefCount_CallsEndImpl()
    {
        var c = new CountingCoordinator();
        c.AddObserver(AssetA, TraceLevel.Lifecycle);
        c.RemoveObserver(AssetA);

        Assert.Equal(1, c.EndImplCallCount);
        Assert.False(c.IsObserving(AssetA));
    }

    [Fact]
    public void RemoveObserver_WhenTwoObservers_RefCountIsOne_EndImplNotCalled()
    {
        var c = new CountingCoordinator();
        c.AddObserver(AssetA, TraceLevel.Lifecycle);
        c.AddObserver(AssetA, TraceLevel.Decisions);

        c.RemoveObserver(AssetA);

        Assert.Equal(0, c.EndImplCallCount);
        Assert.True(c.IsObserving(AssetA));
    }

    [Fact]
    public void RemoveObserver_UnobservedAsset_IsNoOp()
    {
        var c = new CountingCoordinator();
        // Should not throw and EndImpl should not be called.
        c.RemoveObserver(AssetA);

        Assert.Equal(0, c.EndImplCallCount);
    }

    [Fact]
    public void GetEffectiveLevel_ReturnsNone_WhenNotObserved()
    {
        var c = new AiTracerCoordinator();
        Assert.Equal(TraceLevel.None, c.GetEffectiveLevel(AssetA));
    }

    [Fact]
    public void GetEffectiveLevel_ReturnsSingleLevel_WhenOneObserver()
    {
        var c = new AiTracerCoordinator();
        c.AddObserver(AssetA, TraceLevel.Errors);

        Assert.Equal(TraceLevel.Errors, c.GetEffectiveLevel(AssetA));
    }

    [Fact]
    public void GetEffectiveLevel_ReturnsUnion_WhenMultipleObservers()
    {
        var c = new AiTracerCoordinator();
        c.AddObserver(AssetA, TraceLevel.Lifecycle);
        c.AddObserver(AssetA, TraceLevel.Decisions);

        Assert.Equal(TraceLevel.Lifecycle | TraceLevel.Decisions, c.GetEffectiveLevel(AssetA));
    }

    [Fact]
    public void IsObserving_ReturnsFalse_WhenNotObserved()
    {
        var c = new AiTracerCoordinator();
        Assert.False(c.IsObserving(AssetA));
    }

    [Fact]
    public void IsObserving_ReturnsTrue_WhenObserved()
    {
        var c = new AiTracerCoordinator();
        c.AddObserver(AssetA, TraceLevel.Lifecycle);
        Assert.True(c.IsObserving(AssetA));
    }

    [Fact]
    public void AddObserver_ThenRemoveTwice_SecondRemoveIsNoOp()
    {
        var c = new CountingCoordinator();
        c.AddObserver(AssetA, TraceLevel.Lifecycle);
        c.RemoveObserver(AssetA);
        c.RemoveObserver(AssetA); // second remove -- should be a no-op

        Assert.Equal(1, c.EndImplCallCount);
        Assert.False(c.IsObserving(AssetA));
    }

    [Fact]
    public void BeginObservingAssetImpl_CalledOnFirstAdd_NotOnSubsequentAdds()
    {
        var c = new CountingCoordinator();
        c.AddObserver(AssetA, TraceLevel.Lifecycle);
        c.AddObserver(AssetA, TraceLevel.Decisions);
        c.AddObserver(AssetA, TraceLevel.Values);

        Assert.Equal(1, c.BeginImplCallCount);
    }
}
