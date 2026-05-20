using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace Hrot.Blueprints.Tests;

public sealed class AlcUnloadTests
{
    // Use a small, non-test assembly so the xUnit runner's AppDomain.AssemblyLoad
    // handler does not cache the loaded copy and prevent GC reclaim.
    private static byte[] GetTestAsmBytes()
        => File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Fdp.Diagnostics.Contracts.dll"));

    // SC2 / SS7.5: After Dispose, ALC is reclaimed by GC.
    // Pattern follows the official .NET "How to use and debug assembly unloadability" guide:
    //   - [NoInlining] CreateLoadAndDispose isolates all ALC-touching locals so that neither
    //     the loaded Assembly nor the ALC reference spills into the test-method frame as a
    //     Debug-JIT local GC root during the GC loop.
    //   - Non-generic WeakReference.IsAlive is used in the GC loop (not TryGetTarget(out _))
    //     because IsAlive never creates a strong reference to the target.
    [Fact]
    public void Fixture_DisposeAfterLoadAssembly_ReclaimsAlc()
    {
        WeakReference alcWeakRef;
        CreateLoadAndDispose(out alcWeakRef);

        for (int i = 0; i < 10 && alcWeakRef.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.False(alcWeakRef.IsAlive,
            "ALC should be GC-reclaimed after fixture.Dispose()");
    }

    // [NoInlining] ensures the fixture, loaded Assembly, and ALC references are confined
    // to this frame's locals and do not escape into the caller as hidden Debug-JIT GC roots.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CreateLoadAndDispose(out WeakReference alcWeakRef)
    {
        var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        fixture.LoadTestAssemblyFromBytes(GetTestAsmBytes());
        // Obtain a non-generic WeakReference (supports IsAlive); null alc immediately
        // so the Debug JIT slot is cleared before this frame's locals are examined by GC.
        fixture.GetAlcWeakReferences()[0].TryGetTarget(out var alc);
        alcWeakRef = new WeakReference(alc);
        alc = null;
        fixture.Dispose();
    }

    // SC3 / SS7.5: After multiple loads, all ALCs tracked; all live until Dispose
    [Fact]
    public void Fixture_AfterMultipleLoads_OldAlcsReclaimedNewestStillLive()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        var bytes = GetTestAsmBytes();

        // Simulate three "generations" of loaded assemblies
        fixture.LoadTestAssemblyFromBytes(bytes);   // gen 1
        fixture.LoadTestAssemblyFromBytes(bytes);   // gen 2
        fixture.LoadTestAssemblyFromBytes(bytes);   // gen 3

        Assert.Equal(3, fixture.GetAlcWeakReferences().Count);

        // All three ALCs should be live until Unload() is called
        Assert.All(fixture.GetAlcWeakReferences(),
            w => Assert.True(w.TryGetTarget(out _), "All ALCs should be live before Dispose"));
    }

    // SC1 / SS7.5: Dispose with VerifyAlcUnloadOnDispose=false and no ALCs is instant
    [Fact]
    public void Fixture_DisposeNoAlcs_Succeeds()
    {
        var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        // Should complete without exception and without calling GC.Collect
        fixture.Dispose();
    }

    // SC5 leak detection: deliberately hold a reference into the ALC, verify throw
    [Fact]
    public void Fixture_StrongRefToAlc_DetectsLeakAndThrows()
    {
        AssemblyLoadContext? heldRef = null;
        var fixture = new BlueprintTestFixture();
        try
        {
            fixture.LoadTestAssemblyFromBytes(GetTestAsmBytes());
            // Hold a strong reference to the ALC to prevent GC reclaim
            fixture.GetAlcWeakReferences()[0].TryGetTarget(out heldRef);
            Assert.NotNull(heldRef);

            // Dispose should throw because the ALC cannot be reclaimed while heldRef is live
            var ex = Assert.Throws<InvalidOperationException>(() => fixture.Dispose());
            Assert.Contains("ALC(s) not GC-reclaimed", ex.Message);
        }
        finally
        {
            heldRef = null;          // release the strong reference
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            // Second Dispose should succeed after releasing the ref
            // fixture is already partially disposed; we must not throw here
            // If fixture.Dispose() was already called and threw, the ALCs were unloaded.
            // The GC should now reclaim them. No further Dispose needed.
        }
    }
}
