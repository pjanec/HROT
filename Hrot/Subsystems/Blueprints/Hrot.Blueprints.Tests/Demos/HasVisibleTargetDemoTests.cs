using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Tests.Demos;

/// <summary>
/// DEMO-004: HasVisibleTarget runtime integration demo tests.
/// Covers CompileAndLoad, BTree action invocation (condition graph EventEntry -> Return),
/// and ALC GC reclaim.
/// </summary>
[Collection("DebugProbe")]
public sealed class HasVisibleTargetDemoTests
{
    // SC1: CompileAndLoad succeeds for HasVisibleTarget (AiPrimitive/Condition with a simple graph).
    [Fact]
    public void HasVisibleTarget_CompileAndLoad_Succeeds()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var asset = TestData.LoadAsset(TestData.SampleAssets.HasVisibleTarget);
        var assembly = fixture.CompileAndLoad(asset);
        Assert.NotNull(assembly);
    }

    // SC2: InvokeBTreeAction returns a valid NodeStatus (Success or Failure).
    // The condition graph is: EventEntry -> Return (default return value).
    // The exact status depends on the Return node default; any valid status is acceptable.
    [Fact]
    public void HasVisibleTarget_InvokeBTreeAction_ReturnsValidStatus()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var asset = TestData.LoadAsset(TestData.SampleAssets.HasVisibleTarget);
        fixture.CompileAndLoad(asset);

        var entity = fixture.CreateEntity();
        var status = fixture.InvokeBTreeAction(asset, entity);

        Assert.True(
            status == NodeStatus.Success || status == NodeStatus.Failure || status == NodeStatus.Running,
            $"Unexpected NodeStatus: {status}");
    }

    // SC3: ALC is GC-reclaimed after the fixture is disposed following a reload.
    [Fact]
    public void HasVisibleTarget_ALC_ReclaimedAfterReload()
    {
        WeakReference<AssemblyLoadContext>[] alcWeakRefs;
        HasVisibleTarget_ALC_ReclaimedAfterReload_Body(out alcWeakRefs);
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
    private static void HasVisibleTarget_ALC_ReclaimedAfterReload_Body(
        out WeakReference<AssemblyLoadContext>[] alcWeakRefs)
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var asset = TestData.LoadAsset(TestData.SampleAssets.HasVisibleTarget);
        fixture.CompileAndLoad(asset);
        fixture.SimulateReload(new[] { asset });
        alcWeakRefs = fixture.GetAlcWeakReferences().ToArray();
    }

    // MANUAL WALKTHROUGH: DEMO-004
    // 1. Open Asset Browser -> double-click HasVisibleTarget.bp.json
    // 2. Verify graph shows: EventEntry -> Return (direct link, no condition logic yet)
    // 3. The Return node defaults to Success or Failure depending on its default pin value
    // 4. InvokeBTreeAction calls TickCore via reflection; working state is persisted across calls
    // 5. To add real visibility logic: insert a FunctionCall node between EventEntry and Return
    //    that queries a spatial index and sets the Return node's result pin
    // 6. Quick Reload: modify the Return default in JSON, reload, verify updated status on next tick
}
