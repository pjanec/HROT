using Hrot.Editor.AiShared.Comparison;

namespace Hrot.Editor.AiShared.Tests.Comparison;

public sealed class ResponseAssetMatcherTests
{
    // Builds a minimal ComparisonChange with the given elementId.
    private static ComparisonChange MakeChange(string? elementId) =>
        new(
            Kind: "node_added",
            ElementId: elementId,
            ElementDescription: "test element",
            Field: null,
            OldValue: null,
            NewValue: null,
            Severity: "behavior",
            Description: "test description");

    private static ComparisonResponse MakeResponse(params string?[] elementIds) =>
        new(
            HumanSummary: null,
            TopLevelSummary: "test",
            Changes: elementIds.Select(MakeChange).ToList(),
            Warnings: Array.Empty<string>());

    [Fact]
    public void MatchScore_AllResolve_Returns1_And_IsLikelyMismatch_False()
    {
        var response = MakeResponse("id-1", "id-2");
        var activeIds = new HashSet<string> { "id-1", "id-2", "id-3" };

        var score = ResponseAssetMatcher.MatchScore(response, activeIds);
        Assert.Equal(1.0, score);
        Assert.False(ResponseAssetMatcher.IsLikelyMismatch(response, activeIds));
    }

    [Fact]
    public void MatchScore_NoneResolve_Returns0_And_IsLikelyMismatch_True()
    {
        var response = MakeResponse("id-x", "id-y");
        var activeIds = new HashSet<string> { "id-1", "id-2" };

        var score = ResponseAssetMatcher.MatchScore(response, activeIds);
        Assert.Equal(0.0, score);
        Assert.True(ResponseAssetMatcher.IsLikelyMismatch(response, activeIds));
    }

    [Fact]
    public void MatchScore_HalfResolve_Returns0Point5_And_IsLikelyMismatch_False()
    {
        // 1 of 2 resolves => score = 0.5; threshold is < 0.5, so no mismatch.
        var response = MakeResponse("id-1", "id-x");
        var activeIds = new HashSet<string> { "id-1" };

        var score = ResponseAssetMatcher.MatchScore(response, activeIds);
        Assert.Equal(0.5, score, precision: 10);
        Assert.False(ResponseAssetMatcher.IsLikelyMismatch(response, activeIds));
    }

    [Fact]
    public void MatchScore_LessThanHalf_IsLikelyMismatch_True()
    {
        // 1 of 3 resolves => score ~0.333 < 0.5 => mismatch.
        var response = MakeResponse("id-1", "id-x", "id-y");
        var activeIds = new HashSet<string> { "id-1" };

        var score = ResponseAssetMatcher.MatchScore(response, activeIds);
        Assert.True(score < 0.5);
        Assert.True(ResponseAssetMatcher.IsLikelyMismatch(response, activeIds));
    }

    [Fact]
    public void MatchScore_AllNullElementIds_Returns1_NoMismatch()
    {
        // All changes are intent_shift with ElementId=null.
        var response = MakeResponse(null, null);
        var activeIds = new HashSet<string> { "id-1" };

        var score = ResponseAssetMatcher.MatchScore(response, activeIds);
        Assert.Equal(1.0, score);
        Assert.False(ResponseAssetMatcher.IsLikelyMismatch(response, activeIds));
    }

    [Fact]
    public void MatchScore_EmptyChanges_Returns1_NoMismatch()
    {
        var response = MakeResponse();
        var activeIds = new HashSet<string> { "id-1" };

        var score = ResponseAssetMatcher.MatchScore(response, activeIds);
        Assert.Equal(1.0, score);
        Assert.False(ResponseAssetMatcher.IsLikelyMismatch(response, activeIds));
    }
}
