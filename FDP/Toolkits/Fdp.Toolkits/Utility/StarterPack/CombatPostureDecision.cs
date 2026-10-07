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
    /// <para>⭐ <c>CE-2105</c> (R-208, 🔒 user <c>2026-10-05</c>: "advance without enemy"): <c>AdvanceAndAttack</c> no longer
    /// needs a live target. With nothing left to fight (an enemy killed or lost on the way) a healthy, armed unit keeps
    /// ADVANCING — <c>AdvanceAndAttack</c> moves on to the objective and fires only when there is something to fire at —
    /// instead of holding short of it for ever. Suppress still needs a live target (a point cannot be shot).</para>
    /// <para>⭐ <c>CE-3090</c> (🔒 user <c>2026-10-07</c>: <i>"flee is only realistic if the unit is healthy and capable of fleeing
    /// without becoming easy target; for wounded one the hold-prone seems a better option"</i>): <c>Flee</c> needs at least half health
    /// (and still a HIDDEN retreat point); <c>HoldProne</c> is TakeCover's mirror — hurt × a live threat × NO cover × outmatched — so a
    /// wounded unit takes cover where there is cover and lies down and returns fire where there is none. 📄 docs/DESIGN_Decision_Layer.md §3.3f.</para>
    /// </remarks>
    [UtilityDecision(
        assetId:         "3c6f9e42-5d10-6f3a-ac23-posture0000001",
        displayName:     "Combat posture",
        kind:            DecisionKind.PostureSelect,
        category:        "Tactical/Posture",
        hysteresisBonus: 0.08f,
        OptionNames = typeof(Posture))]
    public sealed partial class CombatPostureDecision : IUtilityDecisionDefinition
    {
        /// <summary>Builds the decision definition via the fluent builder.</summary>
        public static void Build(IUtilityDecisionBuilder b) => b
            .Option((ushort)Posture.AdvanceAndAttack, ScoringMode.WeightedProduct, o => o
                .Consider(In.HealthFraction(),     0.7f, Curve.Linear)
                .Consider(In.AmmoFraction(),       0.9f, Curve.Threshold)
                .Consider(In.EnemyStrengthRatio(), 0.8f, Curve.InverseLinear))
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
                .Consider(In.HealthFraction(),               1.0f, Curve.Threshold)            // CE-3090: able to run (≥ half health)
                .Consider(In.HealthFraction(),               1.0f, Curve.InverseQuadratic)
                .Consider(In.EqsTopScore(FindSafeRetreatPoint.AssetId), 0.8f, Curve.Linear)
                .Consider(In.EnemyStrengthRatio(),           0.7f, Curve.Logistic))
            .Option((ushort)Posture.HoldProne, ScoringMode.WeightedProduct, o => o
                .Consider(In.HealthFraction(),              0.8f, Curve.InverseLinear)
                .Consider(In.HaveLiveTarget(),              1.0f, Curve.Step)
                .Consider(In.EqsTopScore(FindCoverFromTarget.AssetId),  1.0f, Curve.InverseLinear)   // no cover
                .Consider(In.EnemyStrengthRatio(),          0.6f, Curve.Logistic))
            .Option((ushort)Posture.Hold, ScoringMode.WeightedSum, o => o
                .Consider(In.HealthFraction(), 0.3f, Curve.Linear)
                .Consider(In.Constant(0.2f),   1.0f, Curve.Linear));
    }
}
