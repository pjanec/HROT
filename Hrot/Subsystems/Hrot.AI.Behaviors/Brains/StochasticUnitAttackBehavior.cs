using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Command;
using Hrot.AI.Behaviors.Orders;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>The working state of <c>StochasticUnitAttack</c> (unmanaged, in the blackboard): the objective, the assault distance point, the phase and what it has reported.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct StochasticAttackParams
    {
        /// <summary>The place to take (the <see cref="AttackOrder.Position"/>, else the target's position — followed with a hysteresis).</summary>
        public Vector3 Objective;
        /// <summary>Where the unit stood when the attack began; the approach point is on the line from here to the objective.</summary>
        public Vector3 Start;
        /// <summary>The point at the assault distance from the objective, on the line from <see cref="Start"/>.</summary>
        public Vector3 Approach;
        /// <summary>The ordered target entity (packed), 0 = a position attack.</summary>
        public ulong Target;
        public float SearchRadius;
        /// <summary>The current leg's move.</summary>
        public MoveDrive Drive;
        /// <summary>0 = not started · 1 = approaching · 2 = assaulting · 3 = holding back (support by fire).</summary>
        public byte Phase;
        /// <summary>1 = at the objective (the assault's move arrived).</summary>
        public byte AtObjective;
        public byte HoldBack;
        /// <summary>Bit 0 = PhaseLine sent · bit 1 = Set sent (hold-back) · bit 2 = the end report sent.</summary>
        public byte Reported;
        /// <summary>⭐ CE-8302 — 1 = support by fire at a POSITION: the approach ends ON the objective (the support position), not at the assault distance short of it.</summary>
        public byte HoldOn;
    }

    /// <summary>
    /// ⭐⭐ agg-behaviors <c>ST-3</c> (docs/designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md §19 B-4, B-6, B-8; §23.5 R-8312 … R-8318; routing row
    /// <c>(Unit, Stochastic, Attack | ClearBuilding | Bound | FireAndMovement | SupportByFire)</c> — <see cref="Hrot.Core.Tkb.ResolutionRoutingTable.Plays.Attack"/>) — the ATTACK
    /// play of a STOCHASTIC unit, one for every echelon and every attack-shaped order, reading the neutral <see cref="AttackOrder"/> (R-8300).
    /// <list type="number">
    /// <item><b>Approach</b>: as a deployed LINE (<see cref="StochasticUnitMoves.Step"/>) to the ASSAULT DISTANCE (<see cref="AssaultDistanceMetres"/>, R-8312) from the
    /// objective (<see cref="AttackOrder.Position"/>, else the target's position). It FIRES the whole way — agg-backend's phase A (<c>StochasticFirePhase</c>) fires every live
    /// system at every reported contact in range, moving or not (finding F-8: it has no base-of-fire share input).</item>
    /// <item><b>PhaseLine</b> (PROGRESS, index 1) on reaching the assault distance.</item>
    /// <item><b>Assault</b>: the <see cref="UnitFormation.Assault"/> formation closes on the objective.</item>
    /// <item><b>Success</b> once AT the objective with no enemy unit STANDING (<see cref="CombatEffective.IsStanding"/>, which answers a stochastic unit) within the search radius
    /// (<see cref="AttackOrder.SearchRadius"/>, default <see cref="DefaultSearchRadiusMetres"/> — R-8313) of it among the unit's REPORTED contacts and the ordered target ⇒ PROGRESS ObjectiveSecure,
    /// the play ends in Success. <b>Failure</b> when the attacker itself is no longer standing ⇒ PROGRESS TaskFailed, Failure.</item>
    /// <item><see cref="AttackOrder.HoldBack"/> (support by fire — the <c>SupportByFire</c> kind; ⭐ CE-8302, design §23.5d R-8341): the <see cref="AttackOrder.Position"/> is the
    /// SUPPORT POSITION the commander chose — the same point its physical base of fire is sent to (<c>CompanyAttackBehavior.SupportAt</c>, 250 m short; the platoon's 120 m) — so the
    /// unit moves ONTO it as a line, HOLDS there firing (phase A fires at whatever is in reach), reports Set and never ends by itself (it is still failed when the unit breaks).
    /// ⛔ It never closes to the assault distance of the enemy (B-8: "Attack without closing"). A HoldBack on a TARGET entity alone (no position) holds at the assault distance as before.</item>
    /// </list>
    /// ⭐ Parallel (R-4212): it writes only its OWN entity's <see cref="AggregateUnit"/> (an order, only on a change); the enemies are READ (their slots, position — declared stable
    /// reads). 0 B per steady tick.
    /// </summary>
    public static class StochasticUnitAttackBehavior
    {
        /// <summary>⭐ R-8312 (lane decision) — the distance from the objective the approach ends at and the assault begins (m): inside rifle range, outside the objective itself.</summary>
        public const float AssaultDistanceMetres = 100f;

        /// <summary>⭐ R-8313 (lane decision) — the default radius round the objective that must hold no standing enemy (m); the neutral order's <see cref="AttackOrder.SearchRadius"/> overrides it.</summary>
        public const float DefaultSearchRadiusMetres = 75f;

        /// <summary>A followed target that moved less than this is not chased (m): no re-order every tick.</summary>
        public const float ChaseHysteresisMetres = 20f;

        private const byte SentPhaseLine = 1, SentSet = 2, SentEnd = 4;

        [StructLayout(LayoutKind.Sequential)]
        public struct Blackboard
        {
            public StochasticAttackParams Attack;
        }

        [BehaviorResolver(Hrot.Core.Tkb.ResolutionRoutingTable.Plays.Attack)]
        public static void Resolve(in AttackOrder order, ref Blackboard block, EntityRepository world, Entity self)
        {
            var objective = (Vector3)order.Position;
            ulong target = 0;
            if (order.Target.Ref.TryResolve(world, out var t))
            {
                target = t.PackedValue;
                if (objective == Vector3.Zero && world.HasComponent<SimTransform>(t)) objective = world.GetComponentRO<SimTransform>(t).Position;
            }
            block.Attack = new StochasticAttackParams
            {
                Objective = objective, Target = target, HoldBack = (byte)(order.HoldBack ? 1 : 0),
                HoldOn = (byte)(order.HoldBack && (Vector3)order.Position != Vector3.Zero ? 1 : 0),   // ⭐ CE-8302 — the commander's support position: stand on it
                SearchRadius = order.SearchRadius > 0f ? order.SearchRadius : DefaultSearchRadiusMetres,
            };
        }

        [Behavior(Hrot.Core.Tkb.ResolutionRoutingTable.Plays.Attack, Params = typeof(AttackOrder), Fulfils = nameof(OrderKind.Attack), Categories = BehaviorCategory.Commander,
                  Description = "A stochastic unit attacks a place or a target: approaches in line firing, assaults from 100 m, secures the objective when no enemy stands near it; HoldBack = support by fire (never closes).")]
        [BTreeDefinition(Hrot.Core.Tkb.ResolutionRoutingTable.Plays.Attack, Curated = true, Group = "Library/Combat", ParamsType = typeof(Blackboard))]
        public static BTreeBuilder<Blackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<Blackboard, BTreeContext>()
                .Action(bb => bb.Attack, Attack);

        [SharedAiAction]
        public static NodeStatus Attack(ref StochasticAttackParams p, Entity self, EntityRepository world)
        {
            if ((p.Reported & SentEnd) != 0) return NodeStatus.Failure;   // ended already (the tree is ticked once more before it stops)
            // the attacker itself is broken ⇒ the attack has failed
            if (!CombatEffective.IsStanding(world, self)) return Fail(ref p, self, world);
            Follow(ref p, world);
            if (p.Objective == Vector3.Zero && p.Target == 0) return Fail(ref p, self, world);   // nothing to attack
            var at = world.HasComponent<SimTransform>(self) ? (Vector3)world.GetComponentRO<SimTransform>(self).Position : p.Objective;
            if (p.Phase == 0)
            {
                p.Start = at;
                p.Phase = Flat(at, p.Objective) <= (p.HoldOn != 0 ? 0f : AssaultDistanceMetres) + StochasticUnitMoves.ArrivalRadiusMetres ? Reached(ref p, self, world) : (byte)1;
            }
            if (p.Phase == 1)
            {
                p.Approach = p.HoldOn != 0 ? p.Objective : ApproachPoint(p.Start, p.Objective);   // re-aimed when a followed target moved; ⭐ CE-8302 support by fire stands ON its position
                switch (StochasticUnitMoves.Step(ref p.Drive, p.Approach, UnitFormation.Line, self, world))
                {
                    case DriveResult.Failed: return Fail(ref p, self, world);
                    case DriveResult.Arrived: p.Phase = Reached(ref p, self, world); p.Drive = default; break;
                    default: return NodeStatus.Running;
                }
            }
            if (p.Phase == 3)
            {
                if ((p.Reported & SentSet) == 0) { p.Reported |= SentSet; CommandNodes.ReportProgress(world, self, ProgressKind.Set, p.Objective); }
                return NodeStatus.Running;   // support by fire: it holds at the assault distance and fires (phase A) — it never ends by itself
            }
            // 2 — the assault: the Assault line closes on the objective
            switch (StochasticUnitMoves.Step(ref p.Drive, p.Objective, UnitFormation.Assault, self, world))
            {
                case DriveResult.Failed: return Fail(ref p, self, world);
                case DriveResult.Arrived: p.AtObjective = 1; break;
                default: p.AtObjective = 0; break;
            }
            if (p.AtObjective != 0 && !EnemyStandingNear(world, self, in p))
            {
                p.Reported |= SentEnd;
                CommandNodes.ReportProgress(world, self, ProgressKind.ObjectiveSecure, p.Objective);
                return NodeStatus.Success;
            }
            return NodeStatus.Running;
        }

        /// <summary>The unit reached the assault distance: PhaseLine once, then the next phase (3 = hold when support by fire, 2 = assault).</summary>
        private static byte Reached(ref StochasticAttackParams p, Entity self, EntityRepository world)
        {
            // the assault line reached (⭐ CE-8302: a base of fire on its own position has no assault line — its report is the Set below)
            if ((p.Reported & SentPhaseLine) == 0 && p.HoldOn == 0) { p.Reported |= SentPhaseLine; CommandNodes.ReportProgress(world, self, ProgressKind.PhaseLine, p.Objective, 1); }
            p.Drive = default;
            return p.HoldBack != 0 ? (byte)3 : (byte)2;
        }

        private static NodeStatus Fail(ref StochasticAttackParams p, Entity self, EntityRepository world)
        {
            if ((p.Reported & SentEnd) == 0) { p.Reported |= SentEnd; CommandNodes.ReportProgress(world, self, ProgressKind.TaskFailed, p.Objective); }
            StochasticUnitMoves.StopIfMoving(self, world);
            return NodeStatus.Failure;
        }

        /// <summary>The objective follows a TARGET entity that moved more than <see cref="ChaseHysteresisMetres"/> (a position attack stays put).</summary>
        private static void Follow(ref StochasticAttackParams p, EntityRepository world)
        {
            if (p.Target == 0) return;
            var t = new Entity(p.Target);
            if (!world.IsAlive(t) || !world.HasComponent<SimTransform>(t)) return;
            var now = (Vector3)world.GetComponentRO<SimTransform>(t).Position;
            if (p.Objective == Vector3.Zero || Flat(now, p.Objective) > ChaseHysteresisMetres) p.Objective = now;
        }

        /// <summary>The point <see cref="AssaultDistanceMetres"/> short of <paramref name="objective"/> on the line from <paramref name="from"/>.</summary>
        public static Vector3 ApproachPoint(Vector3 from, Vector3 objective)
        {
            var d = new Vector2(objective.X - from.X, objective.Y - from.Y);
            float len = d.Length();
            if (len < 1e-3f) return objective;
            if (len <= AssaultDistanceMetres) return from;
            var dir = d / len;
            return new Vector3(objective.X - dir.X * AssaultDistanceMetres, objective.Y - dir.Y * AssaultDistanceMetres, objective.Z);
        }

        private static float Flat(Vector3 a, Vector3 b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        /// <summary>
        /// ⭐ R-8312 / R-8313 — a STANDING enemy within the search radius of the objective: the ordered target itself (when there is one), or any enemy unit among the attacker's own REPORTED
        /// contacts (<see cref="UnitDetections"/>, what it knows). A stochastic enemy only — a physical one is refused until ST-5 (C-7). Allocation-free (an index walk).
        /// </summary>
        public static bool EnemyStandingNear(EntityRepository world, Entity self, in StochasticAttackParams p)
        {
            if (p.Target != 0 && StandingNear(world, new Entity(p.Target), in p)) return true;
            if (!world.IsComponentTypeRegistered<UnitDetections>() || !world.HasComponent<UnitDetections>(self)) return false;
            ref readonly var det = ref world.GetComponentRO<UnitDetections>(self);
            for (int i = 0; i < det.Count; i++)
            {
                ref readonly var pair = ref det.Pairs[i];
                if (pair.Reported != 0 && StandingNear(world, pair.Target, in p)) return true;
            }
            return false;
        }

        private static bool StandingNear(EntityRepository world, Entity t, in StochasticAttackParams p)
            => !t.IsNull && world.IsAlive(t) && AggregateUnits.IsStochastic(world, t) && world.HasComponent<SimTransform>(t)
               && Flat(world.GetComponentRO<SimTransform>(t).Position, p.Objective) <= p.SearchRadius
               && CombatEffective.IsStanding(world, t);

        /// <summary>The attack ends (an order replaced it, the commander's run ended): the unit stops where it stands.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.StochasticUnitAttackBehavior.Attack")]
        public static void Deactivate_Attack(ref StochasticAttackParams p, Entity self, EntityRepository world) => StochasticUnitMoves.StopIfMoving(self, world);
    }
}
