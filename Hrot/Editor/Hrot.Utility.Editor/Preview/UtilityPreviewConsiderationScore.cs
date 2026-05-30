namespace Hrot.Utility.Editor.Preview;

/// <summary>
/// Per-consideration score breakdown from a single preview evaluation pass.
/// Data extracted from UtilityTraceWorkingMemory1024.
/// </summary>
public sealed class UtilityPreviewConsiderationScore
{
    /// <summary>Zero-based option index in the sorted option list.</summary>
    public int    OptionIndex      { get; }
    /// <summary>FNV-1a-16 of the input reader name.</summary>
    public ushort InputId          { get; }
    /// <summary>Raw value returned by the input reader.</summary>
    public float  RawValue         { get; }
    /// <summary>Curve output in [0,1].</summary>
    public float  CurveOutput      { get; }
    /// <summary>Consideration weight.</summary>
    public float  Weight           { get; }
    /// <summary>Running aggregate score after this consideration was applied.</summary>
    public float  RunningAggregate { get; }

    public UtilityPreviewConsiderationScore(
        int optionIndex, ushort inputId, float raw, float curveOut,
        float weight, float runningAggregate)
    {
        OptionIndex      = optionIndex;
        InputId          = inputId;
        RawValue         = raw;
        CurveOutput      = curveOut;
        Weight           = weight;
        RunningAggregate = runningAggregate;
    }
}
