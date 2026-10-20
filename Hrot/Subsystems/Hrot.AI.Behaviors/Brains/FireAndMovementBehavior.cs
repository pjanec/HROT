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
    public struct FireAndMovementParams
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
        /// <summary>⭐ <c>CE-2158</c> F-d (§7.6.4) — 1 = the moving half rushes by BUDDY PAIRS: its even-ranked men (A) first while their
        /// buddies (B) fire, then B; 0 = the whole half at once.</summary>
        public byte Pairs;
        /// <summary>Runtime (F-d): which buddies of the moving half are up — 0 = A, 1 = B.</summary>
        public byte Buddy;
        /// <summary>Runtime: each mover's cover point for the coming rush (<see cref="SquadCover"/>).</summary>
        public SquadCoverPicks Cover;
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
            p.Cover = default;
            p.Buddy = 0;
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
            if (!FireAndMovementManeuver.IsRush(phase) && !SquadCover.IsReady(in p.Cover, e))
                SquadCover.Make(ref p.Cover, ref ws, in ctx, CoverSite, e, p.Objective, p.RushMetres + 2f,
                                ws.ThreatId != 0 ? new Entity((ulong)ws.ThreatId) : Entity.Null, p.Objective, ws.IssuedAt);
            // ⭐ AQ96 C3 — while the base of fire covers the rush, its movers know it: a man down rises under fire then (UnderFire)
            if (FireAndMovementManeuver.IsRush(phase) && SquadGates.Covering(in ws.Gate, in ctx, SquadGates.Element(in ctx, (byte)(1 - e))))
            {
                ushort movers = SquadGates.Element(in ctx, e);
                for (int i = 0; i < ctx.Roster.Count && i < 16; i++)
                    if ((movers & (1 << i)) != 0 && ctx.Live(i, out var m)) UnderFire.MarkCovered(ctx.World, m, ctx.Now);
            }
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
                ushort up = p.Pairs != 0 ? Buddies(in ctx, e, p.Buddy) : SquadGates.Element(in ctx, e);
                bool set = SquadGates.Set(in ws, in ctx, up, SquadGates.SetMetres);
                if (!set && ctx.Now - ws.IssuedAt < p.RushTimeoutSeconds) return false;
                // ⭐ F-d — the A buddies are down: the B buddies go (a re-issue inside the phase, not a phase change)
                if (p.Pairs != 0 && p.Buddy == 0 && Buddies(in ctx, e, 1) != 0) { p.Buddy = 1; ws.Reissue = 1; return false; }
                if (!set) ws.CompletedByTimeout = 1;
                p.Buddy = 0;
                SquadCover.Spend(ref p.Cover, in ctx, e);   // the rush is over: the next one picks afresh
                return true;
            }
            evt = PhaseEventKind.ShotFired;   // "Covering!"
            if (!SquadCover.IsReady(in p.Cover, e)) return false;
            if (SquadGates.Covering(in ws.Gate, in ctx, SquadGates.Element(in ctx, (byte)(1 - e)))) return true;
            if (ctx.Now - ws.IssuedAt < p.CoveringSeconds) return false;
            ws.CompletedByTimeout = 1;   // the movers go anyway: the covering never came
            return true;
        }

        public void BeginIssue(ref FireAndMovementParams p, ref SquadPlayState ws, in SquadPlayContext ctx) { }

        public MemberOrder OrderFor(ref FireAndMovementParams p, ref SquadPlayState ws, in SquadPlayContext ctx, int member, byte element, Vector3 at)
        {
            ushort phase = ctx.Squad.PhaseId;
            if (element == FireAndMovementManeuver.MovingElement(phase))
            {
                if (!FireAndMovementManeuver.IsRush(phase))
                    return new MemberOrder { Kind = MemberOrderKind.Hold, Speed = p.Speed, RoleId = FireAndMovementManeuver.RoleMoving };
                // ⭐ F-d — the buddy who is not up fires for the one who is
                if (p.Pairs != 0 && (Buddies(in ctx, element, p.Buddy) & (1 << member)) == 0) return Covering(in p, in ws);
                bool cover = SquadCover.Has(in p.Cover, member, out var pick);
                return new MemberOrder   // RushTo
                {
                    Kind = MemberOrderKind.AdvanceTo, Point = cover ? pick : OneRush(ref p, at), Speed = p.Speed,
                    ArrivalRadius = 1f, Cover = cover ? (byte)1 : (byte)0, RoleId = FireAndMovementManeuver.RoleMoving,
                };
            }
            return Covering(in p, in ws);
        }

        private static MemberOrder Covering(in FireAndMovementParams p, in SquadPlayState ws)
        {
            if (ws.ThreatId == 0) return new MemberOrder { Kind = MemberOrderKind.Hold, Speed = p.Speed, RoleId = FireAndMovementManeuver.RoleCovering };
            return new MemberOrder
            {
                Kind = MemberOrderKind.Suppress, Target = new Entity((ulong)ws.ThreatId), Point = ws.ThreatPoint, Speed = p.Speed,
                RoleId = FireAndMovementManeuver.RoleCovering,
            };
        }

        /// <summary>⭐ F-d — the live men of <paramref name="element"/> whose rank in it has parity <paramref name="buddy"/> (0 = A, 1 = B).</summary>
        public static ushort Buddies(in SquadPlayContext ctx, byte element, byte buddy)
        {
            ref readonly var squad = ref ctx.World.GetComponentRO<SquadCognitiveState>(ctx.Commander);
            ushort mask = 0; int rank = 0;
            for (int i = 0; i < ctx.Roster.Count && i < 16; i++)
            {
                if (squad.Elements.MemberElements[i] != element || !ctx.Live(i, out _)) continue;
                if ((rank++ & 1) == buddy) mask |= (ushort)(1 << i);
            }
            return mask;
        }

        public void Release(ref SquadPlayState ws, EntityRepository world) => SquadManeuverRunner.DropSensor(ref ws, world);

        /// <summary>No cover ahead: one rush straight toward the objective.</summary>
        private static Vector3 OneRush(ref FireAndMovementParams p, Vector3 at)
        {
            var to = new Vector2(p.Objective.X - at.X, p.Objective.Y - at.Y);
            float dist = to.Length();
            float step = dist > 1e-3f ? MathF.Min(p.RushMetres, dist) / dist : 0f;
            return new Vector3(at.X + to.X * step, at.Y + to.Y * step, at.Z);
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
                Pairs              = authored.Pairs ? (byte)1 : (byte)0,
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
