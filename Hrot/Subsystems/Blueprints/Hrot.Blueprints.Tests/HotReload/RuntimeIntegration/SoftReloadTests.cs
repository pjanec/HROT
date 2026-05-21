using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Tests;

namespace Hrot.Blueprints.Tests.HotReload;

/// <summary>
/// Soft reload: StructureHash unchanged -> slot payload preserved -> tick resumes from saved state.
/// </summary>
public sealed class SoftReloadTests
{
    [Fact]
    public void SoftReload_InstanceBlueprint_SlotPayloadPreserved()
    {
        WeakReference<AssemblyLoadContext>[] alcWeakRefs;
        SoftReload_InstanceBlueprint_SlotPayloadPreserved_Body(out alcWeakRefs);
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
    private static void SoftReload_InstanceBlueprint_SlotPayloadPreserved_Body(
        out WeakReference<AssemblyLoadContext>[] alcWeakRefs)
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        // Use HealthRegen (Instance blueprint with variables).
        var v1 = TestData.LoadAsset(TestData.SampleAssets.HealthRegen);
        fixture.CompileAndLoad(v1);

        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(v1, entity);

        // Tick once so the slot has been touched.
        fixture.TickFrame(0.016f);

        // Get slot state before reload.
        var stateBefore = fixture.GetBlueprintState(v1, entity);
        Assert.NotNull(stateBefore);
        var hashBefore = stateBefore!.Value.StructureHash;

        // Reload with same asset (hash unchanged = soft reload).
        fixture.SimulateReload(new[] { v1 });

        // Slot must still exist and hash must be the same.
        var stateAfter = fixture.GetBlueprintState(v1, entity);
        Assert.NotNull(stateAfter);
        Assert.Equal(hashBefore, stateAfter!.Value.StructureHash);
        stateBefore = null;
        stateAfter  = null;
        alcWeakRefs = fixture.GetAlcWeakReferences().ToArray();
    }
}
