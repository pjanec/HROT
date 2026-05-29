using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Utility;

namespace Fdp.Toolkit.Utility.Integration
{
    /// <summary>
    /// Static helpers for Blueprint-generated code to call the Utility AI scorer
    /// via the <see cref="ISimulationView"/> interface.
    /// Called from generated Blueprint instance code; no allocations on the hot path.
    /// </summary>
    public static class UtilityBlueprintBridge
    {
        /// <summary>
        /// Runs the utility decision identified by <paramref name="decisionId"/> for
        /// <paramref name="self"/> and returns the winning posture option byte.
        /// Returns 0 if the decision is not found or the buffer is empty.
        /// </summary>
        public static byte ScoreDecision(ISimulationView view, Entity self, int decisionId, uint tick)
        {
            if (view is not EntityRepository repo) return 0;
            if (!repo.HasComponent<UtilityResultBuffer>(self)) return 0;

            var scorer = new UtilityScorer(UtilityDecisionCatalog.Shared);
            return (byte)scorer.SelectPosture(repo, self, decisionId, (ushort)tick);
        }

        /// <summary>
        /// Reads rank-<paramref name="rank"/> entry from the entity's
        /// <see cref="UtilityResultBuffer"/>.
        /// Returns (0, 0f, false) if the buffer is absent or rank is out of range.
        /// </summary>
        public static (long candidateHandle, float score, bool isValid)
            ReadRankedResult(ISimulationView view, Entity self, int rank)
        {
            if (!view.HasComponent<UtilityResultBuffer>(self))
                return default;
            ref readonly var buf = ref view.GetComponentRO<UtilityResultBuffer>(self);
            if (rank < 0 || rank >= buf.Count) return default;
            var e = buf.GetSpanRO()[rank];
            return (e.CandidateHandle, e.Score, true);
        }
    }
}
