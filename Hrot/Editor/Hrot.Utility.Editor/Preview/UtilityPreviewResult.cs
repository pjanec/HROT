using System.Collections.Generic;

namespace Hrot.Utility.Editor.Preview;

/// <summary>
/// Full result from a single preview evaluation pass.
/// Contains per-consideration scores and the top-ranked option score.
/// </summary>
public sealed class UtilityPreviewResult
{
    /// <summary>Per-consideration scores, in evaluation order (as recorded by the tracer).</summary>
    public IReadOnlyList<UtilityPreviewConsiderationScore> ConsiderationScores { get; }
    /// <summary>Score of the top-ranked option in the result buffer.</summary>
    public float TopScore    { get; }
    /// <summary>Number of options in the result buffer.</summary>
    public int   OptionCount { get; }

    public UtilityPreviewResult(
        IReadOnlyList<UtilityPreviewConsiderationScore> scores,
        float topScore,
        int optionCount)
    {
        ConsiderationScores = scores;
        TopScore            = topScore;
        OptionCount         = optionCount;
    }
}
