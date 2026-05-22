using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Hrot.Blueprints.Tests;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.HotReload;

/// <summary>
/// Latent cursor: soft reload resumes cleanly; hard reload resets cursor to ResumeAt=0.
/// </summary>
public sealed class LatentCursorReloadTests
{
    [Fact]
    public void HardReload_InstanceBlueprint_NextTickDoesNotCrash()
    {
        WeakReference<AssemblyLoadContext>[] alcWeakRefs;
        HardReload_InstanceBlueprint_NextTickDoesNotCrash_Body(out alcWeakRefs);
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
    private static void HardReload_InstanceBlueprint_NextTickDoesNotCrash_Body(
        out WeakReference<AssemblyLoadContext>[] alcWeakRefs)
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        // Build a simple Instance blueprint.
        var assetId = Guid.NewGuid();
        var v1 = BlueprintAssetBuilder
            .Instance("CursorTest", assetId)
            .WithVariable("x", typeof(int))
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();

        fixture.CompileAndLoad(v1);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(v1, entity);
        fixture.TickFrame(0.016f);

        // Hard reload (add variable changes hash).
        var v2 = BlueprintAssetBuilder
            .Instance("CursorTest", assetId)
            .WithVariable("x", typeof(int))
            .WithVariable("y", typeof(int))
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();

        fixture.SimulateReload(new[] { v2 });

        // Next tick after hard reload must not crash.
        fixture.TickFrame(0.016f);

        Assert.True(fixture.Registry.TryGetByName("CursorTest", out var cdef));
        Assert.NotNull(cdef);
        cdef = null;
        alcWeakRefs = fixture.GetAlcWeakReferences().ToArray();
    }
}
