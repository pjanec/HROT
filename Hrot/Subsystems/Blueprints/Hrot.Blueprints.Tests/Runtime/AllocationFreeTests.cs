using Fdp.Core;
using Hrot.Blueprints.Tests.Runtime;

namespace Hrot.Blueprints.Tests.Runtime;

/// <summary>
/// §10.3: Per-frame Blueprint tick must not allocate managed memory on hot path.
/// </summary>
public sealed class AllocationFreeTests
{
    // 10.3: 100 warm-up frames + 100 measured frames on 10 entities -> 0 bytes allocated.
    [Fact]
    public void TickFrame_1000Frames_AllocatesZeroBytes()
    {
        using var fixture = new BlueprintTestFixture();
        FakeInstanceBp.Register(fixture.Registry);
        var asset = FakeInstanceBp.MakeAsset();

        // Attach 10 entities
        for (int i = 0; i < 10; i++)
        {
            var entity = fixture.CreateEntity();
            fixture.AttachBlueprint(asset, entity);
        }

        // Warm-up: 100 frames to let JIT settle and lazy queries initialize
        for (int i = 0; i < 100; i++)
            fixture.TickFrame(0.016f);

        // Measure: capture allocations over 100 frames
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
            fixture.TickFrame(0.016f);
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0L, after - before);
    }
}
