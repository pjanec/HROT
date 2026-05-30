using Hrot.Utility.Editor.Emit;
using Hrot.Utility.Editor.Model;

namespace Hrot.Utility.Editor.Comparison;

/// <summary>
/// A single parameter-level change between two versions of a consideration.
/// </summary>
public sealed class TuningParamDiff
{
    public string OptionVisualId        { get; }
    public string ConsiderationVisualId { get; }
    /// <summary>The InputName of the consideration, for display.</summary>
    public string ConsiderationName     { get; }
    /// <summary>"Weight", "Slope", "Exponent", "XShift", or "CurveKind".</summary>
    public string ParamLabel            { get; }
    public float  OldValue              { get; }
    public float  NewValue              { get; }

    public TuningParamDiff(string optVid, string conVid, string conName,
                           string paramLabel, float old, float @new)
    {
        OptionVisualId        = optVid;
        ConsiderationVisualId = conVid;
        ConsiderationName     = conName;
        ParamLabel            = paramLabel;
        OldValue              = old;
        NewValue              = @new;
    }
}

/// <summary>
/// Result of a fast-lane tuning diff comparison.
/// </summary>
public sealed class TuningDiffResult
{
    /// <summary>True when both versions have the same StructureHash (option/consideration topology).</summary>
    public bool IsStructureEqual { get; }
    /// <summary>True when both structure AND params are identical (no change at all).</summary>
    public bool IsIdentical      { get; }
    /// <summary>Ordered list of per-consideration param changes.  Empty when IsIdentical.</summary>
    public IReadOnlyList<TuningParamDiff> Diffs { get; }

    public TuningDiffResult(bool structureEqual, bool identical, IReadOnlyList<TuningParamDiff> diffs)
    {
        IsStructureEqual = structureEqual;
        IsIdentical      = identical;
        Diffs            = diffs;
    }
}

/// <summary>
/// Performs a parameter-level diff between two UtilityDecisionAsset versions using
/// UtilityAssetHasher for fast structural equality check and per-field diff for param changes.
/// Design ref: Utility_AI_Editor_Design_v1_2.md SS10.2
/// </summary>
public static class UtilityTuningDiffEngine
{
    /// <summary>
    /// Compares two versions of a utility decision asset.
    /// Returns fast results when structure differs or assets are identical.
    /// Otherwise walks all considerations in deterministic VisualId order and collects diffs.
    /// </summary>
    public static TuningDiffResult Compute(UtilityDecisionAsset versionA, UtilityDecisionAsset versionB)
    {
        // 1. Check structural equality first
        int structA = UtilityAssetHasher.ComputeStructureHash(versionA);
        int structB = UtilityAssetHasher.ComputeStructureHash(versionB);
        if (structA != structB)
            return new TuningDiffResult(structureEqual: false, identical: false, Array.Empty<TuningParamDiff>());

        // 2. Check full equality (structure + params)
        int paramA = UtilityAssetHasher.ComputeParamHash(versionA);
        int paramB = UtilityAssetHasher.ComputeParamHash(versionB);
        if (paramA == paramB)
            return new TuningDiffResult(structureEqual: true, identical: true, Array.Empty<TuningParamDiff>());

        // 3. Walk matching options and considerations in deterministic order to collect diffs
        var diffs = new List<TuningParamDiff>();

        var optsA = versionA.Options.OrderBy(o => o.VisualId, StringComparer.Ordinal).ToList();
        var optsB = versionB.Options.OrderBy(o => o.VisualId, StringComparer.Ordinal).ToList();

        // Structures are equal so counts and VisualIds match
        for (int oi = 0; oi < optsA.Count; oi++)
        {
            var optA = optsA[oi];
            var optB = optsB[oi];

            var consA = optA.Considerations.OrderBy(c => c.VisualId, StringComparer.Ordinal).ToList();
            var consB = optB.Considerations.OrderBy(c => c.VisualId, StringComparer.Ordinal).ToList();

            for (int ci = 0; ci < consA.Count; ci++)
            {
                var conA = consA[ci];
                var conB = consB[ci];

                EmitIfDiff(diffs, optA.VisualId, conA.VisualId, conA.InputName, "Weight",    conA.Weight,   conB.Weight);
                EmitIfDiff(diffs, optA.VisualId, conA.VisualId, conA.InputName, "Slope",     conA.Curve.M,  conB.Curve.M);
                EmitIfDiff(diffs, optA.VisualId, conA.VisualId, conA.InputName, "Exponent",  conA.Curve.K,  conB.Curve.K);
                EmitIfDiff(diffs, optA.VisualId, conA.VisualId, conA.InputName, "XShift",    conA.Curve.B,  conB.Curve.B);

                if (conA.Curve.Kind != conB.Curve.Kind)
                    diffs.Add(new TuningParamDiff(
                        optA.VisualId, conA.VisualId, conA.InputName,
                        "CurveKind",
                        (float)(int)conA.Curve.Kind,
                        (float)(int)conB.Curve.Kind));
            }
        }

        return new TuningDiffResult(structureEqual: true, identical: false, diffs);
    }

    private static void EmitIfDiff(List<TuningParamDiff> diffs,
        string optVid, string conVid, string conName, string label, float old, float @new)
    {
        if (old != @new)
            diffs.Add(new TuningParamDiff(optVid, conVid, conName, label, old, @new));
    }
}
