using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.ScenarioRules;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains.Objectives
{
    /// <summary>
    /// ⭐ rules S1 (SR-75, SR-78, SR-115) — what the two composites share: the children are the entities whose <see cref="ObjectiveLinks"/>
    /// <c>Parent</c> is the composite, and every status carries a normalised <c>Score</c> a parent reads from ONE field.
    /// </summary>
    public static class ObjectiveChildren
    {
        public struct Tally
        {
            public int Count, Started, Achieved, Final, FailedRequired, FailedAny, Closed;
            /// <summary>Skipped children that are not required (SR-117: excluded from aggregation).</summary>
            public int SkippedIgnored;
            /// <summary>Σ w·score and Σ w over the scored children (a Skipped one only when required, as 0).</summary>
            public double WeightedScore, Weight;
            public string FirstFailed;
        }

        public static Tally Of(EntityRepository world, Entity self)
        {
            var t = new Tally { FirstFailed = "" };
            foreach (var e in world.Query().With<ObjectiveLinks>().With<ObjectiveStatus>().Build())
            {
                if (e == self || world.GetComponentRO<ObjectiveLinks>(e).Parent.Resolve(world) != self) continue;
                ref readonly var c = ref world.GetComponentRO<ObjectiveStatus>(e);
                t.Count++;
                if (c.State != ObjectiveState.Pending && c.State != ObjectiveState.Inactive) t.Started++;
                if (c.State == ObjectiveState.Achieved) t.Achieved++;
                if (c.State == ObjectiveState.Skipped && c.Required == 0) t.SkippedIgnored++;
                if (c.IsFinal || (c.Role == ObjectiveRole.Measure && c.Closed != 0)) t.Final++;   // ⭐ S4 E-2: a measure never ends by itself; once the exercise closed it its value is frozen = final
                if (c.Closed != 0) t.Closed++;
                bool failed = c.State == ObjectiveState.Failed || (c.State == ObjectiveState.Skipped && c.Required != 0);   // SR-117: a required Skipped counts as failed
                if (failed)
                {
                    t.FailedAny++;
                    if (c.Required != 0) t.FailedRequired++;
                    if (t.FirstFailed.Length == 0) t.FirstFailed = c.Title.ToString();
                }
                if (c.Scored == 0 || (c.State == ObjectiveState.Skipped && c.Required == 0)) continue;
                float w = c.Weight > 0f ? c.Weight : 1f;
                t.WeightedScore += w * (c.State == ObjectiveState.Skipped ? 0f : c.Score);
                t.Weight += w;
            }
            return t;
        }

        /// <summary>⭐ rules S7 P-2 — the points of a composite's children: Σ over the SCORED children of <c>Value × points</c> (<c>points</c> 0 = 1, as <c>weight</c>). The ONE sum <c>Points</c> and <c>MemberEvaluation</c> (aggregate points) share.</summary>
        public static float PointsOf(EntityRepository world, Entity self, out int count)
        {
            double sum = 0; count = 0;
            foreach (var e in world.Query().With<ObjectiveLinks>().With<ObjectiveStatus>().Build())
            {
                if (e == self || world.GetComponentRO<ObjectiveLinks>(e).Parent.Resolve(world) != self) continue;
                ref readonly var c = ref world.GetComponentRO<ObjectiveStatus>(e);
                if (c.Scored == 0) continue;
                sum += c.Value * (c.Points == 0f ? 1f : c.Points);
                count++;
            }
            return (float)sum;
        }

        /// <summary>SR-115: a composite stays Pending until a child is active (or done).</summary>
        public static bool Evaluates(in ObjectiveStatus s) => s.Closed == 0 && (s.State == ObjectiveState.Active || s.State == ObjectiveState.Pending);
    }

    /// <summary>⭐ rules S1 — <c>AllMustPass</c> (§10.2 Composite, mode all): Achieved when every child is Achieved (a non-required Skipped one is
    /// ignored, a required Skipped one is failed); Failed when any child failed.</summary>
    public static class AllMustPassBehavior
    {
        public const string Kind = BehaviorNames.AllMustPass;

        [StructLayout(LayoutKind.Sequential)]
        public struct Block { public ObjectiveCommonBlock Common; public ObjectiveRun Run; }

        [StructLayout(LayoutKind.Sequential)]
        public struct AllMustPassBlackboard { public Block Block; }

        [BehaviorResolver(BehaviorNames.AllMustPass)]
        public static void Resolve(in AllMustPassParamsJsonDto authored, ref AllMustPassBlackboard bb, EntityRepository world, Entity self)
            => bb.Block = new Block { Common = ObjectiveCommonBlock.From(in authored.Common) };

        [SharedAiAction]
        public static NodeStatus Tick(ref Block b, Entity self, EntityRepository world)
        {
            var f = ObjectiveHost.Enter(world, self, Kind, ObjectiveRole.Objective, in b.Common, ref b.Run, ObjectiveHost.CloseMode.Composite);
            ref var s = ref world.GetComponentRW<ObjectiveStatus>(self);
            if (ObjectiveChildren.Evaluates(in s))   // ⭐ not StartActive: a composite that started Inactive and is switched on later (S2) must evaluate
            {
                var t = ObjectiveChildren.Of(world, self);
                int counted = t.Count - t.SkippedIgnored;
                var next = t.Started == 0 ? ObjectiveState.Pending
                         : t.FailedAny > 0 ? ObjectiveState.Failed
                         : counted > 0 && t.Achieved >= counted ? ObjectiveState.Achieved
                         : ObjectiveState.Active;
                s.ProgressCur = t.Achieved; s.ProgressNeed = counted;
                ObjectiveHost.Settle(ref s, Kind, next,
                    next == ObjectiveState.Failed ? $"'{t.FirstFailed}' failed" : $"{t.Achieved}/{t.Count} achieved", f.Now);
            }
            ObjectiveHost.CloseComposite(world, self, ref s, f.Now);
            ObjectiveRules.WriteScore(ref s);
            return NodeStatus.Running;
        }

        [Behavior(BehaviorNames.AllMustPass, Params = typeof(AllMustPassParamsJsonDto), Categories = BehaviorCategory.Objective)]
        [BTreeDefinition(BehaviorNames.AllMustPass, Curated = true, Group = "Library/Objectives", ParamsType = typeof(AllMustPassBlackboard))]
        public static BTreeBuilder<AllMustPassBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<AllMustPassBlackboard, BTreeContext>().Action(bb => bb.Block, Tick);
    }

    /// <summary>⭐ rules S1 — <c>WeightedMean</c> (§10.2 Composite, mode weighted; SR-75): Score = Σ w·score / Σ w over the scored children (Skipped
    /// excluded unless required — then 0). A required child that failed fails the composite with Score 0 (the showcase's "No harm fails the tree").
    /// Achieved when every child is final, none required failed and Score ≥ passScore; Failed when every child is final and Score &lt; passScore.</summary>
    public static class WeightedMeanBehavior
    {
        public const string Kind = BehaviorNames.WeightedMean;
        /// <summary>The pass score when the params give none.</summary>
        public const float DefaultPassScore = 0.6f;

        [StructLayout(LayoutKind.Sequential)]
        public struct Block { public ObjectiveCommonBlock Common; public ObjectiveRun Run; public float PassScore; }

        [StructLayout(LayoutKind.Sequential)]
        public struct WeightedMeanBlackboard { public Block Block; }

        [BehaviorResolver(BehaviorNames.WeightedMean)]
        public static void Resolve(in WeightedMeanParamsJsonDto authored, ref WeightedMeanBlackboard bb, EntityRepository world, Entity self)
            => bb.Block = new Block { Common = ObjectiveCommonBlock.From(in authored.Common), PassScore = authored.PassScore <= 0f ? DefaultPassScore : authored.PassScore };

        [SharedAiAction]
        public static NodeStatus Tick(ref Block b, Entity self, EntityRepository world)
        {
            var f = ObjectiveHost.Enter(world, self, Kind, ObjectiveRole.Objective, in b.Common, ref b.Run, ObjectiveHost.CloseMode.Composite);
            ref var s = ref world.GetComponentRW<ObjectiveStatus>(self);
            if (ObjectiveChildren.Evaluates(in s)) Aggregate(world, self, ref s, b.PassScore, f.Now);
            ObjectiveHost.CloseComposite(world, self, ref s, f.Now);
            return NodeStatus.Running;
        }

        /// <summary>⭐ rules S4 E-1 — the ONE weighted-mean verdict over a composite's children (<c>MemberEvaluation</c> calls it too): the tally, the score, the
        /// state and the line. <paramref name="note"/> is appended to the reason (what the caller itself has to say).</summary>
        public static void Aggregate(EntityRepository world, Entity self, ref ObjectiveStatus s, float passScore, double now, string kind = Kind, string note = "")
        {
            var t = ObjectiveChildren.Of(world, self);
            float score = t.Weight > 0 ? (float)(t.WeightedScore / t.Weight) : 0f;
            bool allFinal = t.Count > 0 && t.Final == t.Count;
            bool pass = t.Weight <= 0 || score >= passScore;
            ObjectiveState next; string reason;
            if (t.Started == 0) { next = ObjectiveState.Pending; reason = "no child active yet"; }
            else if (t.FailedRequired > 0) { next = ObjectiveState.Failed; score = 0f; reason = $"required '{t.FirstFailed}' failed"; }
            else if (allFinal) { next = pass ? ObjectiveState.Achieved : ObjectiveState.Failed; reason = $"score {score:0.00} of {passScore:0.00}"; }
            else { next = ObjectiveState.Active; reason = $"score {score:0.00}, {t.Final}/{t.Count} done"; }
            s.Score = score; s.Value = score; s.Target = passScore;
            s.ProgressCur = t.Achieved; s.ProgressNeed = t.Count;
            ObjectiveHost.Settle(ref s, kind, next, note.Length == 0 ? reason : reason + "; " + note, now);
        }

        [Behavior(BehaviorNames.WeightedMean, Params = typeof(WeightedMeanParamsJsonDto), Categories = BehaviorCategory.Objective)]
        [BTreeDefinition(BehaviorNames.WeightedMean, Curated = true, Group = "Library/Objectives", ParamsType = typeof(WeightedMeanBlackboard))]
        public static BTreeBuilder<WeightedMeanBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<WeightedMeanBlackboard, BTreeContext>().Action(bb => bb.Block, Tick);
    }
}
