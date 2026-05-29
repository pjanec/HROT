namespace Fdp.Toolkit.Utility
{
    /// <summary>
    /// Starter-pack LeaderAssignment decision.
    /// Used by a squad leader to score (member, target) pairs for greedy assignment.
    /// self = member entity; context = target entity.
    /// </summary>
    [UtilityDecision(
        assetId:     "4d70af53-6e21-7a4b-bd34-leader00000001",
        displayName: "Leader fire assignment (per member-target pair)",
        kind:        DecisionKind.ThreatRanking,
        category:    "Tactical/Coordination")]
    public sealed partial class LeaderAssignmentDecision : IUtilityDecisionDefinition
    {
        /// <summary>Builds the decision definition via the fluent builder.</summary>
        public static void Build(IUtilityDecisionBuilder b) => b
            .CandidateOption(ScoringMode.WeightedProduct, o => o
                .Consider(In.HasLineOfSight(),     1.0f, Curve.Step)
                .Consider(In.ContactThreatLevel(), 0.9f, Curve.Linear)
                .Consider(In.DistanceToContext(InputContext.Candidate),  0.6f, Curve.InverseLinear));
    }
}
