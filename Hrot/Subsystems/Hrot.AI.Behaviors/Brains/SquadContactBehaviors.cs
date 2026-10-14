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
        public float DurationSeconds;
        /// <summary>1 = done once the squad knows no contact.</summary>
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
[Behavior(BehaviorNames.SquadHold, Params = typeof(global::Hrot.Map.Definitions.Behavior.SquadHoldParamsJsonDto), Categories = BehaviorCategory.Commander)]   // ⭐ CE-2134

        [BTreeDefinition(BehaviorNames.SquadHold, Curated = true, Group = "Library/Squad", ParamsType = typeof(SquadHoldBlackboard))]
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
            if (p.EndWhenClear != 0 && ctx.World.GetComponentRO<SquadCognitiveState>(ctx.Commander).Contacts.Count == 0) return NodeStatus.Success;
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
        public float BoundTimeoutSeconds;
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
            block.State = default;
        }

        internal static BreakContactParams Defaults(float radius, float speed, float arrival, float timeout) => new()
        {
            RetreatSearchRadius = radius > 0f ? radius : 60f,
            Speed               = speed > 0f ? speed : 3f,
            ArrivalRadius       = arrival > 0f ? arrival : 8f,
            BoundTimeoutSeconds = timeout > 0f ? timeout : 60f,
        };
[Behavior(BehaviorNames.BreakContact, Params = typeof(global::Hrot.Map.Definitions.Behavior.BreakContactParamsJsonDto), Categories = BehaviorCategory.Commander)]   // ⭐ CE-2134

        [BTreeDefinition(BehaviorNames.BreakContact, Curated = true, Group = "Library/Squad", ParamsType = typeof(BreakContactBlackboard))]
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
            if (!SquadManeuverRunner.TopContact(in ctx, 0, out var contact)) return PlayStart.Fail;   // nothing to break contact with
            ws.ThreatId = contact.EntityId;
            ws.ThreatPoint = new Vector3(contact.PositionX, contact.PositionY, contact.PositionZ);
            BoundingOverwatchManeuver.ComputePartitionInputs(ctx.Roster.Count, partition);
            return PlayStart.Go;
        }

        public void Update(ref BreakContactParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            if (ws.HasAnswer == 0)
                SquadManeuverRunner.AskTerrain(ref ws, in ctx, FindSafeRetreatPoint.BlueprintId, RetreatSite, p.RetreatSearchRadius,
                    SquadManeuverRunner.FirstLive(in ctx), new Entity((ulong)ws.ThreatId));
        }

        public NodeStatus Outcome(ref BreakContactParams p, ref SquadPlayState ws, in SquadPlayContext ctx)
        {
            if (!SquadManeuverRunner.AnyLive(in ctx) || ws.HasAnswer == SquadManeuverRunner.NoAnswer) return NodeStatus.Failure;
            if (ws.HasAnswer != 1) return NodeStatus.Running;
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
            byte moving = ctx.Squad.PhaseId == BoundingOverwatchManeuver.PhaseElement0Moving ? (byte)0 : (byte)1;
            return ctx.Now - ws.IssuedAt >= p.BoundTimeoutSeconds || SquadManeuverRunner.ElementArrived(in ws, in ctx, moving);
        }

        public void BeginIssue(ref BreakContactParams p, ref SquadPlayState ws, in SquadPlayContext ctx) { }

        public MemberOrder OrderFor(ref BreakContactParams p, ref SquadPlayState ws, in SquadPlayContext ctx, int member, byte element, Vector3 at)
        {
            byte moving = ctx.Squad.PhaseId == BoundingOverwatchManeuver.PhaseElement0Moving ? (byte)0 : (byte)1;
            if (ws.HasAnswer != 1 || element != moving)
                return new MemberOrder { Kind = MemberOrderKind.Hold, Speed = p.Speed, RoleId = BoundingOverwatchManeuver.RoleCovering };
            return new MemberOrder
            {
                Kind = MemberOrderKind.AdvanceTo, Point = ws.Answer, Speed = p.Speed,
                ArrivalRadius = SquadManeuverRunner.MoverArrivalMetres, RoleId = BoundingOverwatchManeuver.RoleMoving,
            };
        }

        public void Release(ref SquadPlayState ws, EntityRepository world) => SquadManeuverRunner.DropSensor(ref ws, world);

        private static float Flat(Vector3 d) { d.Z = 0f; return d.Length(); }
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
    }

    /// <summary>⭐ <c>CE-2123</c> S2 — where the reaction is: one play at a time, so one play state.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ReactToContactState
    {
        /// <summary>0 = returning fire · 1 = the chosen drill · 2 = holding until clear.</summary>
        public byte Stage;
        /// <summary>⭐ CE-2125 — sim time the current stage began (a leader lost since then sends the reaction back to decide again).</summary>
        public double StageSince;
        public UtilityChoice Choice;
        public SquadPlayState Play;
        /// <summary>⭐ <c>CE-2152</c> C-c (R-d) — sim time the choice was last scored (the re-decide clock while a man is down).</summary>
        public double DecidedAt;
        /// <summary>⭐ <c>CE-2158</c> §7.6.3 — the Assault's stage: 0 = not decided · 1 = closing (fire and movement) · 2 = flank and assault.</summary>
        public byte AssaultStage;
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
            block.State = default;
        }
