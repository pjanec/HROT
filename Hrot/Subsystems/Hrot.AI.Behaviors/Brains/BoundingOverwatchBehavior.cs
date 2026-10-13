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
        public const float MoverArrivalMetres = SquadManeuverRunner.MoverArrivalMetres;

        [StructLayout(LayoutKind.Sequential)]
        public struct BoundingOverwatchBlackboard
        {
            public BoundingOverwatchParams Order;
            /// <summary>⭐ <c>CE-2123</c> — the generic runner's state (was a bounding-only struct).</summary>
            public SquadPlayState State;
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
[Behavior(BehaviorNames.BoundingOverwatch, Params = typeof(global::Hrot.Map.Definitions.Behavior.BoundingOverwatchParamsJsonDto), Categories = BehaviorCategory.Commander)]   // ⭐ CE-2134

        [BTreeDefinition(BehaviorNames.BoundingOverwatch, Curated = true, Group = "Library/Squad", ParamsType = typeof(BoundingOverwatchBlackboard))]
        public static BTreeBuilder<BoundingOverwatchBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<BoundingOverwatchBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Order, bb => bb.State, Run);

        /// <summary>⭐ <c>CE-2123</c> — the commander's tick, through the generic runner (<see cref="BoundingOverwatchPlay"/>).</summary>
        public static NodeStatus Run(ref BoundingOverwatchParams p, ref SquadPlayState ws, Entity self, EntityRepository world)
            => SquadManeuverRunner.Tick<BoundingOverwatchPlay, BoundingOverwatchParams>(ref p, ref ws, self, world);
    }

    /// <summary>
    /// ⭐ <c>CE-2123</c> — bounding overwatch as a play: two elements; the moving element advances its centre <c>BoundMetres</c>
    /// toward the objective (each member keeping its offset) while the other holds and fights; every mover at its point, or the bound
    /// timed out ⇒ <see cref="PhaseEventKind.BoundComplete"/>; both elements at the objective ⇒ Success. (As built by CE-3162.)
    /// </summary>
    public struct BoundingOverwatchPlay : ISquadPlay<BoundingOverwatchParams>
    {
        private static readonly SquadHsmShell s_shell = new(
            BoundingOverwatchManeuver.BuildTransitionTable(), BoundingOverwatchManeuver.PhaseAborted);

        public SquadHsmShell Shell => s_shell;
        public ushort ManeuverKind => BoundingOverwatchManeuver.ManeuverKind;
        public ushort StartPhase => BoundingOverwatchManeuver.PhaseElement0Moving;
        public ushort AbortPhase => BoundingOverwatchManeuver.PhaseAborted;
        public byte ElementCount => 2;

        public PlayStart Start(ref BoundingOverwatchParams p, ref SquadPlayState ws, in SquadPlayContext ctx, Span<MemberPartitionInput> partition)
        {
            BoundingOverwatchManeuver.ComputePartitionInputs(ctx.Roster.Count, partition);
            return PlayStart.Go;
        }

        public void Update(ref BoundingOverwatchParams p, ref SquadPlayState ws, in SquadPlayContext ctx) { }

        public NodeStatus Outcome(ref BoundingOverwatchParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            for (byte e = 0; e < 2; e++)
            {
                var c = ctx.Centre(e, out bool any);
                if (!any) continue;
                var d = p.Objective - c; d.Z = 0f;
                if (d.LengthSquared() > p.ArrivalRadius * p.ArrivalRadius) return NodeStatus.Running;
            }
            return NodeStatus.Success;
        }

        public bool PhaseComplete(ref BoundingOverwatchParams p, ref SquadPlayState ws, in SquadPlayContext ctx, out PhaseEventKind evt)
        {
            evt = PhaseEventKind.BoundComplete;
            if (SquadManeuverRunner.ElementArrived(in ws, in ctx, BoundingOverwatchManeuver.GetMovingElement(ctx.Squad.PhaseId))) return true;
            if (ctx.Now - ws.IssuedAt < p.BoundTimeoutSeconds) return false;
            ws.CompletedByTimeout = 1;   // ⭐ CE-2157 (O-a) — the trace shows a timed-out bound as Timeout
            return true;
        }

        public void BeginIssue(ref BoundingOverwatchParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            var centre = ctx.Centre(BoundingOverwatchManeuver.GetMovingElement(ctx.Squad.PhaseId), out bool any);
            ws.Scratch = Vector3.Zero;
            if (!any) return;
            var to = p.Objective - centre; to.Z = 0f;
            float d = to.Length();
            if (d > 1e-3f) ws.Scratch = to * (MathF.Min(p.BoundMetres, d) / d);
        }

        public MemberOrder OrderFor(ref BoundingOverwatchParams p, ref SquadPlayState ws, in SquadPlayContext ctx, int member, byte element, Vector3 at)
        {
            bool mover = element == BoundingOverwatchManeuver.GetMovingElement(ctx.Squad.PhaseId);
            return new MemberOrder
            {
                Kind          = mover ? MemberOrderKind.AdvanceTo : MemberOrderKind.Hold,
                Point         = at + ws.Scratch,
                Speed         = p.Speed,
                ArrivalRadius = SquadManeuverRunner.MoverArrivalMetres,
                RoleId        = mover ? BoundingOverwatchManeuver.RoleMoving : BoundingOverwatchManeuver.RoleCovering,
            };
        }

        public void Release(ref SquadPlayState ws, EntityRepository world) { }
    }
}
