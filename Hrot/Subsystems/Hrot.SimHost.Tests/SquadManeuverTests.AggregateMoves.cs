using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Squad.Command;
using Fdp.Toolkit.Squad;
using Hrot.AI.Behaviors.Brains;
using Hrot.CGF;
using Hrot.CGF.Systems;
using Hrot.Core.Network;
using Fdp.Toolkit.Replication.Services;
using Hrot.Core.Tkb;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>⭐⭐ agg-behaviors <c>ST-1</c> / <c>ST-2</c> — the stochastic unit's MOVING plays (Move, Defend's approach, BreakContact) and its SOP. The unit under test is the
    /// fixture's own commander entity made stochastic (an <c>AggregateUnit</c>, a slot table, a position, a squad pool): it runs the plays on the brain tick as a unit does in a node.</summary>
    public partial class SquadManeuverTests
    {
        private const string Sop = ResolutionRoutingTable.Plays.Sop;

        private static (Squad S, StochasticUnitMovementSystem Move, EntityRepository R, Entity U) StochasticMover(int slots = 12, Vector3? at = null)
        {
            var s = new Squad(members: 0);
            var repo = s.Repo;
            var u = s.Commander;
            AggregateTestUnits.Make(repo, u, slots: slots, up: slots);
            repo.AddComponent(u, new SimTransform { Position = at ?? Vector3.Zero, Rotation = Quaternion.Identity });
            repo.SetDisType(u, UnitDis.Land(UnitArm.Infantry, UnitEchelon.Platoon, 225));
            return (s, new StochasticUnitMovementSystem(), repo, u);
        }

        private static string Loc(string key, Vector3 v) => "\"" + key + "\":" + Pt(v);

        /// <summary>One second of the world: the brain frame, then the path follower (the order a node runs them in: the Brain group before the unit's Simulation step).</summary>
        private static void MoverSecond(Squad s, StochasticUnitMovementSystem mv, float dt = 1f)
        {
            s.Pause(dt - 0.016);
            s.Tick();
            mv.Execute(s.Repo, dt);
        }

        private static void Seconds(Squad s, StochasticUnitMovementSystem mv, int n, float dt = 1f) { for (int i = 0; i < n; i++) MoverSecond(s, mv, dt); }

        private static int Reports(EntityRepository repo, Entity e, ProgressKind kind)
        {
            if (!repo.HasComponent<ReportOutbox>(e)) return 0;
            int n = 0;
            ref readonly var box = ref repo.GetComponentRO<ReportOutbox>(e);
            for (int i = 0; i < box.Queued.Count; i++)
                if (box.Queued.Items[i].Report.Kind == ReportKind.Progress && box.Queued.Items[i].Report.Subtype == (byte)kind) n++;
            return n;
        }

        private static UnitMoveState MoveOf(EntityRepository repo, Entity e) => repo.GetComponentRO<AggregateUnit>(e).Move;
        private static Vector3 Where(EntityRepository repo, Entity e) => repo.GetComponentRO<SimTransform>(e).Position;

        /// <summary>The rounds a step of incoming fire writes — as phase B does (<c>StochasticCombat</c> ⑤): the bucket at the head, the head advances, the share follows.</summary>
        private static void IncomingFire(EntityRepository repo, Entity e, float perMan = 2f)
        {
            ref var u = ref repo.GetComponentRW<AggregateUnit>(e);
            int head = u.IncomingHead % AggregateUnit.SuppressionWindowSteps;
            u.IncomingRounds[head] = perMan;
            u.IncomingHead = (byte)((head + 1) % AggregateUnit.SuppressionWindowSteps);
            float window = 0f;
            for (int i = 0; i < AggregateUnit.SuppressionWindowSteps; i++) window += u.IncomingRounds[i];
            u.SuppressedShare = Math.Clamp(window / StochasticCombat.SuppressingRoundsPerMan, 0f, 1f);
            u.LastContactAt = (SimInstant)(double)repo.GetSingleton<GlobalTime>().TotalTime;
        }

        private static void NoIncomingFire(EntityRepository repo, Entity e)
        {
            ref var u = ref repo.GetComponentRW<AggregateUnit>(e);
            for (int i = 0; i < AggregateUnit.SuppressionWindowSteps; i++) u.IncomingRounds[i] = 0f;
            u.SuppressedShare = 0f;
        }

        /// <summary>⭐⭐ <c>ST-1</c> ACCEPTANCE (design §14 M-2, §19 B-2; R-8307, R-8308) — a stochastic platoon ordered <c>Move</c> over a route arrives: its TAIL (a column, 5 m a man)
        /// reaches the end within ± 10 % of <see cref="UnitSpeedModel.PredictSeconds"/>, the unit's point (its head) ends AT the location, the play reports PROGRESS Set once and ends in Success.
        /// ✅ Red-proofs: the play ordering a <c>Line</c> (not a column) ⇒ the speed cut changes the arrival ⇒ red · the play ending on the first <c>Arrived</c>-or-anything ⇒ no.</summary>
        [Fact]
        public void ST1_Acceptance_AStochasticPlatoon_OrderedMove_ArrivesWithinTenPercentOfThePredictedTime()
        {
            var (s, mv, repo, u) = StochasticMover(slots: 48);
            var target = new Vector3(0f, 300f, 0f);
            s.Order("{" + Loc("Location", target) + "}", ResolutionRoutingTable.Plays.Move);
            double elapsed = -1;
            double predicted = 0;
            for (int i = 0; i < 1500; i++)
            {
                s.Pause(1.0 - 0.016);
                s.Tick();
                var au = repo.GetComponentRO<AggregateUnit>(u);
                if (elapsed < 0 && au.Move != UnitMoveState.Stopped) elapsed = 0;           // the clock starts when the play has ordered the unit
                mv.Execute(repo, 1f);
                if (elapsed >= 0)
                {
                    elapsed += 1.0;
                    if (predicted == 0 && repo.HasManagedComponent<UnitRoute>(u))
                        predicted = UnitSpeedModel.PredictSeconds(repo.GetComponent<UnitRoute>(u), repo.GetComponentRO<AggregateUnit>(u), repo.GetComponentRO<UnitHoldings>(u));
                    if (repo.GetComponentRO<AggregateUnit>(u).Move == UnitMoveState.Arrived) break;
                }
            }
            Assert.Equal(UnitMoveState.Arrived, MoveOf(repo, u));
            Assert.True(predicted > 100, "a 300 m column march on foot takes minutes, not " + predicted);
            Assert.InRange(elapsed, predicted * 0.9, predicted * 1.1);
            Assert.Equal(UnitFormation.Column, repo.GetComponentRO<AggregateUnit>(u).Formation);
            Assert.Equal((48 - 1) * UnitMoves.ManSpacing, repo.GetComponentRO<AggregateUnit>(u).Depth);   // R-8307: depth = ColumnDepth
            Assert.True(Vector3.Distance(target, Where(repo, u)) < 1f, "the head is at the location: " + Where(repo, u));
            s.Tick(); s.Tick();                                                                           // the play sees the arrival
            Assert.Equal(1, Reports(repo, u, ProgressKind.Set));
            Assert.True(s.Finished(out var result) || s.TaskName != ResolutionRoutingTable.Plays.Move, "the play ended");
        }

        /// <summary>⭐ <c>ST-1</c> (R-8308) — a dropped route is RE-REQUESTED (the move goes back to AwaitingRoute) at most 3 times; the 4th drop ends the play in Failure and the commander
        /// is told PROGRESS TaskFailed (never Set). ✅ Red-proofs: no re-request ⇒ stays RouteFailed ⇒ red · the retry limit 4 ⇒ the 4th drop is re-requested ⇒ red.</summary>
        [Fact]
        public void ST1_ADroppedRoute_IsReRequestedThreeTimes_ThenTheCommanderGetsTaskFailed()
        {
            var (s, mv, repo, u) = StochasticMover();
            s.Order("{" + Loc("Location", new Vector3(0f, 500f, 0f)) + "}", ResolutionRoutingTable.Plays.Move);
            for (int i = 0; i < 6 && MoveOf(repo, u) != UnitMoveState.AwaitingRoute; i++) s.Tick();     // the play has ordered; the follower has not run
            Assert.Equal(UnitMoveState.AwaitingRoute, MoveOf(repo, u));
            for (int drop = 1; drop <= 3; drop++)
            {
                repo.GetComponentRW<AggregateUnit>(u).Move = UnitMoveState.RouteFailed;                 // the planner refused / dropped it
                s.Tick();
                Assert.Equal(UnitMoveState.AwaitingRoute, MoveOf(repo, u));                               // re-requested (drop " + drop)
                Assert.Equal(0, Reports(repo, u, ProgressKind.TaskFailed));
            }
            repo.GetComponentRW<AggregateUnit>(u).Move = UnitMoveState.RouteFailed;                       // the 4th drop
            bool finished = false; Fbt.NodeStatus result = default;
            for (int i = 0; i < 3 && !finished; i++) { s.Tick(); finished = s.Finished(out result); }
            Assert.True(finished, "the play ended");
            Assert.Equal(Fbt.NodeStatus.Failure, result);
            Assert.Equal(1, Reports(repo, u, ProgressKind.TaskFailed));
            Assert.Equal(0, Reports(repo, u, ProgressKind.Set));
        }

        /// <summary>⭐ <c>ST-1</c> (R-8307) — <c>Defend</c> farther than its arrival radius MOVES first (a deployed line): no PROGRESS Set and no <c>Prepared</c> level on the way; on arrival it
        /// occupies, reports Set once and the prepared level starts. ✅ Red-proof: the old immediate occupy (Set at the first tick) ⇒ the on-the-way assertions go red.</summary>
        [Fact]
        public void ST1_Defend_MovesToItsPosition_ThenReportsSet_AndPreparesFromArrival()
        {
            var (s, mv, repo, u) = StochasticMover();
            var at = new Vector3(0f, 240f, 0f);
            s.Order("{" + Loc("Position", at) + "}", ResolutionRoutingTable.Plays.Defend);
            Seconds(s, mv, 20);
            Assert.Equal(UnitMoveState.OnRoute, MoveOf(repo, u));
            Assert.Equal(UnitFormation.Line, repo.GetComponentRO<AggregateUnit>(u).Formation);
            Assert.Equal(StochasticUnitMoves.LineDepthMetres, repo.GetComponentRO<AggregateUnit>(u).Depth);
            Assert.Equal(0, Reports(repo, u, ProgressKind.Set));                                          // not yet there
            Assert.Equal(PreparedLevel.Unprepared, repo.GetComponentRO<AggregateUnit>(u).Prepared);
            Assert.True(Where(repo, u).Y > 5f && Where(repo, u).Y < 200f, "on the way: " + Where(repo, u));
            for (int i = 0; i < 400 && Reports(repo, u, ProgressKind.Set) == 0; i++) MoverSecond(s, mv);
            Assert.Equal(1, Reports(repo, u, ProgressKind.Set));
            Assert.True(Vector3.Distance(at, Where(repo, u)) < 1f);
            Assert.Equal(PreparedLevel.Hasty, repo.GetComponentRO<AggregateUnit>(u).Prepared);
            Assert.Equal(UnitMoveState.Arrived, MoveOf(repo, u));
            Assert.Equal(ResolutionRoutingTable.Plays.Defend, s.TaskName);                                // it never ends by itself
        }

        /// <summary>⭐ <c>ST-1</c> — a unit already at its Defend position occupies at ONCE (no move, no order to the follower): Set on the first tick, still <c>Stopped</c>/<c>Arrived</c>.
        /// ⭐ R-8342 — and it KEEPS a position it already prepared: an authored Fortified unit ordered to defend where it stands stays Fortified (an unprepared one starts Hasty).
        /// ✅ Red-proof: the old unconditional <c>Prepared = Hasty</c> at occupation ⇒ the Fortified unit drops to Hasty ⇒ red.</summary>
        [Fact]
        public void ST1_Defend_WhereItAlreadyStands_OccupiesAtOnce_WithoutAMove_AndKeepsItsPreparedLevel()
        {
            var (s, mv, repo, u) = StochasticMover(at: new Vector3(100f, 100f, 0f));
            s.Order("{" + Loc("Position", new Vector3(110f, 100f, 0f)) + "}", ResolutionRoutingTable.Plays.Defend);
            Seconds(s, mv, 5);
            Assert.Equal(1, Reports(repo, u, ProgressKind.Set));
            Assert.Equal(UnitMoveState.Arrived, MoveOf(repo, u));
            Assert.Equal(PreparedLevel.Hasty, repo.GetComponentRO<AggregateUnit>(u).Prepared);

            var (s2, mv2, repo2, f) = StochasticMover(at: new Vector3(100f, 100f, 0f));
            repo2.GetComponentRW<AggregateUnit>(f).Prepared = PreparedLevel.Fortified;                     // an authored, long-held position
            s2.Order("{" + Loc("Position", new Vector3(110f, 100f, 0f)) + "}", ResolutionRoutingTable.Plays.Defend);
            Seconds(s2, mv2, 5);
            Assert.Equal(1, Reports(repo2, f, ProgressKind.Set));
            Assert.Equal(PreparedLevel.Fortified, repo2.GetComponentRO<AggregateUnit>(f).Prepared);
        }

        /// <summary>⭐ <c>ST-1</c> (0 B) — the move play's STEADY tick — watching a unit on its route — allocates nothing (CLAUDE.md "HOT PATH"). ✅ Red-proof: a <c>new List</c> in <c>Step</c> ⇒ red.</summary>
        [Fact]
        public void ST1_ThePlaysSteadyTick_AllocatesNothing()
        {
            var (s, mv, repo, u) = StochasticMover();
            var p = new StochasticMoveParams { Target = new Vector3(0f, 400f, 0f) };
            Assert.Equal(NodeStatusOf(ref p, u, repo), Fbt.NodeStatus.Running);                           // orders the unit
            repo.GetComponentRW<AggregateUnit>(u).Move = UnitMoveState.OnRoute;                           // the follower took it
            for (int i = 0; i < 5; i++) StochasticUnitMoves.Drive(ref p, u, repo);                         // warm
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++) StochasticUnitMoves.Drive(ref p, u, repo);
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
            var sop = new StochasticDefendParams { Position = new Vector3(0f, 400f, 0f) };
            Assert.True(StochasticUnitMoves.Step(ref sop.Drive, sop.Position, UnitFormation.Line, u, repo) == DriveResult.Moving);
            repo.GetComponentRW<AggregateUnit>(u).Move = UnitMoveState.OnRoute;
            long b2 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++)
            {
                StochasticUnitMoves.Step(ref sop.Drive, sop.Position, UnitFormation.Line, u, repo);
                StochasticUnitSop.UnderFireNotDefending(u, repo);
                StochasticUnitSop.IsSuppressed(u, repo);
            }
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - b2);
        }

        private static Fbt.NodeStatus NodeStatusOf(ref StochasticMoveParams p, Entity u, EntityRepository repo) => StochasticUnitMoves.Drive(ref p, u, repo);

        /// <summary>The stochastic unit as a TEMPLATE spawns it: its type's default SOP (<c>BasicSquadSop</c>, origin Sop) published for it, the ingress resolving it through the table.</summary>
        private static void SpawnWithTheTemplatesSop(Squad s, Entity u)
        {
            StochasticSopRouting.Resolver = TacticalIntentResolutionSystem.SopFor;                         // what CgfLogicPack sets (its forwarding rail is below)
            if (!s.Repo.HasComponent<SopState>(u)) s.Repo.AddComponent(u, new SopState());
            s.Repo.Bus.PublishManaged(new AssignSopEvent { Entity = u, BehaviorName = "BasicSquadSop", JsonParams = "{}", Origin = BehaviorOrigin.Sop });
        }

        private static void Contact(Squad s, Vector3 at) => s.Pool(s.Repo.CreateEntity(), at, 1);

        /// <summary>⭐⭐ <c>ST-2</c> ACCEPTANCE (design §19 B-1/B-5, RV-7; R-8309, R-8310) — under fire the stochastic unit BREAKS CONTACT: spawned with its template's <c>BasicSquadSop</c> the
        /// ingress gives it <c>StochasticUnitSop</c> (the table's row); marching north, incoming rounds (written as phase B writes them) start <c>StochasticUnitBreakContact</c>, which
        /// withdraws 300 m directly AWAY from the nearest contact of its squad pool (south), as a column; on arrival it reports Set, and with the fire gone the paused Move resumes.
        /// ✅ Red-proofs: the SOP row's guard always false ⇒ no reaction ⇒ red · the break point toward the contact ⇒ the direction assertion red · the template's SOP kept (no routing) ⇒ the SOP assertion red.</summary>
        [Fact]
        public void ST2_Acceptance_AStochasticUnit_UnderFire_BreaksContact_AwayFromTheFire_AndItsOrderResumes()
        {
            var (s, mv, repo, u) = StochasticMover(slots: 12, at: new Vector3(0f, 0f, 0f));
            SpawnWithTheTemplatesSop(s, u);
            s.Order("{" + Loc("Location", new Vector3(0f, 2000f, 0f)) + "}", ResolutionRoutingTable.Plays.Move);
            Seconds(s, mv, 30);
            Assert.Equal(BehaviorHash.FromName(Sop), repo.GetComponentRO<SopState>(u).SopHash);            // ⭐ the table's SOP, not BasicSquadSop
            Assert.Equal(ResolutionRoutingTable.Plays.Move, s.TaskName);
            float y0 = Where(repo, u).Y;
            Assert.True(y0 > 5f, "marching north: " + y0);
            Contact(s, new Vector3(0f, y0 + 400f, 0f));                                                    // the enemy is ahead
            IncomingFire(repo, u);
            for (int i = 0; i < 6 && s.TaskName != ResolutionRoutingTable.Plays.BreakContact; i++) MoverSecond(s, mv);
            var sopNow = repo.GetComponentRO<SopState>(u);
            Assert.True(s.TaskName == ResolutionRoutingTable.Plays.BreakContact, $"task {s.TaskName}; sop faulted {sopNow.SopFaulted} hash {sopNow.SopHash} under {StochasticUnitSop.UnderFire(repo.GetComponentRO<AggregateUnit>(u))} order {Hrot.AI.Behaviors.Orders.CurrentOrders.Of(repo, u).Kind}");   // the reaction took the task slot
            var au = repo.GetComponentRO<AggregateUnit>(u);
            Assert.Equal(UnitFormation.Column, au.Formation);
            Assert.True(((Vector3)au.Destination).Y < Where(repo, u).Y - 250f, "away from the fire (south): " + (Vector3)au.Destination);
            Assert.InRange(Vector2.Distance(new Vector2(0f, Where(repo, u).Y), new Vector2(((Vector3)au.Destination).X, ((Vector3)au.Destination).Y)), 290f, 310f);
            NoIncomingFire(repo, u);                                                                       // the fire dies down: the retreat completes, the order resumes
            for (int i = 0; i < 600 && s.TaskName == ResolutionRoutingTable.Plays.BreakContact; i++) MoverSecond(s, mv);
            Assert.True(Where(repo, u).Y < y0 - 250f + 1f, "it withdrew: " + Where(repo, u).Y + " from " + y0);
            Assert.Equal(1, Reports(repo, u, ProgressKind.Set));
            for (int i = 0; i < 5; i++) MoverSecond(s, mv);
            Assert.Equal(ResolutionRoutingTable.Plays.Move, s.TaskName);                                  // the paused Move is back…
            Assert.Equal(2000f, ((Vector3)repo.GetComponentRO<AggregateUnit>(u).Destination).Y);          // …and re-ordered the unit north
        }

        /// <summary>⭐ <c>ST-2</c> — a unit with NO contact in its pool breaks contact directly BEHIND its heading (the contract carries no firer position). A <c>To</c> in the order wins over both.</summary>
        [Fact]
        public void ST2_BreakContact_ToThePoint_ElseBehindTheHeading_WhenNoContact()
        {
            var (s, mv, repo, u) = StochasticMover(at: new Vector3(50f, 50f, 0f));
            repo.GetComponentRW<AggregateUnit>(u).Heading = new Bearing(90f);                              // facing east
            s.Order("{}", ResolutionRoutingTable.Plays.BreakContact);
            for (int i = 0; i < 6 && MoveOf(repo, u) != UnitMoveState.AwaitingRoute; i++) s.Tick();
            var d = (Vector3)repo.GetComponentRO<AggregateUnit>(u).Destination;
            Assert.Equal(50f - StochasticUnitMoves.BreakContactMetres, d.X, 1);                            // west, 300 m
            Assert.Equal(50f, d.Y, 1);
            var (s2, mv2, repo2, u2) = StochasticMover(at: new Vector3(50f, 50f, 0f));
            s2.Order("{" + Loc("To", new Vector3(500f, 600f, 0f)) + "}", ResolutionRoutingTable.Plays.BreakContact);
            for (int i = 0; i < 6 && MoveOf(repo2, u2) != UnitMoveState.AwaitingRoute; i++) s2.Tick();
            Assert.Equal(new Vector3(500f, 600f, 0f), (Vector3)repo2.GetComponentRO<AggregateUnit>(u2).Destination);
        }

        /// <summary>⭐ <c>ST-2</c> (R-8310) — a unit whose order is DEFEND does not break contact under fire (the NotDefending precedent): its defence stays the task.
        /// ✅ Red-proof: the row's NotDefending clause dropped ⇒ the reaction replaces the Defend ⇒ red.</summary>
        [Fact]
        public void ST2_ADefendingUnit_UnderFire_DoesNotBreakContact()
        {
            var (s, mv, repo, u) = StochasticMover(at: new Vector3(0f, 0f, 0f));
            SpawnWithTheTemplatesSop(s, u);
            s.Order("{" + Loc("Position", new Vector3(0f, 0f, 0f)) + "}", ResolutionRoutingTable.Plays.Defend);
            Seconds(s, mv, 5);
            Assert.Equal(ResolutionRoutingTable.Plays.Defend, s.TaskName);
            Contact(s, new Vector3(0f, 300f, 0f));
            for (int i = 0; i < 6; i++) { IncomingFire(repo, u, 0.5f); MoverSecond(s, mv); }               // fire, but below the suppressed share (0.5 × 5 / 6 = 0.41 < 0.5)
            Assert.True(repo.GetComponentRO<AggregateUnit>(u).SuppressedShare < StochasticUnitSop.SuppressedAt);
            Assert.True(StochasticUnitSop.UnderFire(repo.GetComponentRO<AggregateUnit>(u)));
            Assert.Equal(ResolutionRoutingTable.Plays.Defend, s.TaskName);
            Assert.Equal(PreparedLevel.Hasty, repo.GetComponentRO<AggregateUnit>(u).Prepared);
        }

        /// <summary>⭐ <c>ST-2</c> (R-8310) — SUPPRESSED (share ≥ 0.5) with no fresh fire stops a marching unit (<c>StochasticUnitHold</c>); when the share falls the reaction ends and the paused
        /// Move re-orders the unit. ✅ Red-proof: the SOP's second row dropped ⇒ the unit keeps marching ⇒ red.</summary>
        [Fact]
        public void ST2_ASuppressedUnit_Stops_AndMarchesOnWhenTheShareFalls()
        {
            var (s, mv, repo, u) = StochasticMover();
            SpawnWithTheTemplatesSop(s, u);
            s.Order("{" + Loc("Location", new Vector3(0f, 2000f, 0f)) + "}", ResolutionRoutingTable.Plays.Move);
            Seconds(s, mv, 20);
            Assert.Equal(UnitMoveState.OnRoute, MoveOf(repo, u));
            float y = Where(repo, u).Y;
            repo.GetComponentRW<AggregateUnit>(u).SuppressedShare = 0.6f;                                   // pinned down; no new rounds (ring empty ⇒ not "under fire")
            Seconds(s, mv, 3);
            Assert.Equal(StochasticUnitHoldBehavior.Name, s.TaskName);
            Assert.Equal(UnitMoveState.Stopped, MoveOf(repo, u));
            float held = Where(repo, u).Y;
            Seconds(s, mv, 5);
            Assert.Equal(held, Where(repo, u).Y);                                                           // it does not creep
            repo.GetComponentRW<AggregateUnit>(u).SuppressedShare = 0.2f;                                   // the fire eased
            Seconds(s, mv, 5);
            Assert.Equal(ResolutionRoutingTable.Plays.Move, s.TaskName);
            Assert.True(Where(repo, u).Y > held + 1f, "marching again: " + Where(repo, u).Y + " vs " + held);
            Assert.True(held >= y);
        }

        /// <summary>⭐ <c>ST-2</c> — the SOP's two conditions on the ring: rounds in the last 3 steps (and the head's pending bucket) are fire; a bucket older than that is not.</summary>
        [Fact]
        public void ST2_UnderFire_IsTheLastThreeSecondsOfTheRing()
        {
            var u = new AggregateUnit { Resolution = AggregateResolution.Stochastic };
            Assert.False(StochasticUnitSop.UnderFire(in u));
            u.IncomingRounds[0] = 1f; u.IncomingHead = 1;                                                   // a step just closed (bucket 0)
            Assert.True(StochasticUnitSop.UnderFire(in u));
            u.IncomingHead = 4;                                                                             // that bucket is now the 4th newest
            Assert.False(StochasticUnitSop.UnderFire(in u));
            u.IncomingHead = 3;                                                                             // the 3rd newest
            Assert.True(StochasticUnitSop.UnderFire(in u));
            u.IncomingRounds[0] = 0f; u.IncomingRounds[3] = 0.1f; u.IncomingHead = 3;                       // a burst the area sink added to the head's pending bucket
            Assert.True(StochasticUnitSop.UnderFire(in u));
        }

        /// <summary>⭐ <c>ST-2</c> FORWARDING RAIL (the silent-default pattern, R-8311) — the production host's brain pack SETS the ingress's stochastic-SOP resolver, and the resolver answers the
        /// table's row for a stochastic unit (null for a physical one — the ingress never asks it). ✅ Red-proof: the assignment in <c>CgfLogicPack</c> removed ⇒ red.</summary>
        [Fact]
        public void ST2_TheBrainPack_HandsTheIngressTheTablesSopResolver()
        {
            StochasticSopRouting.Resolver = null;
            using var world = new EntityRepository();
            _ = new CgfLogicPack(new BehaviorRegistry(), new NetworkEntityMap(),
                Hrot.AI.Behaviors.Mappers.DefaultTacticalMappers.Create(), new Fdp.Toolkit.Blueprints.BlueprintRegistry());
            Assert.NotNull(StochasticSopRouting.Resolver);
            var (s, _, repo, u) = StochasticMover();
            Assert.Equal(Sop, StochasticSopRouting.Resolver!(repo, u, n => true));
            repo.GetComponentRW<AggregateUnit>(u).Resolution = AggregateResolution.Physical;
            Assert.False(AggregateUnits.IsStochastic(repo, u));
        }
    }
}
