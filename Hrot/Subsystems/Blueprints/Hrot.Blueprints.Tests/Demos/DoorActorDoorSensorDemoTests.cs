using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Fdp.Toolkit.Blueprints;

namespace Hrot.Blueprints.Tests.Demos;

/// <summary>
/// DEMO-003: DoorActor + DoorSensor runtime integration demo tests.
/// Covers loading both assets into a single ALC, ALC GC reclaim, and registry verification.
/// </summary>
public sealed class DoorActorDoorSensorDemoTests
{
    // SC1: CompileAndLoadMany loads DoorActor and DoorSensor into a single ALC assembly.
    // The assembly must contain at least 2 generated types (one per blueprint).
    [Fact]
    public void DoorActor_And_DoorSensor_CompileAndLoadTogether()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var doorActor  = TestData.LoadAsset(TestData.SampleAssets.DoorActor);
        var doorSensor = TestData.LoadAsset(TestData.SampleAssets.DoorSensor);

        var assembly = fixture.CompileAndLoadMany(new[] { doorActor, doorSensor });

        Assert.NotNull(assembly);
        // Each asset emits at least a blueprint class and a registrar class.
        Assert.True(assembly.GetTypes().Length >= 2,
            $"Expected >= 2 generated types but found {assembly.GetTypes().Length}.");
    }

    // SC2: ALC is GC-reclaimed after both assets are reloaded and the fixture is disposed.
    [Fact]
    public void DoorActor_ALC_ReclaimedAfterReload()
    {
        WeakReference<AssemblyLoadContext>[] alcWeakRefs;
        DoorActor_ALC_ReclaimedAfterReload_Body(out alcWeakRefs);
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
    private static void DoorActor_ALC_ReclaimedAfterReload_Body(
        out WeakReference<AssemblyLoadContext>[] alcWeakRefs)
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var doorActor  = TestData.LoadAsset(TestData.SampleAssets.DoorActor);
        var doorSensor = TestData.LoadAsset(TestData.SampleAssets.DoorSensor);
        fixture.CompileAndLoadMany(new[] { doorActor, doorSensor });
        fixture.SimulateReload(new[] { doorActor, doorSensor });
        alcWeakRefs = fixture.GetAlcWeakReferences().ToArray();
    }

    // SC3: After CompileAndLoadMany, DoorActor is registered in the Registry with StateSize > 0
    // (it has the IsOpen bool variable which contributes to the state layout).
    [Fact]
    public void DoorActor_HasIsOpen_Variable_InRegistry()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var doorActor  = TestData.LoadAsset(TestData.SampleAssets.DoorActor);
        var doorSensor = TestData.LoadAsset(TestData.SampleAssets.DoorSensor);
        fixture.CompileAndLoadMany(new[] { doorActor, doorSensor });

        var hash = BlueprintIdHash.Compute(doorActor.AssetId);
        Assert.True(fixture.Registry.TryGetById(hash, out var def),
            "DoorActor blueprint definition not found in registry after CompileAndLoadMany.");
        Assert.True(def!.StateSize > 0,
            $"Expected StateSize > 0 for DoorActor (has IsOpen variable) but got {def.StateSize}.");
    }

    // MANUAL WALKTHROUGH: DEMO-003
    // 1. Open Asset Browser -> double-click DoorActor.bp.json
    // 2. Verify Variables panel shows: IsOpen (bool, default false)
    // 3. Verify CustomEvents panel shows: OnDoorOpen (no parameters)
    // 4. Open DoorSensor.bp.json -> verify CallablePeers references DoorActor by GUID
    // 5. Note: Peer call tests (DoorSensor triggers DoorActor.IsOpen = true) require
    //    graph nodes in DoorActor/DoorSensor assets -- deferred to when graph authoring is complete
    // 6. Load both assets together via CompileAndLoadMany to share a single ALC
    // 7. Trigger a Quick Reload for both simultaneously; verify both re-register in the same ALC
}
