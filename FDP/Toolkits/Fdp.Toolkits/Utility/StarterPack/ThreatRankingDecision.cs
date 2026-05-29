namespace Fdp.Toolkit.Utility
{
    /// <summary>
    /// Starter-pack ThreatRanking decision.
    /// Scores each contact in the agent's TargetMemory and ranks them by threat priority.
    /// </summary>
    [UtilityDecision(
        assetId:     "1a4f7c20-3b9e-4d18-8a01-threat0000001",
        displayName: "Threat ranking",
        kind:        DecisionKind.ThreatRanking,
        category:    "Tactical/Targeting")]
    public sealed partial class ThreatRankingDecision : IUtilityDecisionDefinition
    {
        /// <summary>Builds the decision definition via the fluent builder.</summary>
        public static void Build(IUtilityDecisionBuilder b) => b
            .CandidateOption(ScoringMode.WeightedProduct, o => o
                .Consider(In.HasLineOfSight(),        1.0f, Curve.Step)
                .Consider(In.DistanceToContext(InputContext.Candidate),     0.7f, Curve.Linear)
                .Consider(In.ContactThreatLevel(),    1.0f, Curve.Linear)
                .Consider(In.ContactHealthFraction(), 0.4f, Curve.InverseLinear)
                .Consider(In.IsAssignedTarget(),      0.9f, Curve.Threshold));
    }
}
