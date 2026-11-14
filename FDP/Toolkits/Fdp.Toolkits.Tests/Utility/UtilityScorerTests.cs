using System;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Utility;
using Hrot.AI.Behaviors.Brains;
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

        // ── ⭐ CE-2271 (R-2075) — a MODIFIER multiplies after the aggregate and is not counted by the compensation ──

        private static unsafe float Stub10(in UtilityInputCtx ctx) => 1.0f;
        private static unsafe float Stub05(in UtilityInputCtx ctx) => 0.5f;

        private static unsafe float ScoreOf(ScoringMode mode, params UtilityConsideration[] considerations)
        {
            var def = new UtilityDecisionDef
            {
                DebugName = "Modifier", Kind = DecisionKind.PostureSelect,
                Options = new[] { new UtilityOption { OptionId = 0, Mode = mode, Considerations = considerations } },
            };
            var output = default(UtilityResultBuffer);
            UtilityScorer.Evaluate(null, default, in def, default, ref output, null);
            return output.GetSpanRO()[0].Score;
        }

        /// <summary>⭐ <c>CE-2271</c> — a modifier at 1.0 leaves a product's score BIT-identical (an ordinary consideration at 1.0 does not —
        /// the compensation counts it: the whole reason the modifier exists); at 0.5 it halves the score, in either mode.</summary>
        [Fact]
        public unsafe void CE2271_AModifierAt1_LeavesTheScoreBitIdentical_AConsiderAt1_DoesNot_AndAt05_HalvesIt()
        {
            UtilityInputReaderStore.Register(60, &Stub09);
            UtilityInputReaderStore.Register(61, &Stub06);
            UtilityInputReaderStore.Register(62, &Stub10);
            UtilityInputReaderStore.Register(63, &Stub05);
            var curve = new ResponseCurve(CurveKind.Linear, slope: 1f);
            UtilityConsideration C(ushort id) => new(id, InputContext.Self, weight: 1f, curve: curve);
            UtilityConsideration M(ushort id) => new(id, InputContext.Self, weight: 1f, curve: curve, flags: ConsiderationFlags.Modifier);

            float plain = ScoreOf(ScoringMode.WeightedProduct, C(60), C(61));
            Assert.Equal(BitConverter.SingleToInt32Bits(plain), BitConverter.SingleToInt32Bits(ScoreOf(ScoringMode.WeightedProduct, C(60), C(61), M(62))));
            Assert.NotEqual(plain, ScoreOf(ScoringMode.WeightedProduct, C(60), C(61), C(62)));          // the count moved the compensation
            Assert.Equal(plain * 0.5f, ScoreOf(ScoringMode.WeightedProduct, C(60), C(61), M(63)), 6);
            Assert.Equal(plain * 0.5f, ScoreOf(ScoringMode.WeightedProduct, M(63), C(60), C(61)), 6);   // order does not matter

            float sum = ScoreOf(ScoringMode.WeightedSum, C(60), C(61));
            Assert.Equal(sum * 0.5f, ScoreOf(ScoringMode.WeightedSum, C(60), C(61), M(63)), 6);
            Assert.Equal(BitConverter.SingleToInt32Bits(sum), BitConverter.SingleToInt32Bits(ScoreOf(ScoringMode.WeightedSum, C(60), M(62), C(61))));
        }

        /// <summary>⭐ <c>CE-2271</c> — the builder's <c>Modify</c> writes a modifier row; <c>Consider</c> an ordinary one.</summary>
        [Fact]
        public void CE2271_TheBuilder_ModifyMarksTheRow()
        {
            var b = new UtilityDecisionBuilder();
            b.Option(0, ScoringMode.WeightedProduct, o => o.Consider(In.Constant(0.5f), 1f, Curve.Linear).Modify(In.Constant(1f), 1f, Curve.Linear));
            var def = b.Build(new UtilityDecisionAttribute("ce2271-modify", "Modify", DecisionKind.PostureSelect));
            Assert.Equal(ConsiderationFlags.None, def.Options[0].Considerations[0].Flags);
            Assert.Equal(ConsiderationFlags.Modifier, def.Options[0].Considerations[1].Flags);
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

        // ── ⭐ CE-2067 — the scorer core without the unit buffer (DESIGN_Decision_Layer.md §3.3) ──

        private static Entity s_favoured;
        private static unsafe float FavouredReader(in UtilityInputCtx ctx) => ctx.Context.Equals(s_favoured) ? 0.9f : 0.2f;

        // ── ⭐ CE-2069 — the utility nodes switch a BTree branch on the winner (DESIGN_Decision_Layer.md §3.3) ──────

        [StructLayout(LayoutKind.Sequential)]
        private struct Ran { public int N; }

        [StructLayout(LayoutKind.Sequential)]
        private struct PostureBlackboard
        {
            public ChooseOptionParams Choose;
            public UtilityChoice      Choice;   // ⭐ the ONE working state ChooseOption writes and both guards read
            public IsOptionParams     IsOne;
            public IsOptionParams     IsTwo;
            public Ran                One;
            public Ran                Two;
        }

        private static float s_one, s_two;
        private static unsafe float OneReader(in UtilityInputCtx ctx) => s_one;
        private static unsafe float TwoReader(in UtilityInputCtx ctx) => s_two;
        private static NodeStatus Run(ref Ran p, Entity self, EntityRepository world) { p.N++; return NodeStatus.Running; }

        /// <summary>
        /// ⭐⭐ <c>CE-2069</c> — a tree <c>Parallel[ ChooseOption, ObserverSelector[ IsOption(1) → one, IsOption(2) → two ] ]</c>
        /// runs the winner's branch and SWITCHES when the scores move past the hysteresis; the guards read the winner the
        /// action wrote, through one shared working-state field. (The Parallel-repeat shape §3.2 assumed, measured here.)
        /// </summary>
        [Fact]
        public unsafe void CE2069_UtilityNodes_SwitchTheBranch_WhenTheWinnerChanges()
        {
            UtilityDecisionCatalog.EnsureRegistered();
            const int id = 0x20690001;
            UtilityDecisionCatalog.Shared.Register(id, new UtilityDecisionDef
            {
                DebugName = "CE2069Posture", Kind = DecisionKind.PostureSelect,
                Options = new[] { BuildSingleLinearOption(optionId: 1, inputId: 2069), BuildSingleLinearOption(optionId: 2, inputId: 2070) },
            }, hysteresisBonus: 0.08f);
            UtilityInputReaderStore.Register(2069, &OneReader);
            UtilityInputReaderStore.Register(2070, &TwoReader);

            var builder = new BTreeBuilder<PostureBlackboard, BTreeContext>()
                .Parallel(0, par => par
                    .StatefulAction<PostureBlackboard, ChooseOptionParams, UtilityChoice>(bb => bb.Choose, bb => bb.Choice, UtilityNodes.ChooseOption)
                    .ObserverSelector(obs => obs
                        .Sequence(one => one
                            .StatefulCondition<PostureBlackboard, IsOptionParams, UtilityChoice>(bb => bb.IsOne, bb => bb.Choice, UtilityNodes.IsOption)
                            .Action(bb => bb.One, Run))
                        .Sequence(two => two
                            .StatefulCondition<PostureBlackboard, IsOptionParams, UtilityChoice>(bb => bb.IsTwo, bb => bb.Choice, UtilityNodes.IsOption)
                            .Action(bb => bb.Two, Run))));
            var interp = new Interpreter<PostureBlackboard, BTreeContext>(builder.Compile("CE2069Posture"), builder.GetRegistry());

            using var world = new EntityRepository();
            var bb = new PostureBlackboard { Choose = { Decision = new UtilityDecisionRef(id) }, IsOne = { Option = 1 }, IsTwo = { Option = 2 } };
            var ctx = new BTreeContext { Self = world.CreateEntity(), World = world };
            var state = new BehaviorTreeState();

            s_one = 0.9f; s_two = 0.2f;
            interp.Tick(ref bb, ref state, ref ctx);
            interp.Tick(ref bb, ref state, ref ctx);
            Assert.Equal(1, bb.Choice.Winner);
            Assert.Equal(2, bb.One.N);
            Assert.Equal(0, bb.Two.N);

            s_one = 0.75f; s_two = 0.8f;   // inside the 0.08 hysteresis: option 1 holds
            interp.Tick(ref bb, ref state, ref ctx);
            Assert.Equal(1, bb.Choice.Winner);
            Assert.Equal(3, bb.One.N);

            s_one = 0.2f; s_two = 0.9f;    // past it: the winner moves, and so does the branch
            interp.Tick(ref bb, ref state, ref ctx);
            interp.Tick(ref bb, ref state, ref ctx);
            Assert.Equal(2, bb.Choice.Winner);
            Assert.Equal(3, bb.One.N);
            Assert.True(bb.Two.N >= 1, $"the second branch must run once its option wins; ran {bb.Two.N}");
        }

        /// <summary>
        /// ⭐ The utility nodes' cached scorer follows <see cref="UtilityDecisionCatalog.Shared"/> when it is REPLACED
        /// (<see cref="UtilityDecisionCatalog.RegisterAll"/> stores a new registry — every <c>UtilityTestWorld</c> does). 🔴 It used
        /// to keep the old registry, so a decision registered after the swap scored 0: the order-dependent red of
        /// <see cref="CE2069_UtilityNodes_SwitchTheBranch_WhenTheWinnerChanges"/> (Winner 0 at its first assert).
        /// </summary>
        [Fact]
        public unsafe void UtilityNodes_FollowTheSharedCatalog_WhenItIsReplaced()
        {
            const int before = 0x20690002, after = 0x20690003;
            void Register(int id) => UtilityDecisionCatalog.Shared.Register(id, new UtilityDecisionDef
            {
                DebugName = "SwapPosture", Kind = DecisionKind.PostureSelect,
                Options = new[] { BuildSingleLinearOption(optionId: 1, inputId: 2069), BuildSingleLinearOption(optionId: 2, inputId: 2070) },
            }, hysteresisBonus: 0f);
            UtilityDecisionCatalog.EnsureRegistered();
            UtilityInputReaderStore.Register(2069, &OneReader);
            UtilityInputReaderStore.Register(2070, &TwoReader);
            s_one = 0.9f; s_two = 0.2f;

            using var world = new EntityRepository();
            var self = world.CreateEntity();
            var p = new ChooseOptionParams { Decision = new UtilityDecisionRef(before) };

            Register(before);
            var ws = new UtilityChoice();
            UtilityNodes.ChooseOption(ref p, ref ws, self, world);   // caches the nodes' scorer over this registry
            Assert.Equal(1, ws.Winner);

            UtilityDecisionCatalog.RegisterAll(out _);               // Shared is now a NEW registry
            Register(after);                                         // ⚠ a decision the OLD registry never had
            p.Decision = new UtilityDecisionRef(after);
            ws = new UtilityChoice();
            UtilityNodes.ChooseOption(ref p, ref ws, self, world);
            Assert.Equal(1, ws.Winner);
        }

        private static UtilityScorer PostureScorer(out int id)
        {
            var registry = new UtilityRegistry();
            id = 0x2067;
            registry.Register(id, new UtilityDecisionDef
            {
                DebugName = "CE2067Posture", Kind = DecisionKind.PostureSelect,
                Options = new[] { BuildSingleLinearOption(optionId: 1, inputId: 60), BuildSingleLinearOption(optionId: 2, inputId: 61) },
            }, hysteresisBonus: 0.08f);
            return new UtilityScorer(registry);
        }

        [Fact]
        public unsafe void CE2067_ChooseOption_NeedsNoUnitBuffer_AndTheCallersLastWinnerGetsTheHysteresis()
        {
            UtilityInputReaderStore.Register(60, &Stub07);    // option 1 = 0.70
            UtilityInputReaderStore.Register(61, &Stub075);   // option 2 = 0.75
            var scorer = PostureScorer(out int id);
            using var world = new EntityRepository();
            var unit = world.CreateEntity();                  // no UtilityResultBuffer

            Assert.Equal(2, scorer.ChooseOption(world, unit, id, lastWinner: 0));   // no previous winner: the best
            Assert.Equal(1, scorer.ChooseOption(world, unit, id, lastWinner: 1));   // 0.70 + 0.08 holds over 0.75
            Assert.Equal(2, scorer.ChooseOption(world, unit, id, lastWinner: 2));
            Assert.Equal(0, scorer.ChooseOption(world, unit, decisionId: 0x7777, lastWinner: 0));   // not registered
        }

        [Fact]
        public unsafe void CE2067_ChooseOption_StillFillsTheUnitBuffer_WhenTheUnitHasOne()
        {
            UtilityInputReaderStore.Register(60, &Stub07);
            UtilityInputReaderStore.Register(61, &Stub075);
            var scorer = PostureScorer(out int id);
            using var world = new EntityRepository();
            world.RegisterComponent<UtilityResultBuffer>();
            var unit = world.CreateEntity();
            world.AddComponent(unit, new UtilityResultBuffer());

            scorer.ChooseOption(world, unit, id, lastWinner: 0);
            ref readonly var buf = ref world.GetComponentRO<UtilityResultBuffer>(unit);
            Assert.Equal(2, buf.Count);
            Assert.Equal(2, buf.GetSpanRO()[0].WinningPostureId);
        }

        [Fact]
        public unsafe void CE2067_RankCandidates_ReturnsTheTopContactAsAnEntityRef()
        {
            UtilityInputReaderStore.Register(62, &FavouredReader);
            var registry = new UtilityRegistry();
            registry.Register(0x2068, new UtilityDecisionDef
            {
                DebugName = "CE2067Rank", Kind = DecisionKind.ThreatRanking,
                Options = new[] { BuildSingleLinearOption(optionId: 0, inputId: 62) },
            });
            registry.Register(0x2069, new UtilityDecisionDef { DebugName = "Posture", Kind = DecisionKind.PostureSelect, Options = Array.Empty<UtilityOption>() });
            var scorer = new UtilityScorer(registry);

            using var world = new EntityRepository();
            world.RegisterComponent<Fdp.Toolkit.Perception.Components.TargetMemory>();
            world.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            var unit = world.CreateEntity();
            var a = world.CreateEntity();
            var b = world.CreateEntity();
            world.AddComponent(a, new Fdp.Toolkit.Replication.Components.NetworkIdentity(501));
            world.AddComponent(b, new Fdp.Toolkit.Replication.Components.NetworkIdentity(502));
            var mem = new Fdp.Toolkit.Perception.Components.TargetMemory { Count = 2 };
            mem.EntityIds[0] = (long)a.PackedValue;
            mem.EntityIds[1] = (long)b.PackedValue;
            world.AddComponent(unit, mem);
            s_favoured = b;

            Assert.True(scorer.RankCandidates(world, unit, 0x2068, 0, out var top, out float score));
            Assert.Equal(502, top.NetworkId);
            Assert.Equal(0.9f, score, 3);
            Assert.False(scorer.RankCandidates(world, unit, 0x2069, 0, out _, out _));   // an option decision
        }

        // ── ⭐ CE-3069 G2 — the per-decision log of an OBSERVED unit (docs/DESIGN_Utility_AI_Demo_Scenarios.md §5.1) ──

        private static Entity ObservedUnit(EntityRepository world)
        {
            world.RegisterComponent<UtilityDecisionLog>();
            world.RegisterComponent<UtilityDebugFlags>();
            world.RegisterComponent<UtilityTraceWorkingMemory1024>();
            var unit = world.CreateEntity();
            world.AddComponent(unit, new UtilityDecisionLog());
            world.AddComponent(unit, new UtilityTraceWorkingMemory1024());
            world.AddComponent(unit, new UtilityDebugFlags { TraceEnabled = 1 });
            return unit;
        }

        [Fact]
        public unsafe void CE3069_ObservedUnit_LogsTheWinnerAfterHysteresis_AndCountsTheSwitch()
        {
            UtilityInputReaderStore.Register(60, &Stub07);    // option 1 = 0.70
            UtilityInputReaderStore.Register(61, &Stub075);   // option 2 = 0.75
            var scorer = PostureScorer(out int id);
            using var world = new EntityRepository();
            var unit = ObservedUnit(world);

            Assert.Equal(2, scorer.ChooseOption(world, unit, id, lastWinner: 0));
            var slot = world.GetComponentRO<UtilityDecisionLog>(unit).SlotsRO()[0];
            Assert.Equal(id, slot.DecisionId);
            Assert.Equal(2, slot.Winner);
            Assert.Equal(0, slot.SwitchCount);   // the first winner is not a switch

            Assert.Equal(1, scorer.ChooseOption(world, unit, id, lastWinner: 1));   // 0.70 + 0.08 holds over 0.75
            slot = world.GetComponentRO<UtilityDecisionLog>(unit).SlotsRO()[0];
            Assert.Equal(1, slot.Winner);
            Assert.Equal(2, slot.PreviousWinner);
            Assert.Equal(1, slot.SwitchCount);
            Assert.Equal(2, slot.EvalCount);
            Assert.Equal(1, slot.RankedRO()[0].WinningPostureId);
            Assert.Equal(0.78f, slot.RankedRO()[0].Score, 3);   // the logged score includes the bonus
        }

        [Fact]
        public unsafe void CE3069_F3_TheTracedWinner_IsTheOptionChosenAfterHysteresis()
        {
            UtilityInputReaderStore.Register(60, &Stub07);
            UtilityInputReaderStore.Register(61, &Stub075);
            var scorer = PostureScorer(out int id);
            using var world = new EntityRepository();
            var unit = ObservedUnit(world);

            // Raw scores favour option 2 (0.75 > 0.70); the bonus keeps option 1. The trace used to name option 2.
            Assert.Equal(1, scorer.ChooseOption(world, unit, id, lastWinner: 1));
            var traced = world.GetComponentRW<UtilityTraceWorkingMemory1024>(unit).LatestSelected();
            Assert.Equal(1, traced.OptionId);
        }

        [Fact]
        public unsafe void CE3069_ObservedUnit_LogsARankingAsItsTopCandidate_BesideTheOptionDecision()
        {
            UtilityInputReaderStore.Register(60, &Stub07);
            UtilityInputReaderStore.Register(61, &Stub075);
            UtilityInputReaderStore.Register(62, &FavouredReader);
            var registry = new UtilityRegistry();
            registry.Register(0x2067, new UtilityDecisionDef
            {
                DebugName = "Posture", Kind = DecisionKind.PostureSelect,
                Options = new[] { BuildSingleLinearOption(optionId: 1, inputId: 60), BuildSingleLinearOption(optionId: 2, inputId: 61) },
            }, hysteresisBonus: 0.08f);
            registry.Register(0x2068, new UtilityDecisionDef
            {
                DebugName = "Rank", Kind = DecisionKind.ThreatRanking,
                Options = new[] { BuildSingleLinearOption(optionId: 0, inputId: 62) },
            });
            var scorer = new UtilityScorer(registry);

            using var world = new EntityRepository();
            world.RegisterComponent<Fdp.Toolkit.Perception.Components.TargetMemory>();
            var unit = ObservedUnit(world);
            var a = world.CreateEntity();
            var b = world.CreateEntity();
            var mem = new Fdp.Toolkit.Perception.Components.TargetMemory { Count = 2 };
            mem.EntityIds[0] = (long)a.PackedValue;
            mem.EntityIds[1] = (long)b.PackedValue;
            world.AddComponent(unit, mem);
            s_favoured = b;

            scorer.ChooseOption(world, unit, 0x2067, lastWinner: 0);
            Assert.True(scorer.TopCandidate(world, unit, 0x2068, 0, out var top, out _));
            Assert.Equal(b, top);

            // Both decisions keep their own slot — the unit's one result buffer could only show the last of them.
            var slots = world.GetComponentRO<UtilityDecisionLog>(unit).SlotsRO();
            Assert.Equal(0x2067, slots[0].DecisionId);
            Assert.Equal(2, slots[0].Winner);
            Assert.Equal(0x2068, slots[1].DecisionId);
            Assert.Equal((long)b.PackedValue, slots[1].Winner);
        }

        /// <summary>⭐ <c>CE-1319</c> (BV-61; ⛔ SUPERSEDES CE-3069's "an unobserved unit is not logged") — the decision is logged whenever
        /// the unit carries the log, trace switch off or no flags at all: the Why replays for every unit. ✅ Red-proof: the old
        /// <c>TraceEnabled</c> gate back in <c>LogDecision</c> ⇒ the slot stays empty.</summary>
        [Fact]
        public unsafe void CE1319_AnUnobservedUnit_IsLoggedToo()
        {
            UtilityInputReaderStore.Register(60, &Stub07);
            UtilityInputReaderStore.Register(61, &Stub075);
            var scorer = PostureScorer(out int id);
            using var world = new EntityRepository();
            var unit = ObservedUnit(world);
            world.GetComponentRW<UtilityDebugFlags>(unit).TraceEnabled = 0;

            scorer.ChooseOption(world, unit, id, lastWinner: 0);
            Assert.Equal(id, world.GetComponentRO<UtilityDecisionLog>(unit).SlotsRO()[0].DecisionId);
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

        /// <summary>
        /// ⭐ <c>CE-3196</c> — readers registered from many threads at once (as parallel test classes do, each calling
        /// <c>StandardInputs.RegisterAll</c>) all land and nothing throws. 🔴 Red before: the store was a plain Dictionary and a
        /// concurrent write corrupted it — 61 SimHost squad tests failed in one run with "Operations that change non-concurrent
        /// collections must have exclusive access".
        /// </summary>
        [Fact]
        public unsafe void CE3196_ReadersRegisteredFromManyThreads_AllLand()
        {
            const int Threads = 16, PerThread = 2000;
            System.Threading.Tasks.Parallel.For(0, Threads, t =>
            {
                for (int i = 0; i < PerThread; i++)
                    UtilityInputReaderStore.Register((ushort)(0x8000 + ((t * PerThread + i) % 0x4000)), &Stub09);
            });
            for (int id = 0x8000; id < 0x8000 + Math.Min(Threads * PerThread, 0x4000); id++)
                Assert.True(UtilityInputReaderStore.TryGet((ushort)id, out _), $"input {id:X} missing");
        }
    
        // ── ⭐ CE-4395 — a decision at a CADENCE (DecisionCadence; Cost & Cadence §11g-2) ──────────────────────────────────────────
        private static int s_cadenceReads;
        private static float s_optionOne = 0.7f, s_optionTwo = 0.75f;
        private static unsafe float CountedOne(in UtilityInputCtx ctx) { s_cadenceReads++; return s_optionOne; }
        private static unsafe float CountedTwo(in UtilityInputCtx ctx) => s_optionTwo;

        private static void At(EntityRepository world, double t) { world.Tick(); world.SetSingleton(new GlobalTime { TotalTime = t }); }

        /// <summary>
        /// ⭐ CE-4395 — a cadenced option decision is scored once per period on the unit's phase and reused between (the unit buffer
        /// written back as the scoring wrote it); re-scored at once when the unit's senses changed, when the caller's winner is not the
        /// stored one (a new run), when time went back, and on every call in a world with no GlobalTime. A reuse allocates nothing.
        /// 🔴 Red-proof: the reuse path dropped ⇒ the read count moves on a reused call.
        /// </summary>
        [Fact]
        public unsafe void CE4395_ACadencedDecision_IsScoredOncePerPeriod_AndAtOnceWhenWoken()
        {
            UtilityInputReaderStore.Register(70, &CountedOne);
            UtilityInputReaderStore.Register(71, &CountedTwo);
            s_optionOne = 0.7f; s_optionTwo = 0.75f; s_cadenceReads = 0;
            var registry = new UtilityRegistry();
            const int id = 0x4395;
            registry.Register(id, new UtilityDecisionDef
            {
                DebugName = "CE4395Posture", Kind = DecisionKind.PostureSelect, CadenceSeconds = 0.2,
                Options = new[] { BuildSingleLinearOption(optionId: 1, inputId: 70), BuildSingleLinearOption(optionId: 2, inputId: 71) },
            });
            var scorer = new UtilityScorer(registry);
            using var world = new EntityRepository();
            world.RegisterComponent<UtilityResultBuffer>();
            world.RegisterEvent<Fdp.Toolkit.Perception.Events.SensorChangedEvent>();
            var unit = world.CreateEntity();
            world.AddComponent(unit, new UtilityResultBuffer());
            double due = CadenceGate.NextOnGrid(10.0, 0.2, unit.Index);
            Assert.InRange(due - 10.0, 0.0, 0.2 + 1e-9);                                       // never older than one period

            At(world, 10.0);
            Assert.Equal(2, scorer.ChooseOption(world, unit, id, lastWinner: 0));
            Assert.Equal(1, s_cadenceReads);
            var scored = world.GetComponentRO<UtilityResultBuffer>(unit);

            s_optionOne = 0.9f;                                                                  // option 1 would win now…
            world.GetComponentRW<UtilityResultBuffer>(unit) = new UtilityResultBuffer();         // …and another decision wrote the buffer
            At(world, (10.0 + due) / 2);
            Assert.Equal(2, scorer.ChooseOption(world, unit, id, lastWinner: 2));                // ⭐ reused: the stored winner
            Assert.Equal(1, s_cadenceReads);
            var back = world.GetComponentRO<UtilityResultBuffer>(unit);
            Assert.Equal(scored.Count, back.Count);
            Assert.Equal(scored.GetSpanRO()[0].WinningPostureId, back.GetSpanRO()[0].WinningPostureId);
            Assert.Equal(scored.GetSpanRO()[0].Score, back.GetSpanRO()[0].Score);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) scorer.ChooseOption(world, unit, id, lastWinner: 2);
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Equal(1, s_cadenceReads);

            At(world, due);
            Assert.Equal(1, scorer.ChooseOption(world, unit, id, lastWinner: 2));                // ⭐ due: re-scored, the new winner
            Assert.Equal(2, s_cadenceReads);

            s_optionOne = 0.1f;
            world.Bus.Publish(new Fdp.Toolkit.Perception.Events.SensorChangedEvent { Unit = unit, What = Fdp.Toolkit.Perception.Events.SensorChange.Hit });
            world.Bus.SwapBuffers();
            At(world, due + 0.01);
            Assert.Equal(2, scorer.ChooseOption(world, unit, id, lastWinner: 1));                // ⭐ woken: re-scored at once
            Assert.Equal(3, s_cadenceReads);
            scorer.ChooseOption(world, unit, id, lastWinner: 2);                                  // …once per frame
            Assert.Equal(3, s_cadenceReads);

            At(world, due + 0.02);
            scorer.ChooseOption(world, unit, id, lastWinner: 0);                                  // a new run (no winner): scored
            Assert.Equal(4, s_cadenceReads);
            At(world, 5.0);
            scorer.ChooseOption(world, unit, id, lastWinner: 2);                                  // time went back (a restore): scored
            Assert.Equal(5, s_cadenceReads);
            Assert.True(DecisionCadence.Of(world).Reused >= 102);

            using var bare = new EntityRepository();                                              // no GlobalTime: every call, as before
            var other = bare.CreateEntity();
            scorer.ChooseOption(bare, other, id, lastWinner: 0);
            scorer.ChooseOption(bare, other, id, lastWinner: 2);
            Assert.Equal(7, s_cadenceReads);
        }

        /// <summary>⭐ CE-4395 — the three combat decisions carry the approved 0.2 s cadence; a decision that declares none is scored on
        /// every call (the cadence stays opt-in).</summary>
        [Fact]
        public void CE4395_TheCombatDecisions_AreCadencedAt0_2s_AndNothingElseIs()
        {
            UtilityDecisionCatalog.EnsureRegistered();
            foreach (var asset in new[] { "1a4f7c20-3b9e-4d18-8a01-threat0000001" })
                Assert.True(UtilityDecisionCatalog.Shared.TryGet(UtilityDecisionCatalog.ComputeId(asset), out var d, out _) && d!.CadenceSeconds == 0.2);
            int cadenced = 0;
            foreach (var t in typeof(UtilityScorer).Assembly.GetTypes())
                if (System.Reflection.CustomAttributeExtensions.GetCustomAttribute<UtilityDecisionAttribute>(t) is { CadenceSeconds: > 0 } a)
                {
                    cadenced++;
                    Assert.Contains(a.DisplayName, new[] { "Combat posture", "Attack approach", "Threat ranking" });
                    Assert.Equal(0.2, a.CadenceSeconds);
                }
            Assert.Equal(3, cadenced);
        }
    }
}
