using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
    /// Writes results into the leader's <see cref="SquadCognitiveState"/> via
    /// <see cref="ThreatMatrixAssignmentState"/>.
    /// <para>
    /// Algorithm: for each squad member in roster order, iterate all targets and pick the highest-scoring target
    /// whose focus-fire count is below the cap. Reads positions and perception data
    /// from each member via the supplied <see cref="UtilityDecisionDef"/>.
    /// </para>
    /// <para>⭐ <c>CE-3088</c> (F4) — the TARGETS are the squad's merged pool (<see cref="SquadCognitiveState.Contacts"/>,
    /// what Squad Coordination §4 says the leader's fire allocation reads) ∪ the leader's own identified memory (the merge
    /// walks subordinates only), deduplicated, at most 16; heard (anonymous) contacts are never assignable.
    /// 📄 docs/DESIGN_Utility_AI_Demo_Scenarios.md §11. Called by <c>SquadCoordinationSystem</c> after each merge.</para>
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
            if (!repo.HasComponent<SquadCognitiveState>(leader)) return;

            if (!UtilityDecisionCatalog.Shared.TryGet(_decisionId, out var def, out _) || def == null)
                return;

            ref readonly var roster    = ref repo.GetComponentRO<UnitRoster>(leader);
            ref var cognitive          = ref repo.GetComponentRW<SquadCognitiveState>(leader);
            ref var state              = ref cognitive.Assignment;

            int memberCount = roster.Count;
            if (memberCount <= 0) return;
            int maxMembers = memberCount < 16 ? memberCount : 16;

            // ⭐ CE-3088 — the targets: the merged pool's identified contacts, then the leader's own identified memory.
            long* targets = stackalloc long[16];
            int maxTargets = 0;
            var pool = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<SquadContactPoolSlots, SquadContact>(ref cognitive.Contacts.Contacts), 16);
            for (int i = 0; i < cognitive.Contacts.Count && i < 16 && maxTargets < 16; i++)
            {
                // ⭐ CE-3063 ② — a heard contact cannot be assigned as a target to fire at.
                if ((pool[i].Flags & SquadContact.AnonymousFlag) != 0 || pool[i].EntityId <= 0) continue;
                AddUnique(targets, ref maxTargets, pool[i].EntityId);
            }
            if (repo.HasComponent<TargetMemory>(leader))
            {
                ref readonly var leaderMem = ref repo.GetComponentRO<TargetMemory>(leader);
                for (int i = 0; i < leaderMem.Count && maxTargets < 16; i++)
                    if (!TargetMemory.IsAnonymous(in leaderMem, i)) AddUnique(targets, ref maxTargets, leaderMem.EntityIds[i]);
            }

            // Clear previous assignments — also when there is nothing to assign (a stale slot would keep a dead order).
            for (int i = 0; i < maxMembers; i++)
            {
                ref var slot = ref state.GetSlot(i);
                slot.AssignedTargetHandle = 0;
                slot.AssignmentScore      = 0f;
                slot.FocusFireCount       = 0;
            }
            if (maxTargets <= 0) return;

            // Build flat score matrix on the stack.
            float* matrixBuf = stackalloc float[maxMembers * maxTargets];
            var tmpBuffer = new UtilityResultBuffer();
            for (int memberIdx = 0; memberIdx < maxMembers; memberIdx++)
            {
                var member = roster.SubordinateEntities[memberIdx];
                for (int tIdx = 0; tIdx < maxTargets; tIdx++)
                {
                    var target = new Entity((ulong)targets[tIdx]);
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
                    ulong targetHandle = (ulong)targets[bestTgtIdx];
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
                    if (targets[tIdx] == handle)
                    {
                        state.GetSlot(memberIdx).FocusFireCount = (byte)focusCount[tIdx];
                        break;
                    }
                }
            }
        }

        private static unsafe void AddUnique(long* targets, ref int count, long id)
        {
            for (int i = 0; i < count; i++) if (targets[i] == id) return;
            targets[count++] = id;
        }
    }
}
