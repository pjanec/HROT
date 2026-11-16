using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Squad.Command;
using Hrot.AI.Behaviors.Orders;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>The play's working state: where it holds and what it has done (unmanaged, in the blackboard).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct StochasticDefendParams
    {
        public Vector3 Position;
        public SimInstant Since;
        public byte Occupied;
        public byte Reported;
    }

    /// <summary>
    /// ⭐⭐ agg-behaviors <c>ST-0 ③</c> (docs/designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md §23.5; routing row <c>(Unit, Stochastic, Defend)</c>
    /// — <see cref="Hrot.Core.Tkb.ResolutionRoutingTable.Plays.Defend"/>) — the DEFEND play of a STOCHASTIC unit: it OCCUPIES its position at once and holds it.
    /// <para>⚠ MINIMAL, and said so: <b>movement is ST-1</b> (agg-backend's path follower is not built), so "occupy" is immediate — the unit's footprint
    /// centre (<see cref="SimTransform"/>) is set to the position and <see cref="AggregateUnit.Move"/> reads <see cref="UnitMoveState.Arrived"/>. It then reports
    /// PROGRESS <see cref="ProgressKind.Set"/> to its commander (the one report path, <see cref="CommandNodes.ReportProgress"/>), and raises
    /// <see cref="AggregateUnit.Prepared"/> with time in position (R-8306: Hasty at once, Prepared after <see cref="PreparedAfterSeconds"/>, Fortified after
    /// <see cref="FortifiedAfterSeconds"/> of SIM time; <see cref="AggregateUnit.PreparedSince"/> stamped at occupation). It never ends by itself — an order
    /// (or the commander's run ending, <c>SubordinateOrders.Release</c>) replaces it.</para>
    /// <para>⭐ Parallel (R-4212): it writes only its OWN entity's <see cref="AggregateUnit"/> / <see cref="SimTransform"/>, in place (both already there); it
    /// orders nobody. The report is queued on its own outbox.</para>
    /// </summary>
    public static class StochasticUnitDefendBehavior
    {
        /// <summary>⭐ R-8306 (lane decision) — sim seconds in position before the position counts as Prepared / Fortified.</summary>
        public const float PreparedAfterSeconds = 600f, FortifiedAfterSeconds = 3600f;

        [StructLayout(LayoutKind.Sequential)]
        public struct Blackboard
        {
            public StochasticDefendParams Hold;
        }

        [BehaviorResolver(Hrot.Core.Tkb.ResolutionRoutingTable.Plays.Defend)]
        public static void Resolve(in DefendOrder order, ref Blackboard block, EntityRepository world, Entity self)
        {
            var at = (Vector3)order.Position;
            if (at == Vector3.Zero && world.HasComponent<SimTransform>(self)) at = world.GetComponentRO<SimTransform>(self).Position;   // no position ⇒ hold where it is
            block.Hold = new StochasticDefendParams { Position = at };
        }

        [Behavior(Hrot.Core.Tkb.ResolutionRoutingTable.Plays.Defend, Params = typeof(DefendOrder), Fulfils = nameof(OrderKind.Defend), Categories = BehaviorCategory.Commander,
                  Description = "A stochastic unit holds its position (occupied at once; movement is ST-1) and becomes better prepared with time.")]
        [BTreeDefinition(Hrot.Core.Tkb.ResolutionRoutingTable.Plays.Defend, Curated = true, Group = "Library/Combat", ParamsType = typeof(Blackboard))]
        public static BTreeBuilder<Blackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<Blackboard, BTreeContext>()
                .Action(bb => bb.Hold, Hold);

        /// <summary>The prepared level after <paramref name="seconds"/> in position (R-8306).</summary>
        public static PreparedLevel LevelAfter(double seconds)
            => seconds >= FortifiedAfterSeconds ? PreparedLevel.Fortified : seconds >= PreparedAfterSeconds ? PreparedLevel.Prepared : PreparedLevel.Hasty;

        [SharedAiAction]
        public static NodeStatus Hold(ref StochasticDefendParams p, Entity self, EntityRepository world)
        {
            SimInstant now = CommandPictureOps.Now(world);
            bool aggregate = world.IsComponentTypeRegistered<AggregateUnit>() && world.HasComponent<AggregateUnit>(self);
            if (p.Occupied == 0)
            {
                p.Occupied = 1;
                p.Since = now;
                if (world.HasComponent<SimTransform>(self)) world.GetComponentRW<SimTransform>(self).Position = p.Position;   // ST-1 replaces this with the follower
                if (aggregate)
                {
                    ref var u = ref world.GetComponentRW<AggregateUnit>(self);
                    u.Move = UnitMoveState.Arrived;
                    u.Destination = p.Position;
                    u.Activity &= ~UnitActivity.Moving;
                    u.Prepared = PreparedLevel.Hasty;
                    u.PreparedSince = now;
                }
            }
            if (p.Reported == 0)
            {
                p.Reported = 1;
                CommandNodes.ReportProgress(world, self, ProgressKind.Set, p.Position);
            }
            if (aggregate)
            {
                var level = LevelAfter((double)now - (double)p.Since);
                if (world.GetComponentRO<AggregateUnit>(self).Prepared < level) world.GetComponentRW<AggregateUnit>(self).Prepared = level;   // write only on change
            }
            return NodeStatus.Running;
        }
    }
}
