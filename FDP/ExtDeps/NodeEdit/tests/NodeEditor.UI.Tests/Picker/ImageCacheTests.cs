using NodeEditor.UI.Picker;
using Xunit;

namespace NodeEditor.UI.Tests.Picker;

/// <summary>
/// ⭐ CE-1468 — the ONE image LRU, generic over its key (the OSM wizard's tiles key it by a struct): a PENDING answer is asked again next time and costs no budget,
/// a failed one is remembered, a loaded one is kept and evicted least-recently-used with the unloader called; a struct key looks up without allocating.
/// 🔴 Red-proof (compiles): the pending answer remembered like a failure ⇒ the "arrives later" rail reds.
/// </summary>
public sealed class ImageCacheTests
{
    [Fact]
    public void APendingKey_IsAskedAgain_WithoutSpendingTheBudget_AndLoadsWhenItArrives()
    {
        bool arrived = false; int calls = 0;
        var c = new ImageCache<(int, int, int)>(k => { calls++; return arrived ? ImageLoad.Loaded(new PickerImage((IntPtr)7, 256, 256)) : ImageLoad.Pending; }, maxLoadsPerFrame: 1);
        c.BeginFrame();
        Assert.False(c.TryGet((1, 2, 3), out _));
        Assert.False(c.TryGet((1, 2, 4), out _));   // the first answer was pending ⇒ the one-load budget is still there
        Assert.Equal(2, calls);
        Assert.Equal(0, c.Count);                    // nothing remembered
        arrived = true; c.BeginFrame();
        Assert.True(c.TryGet((1, 2, 3), out var img));
        Assert.Equal((IntPtr)7, img.Texture);
        Assert.True(c.TryGet((1, 2, 3), out _));     // now held: no loader call
        Assert.Equal(3, calls);
    }

    [Fact]
    public void AFailedKey_IsRemembered_AndTheLeastRecentlyUsed_IsEvictedAndUnloaded()
    {
        var unloaded = new List<IntPtr>();
        var c = new ImageCache<(int, int, int)>(k => k.Item1 < 0 ? ImageLoad.Failed : ImageLoad.Loaded(new PickerImage((IntPtr)k.Item1, 1, 1)), unloaded.Add, capacity: 2, maxLoadsPerFrame: 99);
        c.BeginFrame();
        Assert.False(c.TryGet((-1, 0, 0), out _));
        Assert.False(c.TryGet((-1, 0, 0), out _));
        Assert.Equal(1, c.Count);                    // the failure remembered once
        c.TryGet((10, 0, 0), out _); c.TryGet((20, 0, 0), out _);
        Assert.Empty(unloaded);   // the failure was evicted: nothing to unload
        c.TryGet((10, 0, 0), out _); c.TryGet((30, 0, 0), out _);         // 20 is now the least recently used
        Assert.Equal(new[] { (IntPtr)20 }, unloaded);
    }

    [Fact]
    public void AStructKeyLookup_DoesNotAllocate()
    {
        var c = new ImageCache<(int, int, int)>(k => ImageLoad.Loaded(new PickerImage((IntPtr)1, 1, 1)), capacity: 8, maxLoadsPerFrame: 99);
        c.BeginFrame();
        for (int f = 0; f < 50; f++) { c.BeginFrame(); for (int i = 0; i < 4; i++) c.TryGet((5, i, 0), out _); }   // warm (first calls JIT)
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        for (int f = 0; f < 50; f++) { c.BeginFrame(); for (int i = 0; i < 4; i++) c.TryGet((5, i, 0), out _); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - a0);
    }
}
