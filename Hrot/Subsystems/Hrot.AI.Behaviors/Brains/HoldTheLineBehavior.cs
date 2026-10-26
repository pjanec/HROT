using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Components;
using Hrot.Map.Definitions.Behavior;
using Fdp.Toolkit.Behavior.Diagnostics;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐ <c>CE-2239</c> part 2 (CV-11, FL-5; 📄 docs/DESIGN_Civilians_And_Crowd_Control.md §5j) — <c>HoldTheLine</c>, the police brain: he
    /// holds the post where the behaviour started (drawn more than <see cref="HoldTheLineParams.Hold"/> m from it, he walks back), fires at
    /// the nearest Hostile he remembers within <see cref="HoldTheLineParams.Reach"/> (<c>AimAndFire MountAuto</c> — the weapon choice and
    /// the gate apply his ROE's force level, so a non-lethal policeman fires baton rounds) and, a Hostile within
    /// <see cref="Melee.ReachMetres"/>, strikes him with the baton. ⛔ It holds no force rule of its own (FL-2). A man who is down, out
    /// of the fight, Neutral or showing his hands is never his target.
    /// </summary>
    public static class HoldTheLineBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct HoldTheLineParams
        {
            public float Reach;
            public float Hold;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HoldTheLineState
        {
            /// <summary>Where he stood when the behaviour started (valid once <see cref="HasPost"/> is 1).</summary>
            public Vector3 Post;
            public byte HasPost;
            /// <summary>1 while he walks back to the post.</summary>
            public byte Returning;
            public EngageState Engage;
            public double NextStrikeAt;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HoldTheLineBlackboard
        {
            public HoldTheLineParams Params;
            public HoldTheLineState State;
        }

        public const float DefaultReachMetres = 40f;
        public const float DefaultHoldMetres = 3f;
        /// <summary>Between his aimed rounds (s).</summary>
        public const float CooldownSeconds = 1.5f;
        /// <summary>His walk back to the post (m/s).</summary>
        public const float ReturnSpeed = 1.5f;

        [BehaviorResolver(BehaviorNames.HoldTheLine)]
        public static void Resolve(in HoldTheLineParamsJsonDto authored, ref HoldTheLineBlackboard block, EntityRepository world, Entity self)
        {
            block.Params = new HoldTheLineParams
            {
                Reach = authored.Reach > 0f ? authored.Reach : DefaultReachMetres,
                Hold = authored.Hold > 0f ? authored.Hold : DefaultHoldMetres,
            };
            block.State = default;
        }

        [Behavior(BehaviorNames.HoldTheLine, Params = typeof(HoldTheLineParamsJsonDto), Categories = BehaviorCategory.AllMilitary)]
        [BTreeDefinition(BehaviorNames.HoldTheLine, Curated = true, Group = "Library/Orders", ParamsType = typeof(HoldTheLineBlackboard))]
        public static BTreeBuilder<HoldTheLineBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<HoldTheLineBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Params, bb => bb.State, Hold);

        /// <summary>One tick of the line: the post, then the baton in reach, else a round at the nearest Hostile in reach. Running.</summary>
        public static NodeStatus Hold(ref HoldTheLineParams p, ref HoldTheLineState ws, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<SimTransform>(self)) return NodeStatus.Running;
            var me = world.GetComponentRO<SimTransform>(self).Position;
            if (ws.HasPost == 0) { ws.Post = me; ws.HasPost = 1; }

            float hold = p.Hold > 0f ? p.Hold : DefaultHoldMetres;
            float away = Flat(me, ws.Post);
            if (away > hold) { if (ws.Returning == 0 || LocomotionMoveTo.Status(world, self) != NodeStatus.Running) LocomotionMoveTo.Issue(world, self, ws.Post, ReturnSpeed, 0.5f, MoveGait.Upright, source: MoveSource.SquadPost); ws.Returning = 1; }
            else if (ws.Returning != 0 && away <= 0.75f) { LocomotionMoveTo.Stop(world, self); ws.Returning = 0; }

            // ⭐ CE-2239 part 3 (B2, §5k EL-3) — the ladder's warning: every identified-armed man in reach is warned first (a Neutral too)
            EscalationLadder.Watch(world, self, p.Reach > 0f ? p.Reach : DefaultReachMetres);
            var target = Nearest(world, self, me, p.Reach > 0f ? p.Reach : DefaultReachMetres);
            if (target.IsNull) { PostureNodes.StopFiring(world, self, ref ws.Engage); return NodeStatus.Running; }

            if (Melee.InReach(world, self, target))
            {
                PostureNodes.StopFiring(world, self, ref ws.Engage);
                double now = world.SimulationTime;
                // ⭐ CE-2239 part 3 (B2, §5k EL-7) — the baton is non-lethal FORCE: at the Warn rung (or under a Warn ceiling) no blow
                if (now >= ws.NextStrikeAt && ForceLevels.LevelFor(world, self, target) >= RoeForceLevel.NonLethal && Melee.Strike(world, self, target))
                    ws.NextStrikeAt = now + Melee.CooldownSeconds;
                return NodeStatus.Running;
            }
            PostureNodes.Fire(world, self, ref ws.Engage, CooldownSeconds, 0, target);
            return NodeStatus.Running;
        }

        /// <summary>Leaving the line (a new order): his fire and his walk stop.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.HoldTheLineBehavior.Hold")]
        public static void Deactivate_Hold(ref HoldTheLineParams p, ref HoldTheLineState ws, Entity self, EntityRepository world)
        {
            PostureNodes.StopFiring(world, self, ref ws.Engage);
            LocomotionMoveTo.Stop(world, self);
            ws = default;
        }

        /// <summary>The man he REMEMBERS (where he last saw him) within <paramref name="reach"/> m who is Hostile and still on his feet — in the
        /// fight, not Neutral, not knocked down or stunned, his hands not up (the gate holds a ROUND at raised hands, CV-13a; nothing holds a
        /// baton blow, so the line asks it here) — that he may use the MOST force on (<see cref="ForceLevels.LevelFor"/>: a gunman he may
        /// shoot before a stone thrower he may only stun, a man he may only warn last — ⭐ §5k EL-8), the nearest among those.
        /// <see cref="Entity.Null"/> when none.</summary>
        internal static unsafe Entity Nearest(EntityRepository world, Entity self, Vector3 me, float reach)
        {
            if (!world.HasComponent<TargetMemory>(self)) return Entity.Null;
            ref readonly var mem = ref world.GetComponentRO<TargetMemory>(self);
            Entity best = Entity.Null;
            float bestDist = reach * reach;
            var bestLevel = RoeForceLevel.ForceUnset;
            for (int i = 0; i < mem.Count; i++)
            {
                if (TargetMemory.IsAnonymous(in mem, i)) continue;
                var e = new Entity((ulong)mem.EntityIds[i]);
                if (e.IsNull || !world.IsAlive(e) || !Injury.IsInTheFight(world, e) || ThreatDanger.IsNeutral(world, e)
                    || ThreatDanger.IsOutOfTheFight(world, e) || ThreatDanger.ShowsHandsUp(world, e) || Effects.IsKnockedDown(world, e)
                    || Effects.IsStunned(world, e)
                    || CarryStations.SeatOf(world, e, out var carrier) >= 0 && CarryStations.Hands(world, carrier) >= 0) continue;   // ⭐ CE-2239 part 3 — a weapon a PERSON carries is never the target, he is (twin: 17 rounds at the gunman's rifle)   // ⭐ a stunned man is out of the fight for its seconds: not beaten while he cannot fight (live: a stunned instigator in reach was struck to death)
                float dx = mem.PositionsX[i] - me.X, dy = mem.PositionsY[i] - me.Y;
                float d = dx * dx + dy * dy;
                if (d > reach * reach) continue;
                var level = ForceLevels.LevelFor(world, self, e);
                if (level > bestLevel || level == bestLevel && d <= bestDist) { bestLevel = level; bestDist = d; best = e; }
            }
            return best;
        }

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y));
    }
}
