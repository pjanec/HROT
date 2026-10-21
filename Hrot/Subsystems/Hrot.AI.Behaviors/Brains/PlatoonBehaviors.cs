using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Squad.Command;
using Fhsm.Compiler;
using Fhsm.Compiler.Graph;
using Fhsm.Kernel.Attributes;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-2294</c> CC-D (📄 docs/designs/group-maneuvers/DESIGN_Command_Chain_Example.md §1, §6, §8a) — <c>PlatoonBound</c>, the
    /// platoon's task in the first command chain: the support squad moves to the support point; ONLY when it reports SET does the assault
    /// squad move to the objective; the task finishes when the assault squad reports SET. One HSM region, each LEG a state whose activity
    /// (<see cref="CommandNodes.MoveLeg"/>) keeps its squad ordered and whose polled guard (<see cref="PictureConditions.ProgressReached"/>)
    /// reads the platoon's picture — the plan advances on REPORTS, never on the squads' state (R-2031).
    /// <para>⭐ A reaction (<c>PlatoonHold</c>) pauses it; it resumes in the state it was in, and that state's leg orders its squad again
    /// (its order was replaced by the hold's).</para>
    /// </summary>
    public static class PlatoonBoundBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct PlatoonBoundBlackboard
        {
            public MoveLegParams Support;
            public MoveLegState SupportState;
            public MoveLegParams Assault;
            public MoveLegState AssaultState;
            public ProgressReachedParams SupportSet;
            public ProgressReachedParams AssaultSet;
            public WithdrawReportingParams Both;
        }

        [BehaviorResolver(BehaviorNames.PlatoonBound)]
        public static void Resolve(in PlatoonBoundParamsJsonDto authored, ref PlatoonBoundBlackboard block, EntityRepository world, Entity self)
        {
            int support = (authored.SupportSquad > 0 ? authored.SupportSquad : 2) - 1;
            int assault = (authored.AssaultSquad > 0 ? authored.AssaultSquad : 1) - 1;
            block.Support = new MoveLegParams
            {
                Slot = support, Point = authored.SupportPoint, Speed = authored.Speed, ArrivalRadius = authored.ArrivalRadius,
                SitrepEverySeconds = authored.SitrepEverySeconds,
            };
            block.Assault = block.Support;
            block.Assault.Slot = assault;
            block.Assault.Point = authored.Objective;
            block.SupportState = default;
            block.AssaultState = default;
            block.SupportSet = new ProgressReachedParams { Slot = support, Progress = ProgressKind.Set };
            block.AssaultSet = new ProgressReachedParams { Slot = assault, Progress = ProgressKind.Set };
            bool asked = authored.SitrepEverySeconds > 0f;
            block.Both = new WithdrawReportingParams { SlotA = asked ? support : -1, SlotB = asked ? assault : -1 };
        }

        [Behavior(BehaviorNames.PlatoonBound, Params = typeof(PlatoonBoundParamsJsonDto), Categories = BehaviorCategory.Commander)]
        [HsmDefinition(BehaviorNames.PlatoonBound, Curated = true, Group = "Library/Platoon", ParamsType = typeof(PlatoonBoundBlackboard))]
        public static StateMachineGraph BuildPlatoonBound()
        {
            var builder = new HsmBuilder("PlatoonBound_HSM");
            var support = builder.State("Support").Initial()
                .Activity<PlatoonBoundBlackboard, MoveLegParams, MoveLegState>(bb => bb.Support, bb => bb.SupportState, CommandNodes.MoveLeg);
            var assault = builder.State("Assault")
                .Activity<PlatoonBoundBlackboard, MoveLegParams, MoveLegState>(bb => bb.Assault, bb => bb.AssaultState, CommandNodes.MoveLeg);
            builder.State("Done").Final()
                .OnEntry<PlatoonBoundBlackboard, WithdrawReportingParams>(bb => bb.Both, WithdrawIfAsked);
            support.On(0).GoTo("Assault").Guard<PlatoonBoundBlackboard, ProgressReachedParams>(bb => bb.SupportSet, PictureConditions.ProgressReached).Polled();
            assault.On(0).GoTo("Done").Guard<PlatoonBoundBlackboard, ProgressReachedParams>(bb => bb.AssaultSet, PictureConditions.ProgressReached).Polled();
            return builder.Build();
        }

        /// <summary>The task is done: take back the extra SITREPs it asked (only when it asked — a squad's own requirements from another
        /// superior's play stay).</summary>
        [SharedAiAction]
        public static NodeStatus WithdrawIfAsked(ref WithdrawReportingParams p, Entity self, EntityRepository world)
            => p.SlotA < 0 ? NodeStatus.Success : CommandNodes.WithdrawReporting(ref p, self, world);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2294</c> CC-D (Command Chain §6) — <c>PlatoonHold</c>, the platoon SOP's reaction to a reported enemy element: every
    /// squad in the roster is ordered to HOLD (abstract, CC-9 — each squad's class decides how) and kept on it; the reaction ends when
    /// the platoon's picture holds no such element any more (<see cref="PictureConditions.ElementReported"/> false — the squads keep a
    /// held element fresh by re-SPOTting it, so "not fresh" means gone). Its end releases the hold orders (the run's
    /// <c>SubordinateOrders</c>) and the paused task resumes.
    /// </summary>
    public static class PlatoonHoldBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct PlatoonHoldState
        {
            public int Orders;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PlatoonHoldBlackboard
        {
            public ElementReportedParams Order;
            public PlatoonHoldState State;
        }

        [BehaviorResolver(BehaviorNames.PlatoonHold)]
        public static void Resolve(in PlatoonHoldParamsJsonDto authored, ref PlatoonHoldBlackboard block, EntityRepository world, Entity self)
        {
            block.Order = new ElementReportedParams
            {
                MinSize      = authored.MinSize is >= 1 and <= 4 ? (ElementSize)authored.MinSize : ElementSize.Team,
                WithinMetres = authored.WithinMetres > 0f ? authored.WithinMetres : 400f,
                FreshSeconds = authored.FreshSeconds > 0f ? authored.FreshSeconds : 30f,
            };
            block.State = default;
        }

        [Behavior(BehaviorNames.PlatoonHold, Params = typeof(PlatoonHoldParamsJsonDto), Categories = BehaviorCategory.Commander)]
        [BTreeDefinition(BehaviorNames.PlatoonHold, Curated = true, Group = "Library/Platoon", ParamsType = typeof(PlatoonHoldBlackboard))]
        public static BTreeBuilder<PlatoonHoldBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<PlatoonHoldBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Order, bb => bb.State, Run);

        /// <summary>⭐ <c>CE-4032</c> — <paramref name="member"/> runs its OWN reaction (its SOP answered a contact, a casualty, fire): a
        /// superior's order would replace it (a reaction yields to any new order), so the hold leaves it to fight and holds it once the
        /// reaction ends. 📌 Live on <c>uc-urban-battle</c>: Squad B, closing on the ambush and carrying a casualty, was ordered to hold at
        /// ~126 s — 13 rounds from the whole squad in ten minutes.</summary>
        public static bool Fighting(EntityRepository world, Entity member)
            => world.HasComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>(member)
               && world.GetComponentRO<Fdp.Toolkit.Behavior.Components.BehaviorState>(member).Origin == Fdp.Toolkit.Behavior.Components.BehaviorOrigin.Reaction;

        /// <summary>Hold every squad (re-ordering one whose hold another run replaced) while the element is reported; Success once it
        /// is not. A squad fighting its own reaction is left to it (<see cref="Fighting"/>).</summary>
        public static NodeStatus Run(ref ElementReportedParams p, ref PlatoonHoldState ws, Entity self, EntityRepository world)
        {
            if (!PictureConditions.ElementReported(ref p, self, world)) return NodeStatus.Success;
            if (!world.HasComponent<UnitRoster>(self)) return NodeStatus.Running;
            int n = world.GetComponentRO<UnitRoster>(self).Count;
            for (int slot = 0; slot < n; slot++)
            {
                var member = CommandPictureOps.Subordinate(world, self, slot);
                if (member.IsNull || !world.IsAlive(member) || CommandOrders.Stands(world, self, member)) continue;
                if (Fighting(world, member)) continue;   // ⭐ CE-4032 — it is in its own fight: the hold waits for that to end
                CommandOrders.Order(world, self, member, "Hold", "{}");
                ws.Orders++;
            }
            return NodeStatus.Running;
        }
    }
}
