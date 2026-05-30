using Fdp.Toolkit.Utility;
using Hrot.Utility.Editor.Comparison;
using Hrot.Utility.Editor.Model;
using Xunit;

namespace Hrot.Utility.Editor.Tests.Comparison;

public sealed class UtilityTuningDiffEngineTests
{
    // ---- Helper ----

    private static UtilityDecisionAsset MakeAsset() => new UtilityDecisionAsset
    {
        AssetId      = new Guid("3c6f9e42-5d10-6f3a-ac23-000000000001"),
        DisplayName  = "Combat Posture",
        DecisionKind = DecisionKind.PostureSelect,
        Category     = "Tactical/Posture",
        Options      = new List<OptionModel>
        {
            new OptionModel
            {
                OptionId       = 1,
                Mode           = ScoringMode.WeightedProduct,
                VisualId       = "aaa",
                Considerations = new List<ConsiderationModel>
                {
                    new ConsiderationModel
                    {
                        InputName = "HealthFraction",
                        Context   = InputContext.Self,
                        Weight    = 0.8f,
                        Curve     = new ResponseCurveModel
                        {
                            Kind = CurveKind.InverseLinear,
                            M    = 1f,
                            K    = 1f,
                            B    = 0f,
                            C    = 0f,
                        },
                        VisualId  = "aab",
                    }
                }
            }
        }
    };

    // ---- Tests ----

    [Fact]
    public void Compute_IdenticalAssets_IsIdenticalTrue()
    {
        var a      = MakeAsset();
        var b      = MakeAsset();
        var result = UtilityTuningDiffEngine.Compute(a, b);

        Assert.True(result.IsIdentical);
        Assert.True(result.IsStructureEqual);
        Assert.Empty(result.Diffs);
    }

    [Fact]
    public void Compute_StructureDiffer_AddOption_IsStructureEqualFalse()
    {
        var a = MakeAsset();
        var b = MakeAsset();
        b.Options.Add(new OptionModel
        {
            OptionId       = 2,
            Mode           = ScoringMode.WeightedProduct,
            VisualId       = "bbb",
            Considerations = new List<ConsiderationModel>
            {
                new ConsiderationModel
                {
                    InputName = "HasLiveTarget",
                    Context   = InputContext.Self,
                    Weight    = 1f,
                    Curve     = new ResponseCurveModel { Kind = CurveKind.Linear },
                    VisualId  = "bbc",
                }
            }
        });

        var result = UtilityTuningDiffEngine.Compute(a, b);

        Assert.False(result.IsStructureEqual);
        Assert.False(result.IsIdentical);
    }

    [Fact]
    public void Compute_WeightChange_IsStructureEqualTrue_OneWeightDiff()
    {
        var a = MakeAsset();
        var b = MakeAsset();
        b.Options[0].Considerations[0].Weight = 0.5f;

        var result = UtilityTuningDiffEngine.Compute(a, b);

        Assert.True(result.IsStructureEqual);
        Assert.False(result.IsIdentical);
        var diff = Assert.Single(result.Diffs);
        Assert.Equal("Weight", diff.ParamLabel);
        Assert.Equal(0.8f, diff.OldValue);
        Assert.Equal(0.5f, diff.NewValue);
    }

    [Fact]
    public void Compute_CurveParamChange_SlopeAndExponent_TwoDiffs()
    {
        var a = MakeAsset();
        var b = MakeAsset();
        b.Options[0].Considerations[0].Curve.M = 2f;
        b.Options[0].Considerations[0].Curve.K = 0.5f;

        var result = UtilityTuningDiffEngine.Compute(a, b);

        Assert.True(result.IsStructureEqual);
        Assert.Equal(2, result.Diffs.Count);
        Assert.Contains(result.Diffs, d => d.ParamLabel == "Slope");
        Assert.Contains(result.Diffs, d => d.ParamLabel == "Exponent");
    }

    [Fact]
    public void Compute_CurveKindChange_OneDiff_LabelCurveKind()
    {
        var a = MakeAsset();
        var b = MakeAsset();
        b.Options[0].Considerations[0].Curve.Kind = CurveKind.Logistic;

        var result = UtilityTuningDiffEngine.Compute(a, b);

        Assert.True(result.IsStructureEqual);
        Assert.False(result.IsIdentical);
        var diff = Assert.Single(result.Diffs);
        Assert.Equal("CurveKind", diff.ParamLabel);
        Assert.Equal((float)(int)CurveKind.InverseLinear, diff.OldValue);
        Assert.Equal((float)(int)CurveKind.Logistic, diff.NewValue);
    }

    [Fact]
    public void Compute_DiffsOrderedByVisualId()
    {
        // Two considerations with VisualIds "aac" and "aab" (stored in reverse alphabetical order).
        // After sorting, diffs must appear with "aab" first.
        var cons = new List<ConsiderationModel>
        {
            new ConsiderationModel
            {
                InputName = "HealthFraction", Context = InputContext.Self,
                Weight    = 0.8f,
                Curve     = new ResponseCurveModel { Kind = CurveKind.InverseLinear, M = 1f, K = 1f },
                VisualId  = "aac",
            },
            new ConsiderationModel
            {
                InputName = "HasLiveTarget", Context = InputContext.Self,
                Weight    = 0.5f,
                Curve     = new ResponseCurveModel { Kind = CurveKind.Linear },
                VisualId  = "aab",
            },
        };

        var a = new UtilityDecisionAsset
        {
            AssetId      = Guid.Empty,
            DisplayName  = "Test",
            DecisionKind = DecisionKind.PostureSelect,
            Options      = new List<OptionModel>
            {
                new OptionModel
                {
                    OptionId       = 1,
                    Mode           = ScoringMode.WeightedProduct,
                    VisualId       = "aaa",
                    Considerations = cons,
                }
            }
        };

        var b = new UtilityDecisionAsset
        {
            AssetId      = Guid.Empty,
            DisplayName  = "Test",
            DecisionKind = DecisionKind.PostureSelect,
            Options      = new List<OptionModel>
            {
                new OptionModel
                {
                    OptionId       = 1,
                    Mode           = ScoringMode.WeightedProduct,
                    VisualId       = "aaa",
                    Considerations = new List<ConsiderationModel>
                    {
                        new ConsiderationModel
                        {
                            InputName = "HealthFraction", Context = InputContext.Self,
                            Weight    = 0.9f,   // changed
                            Curve     = new ResponseCurveModel { Kind = CurveKind.InverseLinear, M = 1f, K = 1f },
                            VisualId  = "aac",
                        },
                        new ConsiderationModel
                        {
                            InputName = "HasLiveTarget", Context = InputContext.Self,
                            Weight    = 0.6f,   // changed
                            Curve     = new ResponseCurveModel { Kind = CurveKind.Linear },
                            VisualId  = "aab",
                        },
                    }
                }
            }
        };

        var result = UtilityTuningDiffEngine.Compute(a, b);

        Assert.Equal(2, result.Diffs.Count);
        // Sorted by ConsiderationVisualId: "aab" < "aac"
        Assert.Equal("aab", result.Diffs[0].ConsiderationVisualId);
        Assert.Equal("aac", result.Diffs[1].ConsiderationVisualId);
    }
}
