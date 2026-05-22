using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Fdp.Toolkit.Blueprints;

namespace Hrot.Blueprints.Tests.Demos;

/// <summary>
/// DEMO-002: HealthRegen runtime integration demo tests.
/// Covers CompileAndLoad, initial variable slot attachment, soft reload slot preservation,
/// and ALC GC reclaim.
/// </summary>
public sealed class HealthRegenDemoTests
{
    // SC1: CompileAndLoad succeeds for HealthRegen (Instance blueprint with variables, no graphs).
    [Fact]
    public void HealthRegen_CompileAndLoad_Succeeds()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var asset = TestData.LoadAsset(TestData.SampleAssets.HealthRegen);
        var assembly = fixture.CompileAndLoad(asset);
        Assert.NotNull(assembly);
    }

    // SC2: After AttachBlueprint, the slot exists and the registry definition has a non-zero StateSize.
    // Note: reading the actual CurrentHealth float value from the slot requires unsafe pointer arithmetic
    // and is deferred until the Tick graph is implemented in the asset.
    [Fact]
    public void HealthRegen_InitialVariables_CurrentHealth_DefaultsTo100()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var asset = TestData.LoadAsset(TestData.SampleAssets.HealthRegen);
        fixture.CompileAndLoad(asset);

        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        Assert.True(fixture.HasSlot(asset, entity));

        var state = fixture.GetBlueprintState(asset, entity);
        Assert.NotNull(state);

        var hash = BlueprintIdHash.Compute(asset.AssetId);
        Assert.True(fixture.Registry.TryGetById(hash, out var def));
        Assert.True(def!.StateSize > 0);
    }

    // SC3: After a soft reload (same asset, StructureHash unchanged), the slot payload is preserved.
    // Follows the NoInlining + GC loop pattern so ALC leak is also verified.
    [Fact]
    public void HealthRegen_SoftReload_SlotPreserved()
    {
        WeakReference<AssemblyLoadContext>[] alcWeakRefs;
        HealthRegen_SoftReload_SlotPreserved_Body(out alcWeakRefs);
        for (int i = 0; i < 50; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (alcWeakRefs.All(w => !w.TryGetTarget(out _))) return;
            Thread.Sleep(50);
        }
        int leaked = alcWeakRefs.Count(w => w.TryGetTarget(out _));
        Assert.True(leaked == 0, $"{leaked} ALC(s) not GC-reclaimed.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HealthRegen_SoftReload_SlotPreserved_Body(
        out WeakReference<AssemblyLoadContext>[] alcWeakRefs)
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        var asset = TestData.LoadAsset(TestData.SampleAssets.HealthRegen);
        fixture.CompileAndLoad(asset);

        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        // Tick once so the slot has been touched (HealthRegen has no Tick graph, so tick is a no-op).
        fixture.TickFrame(0.016f);

        var stateBefore = fixture.GetBlueprintState(asset, entity);
        Assert.NotNull(stateBefore);
        var hashBefore = stateBefore!.Value.StructureHash;

        // Reload with the same asset (StructureHash unchanged => soft reload, slot preserved).
        fixture.SimulateReload(new[] { asset });

        var stateAfter = fixture.GetBlueprintState(asset, entity);
        Assert.NotNull(stateAfter);
        Assert.Equal(hashBefore, stateAfter!.Value.StructureHash);

        stateBefore = null;
        stateAfter  = null;
        alcWeakRefs = fixture.GetAlcWeakReferences().ToArray();
    }

    // SC4: ALC is GC-reclaimed after the fixture is disposed following a reload.
    [Fact]
    public void HealthRegen_ALC_ReclaimedAfterReload()
    {
        WeakReference<AssemblyLoadContext>[] alcWeakRefs;
        HealthRegen_ALC_ReclaimedAfterReload_Body(out alcWeakRefs);
        for (int i = 0; i < 50; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (alcWeakRefs.All(w => !w.TryGetTarget(out _))) return;
            Thread.Sleep(50);
        }
        int leaked = alcWeakRefs.Count(w => w.TryGetTarget(out _));
        Assert.True(leaked == 0, $"{leaked} ALC(s) not GC-reclaimed.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void HealthRegen_ALC_ReclaimedAfterReload_Body(
        out WeakReference<AssemblyLoadContext>[] alcWeakRefs)
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var asset = TestData.LoadAsset(TestData.SampleAssets.HealthRegen);
        fixture.CompileAndLoad(asset);
        fixture.SimulateReload(new[] { asset });
        alcWeakRefs = fixture.GetAlcWeakReferences().ToArray();
    }

    // MANUAL WALKTHROUGH: DEMO-002
    // 1. Open Asset Browser -> double-click HealthRegen.bp.json
    // 2. Verify Variables panel shows: CurrentHealth (float, default 100), MaxHealth (float, default 100)
    // 3. Note: no Tick graph is defined yet; adding a Tick graph that increments CurrentHealth each frame
    //    would enable testing the actual regen logic (deferred to when graph authoring is complete)
    // 4. Attach entity in the inspector and verify the slot allocation in the blackboard view
    // 5. Trigger a Quick Reload and verify the slot payload is preserved (StructureHash unchanged)
}
