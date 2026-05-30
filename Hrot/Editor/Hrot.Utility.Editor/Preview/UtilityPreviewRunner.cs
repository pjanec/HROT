using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Utility;
using Hrot.Utility.Editor.Model;

namespace Hrot.Utility.Editor.Preview;

/// <summary>
/// Converts a <see cref="UtilityDecisionAsset"/> to a runtime <see cref="UtilityDecisionDef"/>,
/// evaluates it via <see cref="UtilityScorer"/>, and returns per-consideration trace data.
/// The output is byte-identical to a direct UtilityScorer.Evaluate call on the same def (SC-P5-2).
/// </summary>
public static class UtilityPreviewRunner
{
    /// <summary>
    /// Evaluates <paramref name="asset"/> using the real runtime scorer and returns
    /// per-consideration scores together with the top-ranked option score.
    /// Pass <c>null</c>/<c>default</c> for repo/self/context when readers do not need ECS data.
    /// </summary>
    public static unsafe UtilityPreviewResult Evaluate(
        UtilityDecisionAsset asset,
        EntityRepository? repo    = null,
        Entity            self    = default,
        Entity            context = default)
    {
        UtilityDecisionDef           def          = BuildDef(asset);
        UtilityResultBuffer          resultBuffer = default;
        UtilityTraceWorkingMemory1024 traceMem    = default;

        // repo! — null is acceptable when readers do not access the repository.
        UtilityScorer.Evaluate(repo!, self, in def, context, ref resultBuffer, &traceMem);

        List<UtilityPreviewConsiderationScore> scores = ReadTraceScores(ref traceMem);
        float topScore = resultBuffer.Count > 0 ? resultBuffer.GetSpanRO()[0].Score : 0f;
        return new UtilityPreviewResult(scores, topScore, resultBuffer.Count);
    }

    // ---- Internal helpers ----------------------------------------------

    private static UtilityDecisionDef BuildDef(UtilityDecisionAsset asset)
    {
        var options = new UtilityOption[asset.Options.Count];
        for (int i = 0; i < asset.Options.Count; i++)
        {
            OptionModel opt  = asset.Options[i];
            var         cons = new UtilityConsideration[opt.Considerations.Count];
            for (int j = 0; j < opt.Considerations.Count; j++)
            {
                ConsiderationModel con = opt.Considerations[j];
                cons[j] = new UtilityConsideration(
                    ComputeInputId(con.InputName),
                    con.Context,
                    con.Weight,
                    con.Curve.ToRuntime(),
                    new InputParams
                    {
                        BlueprintId = con.Params.BlueprintId,
                        MaxRange    = con.Params.MaxRange,
                        MountIndex  = con.Params.MountIndex,
                    });
            }
            options[i] = new UtilityOption
            {
                OptionId       = opt.OptionId,
                Mode           = opt.Mode,
                Considerations = cons,
            };
        }

        return new UtilityDecisionDef
        {
            BlueprintId = 0,
            DebugName   = asset.DisplayName,
            Kind        = asset.DecisionKind,
            Options     = options,
        };
    }

    private static ushort ComputeInputId(string inputName) =>
        (ushort)(In.Fnv1a32(inputName) & 0xFFFF);

    private static List<UtilityPreviewConsiderationScore> ReadTraceScores(
        ref UtilityTraceWorkingMemory1024 trace)
    {
        var list = new List<UtilityPreviewConsiderationScore>(trace.RecordCount);
        for (int i = 0; i < trace.RecordCount; i++)
        {
            trace.ReadRecord(i, out UtilityTraceRecord rec);
            if (rec.OpCode == UtilityTraceOpCode.Consideration)
            {
                list.Add(new UtilityPreviewConsiderationScore(
                    rec.OptionIndex, rec.InputId, rec.RawValue,
                    rec.CurveOutput, rec.Weight, rec.RunningAggregate));
            }
        }
        return list;
    }
}
