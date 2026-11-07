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
    /// <summary>⭐ rules S1 — <c>ClearArea</c> (§10.2): no more than maxEnemy enemies inside for forSeconds; Failed at the deadline.</summary>
    public static class ClearAreaObjectiveBehavior
    {
        public const string Kind = BehaviorNames.ClearAreaObjective;

        [StructLayout(LayoutKind.Sequential)]
        public struct Block
        {
            public ObjectiveCommonBlock Common; public ObjectiveRun Run; public AreaLink Area;
            public ForceId EnemySide; public int MaxEnemy; public Fdp.Core.SimDuration ForSeconds;
            public double ClearFor; public int LastEnemy;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ClearAreaObjectiveBlackboard { public Block Block; }

        [BehaviorResolver(BehaviorNames.ClearAreaObjective)]
        public static void Resolve(in ClearAreaObjectiveParamsJsonDto authored, ref ClearAreaObjectiveBlackboard bb, EntityRepository world, Entity self)
            => bb.Block = new Block
            {
                Common = ObjectiveCommonBlock.From(in authored.Common), Area = authored.Area, EnemySide = authored.EnemySide,
                MaxEnemy = authored.MaxEnemy, ForSeconds = authored.ForSeconds <= 0f ? 30f : authored.ForSeconds,
            };

        [SharedAiAction]
        public static NodeStatus Tick(ref Block b, Entity self, EntityRepository world)
        {
            var f = ObjectiveHost.Enter(world, self, Kind, ObjectiveRole.Objective, in b.Common, ref b.Run);
            if (f.Reset) { b.ClearFor = 0; b.LastEnemy = 0; }   // SR-114: a reset clears the working state
            ref var s = ref world.GetComponentRW<ObjectiveStatus>(self);
            if (f.Evaluate)
            {
                var mine = ObjectiveCensus.SideOf(world, b.Common.Subject.Resolve(world));
                int enemy = ObjectiveCensus.EnemiesInside(world, mine, in b.Area, b.EnemySide);
                b.ClearFor = enemy <= b.MaxEnemy ? b.ClearFor + f.Dt : 0;
                int cur = (int)b.ClearFor, need = (int)b.ForSeconds;
                bool dirty = cur != s.ProgressCur || need != s.ProgressNeed || enemy != b.LastEnemy;
                s.ProgressCur = cur; s.ProgressNeed = need; b.LastEnemy = enemy;
                var next = b.ClearFor >= b.ForSeconds ? ObjectiveState.Achieved : ObjectiveRules.DeadlinePassed(in b.Common, f.Now) ? ObjectiveState.Failed : ObjectiveState.Active;
                if (next != ObjectiveState.Active || dirty || s.Reason.Length == 0)
                    ObjectiveHost.Settle(ref s, Kind, next, $"{enemy} enemy inside, clear {cur} of {need} s", f.Now);
            }
            ObjectiveRules.WriteScore(ref s);
            return NodeStatus.Running;
        }

        [Behavior(BehaviorNames.ClearAreaObjective, Params = typeof(ClearAreaObjectiveParamsJsonDto), Categories = BehaviorCategory.Objective)]
        [BTreeDefinition(BehaviorNames.ClearAreaObjective, Curated = true, Group = "Library/Objectives", ParamsType = typeof(ClearAreaObjectiveBlackboard))]
        public static BTreeBuilder<ClearAreaObjectiveBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<ClearAreaObjectiveBlackboard, BTreeContext>().Action(bb => bb.Block, Tick);
    }
}
