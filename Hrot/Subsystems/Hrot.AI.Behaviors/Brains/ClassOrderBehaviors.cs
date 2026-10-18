using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fhsm.Compiler;
using Fhsm.Compiler.Graph;
using Fhsm.Kernel.Attributes;
using Hrot.AI.Behaviors.Orders;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    // ⭐⭐ CE-2143 (D4-B, docs/DESIGN_Orders_Params_Entity_Classes.md §9) — how the BROAD classes fulfil the three orders, so every
    //   entity the unit menu / RTS / HTTP ordered before keeps its orders once the sender names an order instead of a behaviour:
    //   Person (DIS Kind 3: soldiers, insurgents, civilians) and Platform (Kind 1: vehicles). Each is a front door to the behaviour
    //   the sender used to name — MoveToLocation, CombatPosture (the designated target, assault), Idle. A more specific class
    //   (InfantrySquad, later Tank …) is found first (§8) and overrides these.

    /// <summary>⭐ <c>CE-2143</c> — MOVE for a person or a platform: <c>MoveToLocation</c> to the order's point (on the surface).</summary>
    public static class ClassMoveOrders
    {
        [BehaviorResolver(BehaviorNames.PersonMove)]
        public static void ResolvePerson(in MoveOrder order, ref CgfNodes.MoveToLocationParams block, EntityRepository world, Entity self)
            => block = From(in order, world);

        [BehaviorResolver(BehaviorNames.PlatformMove)]
        public static void ResolvePlatform(in MoveOrder order, ref CgfNodes.MoveToLocationParams block, EntityRepository world, Entity self)
            => block = From(in order, world);

        /// <summary>The order's point in local metres (the move puts it on the surface itself); zero speed / radius keep the
        /// mover's defaults, as for <c>MoveToLocation</c>.</summary>
        public static CgfNodes.MoveToLocationParams From(in MoveOrder order, EntityRepository world)
        {
            var at = OrderGeo.ToLocal(world, order.Location, 0f);
            return new CgfNodes.MoveToLocationParams
            {
                X = at.X, Y = at.Y, Speed = order.Speed, ArrivalRadius = order.ArrivalRadius, RoadUse = order.RoadUse,
            };
        }

        [Behavior(BehaviorNames.PersonMove, Params = typeof(MoveOrder), Categories = BehaviorCategory.Infantry | BehaviorCategory.Insurgent | BehaviorCategory.Civilian)]
        [BTreeDefinition(BehaviorNames.PersonMove, Curated = true, Group = "Library/Orders", ParamsType = typeof(CgfNodes.MoveToLocationParams))]
        public static BTreeBuilder<CgfNodes.MoveToBlackboard, BTreeContext> BuildPersonMove()
            => new BTreeBuilder<CgfNodes.MoveToBlackboard, BTreeContext>().Action(bb => bb.Params, CgfNodes.Action_WriteMoveToChannel);

        [Behavior(BehaviorNames.PlatformMove, Params = typeof(MoveOrder), Categories = BehaviorCategory.MilitaryApc)]
        [BTreeDefinition(BehaviorNames.PlatformMove, Curated = true, Group = "Library/Orders", ParamsType = typeof(CgfNodes.MoveToLocationParams))]
        public static BTreeBuilder<CgfNodes.MoveToBlackboard, BTreeContext> BuildPlatformMove()
            => new BTreeBuilder<CgfNodes.MoveToBlackboard, BTreeContext>().Action(bb => bb.Params, CgfNodes.Action_WriteMoveToChannel);
    }

    /// <summary>⭐ <c>CE-2143</c> — ATTACK for a person or a platform: <c>CombatPosture</c> with the target designated and assault on
    /// (Entity Control C-5) — the unit closes in and fights IT; the order succeeds when the target is out of the fight.</summary>
    public static class ClassAttackOrders
    {
        [BehaviorResolver(BehaviorNames.PersonAttack)]
        public static void ResolvePerson(in AttackOrder order, ref CombatPostureBehavior.CombatPostureBlackboard block, EntityRepository world, Entity self)
            => CombatPostureBehavior.Resolve(new CombatPostureParamsJsonDto { Target = order.Target, Assault = true }, ref block, world, self);

        [BehaviorResolver(BehaviorNames.PlatformAttack)]
        public static void ResolvePlatform(in AttackOrder order, ref CombatPostureBehavior.CombatPostureBlackboard block, EntityRepository world, Entity self)
            => CombatPostureBehavior.Resolve(new CombatPostureParamsJsonDto { Target = order.Target, Assault = true }, ref block, world, self);

        [Behavior(BehaviorNames.PersonAttack, Params = typeof(AttackOrder), Categories = BehaviorCategory.Infantry | BehaviorCategory.Insurgent)]
        [BTreeDefinition(BehaviorNames.PersonAttack, Curated = true, Group = "Library/Orders", ParamsType = typeof(CombatPostureBehavior.CombatPostureBlackboard))]
        public static BTreeBuilder<CombatPostureBehavior.CombatPostureBlackboard, BTreeContext> BuildPersonAttack()
            => CombatPostureBehavior.BuildCombatPostureTree();

        [Behavior(BehaviorNames.PlatformAttack, Params = typeof(AttackOrder), Categories = BehaviorCategory.MilitaryApc)]
        [BTreeDefinition(BehaviorNames.PlatformAttack, Curated = true, Group = "Library/Orders", ParamsType = typeof(CombatPostureBehavior.CombatPostureBlackboard))]
        public static BTreeBuilder<CombatPostureBehavior.CombatPostureBlackboard, BTreeContext> BuildPlatformAttack()
            => CombatPostureBehavior.BuildCombatPostureTree();
    }

    /// <summary>⭐ <c>CE-2143</c> — HOLD for a person or a platform: stay where it is, as <c>Idle</c> (one state, never ends).
    /// ⭐ <c>CE-2297</c> LD-3 — a person's hold carries his eyes: the threat, else a fresh sound, else his sector.</summary>
    public static class ClassHoldOrders
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct PersonHoldBlackboard
        {
            public LookParams Look;
            public LookNodeState LookState;
        }

        /// <summary>The order carries nothing: his eyes come from the threat, a sound, or his sector (<c>LookAt</c>).</summary>
        [BehaviorResolver(BehaviorNames.PersonHold)]
        public static void ResolvePerson(in HoldOrder order, ref PersonHoldBlackboard block, EntityRepository world, Entity self)
            => block = default;

        [Behavior(BehaviorNames.PersonHold, Params = typeof(HoldOrder), Categories = BehaviorCategory.AllMilitary | BehaviorCategory.Civilian)]
        [HsmDefinition(BehaviorNames.PersonHold, Curated = true, Group = "Library/Orders", ParamsType = typeof(PersonHoldBlackboard))]
        public static StateMachineGraph BuildPersonHold()
        {
            var builder = new HsmBuilder("PersonHold_HSM");
            builder.State("Hold").Initial()
                .Activity<PersonHoldBlackboard, LookParams, LookNodeState>(bb => bb.Look, bb => bb.LookState, LookNodes.Look);
            return builder.Build();
        }

        [Behavior(BehaviorNames.PlatformHold, Params = typeof(HoldOrder), Categories = BehaviorCategory.AllMilitary)]
        [HsmDefinition(BehaviorNames.PlatformHold, Curated = true, Group = "Library/Orders")]
        public static StateMachineGraph BuildPlatformHold() => Hold("PlatformHold_HSM");

        private static StateMachineGraph Hold(string name)
        {
            var builder = new HsmBuilder(name);
            builder.State("Hold").Initial();
            return builder.Build();
        }
    }
}
