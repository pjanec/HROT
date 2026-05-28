using Hrot.Editor.AiShared.Comparison;

namespace Hrot.Editor.AiShared.Tests.Comparison;

public sealed class ComparisonSessionStateTests
{
    private static ComparisonResponse MakeResponse() =>
        new ComparisonResponse(null, "Summary.", Array.Empty<ComparisonChange>(), Array.Empty<string>());

    [Fact]
    public void DefaultEnabledSeverities_ContainsBehaviorFeatureRemovalTuning_NotCosmetic()
    {
        var state = new ComparisonSessionState(Guid.NewGuid(), MakeResponse());

        Assert.Contains("behavior", state.EnabledSeverities);
        Assert.Contains("feature", state.EnabledSeverities);
        Assert.Contains("removal", state.EnabledSeverities);
        Assert.Contains("tuning", state.EnabledSeverities);
        Assert.DoesNotContain("cosmetic", state.EnabledSeverities);
    }

    [Fact]
    public void ToggleSeverity_CosmeticDisabledByDefault_TogglesOnAfterFirstCall()
    {
        var state = new ComparisonSessionState(Guid.NewGuid(), MakeResponse());

        state.ToggleSeverity("cosmetic");

        Assert.Contains("cosmetic", state.EnabledSeverities);
    }

    [Fact]
    public void ToggleSeverity_BehaviorEnabled_TogglesOffAfterCall()
    {
        var state = new ComparisonSessionState(Guid.NewGuid(), MakeResponse());

        state.ToggleSeverity("behavior");

        Assert.DoesNotContain("behavior", state.EnabledSeverities);
    }

    [Fact]
    public void MarkStale_InitiallyFalse_TrueAfterCall()
    {
        var state = new ComparisonSessionState(Guid.NewGuid(), MakeResponse());

        Assert.False(state.IsStale);
        state.MarkStale();
        Assert.True(state.IsStale);
    }

    [Fact]
    public void Registry_SetAndGet_ReturnsSameInstance()
    {
        var registry = new ComparisonSessionRegistry();
        var assetId = Guid.NewGuid();
        var state = new ComparisonSessionState(assetId, MakeResponse());

        registry.SetSession(state);
        var retrieved = registry.GetSession(assetId);

        Assert.Same(state, retrieved);
    }

    [Fact]
    public void Registry_SetTwiceForSameId_ReturnsSecondInstance()
    {
        var registry = new ComparisonSessionRegistry();
        var assetId = Guid.NewGuid();
        var first = new ComparisonSessionState(assetId, MakeResponse());
        var second = new ComparisonSessionState(assetId, MakeResponse());

        registry.SetSession(first);
        registry.SetSession(second);

        Assert.Same(second, registry.GetSession(assetId));
    }

    [Fact]
    public void Registry_ClearSession_GetReturnsNull()
    {
        var registry = new ComparisonSessionRegistry();
        var assetId = Guid.NewGuid();
        var state = new ComparisonSessionState(assetId, MakeResponse());
        registry.SetSession(state);

        registry.ClearSession(assetId);

        Assert.Null(registry.GetSession(assetId));
    }
}
