using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Hrot.CGF.Commander;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ The duel's pre-test (DESIGN_LLM_Commander §8, §11 L-4 "a second seat") — TWO seats on one node, one free-running clock: each mind's think
    /// holds the clock; the clock runs again only when BOTH have answered, and never stays paused after that.
    /// </summary>
    public partial class SquadManeuverTests
    {
        /// <summary>Blue's seat (the fixture's platoon) and a second, Red seat on its own commander — each with its own slow mind.</summary>
        private static (Seated s, SlowMind blue, SlowMind red, Entity redCommander) TwoSeats(RailClock clock)
        {
            var blue = new SlowMind { Slow = true };
            var red = new SlowMind { Slow = true };
            var s = new Seated(blue, clockBus: clock.Bus);
            var rc = s.Repo.CreateEntity();
            s.Repo.AddComponent(rc, new EntityInfo { Name = new FixedString64("Red Platoon"), ForceId = ForceId.Hostile });
            Fdp.Toolkit.Squad.SquadStateProvisioning.EnsureForCommander(s.Repo, rc);
            s.Repo.AddComponent(rc, new CommanderSeat { Mind = CommanderMind.External, Mode = CommanderMode.ThinkPause });
            s.Seats.MindFor = seat => seat.Mind == CommanderMind.External ? red : blue;
            return (s, blue, red, rc);
        }

        private static void Frames(Seated s, RailClock clock, int n, int sleepMs = 0)
        {
            for (int i = 0; i < n; i++) { s.Tick(); clock.EndFrame(); if (sleepMs > 0) Thread.Sleep(sleepMs); }
        }

        /// <summary>
        /// ⭐⭐ Both seats wake on the same frame (the opening snapshot) and both think: the clock is paused; Red answers first — the clock STAYS paused
        /// while Blue still thinks; Blue answers — the clock RUNS again (and is never left paused by the two seats taking each other's pause for a user's).
        /// </summary>
        [Fact]
        public void TwoSeats_TheClockRunsAgain_OnlyWhenBothMindsHaveAnswered()
        {
            var clock = new RailClock();
            var (s, blue, red, rc) = TwoSeats(clock);
            Frames(s, clock, 5);
            Assert.True(s.Seats.TryGet(rc, out var redSeat), "the Red seat opened");
            Assert.True(clock.Paused, "both minds think: the clock is held");
            double held = clock.SimTime;

            red.Gate.Set();
            Frames(s, clock, 30, 5);
            Assert.False(redSeat.HoldsClock, "Red answered");
            Assert.True(s.Seat.HoldsClock, "Blue still thinks");
            Assert.True(clock.Paused, "⭐ one mind still thinks: the clock stays held");
            Assert.Equal(held, clock.SimTime);

            blue.Gate.Set();
            Frames(s, clock, 30, 5);
            Assert.False(s.Seats.AnyHoldsClock);
            Assert.False(clock.Paused, "⭐ both answered: the clock runs again");
            Assert.True(clock.SimTime > held);
            s.Seats.Dispose();
        }

        /// <summary>
        /// ⭐ A user's pause while both seats think still outlives both holds — the two seats' own pauses are not taken for the user's, and the user's is
        /// not taken for a seat's.
        /// </summary>
        [Fact]
        public void TwoSeats_AUserPauseDuringBothHolds_OutlivesThem()
        {
            var clock = new RailClock();
            var (s, blue, red, _) = TwoSeats(clock);
            Frames(s, clock, 5);
            Assert.True(clock.Paused);
            clock.UserPause();
            Frames(s, clock, 5);
            red.Gate.Set();
            blue.Gate.Set();
            Frames(s, clock, 30, 5);
            Assert.False(s.Seats.AnyHoldsClock);
            Assert.True(clock.Paused, "⭐ the user's pause stays");
            Assert.Equal(0, clock.Resumes);
            s.Seats.Dispose();
        }

        /// <summary>
        /// ⭐ Each seat is told only the places its side may know: a Neutral place is everybody's, a place drawn for a side is only that side's — Red is never
        /// told Blue's start (found by the duel's first briefing). And the play advice speaks to a defender too.
        /// 🔴 Red-proof: the side filter dropped ⇒ Red's prefix names "blue-start".
        /// </summary>
        [Fact]
        public void TwoSeats_EachSeatIsToldOnlyItsOwnSidesPlaces()
        {
            var clock = new RailClock();
            var (s, blue, red, rc) = TwoSeats(clock);
            blue.Slow = red.Slow = false;
            var bluePlace = s.Repo.CreateEntity();
            s.Repo.AddComponent(bluePlace, new TkbIdentity { TkbType = (TkbTypeId)Gazetteer.PointType });
            s.Repo.AddComponent(bluePlace, new EntityInfo { Name = new FixedString64("blue-start"), ForceId = ForceId.Friend });
            s.Repo.AddComponent(bluePlace, new SimTransform { Position = new System.Numerics.Vector3(5f, 5f, 0f), Rotation = System.Numerics.Quaternion.Identity });
            Frames(s, clock, 3);
            Assert.True(s.Seats.TryGet(rc, out var redSeat));
            Assert.Contains("blue-start", s.Seat.Prefix);                // Blue's own
            Assert.DoesNotContain("blue-start", redSeat.Prefix);         // ⭐ never the other side's
            Assert.Contains("house", redSeat.Prefix);                    // a Neutral place is everybody's
            Assert.Contains("Defend: they hold an area together", redSeat.Prefix);
            s.Seats.Dispose();
        }

        /// <summary>
        /// ⭐ The same play on the same place while it runs is a NO-OP (answered "already in force", never re-sent — re-sent it started the play over); and
        /// the snapshot says how old it is. 📌 Found by the duel: Blue re-ran PlatoonAttack at 01:05 trusting a 65 s old snapshot's "ORDERS IN FORCE: none".
        /// 🔴 Red-proof: the play no-op dropped ⇒ two PlatoonAttack intents on the commander.
        /// </summary>
        [Fact]
        public void Duel_ARepeatedPlayInForce_IsANoOp_AndTheSnapshotSaysItsAge()
        {
            var mind = new RailMind();
            mind.Answer = _ => new[] { new CommanderAction(CommanderActionKind.RunPlay, Order: "Attack", Where: "House") };
            var s = new Seated(mind);
            Fast(s, heartbeat: 0.5f, snapshot: 60f);
            Run(s, 200);
            Assert.Equal(1, s.P.S.Orders.Count(o => o.Entity.Equals(s.P.S.Commander) && o.IntentId == "Order.Attack"));   // ⭐ CE-4685 — the seat sends the KIND
            Assert.True(s.Act.NoOps >= 1, $"{s.Act.NoOps} no-ops");
            Assert.Contains(s.Items(), i => i.Kind == FeedKind.OrderState && i.Radio.Contains("PLATOON ORDER Attack → House: already in force since"));
            Assert.Contains(s.Items(), i => i.Kind == FeedKind.Heartbeat && i.Radio.Contains("the orders in force stand unless you change them"));   // D-6
            List<Briefing> seen;
            lock (mind.Seen) seen = mind.Seen.ToList();
            Assert.Contains(seen, b => b.Snapshot != null && b.SimTime - b.Snapshot.SimTime >= 1 && b.Changing().Contains(" s ago — the radio log below is newer)"));
            s.Seats.Dispose();
        }
    }
}
