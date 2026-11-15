using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.ScenarioRules;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains.Objectives
{
    /// <summary>
    /// ⭐ <c>CE-4614</c> (DESIGN_Scenario_Rules §10.2; closes CE-6018 ①) — <c>QuietArea</c>: no fighting inside an area for <c>forSeconds</c> — no SHOT fired
    /// from inside (<see cref="WeaponFireIntent"/> by a man standing in it), no BURST landing in it (<see cref="RecentDetonations"/>, read by serial so a
    /// burst on a frame the rule skipped still counts) and no HIT on anyone in it (<see cref="DamageAssessedEvent"/>). Failed at the deadline.
    /// ⭐ Events are read through the rule's own window (CE-2383 — the cadence gate keeps it), so a skipped frame loses nothing.
    /// </summary>
    public static class QuietAreaObjectiveBehavior
    {
        public const string Kind = BehaviorNames.QuietAreaObjective;

        [StructLayout(LayoutKind.Sequential)]
        public struct Block
        {
            public ObjectiveCommonBlock Common; public ObjectiveRun Run; public AreaLink Area;
            public Fdp.Core.SimDuration ForSeconds;
            public double QuietFor; public long LastBurst; public byte Started;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct QuietAreaObjectiveBlackboard { public Block Block; }

        [BehaviorResolver(BehaviorNames.QuietAreaObjective)]
        public static void Resolve(in QuietAreaObjectiveParamsJsonDto authored, ref QuietAreaObjectiveBlackboard bb, EntityRepository world, Entity self)
            => bb.Block = new Block
            {
                Common = ObjectiveCommonBlock.From(in authored.Common), Area = authored.Area,
                ForSeconds = authored.ForSeconds <= 0f ? 60f : authored.ForSeconds,
            };

        /// <summary>How many fighting events touched <paramref name="area"/> since the last call (shots from inside, bursts in it, hits on men in it);
        /// <paramref name="lastBurst"/> is the newest burst serial already seen.</summary>
        public static int Noise(EntityRepository world, in AreaLink area, ref long lastBurst)
        {
            int n = 0;
            if (world.Bus.IsRegistered<WeaponFireIntent>())
            {
                var shots = world.ReadEvents<WeaponFireIntent>();
                for (int i = 0; i < shots.Length; i++) if (Inside(world, in area, shots[i].Shooter)) n++;
            }
            if (world.Bus.IsRegistered<DamageAssessedEvent>())
            {
                var hits = world.ReadEvents<DamageAssessedEvent>();
                for (int i = 0; i < hits.Length; i++) if (hits[i].TotalDamage > 0f && Inside(world, in area, hits[i].HitEntity)) n++;
            }
            if (world.HasSingletonUnmanaged<RecentDetonations>())
            {
                ref readonly var m = ref world.GetSingletonUnmanagedRO<RecentDetonations>();
                for (long s = System.Math.Max(lastBurst + 1, m.Count - RecentDetonations.Capacity + 1); s <= m.Count; s++)
                    if (s >= 1 && area.Contains(world, m.Slots[(int)((s - 1) % RecentDetonations.Capacity)].At)) n++;
                lastBurst = m.Count;
            }
            return n;
        }

        private static bool Inside(EntityRepository world, in AreaLink area, Entity e)
            => !e.IsNull && world.IsAlive(e) && world.HasComponent<SimTransform>(e) && area.Contains(world, world.GetComponentRO<SimTransform>(e).Position);

        [SharedAiAction]
        public static NodeStatus Tick(ref Block b, Entity self, EntityRepository world)
        {
            var f = ObjectiveHost.Enter(world, self, Kind, ObjectiveRole.Objective, in b.Common, ref b.Run);
            if (!f.Ready) return NodeStatus.Running;
            if (f.Reset) { b.QuietFor = 0; b.Started = 0; }
            ref var s = ref world.GetComponentRW<ObjectiveStatus>(self);
            if (f.Evaluate)
            {
                if (b.Started == 0) { b.Started = 1; b.LastBurst = RecentDetonations.CountOf(world); }   // bursts before the objective began are history
                int noise = Noise(world, in b.Area, ref b.LastBurst);
                b.QuietFor = noise > 0 ? 0 : b.QuietFor + f.Dt;
                int cur = (int)b.QuietFor, need = (int)b.ForSeconds;
                bool dirty = cur != s.ProgressCur || need != s.ProgressNeed || noise > 0;
                s.ProgressCur = cur; s.ProgressNeed = need;
                var next = b.QuietFor >= b.ForSeconds ? ObjectiveState.Achieved : ObjectiveRules.DeadlinePassed(in b.Common, f.Now) ? ObjectiveState.Failed : ObjectiveState.Active;
                if (next != ObjectiveState.Active || dirty || s.Reason.Length == 0)
                    ObjectiveHost.Settle(ref s, Kind, next, noise > 0 ? $"fighting inside ({noise}), quiet 0 of {need} s" : $"quiet {cur} of {need} s", f.Now);
            }
            ObjectiveRules.WriteScore(ref s);
            return NodeStatus.Running;
        }

        [Behavior(BehaviorNames.QuietAreaObjective, Params = typeof(QuietAreaObjectiveParamsJsonDto), Categories = BehaviorCategory.Objective)]
        [BTreeDefinition(BehaviorNames.QuietAreaObjective, Curated = true, Group = "Library/Objectives", ParamsType = typeof(QuietAreaObjectiveBlackboard))]
        public static BTreeBuilder<QuietAreaObjectiveBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<QuietAreaObjectiveBlackboard, BTreeContext>().Action(bb => bb.Block, Tick);
    }
}
