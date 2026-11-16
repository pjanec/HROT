using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Squad.Command;
using Fdp.Toolkit.Squad;
using Hrot.AI.Behaviors.Orders;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>What a drive step says (see <see cref="StochasticUnitMoves.Step"/>).</summary>
    public enum DriveResult : byte { Moving = 0, Arrived = 1, Failed = 2 }

    /// <summary>A play's progress in ONE move: whether it has ordered the unit, and how many times it re-requested a dropped route.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MoveDrive
    {
        public byte Started;
        public byte Retries;
    }

    /// <summary>The working state of <c>StochasticUnitMove</c> / <c>StochasticUnitBreakContact</c>: where to, and what it has reported (unmanaged, in the blackboard).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct StochasticMoveParams
    {
        public Vector3 Target;
        public MoveDrive Drive;
        public byte Reported;
    }

    /// <summary>
    /// ⭐⭐ agg-behaviors <c>ST-1</c> / <c>ST-2</c> (docs/designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md §23.5; B-2, B-5; R-8307…R-8310) — the ONE way a
    /// stochastic unit's PLAYS move it: <see cref="Step"/> orders the unit through <see cref="UnitMoves.Order"/> (the one way, agg-backend's) and watches
    /// <see cref="AggregateUnit.Move"/> go AwaitingRoute → OnRoute → Arrived | RouteFailed; <c>StochasticUnitMove</c>, <c>StochasticUnitBreakContact</c> and
    /// <c>StochasticUnitDefend</c>'s approach all call it, so a re-request rule has ONE owner.
    /// <para>⭐ Parallel (R-4212): a play writes only its OWN entity's <see cref="AggregateUnit"/>, and only on a change (an order or a stop) — a tick that
    /// merely watches reads. 0 B per steady tick (index loops, no closures).</para>
    /// </summary>
    public static class StochasticUnitMoves
    {
        /// <summary>⭐ R-8308 — a dropped route is re-requested at most this many times; the next drop fails the order (TaskFailed).</summary>
        public const int MaxRouteRetries = 3;

        /// <summary>⭐ R-8307 — a Defend farther than this from its position moves first (m): about a platoon's frontage.</summary>
        public const float ArrivalRadiusMetres = 30f;

        /// <summary>⭐ R-8307 — the depth a deployed LINE moves with (m): the least depth <c>StochasticAreaSink</c> takes a footprint as.</summary>
        public const float LineDepthMetres = 20f;

        /// <summary>⭐ R-8309 — how far a break-contact with no ordered point withdraws (m).</summary>
        public const float BreakContactMetres = 300f;

        /// <summary>The unit is in motion toward a destination (a route asked for, or being followed).</summary>
        public static bool InMotion(in AggregateUnit u) => u.Move == UnitMoveState.AwaitingRoute || u.Move == UnitMoveState.OnRoute;

        /// <summary>
        /// ⭐ One step of a play's move to <paramref name="target"/> (R-8307, R-8308): the first call orders the unit (a column over the
        /// route's length, <see cref="UnitMoves.ColumnDepth"/>; a line <see cref="LineDepthMetres"/> deep); every later call WATCHES it.
        /// A unit that stopped, or was sent elsewhere by another play (a reaction paused this one), is re-ordered — not counted as a retry. A
        /// <see cref="UnitMoveState.RouteFailed"/> is re-requested up to <see cref="MaxRouteRetries"/> times, then the step answers
        /// <see cref="DriveResult.Failed"/>. <see cref="DriveResult.Arrived"/> is the follower's own word (the TAIL at the end, M-3).
        /// </summary>
        public static DriveResult Step(ref MoveDrive d, Vector3 target, UnitFormation formation, Entity self, EntityRepository world)
        {
            if (!world.IsComponentTypeRegistered<AggregateUnit>() || !world.HasComponent<AggregateUnit>(self)) return DriveResult.Failed;
            ref readonly var u = ref world.GetComponentRO<AggregateUnit>(self);
            if (d.Started != 0 && (Vector3)u.Destination == target)
            {
                switch (u.Move)
                {
                    case UnitMoveState.Arrived: return DriveResult.Arrived;
                    case UnitMoveState.AwaitingRoute:
                    case UnitMoveState.OnRoute: return DriveResult.Moving;
                    case UnitMoveState.RouteFailed:
                        if (d.Retries >= MaxRouteRetries) return DriveResult.Failed;
                        d.Retries++;
                        break;
                    // Stopped: somebody held it (a reaction) — ordered again below, not a retry
                }
            }
            float depth = formation == UnitFormation.Column ? ColumnDepthOf(world, self, in u) : LineDepthMetres;   // a Line and the Assault line (B-4) are as deep as the area sink takes
            SimInstant now = CommandPictureOps.Now(world);
            UnitMoves.Order(ref world.GetComponentRW<AggregateUnit>(self), target, formation, depth, now);
            d.Started = 1;
            return DriveResult.Moving;
        }

        private static float ColumnDepthOf(EntityRepository world, Entity self, in AggregateUnit u)
        {
            var h = world.IsComponentTypeRegistered<UnitHoldings>() && world.HasComponent<UnitHoldings>(self) ? world.GetComponentRO<UnitHoldings>(self) : default;
            var slots = world.IsComponentTypeRegistered<UnitSlotTable>() && world.HasComponent<UnitSlotTable>(self) ? world.GetComponentRO<UnitSlotTable>(self) : default;
            return UnitMoves.ColumnDepth(in u, in h, in slots);
        }

        /// <summary>Stop the unit where it stands — only when it is in motion (an arrived unit keeps its state; a write bumps the version).</summary>
        public static void StopIfMoving(Entity self, EntityRepository world)
        {
            if (!world.IsComponentTypeRegistered<AggregateUnit>() || !world.HasComponent<AggregateUnit>(self)) return;
            if (InMotion(in world.GetComponentRO<AggregateUnit>(self))) UnitMoves.Stop(ref world.GetComponentRW<AggregateUnit>(self));
        }

        /// <summary>⭐ The move play's one action (Move and BreakContact share it): drive to the target; Arrived ⇒ PROGRESS Set, Success; a route dropped
        /// past the retries ⇒ PROGRESS TaskFailed, Failure.</summary>
        [SharedAiAction]
        public static NodeStatus Drive(ref StochasticMoveParams p, Entity self, EntityRepository world)
        {
            switch (Step(ref p.Drive, p.Target, UnitFormation.Column, self, world))
            {
                case DriveResult.Arrived:
                    if (p.Reported == 0) { p.Reported = 1; CommandNodes.ReportProgress(world, self, ProgressKind.Set, p.Target); }
                    return NodeStatus.Success;
                case DriveResult.Failed:
                    if (p.Reported == 0) { p.Reported = 1; CommandNodes.ReportProgress(world, self, ProgressKind.TaskFailed, p.Target); }
                    return NodeStatus.Failure;
                default:
                    return NodeStatus.Running;
            }
        }

        /// <summary>The play ends (an order replaced it, the commander's run ended): the unit stops where it stands.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.StochasticUnitMoves.Drive")]
        public static void Deactivate_Drive(ref StochasticMoveParams p, Entity self, EntityRepository world) => StopIfMoving(self, world);

        /// <summary>⭐ <c>StochasticUnitHold</c>'s action (R-8310): while the unit is SUPPRESSED (<see cref="StochasticUnitSop.SuppressedAt"/> or more)
        /// it stops where it stands; the reaction succeeds when the share has fallen, and the paused order resumes (a play that finds the unit
        /// stopped orders it again).</summary>
        [SharedAiAction]
        public static NodeStatus HoldWhileSuppressed(Entity self, EntityRepository world)
        {
            if (!StochasticUnitSop.Suppressed(self, world)) return NodeStatus.Success;
            StopIfMoving(self, world);
            return NodeStatus.Running;
        }
    }

    /// <summary>
    /// ⭐⭐ agg-behaviors <c>ST-1</c> (B-2; routing row <c>(Unit, Stochastic, Move | March | Dash)</c> — <see cref="Hrot.Core.Tkb.ResolutionRoutingTable.Plays.Move"/>) — the
    /// MOVE play of a STOCHASTIC unit: it orders the unit to the <see cref="MoveOrder.Location"/> as a column (R-8307), waits for the follower's route and the
    /// march over several ticks, re-requests a dropped route (at most 3 times, R-8308), reports PROGRESS Set on arrival and ends in Success; a route dropped past
    /// the retries reports TaskFailed and ends in Failure. The deactivator stops the unit.
    /// </summary>
    public static class StochasticUnitMoveBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Blackboard
        {
            public StochasticMoveParams Move;
        }

        [BehaviorResolver(Hrot.Core.Tkb.ResolutionRoutingTable.Plays.Move)]
        public static void Resolve(in MoveOrder order, ref Blackboard block, EntityRepository world, Entity self)
            => block.Move = new StochasticMoveParams { Target = (Vector3)order.Location };

        [Behavior(Hrot.Core.Tkb.ResolutionRoutingTable.Plays.Move, Params = typeof(MoveOrder), Fulfils = nameof(OrderKind.Move), Categories = BehaviorCategory.Commander,
                  Description = "A stochastic unit marches to a point as a column (a route from the one planner, a path follower); Set on arrival.")]
        [BTreeDefinition(Hrot.Core.Tkb.ResolutionRoutingTable.Plays.Move, Curated = true, Group = "Library/Movement", ParamsType = typeof(Blackboard))]
        public static BTreeBuilder<Blackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<Blackboard, BTreeContext>()
                .Action(bb => bb.Move, StochasticUnitMoves.Drive);
    }

    /// <summary>
    /// ⭐⭐ agg-behaviors <c>ST-2</c> (B-5; routing row <c>(Unit, Stochastic, BreakContact)</c> — <see cref="Hrot.Core.Tkb.ResolutionRoutingTable.Plays.BreakContact"/>) — the
    /// BREAK-CONTACT play of a STOCHASTIC unit: it withdraws as a column to <see cref="BreakContactOrder.To"/>; with no point (R-8309) it goes
    /// <see cref="StochasticUnitMoves.BreakContactMetres"/> straight AWAY from the nearest contact of its own squad pool, else straight BEHIND its heading
    /// (the contract carries no firer position, only the rounds that arrived). Set on arrival; a dropped route is re-requested like the Move play's.
    /// </summary>
    public static class StochasticUnitBreakContactBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Blackboard
        {
            public StochasticMoveParams Move;
        }

        [BehaviorResolver(Hrot.Core.Tkb.ResolutionRoutingTable.Plays.BreakContact)]
        public static void Resolve(in BreakContactOrder order, ref Blackboard block, EntityRepository world, Entity self)
        {
            var to = (Vector3)order.To;
            if (to == Vector3.Zero) to = AwayFromContact(world, self);
            block.Move = new StochasticMoveParams { Target = to };
        }

        [Behavior(Hrot.Core.Tkb.ResolutionRoutingTable.Plays.BreakContact, Params = typeof(BreakContactOrder), Fulfils = nameof(OrderKind.BreakContact), Categories = BehaviorCategory.Commander,
                  Description = "A stochastic unit withdraws as a column: to the ordered point, else 300 m away from the nearest contact (else behind its heading); Set on arrival.")]
        [BTreeDefinition(Hrot.Core.Tkb.ResolutionRoutingTable.Plays.BreakContact, Curated = true, Group = "Library/Combat", ParamsType = typeof(Blackboard))]
        public static BTreeBuilder<Blackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<Blackboard, BTreeContext>()
                .Action(bb => bb.Move, StochasticUnitMoves.Drive);

        /// <summary>⭐ R-8309 — the withdrawal point: <see cref="StochasticUnitMoves.BreakContactMetres"/> from where the unit stands, directly away from the nearest
        /// contact in its OWN squad pool; no contact ⇒ directly behind its heading.</summary>
        public static Vector3 AwayFromContact(EntityRepository world, Entity self)
        {
            var at = world.HasComponent<SimTransform>(self) ? (Vector3)world.GetComponentRO<SimTransform>(self).Position : Vector3.Zero;
            Vector2 dir = default;
            bool found = false;
            if (world.IsComponentTypeRegistered<SquadCognitiveState>() && world.HasComponent<SquadCognitiveState>(self))
            {
                ref readonly var squad = ref world.GetComponentRO<SquadCognitiveState>(self);
                var contacts = MemoryMarshal.CreateReadOnlySpan(
                    ref System.Runtime.CompilerServices.Unsafe.As<SquadContactPoolSlots, SquadContact>(ref System.Runtime.CompilerServices.Unsafe.AsRef(in squad.Contacts.Contacts)), 16);
                float best = float.MaxValue;
                for (int i = 0; i < squad.Contacts.Count && i < contacts.Length; i++)
                {
                    float dx = at.X - contacts[i].PositionX, dy = at.Y - contacts[i].PositionY;
                    float d2 = dx * dx + dy * dy;
                    if (d2 >= best || d2 < 1e-4f) continue;
                    best = d2;
                    dir = new Vector2(dx, dy) / MathF.Sqrt(d2);
                    found = true;
                }
            }
            if (!found)
            {
                float behind = (world.IsComponentTypeRegistered<AggregateUnit>() && world.HasComponent<AggregateUnit>(self)
                                ? world.GetComponentRO<AggregateUnit>(self).Heading.Degrees : 0f) + 180f;
                float rad = behind * (MathF.PI / 180f);
                dir = new Vector2(MathF.Sin(rad), MathF.Cos(rad));   // compass: 0 = north (+Y), clockwise
            }
            return new Vector3(at.X + dir.X * StochasticUnitMoves.BreakContactMetres, at.Y + dir.Y * StochasticUnitMoves.BreakContactMetres, at.Z);
        }
    }

    /// <summary>
    /// ⭐⭐ agg-behaviors <c>ST-2</c> (R-8310) — <c>StochasticUnitHold</c>: the SOP's SUPPRESSED reaction. No params, no order kind: it stops a moving unit while
    /// <see cref="AggregateUnit.SuppressedShare"/> is at least <see cref="StochasticUnitSop.SuppressedAt"/> and ends in Success when the share falls — the paused
    /// order then resumes (its play finds the unit stopped and orders it again).
    /// </summary>
    public static class StochasticUnitHoldBehavior
    {
        public const string Name = "StochasticUnitHold";

        [Behavior(Name, Params = typeof(NoParams), Categories = BehaviorCategory.Commander,
                  Description = "A suppressed stochastic unit stops where it stands until the suppression falls (the SOP's reaction).")]
        [BTreeDefinition(Name, Curated = true, Group = "Library/Combat")]
        public static BTreeBuilder<byte, BTreeContext> BuildTree()
            => new BTreeBuilder<byte, BTreeContext>()
                .Action(StochasticUnitMoves.HoldWhileSuppressed);
    }
}
