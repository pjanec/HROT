using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Hrot.Blueprints.Tests;

namespace Hrot.Blueprints.Tests.HotReload;

/// <summary>
/// Verifies ALC unload and GC reclaim behavior across reload sequences.
/// </summary>
[Collection("DebugProbe")]
public sealed class AlcLifecycleTests
{
    [Fact]
    public void SuccessfulReload_UnloadsOldAlc()
    {
        WeakReference alc1WeakRef;
        WeakReference<AssemblyLoadContext>[] alcWeakRefs;
        SuccessfulReload_UnloadsOldAlc_Body(out alc1WeakRef, out alcWeakRefs);
        for (int i = 0; i < 50; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (!alc1WeakRef.IsAlive && alcWeakRefs.All(w => !w.TryGetTarget(out _))) return;
            Thread.Sleep(50);
        }
        Assert.False(alc1WeakRef.IsAlive, "Old ALC should be reclaimed after successful reload.");
    }

    // [NoInlining] confines all ALC-touching locals (including fixture) to this frame so
    // the GC loop in the [Fact] runs with no ALC-holding roots on the stack (DEBT-009).
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SuccessfulReload_UnloadsOldAlc_Body(
        out WeakReference alc1WeakRef, out WeakReference<AssemblyLoadContext>[] alcWeakRefs)
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var v1 = TestData.LoadAsset(TestData.SampleAssets.LibraryMath);
        fixture.CompileAndLoad(v1);

        var alc1 = fixture.GetCurrentAlc();
        Assert.NotNull(alc1);
        alc1WeakRef = MakeWeakRef(alc1!);

        var v2 = TestData.LoadAsset(TestData.SampleAssets.MoveToAndFire);
        fixture.SimulateReload(new[] { v2 });

        var alc2 = fixture.GetCurrentAlc();
        Assert.NotSame(alc1, alc2);
        alc1 = null;
        alc2 = null;
        alcWeakRefs = fixture.GetAlcWeakReferences().ToArray();
    }

    [Fact]
    public void FailedReload_DoesNotLeakNewAlc()
    {
        WeakReference<AssemblyLoadContext>[] alcWeakRefs;
        FailedReload_DoesNotLeakNewAlc_Body(out alcWeakRefs);
        for (int i = 0; i < 50; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (alcWeakRefs.All(w => !w.TryGetTarget(out _))) return;
            Thread.Sleep(50);
        }
        int leaked = alcWeakRefs.Count(w => w.TryGetTarget(out _));
        Assert.True(leaked == 0, $"{leaked} ALC(s) not GC-reclaimed after 20 retries.");
    }

    // [NoInlining] confines all ALC-touching locals (including fixture) to this frame so
    // the GC loop in the [Fact] runs with no ALC-holding roots on the stack (DEBT-009).
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void FailedReload_DoesNotLeakNewAlc_Body(
        out WeakReference<AssemblyLoadContext>[] alcWeakRefs)
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var v1 = TestData.LoadAsset(TestData.SampleAssets.LibraryMath);
        fixture.CompileAndLoad(v1);

        // Failed reload — coordinator should unload the new (failed) ALC.
        // Use [NoInlining] helper so the exception (which holds TargetSite from the failed ALC)
        // goes out of scope before the GC check (DEBT-017).
        ThrowingRegistrarMustThrow(fixture);

        // Force GC to reclaim the failed ALC.
        fixture.ForceGcReclaim();

        // The only live ALC should be the coordinator's current one.
        var liveAlcs = fixture.GetAlcWeakReferences()
            .Count(w => w.TryGetTarget(out _));
        Assert.Equal(1, liveAlcs);
        alcWeakRefs = fixture.GetAlcWeakReferences().ToArray();
    }

    [Fact]
    public void ChainedReloads_R1Success_R2Failure_R3Success_CorrectAlcAtEachStep()
    {
        WeakReference alcBWeakRef;
        WeakReference<AssemblyLoadContext>[] alcWeakRefs;
        ChainedReloads_R1Success_R2Failure_R3Success_Body(out alcBWeakRef, out alcWeakRefs);
        for (int i = 0; i < 50; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (!alcBWeakRef.IsAlive && alcWeakRefs.All(w => !w.TryGetTarget(out _))) return;
            Thread.Sleep(50);
        }
        Assert.False(alcBWeakRef.IsAlive, "R1 ALC should be reclaimed after R3 success.");
    }

    // [NoInlining] confines all ALC-touching locals (including fixture) to this frame so
    // the GC loop in the [Fact] runs with no ALC-holding roots on the stack (DEBT-009).
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ChainedReloads_R1Success_R2Failure_R3Success_Body(
        out WeakReference alcBWeakRef, out WeakReference<AssemblyLoadContext>[] alcWeakRefs)
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var v1 = TestData.LoadAsset(TestData.SampleAssets.LibraryMath);
        fixture.CompileAndLoad(v1);
        var alcA = fixture.GetCurrentAlc();
        Assert.NotNull(alcA);

        // R1: success.
        var v2 = TestData.LoadAsset(TestData.SampleAssets.MoveToAndFire);
        fixture.SimulateReload(new[] { v2 });
        var alcB = fixture.GetCurrentAlc();
        Assert.NotSame(alcA, alcB);

        // R2: failure.
        var ex = Record.Exception(() => fixture.SimulateReloadWithThrowingRegistrar());
        Assert.NotNull(ex);
        var alcAfterFailure = fixture.GetCurrentAlc();
        Assert.Same(alcB, alcAfterFailure);  // unchanged after failure.

        // R3: success with another asset.
        var v3 = TestData.LoadAsset(TestData.SampleAssets.HasVisibleTarget);
        fixture.SimulateReload(new[] { v3 });
        var alcD = fixture.GetCurrentAlc();
        Assert.NotSame(alcB, alcD);
        alcA = null;
        alcAfterFailure = null;
        alcD = null;

        // B should be reclaimed (replaced by R3).
        alcBWeakRef = MakeWeakRef(alcB!);
        alcB = null;
        alcWeakRefs = fixture.GetAlcWeakReferences().ToArray();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference MakeWeakRef(AssemblyLoadContext alc)
        => new WeakReference(alc);

    // [NoInlining] ensures the exception (which holds TargetSite from the failed ALC)
    // goes out of scope when this method returns, before the GC check runs (DEBT-017).
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowingRegistrarMustThrow(BlueprintTestFixture fixture)
    {
        var ex = Record.Exception(() => fixture.SimulateReloadWithThrowingRegistrar());
        Assert.NotNull(ex);
        // ex (which holds InnerException.TargetSite from failed ALC) goes out of scope here
    }
}
