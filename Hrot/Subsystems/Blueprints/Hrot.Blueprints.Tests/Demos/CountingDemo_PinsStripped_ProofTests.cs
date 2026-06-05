using Fdp.Toolkit.Blueprints;

namespace Hrot.Blueprints.Tests.Demos;

/// <summary>
/// BP-2 keystone proof: CountingDemo.bp.json with ALL node Pins stripped to [] (mimicking
/// a blueprint saved projection-only and reloaded) must compile, load, and execute
/// identically to the pins-populated baseline — Count climbs to 5 after 5 ticks.
///
/// This proves Stage0_Rehydrate produces a CONNECTED graph (link GUIDs correctly
/// reassigned to rehydrated pins, so Stage4/Stage5 wire resolution succeeds).
/// </summary>
[Collection("DebugProbe")]
public sealed class CountingDemo_PinsStripped_ProofTests
{
    /// <summary>
    /// PROOF-CD-STRIPPED-001: After stripping all node Pins and attaching the blueprint,
    /// Count starts at 0 (default value) — the blueprint loads without errors.
    /// </summary>
    [Fact]
    public void CountingDemo_PinsStripped_AfterAttach_CountIsZero()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        var asset = TestData.LoadAsset(TestData.SampleAssets.CountingDemo);

        // Strip all node Pins — mimic a projection-only saved/reloaded blueprint.
        foreach (var graph in asset.Graphs)
            foreach (var node in graph.Nodes)
                node.Pins.Clear();

        fixture.CompileAndLoad(asset);

        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        var view = fixture.GetBlueprintState(asset, entity);
        Assert.NotNull(view);

        Assert.True(view!.Value.TryGetField<int>("Count", out var countBefore),
            "TryGetField returned false for 'Count' before any tick " +
            "(pins-stripped path). Rehydration may have failed to connect the graph.");
        Assert.Equal(0, countBefore);
    }

    /// <summary>
    /// PROOF-CD-STRIPPED-002: After stripping all node Pins and running 5 TickFrame calls,
    /// Count == 5 — proving Stage0_Rehydrate restores connectivity and the Tick graph
    /// (EventEntry → SetVariable ← AddInt(GetVariable, Literal(1))) executes correctly.
    /// This is the BP-2 keystone proof.
    /// </summary>
    [Fact]
    public void CountingDemo_PinsStripped_After5Ticks_CountEquals5()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        var asset = TestData.LoadAsset(TestData.SampleAssets.CountingDemo);

        // Strip all node Pins — mimic a projection-only saved/reloaded blueprint.
        // Stage0_Rehydrate must rebuild them (with correct link GUIDs) before the
        // rest of the compiler runs.
        foreach (var graph in asset.Graphs)
            foreach (var node in graph.Nodes)
                node.Pins.Clear();

        fixture.CompileAndLoad(asset);

        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        // Tick 5 frames
        for (int i = 0; i < 5; i++)
            fixture.TickFrame(0.016f);

        var view = fixture.GetBlueprintState(asset, entity);
        Assert.NotNull(view);

        Assert.True(view!.Value.TryGetField<int>("Count", out var count),
            "TryGetField returned false for 'Count' after 5 ticks (pins-stripped path) — " +
            "Stage0_Rehydrate did not restore pin-ID connectivity, so the Tick graph " +
            "produced an empty schedule (wires dangled).");
        Assert.Equal(5, count);
    }
}
