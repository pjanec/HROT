using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Fbt;
using Fbt.Compiler;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Maneuvers;
using Fdp.Toolkit.Squad.Primitives;

using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>⭐ <c>CE-3160</c> / <c>CE-507</c> D2 (a) (R-264) — the commander's bounding-overwatch order.
    /// 📄 docs/designs/group-maneuvers/DESIGN_Squad_Wiring.md §6.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct BoundingOverwatchParams
    {
        public Vector3 Objective;
        /// <summary>How far one bound carries the moving element (m).</summary>
        public float BoundMetres;
        /// <summary>The members' asked speed (m/s); their stance caps it (R-265).</summary>
        public float Speed;
        /// <summary>How close both elements must be to the objective for the manoeuvre to end (m).</summary>
        public float ArrivalRadius;
        /// <summary>A bound counts as complete after this long even if a mover never arrives (s).</summary>
        public float BoundTimeoutSeconds;
    }

    /// <summary>⭐ The commander's working memory: each member's point for the current bound.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct BoundingOverwatchState
    {
        public byte Started;
        public ushort IssuedPhase;
        public double BoundStartedAt;
        public fixed float TargetX[UnitRoster.Capacity];
        public fixed float TargetY[UnitRoster.Capacity];
        public fixed float TargetZ[UnitRoster.Capacity];
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3160</c> / <c>CE-507</c> D2 (a) (R-264) — the first commander manoeuvre that RUNS: bounding overwatch. The phases are
    /// the library's (<see cref="SquadHsmShell"/> over <see cref="BoundingOverwatchManeuver"/>'s table); this node only TRANSLATES a
    /// phase into one order per member (the moving element advances a bound toward the objective, the covering element holds and
    /// fights — both through the library <c>CombatPosture</c>) and DETECTS a finished bound (every live mover at its point, polled as
    /// the hill-attack commander polls) to feed the shell <see cref="PhaseEventKind.BoundComplete"/>.
    /// 📄 docs/designs/group-maneuvers/DESIGN_Squad_Wiring.md §6.
    /// </summary>
    public static class BoundingOverwatchBehavior
    {
        /// <summary>A mover this close to its point has finished its bound (m).</summary>
        public const float MoverArrivalMetres = 3f;

        private static readonly SquadHsmShell s_shell = new(
            BoundingOverwatchManeuver.BuildTransitionTable(), BoundingOverwatchManeuver.PhaseAborted);

        [StructLayout(LayoutKind.Sequential)]
        public struct BoundingOverwatchBlackboard
        {
            public BoundingOverwatchParams Order;
            public BoundingOverwatchState State;
        }

        [BehaviorResolver(BehaviorNames.BoundingOverwatch)]
        public static void Resolve(in BoundingOverwatchParamsJsonDto authored, ref BoundingOverwatchBlackboard block, EntityRepository world, Entity self)
        {
            block.Order = new BoundingOverwatchParams
            {
                Objective           = authored.Objective,
                BoundMetres         = authored.BoundMetres > 0f ? authored.BoundMetres : 30f,
                Speed               = authored.Speed > 0f ? authored.Speed : 3f,
                ArrivalRadius       = authored.ArrivalRadius > 0f ? authored.ArrivalRadius : 10f,
                BoundTimeoutSeconds = authored.BoundTimeoutSeconds > 0f ? authored.BoundTimeoutSeconds : 60f,
            };
            block.State = default;
        }

        [BTreeDefinition(BehaviorNames.BoundingOverwatch, Curated = true, Group = "Library/Squad", ParamsType = typeof(BoundingOverwatchBlackboard))]
        public static BTreeBuilder<BoundingOverwatchBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<BoundingOverwatchBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Order, bb => bb.State, Run);

        /// <summary>The commander's tick: start (partition, first orders) · detect a finished bound ⇒ the shell flips the phase ⇒
        /// new orders · both elements at the objective ⇒ Success. Failure when the commander has no squad.</summary>
        public static NodeStatus Run(ref BoundingOverwatchParams p, ref BoundingOverwatchState ws, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<UnitRoster>(self) || !world.HasComponent<SquadCognitiveState>(self)) return NodeStatus.Failure;
            ref readonly var roster = ref world.GetComponentRO<UnitRoster>(self);
            if (roster.Count == 0) return NodeStatus.Failure;
            ref var squad = ref world.GetComponentRW<SquadCognitiveState>(self);
            double now = Now(world);

            if (ws.Started == 0)
            {
                Span<MemberPartitionInput> inputs = stackalloc MemberPartitionInput[roster.Count];
                BoundingOverwatchManeuver.ComputePartitionInputs(roster.Count, inputs);
                ElementPartitionPrimitive.Partition(ref squad, inputs, 2, 0f, out _);
                squad.ManeuverKind = BoundingOverwatchManeuver.ManeuverKind;
                squad.PhaseId = BoundingOverwatchManeuver.PhaseElement0Moving;
                ws.Started = 1;
                Issue(ref p, ref ws, ref squad, in roster, self, world, now);
            }

            if (squad.PhaseId == BoundingOverwatchManeuver.PhaseAborted) return NodeStatus.Failure;
            if (BothAtObjective(in p, in squad, in roster, world)) return NodeStatus.Success;

            if (BoundDone(in p, ref ws, in squad, in roster, world, now))
            {
                Span<PhaseEvent> events = stackalloc PhaseEvent[1];
                events[0] = new PhaseEvent(PhaseEventKind.BoundComplete);
                s_shell.Tick(ref squad, events, (uint)world.GetSingleton<GlobalTime>().FrameNumber);
            }
            if (squad.PhaseId != ws.IssuedPhase) Issue(ref p, ref ws, ref squad, in roster, self, world, now);
            return NodeStatus.Running;
        }

        /// <summary>One order per live member for the current phase: movers advance their element's centre <c>BoundMetres</c> toward
        /// the objective (each keeping its offset), the covering element holds where it is — both run the library posture.</summary>
        private static unsafe void Issue(ref BoundingOverwatchParams p, ref BoundingOverwatchState ws, ref SquadCognitiveState squad,
                                         in UnitRoster roster, Entity self, EntityRepository world, double now)
        {
            byte moving = BoundingOverwatchManeuver.GetMovingElement(squad.PhaseId);
            var centre = Centre(in squad, in roster, world, moving, out bool any);
            var step = Vector3.Zero;
            if (any)
            {
                var to = p.Objective - centre; to.Z = 0f;
                float d = to.Length();
                if (d > 1e-3f) step = to * (MathF.Min(p.BoundMetres, d) / d);
            }
            for (int i = 0; i < roster.Count; i++)
            {
                var m = roster.SubordinateEntities[i];
                if (m.IsNull || !CombatLife.IsAlive(world, m) || !world.HasComponent<SimTransform>(m)) continue;
                var at = world.GetComponentRO<SimTransform>(m).Position;
                bool mover = squad.Elements.MemberElements[i] == moving;
                squad.Roles[i].RoleId = mover ? BoundingOverwatchManeuver.RoleMoving : BoundingOverwatchManeuver.RoleCovering;
                var point = mover ? at + step : at;
                ws.TargetX[i] = point.X; ws.TargetY[i] = point.Y; ws.TargetZ[i] = point.Z;
                Order(world, m, point, p.Speed, mover ? MoverArrivalMetres : 1f);
            }
            ws.IssuedPhase = squad.PhaseId;
            ws.BoundStartedAt = now;
        }

        private static void Order(EntityRepository world, Entity member, Vector3 point, float speed, float arrival)
        {
            var posture = new CombatPostureParamsJsonDto
            {
                Advance = new CombatPostureAdvanceJsonDto { Objective = point, Speed = speed, ArrivalRadius = arrival },
            };
            world.Bus.PublishManaged(new AssignTacticalIntentEvent
            {
                Entity     = member,
                IntentId   = BehaviorNames.CombatPosture,   // no mapper ⇒ the intent id IS the behaviour (TacticalIntentResolutionSystem)
                JsonParams = JsonSerializer.Serialize(posture, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed),
                Origin     = BehaviorOrigin.Superior,
            });
        }

        /// <summary>The bound is over when every live mover is within <see cref="MoverArrivalMetres"/> of its point, or it timed out.</summary>
        private static unsafe bool BoundDone(in BoundingOverwatchParams p, ref BoundingOverwatchState ws, in SquadCognitiveState squad,
                                             in UnitRoster roster, EntityRepository world, double now)
        {
            if (now - ws.BoundStartedAt >= p.BoundTimeoutSeconds) return true;
            byte moving = BoundingOverwatchManeuver.GetMovingElement(squad.PhaseId);
            for (int i = 0; i < roster.Count; i++)
            {
                var m = roster.SubordinateEntities[i];
                if (m.IsNull || squad.Elements.MemberElements[i] != moving) continue;
                if (!CombatLife.IsAlive(world, m) || !world.HasComponent<SimTransform>(m)) continue;
                var at = world.GetComponentRO<SimTransform>(m).Position;
                float dx = at.X - ws.TargetX[i], dy = at.Y - ws.TargetY[i];
                if (dx * dx + dy * dy > MoverArrivalMetres * MoverArrivalMetres) return false;
            }
            return true;
        }

        private static bool BothAtObjective(in BoundingOverwatchParams p, in SquadCognitiveState squad, in UnitRoster roster, EntityRepository world)
        {
            for (byte e = 0; e < 2; e++)
            {
                var c = Centre(in squad, in roster, world, e, out bool any);
                if (!any) continue;
                var d = p.Objective - c; d.Z = 0f;
                if (d.LengthSquared() > p.ArrivalRadius * p.ArrivalRadius) return false;
            }
            return true;
        }

        private static Vector3 Centre(in SquadCognitiveState squad, in UnitRoster roster, EntityRepository world, byte element, out bool any)
        {
            var sum = Vector3.Zero; int n = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                var m = roster.SubordinateEntities[i];
                if (m.IsNull || squad.Elements.MemberElements[i] != element) continue;
                if (!CombatLife.IsAlive(world, m) || !world.HasComponent<SimTransform>(m)) continue;
                sum += world.GetComponentRO<SimTransform>(m).Position; n++;
            }
            any = n > 0;
            return n > 0 ? sum / n : Vector3.Zero;
        }

        private static double Now(EntityRepository world)
            => world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : 0d;
    }
}
