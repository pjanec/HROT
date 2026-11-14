using Fdp.Toolkit.Behavior;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains.Objectives
{
    /// <summary>
    /// ⭐ rules ② (📄 docs/DESIGN_Scenario_Rules.md §00.11a, L2 / L3 / R4) — the STATE KEEPERS of the rule kinds that can be edited live. On a live edit the kind's own resolver
    /// parses the new params into <c>fresh</c> (exactly as a load does), and the keeper copies ONLY the runtime fields — the run, the clocks, the tallies, the served sets — from the
    /// running blackboard <c>old</c>; the authored fields stay the new ones. Each is a few lines with the field names written out (⛔ never a by-name reflection copy), discovered by
    /// <c>BehaviorSchemaDiscovery.BindKeepers</c> beside the validators. ⭐ A kind WITHOUT a keeper refuses a live edit ("restart the rule") — a new runtime field a kind grows must be
    /// added here, or an edit silently resets it (the rails pin the kinds that keep state).
    /// <para>Not kept, on purpose: a Trigger's V-1 links (<c>DependsOn</c> / <c>ActsOn</c>) — they are DERIVED from the <c>when</c> / <c>do</c> JSON, which an edit may change, so the freshly
    /// resolved ones are the right ones. Not editable live (no keeper): NoHarm, Points, Best, Bounty.</para>
    /// </summary>
    public static class ObjectiveStateKeepers
    {
        [BehaviorStateKeeper(BehaviorNames.Trigger)]
        public static void KeepTrigger(ref TriggerBehavior.TriggerBlackboard fresh, in TriggerBehavior.TriggerBlackboard old)
        {
            ref var f = ref fresh.Block; ref readonly var o = ref old.Block;
            f.Run = o.Run; f.Phase = o.Phase; f.Prev = o.Prev; f.PrevKnown = o.PrevKnown;
            f.EdgeAt = o.EdgeAt; f.WaitUntil = o.WaitUntil; f.CooldownUntil = o.CooldownUntil;
            f.ListParams = o.ListParams; f.List = o.List;
        }

        [BehaviorStateKeeper(BehaviorNames.HoldArea)]
        public static void KeepHoldArea(ref HoldAreaBehavior.HoldAreaBlackboard fresh, in HoldAreaBehavior.HoldAreaBlackboard old)
        {
            ref var f = ref fresh.Block; ref readonly var o = ref old.Block;
            f.Run = o.Run; f.HeldFor = o.HeldFor; f.LastMen = o.LastMen; f.LastEnemy = o.LastEnemy; f.Challenger = o.Challenger; f.AdvantageFor = o.AdvantageFor;
        }

        [BehaviorStateKeeper(BehaviorNames.ReachArea)]
        public static void KeepReachArea(ref ReachAreaBehavior.ReachAreaBlackboard fresh, in ReachAreaBehavior.ReachAreaBlackboard old)
            => fresh.Block.Run = old.Block.Run;

        [BehaviorStateKeeper(BehaviorNames.ClearAreaObjective)]
        public static void KeepClearArea(ref ClearAreaObjectiveBehavior.ClearAreaObjectiveBlackboard fresh, in ClearAreaObjectiveBehavior.ClearAreaObjectiveBlackboard old)
        {
            ref var f = ref fresh.Block; ref readonly var o = ref old.Block;
            f.Run = o.Run; f.ClearFor = o.ClearFor; f.LastEnemy = o.LastEnemy;
        }

        // ── the measures ── (a Counter's Value lives on its status, which an edit never touches; `Started` stops the start value being written again)
        [BehaviorStateKeeper(BehaviorNames.Counter)]
        public static void KeepCounter(ref CounterBehavior.CounterBlackboard fresh, in CounterBehavior.CounterBlackboard old)
        {
            ref var f = ref fresh.Block; ref readonly var o = ref old.Block;
            f.Run = o.Run; f.LastDowns0 = o.LastDowns0; f.LastDowns1 = o.LastDowns1; f.Started = o.Started; f.Shown = o.Shown;
        }

        [BehaviorStateKeeper(BehaviorNames.HitRatio)]
        public static void KeepHitRatio(ref HitRatioBehavior.HitRatioBlackboard fresh, in HitRatioBehavior.HitRatioBlackboard old)
        {
            ref var f = ref fresh.Block; ref readonly var o = ref old.Block;
            f.Run = o.Run; f.Tally = o.Tally; f.ShownValue = o.ShownValue; f.ShownN = o.ShownN;
        }

        [BehaviorStateKeeper(BehaviorNames.Count)]
        public static void KeepCount(ref CountBehavior.CountBlackboard fresh, in CountBehavior.CountBlackboard old)
        {
            ref var f = ref fresh.Block; ref readonly var o = ref old.Block;
            f.Run = o.Run; f.Tally = o.Tally; f.Reports = o.Reports; f.ShownValue = o.ShownValue; f.ShownN = o.ShownN;
        }

        [BehaviorStateKeeper(BehaviorNames.ReportLatency)]
        public static void KeepReportLatency(ref ReportLatencyBehavior.ReportLatencyBlackboard fresh, in ReportLatencyBehavior.ReportLatencyBlackboard old)
        {
            ref var f = ref fresh.Block; ref readonly var o = ref old.Block;
            f.Run = o.Run; f.Reports = o.Reports; f.LatencySum = o.LatencySum; f.ShownValue = o.ShownValue; f.ShownN = o.ShownN;
        }

        [BehaviorStateKeeper(BehaviorNames.TimeTo)]
        public static void KeepTimeTo(ref TimeToBehavior.TimeToBlackboard fresh, in TimeToBehavior.TimeToBlackboard old)
        {
            ref var f = ref fresh.Block; ref readonly var o = ref old.Block;
            f.Run = o.Run; f.Achieved = o.Achieved; f.ShownValue = o.ShownValue; f.ShownN = o.ShownN;
        }

        [BehaviorStateKeeper(BehaviorNames.Sample)]
        public static void KeepSample(ref SampleBehavior.SampleBlackboard fresh, in SampleBehavior.SampleBlackboard old)
        {
            ref var f = ref fresh.Block; ref readonly var o = ref old.Block;
            f.Run = o.Run; f.Sum = o.Sum; f.Above = o.Above; f.Max = o.Max; f.Readings = o.Readings; f.Missing = o.Missing; f.ShownValue = o.ShownValue; f.ShownN = o.ShownN;
        }

        // ── the aggregators ──
        [BehaviorStateKeeper(BehaviorNames.AllMustPass)]
        public static void KeepAllMustPass(ref AllMustPassBehavior.AllMustPassBlackboard fresh, in AllMustPassBehavior.AllMustPassBlackboard old)
            => fresh.Block.Run = old.Block.Run;

        [BehaviorStateKeeper(BehaviorNames.WeightedMean)]
        public static void KeepWeightedMean(ref WeightedMeanBehavior.WeightedMeanBlackboard fresh, in WeightedMeanBehavior.WeightedMeanBlackboard old)
            => fresh.Block.Run = old.Block.Run;

        [BehaviorStateKeeper(BehaviorNames.SelectionMean)]
        public static void KeepSelectionMean(ref SelectionMeanBehavior.SelectionMeanBlackboard fresh, in SelectionMeanBehavior.SelectionMeanBlackboard old)
        {
            ref var f = ref fresh.Block; ref readonly var o = ref old.Block;
            f.Run = o.Run; f.Roots = o.Roots; f.RootCount = o.RootCount;
        }

        [BehaviorStateKeeper(BehaviorNames.MemberEvaluation)]
        public static void KeepMemberEvaluation(ref MemberEvaluationBehavior.MemberEvaluationBlackboard fresh, in MemberEvaluationBehavior.MemberEvaluationBlackboard old)
        {
            ref var f = ref fresh.Block; ref readonly var o = ref old.Block;
            f.Run = o.Run; f.Served = o.Served; f.ServedCount = o.ServedCount; f.Unmatched = o.Unmatched;
            f.RoleMeans = o.RoleMeans; f.StandardMeans = o.StandardMeans; f.RoleMeanCount = o.RoleMeanCount; f.StandardMeanCount = o.StandardMeanCount;
        }
    }
}
