using System;
using Fdp.Core;
using Fdp.Toolkit.Utility;
using Xunit;

namespace Fdp.Toolkit.Tests
{
    /// <summary>
    /// Unit tests for <see cref="UtilityScorer"/> (TASK-UAI-P1-05 success criteria).
    /// </summary>
    /// <remarks>
    /// Each test registers stub readers via <see cref="UtilityInputReaderStore"/> and
    /// clears them in Dispose to prevent cross-test pollution of the static registry.
    /// </remarks>
    public sealed class UtilityScorerTests : IDisposable
    {
        // ── Shared stub state for MountIndex-parametrized reader ──
        // s_slotScores[i] is returned by SlotReader when ctx.Params.MountIndex == i.
        private static readonly float[] s_slotScores = new float[32];

        public UtilityScorerTests()
        {
            UtilityInputReaderStore.Clear();
        }

        public void Dispose()
        {
            UtilityInputReaderStore.Clear();
        }

        // ── Stub readers ───────────────────────────────────────────────────────────

        private static unsafe float Stub09(in UtilityInputCtx ctx) => 0.9f;
        private static unsafe float Stub06(in UtilityInputCtx ctx) => 0.6f;
        private static unsafe float Stub07(in UtilityInputCtx ctx) => 0.7f;
        private static unsafe float Stub075(in UtilityInputCtx ctx) => 0.75f;
        private static unsafe float Stub08(in UtilityInputCtx ctx) => 0.80f;
        private static unsafe float Stub03(in UtilityInputCtx ctx) => 0.3f;
        private static unsafe float Stub00(in UtilityInputCtx ctx) => 0.0f;
        // Reader using ctx.Params.MountIndex as an array index into s_slotScores.
        private static unsafe float SlotReader(in UtilityInputCtx ctx) => s_slotScores[ctx.Params.MountIndex];

        // ── SC-P1-05-1: Step curve below threshold returns 0; option score is 0 ──

        [Fact]
        public unsafe void Evaluate_StepCurveBelowThreshold_OptionScoreIsZero()
        {
            // Reader 10 → 0.9f  (option 0: above any threshold)
            // Reader 11 → 0.5f  (option 1: below Step threshold of 0.6)
            // Reader 12 → 0.3f  (option 2: linear, non-zero)
            UtilityInputReaderStore.Register(10, &Stub09);
            UtilityInputReaderStore.Register(11, (delegate*<in UtilityInputCtx, float>)&HalfReader);
            UtilityInputReaderStore.Register(12, &Stub03);

            var def = new UtilityDecisionDef
            {
                DebugName = "TestDef",
                Kind      = DecisionKind.ThreatRanking,
                Options   = new[]
                {
                    new UtilityOption
                    {
                        OptionId = 0, Mode = ScoringMode.WeightedProduct,
                        Considerations = new[]
                        {
                            new UtilityConsideration(10, InputContext.Self, weight: 1f,
                                curve: new ResponseCurve(CurveKind.Linear, slope: 1f))
                        }
                    },
                    new UtilityOption
                    {
                        OptionId = 1, Mode = ScoringMode.WeightedProduct,
                        Considerations = new[]
                        {
                            // Step curve: threshold at XShift=0.6. Reader returns 0.5 < 0.6 → output 0.
                            new UtilityConsideration(11, InputContext.Self, weight: 1f,
                                curve: new ResponseCurve(CurveKind.Step, xShift: 0.6f))
                        }
                    },
                    new UtilityOption
                    {
                        OptionId = 2, Mode = ScoringMode.WeightedProduct,
                        Considerations = new[]
                        {
                            new UtilityConsideration(12, InputContext.Self, weight: 1f,
                                curve: new ResponseCurve(CurveKind.Linear, slope: 1f))
                        }
                    }
                }
            };

            var output = default(UtilityResultBuffer);
            UtilityScorer.Evaluate(null, default, in def, default, ref output, null);

            // Option 1 (Step below threshold) must score 0.
            // After ranking, the zero-score option is last.
            Assert.Equal(3, output.Count);
            // Find the zero-score entry.
            bool foundZero = false;
            for (int i = 0; i < output.Count; i++)
            {
                if (output.GetSpanRO()[i].Score == 0f)
                {
                    foundZero = true;
                    // The OptionId of the zero-score entry should be 1.
                    Assert.Equal(1, output.GetSpanRO()[i].WinningPostureId);
                }
            }
            Assert.True(foundZero, "Expected at least one option to score exactly 0.");

            // Top two are non-zero.
            Assert.NotEqual(0f, output.GetSpanRO()[0].Score);
            Assert.NotEqual(0f, output.GetSpanRO()[1].Score);
        }

