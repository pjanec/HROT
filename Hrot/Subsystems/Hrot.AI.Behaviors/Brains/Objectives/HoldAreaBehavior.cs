using System;
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
            /// <summary>⭐ S6 G-1 — <c>captureSeconds</c> (0 = the S1 rule); the side whose advantage is being timed; the seconds it has been the one with more men.</summary>
            public float CaptureSeconds; public ForceId Challenger; public double AdvantageFor;
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
                CaptureSeconds = Math.Max(0f, authored.CaptureSeconds),
            };

        [SharedAiAction]
        public static NodeStatus Tick(ref Block b, Entity self, EntityRepository world)
        {
            var f = ObjectiveHost.Enter(world, self, Kind, ObjectiveRole.Objective, in b.Common, ref b.Run, capture: b.Capture != 0);
            if (f.Reset) { b.HeldFor = 0; b.LastMen = 0; b.LastEnemy = 0; b.Challenger = ForceId.Neutral; b.AdvantageFor = 0; }   // SR-114: a reset clears the working state
            ref var s = ref world.GetComponentRW<ObjectiveStatus>(self);
            if (f.Evaluate)
            {
                if (b.Capture != 0 && b.CaptureSeconds > 0f)   // ⭐ S6 G-1: the point is taken BY SIDE, not by the subject's roster
                {
                    CaptureBySide(ref b, ref s, world, self, f.Dt, f.Now);
                    ObjectiveRules.WriteScore(ref s);
                    return NodeStatus.Running;
                }
                var subject = b.Common.Subject.Resolve(world);
                var mine = ObjectiveCensus.SideOf(world, subject);
                int men = ObjectiveCensus.MenInside(world, b.Common.Subject, in b.Area);
                int enemy = ObjectiveCensus.EnemiesInside(world, mine, in b.Area);
                bool held = men >= b.MinMen && enemy <= b.MaxEnemy;
                if (b.Capture != 0) Capture(ref b, ref s, world, mine, men, enemy, held, f.Dt, f.Now);
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
        private static void Capture(ref Block b, ref ObjectiveStatus s, EntityRepository world, ForceId mine, int men, int enemy, bool held, double dt, double now)
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

        /// <summary>
        /// ⭐ S6 G-1 (📄 DESIGN_Scenario_Rules §00.12) — capture BY SIDE with a clock. The leader = the side with strictly more men inside than the other (every man of the
        /// side) and at least <c>minMen</c>. The owner is lost AT ONCE (→ Neutral) when the other side outnumbers it; a leader that is not the owner accumulates
        /// <c>AdvantageFor</c> (reset when the leader changes or there is none) and becomes the Owner at <c>captureSeconds</c>. The state stays Active.
        /// </summary>
        private static void CaptureBySide(ref Block b, ref ObjectiveStatus s, EntityRepository world, Entity self, double dt, double now)
        {
            int f = ObjectiveCensus.MenOfSideInside(world, ForceId.Friend, in b.Area), h = ObjectiveCensus.MenOfSideInside(world, ForceId.Hostile, in b.Area);
            var leader = f > h && f >= b.MinMen ? ForceId.Friend : h > f && h >= b.MinMen ? ForceId.Hostile : ForceId.Neutral;
            var owner = s.Owner;
            if (owner == ForceId.Friend && h > f) owner = ForceId.Neutral;          // lost: outnumbered
            else if (owner == ForceId.Hostile && f > h) owner = ForceId.Neutral;
            if (leader == ForceId.Neutral) { b.Challenger = ForceId.Neutral; b.AdvantageFor = 0; }
            else if (leader == owner) { b.Challenger = leader; b.AdvantageFor = b.CaptureSeconds; }   // held: the clock reads full
            else
            {
                if (b.Challenger != leader) { b.Challenger = leader; b.AdvantageFor = 0; }
                b.AdvantageFor += dt;
                if (b.AdvantageFor >= b.CaptureSeconds - 1e-9) { owner = leader; b.AdvantageFor = b.CaptureSeconds; }
            }
            int cur = (int)b.AdvantageFor, need = (int)b.CaptureSeconds;
            bool dirty = owner != s.Owner || f != b.LastMen || h != b.LastEnemy || cur != s.ProgressCur || need != s.ProgressNeed;
            if (owner != s.Owner && owner != ForceId.Neutral) PublishCaptured(world, self, owner, in b.Area);   // ⭐ S7 P-4: the moment, once
            s.Owner = owner; b.LastMen = f; b.LastEnemy = h; s.ProgressCur = cur; s.ProgressNeed = need;
            if (dirty || s.Reason.Length == 0)
                ObjectiveHost.Settle(ref s, Kind, ObjectiveState.Active, $"F {f} / H {h} men, owner {owner}, {leader} {b.AdvantageFor:0}/{b.CaptureSeconds:0} s", now);
        }

        private static readonly System.Collections.Generic.List<Entity> s_inside = new();

        /// <summary>⭐ S7 P-4 (§00.13) — the point changed hands to <paramref name="side"/>: raise <see cref="ObjectiveCapturedEvent"/> naming that side's men inside now (≤ 16; the census test, once).</summary>
        private static void PublishCaptured(EntityRepository world, Entity self, ForceId side, in AreaLink area)
        {
            if (!world.Bus.IsRegistered<ObjectiveCapturedEvent>()) return;   // a node that registered no reader of it raises nothing
            s_inside.Clear();
            ObjectiveCensus.MenOfSideInside(world, side, in area, s_inside);
            var e = new ObjectiveCapturedEvent { Point = self, Side = side };
            for (int i = 0; i < s_inside.Count && i < 16; i++) e.Men[e.MenCount++] = s_inside[i];
            s_inside.Clear();
            world.Bus.Publish(e);
        }

        [Behavior(BehaviorNames.HoldArea, Params = typeof(HoldAreaParamsJsonDto), Categories = BehaviorCategory.Objective)]
        [BTreeDefinition(BehaviorNames.HoldArea, Curated = true, Group = "Library/Objectives", ParamsType = typeof(HoldAreaBlackboard))]
        public static BTreeBuilder<HoldAreaBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<HoldAreaBlackboard, BTreeContext>().Action(bb => bb.Block, Tick);
    }
}
