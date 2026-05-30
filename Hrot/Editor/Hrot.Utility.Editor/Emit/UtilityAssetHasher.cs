using System;
using System.Linq;
using Hrot.Editor.AiShared.HotReload;
using Hrot.Utility.Editor.Model;

namespace Hrot.Utility.Editor.Emit;

// Computes structure and parameter hashes for hot-reload classification.
// StructureHash covers option/consideration topology (kind, option count, consideration count,
// input names, contexts). ParamHash covers tunable parameter values (weights, curve params).
public static class UtilityAssetHasher
{
    // Computes the hash of option/consideration structure only.
    // Changes that affect StructureHash trigger HotReloadTier.Hard.
    public static int ComputeStructureHash(UtilityDecisionAsset asset)
    {
        var hc = new HashCode();
        hc.Add(asset.DecisionKind);
        foreach (var opt in SortedOptions(asset))
        {
            hc.Add(opt.VisualId);
            hc.Add(opt.Mode);
            foreach (var con in SortedConsiderations(opt))
            {
                hc.Add(con.VisualId);
                hc.Add(con.InputName);
                hc.Add(con.Context);
                hc.Add(con.Curve.Kind);
            }
        }
        return hc.ToHashCode();
    }

    // Computes the hash of tunable parameter values only.
    // Changes that affect ParamHash (but not StructureHash) trigger HotReloadTier.Soft.
    public static int ComputeParamHash(UtilityDecisionAsset asset)
    {
        var hc = new HashCode();
        hc.Add(asset.HysteresisBonus);
        foreach (var opt in SortedOptions(asset))
        foreach (var con in SortedConsiderations(opt))
        {
            hc.Add(con.Weight);
            hc.Add(con.Curve.M);
            hc.Add(con.Curve.K);
            hc.Add(con.Curve.B);
            hc.Add(con.Curve.C);
        }
        return hc.ToHashCode();
    }

    // Classifies the hot-reload tier by comparing before/after hashes.
    public static HotReloadTier Classify(
        UtilityDecisionAsset before, UtilityDecisionAsset after)
    {
        return HotReloadClassifier.Classify(
            ComputeStructureHash(before), ComputeStructureHash(after),
            ComputeParamHash(before),     ComputeParamHash(after));
    }

    private static System.Collections.Generic.IEnumerable<OptionModel> SortedOptions(
        UtilityDecisionAsset asset)
        => asset.Options.OrderBy(o => o.VisualId, StringComparer.Ordinal);

    private static System.Collections.Generic.IEnumerable<ConsiderationModel> SortedConsiderations(
        OptionModel opt)
        => opt.Considerations.OrderBy(c => c.VisualId, StringComparer.Ordinal);
}
