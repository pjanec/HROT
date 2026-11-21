using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Maneuvers;
using Fdp.Toolkit.Squad.Primitives;
using Fdp.Toolkit.Utility;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Brains
{
    // ── SquadHold ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>⭐ <c>CE-2123</c> S2 — a squad hold (resolved from <see cref="SquadHoldParamsJsonDto"/>).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SquadHoldParams
    {
        public float Speed;
        /// <summary>Done after this long (s); 0 = never by time.</summary>
        public SimDuration DurationSeconds;
        /// <summary>1 = done once the squad knows no enemy (<see cref="Fdp.Toolkit.Squad.SquadContacts.AnyEnemy"/> — a neutral or a man
        /// out of the fight is none, CE-2353).</summary>
        public byte EndWhenClear;
    }

    /// <summary>⭐ <c>CE-2123</c> S2 — the squad holds where it stands and fights (every member <c>CombatPosture</c> at its position).
    /// 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §4.</summary>
    public static class SquadHoldBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct SquadHoldBlackboard
        {
            public SquadHoldParams Order;
            public SquadPlayState State;
        }

        [BehaviorResolver(BehaviorNames.SquadHold)]
        public static void Resolve(in SquadHoldParamsJsonDto authored, ref SquadHoldBlackboard block, EntityRepository world, Entity self)
        {
            block.Order = new SquadHoldParams
            {
                Speed           = authored.Speed > 0f ? authored.Speed : 3f,
                DurationSeconds = authored.DurationSeconds > 0f ? authored.DurationSeconds : 0f,
                EndWhenClear    = authored.EndWhenClear ? (byte)1 : (byte)0,
            };
            block.State = default;
        }
[Behavior(BehaviorNames.SquadHold, Params = typeof(global::Hrot.Map.Definitions.Behavior.SquadHoldParamsJsonDto), Fulfils = nameof(Hrot.AI.Behaviors.Orders.OrderKind.Hold), Categories = BehaviorCategory.Commander)]   // ⭐ CE-2134

        [BTreeDefinition(BehaviorNames.SquadHold, Curated = true, Group = "Library/Combat", ParamsType = typeof(SquadHoldBlackboard))]
        public static BTreeBuilder<SquadHoldBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<SquadHoldBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Order, bb => bb.State, Run);

        public static NodeStatus Run(ref SquadHoldParams p, ref SquadPlayState ws, Entity self, EntityRepository world)
            => SquadManeuverRunner.Tick<SquadHoldPlay, SquadHoldParams>(ref p, ref ws, self, world);
    }

    /// <summary>⭐ <c>CE-2123</c> S2 — the hold as a play over <see cref="SquadHoldManeuver"/>.</summary>
    public struct SquadHoldPlay : ISquadPlay<SquadHoldParams>
    {
        private static readonly SquadHsmShell s_shell = new(SquadHoldManeuver.BuildTransitionTable(), SquadHoldManeuver.PhaseAborted);

        public SquadHsmShell Shell => s_shell;
        public ushort ManeuverKind => SquadHoldManeuver.ManeuverKind;
        public ushort StartPhase => SquadHoldManeuver.PhaseHolding;
        public ushort AbortPhase => SquadHoldManeuver.PhaseAborted;
        public byte ElementCount => 1;

        public PlayStart Start(ref SquadHoldParams p, ref SquadPlayState ws, in SquadPlayContext ctx, Span<MemberPartitionInput> partition)
        {
            for (int i = 0; i < ctx.Roster.Count; i++) partition[i] = new MemberPartitionInput(1f, 0f);
            return SquadManeuverRunner.AnyLive(in ctx) ? PlayStart.Go : PlayStart.Fail;
        }

        public void Update(ref SquadHoldParams p, ref SquadPlayState ws, in SquadPlayContext ctx) { }

        public NodeStatus Outcome(ref SquadHoldParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            if (!SquadManeuverRunner.AnyLive(in ctx)) return NodeStatus.Failure;
            if (p.DurationSeconds > 0f && ctx.Now - ws.StartedAt >= p.DurationSeconds) return NodeStatus.Success;
            if (p.EndWhenClear != 0 && !SquadContacts.AnyEnemy(ctx.World, in ctx.World.GetComponentRO<SquadCognitiveState>(ctx.Commander).Contacts)) return NodeStatus.Success;   // ⭐ CE-2353
            return NodeStatus.Running;
        }

        public bool PhaseComplete(ref SquadHoldParams p, ref SquadPlayState ws, in SquadPlayContext ctx, out PhaseEventKind evt)
        {
            evt = default;
            return false;
        }

        public void BeginIssue(ref SquadHoldParams p, ref SquadPlayState ws, in SquadPlayContext ctx) { }

        public MemberOrder OrderFor(ref SquadHoldParams p, ref SquadPlayState ws, in SquadPlayContext ctx, int member, byte element, Vector3 at)
            => new MemberOrder { Kind = MemberOrderKind.Hold, Speed = p.Speed, RoleId = SquadHoldManeuver.RoleHolding };

        public void Release(ref SquadPlayState ws, EntityRepository world) { }
    }

    // ── BreakContact ──────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>⭐ <c>CE-2123</c> S2 — a squad's break contact (resolved from <see cref="BreakContactParamsJsonDto"/>).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct BreakContactParams
    {
        public float RetreatSearchRadius;
        public float Speed;
        public float ArrivalRadius;
        public SimDuration BoundTimeoutSeconds;
        /// <summary>⭐ E-3a (DA-68) — the authored destination; <see cref="HasTo"/> 0 = ask the terrain.</summary>
        public Vector3 To;
        public byte HasTo;
        /// <summary>⭐ <c>CE-2278</c> (DA-12) — the smoke to break contact: 0 not yet · 1 thrown (orders re-sent after <see cref="BreakContactPlay.SmokeSeconds"/>)
        /// · 2 done. Set when the first bound is issued.</summary>
        public byte Smoked;
        public SimInstant SmokedAt;
        /// <summary>⭐ <c>CE-2460</c> — the moving element's cover picks for its NEXT bound (fire and movement's own picker, <see cref="SquadCover"/>).</summary>
        public SquadCoverPicks Cover;
        /// <summary>⭐ <c>CE-2465</c> — bit e: element e has ended a bound since the play started (the contact-broken end needs both).</summary>
        public byte BoundsDone;
    }

    /// <summary>
    /// ⭐ <c>CE-2123</c> S2-F — BREAK CONTACT on the commander: it asks the terrain for a point the enemy cannot see (its own
    /// <see cref="FindSafeRetreatPoint"/> query, measured from a member, aimed at the squad's top identified contact); the squad
    /// bounds STRAIGHT to it — one element moves while the other holds and fires, then the other way — until both are there.
    /// No point found ⇒ Failure. 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §4.5.
    /// </summary>
    public static class BreakContactBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct BreakContactBlackboard
        {
            public BreakContactParams Order;
            public SquadPlayState State;
        }

        [BehaviorResolver(BehaviorNames.BreakContact)]
        public static void Resolve(in BreakContactParamsJsonDto authored, ref BreakContactBlackboard block, EntityRepository world, Entity self)
        {
            block.Order = Defaults(authored.RetreatSearchRadius, authored.Speed, authored.ArrivalRadius, authored.BoundTimeoutSeconds);
            block.Order.To = authored.To;
            block.Order.HasTo = (Vector3)authored.To != Vector3.Zero ? (byte)1 : (byte)0;
            block.State = default;
        }

        internal static BreakContactParams Defaults(float radius, float speed, float arrival, float timeout) => new()
        {
            RetreatSearchRadius = radius > 0f ? radius : 60f,
            Speed               = speed > 0f ? speed : 3f,
            ArrivalRadius       = arrival > 0f ? arrival : 8f,
            BoundTimeoutSeconds = timeout > 0f ? timeout : 60f,
        };
