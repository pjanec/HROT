using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fdp.Toolkit.Utility;
using Hrot.Editor.AiShared.Emit;
using Hrot.Editor.AiShared.HotReload;
using Hrot.Utility.Editor.Emit;
using Hrot.Utility.Editor.Loading;
using Hrot.Utility.Editor.Model;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Hrot.Utility.Editor.Tests;

// Tests for UtilityFluentEmitter (Tasks SC-P5-1) and UtilityAssetHasher (hot-reload tier).

public class UtilityFluentEmitterTests
{
    // ---- Helper ----

    private static UtilityDecisionAsset MakeAsset(DecisionKind kind = DecisionKind.PostureSelect)
    {
        return new UtilityDecisionAsset
        {
            AssetId      = new Guid("3c6f9e42-5d10-6f3a-ac23-000000000001"),
            DisplayName  = "Combat Posture",
            DecisionKind = kind,
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
                            },
                            VisualId  = "aab",
                        }
                    }
                }
            }
        };
    }

    // ---- Determinism tests ----

    [Fact]
    public void Emit_SameModel_ByteIdentical_SecondEmit()
    {
        var emitter = new UtilityFluentEmitter();
        var asset   = MakeAsset();

        string first  = emitter.Emit(asset);
        string second = emitter.Emit(asset);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Emit_SortedByVisualId_WhenOptionsOutOfOrder()
    {
        var emitter = new UtilityFluentEmitter();
        var asset   = new UtilityDecisionAsset
        {
            AssetId      = Guid.NewGuid(),
            DisplayName  = "SortTest",
            DecisionKind = DecisionKind.PostureSelect,
            Options      = new List<OptionModel>
            {
                new OptionModel
                {
                    OptionId = 2, VisualId = "zzz",
                    Considerations = new List<ConsiderationModel>
                    {
                        new ConsiderationModel
                        {
                            InputName = "EnemyStrengthRatio",
                            Curve     = new ResponseCurveModel { Kind = CurveKind.Linear, M = 1f, K = 1f, B = 0f },
                            VisualId  = "z01",
                        }
                    }
                },
                new OptionModel
                {
                    OptionId = 1, VisualId = "aaa",
                    Considerations = new List<ConsiderationModel>
                    {
                        new ConsiderationModel
                        {
                            InputName = "HealthFraction",
                            Curve     = new ResponseCurveModel { Kind = CurveKind.Linear, M = 1f, K = 1f, B = 0f },
                            VisualId  = "a01",
                        }
                    }
                },
            }
        };

        string output = emitter.Emit(asset);

        int posAaa = output.IndexOf("HealthFraction",  StringComparison.Ordinal);
        int posZzz = output.IndexOf("EnemyStrengthRatio", StringComparison.Ordinal);

        Assert.True(posAaa < posZzz, "Option 'aaa' (HealthFraction) should appear before 'zzz' (EnemyStrengthRatio).");
    }

    [Fact]
    public void Emit_SortedByVisualId_ConsiderationsWithinOption()
    {
        var emitter = new UtilityFluentEmitter();
        var asset   = new UtilityDecisionAsset
        {
            AssetId      = Guid.NewGuid(),
            DisplayName  = "ConSortTest",
            DecisionKind = DecisionKind.PostureSelect,
            Options      = new List<OptionModel>
            {
                new OptionModel
                {
                    OptionId = 1, VisualId = "opt1",
                    Considerations = new List<ConsiderationModel>
                    {
                        new ConsiderationModel
                        {
                            InputName = "EnemyStrengthRatio",
                            Curve     = new ResponseCurveModel { Kind = CurveKind.Linear, M = 1f, K = 1f, B = 0f },
                            VisualId  = "zzz",
                        },
                        new ConsiderationModel
                        {
                            InputName = "HealthFraction",
                            Curve     = new ResponseCurveModel { Kind = CurveKind.Linear, M = 1f, K = 1f, B = 0f },
                            VisualId  = "aaa",
                        },
                    }
                }
            }
        };

        string output = emitter.Emit(asset);

        int posHealth = output.IndexOf("HealthFraction", StringComparison.Ordinal);
        int posEnemy  = output.IndexOf("EnemyStrengthRatio", StringComparison.Ordinal);

        Assert.True(posHealth < posEnemy, "Consideration 'aaa' (HealthFraction) should appear before 'zzz' (EnemyStrengthRatio).");
    }

    // ---- Header and attribute tests ----

    [Fact]
    public void Emit_Contains_EditorGeneratedMarker()
    {
        var output = new UtilityFluentEmitter().Emit(MakeAsset());
        Assert.Contains(FluentCSharpEmitterBase.EditorGeneratedMarker, output, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_Contains_AssetId_InHeader()
    {
        var asset  = MakeAsset();
        var output = new UtilityFluentEmitter().Emit(asset);
        Assert.Contains(asset.AssetId.ToString("D"), output, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_Contains_DisplayName_InAttribute()
    {
        var asset  = MakeAsset();
        var output = new UtilityFluentEmitter().Emit(asset);
        Assert.Contains($"displayName: \"{asset.DisplayName}\"", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_Contains_DecisionKind_InAttribute()
    {
        var asset  = MakeAsset(DecisionKind.ThreatRanking);
        var output = new UtilityFluentEmitter().Emit(asset);
        Assert.Contains("DecisionKind.ThreatRanking", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_Contains_Category_InAttribute()
    {
        var asset  = MakeAsset();
        var output = new UtilityFluentEmitter().Emit(asset);
        Assert.Contains($"category:    \"{asset.Category}\"", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_HysteresisBonus_NonZero_EmittedInAttribute()
    {
        var asset = MakeAsset(DecisionKind.PostureSelect);
        asset.HysteresisBonus = 0.25f;
        var output = new UtilityFluentEmitter().Emit(asset);
        Assert.Contains("hysteresisBonus:", output, StringComparison.Ordinal);
        Assert.Contains("0.25f", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_HysteresisBonus_Zero_NotEmitted()
    {
        var asset = MakeAsset();
        asset.HysteresisBonus = 0f;
        var output = new UtilityFluentEmitter().Emit(asset);
        Assert.DoesNotContain("hysteresisBonus:", output, StringComparison.Ordinal);
    }

    // ---- Build method tests ----

    [Fact]
    public void Emit_CandidateOption_ForThreatRankingDecision()
    {
        var asset  = MakeAsset(DecisionKind.ThreatRanking);
        var output = new UtilityFluentEmitter().Emit(asset);
        Assert.Contains(".CandidateOption(", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_NamedOption_ForPostureSelectDecision()
    {
        var asset  = MakeAsset(DecisionKind.PostureSelect);
        var output = new UtilityFluentEmitter().Emit(asset);
        Assert.Contains(".Option(1,", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_Consideration_WithLinearCurvePreset()
    {
        var asset = MakeAsset();
        asset.Options[0].Considerations[0].Curve = new ResponseCurveModel
        {
            Kind = CurveKind.Linear, M = 1f, K = 1f, B = 0f
        };
        var output = new UtilityFluentEmitter().Emit(asset);
        Assert.Contains("Curve.Linear", output, StringComparison.Ordinal);
        Assert.DoesNotContain("new ResponseCurve", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_Consideration_WithCustomCurve_EmitsNewResponseCurve()
    {
        var asset = MakeAsset();
        asset.Options[0].Considerations[0].Curve = new ResponseCurveModel
        {
            Kind = CurveKind.Linear, M = 0.5f, K = 2f, B = 0.1f
        };
        var output = new UtilityFluentEmitter().Emit(asset);
        Assert.Contains("new ResponseCurve(", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_Consideration_Weight_UsesRFormat()
    {
        // 0.8 cannot be represented exactly in IEEE 754 single precision;
        // R format round-trips the actual stored value.
        float weight = 0.8f;
        var asset = MakeAsset();
        asset.Options[0].Considerations[0].Weight = weight;
        var output = new UtilityFluentEmitter().Emit(asset);
        string expected = weight.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "f";
        Assert.Contains(expected, output, StringComparison.Ordinal);
    }

    // ---- OFX-015 / FIX2-021: Loader round-trip full structural equality ----

    /// <summary>
    /// Emits C# for a two-consideration model, writes it to a temp file, loads it back
    /// via UtilityAssetLoader, and verifies full structural equality in file-emission
    /// order (VisualId-sorted: "aaa" before "bbb") without any alphabetical re-sorting.
    /// </summary>
    [Fact]
    public void EmitAndRoundTrip_UtilityDecisionAsset_StructuralEquality()
    {
        // Arrange: build a model with 2 considerations using distinct inputs and contexts.
        // VisualIds control sort order (emitter sorts considerations by VisualId).
        var asset = new UtilityDecisionAsset
        {
            AssetId      = new Guid("00000000-0000-0000-0000-000000000099"),
            DisplayName  = "RoundTripTest",
            DecisionKind = DecisionKind.PostureSelect,
            Options = new List<OptionModel>
            {
                new OptionModel
                {
                    OptionId = 1,
                    VisualId = "opt1",
                    Mode     = ScoringMode.WeightedProduct,
                    Considerations = new List<ConsiderationModel>
                    {
                        new ConsiderationModel
                        {
                            InputName = "HealthFraction",
                            Context   = InputContext.Self,
                            Weight    = 0.8f,
                            VisualId  = "aaa",
                            Curve     = new ResponseCurveModel
                            {
                                Kind = CurveKind.InverseLinear, M = 1f, K = 1f, B = 0f,
                            },
                        },
                        new ConsiderationModel
                        {
                            InputName = "ThreatRange",
                            Context   = InputContext.Target,
                            Weight    = 1.2f,
                            VisualId  = "bbb",
                            Curve     = new ResponseCurveModel
                            {
                                Kind = CurveKind.Linear, M = 1f, K = 1f, B = 0f,
                            },
                        },
                    }
                }
            }
        };

        // Act: emit to temp file and load back.
        string emittedCode = new UtilityFluentEmitter().Emit(asset);
        string tempPath    = Path.Combine(Path.GetTempPath(),
            "fix2021_roundtrip_" + Guid.NewGuid().ToString("N") + ".cs");
        try
        {
            File.WriteAllText(tempPath, emittedCode, System.Text.Encoding.UTF8);
            var result = UtilityAssetLoader.Load(tempPath);
            var loaded = result.Asset;

            // Assert structural equality in file-emission order (VisualId-sorted: aaa < bbb).
            Assert.Equal(1, loaded.Options.Count);
            Assert.Equal((ushort)1,                  loaded.Options[0].OptionId);
            Assert.Equal(ScoringMode.WeightedProduct, loaded.Options[0].Mode);
            Assert.Equal(2,                           loaded.Options[0].Considerations.Count);

            // Consideration 0: HealthFraction (VisualId "aaa" → emitted first)
            var con0 = loaded.Options[0].Considerations[0];
            Assert.Equal("HealthFraction",    con0.InputName);
            Assert.Equal(InputContext.Self,   con0.Context);
            Assert.Equal(0.8f,               con0.Weight);
            Assert.Equal(CurveKind.InverseLinear, con0.Curve.Kind);

            // Consideration 1: ThreatRange (VisualId "bbb" → emitted second)
            var con1 = loaded.Options[0].Considerations[1];
            Assert.Equal("ThreatRange",       con1.InputName);
            Assert.Equal(InputContext.Target, con1.Context);
            Assert.Equal(1.2f,               con1.Weight);
            Assert.Equal(CurveKind.Linear,   con1.Curve.Kind);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}

// ---- Hot-reload classification tests ----

public class UtilityAssetHasherTests
{
    private static UtilityDecisionAsset MakeBase()
    {
        return new UtilityDecisionAsset
        {
            AssetId      = new Guid("3c6f9e42-5d10-6f3a-ac23-000000000001"),
            DisplayName  = "Combat Posture",
            DecisionKind = DecisionKind.PostureSelect,
            Category     = "Tactical/Posture",
            Options      = new List<OptionModel>
            {
                new OptionModel
                {
                    OptionId = 1,
                    Mode     = ScoringMode.WeightedProduct,
                    VisualId = "opt1",
                    Considerations = new List<ConsiderationModel>
                    {
                        new ConsiderationModel
                        {
                            InputName = "HealthFraction",
                            Context   = InputContext.Self,
                            Weight    = 0.8f,
                            Curve     = new ResponseCurveModel
                            {
                                Kind = CurveKind.InverseLinear, M = 1f, K = 1f, B = 0f
                            },
                            VisualId  = "con1",
                        }
                    }
                }
            }
        };
    }

    [Fact]
    public void Classify_LayoutChangeOnly_IsCosmetic()
    {
        var before = MakeBase();
        var after  = MakeBase();
        // Layout changes are not hashed by UtilityAssetHasher
        after.Layout.PinnedFixture = "SomeFixture";

        var tier = UtilityAssetHasher.Classify(before, after);

        Assert.Equal(HotReloadTier.Cosmetic, tier);
    }

    [Fact]
    public void Classify_WeightChange_IsSoft()
    {
        var before = MakeBase();
        var after  = MakeBase();
        after.Options[0].Considerations[0].Weight = 0.3f;

        var tier = UtilityAssetHasher.Classify(before, after);

        Assert.Equal(HotReloadTier.Soft, tier);
    }

    [Fact]
    public void Classify_AddOption_IsHard()
    {
        var before = MakeBase();
        var after  = MakeBase();
        after.Options.Add(new OptionModel
        {
            OptionId = 2,
            Mode     = ScoringMode.WeightedSum,
            VisualId = "opt2",
            Considerations = new List<ConsiderationModel>
            {
                new ConsiderationModel
                {
                    InputName = "EnemyStrengthRatio",
                    Curve     = new ResponseCurveModel { Kind = CurveKind.Linear, M = 1f, K = 1f, B = 0f },
                    VisualId  = "con2",
                }
            }
        });

        var tier = UtilityAssetHasher.Classify(before, after);

        Assert.Equal(HotReloadTier.Hard, tier);
    }

    [Fact]
    public void Classify_InputNameChange_IsHard()
    {
        var before = MakeBase();
        var after  = MakeBase();
        after.Options[0].Considerations[0].InputName = "EnemyStrengthRatio";

        var tier = UtilityAssetHasher.Classify(before, after);

        Assert.Equal(HotReloadTier.Hard, tier);
    }
}
