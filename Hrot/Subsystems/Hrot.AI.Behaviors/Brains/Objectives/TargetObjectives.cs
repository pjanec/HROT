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
    /// <summary>⭐ rules S1 — the target list the two target kinds share (≤ 8 network ids) and what "alive" and "destroyed" mean for them.</summary>
    public static class ObjectiveTargets
    {
        public const int Max = 8;

        public static int Fill(EntityLink[]? from, ref NetIds8 into)
        {
            int n = 0;
            if (from != null) foreach (var t in from) { if (n == Max) break; if (!t.Ref.IsNone) into[n++] = t.Ref.NetworkId; }
            return n;
        }

        /// <summary>Alive and Health above zero (an entity with no Health is alive while it exists).</summary>
        public static bool Standing(EntityRepository world, Entity e)
            => !e.IsNull && world.IsAlive(e) && (!world.IsComponentTypeRegistered<Health>() || !world.HasComponent<Health>(e) || world.GetComponentRO<Health>(e).Current > 0f);

        /// <summary>
        /// Counts the targets that STAND and that are GONE. A target not yet seen on this node (not created / replicated yet) is neither: it is
        /// pending, so a load order never destroys or saves anything. Once seen standing, a vanished target is gone.
        /// </summary>
        public static void Count(EntityRepository world, in NetIds8 ids, int n, ref byte seenMask, out int standing, out int gone)
        {
            standing = 0; gone = 0;
            for (int i = 0; i < n; i++)
            {
                var e = new EntityRef(ids[i]).Resolve(world);
                if (Standing(world, e)) { seenMask |= (byte)(1 << i); standing++; }
                else if (!e.IsNull || (seenMask & (1 << i)) != 0) gone++;
            }
        }
    }

    /// <summary>⭐ rules S1 — <c>DestroyTargets</c> (§10.2): <c>count</c> of the named targets destroyed (not alive, or Health at 0); Failed at the deadline.</summary>
    public static class DestroyTargetsBehavior
    {
        public const string Kind = BehaviorNames.DestroyTargets;

        [StructLayout(LayoutKind.Sequential)]
        public struct Block { public ObjectiveCommonBlock Common; public ObjectiveRun Run; public NetIds8 Targets; public int N, Count; public byte Seen; }

        [StructLayout(LayoutKind.Sequential)]
        public struct DestroyTargetsBlackboard { public Block Block; }

        [BehaviorResolver(BehaviorNames.DestroyTargets)]
        public static void Resolve(in DestroyTargetsParamsJsonDto authored, ref DestroyTargetsBlackboard bb, EntityRepository world, Entity self)
        {
            var b = new Block { Common = ObjectiveCommonBlock.From(in authored.Common), Count = authored.Count };
            b.N = ObjectiveTargets.Fill(authored.Targets, ref b.Targets);
            bb.Block = b;
        }

        [SharedAiAction]
        public static NodeStatus Tick(ref Block b, Entity self, EntityRepository world)
        {
            var f = ObjectiveHost.Enter(world, self, Kind, ObjectiveRole.Objective, in b.Common, ref b.Run);
            if (f.Reset) { b.Seen = 0; }   // SR-114: a reset clears the working state
            ref var s = ref world.GetComponentRW<ObjectiveStatus>(self);
            if (f.Evaluate && b.N > 0)
            {
                ObjectiveTargets.Count(world, in b.Targets, b.N, ref b.Seen, out _, out int gone);
                int need = b.Count > 0 ? System.Math.Min(b.Count, b.N) : b.N;
                bool dirty = gone != s.ProgressCur || need != s.ProgressNeed;
                s.ProgressCur = gone; s.ProgressNeed = need;
                var next = gone >= need ? ObjectiveState.Achieved : ObjectiveRules.DeadlinePassed(in b.Common, f.Now) ? ObjectiveState.Failed : ObjectiveState.Active;
                if (next != ObjectiveState.Active || dirty || s.Reason.Length == 0)
                    ObjectiveHost.Settle(ref s, Kind, next, $"{gone}/{need} destroyed", f.Now);
            }
            ObjectiveRules.WriteScore(ref s);
            return NodeStatus.Running;
        }

        [Behavior(BehaviorNames.DestroyTargets, Params = typeof(DestroyTargetsParamsJsonDto), Categories = BehaviorCategory.Objective)]
        [BTreeDefinition(BehaviorNames.DestroyTargets, Curated = true, Group = "Library/Objectives", ParamsType = typeof(DestroyTargetsBlackboard))]
        public static BTreeBuilder<DestroyTargetsBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<DestroyTargetsBlackboard, BTreeContext>().Action(bb => bb.Block, Tick);
    }

    /// <summary>⭐ rules S1 — <c>Protect</c> (§10.2): at least <c>minSurvivors</c> of the named targets stay alive. Failed at once when fewer survive;
    /// JUDGED AT THE END — at the deadline it is Achieved if still within, else it stays Active (SR-117's OnClose settles it).</summary>
    public static class ProtectBehavior
    {
        public const string Kind = BehaviorNames.Protect;

        [StructLayout(LayoutKind.Sequential)]
        public struct Block { public ObjectiveCommonBlock Common; public ObjectiveRun Run; public NetIds8 Targets; public int N, MinSurvivors; public byte Seen; }

        [StructLayout(LayoutKind.Sequential)]
        public struct ProtectBlackboard { public Block Block; }

        [BehaviorResolver(BehaviorNames.Protect)]
        public static void Resolve(in ProtectParamsJsonDto authored, ref ProtectBlackboard bb, EntityRepository world, Entity self)
        {
            var b = new Block { Common = ObjectiveCommonBlock.From(in authored.Common), MinSurvivors = authored.MinSurvivors };
            b.N = ObjectiveTargets.Fill(authored.Targets, ref b.Targets);
            bb.Block = b;
        }

        [SharedAiAction]
        public static NodeStatus Tick(ref Block b, Entity self, EntityRepository world)
        {
            var f = ObjectiveHost.Enter(world, self, Kind, ObjectiveRole.Objective, in b.Common, ref b.Run);
            if (f.Reset) { b.Seen = 0; }   // SR-114: a reset clears the working state
            ref var s = ref world.GetComponentRW<ObjectiveStatus>(self);
            if (f.Evaluate && b.N > 0)
            {
                ObjectiveTargets.Count(world, in b.Targets, b.N, ref b.Seen, out int standing, out int gone);
                int survivors = b.N - gone, need = b.MinSurvivors > 0 ? System.Math.Min(b.MinSurvivors, b.N) : b.N;
                bool dirty = survivors != s.ProgressCur || need != s.ProgressNeed;
                s.ProgressCur = survivors; s.ProgressNeed = need;
                var next = survivors < need ? ObjectiveState.Failed : ObjectiveRules.DeadlinePassed(in b.Common, f.Now) ? ObjectiveState.Achieved : ObjectiveState.Active;
                if (next != ObjectiveState.Active || dirty || s.Reason.Length == 0)
                    ObjectiveHost.Settle(ref s, Kind, next, $"{survivors}/{need} survive", f.Now);
            }
            ObjectiveRules.WriteScore(ref s, judgedAtEnd: true);
            return NodeStatus.Running;
        }

        [Behavior(BehaviorNames.Protect, Params = typeof(ProtectParamsJsonDto), Categories = BehaviorCategory.Objective)]
        [BTreeDefinition(BehaviorNames.Protect, Curated = true, Group = "Library/Objectives", ParamsType = typeof(ProtectBlackboard))]
        public static BTreeBuilder<ProtectBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<ProtectBlackboard, BTreeContext>().Action(bb => bb.Block, Tick);
    }
}
