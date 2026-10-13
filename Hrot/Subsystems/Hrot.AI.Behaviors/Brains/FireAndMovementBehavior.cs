using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Maneuvers;
using Fdp.Toolkit.Squad.Primitives;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>⭐ <c>CE-2158</c> F-b — fire and movement's params; the pick fields are the play's own memory (each mover's cover point for
    /// the coming rush).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct FireAndMovementParams
    {
        /// <summary>Where the squad closes to.</summary>
        public Vector3 Objective;
        /// <summary>The longest one rush (m).</summary>
        public float RushMetres;
        /// <summary>The movers' asked speed (m/s).</summary>
        public float Speed;
        /// <summary>Both halves' centres this close to <see cref="Objective"/> ⇒ Success (m).</summary>
        public float ArrivalRadius;
        /// <summary>The movers go after this long even when not every coverer has fired (s).</summary>
        public float CoveringSeconds;
        /// <summary>A rush counts as set after this long (s).</summary>
        public float RushTimeoutSeconds;
        /// <summary>Runtime: each mover's point for the coming rush, by roster slot.</summary>
        public fixed float PickX[16];
        public fixed float PickY[16];
        public fixed float PickZ[16];
        /// <summary>Runtime: the roster bits whose pick is a cover point (the query's), not a geometric one.</summary>
        public ushort CoverMask;
        /// <summary>Runtime: the Cover phase the picks were made for, plus one (0 = none yet).</summary>
        public ushort PickedPhase;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2158</c> F-b (R-2013; 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §7.6.1) — fire and movement as a play, for a
    /// squad in contact: two halves; in <c>Cover_e</c> half <c>1−e</c> suppresses the squad's top contact while half <c>e</c> holds and the
    /// commander picks each mover a cover point AHEAD (its <c>FindCoverFromTarget</c> query, the whole answer buffer); "covering" (every
    /// base-of-fire man fired in the last 2 s, <see cref="SquadGates.Covering"/>) ⇒ <c>Rush_e</c>: the movers go to their points; "set"
    /// (every mover at his point, <see cref="SquadGates.Set"/>) ⇒ the halves swap. Both halves within <c>ArrivalRadius</c> of the
    /// objective ⇒ Success; a half with nobody left in the fight ⇒ Failure.
    /// </summary>
    public struct FireAndMovementPlay : ISquadPlay<FireAndMovementParams>
    {
        /// <summary>The commander's cover query, one per bound.</summary>
        public const int CoverSite = 0x21580001;
        /// <summary>A pick must bring the mover at least this much nearer the objective (m).</summary>
        public const float MinProgressMetres = 3f;
        /// <summary>Two movers' picks at least this far apart (m).</summary>
        public const float PickSpacingMetres = 2f;
        /// <summary>The query gives up after this long; the movers get geometric points (s).</summary>
        public const float PickTimeoutSeconds = 5f;

        private static readonly SquadHsmShell s_shell = new(FireAndMovementManeuver.BuildTransitionTable(), FireAndMovementManeuver.PhaseAborted);

        public SquadHsmShell Shell => s_shell;
        public ushort ManeuverKind => FireAndMovementManeuver.ManeuverKind;
        public ushort StartPhase => FireAndMovementManeuver.PhaseCover0;
        public ushort AbortPhase => FireAndMovementManeuver.PhaseAborted;
        public byte ElementCount => 2;

        public PlayStart Start(ref FireAndMovementParams p, ref SquadPlayState ws, in SquadPlayContext ctx, Span<MemberPartitionInput> partition)
        {
            int live = 0;
            for (int i = 0; i < ctx.Roster.Count; i++) if (ctx.Live(i, out _)) live++;
            if (live < 2) return PlayStart.Fail;
            BoundingOverwatchManeuver.ComputePartitionInputs(ctx.Roster.Count, partition);   // first half, second half
            p.PickedPhase = 0;
            p.CoverMask = 0;
            return PlayStart.Go;
        }

        public void Update(ref FireAndMovementParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            if (SquadManeuverRunner.TopContact(in ctx, ws.ThreatId, out var contact))
            {
                if (contact.EntityId != ws.ThreatId) ws.Reissue = 1;   // a new contact to suppress
                ws.ThreatId = contact.EntityId;
                ws.ThreatPoint = new Vector3(contact.PositionX, contact.PositionY, contact.PositionZ);
            }
            SquadGates.Track(ref ws.Gate, in ctx);
            ushort phase = ctx.Squad.PhaseId;
            byte e = FireAndMovementManeuver.MovingElement(phase);
            if (!FireAndMovementManeuver.IsRush(phase) && p.PickedPhase != phase + 1) PickCover(ref p, ref ws, in ctx, phase, e);
            SquadDrillTrace.Gates(ctx.World, ctx.Commander,
                SquadGates.Covering(in ws.Gate, in ctx, SquadGates.Element(in ctx, (byte)(1 - e))),
                SquadGates.Set(in ws, in ctx, SquadGates.Element(in ctx, e), SquadGates.SetMetres));
        }

        public NodeStatus Outcome(ref FireAndMovementParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            bool near = true;
            for (byte e = 0; e < 2; e++)
            {
                var c = ctx.Centre(e, out bool any);
                if (!any) return NodeStatus.Failure;   // a half is gone: the squad decides again
                var d = p.Objective - c; d.Z = 0f;
                if (d.LengthSquared() > p.ArrivalRadius * p.ArrivalRadius) near = false;
            }
            return near ? NodeStatus.Success : NodeStatus.Running;
        }

        public bool PhaseComplete(ref FireAndMovementParams p, ref SquadPlayState ws, in SquadPlayContext ctx, out PhaseEventKind evt)
        {
            ushort phase = ctx.Squad.PhaseId;
            byte e = FireAndMovementManeuver.MovingElement(phase);
            if (FireAndMovementManeuver.IsRush(phase))
            {
                evt = PhaseEventKind.DefiladeReached;   // "Set!"
                if (SquadGates.Set(in ws, in ctx, SquadGates.Element(in ctx, e), SquadGates.SetMetres)) return true;
                if (ctx.Now - ws.IssuedAt < p.RushTimeoutSeconds) return false;
                ws.CompletedByTimeout = 1;
                return true;
            }
            evt = PhaseEventKind.ShotFired;   // "Covering!"
            if (p.PickedPhase != phase + 1) return false;
            if (SquadGates.Covering(in ws.Gate, in ctx, SquadGates.Element(in ctx, (byte)(1 - e)))) return true;
            if (ctx.Now - ws.IssuedAt < p.CoveringSeconds) return false;
            ws.CompletedByTimeout = 1;   // the movers go anyway: the covering never came
            return true;
        }

        public void BeginIssue(ref FireAndMovementParams p, ref SquadPlayState ws, in SquadPlayContext ctx) { }

        public unsafe MemberOrder OrderFor(ref FireAndMovementParams p, ref SquadPlayState ws, in SquadPlayContext ctx, int member, byte element, Vector3 at)
        {
            ushort phase = ctx.Squad.PhaseId;
            if (element == FireAndMovementManeuver.MovingElement(phase))
            {
                if (!FireAndMovementManeuver.IsRush(phase))
                    return new MemberOrder { Kind = MemberOrderKind.Hold, Speed = p.Speed, RoleId = FireAndMovementManeuver.RoleMoving };
                return new MemberOrder   // RushTo
                {
                    Kind = MemberOrderKind.AdvanceTo, Point = new Vector3(p.PickX[member], p.PickY[member], p.PickZ[member]), Speed = p.Speed,
                    ArrivalRadius = 1f, Cover = (p.CoverMask & (1 << member)) != 0 ? (byte)1 : (byte)0, RoleId = FireAndMovementManeuver.RoleMoving,
                };
            }
            if (ws.ThreatId == 0) return new MemberOrder { Kind = MemberOrderKind.Hold, Speed = p.Speed, RoleId = FireAndMovementManeuver.RoleCovering };
            return new MemberOrder
            {
                Kind = MemberOrderKind.Suppress, Target = new Entity((ulong)ws.ThreatId), Point = ws.ThreatPoint, Speed = p.Speed,
                RoleId = FireAndMovementManeuver.RoleCovering,
            };
        }

        public void Release(ref SquadPlayState ws, EntityRepository world) => SquadManeuverRunner.DropSensor(ref ws, world);

        /// <summary>Ask for cover around half <paramref name="e"/>'s first mover (hidden from the threat) and give each mover the answer
        /// that brings him furthest toward the objective, within one rush of him; no threat, no answer, or the query too slow ⇒ a
        /// geometric point one rush toward the objective.</summary>
        private static void PickCover(ref FireAndMovementParams p, ref SquadPlayState ws, in SquadPlayContext ctx, ushort phase, byte e)
        {
            var lead = SquadManeuverRunner.FirstLive(in ctx, e);
            Span<EqsResult> answers = stackalloc EqsResult[16];
            int count = 0;
            if (ws.ThreatId != 0 && !lead.IsNull)
            {
                bool answered = SquadManeuverRunner.AskTerrainAll(ref ws, in ctx, FindCoverFromTarget.BlueprintId, CoverSite,
                    p.RushMetres + 5f, lead, new Entity((ulong)ws.ThreatId), out var buffer);
                if (!answered)
                {
                    if (ctx.Now - ws.IssuedAt < PickTimeoutSeconds) return;
                    SquadManeuverRunner.DropSensor(ref ws, ctx.World);
                }
                else
                {
                    count = Math.Min(buffer.Count, 16);
                    buffer.GetSpanRO()[..count].CopyTo(answers);
                }
            }
            Pick(ref p, in ctx, e, answers[..count]);
            p.PickedPhase = (ushort)(phase + 1);
        }

        /// <summary>One point per live mover of half <paramref name="e"/> (see <see cref="PickCover"/>).</summary>
        public static unsafe void Pick(ref FireAndMovementParams p, in SquadPlayContext ctx, byte e, ReadOnlySpan<EqsResult> answers)
        {
            p.CoverMask = 0;
            ushort movers = SquadGates.Element(in ctx, e);
            Span<Vector2> taken = stackalloc Vector2[16];
            int takenCount = 0;
            for (int i = 0; i < ctx.Roster.Count && i < 16; i++)
            {
                if ((movers & (1 << i)) == 0 || !ctx.Live(i, out var m)) continue;
                var me = ctx.At(m);
                var toObj = new Vector2(p.Objective.X - me.X, p.Objective.Y - me.Y);
                float dist = toObj.Length();
                int best = -1;
                float bestProgress = MinProgressMetres;
                for (int k = 0; k < answers.Length; k++)
                {
                    var pt = new Vector2(answers[k].PositionX, answers[k].PositionY);
                    if (Vector2.Distance(pt, new Vector2(me.X, me.Y)) > p.RushMetres + 2f) continue;
                    float progress = dist - Vector2.Distance(pt, new Vector2(p.Objective.X, p.Objective.Y));
                    if (progress < bestProgress || Near(taken[..takenCount], pt)) continue;
                    best = k; bestProgress = progress;
                }
                Vector3 point;
                if (best >= 0)
                {
                    point = new Vector3(answers[best].PositionX, answers[best].PositionY, answers[best].PositionZ);
                    p.CoverMask |= (ushort)(1 << i);
                }
                else
                {
                    float step = dist > 1e-3f ? MathF.Min(p.RushMetres, dist) / dist : 0f;
                    point = new Vector3(me.X + toObj.X * step, me.Y + toObj.Y * step, me.Z);
                }
                p.PickX[i] = point.X; p.PickY[i] = point.Y; p.PickZ[i] = point.Z;
                taken[takenCount++] = new Vector2(point.X, point.Y);
            }
        }

        private static bool Near(ReadOnlySpan<Vector2> taken, Vector2 pt)
        {
            foreach (var t in taken) if (Vector2.Distance(t, pt) < PickSpacingMetres) return true;
            return false;
        }
    }

    /// <summary>
    /// ⭐ <c>CE-2158</c> F-b — the library <c>FireAndMovement</c>: a commander closes on an objective in contact, by halves.
    /// </summary>
    public static class FireAndMovementBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct FireAndMovementBlackboard
        {
            public FireAndMovementParams Order;
            public SquadPlayState State;
        }

        [BehaviorResolver(BehaviorNames.FireAndMovement)]
        public static void Resolve(in FireAndMovementParamsJsonDto authored, ref FireAndMovementBlackboard block, EntityRepository world, Entity self)
        {
            block.Order = new FireAndMovementParams
            {
                Objective          = authored.Objective,
                RushMetres         = authored.RushMetres > 0f ? authored.RushMetres : 15f,
                Speed              = authored.Speed > 0f ? authored.Speed : 4f,
                ArrivalRadius      = authored.ArrivalRadius > 0f ? authored.ArrivalRadius : 20f,
                CoveringSeconds    = 3f,
                RushTimeoutSeconds = authored.RushTimeoutSeconds > 0f ? authored.RushTimeoutSeconds : 12f,
            };
            block.State = default;
        }

        [Behavior(BehaviorNames.FireAndMovement, Params = typeof(FireAndMovementParamsJsonDto), Categories = BehaviorCategory.Commander)]
        [BTreeDefinition(BehaviorNames.FireAndMovement, Curated = true, Group = "Library/Squad", ParamsType = typeof(FireAndMovementBlackboard))]
        public static BTreeBuilder<FireAndMovementBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<FireAndMovementBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Order, bb => bb.State, Run);

        public static NodeStatus Run(ref FireAndMovementParams p, ref SquadPlayState ws, Entity self, EntityRepository world)
            => SquadManeuverRunner.Tick<FireAndMovementPlay, FireAndMovementParams>(ref p, ref ws, self, world);

        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.FireAndMovementBehavior.Run")]
        public static void Deactivate_Run(ref FireAndMovementParams p, ref SquadPlayState ws, Entity self, EntityRepository world)
            => SquadManeuverRunner.DropSensor(ref ws, world);
    }
}
