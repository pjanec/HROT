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
    /// ⭐ <c>CE-2241</c> ② (CV-14, B7; 📄 docs/DESIGN_Civilians_And_Crowd_Control.md §5m PV-4) — <c>BatonCharge</c>, a police order: he
    /// goes at the point (<see cref="BatonChargeParams.At"/>) and, for <see cref="BatonChargeParams.Seconds"/>, strikes with the baton the
    /// nearest standing person of the <c>Civilian</c> class he remembers within <see cref="BatonChargeParams.Reach"/> of it — closing on him,
    /// one blow every <see cref="Melee.CooldownSeconds"/> — Neutral or not. A man down, stunned, out of the fight or showing his hands is
    /// left. No blow below <c>NonLethal</c> (<see cref="ForceLevels.LevelFor"/>, §5k EL-7). Success when the time is up.
    /// ⛔ It is the only order that puts force on a NEUTRAL crowd — <c>HoldTheLine</c> answers Hostiles only (FL-5).
    /// </summary>
    public static class BatonChargeBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct BatonChargeParams
        {
            public Vector3 At;
            public float Reach;
            public float Seconds;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BatonChargeState
        {
            /// <summary>When the charge began (0 = not yet).</summary>
            public double Since;
            public double NextStrikeAt;
            /// <summary>Where his move goes (valid while <see cref="Moving"/> is 1).</summary>
            public Vector3 Goal;
            public byte Moving;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BatonChargeBlackboard
        {
            public BatonChargeParams Params;
            public BatonChargeState State;
        }

        public const float DefaultReachMetres = 8f;
        public const float DefaultSeconds = 10f;
        /// <summary>His charge, m/s (a run).</summary>
        public const float ChargeSpeed = 3f;

        [BehaviorResolver(BehaviorNames.BatonCharge)]
        public static void Resolve(in BatonChargeParamsJsonDto authored, ref BatonChargeBlackboard block, EntityRepository world, Entity self)
        {
            block.Params = new BatonChargeParams
            {
                At = new Vector3(authored.X, authored.Y, authored.Z),
                Reach = authored.Reach > 0f ? authored.Reach : DefaultReachMetres,
                Seconds = authored.Seconds > 0f ? authored.Seconds : DefaultSeconds,
            };
            block.State = default;
        }

        [Behavior(BehaviorNames.BatonCharge, Params = typeof(BatonChargeParamsJsonDto), Categories = BehaviorCategory.AllMilitary)]
        [BTreeDefinition(BehaviorNames.BatonCharge, Curated = true, Group = "Library/Orders", ParamsType = typeof(BatonChargeBlackboard))]
        public static BTreeBuilder<BatonChargeBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<BatonChargeBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Params, bb => bb.State, Charge);

        /// <summary>One tick: the nearest protester in reach of the point — strike him in reach, else close on him; none, go to the point.
        /// Success once <c>Seconds</c> have passed.</summary>
        public static NodeStatus Charge(ref BatonChargeParams p, ref BatonChargeState ws, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<SimTransform>(self)) return NodeStatus.Failure;
            double now = world.SimulationTime > 0 ? world.SimulationTime : 1e-6;
            if (ws.Since <= 0) ws.Since = now;
            if (now - ws.Since >= p.Seconds)
            {
                LocomotionMoveTo.Stop(world, self);
                ws.Moving = 0;
                return NodeStatus.Success;
            }

            var target = Nearest(world, self, p.At, p.Reach > 0f ? p.Reach : DefaultReachMetres);
            if (!target.IsNull && Melee.InReach(world, self, target))
            {
                if (ws.Moving != 0) { LocomotionMoveTo.Stop(world, self); ws.Moving = 0; }
                if (now >= ws.NextStrikeAt && ForceLevels.LevelFor(world, self, target) >= RoeForceLevel.NonLethal && Melee.Strike(world, self, target))
                    ws.NextStrikeAt = now + Melee.CooldownSeconds;
                return NodeStatus.Running;
            }

            var goal = target.IsNull ? p.At : world.GetComponentRO<SimTransform>(target).Position;
            if (ws.Moving == 0 || Flat(goal, ws.Goal) > 0.75f || LocomotionMoveTo.Status(world, self) != NodeStatus.Running)
            {
                LocomotionMoveTo.Issue(world, self, goal, ChargeSpeed, target.IsNull ? 1f : 0.8f, MoveGait.Upright, source: MoveSource.Attack);
                ws.Goal = goal;
                ws.Moving = 1;
            }
            return NodeStatus.Running;
        }

        /// <summary>Leaving the charge (a new order): his walk stops.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.BatonChargeBehavior.Charge")]
        public static void Deactivate_Charge(ref BatonChargeParams p, ref BatonChargeState ws, Entity self, EntityRepository world)
        {
            LocomotionMoveTo.Stop(world, self);
            ws = default;
        }

        /// <summary>The standing person of the <c>Civilian</c> class he remembers within <paramref name="reach"/> m of <paramref name="at"/>,
        /// nearest to HIM — not down, not stunned, not out of the fight, his hands not up, not a carried item. <see cref="Entity.Null"/> when none.</summary>
        internal static unsafe Entity Nearest(EntityRepository world, Entity self, Vector3 at, float reach)
        {
            if (!world.HasComponent<TargetMemory>(self) || !world.HasComponent<SimTransform>(self)) return Entity.Null;
            var me = world.GetComponentRO<SimTransform>(self).Position;
            ref readonly var mem = ref world.GetComponentRO<TargetMemory>(self);
            Entity best = Entity.Null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < mem.Count; i++)
            {
                if (TargetMemory.IsAnonymous(in mem, i)) continue;
                var e = new Entity((ulong)mem.EntityIds[i]);
                if (e.IsNull || !world.IsAlive(e) || !world.HasComponent<SimTransform>(e) || !Armament.IsCivilianClass(world.GetDisType(e))
                    || !CombatLife.IsAlive(world, e) || ThreatDanger.IsOutOfTheFight(world, e) || ThreatDanger.ShowsHandsUp(world, e)
                    || Effects.IsKnockedDown(world, e) || Effects.IsStunned(world, e)) continue;
                var pos = world.GetComponentRO<SimTransform>(e).Position;
                if (Flat(pos, at) > reach) continue;
                float d = Flat(pos, me);
                if (d < bestDist) { bestDist = d; best = e; }
            }
            return best;
        }

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y));
    }
}