        // Reader for stub returning 0.5.
        private static unsafe float HalfReader(in UtilityInputCtx ctx) => 0.5f;

        // ── SC-P1-05-2: Ranked order and RunnerUpMargin ──

        [Fact]
        public unsafe void Evaluate_ThreeOptions_SortedDescendingWithCorrectMargin()
        {
            // Register 3 readers returning 0.9, 0.6, 0.3 (single Linear consideration each).
            UtilityInputReaderStore.Register(20, &Stub09);
            UtilityInputReaderStore.Register(21, &Stub06);
            UtilityInputReaderStore.Register(22, &Stub03);

            var def = BuildSimpleDef(DecisionKind.ThreatRanking,
                (20, 0.9f), (21, 0.6f), (22, 0.3f));

            var output = default(UtilityResultBuffer);
            UtilityScorer.Evaluate(null, default, in def, default, ref output, null);

            Assert.Equal(3, output.Count);

            var ro = output.GetSpanRO();
            Assert.Equal(0.9f, ro[0].Score, precision: 5);
            Assert.Equal(0.6f, ro[1].Score, precision: 5);
            Assert.Equal(0.3f, ro[2].Score, precision: 5);

            // RunnerUpMargin = score[0] - score[1] = 0.3.
            Assert.Equal(0.3f, output.RunnerUpMargin, precision: 5);
        }

        // ── SC-P1-05-3a: Hysteresis hold (bonus keeps active option on top) ──

        [Fact]
        public unsafe void SelectPosture_HysteresisHoldsActiveWhenBonusBridgesGap()
        {
            // A = 0.70 (active), B = 0.75. bonus = 0.08. A+bonus = 0.78 > 0.75 → A wins.
            UtilityInputReaderStore.Register(30, &Stub07);   // option A
            UtilityInputReaderStore.Register(31, &Stub075);  // option B

            var def = new UtilityDecisionDef
            {
                DebugName = "PostureDef",
                Kind      = DecisionKind.PostureSelect,
                Options   = new[]
                {
                    BuildSingleLinearOption(optionId: 0, inputId: 30),
                    BuildSingleLinearOption(optionId: 1, inputId: 31)
                }
            };

            var output = default(UtilityResultBuffer);
            byte winner = UtilityScorer.SelectPosture(
                null, default, in def,
                activePostureId: 0, hysteresisBonus: 0.08f,
                ref output, null);

            Assert.Equal(0, winner); // Option A holds
        }

        // ── SC-P1-05-3b: Hysteresis switch (bonus insufficient to hold active option) ──

        [Fact]
        public unsafe void SelectPosture_HysteresisSwitchesWhenGapExceedsBonus()
        {
            // A = 0.70 (active), B = 0.80. bonus = 0.08. A+bonus = 0.78 < 0.80 → B wins.
            UtilityInputReaderStore.Register(32, &Stub07);  // option A
            UtilityInputReaderStore.Register(33, &Stub08);  // option B

            var def = new UtilityDecisionDef
            {
                DebugName = "PostureDef2",
                Kind      = DecisionKind.PostureSelect,
                Options   = new[]
                {
                    BuildSingleLinearOption(optionId: 0, inputId: 32),
                    BuildSingleLinearOption(optionId: 1, inputId: 33)
                }
            };

            var output = default(UtilityResultBuffer);
            byte winner = UtilityScorer.SelectPosture(
                null, default, in def,
                activePostureId: 0, hysteresisBonus: 0.08f,
                ref output, null);

            Assert.Equal(1, winner); // Option B wins
        }

        // ── SC-P1-05-4: 16-option ThreatRanking — Count==16, sorted descending ──

        [Fact]
        public unsafe void Evaluate_16Options_CountIs16AndSortedDescending()
        {
            // One SlotReader registered for all options; each option's MountIndex carries its score index.
            UtilityInputReaderStore.Register(40, &SlotReader);

            // Assign distinct descending values so the sorted output can be predicted.
            // s_slotScores[i] = (16 - i) / 16f  → slot 0 = 1.0, slot 15 = 1/16.
            for (int i = 0; i < 16; i++)
                s_slotScores[i] = (16 - i) / 16f;

            var options = new UtilityOption[16];
            for (int i = 0; i < 16; i++)
            {
                options[i] = new UtilityOption
                {
                    OptionId = (ushort)i,
                    Mode     = ScoringMode.WeightedProduct,
                    Considerations = new[]
                    {
                        new UtilityConsideration(40, InputContext.Candidate, weight: 1f,
                            curve: new ResponseCurve(CurveKind.Linear, slope: 1f),
                            @params: new InputParams { MountIndex = i })
                    }
                };
            }

            var def = new UtilityDecisionDef
            {
                DebugName = "ThreatRanking16",
                Kind      = DecisionKind.ThreatRanking,
                Options   = options
            };

            var output = default(UtilityResultBuffer);
            UtilityScorer.Evaluate(null, default, in def, default, ref output, null);

            Assert.Equal(16, output.Count);

            // Verify strictly descending order.
            var ro = output.GetSpanRO();
            for (int i = 0; i < 15; i++)
                Assert.True(ro[i].Score > ro[i + 1].Score,
                    $"Expected ro[{i}].Score ({ro[i].Score}) > ro[{i + 1}].Score ({ro[i + 1].Score})");
        }

