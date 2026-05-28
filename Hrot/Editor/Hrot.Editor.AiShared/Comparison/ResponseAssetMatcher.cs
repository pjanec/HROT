namespace Hrot.Editor.AiShared.Comparison;

/// <summary>
/// Scores how well a parsed LLM comparison response matches the active asset's nodes.
/// Used to detect when the user pasted a response from the wrong asset. See design §7.6.
/// </summary>
public static class ResponseAssetMatcher
{
    /// <summary>
    /// Returns the fraction [0.0, 1.0] of non-null elementIds in the response that
    /// resolve against <paramref name="activeNodeIds"/>.
    /// Returns 1.0 (no mismatch) when the response has no non-null elementIds.
    /// </summary>
    public static double MatchScore(ComparisonResponse response, IReadOnlySet<string> activeNodeIds)
    {
        var candidates = response.Changes
            .Where(c => c.ElementId != null)
            .ToList();

        if (candidates.Count == 0)
            return 1.0;

        int matches = candidates.Count(c => activeNodeIds.Contains(c.ElementId!));
        return (double)matches / candidates.Count;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <see cref="MatchScore"/> is below 0.5,
    /// indicating that the majority of element references do not resolve and the
    /// response likely belongs to a different asset version.
    /// </summary>
    public static bool IsLikelyMismatch(ComparisonResponse response, IReadOnlySet<string> activeNodeIds)
        => MatchScore(response, activeNodeIds) < 0.5;
}
