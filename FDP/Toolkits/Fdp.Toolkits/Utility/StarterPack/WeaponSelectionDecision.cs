namespace Fdp.Toolkit.Utility
{
    /// <summary>
    /// Starter-pack WeaponSelection decision.
    /// Scores each weapon mount on the agent and selects the best-fit weapon for the current
    /// engagement range.
    /// evalSelf = mount entity; evalContext = target entity.
    /// </summary>
    [UtilityDecision(
        assetId:     "2b5e8d31-4c0f-5e29-9b12-weapon0000001",
        displayName: "Weapon selection",
        kind:        DecisionKind.WeaponSelection,
        category:    "Tactical/Effectors")]
    public sealed partial class WeaponSelectionDecision : IUtilityDecisionDefinition
    {
        /// <summary>Builds the decision definition via the fluent builder.</summary>
        public static void Build(IUtilityDecisionBuilder b) => b
            .CandidateOption(ScoringMode.WeightedProduct, o => o
                .Consider(In.WeaponHasAmmo(),               1.0f, Curve.Step)
                // ⭐ CE-3089 (G7, W6) — IN RANGE is good: ≈ 1 up to the mount's range, falling off past it (a reversed logistic
                //   at distance / range = 1). ⛔ Was Curve.Bell (peak AT the range, exp(-8(x-1)²)): a target at a fifth of the
                //   range scored 0.007, so at any mid-range engagement every mount scored ≈ 0 and the choice fell to mount 0 —
                //   measured live on ua-weapon-choice (a Bradley emptied its 25 mm into a T-72 at 523 m).
                .Consider(In.WeaponRangeBandFit(),          1.0f, new ResponseCurve(CurveKind.Logistic, slope: 1f, exponent: -12f, xShift: 1.0f))
                .Consider(In.WeaponEffectivenessVsTarget(), 1.0f, Curve.Linear)
                .Consider(In.WeaponReadiness(),             0.6f, Curve.Linear)
                // ⭐ CE-3071 (design §9) — keep the scarce round for the target only it can hurt: with 7 TOW and 300 of 25 mm
                //   the 25 mm wins on infantry, the TOW on a tank (effectiveness comes from ArmorModel, not range).
                .Consider(In.RoundsLeft(),                  1.0f, Curve.Linear));
    }
}
