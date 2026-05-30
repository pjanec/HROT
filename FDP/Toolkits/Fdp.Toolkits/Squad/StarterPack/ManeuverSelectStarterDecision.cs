using Fdp.Toolkit.Squad.DangerArea;
using Fdp.Toolkit.Utility;

namespace Fdp.Toolkit.Squad.StarterPack
{
    /// <summary>
    /// Worked-example ManeuverSelect decision for a squad commander.
    /// Three options: DangerAreaCross (0), BoundOverwatch (1), Hold (2).
    ///
    /// Considerations reference squad-commander Utility inputs registered by
    /// <see cref="Fdp.Toolkit.Utility.SquadInputs.RegisterAll"/>.
    ///
    /// Option weightings (WeightedProduct mode):
    ///   DangerAreaCross  -- SquadStrengthRatio(Linear,0.6) + ActiveFeatureKindIs(SC|CP,0.9) + SquadAmmoRollup(Threshold@0.3,0.5)
    ///   BoundOverwatch   -- SquadStrengthRatio(Linear,0.8) + ActiveFeatureKindIs(OpenGround,0.7) + ActiveFeatureThreatRating(Logistic,0.6)
    ///   Hold             -- ActiveFeatureThreatRating(Linear,0.9) + SquadAmmoRollup(InverseLinear,0.5)
    /// </summary>
    public static class ManeuverSelectStarterDecision
    {
        public const ushort OptionIdDangerAreaCross  = 0;
        public const ushort OptionIdBoundOverwatch   = 1;
        public const ushort OptionIdHold             = 2;

        public static UtilityDecisionDef Build() => new UtilityDecisionDef
        {
            DebugName = "ManeuverSelect",
            Kind      = DecisionKind.ManeuverSelect,
            Options   = new[]
            {
                new UtilityOption
                {
                    OptionId = OptionIdDangerAreaCross,
                    Mode     = ScoringMode.WeightedProduct,
                    Considerations = new[]
                    {
                        new UtilityConsideration(SquadInputIds.SquadStrengthRatio,
                            InputContext.Self, weight: 0.6f,
                            curve: new ResponseCurve(CurveKind.Linear, slope: 1f)),
                        // ActiveFeatureKindIs(StreetCrossing): BlueprintId encodes the DangerAreaKind byte.
                        new UtilityConsideration(SquadInputIds.ActiveFeatureKindIs,
                            InputContext.Self, weight: 0.9f,
                            curve: new ResponseCurve(CurveKind.Linear, slope: 1f),
                            @params: new InputParams { BlueprintId = (uint)DangerAreaKind.StreetCrossing }),
                        new UtilityConsideration(SquadInputIds.SquadAmmoRollup,
                            InputContext.Self, weight: 0.5f,
                            curve: new ResponseCurve(CurveKind.Step, xShift: 0.3f)),
                    }
                },
                new UtilityOption
                {
                    OptionId = OptionIdBoundOverwatch,
                    Mode     = ScoringMode.WeightedProduct,
                    Considerations = new[]
                    {
                        new UtilityConsideration(SquadInputIds.SquadStrengthRatio,
                            InputContext.Self, weight: 0.8f,
                            curve: new ResponseCurve(CurveKind.Linear, slope: 1f)),
                        new UtilityConsideration(SquadInputIds.ActiveFeatureKindIs,
                            InputContext.Self, weight: 0.7f,
                            curve: new ResponseCurve(CurveKind.Linear, slope: 1f),
                            @params: new InputParams { BlueprintId = (uint)DangerAreaKind.OpenGround }),
                        new UtilityConsideration(SquadInputIds.ActiveFeatureThreatRating,
                            InputContext.Self, weight: 0.6f,
                            curve: new ResponseCurve(CurveKind.Logistic, slope: 6f, xShift: 0.5f)),
                    }
                },
                new UtilityOption
                {
                    OptionId = OptionIdHold,
                    Mode     = ScoringMode.WeightedProduct,
                    Considerations = new[]
                    {
                        new UtilityConsideration(SquadInputIds.ActiveFeatureThreatRating,
                            InputContext.Self, weight: 0.9f,
                            curve: new ResponseCurve(CurveKind.Linear, slope: 1f)),
                        // InverseLinear: CurveKind.InverseLinear evaluates as (1 - x) internally.
                        new UtilityConsideration(SquadInputIds.SquadAmmoRollup,
                            InputContext.Self, weight: 0.5f,
                            curve: new ResponseCurve(CurveKind.InverseLinear, slope: 1f)),
                    }
                },
            }
        };
    }
}
