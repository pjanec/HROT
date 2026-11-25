using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using Hrot.CGF.Commander;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4683</c> (DESIGN_LLM_Commander §5a-4, L-3a) — the briefing a small model can live with: ONE running estimate, the orders in force
    /// (a repeat is a no-op), the plays preferred, and a per-call budget — the three findings of the L-3 Haiku relay run.
    /// </summary>
    public partial class SquadManeuverTests
    {
        private static void Fast(Seated s, float heartbeat, float snapshot)
        {
            var cfg = s.Repo.GetComponentRO<CommanderSeat>(s.P.S.Commander);
            cfg.HeartbeatSeconds = heartbeat;
            cfg.SnapshotSeconds = snapshot;
            s.Repo.SetComponent(s.P.S.Commander, cfg);
        }

        /// <summary>Ticks while giving the mind's thread room to answer each wake.</summary>
        private static void Run(Seated s, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                s.Tick();
                if (i % 8 == 0) Thread.Sleep(1);
            }
        }

        /// <summary>
        /// ⭐ ② — the SAME order again (another case: <c>"Objective"</c>) is a NO-OP: answered <i>"already in force since …"</i>, never re-sent to the squad;
        /// and every snapshot carries the ORDERS IN FORCE block, so a snapshot (which clears the radio log) never loses it.
        /// 🔴 Red-proof: <c>Same</c> always false ⇒ the Move is sent again.
        /// </summary>
        [Fact]
        public void CE4683_ARepeatedIdenticalOrder_IsANoOp_AndTheOrdersInForceSurviveASnapshot()
        {
            int call = 0;
            var mind = new RailMind();
            mind.Answer = _ => new[]
            {
                new CommanderAction(CommanderActionKind.Order, "Alpha", "Move", Where: call++ == 0 ? "objective" : "Objective"),
            };
            var s = new Seated(mind);
            Fast(s, heartbeat: 0.5f, snapshot: 1.5f);
            Run(s, 300);   // ~5 s: a dozen wakes, three snapshots
            Assert.Equal(1, s.P.OrdersTo(s.P.Alpha, "Order.Move"));
            Assert.True(s.Act.NoOps >= 2, $"{s.Act.NoOps} no-ops");
            var items = s.Items();
            Assert.Contains(items, i => i.Kind == FeedKind.OrderState && i.Radio.Contains("already in force since") && !i.Wake);
            var lastSnapshot = items.Last(i => i.Kind == FeedKind.Snapshot);
            Assert.Contains("ORDERS IN FORCE: ALPHA Move → objective since 00:00:00 (you)", lastSnapshot.Radio);
            s.Seats.Dispose();
        }

        /// <summary>
        /// ⭐ ①④ — a long run (a 900-character note EVERY call (past the cap), a SPOT every half second): the briefing holds ONE estimate — the last — capped at 800
        /// characters; no part of it grows with the number of calls; the whole request (system + user + tools) stays under the 4 000-token budget.
        /// 🔴 Red-proof: notes appended to the estimate ⇒ it grows past the cap and the budget.
        /// </summary>
        [Fact]
        public void CE4683_TheEstimateIsOne_Replaced_Capped_AndALongRunStaysUnderTheBudget()
        {
            int call = 0;
            var mind = new RailMind();
            mind.Answer = _ =>
            {
                int n = Interlocked.Increment(ref call);
                return new[] { new CommanderAction(CommanderActionKind.Note, Text: $"estimate #{n}: " + new string('x', 900)) };
            };
            var s = new Seated(mind);
            Fast(s, heartbeat: 0.5f, snapshot: 3f);
            for (int k = 0; k < 40; k++)   // 20 s of sim time
            {
                s.P.ReportSpot(k % 2 == 0 ? s.P.Alpha : s.P.Bravo, new Vector3(110f, 100f, 0f), 3);
                Run(s, 31);
            }
            Thread.Sleep(50);
            List<Briefing> seen;
            lock (mind.Seen) seen = mind.Seen.ToList();
            Assert.True(seen.Count >= 30, $"{seen.Count} calls");
            var last = seen[^1];
            var estimate = Assert.Single(last.Notes);
            Assert.StartsWith($"estimate #{seen.Count - 1}:", estimate);   // the previous call's note — replaced, not appended
            Assert.True(estimate.Length <= MindRunner.EstimateLimit, $"{estimate.Length} chars");
            Assert.EndsWith(MindRunner.Cut, estimate);

            // no part grows with the number of calls: the second half's largest briefing is no larger than the first half's (+ one snapshot's worth)
            int firstHalf = seen.Take(seen.Count / 2).Max(b => b.Changing().Length);
            int secondHalf = seen.Skip(seen.Count / 2).Max(b => b.Changing().Length);
            Assert.True(secondHalf <= firstHalf + 600, $"the briefing grew: {firstHalf} → {secondHalf} chars");
            int tokens = seen.Max(b => BriefingBudget.Tokens(BriefingBudget.CharsOf(b, null)));
            Assert.True(tokens < BriefingBudget.Limit, $"{tokens} tokens");
            s.Seats.Dispose();
        }

        /// <summary>
        /// ⭐ ③ — the prefix says it plainly (<i>PREFER run_play … a direct order ENDS the play</i>), <c>run_play</c> is the FIRST tool, and once a play
        /// runs the snapshot names the play in force. 🔴 Red-proof: the PLAY IN FORCE line dropped ⇒ no line.
        /// </summary>
        [Fact]
        public void CE4683_ThePlaysArePreferred_InThePrefixAndTheTools_AndTheSnapshotNamesThePlayInForce()
        {
            var mind = new RailMind();
            int call = 0;
            mind.Answer = _ => call++ == 0
                ? new[] { new CommanderAction(CommanderActionKind.RunPlay, Order: "Attack", Where: "house") }
                : Array.Empty<CommanderAction>();
            var s = new Seated(mind);
            Fast(s, heartbeat: 0.5f, snapshot: 1f);
            Run(s, 150);
            Assert.Contains("PREFER run_play", s.Seat.Prefix);
            Assert.Contains("an order to your whole platoon coordinates your squads", s.Seat.Prefix);   // ⭐ CE-4685 — the whole unit's order, by kind
            Assert.Contains("a direct order to one of them ENDS it", s.Seat.Prefix);
            Assert.Contains("with play Attack | Defend | Bound | Hold (your whole platoon)", s.Seat.Prefix);
            Assert.Equal("run_play", (string?)CommanderTools.Schema()[0]!["name"]);
            Assert.Contains("PREFER", (string?)CommanderTools.Schema()[0]!["description"]);
            Assert.Contains(s.Items(), i => i.Kind == FeedKind.Snapshot && i.Radio.Contains("PLATOON ORDER IN FORCE: Attack → house since"));
            s.Seats.Dispose();
        }

        /// <summary>
        /// ⭐ CE-4683 — asking a squad for a SITREP during a play does NOT end the play (it is not an order). 📌 Found by the L-3a Haiku relay run.
        /// 🔴 Red-proof: ask_sitrep back behind the play check ⇒ "PLATOON ORDER Attack ENDED".
        /// </summary>
        [Fact]
        public void CE4683_AskingForASitrep_DuringAPlay_DoesNotEndThePlay()
        {
            var mind = new RailMind();
            int call = 0;
            mind.Answer = _ => (call++) switch
            {
                0 => new[] { new CommanderAction(CommanderActionKind.RunPlay, Order: "Attack", Where: "house") },
                1 => new[] { new CommanderAction(CommanderActionKind.AskSitrep, "Alpha") },
                _ => Array.Empty<CommanderAction>(),
            };
            var s = new Seated(mind);
            Fast(s, heartbeat: 0.5f, snapshot: 60f);
            Run(s, 120);
            Assert.True(call >= 3, $"{call} calls");
            Assert.DoesNotContain(s.Items(), i => i.Radio.Contains("ENDED"));
            Assert.Contains(s.Items(), i => i.Kind == FeedKind.OrderState && i.Radio.Contains("AskSitrep Alpha") && i.Radio.Contains("given"));
            s.Seats.Dispose();
        }
    }
}
