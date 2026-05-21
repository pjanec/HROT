using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Tests;

namespace Hrot.Blueprints.Tests.HotReload;

/// <summary>
/// Patch 1: verifies that a failed reload does not mutate _currentAlc.
/// </summary>
public sealed class FailureRollbackTests
{
    [Fact]
    public void Reload_Failure_DoesNotMutateCurrentAlc()
    {
        WeakReference<AssemblyLoadContext>[] alcWeakRefs;
        Reload_Failure_DoesNotMutateCurrentAlc_Body(out alcWeakRefs);
        for (int i = 0; i < 20; i++)
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
    private static void Reload_Failure_DoesNotMutateCurrentAlc_Body(
        out WeakReference<AssemblyLoadContext>[] alcWeakRefs)
    {
        // Load a baseline blueprint so coordinator has a non-null currentAlc.
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var v1 = TestData.LoadAsset(TestData.SampleAssets.LibraryMath);
        fixture.CompileAndLoad(v1);

        var aliveAlcBefore = fixture.GetCurrentAlc();
        Assert.NotNull(aliveAlcBefore);

        // Simulate a reload that throws inside the registrar.
        var ex = Record.Exception(() => fixture.SimulateReloadWithThrowingRegistrar());

        // The exception should propagate.
        Assert.NotNull(ex);

        // _currentAlc must be unchanged after failure.
        var aliveAlcAfter = fixture.GetCurrentAlc();
        Assert.Same(aliveAlcBefore, aliveAlcAfter);
        aliveAlcBefore = null;
        aliveAlcAfter  = null;

        // Registry still has the original blueprint.
        Assert.True(fixture.Registry.TryGetByName("LibraryMath", out _));
        alcWeakRefs = fixture.GetAlcWeakReferences().ToArray();
    }

    [Fact]
    public void Reload_FailureThenSuccess_LiveCodeNeverInterrupted()
    {
        WeakReference<AssemblyLoadContext>[] alcWeakRefs;
        Reload_FailureThenSuccess_LiveCodeNeverInterrupted_Body(out alcWeakRefs);
        for (int i = 0; i < 20; i++)
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
    private static void Reload_FailureThenSuccess_LiveCodeNeverInterrupted_Body(
        out WeakReference<AssemblyLoadContext>[] alcWeakRefs)
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var v1 = TestData.LoadAsset(TestData.SampleAssets.LibraryMath);
        fixture.CompileAndLoad(v1);

        // Failed reload.
        var ex = Record.Exception(() => fixture.SimulateReloadWithThrowingRegistrar());
        Assert.NotNull(ex);

        // Original code still runs.
        Assert.True(fixture.Registry.TryGetByName("LibraryMath", out var def));
        Assert.Equal(BlueprintDispatchKind.Library, def!.Kind);

        // Successful reload with new blueprint.
        var v2 = TestData.LoadAsset(TestData.SampleAssets.MoveToAndFire);
        fixture.SimulateReload(new[] { v2 });

        Assert.True(fixture.Registry.TryGetByName("MoveToAndFire", out _));
        alcWeakRefs = fixture.GetAlcWeakReferences().ToArray();
    }
}
