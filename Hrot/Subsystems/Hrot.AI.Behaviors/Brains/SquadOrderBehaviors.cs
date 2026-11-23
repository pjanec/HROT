using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Squad.Maneuvers;
using Hrot.AI.Behaviors.Orders;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    // ⭐⭐ CE-2141 (D2, R-273) — how an infantry SQUAD fulfils the orders. Each is a front door to a drill the squad already has (D2-A):
    //   the order says WHAT, the squad decides HOW (O3). Named <Class><Order> (R-279); the order's DTO is the behaviour's params DTO.
    //   📄 docs/DESIGN_Orders_Params_Entity_Classes.md §7.

    /// <summary>⭐ <c>CE-2141</c> — MOVE: the squad marches to <see cref="MoveOrder.Location"/> — in File when the order prefers
    /// roads (a squad on a road walks in file), else in a Wedge (D2-E); the point's height is the squad's own (D2-F).</summary>
    public static class InfantrySquadMoveBehavior
    {
        [BehaviorResolver(BehaviorNames.InfantrySquadMove)]
        public static void Resolve(in MoveOrder order, ref SquadMarchBehavior.SquadMarchBlackboard block, EntityRepository world, Entity self)
        {
            float z = world.HasComponent<SimTransform>(self) ? world.GetComponentRO<SimTransform>(self).Position.Z : 0f;
            block.Order = new SquadMarchParams
            {
                Objective     = new System.Numerics.Vector3(order.Location.Local.X, order.Location.Local.Y, z),   // S0 item 5: the order's place IS local metres; the unit's own height rides in z
                Shape         = ShapeFor(order.RoadUse),
                Spacing       = 5f,
                Speed         = order.Speed > 0f ? order.Speed : 1.5f,
                ArrivalRadius = order.ArrivalRadius > 0f ? order.ArrivalRadius : 5f,
                MaxLagMetres  = 10f,
            };
            block.State = default;
        }

        /// <summary>⭐ D2-E — the squad's own choice of shape.</summary>
        public static byte ShapeFor(RoadUse roads)
            => roads is RoadUse.Prefer or RoadUse.StronglyPrefer ? SquadMarchManeuver.ShapeFile : SquadMarchManeuver.ShapeWedge;

        [Behavior(BehaviorNames.InfantrySquadMove, Params = typeof(MoveOrder), Categories = BehaviorCategory.Commander)]   // ⭐ CE-2134 — the contract on the behaviour
        [BTreeDefinition(BehaviorNames.InfantrySquadMove, Curated = true, Group = "Library/Orders", ParamsType = typeof(SquadMarchBehavior.SquadMarchBlackboard))]
        public static BTreeBuilder<SquadMarchBehavior.SquadMarchBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<SquadMarchBehavior.SquadMarchBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Order, bb => bb.State, SquadMarchBehavior.Run);
    }

    /// <summary>⭐ <c>CE-2141</c> — HOLD: the squad holds where it stands and fights, until told otherwise (it never ends by itself).</summary>
    public static class InfantrySquadHoldBehavior
    {
        [BehaviorResolver(BehaviorNames.InfantrySquadHold)]
        public static void Resolve(in HoldOrder order, ref SquadHoldBehavior.SquadHoldBlackboard block, EntityRepository world, Entity self)
        {
            block.Order = new SquadHoldParams { Speed = 3f, DurationSeconds = 0f, EndWhenClear = 0 };
            block.State = default;
        }

        [Behavior(BehaviorNames.InfantrySquadHold, Params = typeof(HoldOrder), Categories = BehaviorCategory.Commander)]   // ⭐ CE-2134 — the contract on the behaviour
        [BTreeDefinition(BehaviorNames.InfantrySquadHold, Curated = true, Group = "Library/Orders", ParamsType = typeof(SquadHoldBehavior.SquadHoldBlackboard))]
        public static BTreeBuilder<SquadHoldBehavior.SquadHoldBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<SquadHoldBehavior.SquadHoldBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Order, bb => bb.State, SquadHoldBehavior.Run);
    }

    /// <summary>⭐ <c>CE-2141</c> — ATTACK: suppress and manoeuvre on <see cref="AttackOrder.Target"/> — seen by the squad or not
    /// (D2-G); no target ⇒ the squad's top identified contact. Ends when the target is down or the assault is through.
    /// ⭐⭐ <c>CE-2473</c> (R-8316) — an attack on a PLACE (<see cref="AttackOrder.Position"/>, no target): the squad first BOUNDS to it
    /// (<see cref="BoundingOverwatchBehavior"/> — what a platoon's assault squad is given for its objective, <c>PlatoonAttackBehavior.Bound</c>),
    /// fighting what it meets through its own contact reactions, then the drill runs on the top enemy still there; with nobody left it holds the place.</summary>
    public static class InfantrySquadAttackBehavior
    {
        /// <summary>⭐ <c>CE-2473</c> — the bound to the place, then the drill. <see cref="Bound"/>'s <c>BoundMetres</c> 0 = no place: the bound is skipped.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct SquadAttackBlackboard
        {
            public BoundingOverwatchParams Bound;
            public SquadPlayState BoundState;
            public SuppressAndManeuverParams Order;
            public SquadPlayState State;
        }

        [BehaviorResolver(BehaviorNames.InfantrySquadAttack)]
        public static void Resolve(in AttackOrder order, ref SquadAttackBlackboard block, EntityRepository world, Entity self)
        {
            block.Order = SuppressAndManeuverBehavior.Defaults(0f, 0f, 0f, 0f);
            block.Order.TargetId = order.Target.Ref.TryResolve(world, out var target) ? (long)target.PackedValue : 0;
            block.State = default;
            block.Bound = default;
            block.BoundState = default;
            var place = (Vector3)order.Position;
            if (block.Order.TargetId == 0 && place != Vector3.Zero)
                block.Bound = new BoundingOverwatchParams
                {
                    Objective = place, BoundMetres = 30f, Speed = order.Speed > 0f ? order.Speed : 3f,
                    ArrivalRadius = order.SearchRadius > 0f ? order.SearchRadius : 10f, BoundTimeoutSeconds = 60f,
                };
        }

        /// <summary>⭐ <c>CE-2473</c> — the bound to the place, or Success at once when the order named no place.</summary>
        public static NodeStatus BoundToPlace(ref BoundingOverwatchParams p, ref SquadPlayState ws, Entity self, EntityRepository world)
            => p.BoundMetres <= 0f ? NodeStatus.Success : BoundingOverwatchBehavior.Run(ref p, ref ws, self, world);

        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.InfantrySquadAttackBehavior.BoundToPlace")]
        public static void Deactivate_BoundToPlace(ref BoundingOverwatchParams p, ref SquadPlayState ws, Entity self, EntityRepository world)
            => SquadManeuverRunner.DropSensor(ref ws, world);

        [Behavior(BehaviorNames.InfantrySquadAttack, Params = typeof(AttackOrder), Categories = BehaviorCategory.Commander)]   // ⭐ CE-2134 — the contract on the behaviour
        [BTreeDefinition(BehaviorNames.InfantrySquadAttack, Curated = true, Group = "Library/Orders", ParamsType = typeof(SquadAttackBlackboard))]
        public static BTreeBuilder<SquadAttackBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<SquadAttackBlackboard, BTreeContext>()
                .Sequence(s => s
                    .StatefulAction(bb => bb.Bound, bb => bb.BoundState, BoundToPlace)
                    .StatefulAction(bb => bb.Order, bb => bb.State, SuppressAndManeuverBehavior.Run));
    }
}
