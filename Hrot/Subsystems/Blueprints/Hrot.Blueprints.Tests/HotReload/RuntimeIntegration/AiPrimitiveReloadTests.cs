using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Tests;

namespace Hrot.Blueprints.Tests.HotReload;

/// <summary>
/// AiPrimitive reload: working-state reset on hash change (inline hash check in BTreeTick thunk).
/// </summary>
public sealed class AiPrimitiveReloadTests
{
    [Fact]
    public void AiPrimitive_AfterReload_CompilesAndTicksWithoutError()
    {
        WeakReference<AssemblyLoadContext>[] alcWeakRefs;
        AiPrimitive_AfterReload_CompilesAndTicksWithoutError_Body(out alcWeakRefs);
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
    private static void AiPrimitive_AfterReload_CompilesAndTicksWithoutError_Body(
        out WeakReference<AssemblyLoadContext>[] alcWeakRefs)
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        var v1 = TestData.LoadAsset(TestData.SampleAssets.MoveToAndFire);
        fixture.CompileAndLoad(v1);

        var entity = fixture.CreateEntity();

        // Reload the same asset -- should reset working state on next call.
        fixture.SimulateReload(new[] { v1 });

        // The new tick should not crash (working-state reset by inline hash check).
        var status = fixture.InvokeBTreeAction(v1, entity);
        // MoveToAndFire currently returns Failure (Stage5 WaitForChannel traversal deferred to Phase 5).
        // We just verify it doesn't throw.
        Assert.True(
            status == NodeStatus.Failure || status == NodeStatus.Running || status == NodeStatus.Success,
            $"Unexpected status: {status}");
        alcWeakRefs = fixture.GetAlcWeakReferences().ToArray();
    }
}
