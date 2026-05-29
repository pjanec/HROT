namespace Fdp.Toolkit.Utility
{
    /// <summary>
    /// Starter-pack CombatPosture decision.
    /// Selects one of five tactical postures based on health, ammo, situational inputs,
    /// and EQS query scores. Applies a 0.08 hysteresis bonus to reduce flickering.
    /// </summary>
    [UtilityDecision(
        assetId:         "3c6f9e42-5d10-6f3a-ac23-posture0000001",
        displayName:     "Combat posture",
        kind:            DecisionKind.PostureSelect,
        category:        "Tactical/Posture",
        hysteresisBonus: 0.08f)]
    public sealed partial class CombatPostureDecision : IUtilityDecisionDefinition
    {
        /// <summary>Builds the decision definition via the fluent builder.</summary>
        public static void Build(IUtilityDecisionBuilder b) => b
            .Option((ushort)Posture.AdvanceAndAttack, ScoringMode.WeightedProduct, o => o
                .Consider(In.HealthFraction(),     0.7f, Curve.Linear)
                .Consider(In.AmmoFraction(),       0.9f, Curve.Threshold)
                .Consider(In.EnemyStrengthRatio(), 0.8f, Curve.InverseLinear)
                .Consider(In.HaveLiveTarget(),     1.0f, Curve.Step))
            .Option((ushort)Posture.TakeCover, ScoringMode.WeightedProduct, o => o
                .Consider(In.HealthFraction(),              0.8f, Curve.InverseLinear)
                .Consider(In.EqsTopScore("CoverQuery"),     1.0f, Curve.Linear)
                .Consider(In.EnemyStrengthRatio(),          0.6f, Curve.Logistic))
            .Option((ushort)Posture.Suppress, ScoringMode.WeightedProduct, o => o
                .Consider(In.AmmoFraction(),        0.9f, Curve.Linear)
                .Consider(In.HaveLiveTarget(),      1.0f, Curve.Step)
                .Consider(In.AllyAdvancingNearby(), 0.7f, Curve.Linear))
            .Option((ushort)Posture.Flee, ScoringMode.WeightedProduct, o => o
                .Consider(In.HealthFraction(),               1.0f, Curve.InverseQuadratic)
                .Consider(In.EqsTopScore("RetreatQuery"),    0.8f, Curve.Linear)
                .Consider(In.EnemyStrengthRatio(),           0.7f, Curve.Logistic))
            .Option((ushort)Posture.Hold, ScoringMode.WeightedSum, o => o
                .Consider(In.HealthFraction(), 0.3f, Curve.Linear)
                .Consider(In.Constant(0.2f),   1.0f, Curve.Linear));
    }
}
