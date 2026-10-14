using Fdp.Toolkit.Squad.DangerArea;

namespace Fdp.Toolkit.Utility
{
    /// <summary>⭐ <c>CE-2159</c> D3 — how a squad crosses the danger area ahead (option ids start at 1).</summary>
    public enum SquadCrossingChoice : byte
    {
        /// <summary>The danger-area crossing drill: security, pairs, far side, reform (<c>SquadDangerCrossing</c>'s own play).</summary>
        Cross = 1,
        /// <summary>Bounding overwatch to beyond the far side — open ground, where watching and bounding beats pairs on lanes.</summary>
        Bound = 2,
        /// <summary>Hold where the squad stands for a while, then decide again.</summary>
        Hold  = 3,
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2159</c> D3 (R-2014; 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §7.7.7) — the squad leader's choice of HOW
    /// to cross the area ahead: the considerations of the retired <c>ManeuverSelectStarterDecision</c> (a <c>ManeuverSelect</c> decision,
    /// which <c>UtilityScorer.ChooseOption</c> does not score — superseded by this one and removed `2026-10-14`) as an OPTION decision, with one change —
    /// Cross weighs "not open ground" instead of "a street", so an intersection, choke point or crest is crossed rather than scoring
    /// nothing. ctx.Self is the COMMANDER; the area is the one it has acquired (<c>SquadCognitiveState.ActiveFeatureId</c>).
    /// </summary>
    [UtilityDecision(
        assetId:         "5a2159d3-0000-4000-a000-squadcrossing",
        displayName:     "Squad crossing choice",
        kind:            DecisionKind.PostureSelect,   // ⚠ the OPTION-decision kind (see SquadContactResponseDecision)
        category:        "Tactical/Squad",
        hysteresisBonus: 0f,
        OptionNames = typeof(SquadCrossingChoice))]
    public sealed partial class SquadCrossingDecision : IUtilityDecisionDefinition
    {
        /// <summary>The decision's asset id, for a C# tree that names it (<c>SquadDangerCrossingBehavior</c>).</summary>
        public const string DecisionAssetId = "5a2159d3-0000-4000-a000-squadcrossing";

        private static InputRef KindIs(DangerAreaKind kind)
            => new InputRef(SquadInputIds.ActiveFeatureKindIs, InputContext.Self, new InputParams { BlueprintId = (uint)kind });

        private static InputRef Threat() => new InputRef(SquadInputIds.ActiveFeatureThreatRating, InputContext.Self);

        /// <summary>Builds the decision definition via the fluent builder.</summary>
        public static void Build(IUtilityDecisionBuilder b) => b
            .Option((ushort)SquadCrossingChoice.Cross, ScoringMode.WeightedProduct, o => o
                .Consider(In.SquadStrengthRatio(),           0.6f, Curve.Linear)
                .Consider(KindIs(DangerAreaKind.OpenGround), 0.9f, Curve.InverseLinear)
                .Consider(In.SquadAmmoRollup(),              0.5f, new ResponseCurve(CurveKind.Step, slope: 1f, xShift: 0.3f)))
            .Option((ushort)SquadCrossingChoice.Bound, ScoringMode.WeightedProduct, o => o
                .Consider(In.SquadStrengthRatio(),           0.8f, Curve.Linear)
                .Consider(KindIs(DangerAreaKind.OpenGround), 0.7f, Curve.Linear)
                .Consider(Threat(),                          0.6f, new ResponseCurve(CurveKind.Logistic, slope: 6f, xShift: 0.5f)))
            .Option((ushort)SquadCrossingChoice.Hold, ScoringMode.WeightedProduct, o => o
                .Consider(Threat(),                          0.9f, Curve.Linear)
                .Consider(In.SquadAmmoRollup(),              0.5f, Curve.InverseLinear));
    }
}
