using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Components;
using Hrot.AI.Behaviors.Brains;
using Hrot.CGF.Commander;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4676</c> (docs/designs/llm-commander/DESIGN_LLM_Commander.md §5, L-1) — a platoon commander's SEAT: the feed out, the actions in, the
    /// scripted mind, over the command chain's own platoon fixture (<see cref="Platoon"/>: a body-less platoon over squad stand-ins, its inbox folding
    /// the reports the rails publish).
    /// </summary>
    public partial class SquadManeuverTests
    {
        /// <summary>A mind that does what the rail says on each call and records what it was shown.</summary>
        private sealed class RailMind : IMind
        {
            public readonly List<Briefing> Seen = new();
            public Func<Briefing, IReadOnlyList<CommanderAction>> Answer = _ => Array.Empty<CommanderAction>();
            public int ThreadId;
            public Task<IReadOnlyList<CommanderAction>> ThinkAsync(Briefing b, CancellationToken ct)
            {
                ThreadId = Environment.CurrentManagedThreadId;
                lock (Seen) Seen.Add(b);
                return Task.FromResult(Answer(b));
            }
        }

        /// <summary>The platoon with a seat, four named places, and the two seat systems around the fixture's frame.</summary>
        private sealed class Seated
        {
            public readonly Platoon P;
            public readonly CommanderSeats Seats = new();
            public readonly CommanderFeedSystem Feed;
            public readonly CommanderActionSystem Act;
            public readonly CommanderWatchSystem Watch;   // ⭐ CE-4684 C-2
            public EntityRepository Repo => P.S.Repo;

            /// <summary>⭐ CE-4679 — the node bus the hold's pause / resume ride (null = the world's bus).</summary>
            public readonly Fdp.Core.FdpEventBus? ClockBus;

            public Seated(IMind mind, int squads = 2, Fdp.Core.FdpEventBus? clockBus = null, Func<string, bool>? isBehaviour = null, Action<Seated>? before = null)
            {
                ClockBus = clockBus;
                P = new Platoon(squads);
                if (!Repo.IsComponentTypeRegistered<CommanderSeat>()) Repo.RegisterComponent<CommanderSeat>();
                if (!Repo.IsComponentTypeRegistered<TkbIdentity>()) Repo.RegisterComponent<TkbIdentity>();
                Fdp.Toolkit.Squad.SquadStateProvisioning.EnsureForCommander(Repo, P.S.Commander);   // its report history, as a roster's commander has
                Name(P.S.Commander, "Platoon");
                Name(P.Alpha, "Alpha");
                Name(P.Bravo, "Bravo");
                Place("house", 100, 100);
                Place("objective", 90, 80);
                Place("support", 60, 60);
                Place("start", 0, 0);
                Repo.AddComponent(P.S.Commander, new CommanderSeat { Mind = CommanderMind.Scripted, Mode = CommanderMode.ThinkPause });
                Seats.MindFor = _ => mind;
                // ⭐ CE-4685 — a commander's class, as the spawner gives it (the header DIS type): his own orders resolve by KIND through the routing table
                Repo.SetDisType(P.S.Commander, Hrot.Core.Tkb.UnitDis.Land(Hrot.Core.Tkb.UnitArm.Infantry, Hrot.Core.Tkb.UnitEchelon.Platoon, 225));
                before?.Invoke(this);   // ⭐ CE-4685 — what the seat must see when it opens (its class, its units' classes)
                Feed = new CommanderFeedSystem(Seats, isBehaviour: isBehaviour, registry: P.S.Registry);
                Act = new CommanderActionSystem(Seats, clockBus);
                Watch = new CommanderWatchSystem(Seats);
            }

            public void Name(Entity e, string name)
            {
                var info = new EntityInfo { Name = new FixedString64(name), ForceId = ForceId.Friend };
                if (Repo.HasComponent<EntityInfo>(e)) Repo.SetComponent(e, info); else Repo.AddComponent(e, info);
            }

            public void Place(string name, float x, float y)
            {
                var e = Repo.CreateEntity();
                Repo.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)Gazetteer.PointType });
                Repo.AddComponent(e, new EntityInfo { Name = new FixedString64(name) });
                Repo.AddComponent(e, new SimTransform { Position = new Vector3(x, y, 0f), Rotation = Quaternion.Identity });
            }

            /// <summary>One frame: the action pre-pass, the fixture's frame (orders in, the inbox), then the feed — the pack's order.</summary>
            public void Tick()
            {
                Act.Execute(Repo, 0.016f);
                P.Tick();
                Watch.Execute(Repo, 0.016f);   // ⭐ CE-4684 C-2 — after the action pass, as the pack runs it
                // ⭐ CE-4680 — the intent resolver's part for the commander's OWN task (the fixture has none). ⭐ CE-4685 — the seat gives a KIND
                //   (Order.<kind>): resolved through THE routing table for his class, as TacticalIntentResolutionSystem resolves it in a node
                for (int i = _resolved; i < P.S.Orders.Count; i++)
                    if (P.S.Orders[i].Entity.Equals(P.S.Commander)) P.S.Order(P.S.Orders[i].JsonParams, ResolveOwn(P.S.Orders[i].IntentId));
                _resolved = P.S.Orders.Count;
                Feed.Execute(Repo, 0.016f);
            }
            private int _resolved;

            private string ResolveOwn(string intent)
            {
                const string prefix = "Order.";
                if (!intent.StartsWith(prefix, StringComparison.Ordinal) || !Enum.TryParse<Hrot.AI.Behaviors.Orders.OrderKind>(intent[prefix.Length..], out var kind)) return intent;
                var resolution = Fdp.Toolkit.Aggregate.AggregateUnits.IsStochastic(Repo, P.S.Commander) ? Fdp.Toolkit.Aggregate.AggregateResolution.Stochastic : Fdp.Toolkit.Aggregate.AggregateResolution.Physical;
                return Hrot.AI.Behaviors.Orders.OrderReissue.PlayFor(Repo, P.S.Commander, resolution, kind, KnownBehaviour) ?? intent;
            }
            public void Ticks(int n) { for (int i = 0; i < n; i++) Tick(); }

            public SeatRuntime Seat => Seats.TryGet(P.S.Commander, out var s) ? s : throw new InvalidOperationException("no seat");

            public List<FeedItem> Items(long after = 0) { var l = new List<FeedItem>(); Seat.Channel.ReadAfter(after, l); return l; }
        }

        /// <summary>
        /// ⭐ CE-4676 — the feed says each report into the commander ONCE, as a <c>report</c> item with its radio line (the place from the gazetteer), and
        /// a frame with no new report says nothing. 🔴 Mutation: the history head never kept ⇒ the SPOT is said again every frame.
        /// </summary>
        [Fact]
        public void CE4676_TheFeed_SaysEachReportIntoTheCommanderOnce_WithItsRadioLine()
        {
            var s = new Seated(new RailMind());
            s.Ticks(3);
            long before = s.Seat.Channel.LatestSeq;
            Assert.Contains(s.Items(), i => i.Kind == FeedKind.Snapshot && i.Wake);   // the board, at the start

            s.P.ReportSpot(s.P.Alpha, new Vector3(100f, 130f, 0f), 3);
            s.Ticks(30);
            var reports = s.Items(before).Where(i => i.Kind == FeedKind.Report).ToList();
            var spot = Assert.Single(reports);
            Assert.Contains("ALPHA: spot, 3 enemy", spot.Radio);
            Assert.Contains("30 m N of house", spot.Radio);
            Assert.True(spot.Wake);
            Assert.Contains("\"from\":\"Alpha\"", spot.Json);
        }

        /// <summary>
        /// ⭐ CE-4676 — an illegal action is REFUSED with its reason and changes nothing: another commander's unit, a destroyed squad, an unknown place, an
        /// order a squad does not take. 🔴 Mutation: the place check skipped ⇒ "the moon" orders a move to (0,0).
        /// </summary>
        [Fact]
        public void CE4676_AnIllegalAction_IsRefusedWithItsReason_AndChangesNothing()
        {
            var mind = new RailMind();
            var s = new Seated(mind);
            var zulu = s.Repo.CreateEntity();
            s.Name(zulu, "Zulu");   // a squad that is not his
            int calls = 0;
            mind.Answer = _ => Interlocked.Increment(ref calls) != 1 ? Array.Empty<CommanderAction>() : new[]
            {
                new CommanderAction(CommanderActionKind.Order, "Zulu", "Move", Where: "house"),
                new CommanderAction(CommanderActionKind.Order, "Bravo", "Move", Where: "house"),
                new CommanderAction(CommanderActionKind.Order, "Alpha", "Move", Where: "the moon"),
                new CommanderAction(CommanderActionKind.Order, "Alpha", "Fly", Where: "house"),
            };
            s.Repo.DestroyEntity(s.P.Bravo);
            int orders = s.P.S.Orders.Count;
            s.Ticks(4);

            var states = s.Items().Where(i => i.Kind == FeedKind.OrderState).ToList();
            Assert.Equal(4, states.Count(i => i.Json.Contains("\"state\":\"refused\"")));
            Assert.Contains(states, i => i.Radio.Contains("no squad named 'Zulu'"));
            Assert.Contains(states, i => i.Radio.Contains("no squad named 'Bravo'"));
            Assert.Contains(states, i => i.Radio.Contains("no place named 'the moon'"));
            Assert.Contains(states, i => i.Radio.Contains("'Fly' is not an order"));
            Assert.Equal(orders, s.P.S.Orders.Count);   // nothing reached a unit
            Assert.Equal(4, s.Act.Refused);
        }

        /// <summary>
        /// ⭐⭐ CE-4676 — the scripted mind end to end: at the start every squad moves to the objective; on a SPOT the squad NEAREST the enemy attacks the
        /// reported place and the other supports by fire from the support position — every order at origin <b>Superior</b>, in the frames right after the
        /// report (think-pause), from a mind on its own thread. 🔴 Mutation: the nearest pick reversed ⇒ Alpha attacks.
        /// </summary>
        [Fact]
        public void CE4676_TheScriptedMind_SendsTheNearestSquadAtTheSpot_AtOriginSuperior_OffTheFrameThread()
        {
            var s = new Seated(new ScriptedMind());
            s.Ticks(4);
            Assert.Equal(1, s.P.OrdersTo(s.P.Alpha, "Order.Move"));
            Assert.Equal(1, s.P.OrdersTo(s.P.Bravo, "Order.Move"));

            // Bravo (at 4,0) is nearer the enemy than Alpha (at 0,0)
            s.P.ReportSpot(s.P.Bravo, new Vector3(110f, 100f, 0f), 4);
            s.Ticks(4);   // 64 ms of sim time
            var bravo = s.P.S.Orders.Last(o => o.Entity.Equals(s.P.Bravo));
            var alpha = s.P.S.Orders.Last(o => o.Entity.Equals(s.P.Alpha));
            Assert.Equal("Order.Attack", bravo.IntentId);
            Assert.DoesNotContain("HoldBack", bravo.JsonParams);
            Assert.Equal("Order.Attack", alpha.IntentId);
            Assert.Contains("\"HoldBack\":true", alpha.JsonParams);
            Assert.Contains("[60,60,0]", alpha.JsonParams);   // the support position
            Assert.All(s.P.S.Orders, o => Assert.Equal(BehaviorOrigin.Superior, o.Origin));
            Assert.NotEqual(0, s.Seat.Runner!.ThreadId);
            Assert.NotEqual(Environment.CurrentManagedThreadId, s.Seat.Runner.ThreadId);
            s.Seats.Dispose();
        }

        /// <summary>
        /// ⭐ CE-4676 — the hot path: with a seat and no new report, the feed and the action system allocate NOTHING per frame.
        /// 🔴 Mutation: the steady-frame head compare removed ⇒ the history is walked into a fresh list each frame.
        /// </summary>
        [Fact]
        public void CE4676_ASteadyFrame_WithASeatAndNoNewReport_AllocatesNothing()
        {
            var s = new Seated(new RailMind());
            s.P.ReportSpot(s.P.Alpha, new Vector3(100f, 130f, 0f), 3);
            s.Ticks(20);
            long best = long.MaxValue;
            for (int round = 0; round < 5; round++)
            {
                s.P.Tick();
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                for (int f = 0; f < 10; f++) { s.Act.Execute(s.Repo, 0.016f); s.Feed.Execute(s.Repo, 0.016f); }
                best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - a0);
            }
            Assert.Equal(0L, best);
        }

        /// <summary>A mind that thinks for as long as the rail lets it (a gate), then answers.</summary>
        private sealed class SlowMind : IMind
        {
            public readonly ManualResetEventSlim Gate = new(false);
            public volatile bool Slow;
            public int Calls;
            public Func<Briefing, IReadOnlyList<CommanderAction>> Answer = _ => Array.Empty<CommanderAction>();
            public Task<IReadOnlyList<CommanderAction>> ThinkAsync(Briefing b, CancellationToken ct)
            {
                Interlocked.Increment(ref Calls);
                if (Slow) Gate.Wait(ct);
                return Task.FromResult(Answer(b));
            }
        }

        /// <summary>
        /// The cluster clock as the master runs it, standing in for it on the rail's node bus: a <c>PauseTimeIntent</c> stops sim time, a
        /// <c>ResumeTimeIntent</c> runs it; it reads what the frame published (after the swap), as the master does.
        /// </summary>
        private sealed class RailClock
        {
            public readonly Fdp.Core.FdpEventBus Bus = new();
            public bool Paused;
            public double SimTime;
            public int Pauses, Resumes;
            public RailClock()
            {
                Bus.RegisterManaged<Fdp.Toolkit.Time.Domain.PauseTimeIntent>();
                Bus.RegisterManaged<Fdp.Toolkit.Time.Domain.ResumeTimeIntent>();
            }
            public void EndFrame()
            {
                Bus.SwapBuffers();
                int p = Bus.ReadManaged<Fdp.Toolkit.Time.Domain.PauseTimeIntent>().Count, r = Bus.ReadManaged<Fdp.Toolkit.Time.Domain.ResumeTimeIntent>().Count;
                Pauses += p; Resumes += r;
                if (p > 0) Paused = true;
                if (r > 0) Paused = false;
                if (!Paused) SimTime += 0.016;
            }
            public void UserPause() => Bus.PublishManaged(new Fdp.Toolkit.Time.Domain.PauseTimeIntent());
        }

        private static void RunFor(Seated s, RailClock clock, TimeSpan wall, ref int frames)
        {
            var until = DateTime.UtcNow + wall;
            while (DateTime.UtcNow < until) { s.Tick(); clock.EndFrame(); frames++; Thread.Sleep(5); }
        }

        /// <summary>
        /// ⭐⭐ CE-4679 ① (L-2) — think-pause HOLDS THE CLOCK, not the frame: a mind that thinks for 10 s gets the clock paused through the one pause path
        /// (one <c>PauseTimeIntent</c> on the node bus); sim time does not advance while it thinks, yet frames go on (hundreds — the frame never blocks),
        /// and its answer resumes the clock and lands as orders. A fast mind (the opening snapshot) holds nothing.
        /// 🔴 Mutations: the hold skipped (the old frame-only wait at 0) ⇒ sim time runs while it thinks; the resume dropped ⇒ the clock stays paused.
        /// </summary>
        [Fact]
        public void CE4679_ASlowMind_HoldsTheSimClock_WhileFramesRunOn_AndItsAnswerResumesIt()
        {
            var clock = new RailClock();
            var mind = new SlowMind();
            var s = new Seated(mind, clockBus: clock.Bus);
            int frames = 0;
            for (int i = 0; i < 5; i++) { s.Tick(); clock.EndFrame(); frames++; }
            Assert.Equal(0, clock.Pauses);   // the opening snapshot answered at once: no hold

            mind.Slow = true;
            mind.Answer = _ => new[] { new CommanderAction(CommanderActionKind.Order, "Alpha", "Attack", Where: "house") };
            s.P.ReportSpot(s.P.Alpha, new Vector3(100f, 130f, 0f), 3);
            for (int i = 0; i < 3; i++) { s.Tick(); clock.EndFrame(); frames++; }   // the SPOT is said; the seat holds
            Assert.True(clock.Paused);
            double held = clock.SimTime;
            int before = frames;
            RunFor(s, clock, TimeSpan.FromSeconds(10), ref frames);   // the mind thinks for 10 s
            Assert.Equal(held, clock.SimTime);          // ⭐ sim time did not advance
            Assert.True(frames - before > 100, $"frames ran on: {frames - before}");
            Assert.Equal(0, s.P.OrdersTo(s.P.Alpha, "Order.Attack"));
            Assert.True(s.Seat.HoldsClock);

            mind.Gate.Set();
            for (int i = 0; i < 10 && clock.Paused; i++) { Thread.Sleep(20); s.Tick(); clock.EndFrame(); }
            s.Ticks(2);
            Assert.False(clock.Paused);                 // ⭐ the answer resumed it
            Assert.Equal(1, clock.Pauses);
            Assert.Equal(1, clock.Resumes);
            Assert.Equal(1, s.P.OrdersTo(s.P.Alpha, "Order.Attack"));
            s.Seats.Dispose();
        }

        /// <summary>
        /// ⭐⭐ CE-4679 ① — the hold never un-pauses a USER's pause: a pause asked for while the mind thinks outlives the hold (no resume when it answers),
        /// and a clock the user had paused BEFORE the wake is never paused or resumed by the seat at all.
        /// 🔴 Mutation: the user-pause check dropped ⇒ the seat resumes the clock the user paused.
        /// </summary>
        [Fact]
        public void CE4679_AUserPauseDuringTheHold_OutlivesIt()
        {
            var clock = new RailClock();
            var mind = new SlowMind();
            var s = new Seated(mind, clockBus: clock.Bus);
            for (int i = 0; i < 5; i++) { s.Tick(); clock.EndFrame(); }
            mind.Slow = true;
            s.P.ReportSpot(s.P.Alpha, new Vector3(100f, 130f, 0f), 3);
            for (int i = 0; i < 5; i++) { s.Tick(); clock.EndFrame(); }
            Assert.True(clock.Paused);
            Assert.Equal(1, clock.Pauses);

            clock.UserPause();                          // ⏸ while the mind thinks
            for (int i = 0; i < 5; i++) { s.Tick(); clock.EndFrame(); }
            mind.Gate.Set();
            for (int i = 0; i < 20; i++) { Thread.Sleep(10); s.Tick(); clock.EndFrame(); }
            Assert.False(s.Seat.HoldsClock);            // the answer is in
            Assert.True(clock.Paused);                  // ⭐ and the user's pause stays
            Assert.Equal(0, clock.Resumes);
            s.Seats.Dispose();
        }

        /// <summary>
        /// ⭐⭐ CE-4679 ① — a STEPPED clock (a verify run, run-to, <c>/sim/step</c>) is never paused or resumed by a seat: a resume would switch the master to
        /// continuous and end the stepping. The seat only marks its hold (<see cref="CommanderSeats.AnyHoldsClock"/>), which the stepper reads to withhold
        /// its next step, and clears it when the mind answers.
        /// 🔴 Mutation: the free-running check dropped ⇒ the seat pauses and resumes the stepped clock.
        /// </summary>
        [Fact]
        public void CE4679_AStepperClock_IsNeverPausedOrResumedByASeat_TheHoldIsMarkedForTheStepper()
        {
            var clock = new RailClock();
            var mind = new SlowMind();
            var s = new Seated(mind, clockBus: clock.Bus);
            s.Seats.ClockRunsFree = () => false;   // the master is stepping
            for (int i = 0; i < 5; i++) { s.Tick(); clock.EndFrame(); }
            mind.Slow = true;
            s.P.ReportSpot(s.P.Alpha, new Vector3(100f, 130f, 0f), 3);
            for (int i = 0; i < 5; i++) { s.Tick(); clock.EndFrame(); }
            Assert.True(s.Seats.AnyHoldsClock);           // ⭐ the stepper's signal
            mind.Gate.Set();
            for (int i = 0; i < 20 && s.Seats.AnyHoldsClock; i++) { Thread.Sleep(10); s.Tick(); clock.EndFrame(); }
            Assert.False(s.Seats.AnyHoldsClock);
            Assert.Equal(0, clock.Pauses);                // ⭐ never a pause …
            Assert.Equal(0, clock.Resumes);               // ⭐ … and never a resume of a stepped clock
            s.Seats.Dispose();
        }

        /// <summary>
        /// ⭐⭐ CE-4680 B-1 (DESIGN_LLM_Commander §5b) — <c>run_play</c>: the mind runs <c>PlatoonAttack</c> on the house as the commander's own task and the
        /// PLAY's choreography follows — the support squad first, the assault only once support reports SET — not the mind's. A direct squad order while
        /// it runs ENDS the play first (said in an <c>order-state</c>), and the squad takes the direct order once the play's orders are released —
        /// never released with them.
        /// 🔴 Mutations: the direct order not deferred (given in the play's run) ⇒ the play's release clears it; the play never published ⇒ no task.
        /// </summary>
        [Fact]
        public void CE4680_RunPlay_ThePlaysChoreographyRuns_ADirectOrderEndsItFirst_AndLands()
        {
            var mind = new RailMind();
            int call = 0;
            bool redirect = false;
            mind.Answer = b =>
            {
                if (Interlocked.Increment(ref call) == 1) return new[] { new CommanderAction(CommanderActionKind.RunPlay, Order: "Attack", Where: "house") };   // ⭐ CE-4685 — the KIND; the table runs PlatoonAttack
                return Volatile.Read(ref redirect) ? new[] { new CommanderAction(CommanderActionKind.Order, "Alpha", "Move", Where: "start") } : Array.Empty<CommanderAction>();
            };
            var s = new Seated(mind, squads: 3);
            s.Ticks(4);
            int play = Fdp.Toolkit.Behavior.BehaviorHash.FromName(BehaviorNames.PlatoonAttack);
            Assert.Equal(play, s.Repo.GetComponentRO<BehaviorState>(s.P.S.Commander).ActiveBehaviorHash);   // ⭐ the play is his task
            Assert.Contains(s.Items(), i => i.Json.Contains("\"state\":\"accepted\"") && i.Radio.Contains("Attack"));

            // the play's choreography (behaviors' code): the assault waits for the support squad's SET
            s.Ticks(3); s.P.S.Pause(PlatoonAttackBehavior.SurveyTimeoutSeconds + 0.5); s.Ticks(4);
            Assert.Equal(0, s.P.OrdersTo(s.P.Alpha, BehaviorNames.BoundingOverwatch));
            s.P.ReportSet(s.P.Bravo); s.Ticks(4);
            Assert.Equal(1, s.P.OrdersTo(s.P.Alpha, BehaviorNames.BoundingOverwatch));

            // a direct order mid-play: the play ends first, then Alpha takes the move — and keeps it
            Volatile.Write(ref redirect, true);
            s.P.ReportSpot(s.P.Charlie, new Vector3(120f, 100f, 0f), 2);   // a wake
            s.Ticks(8);
            Assert.Contains(s.Items(), i => i.Json.Contains("\"state\":\"play-ended\""));
            Assert.NotEqual(play, s.Repo.GetComponentRO<BehaviorState>(s.P.S.Commander).ActiveBehaviorHash);
            int move = s.P.S.Orders.FindLastIndex(o => o.Entity.Equals(s.P.Alpha) && o.IntentId == "Order.Move");
            Assert.True(move >= 0, "Alpha took the direct order");
            Assert.True(s.P.S.Orders.Count(o => o.Entity.Equals(s.P.Alpha) && o.IntentId == "Order.Move") == 1, "given once");
            // the play's release of Alpha (a clear naming BoundingOverwatch) came BEFORE the move, never after it
            var held = SubordinateOrders.Of(s.Repo, s.P.S.Commander);
            Assert.NotNull(held);
            Assert.Contains(held!.Orders, e => e.Member.Equals(s.P.Alpha) && e.Behavior == "Order.Move");
            s.Ticks(10);
            Assert.Contains(SubordinateOrders.Of(s.Repo, s.P.S.Commander)!.Orders, e => e.Member.Equals(s.P.Alpha) && e.Behavior == "Order.Move");
            s.Seats.Dispose();
        }

        /// <summary>
        /// ⭐⭐ CE-4679 (§3 "else on the heartbeat") — before any contact the mind is still asked again every heartbeat, though routine SITREPs keep
        /// coming: the heartbeat counts from the last WAKE, not from the last item. 📌 Found by the Haiku run: SITREPs every 30 s kept the 30 s
        /// heartbeat from ever firing, so an outside mind was asked once and the squads stood at the start.
        /// 🔴 Mutation: the heartbeat counted from the last item again ⇒ one call only.
        /// </summary>
        [Fact]
        public void CE4679_RoutineSitreps_NeverStopTheHeartbeat_TheMindIsAskedAgainBeforeContact()
        {
            var mind = new RailMind();
            var s = new Seated(mind);
            var cfg = s.Repo.GetComponentRO<CommanderSeat>(s.P.S.Commander);
            cfg.HeartbeatSeconds = 1f;
            s.Repo.SetComponent(s.P.S.Commander, cfg);
            s.Ticks(2);
            for (int i = 0; i < 12; i++)   // a SITREP from Alpha every 0.5 s for 6 s
            {
                s.P.Report(s.P.Alpha, new Fdp.Toolkit.Squad.Command.CommandReport { Kind = Fdp.Toolkit.Squad.Command.ReportKind.Sitrep });
                s.Ticks(31);
            }
            Thread.Sleep(100);
            Assert.Contains(s.Items(), i => i.Kind == FeedKind.Heartbeat);
            Assert.True(s.Seat.Runner!.Calls >= 4, $"the mind was asked {s.Seat.Runner.Calls} times in 6 s with a 1 s heartbeat");
            s.Seats.Dispose();
        }

        /// <summary>
        /// ⭐⭐ CE-4680 B-2 … B-6 (§5b) — what a decision needs, all from his scenario or a report to him: the MISSION and the places' terrain NOTES in
        /// the prefix; in the snapshot each OBJECTIVE his side may see (⛔ one hidden from Blue never appears), each squad's TASK and PROGRESS, and the
        /// enemy's ACTIVITY, EQUIPMENT and the report's AGE.
        /// 🔴 Mutations: the VisibleSides filter dropped ⇒ the Red-only objective is listed; the task lookup dropped ⇒ no "Attack → house".
        /// </summary>
        [Fact]
        public void CE4680_TheBriefing_CarriesTheMission_PlaceNotes_Objectives_SquadTasks_AndTheEnemysDetail()
        {
            var mind = new RailMind();
            int call = 0;
            mind.Answer = _ => Interlocked.Increment(ref call) == 1
                ? new[] { new CommanderAction(CommanderActionKind.Order, "Alpha", "Attack", Where: "house") }
                : Array.Empty<CommanderAction>();
            var s = new Seated(mind);   // the seat opens on the first tick: the brief and notes are set before it
            var repo = s.Repo;
            if (!repo.IsComponentTypeRegistered<CommanderBrief>()) repo.RegisterComponent<CommanderBrief>();
            if (!repo.IsComponentTypeRegistered<PlaceNote>()) repo.RegisterComponent<PlaceNote>();
            repo.AddComponent(s.P.S.Commander, new CommanderBrief
            {
                Task = new FixedString128("seize the house"), Purpose = new FixedString128("open the road"), Time = new FixedString128("by 00:06"),
            });
            foreach (var e in repo.Query().With<TkbIdentity>().With<EntityInfo>().Build())
                if (repo.GetComponentRO<EntityInfo>(e).Name.ToString() == "house")
                    repo.AddComponent(e, new PlaceNote { Text = new FixedString128("sheds, one storey, open ground to the east") });
            if (!repo.IsComponentTypeRegistered<Fdp.Toolkit.ScenarioRules.ObjectiveStatus>()) repo.RegisterComponent<Fdp.Toolkit.ScenarioRules.ObjectiveStatus>();
            void Objective(string title, ForceId visibleTo)
            {
                var o = repo.CreateEntity();
                repo.AddComponent(o, new Fdp.Toolkit.ScenarioRules.ObjectiveStatus
                {
                    Role = Fdp.Toolkit.ScenarioRules.ObjectiveRole.Objective, State = Fdp.Toolkit.ScenarioRules.ObjectiveState.Active,
                    Owner = ForceId.Friend, Title = new FixedString64(title), VisibleSides = (byte)(1 << (int)visibleTo), ProgressCur = 1, ProgressNeed = 3,
                });
            }
            Objective("Take the house", ForceId.Friend);
            Objective("Hold the sheds", ForceId.Hostile);   // Red's own — never Blue's
            var cfg = repo.GetComponentRO<CommanderSeat>(s.P.S.Commander);
            cfg.SnapshotSeconds = 0.5f;
            repo.SetComponent(s.P.S.Commander, cfg);

            s.Ticks(4);
            string prefix = s.Seat.Prefix;
            Assert.Contains("Mission — Task: seize the house · Purpose: open the road · Time: by 00:06", prefix);   // B-2
            Assert.Contains("house: sheds, one storey, open ground to the east", prefix);                          // B-6

            s.P.ReportSet(s.P.Alpha);
            s.P.Report(s.P.Bravo, new Fdp.Toolkit.Squad.Command.CommandReport
            {
                Kind = Fdp.Toolkit.Squad.Command.ReportKind.Spot, At = new Vector3(100f, 130f, 0f), Count = 3, Size = Fdp.Toolkit.Squad.Command.ElementSize.Team,
                Activity = Fdp.Toolkit.Squad.Command.ElementActivity.Firing, Equipment = Fdp.Toolkit.Squad.Command.ElementEquipment.MachineGun,
            }, observedAgo: 20f);
            s.Ticks(40);
            var spot = s.Items().Last(i => i.Kind == FeedKind.Report && i.Radio.Contains("spot"));
            Assert.Contains("Firing, MachineGun, 20 s old", spot.Radio);                                            // B-5
            var snap = s.Items().Last(i => i.Kind == FeedKind.Snapshot).Radio;
            Assert.Contains("OBJECTIVES: Take the house Active 1/3", snap);                                         // B-3
            Assert.DoesNotContain("Hold the sheds", snap);
            Assert.Contains("ALPHA at", snap);
            Assert.Contains("Attack → house", snap);                                                                // B-4
            Assert.Contains("Set", snap);
            s.Seats.Dispose();
        }

        /// <summary>
        /// ⭐⭐ CE-4680 B-2 / B-6 — the mission and the place notes LOAD with the scenario and SURVIVE a save: <c>llm-platoon-attack</c> read by the
        /// product's scenario serializer gives the Platoon its <see cref="CommanderBrief"/> and the house its <see cref="PlaceNote"/>, and serializing
        /// the world writes both back. 📌 Why this rail exists: as managed records they never loaded (no translator) — the briefing rail set them by hand
        /// and stayed green while the Haiku commander read a briefing with no mission.
        /// 🔴 Mutation: the brief made managed again (no translator) ⇒ nothing loads.
        /// </summary>
        [Fact]
        public void CE4680_TheMissionAndPlaceNotes_LoadWithTheScenario_AndSurviveASave()
        {
            var repo = new EntityRepository();
            Hrot.Map.Common.HrotSharedComponentRegistry.RegisterAll(repo);
            Hrot.CGF.CgfComponentRegistry.RegisterAll(repo);
            var serializer = Hrot.SimHost.Serializers.HrotScenarioSerializerFactory.Build(new Fdp.Toolkit.Behavior.BehaviorRegistry(), null, Fdp.Toolkit.Scenario.UnknownComponentPolicy.WarnAndSkip);
            string json = System.IO.File.ReadAllText(System.IO.Path.Combine(Fdp.Testing.TestRepo.Root, "scenarios", "demos", "platoons-and-vehicles", "llm-platoon-attack", "scenario.json"));
            serializer.Deserialize(repo, json);
            Entity platoon = default, house = default;
            foreach (var e in repo.Query().With<EntityInfo>().Build())
            {
                string n = repo.GetComponentRO<EntityInfo>(e).Name.ToString();
                if (n == "Platoon") platoon = e;
                if (n == "house" && repo.HasComponent<TkbIdentity>(e)) house = e;
            }
            Assert.True(repo.HasComponent<CommanderBrief>(platoon), "the Platoon's CommanderBrief loaded");
            Assert.StartsWith("Mission — Task: seize the house", repo.GetComponentRO<CommanderBrief>(platoon).Line());
            Assert.True(repo.HasComponent<PlaceNote>(house), "the house's PlaceNote loaded");
            Assert.StartsWith("the sheds, one storey", repo.GetComponentRO<PlaceNote>(house).Text.ToString());

            var saved = serializer.Serialize(repo, new Fdp.Toolkit.Scenario.ScenarioHeader("Hrot.Scenario")).ToJsonString();
            Assert.Contains("\"CommanderBrief\"", saved);
            Assert.Contains("seize the house", saved);
            Assert.Contains("\"PlaceNote\"", saved);
        }
    }
}
