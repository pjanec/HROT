using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Components;
using Hrot.CGF.Commander;
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
            public EntityRepository Repo => P.S.Repo;

            /// <summary>⭐ CE-4679 — the node bus the hold's pause / resume ride (null = the world's bus).</summary>
            public readonly Fdp.Core.FdpEventBus? ClockBus;

            public Seated(IMind mind, int squads = 2, Fdp.Core.FdpEventBus? clockBus = null)
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
                Feed = new CommanderFeedSystem(Seats);
                Act = new CommanderActionSystem(Seats, clockBus);
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
            public void Tick() { Act.Execute(Repo, 0.016f); P.Tick(); Feed.Execute(Repo, 0.016f); }
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
    }
}
