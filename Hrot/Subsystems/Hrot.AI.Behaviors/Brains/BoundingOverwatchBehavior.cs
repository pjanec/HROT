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
        /// <summary>⭐ <c>CE-2158</c> F-c — runtime: each mover's cover point for his element's next bound (<see cref="SquadCover"/>).</summary>
        public SquadCoverPicks Cover;
        /// <summary>⭐ <c>CE-2284</c> — runtime: the moving element's distance to the objective when its bound was issued (m), and how many
        /// bounds in a row timed out having gained less than <see cref="BoundingOverwatchPlay.StallMetres"/>.</summary>
        public float BoundFromMetres;
        public byte Stalls;
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

        /// <summary>⭐ <c>CE-2158</c> F-c — the play owns a cover query now: it goes when the run is left.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.BoundingOverwatchBehavior.Run")]
        public static void Deactivate_Run(ref BoundingOverwatchParams p, ref SquadPlayState ws, Entity self, EntityRepository world)
            => SquadManeuverRunner.DropSensor(ref ws, world);
    }

    /// <summary>
    /// ⭐ <c>CE-2123</c> — bounding overwatch as a play: two elements; the moving element advances its centre <c>BoundMetres</c>
    /// toward the objective (each member keeping its offset) while the other holds and fights; every mover at its point, or the bound
    /// timed out ⇒ <see cref="PhaseEventKind.BoundComplete"/>; both elements at the objective ⇒ Success. (As built by CE-3162.)
    /// </summary>
    public struct BoundingOverwatchPlay : ISquadPlay<BoundingOverwatchParams>
    {
        /// <summary>⭐ <c>CE-2158</c> F-c — the commander's cover query for the next element's bound.</summary>
        public const int CoverSite = 0x21580002;
        /// <summary>A bound may stretch this much past <c>BoundMetres</c> to end in cover ("bound 20–50 m to the next cover").</summary>
        public const float CoverStretch = 1.25f;
        /// <summary>A mover sent to a cover point stops this close to it (m): the point stands 0.75 m off the wall.</summary>
        public const float CoverArrivalMetres = 1f;

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
            p.Cover = default;
            p.Stalls = 0;
            return PlayStart.Go;
        }

        /// <summary>⭐ <c>CE-2158</c> F-c — track the squad's top contact (on contact the watchers suppress it), and pick the NEXT element's
        /// cover while this one bounds: hidden from the contact, or before contact from the objective, where the enemy is expected.</summary>
        public void Update(ref BoundingOverwatchParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            if (SquadManeuverRunner.TopContact(in ctx, ws.ThreatId, out var contact, liveOnly: true))
            {
                if (contact.EntityId != ws.ThreatId) ws.Reissue = 1;   // contact: the watchers start (or switch) suppressing
                ws.ThreatId = contact.EntityId;
                ws.ThreatPoint = new Vector3(contact.PositionX, contact.PositionY, contact.PositionZ);
            }
            else if (ws.ThreatId != 0) { ws.ThreatId = 0; ws.Reissue = 1; }   // the contact is down: the watchers go back to watching
            byte next = (byte)(1 - BoundingOverwatchManeuver.GetMovingElement(ctx.Squad.PhaseId));
            if (!SquadCover.IsReady(in p.Cover, next))
                SquadCover.Make(ref p.Cover, ref ws, in ctx, CoverSite, next, p.Objective, p.BoundMetres * CoverStretch,
                                ws.ThreatId != 0 ? new Entity((ulong)ws.ThreatId) : Entity.Null, p.Objective, ws.IssuedAt);
        }

        /// <summary>⭐ <c>CE-2284</c> — a bound that times out having brought its element less than this much closer to the objective (m)
        /// is a STALL · this many in a row and the squad is set where it stands.</summary>
        public const float StallMetres = 2f;
        public const int StallsToSet = 2;

        public NodeStatus Outcome(ref BoundingOverwatchParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            // ⭐ CE-2284 — the objective cannot be reached from here (📌 pc-urban-ambush: the assault's flank point lay behind the buildings
            //   north of Main Street; both elements walked to the wall face and every bound re-issued the same blocked line for 9 minutes,
            //   so the squad never reported Set and the platoon never assaulted): set where it stands
            if (p.Stalls >= StallsToSet) return NodeStatus.Success;
            for (byte e = 0; e < 2; e++)
            {
                var c = ctx.Centre(e, out bool any);
                if (!any) continue;
                var d = p.Objective - c; d.Z = 0f;
                if (d.LengthSquared() > p.ArrivalRadius * p.ArrivalRadius) return NodeStatus.Running;
            }
            // ⭐ CE-2158 F-c — a mover still bounding to a COVER point finishes the bound (or it times out): ⛔ the drill does not end
            //   with him in the open because both centres came within ArrivalRadius (measured live: the last bound to Block B's face
            //   was cut off 5–18 m short). A geometric bound ends as before.
            if (ws.IssuedPhase == ctx.Squad.PhaseId && ctx.Now - ws.IssuedAt < p.BoundTimeoutSeconds)
            {
                ushort movers = SquadGates.Element(in ctx, BoundingOverwatchManeuver.GetMovingElement(ctx.Squad.PhaseId));
                for (int i = 0; i < ctx.Roster.Count && i < 16; i++)
                {
                    if ((movers & (1 << i)) == 0 || !ctx.Live(i, out var m) || !SquadCover.Has(in p.Cover, i, out var pick)) continue;
                    var d = ctx.At(m) - pick; d.Z = 0f;
                    if (d.Length() > SquadGates.SetMetres) return NodeStatus.Running;
                }
            }
            return NodeStatus.Success;
        }

        /// <summary>
        /// ⭐ <c>CE-2158</c> F-c — every live mover is at his point: a man sent to a COVER point within <see cref="SquadGates.SetMetres"/>
        /// (he is ordered to 1 m of it — in cover, not near it), a man on a geometric bound within the runner's 3 m.
        /// ⛔ SUPERSEDED: one 3 m gate for all — measured live, the swap came while the cover movers were 2.6–3.9 m short, and the next
        /// order held them there.
        /// </summary>
        private static unsafe bool MoversSet(in BoundingOverwatchParams p, in SquadPlayState ws, in SquadPlayContext ctx, byte element)
        {
            ushort movers = SquadGates.Element(in ctx, element);
            for (int i = 0; i < ctx.Roster.Count && i < 16; i++)
            {
                if ((movers & (1 << i)) == 0 || !ctx.Live(i, out var m)) continue;
                bool cover = SquadCover.Has(in p.Cover, i, out var pick);
                var target = cover ? pick : new Vector3(ws.TargetX[i], ws.TargetY[i], ws.TargetZ[i]);
                var d = ctx.At(m) - target; d.Z = 0f;
                if (d.Length() > (cover ? SquadGates.SetMetres : SquadManeuverRunner.MoverArrivalMetres)) return false;
            }
            return true;
        }

        public bool PhaseComplete(ref BoundingOverwatchParams p, ref SquadPlayState ws, in SquadPlayContext ctx, out PhaseEventKind evt)
        {
            byte moving = BoundingOverwatchManeuver.GetMovingElement(ctx.Squad.PhaseId);
            // ⭐ CE-2158 F-c — every mover at his point is "set" (DefiladeReached); only the clock is BoundComplete (traced as Timeout).
            evt = PhaseEventKind.DefiladeReached;
            if (!MoversSet(in p, in ws, in ctx, moving))
            {
                if (ctx.Now - ws.IssuedAt < p.BoundTimeoutSeconds) return false;
                evt = PhaseEventKind.BoundComplete;
                ws.CompletedByTimeout = 1;   // ⭐ CE-2157 (O-a) — the trace shows a timed-out bound as Timeout
                // ⭐ CE-2284 — a timed-out bound that gained (almost) nothing is a stall
                var at = ctx.Centre(moving, out bool any);
                var left = p.Objective - at; left.Z = 0f;
                if (any && p.BoundFromMetres - left.Length() < StallMetres) { if (p.Stalls < 255) p.Stalls++; }
                else p.Stalls = 0;
            }
            else p.Stalls = 0;
            SquadCover.Spend(ref p.Cover, in ctx, moving);   // the bound is over: its next one picks afresh
            return true;
        }

        public void BeginIssue(ref BoundingOverwatchParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            var centre = ctx.Centre(BoundingOverwatchManeuver.GetMovingElement(ctx.Squad.PhaseId), out bool any);
            ws.Scratch = Vector3.Zero;
            if (!any) return;
            var to = p.Objective - centre; to.Z = 0f;
            float d = to.Length();
            p.BoundFromMetres = d;   // ⭐ CE-2284 — where this bound starts, for the stall test
            if (d > 1e-3f) ws.Scratch = to * (MathF.Min(p.BoundMetres, d) / d);
        }

        public MemberOrder OrderFor(ref BoundingOverwatchParams p, ref SquadPlayState ws, in SquadPlayContext ctx, int member, byte element, Vector3 at)
        {
            bool mover = element == BoundingOverwatchManeuver.GetMovingElement(ctx.Squad.PhaseId);
            if (!mover)
            {
                // ⭐ CE-2158 F-c — before contact the watchers watch; on contact they suppress the squad's top contact
                if (ws.ThreatId == 0)
                    return new MemberOrder { Kind = MemberOrderKind.Hold, Speed = p.Speed, RoleId = BoundingOverwatchManeuver.RoleCovering };
                return new MemberOrder
                {
                    Kind = MemberOrderKind.Suppress, Target = new Entity((ulong)ws.ThreatId), Point = ws.ThreatPoint, Speed = p.Speed,
                    RoleId = BoundingOverwatchManeuver.RoleCovering,
                };
            }
            bool cover = SquadCover.Has(in p.Cover, member, out var pick);   // ⭐ F-c — a cover point, else the formation's bound
            return new MemberOrder
            {
                Kind          = MemberOrderKind.AdvanceTo,
                Point         = cover ? pick : at + ws.Scratch,
                Cover         = cover ? (byte)1 : (byte)0,
                Speed         = p.Speed,
                ArrivalRadius = cover ? CoverArrivalMetres : SquadManeuverRunner.MoverArrivalMetres,   // in cover, not 3 m from it
                RoleId        = BoundingOverwatchManeuver.RoleMoving,
            };
        }

        public void Release(ref SquadPlayState ws, EntityRepository world) => SquadManeuverRunner.DropSensor(ref ws, world);
    }
}
