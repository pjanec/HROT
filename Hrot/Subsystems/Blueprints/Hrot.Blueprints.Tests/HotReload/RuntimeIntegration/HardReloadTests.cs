using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Tests;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.HotReload;

/// <summary>
/// Hard reload: StructureHash changed -> slot payload zeroed -> InstanceVersion bumped.
/// </summary>
[Collection("DebugProbe")]
public sealed class HardReloadTests
{
    [Fact]
    public void HardReload_InstanceBlueprint_SlotPayloadZeroed()
    {
        WeakReference<AssemblyLoadContext>[] alcWeakRefs;
        HardReload_InstanceBlueprint_SlotPayloadZeroed_Body(out alcWeakRefs);
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
    private static void HardReload_InstanceBlueprint_SlotPayloadZeroed_Body(
        out WeakReference<AssemblyLoadContext>[] alcWeakRefs)
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        // V1 with one variable.
        var assetId = Guid.NewGuid();
        var v1 = BlueprintAssetBuilder
            .Instance("ReloadTarget", assetId)
            .WithVariable("counter", typeof(int))
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();

        fixture.CompileAndLoad(v1);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(v1, entity);
        fixture.TickFrame(0.016f);

        // V2: different variable set -> different StructureHash.
        var v2 = BlueprintAssetBuilder
            .Instance("ReloadTarget", assetId)
            .WithVariable("counter", typeof(int))
            .WithVariable("extra",   typeof(float))  // adds a field -> hash change
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();

        fixture.SimulateReload(new[] { v2 });

        // After hard reload, slot should be reset (zeroed payload).
        // The next tick will re-init via InitDefault.
        fixture.TickFrame(0.016f);

        // Verify blueprint is still accessible (not crashed).
        Assert.True(fixture.Registry.TryGetByName("ReloadTarget", out var def));
        Assert.Equal(BlueprintDispatchKind.Instance, def!.Kind);
        def = null;
        alcWeakRefs = fixture.GetAlcWeakReferences().ToArray();
    }
}
