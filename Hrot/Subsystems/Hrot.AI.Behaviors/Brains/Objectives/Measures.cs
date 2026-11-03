using System;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.ScenarioRules;
using Fdp.Toolkit.Utility;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains.Objectives
{
    /// <summary>
    /// ⭐ rules S4 E-2 (📄 docs/DESIGN_Scenario_Rules.md §00.11, §10.3) — what every MEASURE of what the subject's men DID shares: it is an objective of role
    /// <see cref="ObjectiveRole.Measure"/> that stays Active (it scores, it never ends), writes <c>Value</c> / <c>Target</c>, and is scored by the ONE <see cref="ObjectiveRules.Score"/>
    /// (<c>lowerIsBetter</c> is that function's flag, not a second formula). ⚠ A measure with NO EVIDENCE yet (no shots, no reports, no readings) scores 0 — "nothing measured" is never a pass.
    /// </summary>
    internal static class MeasureHost
    {
        /// <summary>The reason is rewritten only when the numbers it shows change (a string per tick would be garbage): <c>true</c> when <paramref name="value"/> / <paramref name="n"/> differ from what it last showed.</summary>
        public static bool Changed(ref float shownValue, ref int shownN, float value, int n, in ObjectiveStatus s)
        {
            if (value == shownValue && n == shownN && s.Reason.Length != 0) return false;
            shownValue = value; shownN = n;
            return true;
        }
    }

    // ═══ HitRatio ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary><c>HitRatio</c> — hits ÷ shots by the subject's men (<see cref="MeasureEvidence"/>); default target 0.30. 0 shots ⇒ Value 0 (Active, Score 0 — nothing fired yet is not a pass).</summary>
    public static class HitRatioBehavior
    {
        public const string Kind = BehaviorNames.HitRatio;
        public const float DefaultTarget = 0.30f;

        [StructLayout(LayoutKind.Sequential)]
        public struct Block
        {
            public ObjectiveCommonBlock Common; public ObjectiveRun Run;
            public HarmAttribute Attribute; public float Target;
            public FireTally Tally; public float ShownValue; public int ShownN;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HitRatioBlackboard { public Block Block; }

        [BehaviorResolver(BehaviorNames.HitRatio)]
        public static void Resolve(in HitRatioParamsJsonDto authored, ref HitRatioBlackboard bb, EntityRepository world, Entity self)
            => bb.Block = new Block { Common = ObjectiveCommonBlock.From(in authored.Common), Attribute = authored.Attribute, Target = authored.Target ?? DefaultTarget, ShownValue = float.NaN };

        [SharedAiAction]
        public static NodeStatus Tick(ref Block b, Entity self, EntityRepository world)
        {
            var f = ObjectiveHost.Enter(world, self, Kind, ObjectiveRole.Measure, in b.Common, ref b.Run);
            if (f.Reset) { b.Tally = default; b.ShownValue = float.NaN; }
            ref var s = ref world.GetComponentRW<ObjectiveStatus>(self);
            if (f.Evaluate)
            {
                MeasureEvidence.Update(world, b.Common.Subject.Resolve(world), b.Attribute, ref b.Tally, f.Now);
                s.Target = b.Target;
                s.Value = b.Tally.Shots > 0 ? (float)b.Tally.Hits / b.Tally.Shots : 0f;
                var t = b.Tally;
                if (MeasureHost.Changed(ref b.ShownValue, ref b.ShownN, s.Value, t.Shots * 1000 + t.Hits, in s))
                    ObjectiveHost.Settle(ref s, Kind, ObjectiveState.Active, $"{t.Hits} hits / {t.Shots} shots", f.Now);
            }
            ObjectiveRules.WriteScore(ref s);
            return NodeStatus.Running;
        }

        [Behavior(BehaviorNames.HitRatio, Params = typeof(HitRatioParamsJsonDto), Categories = BehaviorCategory.Objective)]
        [BTreeDefinition(BehaviorNames.HitRatio, Curated = true, Group = "Library/Objectives", ParamsType = typeof(HitRatioBlackboard))]
        public static BTreeBuilder<HitRatioBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<HitRatioBlackboard, BTreeContext>().Action(bb => bb.Block, Tick);
    }

    // ═══ Count ══════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <c>Count { what }</c> — how many shots · hits · kills · assists · fratricide · civiliansHarmed · reports · teamKills · deaths · captures by the subject's men (events BY the subject; ⛔ not <see cref="CounterBehavior"/>, a pool the scenario moves).
    /// fratricide · civiliansHarmed · teamKills · deaths are LOWER-is-better with Target 0 (any is a fault, a score of 0); the others higher-is-better with <c>target</c> (default 1). Assists: rules S4 ⑥ (behaviors); teamKills · deaths · captures: S7 P-3 (backend). All in <see cref="MeasureEvidence"/>.
    /// </summary>
    public static class CountBehavior
    {
        public const string Kind = BehaviorNames.Count;

        [StructLayout(LayoutKind.Sequential)]
        public struct Block
        {
            public ObjectiveCommonBlock Common; public ObjectiveRun Run;
            public CountWhat What; public HarmAttribute Attribute; public float Target, AssistSeconds;
            public FireTally Tally; public int Reports; public float ShownValue; public int ShownN;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CountBlackboard { public Block Block; }

        /// <summary>Whether the counted thing is a fault (lower is better, target 0).</summary>
        public static bool IsFault(CountWhat what) => what is CountWhat.Fratricide or CountWhat.CiviliansHarmed or CountWhat.TeamKills or CountWhat.Deaths;

        [BehaviorResolver(BehaviorNames.Count)]
        public static void Resolve(in CountParamsJsonDto authored, ref CountBlackboard bb, EntityRepository world, Entity self)
            => bb.Block = new Block
            {
                Common = ObjectiveCommonBlock.From(in authored.Common), What = authored.What, Attribute = authored.Attribute,
                Target = IsFault(authored.What) ? 0f : authored.Target ?? 1f, ShownValue = float.NaN,
                AssistSeconds = authored.AssistSeconds > 0f ? authored.AssistSeconds : MeasureEvidence.DefaultAssistSeconds,
            };

        [SharedAiAction]
        public static NodeStatus Tick(ref Block b, Entity self, EntityRepository world)
        {
            var f = ObjectiveHost.Enter(world, self, Kind, ObjectiveRole.Measure, in b.Common, ref b.Run);
            if (f.Reset) { b.Tally = default; b.Reports = 0; b.ShownValue = float.NaN; }
            ref var s = ref world.GetComponentRW<ObjectiveStatus>(self);
            if (f.Evaluate)
            {
                var subject = b.Common.Subject.Resolve(world);
                if (b.What == CountWhat.Reports) { MeasureEvidence.Reports(world, subject, out int n, out _); b.Reports += n; }
                else MeasureEvidence.Update(world, subject, b.Attribute, ref b.Tally, f.Now, b.AssistSeconds);
                s.Target = b.Target;
                s.Value = b.What switch
                {
                    CountWhat.Shots => b.Tally.Shots, CountWhat.Hits => b.Tally.Hits, CountWhat.Kills => b.Tally.Kills,
                    CountWhat.Fratricide => b.Tally.Fratricide, CountWhat.CiviliansHarmed => b.Tally.Civilians,
                    CountWhat.Assists => b.Tally.Assists, CountWhat.TeamKills => b.Tally.TeamKills, CountWhat.Deaths => b.Tally.Deaths, CountWhat.Captures => b.Tally.Captures, _ => b.Reports,
                };
                if (MeasureHost.Changed(ref b.ShownValue, ref b.ShownN, s.Value, 0, in s))
                    ObjectiveHost.Settle(ref s, Kind, ObjectiveState.Active, $"{b.What} {s.Value:0} (target {b.Target:0.##})", f.Now);
            }
            ObjectiveRules.WriteScore(ref s, lowerIsBetter: IsFault(b.What));
            return NodeStatus.Running;
        }

        [Behavior(BehaviorNames.Count, Params = typeof(CountParamsJsonDto), Categories = BehaviorCategory.Objective)]
        [BTreeDefinition(BehaviorNames.Count, Curated = true, Group = "Library/Objectives", ParamsType = typeof(CountBlackboard))]
        public static BTreeBuilder<CountBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<CountBlackboard, BTreeContext>().Action(bb => bb.Block, Tick);
    }

    // ═══ ReportLatency ══════════════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary><c>ReportLatency</c> — the mean of <c>SentAt − ObservedAt</c> of the subject's reports so far (lower is better, default target 60 s). No report yet ⇒ Value 0 and Score 0.</summary>
    public static class ReportLatencyBehavior
    {
        public const string Kind = BehaviorNames.ReportLatency;
        public const float DefaultTarget = 60f;

        [StructLayout(LayoutKind.Sequential)]
        public struct Block
        {
            public ObjectiveCommonBlock Common; public ObjectiveRun Run;
            public float Target; public int Reports; public double LatencySum; public float ShownValue; public int ShownN;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ReportLatencyBlackboard { public Block Block; }

        [BehaviorResolver(BehaviorNames.ReportLatency)]
        public static void Resolve(in ReportLatencyParamsJsonDto authored, ref ReportLatencyBlackboard bb, EntityRepository world, Entity self)
            => bb.Block = new Block { Common = ObjectiveCommonBlock.From(in authored.Common), Target = authored.Target ?? DefaultTarget, ShownValue = float.NaN };

        [SharedAiAction]
        public static NodeStatus Tick(ref Block b, Entity self, EntityRepository world)
        {
            var f = ObjectiveHost.Enter(world, self, Kind, ObjectiveRole.Measure, in b.Common, ref b.Run);
            if (f.Reset) { b.Reports = 0; b.LatencySum = 0; b.ShownValue = float.NaN; }
            ref var s = ref world.GetComponentRW<ObjectiveStatus>(self);
            if (f.Evaluate)
            {
                MeasureEvidence.Reports(world, b.Common.Subject.Resolve(world), out int n, out double sum);
                b.Reports += n; b.LatencySum += sum;
                s.Target = b.Target;
                s.Value = b.Reports > 0 ? (float)(b.LatencySum / b.Reports) : 0f;
                if (MeasureHost.Changed(ref b.ShownValue, ref b.ShownN, s.Value, b.Reports, in s))
                    ObjectiveHost.Settle(ref s, Kind, ObjectiveState.Active, $"mean {s.Value:0.#} s over {b.Reports} report(s) (target {b.Target:0.#})", f.Now);
            }
            ObjectiveRules.WriteScore(ref s, lowerIsBetter: true);
            if (b.Reports == 0) s.Score = 0f;   // nothing reported yet is not a pass
            return NodeStatus.Running;
        }

        [Behavior(BehaviorNames.ReportLatency, Params = typeof(ReportLatencyParamsJsonDto), Categories = BehaviorCategory.Objective)]
        [BTreeDefinition(BehaviorNames.ReportLatency, Curated = true, Group = "Library/Objectives", ParamsType = typeof(ReportLatencyBlackboard))]
        public static BTreeBuilder<ReportLatencyBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<ReportLatencyBlackboard, BTreeContext>().Action(bb => bb.Block, Tick);
    }

    // ═══ TimeTo ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary><c>TimeTo { objective }</c> — the scenario time that objective became Achieved (its <c>ChangedAt</c>); lower is better against <c>target</c> seconds. Until then (and when it Failed or
    /// never will) Value = now and Score 0.</summary>
    public static class TimeToBehavior
    {
        public const string Kind = BehaviorNames.TimeTo;

        [StructLayout(LayoutKind.Sequential)]
        public struct Block
        {
            public ObjectiveCommonBlock Common; public ObjectiveRun Run;
            public EntityRef Objective; public float Target; public byte Achieved; public float ShownValue; public int ShownN;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TimeToBlackboard { public Block Block; }

        [BehaviorResolver(BehaviorNames.TimeTo)]
        public static void Resolve(in TimeToParamsJsonDto authored, ref TimeToBlackboard bb, EntityRepository world, Entity self)
            => bb.Block = new Block { Common = ObjectiveCommonBlock.From(in authored.Common), Objective = authored.Objective.Ref, Target = authored.Target ?? 0f, ShownValue = float.NaN };

        [SharedAiAction]
        public static NodeStatus Tick(ref Block b, Entity self, EntityRepository world)
        {
            var f = ObjectiveHost.Enter(world, self, Kind, ObjectiveRole.Measure, in b.Common, ref b.Run);
            if (f.Reset) { b.Achieved = 0; b.ShownValue = float.NaN; }
            ref var s = ref world.GetComponentRW<ObjectiveStatus>(self);
            if (f.Evaluate)
            {
                s.Target = b.Target;
                var target = b.Objective.Resolve(world);
                if (b.Achieved == 0 && !target.IsNull && world.IsAlive(target) && world.HasComponent<ObjectiveStatus>(target)
                    && world.GetComponentRO<ObjectiveStatus>(target).State == ObjectiveState.Achieved)
                {
                    b.Achieved = 1;
                    s.Value = (float)world.GetComponentRO<ObjectiveStatus>(target).ChangedAt;   // frozen: it was achieved at that scenario time
                }
                if (b.Achieved == 0) s.Value = (float)f.Now;
                if (MeasureHost.Changed(ref b.ShownValue, ref b.ShownN, b.Achieved != 0 ? s.Value : -1f, b.Achieved, in s))
                    ObjectiveHost.Settle(ref s, Kind, ObjectiveState.Active, b.Achieved != 0 ? $"achieved at {s.Value:0.#} s (target {b.Target:0.#})" : "not achieved yet", f.Now);
            }
            ObjectiveRules.WriteScore(ref s, lowerIsBetter: true);
            if (b.Achieved == 0) s.Score = 0f;   // not achieved (yet) is not a pass; the time so far only grows
            return NodeStatus.Running;
        }

        [Behavior(BehaviorNames.TimeTo, Params = typeof(TimeToParamsJsonDto), Categories = BehaviorCategory.Objective)]
        [BTreeDefinition(BehaviorNames.TimeTo, Curated = true, Group = "Library/Objectives", ParamsType = typeof(TimeToBlackboard))]
        public static BTreeBuilder<TimeToBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<TimeToBlackboard, BTreeContext>().Action(bb => bb.Block, Tick);
    }

    // ═══ Sample ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <c>Sample { input, aggregate, threshold }</c> (SR-20) — a registered <c>[UtilityInput("name")]</c> read on the SUBJECT every tick the measure is Active, through
    /// <see cref="UtilityInputReaderStore.TryRead"/> (the reader a consideration evaluates; ⛔ no fourth registry): <c>mean</c> of the readings · <c>max</c> · <c>timeAbove</c> = seconds the reading was
    /// above <c>threshold</c>. Default target 1; <c>lowerIsBetter</c> for an input like stress. No reading yet (no subject, input not registered) ⇒ Value 0, Score 0, and the reason says why.
    /// </summary>
    public static class SampleBehavior
    {
        public const string Kind = BehaviorNames.Sample;

        [StructLayout(LayoutKind.Sequential)]
        public struct Block
        {
            public ObjectiveCommonBlock Common; public ObjectiveRun Run;
            public FixedString64 Input; public SampleAggregate Aggregate; public float Threshold, Target; public byte LowerIsBetter;
            public double Sum, Above; public float Max; public int Readings; public byte Missing; public float ShownValue; public int ShownN;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SampleBlackboard { public Block Block; }

        [BehaviorResolver(BehaviorNames.Sample)]
        public static void Resolve(in SampleParamsJsonDto authored, ref SampleBlackboard bb, EntityRepository world, Entity self)
            => bb.Block = new Block
            {
                Common = ObjectiveCommonBlock.From(in authored.Common), Input = new FixedString64(authored.Input ?? string.Empty), Aggregate = authored.Aggregate,
                Threshold = authored.Threshold, Target = authored.Target ?? 1f, LowerIsBetter = (byte)(authored.LowerIsBetter ? 1 : 0), ShownValue = float.NaN,
            };

        [SharedAiAction]
        public static NodeStatus Tick(ref Block b, Entity self, EntityRepository world)
        {
            var f = ObjectiveHost.Enter(world, self, Kind, ObjectiveRole.Measure, in b.Common, ref b.Run);
            if (f.Reset) { b.Sum = 0; b.Above = 0; b.Max = 0; b.Readings = 0; b.Missing = 0; b.ShownValue = float.NaN; }
            ref var s = ref world.GetComponentRW<ObjectiveStatus>(self);
            if (f.Evaluate)
            {
                s.Target = b.Target;
                var subject = b.Common.Subject.Resolve(world);
                b.Missing = 0;
                if (subject.IsNull || !world.IsAlive(subject) || !UtilityInputReaderStore.TryRead(world, b.Input.ToString(), subject, Entity.Null, out float reading))
                    b.Missing = 1;
                else
                {
                    b.Max = b.Readings == 0 ? reading : Math.Max(b.Max, reading);
                    b.Sum += reading; b.Readings++;
                    if (reading > b.Threshold) b.Above += f.Dt;
                }
                s.Value = b.Readings == 0 ? 0f : b.Aggregate switch
                {
                    SampleAggregate.Max => b.Max,
                    SampleAggregate.TimeAbove => (float)b.Above,
                    _ => (float)(b.Sum / b.Readings),
                };
                if (MeasureHost.Changed(ref b.ShownValue, ref b.ShownN, s.Value, b.Readings * 2 + b.Missing, in s))
                    ObjectiveHost.Settle(ref s, Kind, ObjectiveState.Active,
                        b.Missing != 0 ? $"no reading of '{b.Input}' (no subject, or the input is not registered)" : $"{b.Aggregate} of {b.Input} = {s.Value:0.##} over {b.Readings} reading(s)", f.Now);
            }
            ObjectiveRules.WriteScore(ref s, lowerIsBetter: b.LowerIsBetter != 0);
            if (b.Readings == 0) s.Score = 0f;
            return NodeStatus.Running;
        }

        [Behavior(BehaviorNames.Sample, Params = typeof(SampleParamsJsonDto), Categories = BehaviorCategory.Objective)]
        [BTreeDefinition(BehaviorNames.Sample, Curated = true, Group = "Library/Objectives", ParamsType = typeof(SampleBlackboard))]
        public static BTreeBuilder<SampleBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<SampleBlackboard, BTreeContext>().Action(bb => bb.Block, Tick);
    }
}
