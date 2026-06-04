using Fdp.Toolkit.Blueprints;

namespace Hrot.Blueprints.Tests.Demos;

/// <summary>
/// BATCH-05 proof test: CountingDemo.bp.json compiles, loads, attaches to an entity,
/// and its Tick graph increments Count by 1 per frame (observable via BlueprintStateView).
/// Mirrors StateFields_ProofTests PROOF-002 pattern.
/// </summary>
[Collection("DebugProbe")]
public sealed class CountingDemo_ProofTests
{
    /// <summary>
    /// PROOF-CD-001: After AttachBlueprint, Count starts at 0 (default value).
    /// </summary>
    [Fact]
    public void CountingDemo_AfterAttach_CountIsZero()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        var asset = TestData.LoadAsset(TestData.SampleAssets.CountingDemo);
        fixture.CompileAndLoad(asset);

        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        var view = fixture.GetBlueprintState(asset, entity);
        Assert.NotNull(view);

        Assert.True(view!.Value.TryGetField<int>("Count", out var countBefore),
            "TryGetField returned false for 'Count' before any tick.");
        Assert.Equal(0, countBefore);
    }

    /// <summary>
    /// PROOF-CD-002: After 5 TickFrame calls, Count == 5, proving the compiled
    /// Tick graph (EventEntry → SetVariable ← AddInt(GetVariable, Literal(1)))
    /// executes and is observable via BlueprintStateView (BATCH-04 field offset).
    /// </summary>
    [Fact]
    public void CountingDemo_After5Ticks_CountEquals5()
    {
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        var asset = TestData.LoadAsset(TestData.SampleAssets.CountingDemo);
        fixture.CompileAndLoad(asset);

        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        // Tick 5 frames
        for (int i = 0; i < 5; i++)
            fixture.TickFrame(0.016f);

        var view = fixture.GetBlueprintState(asset, entity);
        Assert.NotNull(view);

        Assert.True(view!.Value.TryGetField<int>("Count", out var count),
            "TryGetField returned false for 'Count' after 5 ticks — " +
            "StateFields offset or size mismatch, or Tick graph not executed.");
        Assert.Equal(5, count);
    }
}
