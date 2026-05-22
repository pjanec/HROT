using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Hrot.Blueprints.Tests;

namespace Hrot.Blueprints.Tests.HotReload;

/// <summary>
/// Patch 3: Quick Reload goes through the coordinator.
/// </summary>
[Collection("DebugProbe")]
public sealed class QuickReloadTests
{
    [Fact]
    public void QuickReload_UpdatesCurrentAlc()
    {
        WeakReference<AssemblyLoadContext>[] alcWeakRefs;
        QuickReload_UpdatesCurrentAlc_Body(out alcWeakRefs);
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
    private static void QuickReload_UpdatesCurrentAlc_Body(
        out WeakReference<AssemblyLoadContext>[] alcWeakRefs)
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var v1 = TestData.LoadAsset(TestData.SampleAssets.LibraryMath);
        fixture.CompileAndLoad(v1);

        var alc1 = fixture.GetCurrentAlc();

        var v2 = TestData.LoadAsset(TestData.SampleAssets.MoveToAndFire);
        fixture.SimulateQuickReload(v2);

        var alc2 = fixture.GetCurrentAlc();
        Assert.NotNull(alc2);
        Assert.NotSame(alc1, alc2);
        alc1 = null;
        alc2 = null;
        alcWeakRefs = fixture.GetAlcWeakReferences().ToArray();
    }

    [Fact]
    public void QuickReload_AfterPreviousQuickReload_UnloadsThePreviousAlc()
    {
        WeakReference alc1WeakRef;
        WeakReference alc2WeakRef;
        WeakReference<AssemblyLoadContext>[] alcWeakRefs;
        QuickReload_AfterPreviousQuickReload_Body(out alc1WeakRef, out alc2WeakRef, out alcWeakRefs);
        for (int i = 0; i < 50; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (!alc1WeakRef.IsAlive && !alc2WeakRef.IsAlive && alcWeakRefs.All(w => !w.TryGetTarget(out _))) return;
            Thread.Sleep(50);
        }
        Assert.False(alc1WeakRef.IsAlive, "First ALC should be reclaimed after Quick Reload.");
        Assert.False(alc2WeakRef.IsAlive, "Second ALC should be reclaimed after second Quick Reload.");
    }

    // [NoInlining] confines all ALC-touching locals (including fixture) to this frame so
    // the GC loop in the [Fact] runs with no ALC-holding roots on the stack (DEBT-009).
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void QuickReload_AfterPreviousQuickReload_Body(
        out WeakReference alc1WeakRef, out WeakReference alc2WeakRef,
        out WeakReference<AssemblyLoadContext>[] alcWeakRefs)
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var v1 = TestData.LoadAsset(TestData.SampleAssets.LibraryMath);
        fixture.CompileAndLoad(v1);
        var alc1 = fixture.GetCurrentAlc();
        Assert.NotNull(alc1);
        alc1WeakRef = MakeWeakRef(alc1!);

        // Quick Reload 1.
        var v2 = TestData.LoadAsset(TestData.SampleAssets.MoveToAndFire);
        fixture.SimulateQuickReload(v2);
        var alc2 = fixture.GetCurrentAlc();
        Assert.NotSame(alc1, alc2);
        alc1 = null;

        // Quick Reload 2.
        alc2WeakRef = MakeWeakRef(alc2!);
        var v3 = TestData.LoadAsset(TestData.SampleAssets.HasVisibleTarget);
        fixture.SimulateQuickReload(v3);
        alc2 = null;
        alcWeakRefs = fixture.GetAlcWeakReferences().ToArray();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference MakeWeakRef(AssemblyLoadContext alc)
        => new WeakReference(alc);
}
