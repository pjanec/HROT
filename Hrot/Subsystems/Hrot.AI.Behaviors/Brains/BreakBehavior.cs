using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Map.Definitions.Behavior;
using Hrot.MuscleCharacter.Animation.Stance;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-2272</c> (TS-6, R-2034) — <c>Break</c>: a man whose stress reached his nerve. He holds his fire, runs to his retreat point
    /// if he has one (upright, flat out — the under-fire reflex does not keep an upright move) and goes to ground there; with none he goes
    /// to ground where he is. Success when <see cref="StressModel"/> says he has recovered, so the task he was on resumes. Started by
    /// <see cref="StressSystem"/> as a reaction at <see cref="ReactionUrgency.Break"/>, which ROE StayOnTask does not refuse.
    /// 📄 docs/DESIGN_Temperament_And_Stress.md §4 TS-6, §7.
    /// </summary>
    public static class BreakBehavior
    {
        /// <summary>How fast he runs (m/s).</summary>
        public const float RunSpeed = 4.5f;

        [StructLayout(LayoutKind.Sequential)]
        public struct BreakParams
        {
            public Vector3 Retreat;
            public byte HasRetreat;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BreakState
        {
            public byte Moving, Down;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BreakBlackboard
        {
            public BreakParams Params;
            public BreakState State;
        }

        [BehaviorResolver(BehaviorNames.Break)]
        public static void Resolve(in BreakParamsJsonDto authored, ref BreakBlackboard block, EntityRepository world, Entity self)
        {
            block.Params = new BreakParams { Retreat = authored.Retreat, HasRetreat = authored.HasRetreat ? (byte)1 : (byte)0 };
            block.State = default;
        }

        [Behavior(BehaviorNames.Break, Params = typeof(BreakParamsJsonDto), Categories = BehaviorCategory.Infantry | BehaviorCategory.Insurgent)]
        [BTreeDefinition(BehaviorNames.Break, Curated = true, Group = "Library/Reactions", ParamsType = typeof(BreakBlackboard))]
        public static BTreeBuilder<BreakBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<BreakBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Params, bb => bb.State, Broken);

        /// <summary>One tick: hold fire; run, then down; done when he has recovered.</summary>
        public static NodeStatus Broken(ref BreakParams p, ref BreakState ws, Entity self, EntityRepository world)
        {
            if (!StressModel.IsBroken(world, self)) return NodeStatus.Success;
            HoldFire(world, self);
            if (ws.Down != 0) return NodeStatus.Running;
            if (p.HasRetreat != 0 && ws.Moving == 0)
            {
                ws.Moving = LocomotionMoveTo.Issue(world, self, p.Retreat, RunSpeed, 1f, MoveGait.Upright, onSurface: true,
                                                   source: MoveSource.Flee) ? (byte)1 : (byte)0;
                if (ws.Moving != 0) return NodeStatus.Running;
            }
            if (ws.Moving != 0 && LocomotionMoveTo.Status(world, self) == NodeStatus.Running) return NodeStatus.Running;
            GoDown(world, self, ref ws);
            return NodeStatus.Running;
        }

        /// <summary>Leaving the break (recovered, or something more urgent): he stops; the task he resumes sets his stance and his move.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.BreakBehavior.Broken")]
        public static void Deactivate_Broken(ref BreakParams p, ref BreakState ws, Entity self, EntityRepository world)
        {
            if (ws.Moving != 0 && ws.Down == 0) LocomotionMoveTo.Stop(world, self);
            ws = default;
        }

        /// <summary>No aimed fire while he is broken: an aim-and-fire on his weapon channel is ended.</summary>
        internal static void HoldFire(EntityRepository world, Entity self)
        {
            if (!world.HasComponent<WeaponChannel>(self)) return;
            ref var channel = ref world.GetComponentRW<WeaponChannel>(self);
            if (channel.ActiveAction != CombatConstants.ActionIdAimAndFire) return;
            channel.ActiveAction = 0;
            unchecked { channel.ActionInstanceId++; }
        }

        private static void GoDown(EntityRepository world, Entity self, ref BreakState ws)
        {
            if (ws.Moving != 0) LocomotionMoveTo.Stop(world, self);
            ws.Moving = 0;
            ws.Down = 1;
            StanceRequest.Set(world, self, StanceId.Prone, UnderFire.DropBlendSeconds);
        }
    }
}
