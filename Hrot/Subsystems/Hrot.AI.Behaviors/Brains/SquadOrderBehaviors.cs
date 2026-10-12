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
                Objective     = OrderGeo.ToLocal(world, order.Location, z),
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

        [BTreeDefinition(BehaviorNames.InfantrySquadMove, Curated = true, Group = "Library/Squad", ParamsType = typeof(SquadMarchBehavior.SquadMarchBlackboard))]
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

        [BTreeDefinition(BehaviorNames.InfantrySquadHold, Curated = true, Group = "Library/Squad", ParamsType = typeof(SquadHoldBehavior.SquadHoldBlackboard))]
        public static BTreeBuilder<SquadHoldBehavior.SquadHoldBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<SquadHoldBehavior.SquadHoldBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Order, bb => bb.State, SquadHoldBehavior.Run);
    }

    /// <summary>⭐ <c>CE-2141</c> — ATTACK: suppress and manoeuvre on <see cref="AttackOrder.Target"/> — seen by the squad or not
    /// (D2-G); no target ⇒ the squad's top identified contact. Ends when the target is down or the assault is through.</summary>
    public static class InfantrySquadAttackBehavior
    {
        [BehaviorResolver(BehaviorNames.InfantrySquadAttack)]
        public static void Resolve(in AttackOrder order, ref SuppressAndManeuverBehavior.SuppressAndManeuverBlackboard block, EntityRepository world, Entity self)
        {
            block.Order = SuppressAndManeuverBehavior.Defaults(0f, 0f, 0f, 0f);
            block.Order.TargetId = order.Target.TryResolve(world, out var target) ? (long)target.PackedValue : 0;
            block.State = default;
        }

        [BTreeDefinition(BehaviorNames.InfantrySquadAttack, Curated = true, Group = "Library/Squad", ParamsType = typeof(SuppressAndManeuverBehavior.SuppressAndManeuverBlackboard))]
        public static BTreeBuilder<SuppressAndManeuverBehavior.SuppressAndManeuverBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<SuppressAndManeuverBehavior.SuppressAndManeuverBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Order, bb => bb.State, SuppressAndManeuverBehavior.Run);
    }
}
