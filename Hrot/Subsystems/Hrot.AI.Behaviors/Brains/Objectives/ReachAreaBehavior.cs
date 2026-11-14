using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.ScenarioRules;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains.Objectives
{
    /// <summary>⭐ rules S1 — <c>ReachArea</c> (§10.2): at least <c>minMen</c> of the subject's men inside the area; Failed at the deadline. A thin
    /// behaviour: it reads the census and calls <see cref="ObjectiveRules"/> (through <see cref="ObjectiveHost"/>).</summary>
    public static class ReachAreaBehavior
    {
        public const string Kind = BehaviorNames.ReachArea;

        [StructLayout(LayoutKind.Sequential)]
        public struct Block { public ObjectiveCommonBlock Common; public ObjectiveRun Run; public AreaLink Area; public int MinMen; }

        [StructLayout(LayoutKind.Sequential)]
        public struct ReachAreaBlackboard { public Block Block; }

        [BehaviorResolver(BehaviorNames.ReachArea)]
        public static void Resolve(in ReachAreaParamsJsonDto authored, ref ReachAreaBlackboard bb, EntityRepository world, Entity self)
            => bb.Block = new Block { Common = ObjectiveCommonBlock.From(in authored.Common), Area = authored.Area, MinMen = authored.MinMen <= 0 ? 1 : authored.MinMen };

        [SharedAiAction]
        public static NodeStatus Tick(ref Block b, Entity self, EntityRepository world)
        {
            var f = ObjectiveHost.Enter(world, self, Kind, ObjectiveRole.Objective, in b.Common, ref b.Run);
            if (!f.Ready) return NodeStatus.Running;   // ⭐ CE-4568 — its components land at the merge; it begins next tick
            ref var s = ref world.GetComponentRW<ObjectiveStatus>(self);
            if (f.Evaluate)
            {
                int inside = ObjectiveCensus.MenInside(world, b.Common.Subject, in b.Area), need = b.MinMen;
                bool dirty = inside != s.ProgressCur || need != s.ProgressNeed;
                s.ProgressCur = inside; s.ProgressNeed = need;
                var next = inside >= need ? ObjectiveState.Achieved : ObjectiveRules.DeadlinePassed(in b.Common, f.Now) ? ObjectiveState.Failed : ObjectiveState.Active;
                if (next != ObjectiveState.Active || dirty || s.Reason.Length == 0)
                    ObjectiveHost.Settle(ref s, Kind, next, $"{inside}/{need} men inside", f.Now);
            }
            ObjectiveRules.WriteScore(ref s);
            return NodeStatus.Running;
        }

        [Behavior(BehaviorNames.ReachArea, Params = typeof(ReachAreaParamsJsonDto), Categories = BehaviorCategory.Objective)]
        [BTreeDefinition(BehaviorNames.ReachArea, Curated = true, Group = "Library/Objectives", ParamsType = typeof(ReachAreaBlackboard))]
        public static BTreeBuilder<ReachAreaBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<ReachAreaBlackboard, BTreeContext>().Action(bb => bb.Block, Tick);
    }
}
