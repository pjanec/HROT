using Fdp.Toolkit.Spatial.Eqs;

namespace Fdp.Toolkit.Utility
{
    /// <summary>⭐ <c>CE-3084</c> — how an advancing unit closes on its target (the <see cref="AttackApproachDecision"/> options).</summary>
    public enum Approach : byte
    {
        /// <summary>Straight on: advance to the objective, firing (today's <c>AdvanceAndAttack</c>).</summary>
        Direct         = 1,
        /// <summary>Move to a flank on the target first (<see cref="FindFlankingPosition"/>).</summary>
        Flank          = 2,
        /// <summary>Move to a nearby firing position that sees the target first (<see cref="FindOpenFiringPosition"/>).</summary>
        FiringPosition = 3,
    }

    /// <summary>
    /// ⭐ <c>CE-3084</c> (G6) — scored INSIDE CombatPosture's AdvanceAndAttack branch: with the target in sight the unit goes
    /// straight on; out of sight, with an identified target and a scored position, it manoeuvres to a flank or a firing position
    /// first. With no target it goes straight on (<c>CE-2105</c>: advance without enemy). 📄 docs/DESIGN_Decision_Layer.md §3.3e.
    /// <para>Direct = (in sight + 0.4) / 2 ⇒ 0.7 in sight, 0.2 out of sight. Flank / FiringPosition = a live target × NOT in sight ×
    /// the template's top score — 0 in sight, ≥ 0.2 out of sight whenever the position query found anything.</para>
    /// </summary>
    [UtilityDecision(
        assetId:         "3c6f9e42-5d10-6f3a-ac23-approach00001",
        displayName:     "Attack approach",
        kind:            DecisionKind.PostureSelect,
        category:        "Tactical/Posture",
        hysteresisBonus: 0.08f,
        OptionNames = typeof(Approach))]
    public sealed partial class AttackApproachDecision : IUtilityDecisionDefinition
    {
        /// <summary>Builds the decision definition via the fluent builder.</summary>
        public static void Build(IUtilityDecisionBuilder b) => b
            .Option((ushort)Approach.Direct, ScoringMode.WeightedSum, o => o
                .Consider(In.ThreatInSight(),  1.0f, Curve.Linear)
                .Consider(In.Constant(0.4f),   1.0f, Curve.Linear))
            .Option((ushort)Approach.Flank, ScoringMode.WeightedProduct, o => o
                .Consider(In.HaveLiveTarget(),                             1.0f, Curve.Step)
                .Consider(In.ThreatInSight(),                              1.0f, Curve.InverseLinear)   // not in sight
                .Consider(In.EqsTopScore(FindFlankingPosition.AssetId),    1.0f, Curve.Linear))
            .Option((ushort)Approach.FiringPosition, ScoringMode.WeightedProduct, o => o
                .Consider(In.HaveLiveTarget(),                             1.0f, Curve.Step)
                .Consider(In.ThreatInSight(),                              1.0f, Curve.InverseLinear)   // not in sight
                .Consider(In.EqsTopScore(FindOpenFiringPosition.AssetId),  1.0f, Curve.Linear));
    }
}
