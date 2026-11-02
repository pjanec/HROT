using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.ScenarioRules;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains.Objectives
{
    /// <summary>⭐ rules S1-f — <c>NoHarm</c> (§10.2, a constraint): each tick it reads this frame's <see cref="DamageAssessedEvent"/>s (the
    /// <c>HealthApplicationSystem</c> way) and counts DISTINCT victims of the <c>protected</c> affiliation harmed by the side (default: the subject's;
    /// <c>anySide</c> for anybody). Failed when more than <c>maxCount</c>; JUDGED AT THE END (at the deadline Achieved). ⚠ Harm with no known source
    /// (a cloud, a crash) counts only for <c>anySide</c> (§10.1: drones / gas wait for CE-4155).</summary>
    public static class NoHarmBehavior
    {
        public const string Kind = BehaviorNames.NoHarm;

        [StructLayout(LayoutKind.Sequential)]
        public struct Block
        {
            public ObjectiveCommonBlock Common; public ObjectiveRun Run;
            public ForceId Protected, BySide; public byte AnySide; public int MaxCount;
            /// <summary>Working state: the victims counted so far (16 slots; beyond them harm is counted without dedupe).</summary>
            public Ids16 Victims; public int Counted, Overflow;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NoHarmBlackboard { public Block Block; }

        [BehaviorResolver(BehaviorNames.NoHarm)]
        public static void Resolve(in NoHarmParamsJsonDto authored, ref NoHarmBlackboard bb, EntityRepository world, Entity self)
            => bb.Block = new Block
            {
                Common = ObjectiveCommonBlock.From(in authored.Common), Protected = authored.Protected, BySide = authored.BySide,
                AnySide = (byte)(authored.AnySide ? 1 : 0), MaxCount = authored.MaxCount,
            };

        [SharedAiAction]
        public static NodeStatus Tick(ref Block b, Entity self, EntityRepository world)
        {
            var f = ObjectiveHost.Enter(world, self, Kind, ObjectiveRole.Objective, in b.Common, ref b.Run);
            ref var s = ref world.GetComponentRW<ObjectiveStatus>(self);
            if (f.Evaluate)
            {
                var bySide = b.BySide != ForceId.Neutral ? b.BySide : ObjectiveCensus.SideOf(world, b.Common.Subject.Resolve(world));
                int before = b.Counted + b.Overflow;
                var events = world.ReadEvents<DamageAssessedEvent>();
                for (int i = 0; i < events.Length; i++)
                {
                    ref readonly var e = ref events[i];
                    if (e.TotalDamage <= 0f || e.HitEntity.IsNull || ObjectiveCensus.SideOf(world, e.HitEntity) != b.Protected) continue;
                    if (b.AnySide == 0 && (e.Source.IsNull || ObjectiveCensus.SideOf(world, e.Source) != bySide)) continue;
                    Count(ref b, (long)e.HitEntity.PackedValue);
                }
                int harmed = b.Counted + b.Overflow;
                bool dirty = harmed != before || s.ProgressNeed != b.MaxCount;
                s.ProgressCur = harmed; s.ProgressNeed = b.MaxCount;
                var next = harmed > b.MaxCount ? ObjectiveState.Failed : ObjectiveRules.DeadlinePassed(in b.Common, f.Now) ? ObjectiveState.Achieved : ObjectiveState.Active;
                if (next != ObjectiveState.Active || dirty || s.Reason.Length == 0)
                    ObjectiveHost.Settle(ref s, Kind, next, $"{harmed} harmed of {b.MaxCount} allowed", f.Now);
            }
            ObjectiveRules.WriteScore(ref s, judgedAtEnd: true);
            return NodeStatus.Running;
        }

        private static void Count(ref Block b, long victim)
        {
            for (int i = 0; i < b.Counted; i++) if (b.Victims[i] == victim) return;
            if (b.Counted < 16) b.Victims[b.Counted++] = victim; else b.Overflow++;
        }

        [Behavior(BehaviorNames.NoHarm, Params = typeof(NoHarmParamsJsonDto), Categories = BehaviorCategory.Objective)]
        [BTreeDefinition(BehaviorNames.NoHarm, Curated = true, Group = "Library/Objectives", ParamsType = typeof(NoHarmBlackboard))]
        public static BTreeBuilder<NoHarmBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<NoHarmBlackboard, BTreeContext>().Action(bb => bb.Block, Tick);
    }
}
