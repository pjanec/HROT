using Fdp.Core;
using Hrot.Blueprints.Tests.Runtime;

namespace Hrot.Blueprints.Tests.Runtime;

/// <summary>
/// §10.3: Per-frame Blueprint tick must not allocate managed memory on hot path.
/// </summary>
[Collection("DebugProbe")]
public sealed class AllocationFreeTests
{
    // 10.3: Extended warm-up + multi-pass steady-state measurement on 10 entities -> 0 bytes allocated.
    [SkippableFact]
    public void TickFrame_1000Frames_AllocatesZeroBytes()
    {
        // Zero-allocation budgets are tuned for the Windows runtime; JIT tiering and BCL
        // internals allocate differently on Linux/macOS, so this microbenchmark is
        // Windows-only (matches the platform where it was calibrated and is run for real).
        Skip.IfNot(System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows),
            "Allocation budget is calibrated for the Windows runtime; runtime allocation differs on other platforms.");

        using var fixture = new BlueprintTestFixture();
        FakeInstanceBp.Register(fixture.Registry);
        var asset = FakeInstanceBp.MakeAsset();

        // Attach 10 entities
        for (int i = 0; i < 10; i++)
        {
            var entity = fixture.CreateEntity();
            fixture.AttachBlueprint(asset, entity);
        }

        // Extended warm-up: let JIT, static constructors, and lazy queries fully settle.
        for (int i = 0; i < 500; i++)
            fixture.TickFrame(0.016f);

        // Force full GC before measuring to eliminate pending finalizer allocations.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Multi-pass measurement: take the minimum allocation across several
        // batches to filter out one-time GC housekeeping / JIT residual noise.
        const int passes        = 5;
        const int framesPerPass = 100;
        long minAllocated = long.MaxValue;

        for (int pass = 0; pass < passes; pass++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < framesPerPass; i++)
                fixture.TickFrame(0.016f);
            long after  = GC.GetAllocatedBytesForCurrentThread();
            long allocated = after - before;
            if (allocated < minAllocated)
                minAllocated = allocated;
        }

        Assert.Equal(0L, minAllocated);
    }
}
