using System.Runtime.InteropServices;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Replication;

namespace Fdp.Toolkit.Utility
{
    /// <summary>⭐ <c>CE-2069</c> — which decision a <see cref="UtilityNodes.ChooseOption"/> node scores.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ChooseOptionParams
    {
        /// <summary>The option decision (picked in the Details panel; JSON = its asset id).</summary>
        public UtilityDecisionRef Decision;
    }

    /// <summary>
    /// ⭐ <c>CE-2069</c> — the shared WORKING STATE of one decision: the current winner. <see cref="UtilityNodes.ChooseOption"/>
    /// writes it, every <see cref="UtilityNodes.IsOption"/> branch reads it — they bind the SAME <c>Role=State</c> variable.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UtilityChoice
    {
        /// <summary>The winning option id; 0 = none yet (option ids start at 1).</summary>
        public byte Winner;
    }

    /// <summary>⭐ <c>CE-2069</c> — which option a branch stands for.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct IsOptionParams
    {
        /// <summary>The option id this branch runs for.</summary>
        public byte Option;
    }

    /// <summary>⭐ <c>CE-2069</c> — which ranking decision a <see cref="UtilityNodes.RankCandidates"/> node runs.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RankCandidatesParams
    {
        /// <summary>The ranking decision (e.g. the starter threat ranking).</summary>
        public UtilityDecisionRef Decision;
    }

    /// <summary>⭐ <c>CE-2069</c> — the working state of a ranking: its best candidate and score.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UtilityRanking
    {
        /// <summary>The best candidate (none when nothing ranked, or it has no network id).</summary>
        public EntityRef Top;
        /// <summary>Its score.</summary>
        public float TopScore;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2069</c> — utility scoring as shared BTree / HSM nodes (R-202). 📄 <c>docs/DESIGN_Decision_Layer.md</c> §3.3
    /// "CE-2069 build design". ⭐ The memory of the last winner is the CALLER's working state, so two decisions on one unit
    /// cannot collide and no unit component is needed (the <c>CE-2067</c> scorer core).
    /// </summary>
    public static class UtilityNodes
    {
        /// <summary>Scores the option decision (with the decision's hysteresis on the current winner) and writes the
        /// winner. Running — it keeps deciding (a BTree Parallel arm, an HSM parent state's activity).</summary>
        [SharedAiAction]
        public static NodeStatus ChooseOption(ref ChooseOptionParams p, ref UtilityChoice ws, Entity self, EntityRepository world)
        {
            ws.Winner = Scorer.ChooseOption(world, self, p.Decision.Id, ws.Winner);
            return NodeStatus.Running;
        }

        /// <summary>⭐ <c>CE-2123</c> S2 — <see cref="ChooseOption"/> that DECIDES ONCE: it scores until there is a winner and then
        /// keeps it for the rest of the run (Running, like <see cref="ChooseOption"/>). For a choice that starts something costly
        /// to restart — a squad drill re-partitions and re-asks the terrain — where a mid-run flip would throw the work away.
        /// The run's state is fresh at each start, so the next run decides again.</summary>
        [SharedAiAction]
        public static NodeStatus ChooseOptionOnce(ref ChooseOptionParams p, ref UtilityChoice ws, Entity self, EntityRepository world)
        {
            if (ws.Winner == 0) ws.Winner = Scorer.ChooseOption(world, self, p.Decision.Id, 0);
            return NodeStatus.Running;
        }

        /// <summary>True while the shared winner is this branch's option. Reads only (an HSM guard may be evaluated more
        /// than once per event).</summary>
        [SharedAiCondition]
        public static bool IsOption(ref IsOptionParams p, ref UtilityChoice ws, Entity self, EntityRepository world)
            => ws.Winner != 0 && ws.Winner == p.Option;

        /// <summary>Runs the ranking decision and writes its best candidate. Running, like <see cref="ChooseOption"/>.</summary>
        [SharedAiAction]
        public static NodeStatus RankCandidates(ref RankCandidatesParams p, ref UtilityRanking ws, Entity self, EntityRepository world)
        {
            if (!Scorer.RankCandidates(world, self, p.Decision.Id, 0, out ws.Top, out ws.TopScore))
            {
                ws.Top = EntityRef.None;
                ws.TopScore = 0f;
            }
            return NodeStatus.Running;
        }

        private static UtilityScorer? _scorer;

        private static UtilityScorer Scorer
        {
            get
            {
                UtilityDecisionCatalog.EnsureRegistered();
                return _scorer ??= new UtilityScorer(UtilityDecisionCatalog.Shared);
            }
        }
    }
}