[Behavior(BehaviorNames.BreakContact, Params = typeof(global::Hrot.Map.Definitions.Behavior.BreakContactParamsJsonDto), Fulfils = nameof(Hrot.AI.Behaviors.Orders.OrderKind.BreakContact), Categories = BehaviorCategory.Commander)]   // ⭐ CE-2134

        [BTreeDefinition(BehaviorNames.BreakContact, Curated = true, Group = "Library/Reactions", ParamsType = typeof(BreakContactBlackboard))]
        public static BTreeBuilder<BreakContactBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<BreakContactBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Order, bb => bb.State, Run);

        public static NodeStatus Run(ref BreakContactParams p, ref SquadPlayState ws, Entity self, EntityRepository world)
            => SquadManeuverRunner.Tick<BreakContactPlay, BreakContactParams>(ref p, ref ws, self, world);

        /// <summary>Leaving the node: the commander's retreat query goes.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.BreakContactBehavior.Run")]
        public static void Deactivate_Run(ref BreakContactParams p, ref SquadPlayState ws, Entity self, EntityRepository world)
            => SquadManeuverRunner.DropSensor(ref ws, world);
    }

    /// <summary>⭐ <c>CE-2123</c> S2-F — break contact as a play: its own kind, the bounding table's phases.</summary>
    public struct BreakContactPlay : ISquadPlay<BreakContactParams>
    {
        /// <summary>The commander's retreat-query site.</summary>
        public const int RetreatSite = 0x21230002;

        private static readonly SquadHsmShell s_shell = new(
            BreakContactManeuver.BuildTransitionTable(), BoundingOverwatchManeuver.PhaseAborted);

        public SquadHsmShell Shell => s_shell;
        public ushort ManeuverKind => BreakContactManeuver.ManeuverKind;
        public ushort StartPhase => BoundingOverwatchManeuver.PhaseElement0Moving;
        public ushort AbortPhase => BoundingOverwatchManeuver.PhaseAborted;
        public byte ElementCount => 2;

        public PlayStart Start(ref BreakContactParams p, ref SquadPlayState ws, in SquadPlayContext ctx, Span<MemberPartitionInput> partition)
        {
            if (!SquadManeuverRunner.AnyLive(in ctx)) return PlayStart.Fail;
            p.Cover = default;   // ⭐ CE-2460 — no picks from an earlier run
            p.BoundsDone = 0;    // ⭐ CE-2465
            if (p.HasTo != 0)   // ⭐ E-3a (DA-68) — told where to go: that is the answer, no terrain and no contact needed
            {
                ws.Answer = p.To;
                ws.HasAnswer = 1;
                SquadManeuverRunner.SplitLiveByRank(in ctx, partition);   // ⭐ CE-4605 — the live men by rank
                return PlayStart.Go;
            }
            if (!SquadManeuverRunner.TopContact(in ctx, 0, out var contact)) return PlayStart.Fail;   // nothing to break contact with
            ws.ThreatId = contact.EntityId;
            ws.ThreatPoint = new Vector3(contact.PositionX, contact.PositionY, contact.PositionZ);
            SquadManeuverRunner.SplitLiveByRank(in ctx, partition);   // ⭐ CE-4605 — the live men by rank
            return PlayStart.Go;
        }

        public void Update(ref BreakContactParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            // ⭐ CE-2278 — the thrower's FireAtPoint ends after the throw: once it has had time, every order is sent again (his is a hold now)
            if (p.Smoked == 1 && ctx.Now - p.SmokedAt >= SmokeSeconds) { p.Smoked = 2; ws.Reissue = 1; }
            if (ws.HasAnswer == 0)
                SquadManeuverRunner.AskTerrain(ref ws, in ctx, FindSafeRetreatPoint.BlueprintId, RetreatSite, p.RetreatSearchRadius,
                    SquadManeuverRunner.FirstLive(in ctx), new Entity((ulong)ws.ThreatId));
            // ⭐⭐ CE-2460 (U1) — the answer known and a threat to hide from: the moving element's NEXT bound is cover to cover toward the answer,
            //   ≤ BoundMetres, by fire and movement's own picker (one query, the sensor dropped after it answers). The movers hold until the picks
            //   are made (≤ SquadCover.PickTimeoutSeconds); a mover with no cover ahead keeps the straight move to the answer (walkable — S2-F)
            else if (ws.HasAnswer == 1 && ws.ThreatId != 0)
            {
                byte e = Moving(in ctx);
                if (!SquadCover.IsReady(in p.Cover, e))
                {
                    SquadCover.Make(ref p.Cover, ref ws, in ctx, CoverSite, e, ws.Answer, BoundMetres + 2f,
                                    new Entity((ulong)ws.ThreatId), ws.ThreatPoint, ws.IssuedAt);
                    if (SquadCover.IsReady(in p.Cover, e)) ws.Reissue = 1;   // the picks are made: the movers go
                }
            }
        }

        /// <summary>⭐ <c>CE-2460</c> — a bound is at most this long (m): fire and movement's rush (RushMetres 15), the doctrine's "short, cover to cover".</summary>
        public const float BoundMetres = 15f;
        /// <summary>⭐ <c>CE-2460</c> — the commander's cover-pick query site for the bounds (beside <see cref="RetreatSite"/>).</summary>
        public const int CoverSite = 0x21230003;

        private static byte Moving(in SquadPlayContext ctx)
            => ctx.Squad.PhaseId == BoundingOverwatchManeuver.PhaseElement0Moving ? (byte)0 : (byte)1;

        /// <summary>⭐ <c>CE-2460</c> — the moving element waits for its cover picks (a threat known): never while there is nothing to hide from.</summary>
        private static bool WaitingForPicks(in BreakContactParams p, in SquadPlayState ws, in SquadPlayContext ctx)
            => ws.HasAnswer == 1 && ws.ThreatId != 0 && !SquadCover.IsReady(in p.Cover, Moving(in ctx));

        public NodeStatus Outcome(ref BreakContactParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            if (!SquadManeuverRunner.AnyLive(in ctx) || ws.HasAnswer == SquadManeuverRunner.NoAnswer) return NodeStatus.Failure;
            if (ws.HasAnswer != 1) return NodeStatus.Running;
            // ⭐⭐ CE-2465 (U2-4) — the contact IS broken: each element has bounded once and the squad is in a lull (no fire at it, no new contact
            //   for LullSeconds) ⇒ done, even with an element short of the point. 📐 uf-corner-fight: a mover whose cover picks never moved him
            //   kept his element's centre 30 m from the answer and the squad stood 280 s. ⛔ Not for an ordered To with no threat (go there).
            if (ws.ThreatId != 0 && (p.BoundsDone & 3) == 3 && SquadSopConditions.InALull(ctx.World, ctx.Commander)) return NodeStatus.Success;
            for (byte e = 0; e < 2; e++)
            {
                var c = ctx.Centre(e, out bool any);
                if (any && Flat(c - ws.Answer) > p.ArrivalRadius) return NodeStatus.Running;
            }
            return NodeStatus.Success;
        }

        public bool PhaseComplete(ref BreakContactParams p, ref SquadPlayState ws, in SquadPlayContext ctx, out PhaseEventKind evt)
        {
            evt = PhaseEventKind.BoundComplete;
            if (ws.HasAnswer != 1 || ws.Reissue != 0) return false;
            byte moving = Moving(in ctx);
            if (WaitingForPicks(in p, in ws, in ctx)) return false;   // ⭐ CE-2460 — the movers hold on the spot meanwhile: that is no bound
            // ⭐ CE-2461 — or the movers all stopped short (bounding's rule, one helper): 📐 uf-corner-fight, the element stood 60 s with no task
            bool done = ctx.Now - ws.IssuedAt >= p.BoundTimeoutSeconds || SquadManeuverRunner.ElementArrived(in ws, in ctx, moving)
                        || SquadGates.MoversStopped(in ctx, SquadGates.Element(in ctx, moving), ws.IssuedAt);
            if (done) { SquadCover.Spend(ref p.Cover, in ctx, moving); p.BoundsDone |= (byte)(1 << moving); }   // the next bound picks afresh
            return done;
        }

        public void BeginIssue(ref BreakContactParams p, ref SquadPlayState ws, in SquadPlayContext ctx) { }

        public MemberOrder OrderFor(ref BreakContactParams p, ref SquadPlayState ws, in SquadPlayContext ctx, int member, byte element, Vector3 at)
        {
            byte moving = ctx.Squad.PhaseId == BoundingOverwatchManeuver.PhaseElement0Moving ? (byte)0 : (byte)1;
            // ⭐⭐ CE-2278 (DA-12, Defend & Attack §8 DA-G) — SMOKE TO BREAK CONTACT: as the first bound goes, ONE covering man who carries smoke
            //   (his TYPE's loadout) and is at least Regular throws it between the squad and the threat; once (2 grenades a man, not one a bound)
            if (p.Smoked == 0 && ws.HasAnswer == 1 && element != moving && ws.ThreatId != 0 && Smoker(in ctx, member, out var man))
            {
                p.Smoked = 1;
                p.SmokedAt = (SimInstant)ctx.Now;
                return new MemberOrder
                {
                    Kind = MemberOrderKind.FireAtPoint, Point = SmokePoint(at, ws.ThreatPoint), Rounds = 1, Round = Fdp.Toolkit.Combat.Executors.FireAtPointParams.RoundSmoke,
                    Speed = p.Speed, RoleId = BoundingOverwatchManeuver.RoleCovering,
                };
            }
            if (ws.HasAnswer != 1 || element != moving)
                return new MemberOrder { Kind = MemberOrderKind.Hold, Speed = p.Speed, RoleId = BoundingOverwatchManeuver.RoleCovering };
            if (WaitingForPicks(in p, in ws, in ctx))   // ⭐ CE-2460 — a moment's hold while the cover picks are made
                return new MemberOrder { Kind = MemberOrderKind.Hold, Speed = p.Speed, RoleId = BoundingOverwatchManeuver.RoleMoving };
            // ⭐ CE-2460 — cover to cover; with no cover ahead ONE rush (BoundMetres) toward the answer, never the whole way at once (📐 U1, live:
            //   the first element found no pick and ran ~55 m across the street). No threat (an ordered To) ⇒ straight to the answer (S2-F)
            bool cover = SquadCover.Has(in p.Cover, member, out var pick);
            var point = cover ? pick : ws.ThreatId != 0 ? SquadCover.OneRush(at, ws.Answer, BoundMetres) : ws.Answer;
            return new MemberOrder
            {
                Kind = MemberOrderKind.AdvanceTo, Point = point, Speed = p.Speed, Cover = cover ? (byte)1 : (byte)0,
                ArrivalRadius = SquadManeuverRunner.MoverArrivalMetres, RoleId = BoundingOverwatchManeuver.RoleMoving,
            };
        }

        public void Release(ref SquadPlayState ws, EntityRepository world) => SquadManeuverRunner.DropSensor(ref ws, world);

        private static float Flat(Vector3 d) { d.Z = 0f; return d.Length(); }

        /// <summary>⭐ CE-2278 — seconds a thrown smoke order is given before the squad's orders are re-sent (the throw takes ≈ 1–2 s).</summary>
        public const float SmokeSeconds = 3f;
        /// <summary>⭐ CE-2278 — the smoke lands this far toward the threat (m), at most half-way: between the squad and the enemy, not on him.</summary>
        public const float SmokeMetres = 15f;

        /// <summary>The point <see cref="SmokeMetres"/> from the thrower toward the threat (at most half-way), on his ground.</summary>
        internal static Vector3 SmokePoint(Vector3 from, Vector3 threat)
        {
            var d = new Vector2(threat.X - from.X, threat.Y - from.Y);
            float len = d.Length();
            if (len < 1e-3f) return from;
            float k = MathF.Min(SmokeMetres, len * 0.5f) / len;
            return new Vector3(from.X + d.X * k, from.Y + d.Y * k, from.Z);
        }

        /// <summary>Member <paramref name="member"/> can throw the smoke: live, his type carries it, at least Regular (DA-12 "Regular+").</summary>
        internal static bool Smoker(in SquadPlayContext ctx, int member, out Entity man)   // ⭐ CE-2436 AU-2 — FireAndMovement's crossing smoke too
            => ctx.Live(member, out man)
               && Fdp.Toolkit.Combat.AmmoChoice.TypeCarries(ctx.World, man, Fdp.Toolkit.Tkb.Domain.WarheadKind.Smoke)
               && Fdp.Toolkit.Behavior.Components.ProficiencyOf.Tier(ctx.World, man) >= Fdp.Toolkit.Behavior.Components.ProficiencyTier.Regular;
    }

    // ── ReactToContact ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>⭐ <c>CE-2123</c> S2 — the reaction's params (resolved from <see cref="ReactToContactParamsJsonDto"/>).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ReactToContactParams
    {
        /// <summary>Phase 1: every member holds and returns fire for <see cref="SquadHoldParams.DurationSeconds"/>.</summary>
        public SquadHoldParams ReturnFire;
        /// <summary>The choice (<see cref="SquadContactResponseDecision"/>).</summary>
        public ChooseOptionParams Choose;
        public SuppressAndManeuverParams Assault;
        /// <summary>⭐ <c>CE-2158</c> F-e (§7.6.3) — the Assault's CLOSING stage: a contact farther than <see cref="ReactToContactBehavior.CloseMetres"/>
        /// + one rush is closed on by fire and movement first; the flank and the assault through start from there.</summary>
        public FireAndMovementParams Close;
        public BreakContactParams Break;
        /// <summary>The fallback and the Hold choice: hold until the squad knows no contact.</summary>
        public SquadHoldParams HoldOn;
        /// <summary>⭐ <c>CE-2152</c> C-c (R-d) — the Rescue choice: the casualty-care play.</summary>
        public CasualtyCareParams Care;
        /// <summary>⭐ <c>CE-2466</c> R2 (Squad Tactics §7.9) — the rally hold after a contact with an enemy still there.</summary>
        public SquadHoldParams Rally;
        /// <summary>⭐ <c>CE-2466</c> R5 — the hold of a squad no longer fit to fight (no end: its superior re-tasks it).</summary>
        public SquadHoldParams Unfit;
    }

    /// <summary>⭐ <c>CE-2123</c> S2 — where the reaction is: one play at a time, so one play state.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ReactToContactState
    {
        /// <summary>0 = returning fire · 1 = the chosen drill · 2 = holding until clear.</summary>
        public byte Stage;
        /// <summary>⭐ CE-2125 — sim time the current stage began (a leader lost since then sends the reaction back to decide again).</summary>
        public SimInstant StageSince;
        public UtilityChoice Choice;
        public SquadPlayState Play;
        /// <summary>⭐ <c>CE-2152</c> C-c (R-d) — sim time the choice was last scored (the re-decide clock while a man is down).</summary>
        public SimInstant DecidedAt;
        /// <summary>⭐ <c>CE-2158</c> §7.6.3 — the Assault's stage: 0 = not decided · 1 = closing (fire and movement) · 2 = flank and assault.</summary>
        public byte AssaultStage;
        /// <summary>⭐ <c>CE-2466</c> R1 (S6) — the most enemy danger the pool showed during the reaction (what is remembered).</summary>
        public float PeakEnemy;
        /// <summary>⭐ <c>CE-2466</c> R1 — 1 once the positions were learned at the decision to break contact (or hold).</summary>
        public byte Learned;
        /// <summary>⭐ <c>CE-2466</c> — 1 when the reaction ended by pressing on (CE-2454): no rally.</summary>
        public byte NoRally;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2123</c> S2 — the squad leader's REACTION TO CONTACT, started by its standing orders (<c>BasicSquadSop</c>) on a
    /// squad edge: every member holds and returns fire for a moment; the leader then decides ONCE (<see cref="UtilityNodes.ChooseOptionOnce"/>
    /// over <see cref="SquadContactResponseDecision"/>) — assault (the S1 suppress-and-manoeuvre), break contact, or hold — and runs it;
    /// a drill that cannot start or fails falls back to holding until clear. When it ends, the task it paused resumes.
    /// ⭐ One stateful action stepping through the stages (one play at a time on the S1 runner), not nested composites.
    /// 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §4.3, §4.5.
    /// </summary>
    public static class ReactToContactBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct ReactToContactBlackboard
        {
            public ReactToContactParams Order;
            public ReactToContactState State;
        }

        [BehaviorResolver(BehaviorNames.ReactToContact)]
        public static void Resolve(in ReactToContactParamsJsonDto authored, ref ReactToContactBlackboard block, EntityRepository world, Entity self)
        {
            float speed   = authored.Speed > 0f ? authored.Speed : 3f;
            float arrival = authored.ArrivalRadius > 0f ? authored.ArrivalRadius : 8f;
            float timeout = authored.PhaseTimeoutSeconds > 0f ? authored.PhaseTimeoutSeconds : 90f;
            block.Order = new ReactToContactParams
            {
                ReturnFire = new SquadHoldParams { Speed = speed, DurationSeconds = authored.DecideAfterSeconds > 0f ? authored.DecideAfterSeconds : 3f },
                Choose     = new ChooseOptionParams { Decision = UtilityDecisionRef.FromAssetId(SquadContactResponseDecision.DecisionAssetId) },
                Assault    = new SuppressAndManeuverParams
                {
                    FlankSearchRadius = authored.FlankSearchRadius > 0f ? authored.FlankSearchRadius : 40f,
                    Speed = speed, ArrivalRadius = arrival, PhaseTimeoutSeconds = timeout,
                },
                Close      = new FireAndMovementParams
                {
                    RushMetres = 15f, Speed = MathF.Max(speed, 4f), ArrivalRadius = CloseMetres, CoveringSeconds = 3f, RushTimeoutSeconds = 12f,
                },
                Break      = BreakContactBehavior.Defaults(authored.RetreatSearchRadius, speed, arrival, timeout),
                HoldOn     = new SquadHoldParams { Speed = speed, EndWhenClear = 1 },
                Care       = CasualtyCareBehavior.Defaults(speed),
            };
            block.Order.Care.EvacuateMetres = Math.Max(0f, authored.EvacuateMetres);   // ⭐ CE-7009
            block.Order.Rally = new SquadHoldParams { Speed = speed, DurationSeconds = AfterContact.RallySeconds };   // ⭐ CE-2466 R2
            block.Order.Unfit = new SquadHoldParams { Speed = speed };                                                 // ⭐ CE-2466 R5
            block.State = default;
        }
[Behavior(BehaviorNames.ReactToContact, Conduct = Fdp.Toolkit.Behavior.Conduct.Halts | Fdp.Toolkit.Behavior.Conduct.SeeksCover,   /* CE-4621 M-3 (CD-1) */ Params = typeof(global::Hrot.Map.Definitions.Behavior.ReactToContactParamsJsonDto), Categories = BehaviorCategory.Commander)]   // ⭐ CE-2134

        [BTreeDefinition(BehaviorNames.ReactToContact, Curated = true, Group = "Library/Reactions", ParamsType = typeof(ReactToContactBlackboard))]
        public static BTreeBuilder<ReactToContactBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<ReactToContactBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Order, bb => bb.State, Run);

        public const byte StageReturnFire = 0, StageDrill = 1, StageHoldOn = 2;
        /// <summary>⭐ <c>CE-2466</c> R2 — the rally after a contact with an enemy still there · R5 — the hold of a squad no longer fit to fight.</summary>
        public const byte StageRally = 3, StageUnfit = 4;


        /// <summary>⭐ <c>CE-2158</c> §7.6.3 — the assault distance: the squad closes by fire and movement until its halves are this near
        /// the contact (m); a contact already within this + one rush is flanked at once.</summary>
        public const float CloseMetres = 50f;

        public const byte AssaultUndecided = 0, AssaultClosing = 1, AssaultFlanking = 2;

        /// <summary>
        /// ⭐ <c>CE-2351</c> (user, `2026-10-22`, option 1) — a squad ORDERED TO HOLD does not break contact on its own: when the task this
        /// reaction paused is <c>SquadDefend</c> or <c>SquadHold</c>, a BreakContact winner holds instead (withdrawal from a held position
        /// is the superior's call — the platoon's <c>FallBack</c>, CE-2276). 📐 <c>ua-casualty-care-heavy</c>: three hostiles in the pool,
        /// BreakContact won (0.63 vs Assault 0.37) and the drill dragged the wounded away inside the 30 s the demo holds under fire.
        /// </summary>
        /// <remarks>⭐⭐ <c>CE-4612</c> / <c>CE-4609</c> (R-2088) — the squad's ORDER, typed: a Defend or a Hold, by any behaviour that fulfils it
        /// (<c>SquadDefend</c>, <c>SquadHold</c>, the routed <c>InfantrySquadHold</c> — which the two names missed: a squad given <c>Order.Hold</c>
        /// broke contact).</remarks>
        /// <remarks>⭐ <c>CE-4621</c> M-3 (CD-2, R-4263) — folded into the ONE order-conduct table: an order that forbids leaving the route or
        /// position holds firm (Defend, Hold — and a Dash).</remarks>
        public static bool HoldsFirm(EntityRepository world, Entity self)
            => (Hrot.AI.Behaviors.Orders.OrderConduct.Forbids(Hrot.AI.Behaviors.Orders.CurrentOrders.Of(world, self).Kind)
                & Fdp.Toolkit.Behavior.Conduct.LeavesRoute) != 0;

        private static void Firm(ref UtilityChoice choice, Entity self, EntityRepository world)
        {
            // ⭐⭐ CE-2466 R4 (Squad Tactics §7.9, S7) — never a frontal assault on a force the side broke contact from (a fresh Direct record at a
            //   contact) without 3 : 1: break contact again (a hold would leave the squad where the corner's fire caught it). A lone man on an
            //   attacking order keeps CE-2454's own branch (PressesOn).
            if ((SquadContactResponse)choice.Winner == SquadContactResponse.Assault && !PressesOn(world, self)
                && AfterContact.FacesRemembered(world, self, out float remembered) && !AfterContact.Superior(world, self, remembered))
                choice.Winner = (byte)SquadContactResponse.BreakContact;
            if ((SquadContactResponse)choice.Winner == SquadContactResponse.BreakContact && HoldsFirm(world, self))
                choice.Winner = (byte)SquadContactResponse.Hold;
        }

        /// <summary>
        /// ⭐ <c>CE-2454</c> (Aggregate Squad D-5, §5 ReactToContact) — the squad has ONE man in the fight and the order the reaction paused
        /// ATTACKS (Attack, ClearBuilding, Bound, FireAndMovement): an assault it cannot start ends the reaction and that order resumes. A squad
        /// of two or more, or a lone man on any other order, holds until clear as before. No allocation.
        /// </summary>
        public static bool PressesOn(EntityRepository world, Entity self)
        {
            var kind = Hrot.AI.Behaviors.Orders.CurrentOrders.Of(world, self).Kind;
            if (kind is not (Hrot.AI.Behaviors.Orders.OrderKind.Attack or Hrot.AI.Behaviors.Orders.OrderKind.ClearBuilding
                         or Hrot.AI.Behaviors.Orders.OrderKind.Bound or Hrot.AI.Behaviors.Orders.OrderKind.FireAndMovement)) return false;
            if (!world.HasComponent<Fdp.Core.CommandHierarchy.UnitRoster>(self)) return false;
            ref readonly var roster = ref world.GetComponentRO<Fdp.Core.CommandHierarchy.UnitRoster>(self);
            int fighting = 0;
            for (int i = 0; i < roster.Count; i++)
                if (PlatoonConvoyBehavior.AnyInTheFight(world, roster.SubordinateEntities[i]) && ++fighting > 1) return false;
            return fighting == 1;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2467</c> (Squad Tactics §7.10) — the squad is RUNNING an ordered attack (<see cref="Hrot.AI.Behaviors.Orders.OrderKind.Attack"/>, not paused)
        /// with two or more men in the fight and still 3 : 1 over its contacts (<see cref="AfterContact.Superior"/>, R4's test): its attack drill IS the answer
        /// to the contact, so the contact reaction does not pause it, and a casualty waits until the assault is through (doctrine: the attack continues; the
        /// wounded are treated on the objective). Superiority lost ⇒ false ⇒ the reaction decides again (break contact, hold — R4, R5). 📐 uf-corner-assault:
        /// 6 v 2 broke contact four times and cycled care while three men bled out. No allocation.
        /// </summary>
        public static bool PressesTheAttack(EntityRepository world, Entity self)
        {
            var order = Hrot.AI.Behaviors.Orders.CurrentOrders.Of(world, self);
            return order.Kind == Hrot.AI.Behaviors.Orders.OrderKind.Attack && !order.Paused
                && AfterContact.LiveMembers(world, self) >= 2 && AfterContact.Superior(world, self, 0f);
        }

        /// <summary>The care row's guard: <see cref="PressesTheAttack"/> false.</summary>
        public static bool NotPressingTheAttack(Entity self, EntityRepository world) => !PressesTheAttack(world, self);

        /// <summary>⭐ <c>CE-2152</c> C-c (R-2013) — while a man is down and not yet cared for, the choice is scored again this often (s).</summary>
        public const double RedecideSeconds = 5d;

        /// <summary>
        /// ⭐⭐ <c>CE-2466</c> (R-2140, Squad Tactics §7.9) — the reaction, then what follows it: the fight (<see cref="RunFight"/>), and when it ends
        /// with an enemy still there — R1 the positions learned for the side, R2 the rally (to the hidden point, a hold, LACE + SPOT), R5 a squad
        /// no longer fit to fight stays out (a request and TaskFailed). R1 also learns at the decision to break contact or hold (S4).
        /// </summary>
        public static NodeStatus Run(ref ReactToContactParams p, ref ReactToContactState ws, Entity self, EntityRepository world)
        {
            float danger = Fdp.Toolkit.Utility.SquadInputs.EnemyDangerOf(world, self);
            if (danger > ws.PeakEnemy) ws.PeakEnemy = danger;
            if (ws.Stage == StageRally) return Rally(ref p, ref ws, self, world);
            if (ws.Stage == StageUnfit)
            {
                var hold = SquadManeuverRunner.Tick<SquadHoldPlay, SquadHoldParams>(ref p.Unfit, ref ws.Play, self, world);
                return hold == NodeStatus.Failure ? NodeStatus.Failure : NodeStatus.Running;   // out of it until re-tasked; nobody left ⇒ over
            }
            var fight = RunFight(ref p, ref ws, self, world);
            if (ws.Learned == 0 && ws.Stage is StageDrill or StageHoldOn
                && (SquadContactResponse)ws.Choice.Winner is SquadContactResponse.BreakContact or SquadContactResponse.Hold
                && AfterContact.Learn(world, self, ws.PeakEnemy) > 0)
                ws.Learned = 1;
            if (fight != NodeStatus.Success || ws.NoRally != 0) return fight;
            if (ws.PeakEnemy <= 0f || !AfterContact.EnemyAlive(world, self)) return fight;   // the enemy is gone: nothing to remember, no rally
            AfterContact.Learn(world, self, ws.PeakEnemy);
            // ⭐ CE-2466 R2 — the RALLY is the drill after a BROKEN contact (§7.9.2): a squad that held, assaulted or defended its post goes back to
            //   its task at once (R1's learning still applies). 📐 ia-area-defend: the guard's post rallied 30 s after the first attacker left and
            //   met the second one late.
            bool broke = (SquadContactResponse)ws.Choice.Winner == SquadContactResponse.BreakContact && ws.Play.HasAnswer == 1;
            if (!broke) return fight;
            // ⭐ CE-2466 R2 — a LONE man has nobody to account for and nothing to redistribute: he reports and goes on after a short pause
            //   (📐 ia-area-defend: a lone attacker's full rally pushed his second approach into the window of the next attacker's)
            bool lone = AfterContact.LiveMembers(world, self) <= 1;
            AfterContact.ReportRally(world, self);
            if (lone && AfterContact.LoneRallySeconds <= 0f) return fight;
            Next(ref ws, StageRally, world);
            p.Rally.DurationSeconds = lone ? AfterContact.LoneRallySeconds : AfterContact.RallySeconds;
            return NodeStatus.Running;
        }

        /// <summary>⭐ <c>CE-2466</c> R2 + R5 — hold <see cref="AfterContact.RallySeconds"/> where the squad stands (the break contact ended out of contact, CE-2465),
        /// then fit ⇒ the reaction ends (the task resumes, its route priced by what was learned) · unfit ⇒ a Reinforce request, TaskFailed, and it stays out.
        /// ⛔ SUPERSEDED (S9's "both elements to the hidden point"): an ordered move there walks straight with no threat to hide from — 📐
        /// ua-squad-danger-crossing-hold: it took the Leader across the road the enemy held (a knock-out of the move alone turned it green).</summary>
        private static NodeStatus Rally(ref ReactToContactParams p, ref ReactToContactState ws, Entity self, EntityRepository world)
        {
            var hold = SquadManeuverRunner.Tick<SquadHoldPlay, SquadHoldParams>(ref p.Rally, ref ws.Play, self, world);
            if (hold == NodeStatus.Running) return NodeStatus.Running;
            if (hold == NodeStatus.Failure) return NodeStatus.Failure;
            if (AfterContact.Fit(world, self)) return NodeStatus.Success;
            AfterContact.ReportCannotGoOn(world, self, Fdp.Toolkit.Squad.Command.RequestKind.Reinforce);   // R5
            Next(ref ws, StageUnfit, world);
            return NodeStatus.Running;
        }

        private static NodeStatus RunFight(ref ReactToContactParams p, ref ReactToContactState ws, Entity self, EntityRepository world)
        {
            double now = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : 0d;
            // ⭐ CE-2152 C-c (R-2013) — a man down with NOTHING to fight (the squad knows no contact): no decision to make — bring him in.
            if (ws.Stage == StageReturnFire && SquadCasualties.Count(world, self) > 0 && NothingToFight(world, self))
            {
                Next(ref ws, StageDrill, world);
                ws.Choice = new UtilityChoice { Winner = (byte)SquadContactResponse.Rescue };
                ws.DecidedAt = (SimInstant)now;
                FreshCare(ref p);
            }
            // ⭐ CE-2125 (§7.2 L3) — the leader fell during the drill or the hold: the weaker squad holds and decides again.
            if (ws.Stage != StageReturnFire
                && Fdp.Toolkit.Behavior.Components.RecentSensesOf.WithinSince(world, self, Fdp.Toolkit.Perception.Events.SensorChange.SquadLeaderDown, 3600d, ws.StageSince))
            {
                Next(ref ws, StageReturnFire, world);
                ws.Choice = default;
            }
            if (ws.Stage == StageReturnFire)
            {
                var first = SquadManeuverRunner.Tick<SquadHoldPlay, SquadHoldParams>(ref p.ReturnFire, ref ws.Play, self, world);
                if (first != NodeStatus.Success) return first;            // Running, or nobody left to fight
                Next(ref ws, StageDrill, world);
            }
            // ⭐ CE-2152 C-c (R-2013) — RE-DECIDE while a man is down and not yet cared for: every RedecideSeconds and on each new
            //   SquadMemberDown. A changed winner starts its drill; the same winner keeps running (a drill is never restarted for nothing).
            if (ws.Stage != StageReturnFire && (SquadContactResponse)ws.Choice.Winner != SquadContactResponse.Rescue
                && SquadCasualties.Count(world, self) > 0
                && (now - ws.DecidedAt >= RedecideSeconds
                    || NothingToFight(world, self)   // the enemy is gone: the hold would end "clear" and leave him — decide now
                    || SquadSopConditions.InALull(world, self)   // ⭐ CE-2344 (R-2066 E) — nobody fires at the squad: bring him in now
                    || Fdp.Toolkit.Behavior.Components.RecentSensesOf.WithinSince(world, self, Fdp.Toolkit.Perception.Events.SensorChange.SquadMemberDown, 3600d, ws.DecidedAt)))
            {
                byte was = ws.Choice.Winner;
                var fresh = new UtilityChoice();
                if (NothingToFight(world, self) || SquadSopConditions.InALull(world, self)) fresh.Winner = (byte)SquadContactResponse.Rescue;   // nothing to fight, or a lull: bring him in
                else UtilityNodes.ChooseOptionOnce(ref p.Choose, ref fresh, self, world);
                Firm(ref fresh, self, world);
                ws.DecidedAt = (SimInstant)now;
                if (fresh.Winner != 0 && fresh.Winner != was)
                {
                    Next(ref ws, StageDrill, world);
                    ws.Choice = fresh;
                    if ((SquadContactResponse)fresh.Winner == SquadContactResponse.Rescue) FreshCare(ref p);
                }
            }
            if (ws.Stage == StageDrill)
            {
                if (ws.Choice.Winner == 0)
                {
                    if (NothingToFight(world, self) && SquadCasualties.Count(world, self) > 0) ws.Choice.Winner = (byte)SquadContactResponse.Rescue;
                    else UtilityNodes.ChooseOptionOnce(ref p.Choose, ref ws.Choice, self, world);
                    Firm(ref ws.Choice, self, world);
                    ws.DecidedAt = (SimInstant)now;
                    if ((SquadContactResponse)ws.Choice.Winner == SquadContactResponse.Rescue) FreshCare(ref p);
                }
                var drill = (SquadContactResponse)ws.Choice.Winner switch
                {
                    SquadContactResponse.Assault      => Assault(ref p, ref ws, self, world),
                    SquadContactResponse.BreakContact => SquadManeuverRunner.Tick<BreakContactPlay, BreakContactParams>(ref p.Break, ref ws.Play, self, world),
                    SquadContactResponse.Rescue       => SquadManeuverRunner.Tick<SquadCasualtyCarePlay, CasualtyCareParams>(ref p.Care, ref ws.Play, self, world),
                    _                                 => NodeStatus.Failure,   // Hold, or nothing scored: hold until clear
                };
                if ((SquadContactResponse)ws.Choice.Winner == SquadContactResponse.Rescue && drill != NodeStatus.Running)
                {
                    // ⭐ the man is in (or the care failed): decide again — the squad is still in the fight, or the reaction ends.
                    Next(ref ws, StageDrill, world);
                    ws.Choice = default;
                    if (NothingToFight(world, self) && SquadCasualties.Count(world, self) == 0) return NodeStatus.Success;
                    return NodeStatus.Running;
                }
                if (drill == NodeStatus.Success)
                {
                    // ⭐ CE-2152 R-e — the drill is won with a man still down: the reaction does not end and leave him.
                    if (SquadCasualties.Count(world, self) == 0) return NodeStatus.Success;
                    Next(ref ws, StageDrill, world);
                    ws.Choice = new UtilityChoice { Winner = (byte)SquadContactResponse.Rescue };
                    ws.DecidedAt = (SimInstant)now;
                    FreshCare(ref p);
                    return NodeStatus.Running;
                }
                if (drill == NodeStatus.Running && ws.Play.Started != 0) return NodeStatus.Running;
                // ⭐ CE-2454 — a LONE man cannot fix and flank (Aggregate Squad §5: the flank fails live < 2): on an attacking order he presses
                //   on with it — the order's own posture fights, the room drill answers a contact in the building — instead of standing in the
                //   open until the contact is forgotten (📐 live ia-area-defend: 46 s, 20 m short of House A, the guard unseen).
                if ((SquadContactResponse)ws.Choice.Winner == SquadContactResponse.Assault && PressesOn(world, self)) { ws.NoRally = 1; return NodeStatus.Success; }
                Next(ref ws, StageHoldOn, world);                          // failed, or could not start (e.g. only a heard contact)
            }
            return SquadManeuverRunner.Tick<SquadHoldPlay, SquadHoldParams>(ref p.HoldOn, ref ws.Play, self, world);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2158</c> F-e (📄 DESIGN_Squad_Tactics.md §7.6.3) — the Assault: a FAR contact is first CLOSED on by fire and movement
        /// (halves, cover rushes, toward where the contact stands) until both halves are within <see cref="CloseMetres"/>; then — or at once
        /// when the contact is already near — suppress and manoeuvre flanks and assaults through. The closing ending in any way (won,
        /// a half gone, could not start) hands over to the flank: the assault is what was decided.
        /// </summary>
        private static NodeStatus Assault(ref ReactToContactParams p, ref ReactToContactState ws, Entity self, EntityRepository world)
        {
            if (ws.AssaultStage == AssaultUndecided)
            {
                ws.AssaultStage = AssaultFlanking;
                if (FarContact(world, self, CloseMetres + p.Close.RushMetres, out var at))
                {
                    ws.AssaultStage = AssaultClosing;
                    p.Close.Objective = at;
                    p.Close.Cover = default;
                }
            }
            if (ws.AssaultStage == AssaultClosing)
            {
                var close = SquadManeuverRunner.Tick<FireAndMovementPlay, FireAndMovementParams>(ref p.Close, ref ws.Play, self, world);
                // ⭐ closed when MOST of the squad is near — 📐 live (tt-react-contact): one man who cannot move (the leader soldier on
                //   the z 9 spot) held his half's centre 60 m out, so fire and movement never succeeded and the flank never came
                if (close == NodeStatus.Running && !MostWithin(world, self, p.Close.Objective, CloseMetres)) return NodeStatus.Running;
                SquadManeuverRunner.DropSensor(ref ws.Play, world);   // closed (or could not close): the flank starts from here
                ws.Play = default;
                ws.AssaultStage = AssaultFlanking;
            }
            return SquadManeuverRunner.Tick<SuppressAndManeuverPlay, SuppressAndManeuverParams>(ref p.Assault, ref ws.Play, self, world);
        }

        /// <summary>More than half of the squad's live members stand within <paramref name="metres"/> of <paramref name="at"/>.</summary>
        private static bool MostWithin(EntityRepository world, Entity self, Vector3 at, float metres)
        {
            if (!world.HasComponent<Fdp.Core.CommandHierarchy.UnitRoster>(self)) return false;
            var roster = world.GetComponentRO<Fdp.Core.CommandHierarchy.UnitRoster>(self);
            var ctx = new SquadPlayContext(world, self, 0d, in roster);
            int live = 0, near = 0;
            for (int i = 0; i < roster.Count; i++)
            {
                if (!ctx.Live(i, out var m)) continue;
                live++;
                var d = ctx.At(m) - at; d.Z = 0f;
                if (d.LengthSquared() <= metres * metres) near++;
            }
            return live > 0 && near * 2 > live;
        }

        /// <summary>The squad's top identified live contact is farther than <paramref name="metres"/> from the live members' centre.</summary>
        private static bool FarContact(EntityRepository world, Entity self, float metres, out Vector3 at)
        {
            at = default;
            if (!world.HasComponent<Fdp.Core.CommandHierarchy.UnitRoster>(self) || !world.HasComponent<SquadCognitiveState>(self)) return false;
            var roster = world.GetComponentRO<Fdp.Core.CommandHierarchy.UnitRoster>(self);
            var ctx = new SquadPlayContext(world, self, 0d, in roster);
            if (!SquadManeuverRunner.TopContact(in ctx, 0, out var contact, liveOnly: true)) return false;
            var sum = Vector3.Zero; int n = 0;
            for (int i = 0; i < roster.Count; i++) if (ctx.Live(i, out var m)) { sum += ctx.At(m); n++; }
            if (n == 0) return false;
            at = new Vector3(contact.PositionX, contact.PositionY, contact.PositionZ);
            var d = at - sum / n; d.Z = 0f;
            return d.LengthSquared() > metres * metres;
        }

        /// <summary>
        /// ⭐ <c>CE-2152</c> R-e — nothing left to fight: the squad's pool holds no contact that is still alive. 📐 Measured live
        /// (<c>ua-casualty-care-heavy</c>): the pool is rebuilt from the members' memories, which keep a killed enemy until they fade,
        /// so "the pool is empty" came long after the last enemy fell. A heard contact (no entity) counts as alive.
        /// </summary>
        /// <summary>⭐ R-e — no pooled enemy (a heard contact counts); ⭐ <c>CE-2353</c> — a neutral is none (<see cref="SquadContacts.IsEnemy"/>).</summary>
        private static bool NothingToFight(EntityRepository world, Entity self)
            => !world.HasComponent<SquadCognitiveState>(self) || !SquadContacts.AnyEnemy(world, in world.GetComponentRO<SquadCognitiveState>(self).Contacts);

        /// <summary>A new rescue starts from nothing: the care play's runtime memory (who, who goes, from where) is cleared.</summary>
        private static void FreshCare(ref ReactToContactParams p)
        {
            p.Care.Casualty = Entity.Null; p.Care.Medic = Entity.Null; p.Care.MedicFrom = default;
            p.Care.CasualtySlot = 0; p.Care.MedicSlot = 0;
            if (p.Care.EvacuateMetres > 0f) p.Care.HasDestination = 0;   // ⭐ CE-7009 — each casualty's own collection point
        }

        /// <summary>Leaving the node: whichever query the current play owns goes.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.ReactToContactBehavior.Run")]
        public static void Deactivate_Run(ref ReactToContactParams p, ref ReactToContactState ws, Entity self, EntityRepository world)
            => SquadManeuverRunner.DropSensor(ref ws.Play, world);

        private static void Next(ref ReactToContactState ws, byte stage, EntityRepository world)
        {
            SquadManeuverRunner.DropSensor(ref ws.Play, world);
            ws.Play = default;
            ws.AssaultStage = AssaultUndecided;
            ws.Stage = stage;
            ws.StageSince = (SimInstant)(world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : 0d);
        }
    }
}
