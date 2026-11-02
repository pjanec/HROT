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
    /// <summary>⭐ rules S1 — <c>HoldArea</c> (§10.2): held = the subject's men inside ≥ minMen AND enemies ≤ maxEnemy, continuously for holdSeconds.
    /// <c>capture</c>: never ends — its Owner is the state (SR-113).</summary>
    public static class HoldAreaBehavior
    {
        public const string Kind = BehaviorNames.HoldArea;

        [StructLayout(LayoutKind.Sequential)]
        public struct Block
        {
            public ObjectiveCommonBlock Common; public ObjectiveRun Run; public AreaLink Area;
            public int MinMen, MaxEnemy; public float HoldSeconds; public byte Capture;
            /// <summary>Working state (recorded with the blackboard): how long it has been held without a break; the last census.</summary>
            public double HeldFor; public int LastMen, LastEnemy;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HoldAreaBlackboard { public Block Block; }

        [BehaviorResolver(BehaviorNames.HoldArea)]
        public static void Resolve(in HoldAreaParamsJsonDto authored, ref HoldAreaBlackboard bb, EntityRepository world, Entity self)
            => bb.Block = new Block
            {
                Common = ObjectiveCommonBlock.From(in authored.Common), Area = authored.Area,
                MinMen = authored.MinMen <= 0 ? 4 : authored.MinMen, MaxEnemy = authored.MaxEnemy,
                HoldSeconds = authored.HoldSeconds <= 0f ? 600f : authored.HoldSeconds, Capture = (byte)(authored.Capture ? 1 : 0),
            };

        [SharedAiAction]
        public static NodeStatus Tick(ref Block b, Entity self, EntityRepository world)
        {
            var f = ObjectiveHost.Enter(world, self, Kind, ObjectiveRole.Objective, in b.Common, ref b.Run);
            ref var s = ref world.GetComponentRW<ObjectiveStatus>(self);
            if (f.Evaluate)
            {
                var subject = b.Common.Subject.Resolve(world);
                var mine = ObjectiveCensus.SideOf(world, subject);
                int men = ObjectiveCensus.MenInside(world, b.Common.Subject, in b.Area);
                int enemy = ObjectiveCensus.EnemiesInside(world, mine, in b.Area);
                bool held = men >= b.MinMen && enemy <= b.MaxEnemy;
                if (b.Capture != 0) Capture(ref b, ref s, world, mine, men, enemy, held, f.Now);
                else
                {
                    b.HeldFor = held ? b.HeldFor + f.Dt : 0;
                    int cur = (int)b.HeldFor, need = (int)b.HoldSeconds;
                    bool dirty = cur != s.ProgressCur || need != s.ProgressNeed || men != b.LastMen || enemy != b.LastEnemy;
                    s.ProgressCur = cur; s.ProgressNeed = need; b.LastMen = men; b.LastEnemy = enemy;
                    var next = b.HeldFor >= b.HoldSeconds ? ObjectiveState.Achieved : ObjectiveRules.DeadlinePassed(in b.Common, f.Now) ? ObjectiveState.Failed : ObjectiveState.Active;
                    if (next != ObjectiveState.Active || dirty || s.Reason.Length == 0)
                        ObjectiveHost.Settle(ref s, Kind, next, $"{men}/{b.MinMen} men, {enemy} enemy, held {cur} of {need} s", f.Now);
                }
            }
            ObjectiveRules.WriteScore(ref s);
            return NodeStatus.Running;
        }

        private static readonly ForceId[] s_sides = { ForceId.Friend, ForceId.Hostile };

        /// <summary>Capture: the Owner is the subject's side while held, the enemy side while an enemy side has ≥ minMen inside and the subject's men
        /// are ≤ maxEnemy, else unchanged. The state stays Active.</summary>
        private static void Capture(ref Block b, ref ObjectiveStatus s, EntityRepository world, ForceId mine, int men, int enemy, bool held, double now)
        {
            var owner = s.Owner;
            if (held) owner = mine;
            else if (men <= b.MaxEnemy)
                foreach (var side in s_sides)
                    if (side != mine && ObjectiveCensus.EnemiesInside(world, mine, in b.Area, side) >= b.MinMen) { owner = side; break; }
            bool dirty = owner != s.Owner || men != b.LastMen || enemy != b.LastEnemy;
            s.Owner = owner; b.LastMen = men; b.LastEnemy = enemy; s.ProgressCur = men; s.ProgressNeed = b.MinMen;
            if (dirty || s.Reason.Length == 0)
                ObjectiveHost.Settle(ref s, Kind, ObjectiveState.Active, $"{men}/{b.MinMen} men, {enemy} enemy, owner {owner}", now);
        }

        [Behavior(BehaviorNames.HoldArea, Params = typeof(HoldAreaParamsJsonDto), Categories = BehaviorCategory.Objective)]
        [BTreeDefinition(BehaviorNames.HoldArea, Curated = true, Group = "Library/Objectives", ParamsType = typeof(HoldAreaBlackboard))]
        public static BTreeBuilder<HoldAreaBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<HoldAreaBlackboard, BTreeContext>().Action(bb => bb.Block, Tick);
    }
}
