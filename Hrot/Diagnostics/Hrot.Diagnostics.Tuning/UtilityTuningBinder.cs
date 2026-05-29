using System;
using Fdp.Toolkit.Utility;

namespace Hrot.Diagnostics.Tuning
{
    // Auto-registers consideration fields from a UtilityDecisionDef as tunables.
    // Registered names follow: utility.<DecisionName>.<optionId>.<considerationIdx>.<field>
    // Fields per consideration: weight, slope (m), exponent (k), xShift (b)
    public static class UtilityTuningBinder
    {
        public static void RegisterDecision(TuningRegistry registry, UtilityDecisionDef def)
        {
            ArgumentNullException.ThrowIfNull(registry);
            ArgumentNullException.ThrowIfNull(def);

            string decName = def.DebugName;
            foreach (var option in def.Options)
            {
                for (int ci = 0; ci < option.Considerations.Length; ci++)
                {
                    RegisterConsideration(registry, decName, option, ci);
                }
            }
        }

        private static void RegisterConsideration(
            TuningRegistry registry,
            string decName,
            UtilityOption option,
            int ci)
        {
            string prefix = $"utility.{decName}.{option.OptionId}.{ci}";

            // weight [0..10]
            registry.Register(new TuningKey($"{prefix}.weight"), new Tunable
            {
                Kind       = TuningKind.Float,
                Min        = 0f,
                Max        = 10f,
                Scope      = TuningScope.Global,
                Owner      = TuningOwner.Brain,
                Provenance = $"decision:{decName}",
                Read       = () => option.Considerations[ci].Weight,
                Write      = v =>
                {
                    var old = option.Considerations[ci];
                    option.Considerations[ci] = new UtilityConsideration(
                        old.InputId, old.Context, v, old.Curve, old.Params);
                },
            });

            // slope / m [-2..2]
            registry.Register(new TuningKey($"{prefix}.slope"), new Tunable
            {
                Kind       = TuningKind.Float,
                Min        = -2f,
                Max        = 2f,
                Scope      = TuningScope.Global,
                Owner      = TuningOwner.Brain,
                Provenance = $"decision:{decName}",
                Read       = () => option.Considerations[ci].Curve.Slope,
                Write      = v =>
                {
                    var old = option.Considerations[ci];
                    var c = old.Curve;
                    option.Considerations[ci] = new UtilityConsideration(
                        old.InputId, old.Context, old.Weight,
                        new ResponseCurve(c.Kind, v, c.Exponent, c.XShift),
                        old.Params);
                },
            });

            // exponent / k [0..20]
            registry.Register(new TuningKey($"{prefix}.exponent"), new Tunable
            {
                Kind       = TuningKind.Float,
                Min        = 0f,
                Max        = 20f,
                Scope      = TuningScope.Global,
                Owner      = TuningOwner.Brain,
                Provenance = $"decision:{decName}",
                Read       = () => option.Considerations[ci].Curve.Exponent,
                Write      = v =>
                {
                    var old = option.Considerations[ci];
                    var c = old.Curve;
                    option.Considerations[ci] = new UtilityConsideration(
                        old.InputId, old.Context, old.Weight,
                        new ResponseCurve(c.Kind, c.Slope, v, c.XShift),
                        old.Params);
                },
            });

            // xShift / b [-1..2]
            registry.Register(new TuningKey($"{prefix}.xShift"), new Tunable
            {
                Kind       = TuningKind.Float,
                Min        = -1f,
                Max        = 2f,
                Scope      = TuningScope.Global,
                Owner      = TuningOwner.Brain,
                Provenance = $"decision:{decName}",
                Read       = () => option.Considerations[ci].Curve.XShift,
                Write      = v =>
                {
                    var old = option.Considerations[ci];
                    var c = old.Curve;
                    option.Considerations[ci] = new UtilityConsideration(
                        old.InputId, old.Context, old.Weight,
                        new ResponseCurve(c.Kind, c.Slope, c.Exponent, v),
                        old.Params);
                },
            });
        }
    }
}
