using System;
using Fdp.Core;
using Fdp.Toolkit.Utility;
using Hrot.Utility.Editor.Model;
using Hrot.Utility.Editor.Preview;
using Xunit;

namespace Hrot.Utility.Editor.Tests.Preview;

/// <summary>
/// Tests for <see cref="UtilityPreviewRunner"/> (SC-P5-2).
/// Registers stub input readers in the range 200-220 to avoid collisions with other tests.
/// Global reader store is cleared in Dispose.
/// </summary>
public sealed class UtilityPreviewRunnerTests : IDisposable
{
    // ---- Stub readers --------------------------------------------------

    private static unsafe float Stub06(in UtilityInputCtx ctx)             => 0.6f;
    private static unsafe float StubZero(in UtilityInputCtx ctx)           => 0.0f;
    private static unsafe float StubAboveThreshold(in UtilityInputCtx ctx) => 0.7f;
    private static unsafe float StubBelowThreshold(in UtilityInputCtx ctx) => 0.3f;

    public void Dispose() => UtilityInputReaderStore.Clear();

    // ---- Helpers -------------------------------------------------------

    private static UtilityDecisionAsset BuildSingleConsiderationAsset(
        string inputName = "Constant",
        ResponseCurveModel? curve = null)
    {
        var asset = new UtilityDecisionAsset
        {
            DisplayName  = "PreviewTest",
            DecisionKind = DecisionKind.PostureSelect,
        };
        var option = new OptionModel { OptionId = 0, Mode = ScoringMode.WeightedProduct };
        option.Considerations.Add(new ConsiderationModel
        {
            InputName = inputName,
            Context   = InputContext.Self,
            Weight    = 1f,
            Curve     = curve ?? new ResponseCurveModel { Kind = CurveKind.Linear, M = 1f },
        });
        asset.Options.Add(option);
        return asset;
    }

    // ---- SC-P5-2: runner output byte-identical to direct scorer call ---

    [Fact]
    public unsafe void Evaluate_SingleConsideration_TopScoreMatchesDirectScorerCall()
    {
        UtilityInputReaderStore.Register(StandardInputIds.Constant, &Stub06);

        UtilityDecisionAsset asset = BuildSingleConsiderationAsset("Constant");
        UtilityPreviewResult runnerResult = UtilityPreviewRunner.Evaluate(asset);

        // Build an identical def manually and call the scorer directly.
        var def = new UtilityDecisionDef
        {
            DebugName = "PreviewTest",
            Kind      = DecisionKind.PostureSelect,
            Options   = new[]
            {
                new UtilityOption
                {
                    OptionId = 0,
                    Mode     = ScoringMode.WeightedProduct,
                    Considerations = new[]
                    {
                        new UtilityConsideration(
                            StandardInputIds.Constant,
                            InputContext.Self,
                            weight: 1f,
                            curve:  new ResponseCurve(CurveKind.Linear, slope: 1f, exponent: 1f, xShift: 0f))
                    }
                }
            }
        };

        var directBuffer = default(UtilityResultBuffer);
        UtilityScorer.Evaluate(null, default, in def, default, ref directBuffer, null);

        Assert.Equal(directBuffer.GetSpanRO()[0].Score, runnerResult.TopScore, precision: 5);
    }

    [Fact]
    public unsafe void Evaluate_SingleConsideration_ConsiderationScoreIsRecorded()
    {
        UtilityInputReaderStore.Register(StandardInputIds.Constant, &Stub06);

        UtilityDecisionAsset asset  = BuildSingleConsiderationAsset("Constant");
        UtilityPreviewResult result = UtilityPreviewRunner.Evaluate(asset);

        Assert.Equal(1, result.ConsiderationScores.Count);
        Assert.Equal(StandardInputIds.Constant, result.ConsiderationScores[0].InputId);
    }

    [Fact]
    public unsafe void Evaluate_MultipleConsiderations_AllRecorded()
    {
        UtilityInputReaderStore.Register(StandardInputIds.Constant,     &Stub06);
        UtilityInputReaderStore.Register(StandardInputIds.HaveLiveTarget, &Stub06);

        var asset  = new UtilityDecisionAsset
        {
            DisplayName  = "MultiTest",
            DecisionKind = DecisionKind.PostureSelect,
        };
        var option = new OptionModel { OptionId = 0, Mode = ScoringMode.WeightedProduct };
        option.Considerations.Add(new ConsiderationModel
        {
            InputName = "Constant",
            Context   = InputContext.Self,
            Weight    = 1f,
        });
        option.Considerations.Add(new ConsiderationModel
        {
            InputName = "HaveLiveTarget",
            Context   = InputContext.Self,
            Weight    = 1f,
        });
        asset.Options.Add(option);

        UtilityPreviewResult result = UtilityPreviewRunner.Evaluate(asset);

        Assert.Equal(2, result.ConsiderationScores.Count);
    }

    [Fact]
    public void Evaluate_EmptyOptions_TopScoreZero()
    {
        var asset = new UtilityDecisionAsset
        {
            DisplayName  = "EmptyTest",
            DecisionKind = DecisionKind.PostureSelect,
        };

        UtilityPreviewResult result = UtilityPreviewRunner.Evaluate(asset);

        Assert.Equal(0f, result.TopScore);
        Assert.Equal(0, result.OptionCount);
    }

    [Fact]
    public unsafe void Evaluate_CurveApplied_CurveOutputMatchesExpected()
    {
        // Step curve with xShift=0.5f: input < 0.5 => output 0.
        UtilityInputReaderStore.Register(StandardInputIds.Constant, &StubBelowThreshold);  // 0.3f

        var curve = new ResponseCurveModel { Kind = CurveKind.Step, B = 0.5f };
        UtilityDecisionAsset asset  = BuildSingleConsiderationAsset("Constant", curve);
        UtilityPreviewResult result = UtilityPreviewRunner.Evaluate(asset);

        Assert.Equal(1, result.ConsiderationScores.Count);
        Assert.Equal(0f, result.ConsiderationScores[0].CurveOutput);
    }

    [Fact]
    public unsafe void Evaluate_NullRepo_DoesNotThrow()
    {
        UtilityInputReaderStore.Register(StandardInputIds.Constant, &Stub06);

        UtilityDecisionAsset asset  = BuildSingleConsiderationAsset("Constant");
        UtilityPreviewResult result = UtilityPreviewRunner.Evaluate(asset, null);

        Assert.NotNull(result);
    }
}
