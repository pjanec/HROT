using Fdp.Toolkit.Spatial.Eqs;

namespace Fdp.Toolkit.Utility
{
    /// <summary>
    /// Starter-pack CombatPosture decision.
    /// Selects one of five tactical postures based on health, ammo, situational inputs,
    /// and EQS query scores. Applies a 0.08 hysteresis bonus to reduce flickering.
    /// </summary>
    /// <remarks>
    /// ⭐ <c>CE-2046</c>: the EQS considerations name their templates by AssetId (<see cref="FindCoverFromTarget"/>,
    /// <see cref="FindSafeRetreatPoint"/> — both built, <c>CE-2051</c>).
    /// <para>⭐ <c>CE-2072</c>: <c>Suppress</c> no longer multiplies by <c>AllyAdvancingNearby</c> — a stub returning 0, so the
    /// weighted product made Suppress ALWAYS 0. It now reads: ammo × a target × healthy × the enemy at least my match
    /// (<c>EnemyStrengthRatio</c>, Logistic — as TakeCover and Flee read it). ⇒ a healthy, armed unit ADVANCES on a weaker
    /// enemy and SUPPRESSES a matched or stronger one; a hurt one still takes cover or flees.
    /// 📄 docs/DESIGN_Decision_Layer.md §3.3.</para>
    /// </remarks>
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
                .Consider(In.EqsTopScore(FindCoverFromTarget.AssetId),  1.0f, Curve.Linear)
                .Consider(In.EnemyStrengthRatio(),          0.6f, Curve.Logistic))
            .Option((ushort)Posture.Suppress, ScoringMode.WeightedProduct, o => o
                .Consider(In.AmmoFraction(),        0.9f, Curve.Linear)
                .Consider(In.HaveLiveTarget(),      1.0f, Curve.Step)
                .Consider(In.HealthFraction(),      1.0f, Curve.Linear)
                .Consider(In.EnemyStrengthRatio(),  0.6f, Curve.Logistic))
            .Option((ushort)Posture.Flee, ScoringMode.WeightedProduct, o => o
                .Consider(In.HealthFraction(),               1.0f, Curve.InverseQuadratic)
                .Consider(In.EqsTopScore(FindSafeRetreatPoint.AssetId), 0.8f, Curve.Linear)
                .Consider(In.EnemyStrengthRatio(),           0.7f, Curve.Logistic))
            .Option((ushort)Posture.Hold, ScoringMode.WeightedSum, o => o
                .Consider(In.HealthFraction(), 0.3f, Curve.Linear)
                .Consider(In.Constant(0.2f),   1.0f, Curve.Linear));
    }
}