[Behavior(BehaviorNames.ReactToContact, Params = typeof(global::Hrot.Map.Definitions.Behavior.ReactToContactParamsJsonDto), Categories = BehaviorCategory.Commander)]   // ⭐ CE-2134

        [BTreeDefinition(BehaviorNames.ReactToContact, Curated = true, Group = "Library/Squad", ParamsType = typeof(ReactToContactBlackboard))]
        public static BTreeBuilder<ReactToContactBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<ReactToContactBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Order, bb => bb.State, Run);

        public const byte StageReturnFire = 0, StageDrill = 1, StageHoldOn = 2;

        /// <summary>⭐ <c>CE-2158</c> §7.6.3 — the assault distance: the squad closes by fire and movement until its halves are this near
        /// the contact (m); a contact already within this + one rush is flanked at once.</summary>
        public const float CloseMetres = 50f;

        public const byte AssaultUndecided = 0, AssaultClosing = 1, AssaultFlanking = 2;

        /// <summary>⭐ <c>CE-2152</c> C-c (R-2013) — while a man is down and not yet cared for, the choice is scored again this often (s).</summary>
        public const double RedecideSeconds = 5d;

        public static NodeStatus Run(ref ReactToContactParams p, ref ReactToContactState ws, Entity self, EntityRepository world)
        {
            double now = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : 0d;
            // ⭐ CE-2152 C-c (R-2013) — a man down with NOTHING to fight (the squad knows no contact): no decision to make — bring him in.
            if (ws.Stage == StageReturnFire && SquadCasualties.Count(world, self) > 0 && NothingToFight(world, self))
            {
                Next(ref ws, StageDrill, world);
                ws.Choice = new UtilityChoice { Winner = (byte)SquadContactResponse.Rescue };
                ws.DecidedAt = now;
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
                    || Fdp.Toolkit.Behavior.Components.RecentSensesOf.WithinSince(world, self, Fdp.Toolkit.Perception.Events.SensorChange.SquadMemberDown, 3600d, ws.DecidedAt)))
            {
                byte was = ws.Choice.Winner;
                var fresh = new UtilityChoice();
                if (NothingToFight(world, self)) fresh.Winner = (byte)SquadContactResponse.Rescue;   // nothing to fight: bring him in
                else UtilityNodes.ChooseOptionOnce(ref p.Choose, ref fresh, self, world);
                ws.DecidedAt = now;
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
                    ws.DecidedAt = now;
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
                    ws.DecidedAt = now;
                    FreshCare(ref p);
                    return NodeStatus.Running;
                }
                if (drill == NodeStatus.Running && ws.Play.Started != 0) return NodeStatus.Running;
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
                if (close == NodeStatus.Running) return NodeStatus.Running;
                SquadManeuverRunner.DropSensor(ref ws.Play, world);   // closed (or could not close): the flank starts from here
                ws.Play = default;
                ws.AssaultStage = AssaultFlanking;
            }
            return SquadManeuverRunner.Tick<SuppressAndManeuverPlay, SuppressAndManeuverParams>(ref p.Assault, ref ws.Play, self, world);
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
        private static bool NothingToFight(EntityRepository world, Entity self)
        {
            if (!world.HasComponent<SquadCognitiveState>(self)) return true;
            ref readonly var squad = ref world.GetComponentRO<SquadCognitiveState>(self);
            var pool = MemoryMarshal.CreateReadOnlySpan(
                ref System.Runtime.CompilerServices.Unsafe.As<SquadContactPoolSlots, SquadContact>(
                    ref System.Runtime.CompilerServices.Unsafe.AsRef(in squad.Contacts.Contacts)), 16);
            for (int i = 0; i < squad.Contacts.Count && i < 16; i++)
            {
                var c = pool[i];
                if ((c.Flags & SquadContact.AnonymousFlag) != 0 || c.EntityId <= 0) return false;
                if (CombatLife.IsAlive(world, new Entity((ulong)c.EntityId))) return false;
            }
            return true;
        }

        /// <summary>A new rescue starts from nothing: the care play's runtime memory (who, who goes, from where) is cleared.</summary>
        private static void FreshCare(ref ReactToContactParams p)
        {
            p.Care.Casualty = Entity.Null; p.Care.Medic = Entity.Null; p.Care.MedicFrom = default;
            p.Care.CasualtySlot = 0; p.Care.MedicSlot = 0;
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
            ws.StageSince = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : 0d;
        }
    }
}