        // ── SC-P1-05-5: Trace buffer records all considerations + winner ──

        [Fact]
        public unsafe void Evaluate_WithTrace_RecordsConsiderationsAndWinner()
        {
            // 2 options x 2 considerations = 4 consideration records + 1 winner = 5 total.
            UtilityInputReaderStore.Register(50, &Stub09);
            UtilityInputReaderStore.Register(51, &Stub06);

            var def = new UtilityDecisionDef
            {
                DebugName = "TracedDef",
                Kind      = DecisionKind.PostureSelect,
                Options   = new[]
                {
                    new UtilityOption
                    {
                        OptionId = 0, Mode = ScoringMode.WeightedProduct,
                        Considerations = new[]
                        {
                            new UtilityConsideration(50, InputContext.Self, weight: 1f,
                                curve: new ResponseCurve(CurveKind.Linear, slope: 1f)),
                            new UtilityConsideration(51, InputContext.Self, weight: 1f,
                                curve: new ResponseCurve(CurveKind.Linear, slope: 1f))
                        }
                    },
                    new UtilityOption
                    {
                        OptionId = 1, Mode = ScoringMode.WeightedProduct,
                        Considerations = new[]
                        {
                            new UtilityConsideration(50, InputContext.Self, weight: 1f,
                                curve: new ResponseCurve(CurveKind.Linear, slope: 1f)),
                            new UtilityConsideration(51, InputContext.Self, weight: 1f,
                                curve: new ResponseCurve(CurveKind.Linear, slope: 1f))
                        }
                    }
                }
            };

            var traceMem = default(UtilityTraceWorkingMemory1024);
            var output   = default(UtilityResultBuffer);
            UtilityScorer.Evaluate(null, default, in def, default, ref output, &traceMem, tick: 7);

            // 4 consideration records + 1 winner record = 5 total.
            Assert.Equal(5, traceMem.RecordCount);

            // Last record should be the winner.
            traceMem.ReadRecord(traceMem.RecordCount - 1, out var lastRec);
            Assert.Equal(UtilityTraceOpCode.Winner, lastRec.OpCode);
            Assert.Equal(7, lastRec.Tick);
        }

        // ── SC-P1-05-6: Empty option list produces Count == 0 ──

        [Fact]
        public unsafe void Evaluate_EmptyOptions_ProducesZeroCount()
        {
            var def    = new UtilityDecisionDef { DebugName = "Empty", Options = Array.Empty<UtilityOption>() };
            var output = default(UtilityResultBuffer);
            UtilityScorer.Evaluate(null, default, in def, default, ref output, null);
            Assert.Equal(0, output.Count);
            Assert.Equal(0f, output.RunnerUpMargin);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a decision def where each entry in <paramref name="options"/> is
        /// (inputId, ignored-score-constant). The options use Linear curve / WeightedProduct.
        /// </summary>
        private static UtilityDecisionDef BuildSimpleDef(DecisionKind kind, params (ushort inputId, float ignored)[] options)
        {
            var optList = new UtilityOption[options.Length];
            for (int i = 0; i < options.Length; i++)
            {
                optList[i] = new UtilityOption
                {
                    OptionId = (ushort)i,
                    Mode     = ScoringMode.WeightedProduct,
                    Considerations = new[]
                    {
                        new UtilityConsideration(options[i].inputId, InputContext.Self, weight: 1f,
                            curve: new ResponseCurve(CurveKind.Linear, slope: 1f))
                    }
                };
            }
            return new UtilityDecisionDef { DebugName = "TestDef", Kind = kind, Options = optList };
        }

        private static UtilityOption BuildSingleLinearOption(ushort optionId, ushort inputId)
        {
            return new UtilityOption
            {
                OptionId = optionId,
                Mode     = ScoringMode.WeightedProduct,
                Considerations = new[]
                {
                    new UtilityConsideration(inputId, InputContext.Self, weight: 1f,
                        curve: new ResponseCurve(CurveKind.Linear, slope: 1f))
                }
            };
        }
    }
}
