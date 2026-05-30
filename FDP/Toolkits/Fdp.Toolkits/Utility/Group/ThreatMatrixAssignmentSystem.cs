using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Primitives;

namespace Fdp.Toolkit.Utility
{
    /// <summary>
    /// Scores each (member, target) pair using a registered decision definition and
    /// greedily assigns members to targets while respecting the focus-fire cap.
    /// Writes results into the leader's <see cref="Blackboard1024"/> via
    /// <see cref="ThreatMatrixAssignmentState"/>.
    /// <para>
    /// Algorithm: for each squad member in roster order, iterate all targets from
    /// the leader's <see cref="TargetMemory"/> and pick the highest-scoring target
    /// whose focus-fire count is below the cap. Reads positions and perception data
    /// from each member via the supplied <see cref="UtilityDecisionDef"/>.
    /// </para>
    /// </summary>
    public sealed class ThreatMatrixAssignmentSystem
    {
        private readonly int _decisionId;
        private readonly int _maxFocusFireCount;

        /// <param name="decisionId">
        ///   The integer ID of the decision to use for scoring (e.g.
        ///   <see cref="LeaderAssignmentDecision.Id"/>).
        /// </param>
        /// <param name="maxFocusFireCount">
        ///   Maximum number of squad members that may be assigned to the same target.
        /// </param>
        public ThreatMatrixAssignmentSystem(int decisionId, int maxFocusFireCount = 2)
        {
            _decisionId         = decisionId;
            _maxFocusFireCount  = maxFocusFireCount;
        }

        /// <summary>
        /// Runs the greedy assignment pass for the squad led by <paramref name="leader"/>.
        /// Clears any previous assignment state before writing new assignments.
        /// </summary>
        /// <param name="repo">The entity repository.</param>
        /// <param name="leader">The squad leader entity.</param>
        public unsafe void Run(EntityRepository repo, Entity leader)
        {
            if (!repo.HasComponent<UnitRoster>(leader))     return;
            if (!repo.HasComponent<Blackboard1024>(leader)) return;
            if (!repo.HasComponent<TargetMemory>(leader))   return;

            if (!UtilityDecisionCatalog.Shared.TryGet(_decisionId, out var def, out _) || def == null)
                return;

            ref readonly var roster    = ref repo.GetComponentRO<UnitRoster>(leader);
            ref readonly var leaderMem = ref repo.GetComponentRO<TargetMemory>(leader);
            ref var bb                 = ref repo.GetComponentRW<Blackboard1024>(leader);
            ref var state              = ref SquadCognitiveState.Project(ref bb).Assignment;

            int memberCount = roster.Count;
            if (memberCount <= 0) return;
            int targetCount = leaderMem.Count;
            if (targetCount <= 0) return;

            int maxTargets = targetCount < 16 ? targetCount : 16;
            int maxMembers = memberCount < 16 ? memberCount : 16;

            // Clear previous assignments.
            for (int i = 0; i < maxMembers; i++)
            {
                ref var slot = ref state.GetSlot(i);
                slot.AssignedTargetHandle = 0;
                slot.AssignmentScore      = 0f;
                slot.FocusFireCount       = 0;
            }

            // Build flat score matrix on the stack.
            float* matrixBuf = stackalloc float[maxMembers * maxTargets];
            var tmpBuffer = new UtilityResultBuffer();
            for (int memberIdx = 0; memberIdx < maxMembers; memberIdx++)
            {
                var member = new Entity((ulong)roster.SubordinateEntities[memberIdx]);
                for (int tIdx = 0; tIdx < maxTargets; tIdx++)
                {
                    var target = new Entity((ulong)leaderMem.EntityIds[tIdx]);
                    // Score this (member, target) pair directly via the static scorer.
                    // EvaluateOption will call readers with ctx.Self=member, ctx.Context=target.
                    UtilityScorer.Evaluate(repo, member, in def, target, ref tmpBuffer, null);
                    matrixBuf[memberIdx * maxTargets + tIdx] =
                        tmpBuffer.Count > 0 ? tmpBuffer.GetSpanRO()[0].Score : 0f;
                }
            }

            // Greedy assignment via shared helper.
            int* assignmentsBuf = stackalloc int[maxMembers];
            var assignmentsSpan = new System.Span<int>(assignmentsBuf, maxMembers);
            GreedyMatrixAssigner.Assign(
                new System.ReadOnlySpan<float>(matrixBuf, maxMembers * maxTargets),
                maxMembers, maxTargets, _maxFocusFireCount, assignmentsSpan);

            // Write back results.
            int* focusCount = stackalloc int[maxTargets];
            for (int c = 0; c < maxTargets; c++) focusCount[c] = 0;
            for (int memberIdx = 0; memberIdx < maxMembers; memberIdx++)
            {
                int bestTgtIdx = assignmentsSpan[memberIdx];
                if (bestTgtIdx >= 0)
                {
                    ulong targetHandle = (ulong)leaderMem.EntityIds[bestTgtIdx];
                    state.SetAssignment(memberIdx, targetHandle);
                    state.GetSlot(memberIdx).AssignmentScore = matrixBuf[memberIdx * maxTargets + bestTgtIdx];
                    focusCount[bestTgtIdx]++;
                }
            }

            // Write final FocusFireCount into each slot.
            for (int memberIdx = 0; memberIdx < maxMembers; memberIdx++)
            {
                long handle = state.GetAssignedTarget(memberIdx);
                if (handle == 0) continue;
                for (int tIdx = 0; tIdx < maxTargets; tIdx++)
                {
                    if (leaderMem.EntityIds[tIdx] == handle)
                    {
                        state.GetSlot(memberIdx).FocusFireCount = (byte)focusCount[tIdx];
                        break;
                    }
                }
            }
        }
    }
}
