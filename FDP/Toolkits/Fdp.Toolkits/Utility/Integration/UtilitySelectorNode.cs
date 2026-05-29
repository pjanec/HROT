using Fdp.Core;

namespace Fdp.Toolkit.Utility.Integration
{
    /// <summary>
    /// BTree integration helper: evaluates a utility decision and selects one of N branches
    /// based on score. Hysteresis prevents rapid branch switching.
    ///
    /// Typical BTree usage:
    ///   var selector = new UtilitySelectorNode(scorer, decisionId, new[]{ PostureA, PostureB });
    ///   // Inside [BTreeCondition] for branch i:
    ///   return selector.IsActiveBranch(repo, entity, branchIndex: i);
    /// </summary>
    public sealed class UtilitySelectorNode
    {
        private readonly UtilityScorer _scorer;
        private readonly int           _decisionId;
        private readonly byte[]        _optionIds;       // ordered branch option IDs
        private int                    _activeBranch;    // index of last winning branch (-1 = none)

        public UtilitySelectorNode(UtilityScorer scorer, int decisionId, byte[] optionIds)
        {
            _scorer      = scorer;
            _decisionId  = decisionId;
            _optionIds   = optionIds;
            _activeBranch = -1;
        }

        /// <summary>
        /// Re-scores the decision and returns the 0-based index of the branch that should run.
        /// Applies <paramref name="hysteresisBonus"/> to the currently active branch.
        /// Returns -1 if the decision is not registered.
        /// </summary>
        public int SelectBranch(EntityRepository repo, Entity entity,
                                float hysteresisBonus = 0.08f, ushort tick = 0)
        {
            _scorer.Evaluate(repo, entity, _decisionId, context: default, tick: tick);

            ref readonly var buf = ref repo.GetComponentRO<UtilityResultBuffer>(entity);
            if (buf.Count == 0) return _activeBranch;

            int bestBranch = -1;
            float bestScore = -1f;
            for (int i = 0; i < _optionIds.Length; i++)
            {
                float s = ScoreForOption(in buf, _optionIds[i]);
                if (i == _activeBranch) s += hysteresisBonus;   // boost active branch
                if (s > bestScore) { bestScore = s; bestBranch = i; }
            }
            _activeBranch = bestBranch;
            return bestBranch;
        }

        /// <summary>
        /// Returns true iff <paramref name="branchIndex"/> is the currently active branch
        /// after calling <see cref="SelectBranch"/> with the same arguments.
        /// </summary>
        public bool IsActiveBranch(EntityRepository repo, Entity entity,
                                   int branchIndex, float hysteresisBonus = 0.08f, ushort tick = 0)
            => SelectBranch(repo, entity, hysteresisBonus, tick) == branchIndex;

        private static float ScoreForOption(ref readonly UtilityResultBuffer buf, byte optionId)
        {
            var span = buf.GetSpanRO();
            for (int i = 0; i < buf.Count; i++)
                if (span[i].WinningPostureId == optionId) return span[i].Score;
            return 0f;
        }
    }
}
