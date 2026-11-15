using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.ScenarioRules;
using Fdp.Toolkit.Squad;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains.Objectives
{
    /// <summary>
    /// ⭐ <c>CE-4614</c> (DESIGN_Scenario_Rules §10.2; Command Chain Battalion Test §4 — *"Red below 30 %"*) — <c>Strength</c>: the subject unit's
    /// strength in MEN (<see cref="UnitStrength.Of"/>, the ONE strength seam — a FusionSquad plugs in there) crosses <c>threshold</c>: below it
    /// (default) or, with <c>above</c>, at or above it. Failed at the deadline. A unit with no roster yet is not judged.
    /// </summary>
    public static class StrengthObjectiveBehavior
    {
        public const string Kind = BehaviorNames.StrengthObjective;

        [StructLayout(LayoutKind.Sequential)]
        public struct Block
        {
            public ObjectiveCommonBlock Common; public ObjectiveRun Run;
            public float Threshold; public byte Above; public byte Standing;
            public int LastPercent;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct StrengthObjectiveBlackboard { public Block Block; }

        [BehaviorResolver(BehaviorNames.StrengthObjective)]
        public static void Resolve(in StrengthObjectiveParamsJsonDto authored, ref StrengthObjectiveBlackboard bb, EntityRepository world, Entity self)
            => bb.Block = new Block
            {
                Common = ObjectiveCommonBlock.From(in authored.Common),
                Threshold = authored.Threshold <= 0f ? 0.3f : authored.Threshold,
                Above = authored.Above ? (byte)1 : (byte)0, Standing = authored.Standing ? (byte)1 : (byte)0, LastPercent = -1,
            };

        /// <summary>Whether <paramref name="fraction"/> meets the objective (pure — the rail asks it directly).</summary>
        public static bool Met(float fraction, float threshold, bool above) => above ? fraction >= threshold : fraction < threshold;

        [SharedAiAction]
        public static NodeStatus Tick(ref Block b, Entity self, EntityRepository world)
        {
            var f = ObjectiveHost.Enter(world, self, Kind, ObjectiveRole.Objective, in b.Common, ref b.Run);
            if (!f.Ready) return NodeStatus.Running;
            if (f.Reset) b.LastPercent = -1;
            ref var s = ref world.GetComponentRW<ObjectiveStatus>(self);
            if (f.Evaluate)
            {
                var unit = b.Common.Subject.Resolve(world);
                var st = unit.IsNull || !world.IsAlive(unit) ? default : UnitStrength.Of(world, unit, b.Standing != 0);
                int pct = st.Known ? (int)(st.Fraction * 100f + 0.5f) : -1, need = (int)(b.Threshold * 100f + 0.5f);
                bool dirty = pct != b.LastPercent || s.ProgressNeed != need;
                b.LastPercent = pct; s.ProgressCur = pct < 0 ? 0 : pct; s.ProgressNeed = need;
                var next = st.Known && Met(st.Fraction, b.Threshold, b.Above != 0) ? ObjectiveState.Achieved
                    : ObjectiveRules.DeadlinePassed(in b.Common, f.Now) ? ObjectiveState.Failed : ObjectiveState.Active;
                if (next != ObjectiveState.Active || dirty || s.Reason.Length == 0)
                    ObjectiveHost.Settle(ref s, Kind, next,
                        st.Known ? $"{pct} % of {st.Authorized} men, needs {(b.Above != 0 ? "at least" : "below")} {need} %" : "no roster yet", f.Now);
            }
            ObjectiveRules.WriteScore(ref s);
            return NodeStatus.Running;
        }

        [Behavior(BehaviorNames.StrengthObjective, Params = typeof(StrengthObjectiveParamsJsonDto), Categories = BehaviorCategory.Objective)]
        [BTreeDefinition(BehaviorNames.StrengthObjective, Curated = true, Group = "Library/Objectives", ParamsType = typeof(StrengthObjectiveBlackboard))]
        public static BTreeBuilder<StrengthObjectiveBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<StrengthObjectiveBlackboard, BTreeContext>().Action(bb => bb.Block, Tick);
    }
}
