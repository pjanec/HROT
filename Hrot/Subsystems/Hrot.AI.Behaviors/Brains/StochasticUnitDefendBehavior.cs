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
        /// <summary>⭐ ST-1 (R-8307) — the approach: the move's progress, and whether it is over (arrived, or none was needed).</summary>
        public MoveDrive Drive;
        public byte Approached;
        /// <summary>⭐ R-8342 — 1 = it occupied WITHOUT moving (it already stood there): a better prepared level it already held is kept.</summary>
        public byte InPlace;
    }

    /// <summary>
    /// ⭐⭐ agg-behaviors <c>ST-0 ③</c> (docs/designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md §23.5; routing row <c>(Unit, Stochastic, Defend)</c>
    /// — <see cref="Hrot.Core.Tkb.ResolutionRoutingTable.Plays.Defend"/>) — the DEFEND play of a STOCHASTIC unit: it OCCUPIES its position at once and holds it.
    /// <para>⭐ ST-1 (R-8307): a unit FARTHER than <see cref="StochasticUnitMoves.ArrivalRadiusMetres"/> from its position MOVES there first, as a deployed line
    /// (<see cref="StochasticUnitMoves.Step"/>: the one route/follower, a dropped route re-requested at most 3 times, then PROGRESS TaskFailed and Failure) and occupies
    /// on ARRIVAL; a unit already there occupies at once. Occupying marks <see cref="AggregateUnit.Move"/> <see cref="UnitMoveState.Arrived"/>. It then reports
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
                  Description = "A stochastic unit moves to its position as a line when it is not there (ST-1), then holds it and becomes better prepared with time.")]
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
                if (aggregate && p.Approached == 0)
                {
                    var at = world.HasComponent<SimTransform>(self) ? (Vector3)world.GetComponentRO<SimTransform>(self).Position : p.Position;
                    if (p.Drive.Started == 0 && Vector3.Distance(new Vector3(at.X, at.Y, 0f), new Vector3(p.Position.X, p.Position.Y, 0f)) <= StochasticUnitMoves.ArrivalRadiusMetres)
                    { p.Approached = 1; p.InPlace = 1; }   // already there: occupy at once
                    else
                        switch (StochasticUnitMoves.Step(ref p.Drive, p.Position, UnitFormation.Line, self, world))
                        {
                            case DriveResult.Moving: return NodeStatus.Running;
                            case DriveResult.Failed:
                                CommandNodes.ReportProgress(world, self, ProgressKind.TaskFailed, p.Position);
                                return NodeStatus.Failure;
                            default: p.Approached = 1; break;
                        }
                }
                p.Occupied = 1;
                p.Since = now;
                if (aggregate)
                {
                    ref var u = ref world.GetComponentRW<AggregateUnit>(self);
                    u.Move = UnitMoveState.Arrived;
                    u.Destination = p.Position;
                    u.Activity &= ~UnitActivity.Moving;
                    // ⭐ R-8342 — a unit that occupies WHERE IT STANDS keeps the position it already prepared (an authored Prepared / Fortified one, P-5);
                    //   a unit that MOVED into a new position starts Hasty, and every unit then climbs with time in position (R-8306)
                    if (p.InPlace == 0 || u.Prepared < PreparedLevel.Hasty) u.Prepared = PreparedLevel.Hasty;
                    u.PreparedSince = now;
                }
                else if (world.HasComponent<SimTransform>(self)) world.GetComponentRW<SimTransform>(self).Position = p.Position;   // a body-less test unit with no follower
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

        /// <summary>⭐ ST-1 — the order is replaced (or the commander's run ended) while the unit is still approaching: it stops where it stands.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.StochasticUnitDefendBehavior.Hold")]
        public static void Deactivate_Hold(ref StochasticDefendParams p, Entity self, EntityRepository world)
        {
            if (p.Occupied == 0) StochasticUnitMoves.StopIfMoving(self, world);
        }
    }
}