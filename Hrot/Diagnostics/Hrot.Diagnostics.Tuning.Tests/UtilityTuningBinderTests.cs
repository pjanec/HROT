using Fdp.Toolkit.Utility;
using Hrot.Diagnostics.Tuning;
using Xunit;

namespace Hrot.Diagnostics.Tuning.Tests
{
    public class UtilityTuningBinderTests
    {
        // Creates a minimal UtilityDecisionDef with one option and one consideration.
        private static UtilityDecisionDef MakeDecision()
        {
            var def = new UtilityDecisionDef
            {
                DebugName = "TestDecision",
                Options = new[]
                {
                    new UtilityOption
                    {
                        OptionId = 7,
                        Mode     = ScoringMode.WeightedProduct,
                        Considerations = new[]
                        {
                            new UtilityConsideration(
                                inputId: 1,
                                context: InputContext.Self,
                                weight:  0.8f,
                                curve:   new ResponseCurve(CurveKind.Linear, 1.5f, 1f, 0f)),
                        },
                    },
                },
            };
            return def;
        }

        [Fact]
        public void RegisterDecision_SingleOptionSingleConsideration_RegistersFourTunables()
        {
            var reg = new TuningRegistry();
            var def = MakeDecision();

            UtilityTuningBinder.RegisterDecision(reg, def);

            // Expect weight, slope, exponent, xShift.
            string prefix = "utility.TestDecision.7.0";
            Assert.True(reg.TryGet(new TuningKey($"{prefix}.weight"),   out _));
            Assert.True(reg.TryGet(new TuningKey($"{prefix}.slope"),    out _));
            Assert.True(reg.TryGet(new TuningKey($"{prefix}.exponent"), out _));
            Assert.True(reg.TryGet(new TuningKey($"{prefix}.xShift"),   out _));
        }

        [Fact]
        public void RegisterDecision_Read_ReturnsCurrentConsiderationValue()
        {
            var reg = new TuningRegistry();
            var def = MakeDecision();
            UtilityTuningBinder.RegisterDecision(reg, def);

            string prefix = "utility.TestDecision.7.0";
            reg.TryGet(new TuningKey($"{prefix}.weight"), out var t);

            Assert.NotNull(t);
            Assert.Equal(0.8f, t!.Read(), 4);
        }

        [Fact]
        public void RegisterDecision_Write_UpdatesConsiderationInPlace()
        {
            var reg = new TuningRegistry();
            var def = MakeDecision();
            UtilityTuningBinder.RegisterDecision(reg, def);

            string prefix = "utility.TestDecision.7.0";

            // Apply a new weight and drain the queue.
            reg.Apply(new TuningKey($"{prefix}.weight"), 0.3f);
            reg.BeginFrame();

            Assert.Equal(0.3f, def.Options[0].Considerations[0].Weight, 4);
        }

        [Fact]
        public void RegisterDecision_MultipleOptions_RegistersAllConsiderations()
        {
            var def = new UtilityDecisionDef
            {
                DebugName = "Multi",
                Options = new[]
                {
                    new UtilityOption
                    {
                        OptionId = 1,
                        Mode     = ScoringMode.WeightedProduct,
                        Considerations = new[]
                        {
                            new UtilityConsideration(1, InputContext.Self, 1f,
                                new ResponseCurve(CurveKind.Linear)),
                            new UtilityConsideration(2, InputContext.Target, 0.5f,
                                new ResponseCurve(CurveKind.Logistic)),
                        },
                    },
                    new UtilityOption
                    {
                        OptionId = 2,
                        Mode     = ScoringMode.WeightedSum,
                        Considerations = new[]
                        {
                            new UtilityConsideration(3, InputContext.Leader, 0.9f,
                                new ResponseCurve(CurveKind.Bell)),
                        },
                    },
                },
            };

            var reg = new TuningRegistry();
            UtilityTuningBinder.RegisterDecision(reg, def);

            // Option 1 has 2 considerations => 8 tunables.
            // Option 2 has 1 consideration => 4 tunables.
            // Total = 12.
            Assert.True(reg.TryGet(new TuningKey("utility.Multi.1.0.weight"),   out _));
            Assert.True(reg.TryGet(new TuningKey("utility.Multi.1.1.weight"),   out _));
            Assert.True(reg.TryGet(new TuningKey("utility.Multi.2.0.weight"),   out _));
            Assert.True(reg.TryGet(new TuningKey("utility.Multi.2.0.exponent"), out _));
        }
    }
}
