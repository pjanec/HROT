using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Squad.Command;
using Hrot.CGF.Commander;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4684</c> (DESIGN_LLM_Commander §5c, L-3c) — the decision contract: one schema-decoded decision per wake (C-1), orders with their own timing
    /// and contingencies run by HROT (C-2), the model's own next decision and the cycle echo (C-3), terrain facts (C-4), enemy confidence (C-5), ids only (C-6).
    /// </summary>
    public partial class SquadManeuverTests
    {
        private static readonly SeatVocabulary Vocab = new("Platoon", new[] { "Alpha", "Bravo" }, new[] { "house", "objective", "support", "start" });

        /// <summary>A mind that answers with decisions the rail makes, and records the briefings.</summary>
        private sealed class DecisionRailMind : IMind, IDecisionMind
        {
            public readonly List<Briefing> Seen = new();
            public Func<Briefing, Decision?> Answer = _ => null;
            public bool AnswersWithDecisions => true;
            public Task<IReadOnlyList<CommanderAction>> ThinkAsync(Briefing b, CancellationToken ct)
            {
                lock (Seen) Seen.Add(b);
                return Task.FromResult<IReadOnlyList<CommanderAction>>(Answer(b) is { } d
                    ? new[] { new CommanderAction(CommanderActionKind.Decision) { Plan = d, Vocabulary = b.Vocabulary } }
                    : Array.Empty<CommanderAction>());
            }
            public List<Briefing> Briefings() { lock (Seen) return Seen.ToList(); }
        }

        private static Decision D(long cycle, IReadOnlyList<UnitOrder> orders, IReadOnlyList<Contingency>? contingencies = null, int next = 60,
                                  IReadOnlyList<Condition>? wake = null)
            => new(cycle, "s", "e", "r", "Alpha", "x", "y", orders, contingencies ?? Array.Empty<Contingency>(), next, wake ?? Array.Empty<Condition>());

        private static UnitOrder O(string unit, string task, string? objective = null, Condition? start = null, Condition? complete = null, params Condition[] abort)
            => new(unit, task, objective, null, start, complete, abort, "p");

        /// <summary>The decision object as a model writes it.</summary>
        private static string DecisionText(long cycle, string orders = "[]", string contingencies = "[]", int next = 60)
            => "{\"cycle\":" + cycle + ",\"assessment\":{\"situation\":\"Red holds the sheds\",\"enemy_likely_coa\":\"defend\",\"main_risk\":\"open ground\"},"
               + "\"intent\":{\"main_effort\":\"Alpha\",\"scheme\":\"fix and assault\",\"end_state\":\"sheds taken\"},\"orders\":" + orders
               + ",\"contingencies\":" + contingencies + ",\"next_decision\":{\"not_later_than_s\":" + next + ",\"wake_on\":[]}}";

        private static void Place(Seated s, Entity squad, float x, float y)
        {
            var t = new SimTransform { Position = new Vector3(x, y, 0f), Rotation = Quaternion.Identity };
            if (s.Repo.HasComponent<SimTransform>(squad)) s.Repo.SetComponent(squad, t); else s.Repo.AddComponent(squad, t);
        }

        private static void Until(Seated s, Func<bool> done, int maxTicks = 600)
        {
            for (int i = 0; i < maxTicks && !done(); i++) { s.Tick(); if (i % 4 == 0) Thread.Sleep(1); }
        }

        // ── C-1 — one decision, its schema, validate · repair once · CONTINUE ───────────────────────────────────────────────────

        /// <summary>
        /// ⭐ C-1 — on the seat: the request carries the seat's SCHEMA as <c>format</c> (its id fields are enums of the seat's ids — C-6) and NO tools; the
        /// user message opens with the CYCLE; the decision's order lands on the squad. 🔴 Red-proof: the decision never applied ⇒ no Move.
        /// </summary>
        [Fact]
        public void CE4684_ADecision_IsSentWithTheSeatsSchema_AndItsOrderLands()
        {
            using var server = new StandInChatServer((req, n) => StandInChatServer.Text(
                DecisionText(StandInChatServer.CycleOf(req), "[{\"unit\":\"Alpha\",\"task\":\"Move\",\"objective\":\"objective\",\"purpose\":\"close in\"}]")));
            using var mind = Chat(server, decisions: true);
            var s = new Seated(mind);
            var sw = Stopwatch.StartNew();
            while (s.P.OrdersTo(s.P.Alpha, "Order.Move") == 0 && sw.Elapsed < TimeSpan.FromSeconds(10)) { s.Tick(); Thread.Sleep(5); }
            Assert.Equal(1, s.P.OrdersTo(s.P.Alpha, "Order.Move"));
            Assert.True(s.Seat.Contract);
            var request = server.Requests[0];
            Assert.Null(request["tools"]);
            var unitEnum = ((JsonArray)request["format"]!["properties"]!["orders"]!["items"]!["properties"]!["unit"]!["enum"]!).Select(x => (string?)x).ToArray();
            Assert.Equal(new[] { "Platoon", "Alpha", "Bravo" }, unitEnum);
            var placeEnum = ((JsonArray)request["format"]!["properties"]!["orders"]!["items"]!["properties"]!["objective"]!["enum"]!).Select(x => (string?)x).ToArray();
            Assert.Equal(new[] { "house", "objective", "support", "start" }, placeEnum);
            Assert.StartsWith("CYCLE ", (string)((JsonArray)request["messages"]!)[1]!["content"]!);
            Assert.Contains("Places (ids — use exactly these)", s.Seat.Prefix);   // C-6
            Assert.DoesNotContain("N m <dir>", s.Seat.Prefix);
            Assert.Equal(1, s.Act.Decisions);
            s.Seats.Dispose();
        }

        /// <summary>
        /// ⭐ C-1 — an unknown id AND a wrong cycle echo: refused with BOTH errors named, repaired ONCE, the repaired decision taken.
        /// 🔴 Red-proof: no repair (fall back at once) ⇒ one request, nothing taken.
        /// </summary>
        [Fact]
        public void CE4684_AnUnknownIdAndAWrongCycle_AreRepairedOnce()
        {
            using var server = new StandInChatServer(
                StandInChatServer.Text(DecisionText(7, "[{\"unit\":\"Charlie\",\"task\":\"Move\",\"objective\":\"house\",\"purpose\":\"p\"}]")),
                StandInChatServer.Text(DecisionText(1, "[{\"unit\":\"Bravo\",\"task\":\"Move\",\"objective\":\"house\",\"purpose\":\"p\"}]")));
            using var mind = Chat(server, decisions: true);
            var actions = mind.ThinkAsync(ChatBriefing(10, "00:00:10 ALPHA: spot") with { Vocabulary = Vocab }, default).GetAwaiter().GetResult();
            Assert.Equal(2, server.Requests.Count);
            var retry = (JsonArray)server.Requests[1]["messages"]!;
            string complaint = (string)retry[^1]!["content"]!;
            Assert.Contains("REFUSED", complaint);
            Assert.Contains("'Charlie'", complaint);
            Assert.Contains("it must be 1", complaint);
            Assert.True(mind.LastRepaired);
            var d = Assert.Single(actions, a => a.Kind == CommanderActionKind.Decision).Plan!;
            Assert.Equal("Bravo", d.Orders[0].Unit);
            Assert.Contains(actions, a => a.Kind == CommanderActionKind.Note && a.Text!.StartsWith("ASSESSMENT Red holds the sheds"));   // the estimate
        }

        /// <summary>
        /// ⭐ C-1 — a task the unit cannot do (the whole unit's order given to a squad — CE-4685: Bound, a kind only the platoon takes), then a malformed object: repaired once, still bad ⇒ CONTINUE (nothing).
        /// 🔴 Red-proof: the task-fits-the-unit check dropped ⇒ the first answer is taken.
        /// </summary>
        [Fact]
        public void CE4684_ATaskTheSquadCannotDo_ThenAMalformedAnswer_FallsBackToContinue()
        {
            using var server = new StandInChatServer(
                StandInChatServer.Text(DecisionText(1, "[{\"unit\":\"Alpha\",\"task\":\"Bound\",\"objective\":\"house\",\"purpose\":\"p\"}]")),
                StandInChatServer.Text("{ not json"));
            using var mind = Chat(server, decisions: true);
            var actions = mind.ThinkAsync(ChatBriefing(10, "00:00:10 ALPHA: spot") with { Vocabulary = Vocab }, default).GetAwaiter().GetResult();
            Assert.Empty(actions);
            Assert.Equal(2, server.Requests.Count);
            Assert.True(mind.LastFellBack);
            Assert.Contains(mind.LastValidation!, p => p.Contains("Bound is the whole unit's order"));
            Assert.Contains(mind.LastValidation!, p => p.Contains("not one JSON object"));
        }

        /// <summary>⭐ C-6 — a position said as a bearing ("50 m N of house") is not an id: refused. The schema's id fields are the seat's lists.</summary>
        [Fact]
        public void CE4684_IdsOnly_ABearingIsNotAPlace()
        {
            var problems = new List<string>();
            Assert.Null(DecisionContract.Read(DecisionText(3, "[{\"unit\":\"Alpha\",\"task\":\"Move\",\"objective\":\"50 m N of house\",\"purpose\":\"p\"}]"), Vocab, 3, problems));
            Assert.Contains(problems, p => p.Contains("'50 m N of house' is not a place id"));
            var schema = DecisionContract.Schema(Vocab);
            var cond = schema["properties"]!["contingencies"]!["items"]!["properties"]!["trigger"]!["properties"]!;
            Assert.Equal(new[] { "Alpha", "Bravo" }, ((JsonArray)cond["unit"]!["enum"]!).Select(x => (string?)x).ToArray());
            Assert.Equal(Vocab.Places, ((JsonArray)cond["at"]!["enum"]!).Select(x => (string?)x).ToArray());
        }

        // ── C-2 — timing and contingencies, no model call ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐ C-2 — an order with <c>start: UNIT_AT Alpha objective</c> WAITS (no order), then goes when Alpha is there — said as STARTED; no model call.
        /// 🔴 Red-proof: the watcher starts every order at once ⇒ Bravo ordered before Alpha arrives.
        /// </summary>
        [Fact]
        public void CE4684_AnOrderWaitsForItsStart_ThenGoes()
        {
            var mind = new DecisionRailMind();
            int calls = 0;
            mind.Answer = b => calls++ == 0
                ? D(b.Cycle, new[] { O("Alpha", "Move", "objective"), O("Bravo", "Move", "house", start: new Condition(ConditionOn.UnitAt, "Alpha", "objective")) })
                : null;
            var s = new Seated(mind);
            Place(s, s.P.Alpha, 0, 0);
            Until(s, () => s.P.OrdersTo(s.P.Alpha, "Order.Move") > 0);
            s.Ticks(20);
            Assert.Equal(1, s.P.OrdersTo(s.P.Alpha, "Order.Move"));
            Assert.Equal(0, s.P.OrdersTo(s.P.Bravo, "Order.Move"));    // ⭐ waits
            int callsBefore = mind.Briefings().Count;
            Place(s, s.P.Alpha, 90, 80);                                 // Alpha at the objective
            Until(s, () => s.P.OrdersTo(s.P.Bravo, "Order.Move") > 0, 30);
            Assert.Equal(1, s.P.OrdersTo(s.P.Bravo, "Order.Move"));    // ⭐ then goes
            Assert.Contains(s.Items(), i => i.Kind == FeedKind.OrderState && i.Radio.Contains("Bravo Move → house (start: UNIT_AT Alpha objective): STARTED") && !i.Wake);
            Assert.Equal(1, s.Seat.Watch.Started);
            Assert.Equal(callsBefore, mind.Briefings().Count);           // no model call for it
            s.Seats.Dispose();
        }

        /// <summary>
        /// ⭐ C-2 — a contingency fires on its trigger (CONTACT Alpha) within a frame of the report reaching the picture, gives its order, and the NEXT
        /// briefing says so; an <c>abort_when</c> calls Alpha's order off (Alpha holds). 🔴 Red-proof: the contingency never evaluated ⇒ no Bravo order.
        /// </summary>
        [Fact]
        public void CE4684_AContingencyFires_AndAnAbortHolds_AndTheNextBriefingSaysSo()
        {
            var mind = new DecisionRailMind();
            int calls = 0;
            mind.Answer = b => calls++ == 0
                ? D(b.Cycle, new[] { O("Alpha", "Attack", "house", abort: new Condition(ConditionOn.UnitAt, "Bravo", "support")) },
                    new[] { new Contingency(new Condition(ConditionOn.Contact, "Alpha"), new[] { O("Bravo", "SupportByFire", "house") }) })
                : null;
            var s = new Seated(mind);
            Place(s, s.P.Bravo, 0, 0);
            Until(s, () => s.P.OrdersTo(s.P.Alpha, "Order.Attack") > 0);
            Assert.Equal(1, s.P.OrdersTo(s.P.Alpha, "Order.Attack"));
            Assert.Equal(0, s.Seat.Watch.Fired);

            s.P.ReportSpot(s.P.Alpha, new Vector3(100f, 110f, 0f), 2);
            int ticks = 0;
            while (s.Seat.Watch.Fired == 0 && ticks < 5) { s.Tick(); ticks++; }
            Assert.Equal(1, s.Seat.Watch.Fired);
            Assert.True(ticks <= 3, $"fired after {ticks} frames");      // the report's ingress frame, then the watcher's
            Until(s, () => s.P.OrdersTo(s.P.Bravo, "Order.Attack") > 0, 10);
            Assert.Equal(1, s.P.OrdersTo(s.P.Bravo, "Order.Attack"));   // SupportByFire = Attack, held back (R-8303)
            Until(s, () => mind.Briefings().Any(b => b.New.Any(i => i.Radio.Contains("CONTINGENCY FIRED — CONTACT Alpha: Bravo SupportByFire → house"))), 200);
            Assert.Contains(mind.Briefings(), b => b.New.Any(i => i.Radio.Contains("CONTINGENCY FIRED — CONTACT Alpha") && i.Wake));

            Place(s, s.P.Bravo, 60, 60);                                   // Bravo at support ⇒ Alpha's order is called off
            Until(s, () => s.Seat.Watch.Aborted > 0, 10);
            Until(s, () => s.P.OrdersTo(s.P.Alpha, "Order.Hold") > 0, 10);
            Assert.Equal(1, s.P.OrdersTo(s.P.Alpha, "Order.Hold"));
            Assert.Contains(s.Items(), i => i.Radio.Contains("Alpha Attack → house: ABORTED — UNIT_AT Bravo support; Alpha holds") && i.Wake);
            s.Seats.Dispose();
        }

        // ── C-3 — the cycle echo and the model's own pace ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐ C-3 — news arrives while the model thinks: its decision (on the older cycle) is refused, nothing of it lands, and its decision on the
        /// NEWER cycle lands — once: the refusal is not a wake, so it cannot make that one stale too. 📌 The first L-3c relay run refused 15 in a row
        /// (each refusal a wake that staled the next answer). 🔴 Red-proofs: the cycle check dropped ⇒ Alpha's stale Move lands; the refusal a wake ⇒ the
        /// newer decision is refused too (no Bravo Move).
        /// </summary>
        [Fact]
        public void CE4684_AStaleCycle_IsRefused_AndTheDecisionOnTheNewerCycleLands_NoLoop()
        {
            var mind = new DecisionRailMind();
            var gate = new ManualResetEventSlim(false);
            int calls = 0;
            mind.Answer = b =>
            {
                int n = Interlocked.Increment(ref calls);
                if (n == 1) return D(b.Cycle, Array.Empty<UnitOrder>());                       // the opening snapshot
                if (n == 2) { gate.Wait(TimeSpan.FromSeconds(10)); return D(b.Cycle, new[] { O("Alpha", "Move", "objective") }); }   // thinks while news comes
                if (n == 3) return D(b.Cycle, new[] { O("Bravo", "Move", "objective") });     // on the newer cycle
                return null;
            };
            var s = new Seated(mind);
            Until(s, () => s.Act.Decisions == 1, 300);
            s.P.ReportSpot(s.P.Alpha, new Vector3(100f, 110f, 0f), 2);                       // a wake: call 2 begins and holds
            Until(s, () => Volatile.Read(ref calls) >= 2, 300);
            s.P.ReportSpot(s.P.Bravo, new Vector3(100f, 120f, 0f), 3);                       // NEWER news while it thinks
            long before = s.Seat.Channel.LatestWake;
            Until(s, () => s.Seat.Channel.LatestWake > before, 30);
            gate.Set();
            Until(s, () => s.P.OrdersTo(s.P.Bravo, "Order.Move") > 0, 600);
            s.Ticks(60);
            Assert.Equal(0, s.P.OrdersTo(s.P.Alpha, "Order.Move"));    // ⭐ the stale decision: nothing lands
            Assert.Equal(1, s.P.OrdersTo(s.P.Bravo, "Order.Move"));    // ⭐ the newer one lands
            Assert.Equal(1, s.Act.StaleDecisions);                      // ⭐ once — no loop
            Assert.Contains(s.Items(), i => i.Radio.Contains("REFUSED — newer news came since") && !i.Wake);
            s.Seats.Dispose();
        }

        /// <summary>
        /// ⭐ C-3 — the model's own <c>not_later_than</c> (12 s) wakes it, not the fixed heartbeat (60 s here); a <c>wake_on</c> condition wakes it sooner.
        /// 🔴 Red-proof: the next decision ignored (the fixed heartbeat only) ⇒ no wake at 12 s.
        /// </summary>
        [Fact]
        public void CE4684_TheModelsOwnDeadline_AndItsWakeCondition_WakeIt()
        {
            var mind = new DecisionRailMind();
            int calls = 0;
            mind.Answer = b => calls++ == 0 ? D(b.Cycle, Array.Empty<UnitOrder>(), next: 12) : calls == 2
                ? D(b.Cycle, Array.Empty<UnitOrder>(), next: 300, wake: new[] { new Condition(ConditionOn.UnitAt, "Alpha", "objective") })
                : null;
            var s = new Seated(mind);
            Fast(s, heartbeat: 60f, snapshot: 600f);
            Place(s, s.P.Alpha, 0, 0);
            Run(s, 900);   // 15 s of sim time
            var deadline = s.Items().FirstOrDefault(i => i.Kind == FeedKind.Heartbeat && i.Radio.Contains("your decision time"));
            Assert.NotNull(deadline);
            Assert.InRange(deadline!.SimTime, 11.5, 13.5);
            Place(s, s.P.Alpha, 90, 80);
            Until(s, () => s.Seat.Watch.Woke > 0, 10);
            Assert.Contains(s.Items(), i => i.Radio.Contains("WAKE — your condition UNIT_AT Alpha objective holds") && i.Wake);
            s.Seats.Dispose();
        }

        // ── C-4 — terrain facts ──────────────────────────────────────────────────────────────────────────────────────────────────

        private sealed class FakeSight : ILosService
        {
            public Func<Vector3, Vector3, bool> Clear = (_, _) => true;
            public bool HasLineOfSight(Vector3 eye, Vector3 aim) => Clear(eye, aim);
        }

        private sealed class FakeCover : ICoverProvider
        {
            public Func<Vector2, int> Count = _ => 0;
            public int GetCoverPointsInRadius(Vector2 center, float radius, Span<CoverPoint> results) => Math.Min(Count(center), results.Length);
        }

        private sealed class FakeNav : INavmeshProvider
        {
            public bool IsWalkable(Vector3 position, uint layerMask = 0xFFFFFFFF) => true;
            public bool ProjectToNavmesh(Vector3 position, out Vector3 snapped, uint layerMask = 0xFFFFFFFF)
            {
                // the vehicle layer lies west of x = 50 only
                snapped = layerMask == (uint)NavLayerMask.Vehicle && position.X > 50 ? new Vector3(0, position.Y, 0) : position;
                return true;
            }
            public int SampleNavmeshPoints(Vector3 center, float radius, Span<Vector3> results, uint layerMask = 0xFFFFFFFF) => 0;
            public bool PathExists(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF) => true;
            public float PathCost(Vector3 from, Vector3 to, uint layerMask = 0xFFFFFFFF) => Vector3.Distance(from, to);
            public uint QueryVersion() => 1;
            public int PlanPath(Vector3 from, Vector3 to, Span<NavWaypoint> waypoints, uint layerMask = 0xFFFFFFFF) => 0;
        }

        /// <summary>
        /// ⭐ C-4 — per place: who sees it (A and C cannot see each other), its cover (≥ 4 points covered · 1–3 partial · a building covered), whether a
        /// vehicle reaches it, and minutes on foot to its neighbours. 🔴 Red-proof: the building check dropped ⇒ C reads "open".
        /// </summary>
        [Fact]
        public void CE4684_TerrainFacts_SayWhoSeesEachPlace_ItsCover_VehiclesAndMinutes()
        {
            var places = new List<(string, Vector2)> { ("A", new Vector2(0, 0)), ("B", new Vector2(100, 0)), ("C", new Vector2(0, 100)) };
            static bool Near(Vector3 p, float x, float y) => Vector2.Distance(new Vector2(p.X, p.Y), new Vector2(x, y)) <= 10f;
            var sight = new FakeSight { Clear = (e, a) => !((Near(e, 0, 0) && Near(a, 0, 100)) || (Near(e, 0, 100) && Near(a, 0, 0))) };
            var cover = new FakeCover { Count = c => c.X < 1 && c.Y < 1 ? 5 : c.X > 99 ? 1 : 0 };
            var facts = TerrainFacts.Compute(places, _ => 0f, sight, new FakeNav(), cover, p => p.Y > 99);
            Assert.Equal("seen from: B · cover: covered · vehicles: yes · on foot: B 1 min, C 1 min", facts["A"]);
            Assert.Equal("seen from: A, C · cover: partial · vehicles: no · on foot: A 1 min, C 1.5 min", facts["B"]);
            Assert.Equal("seen from: B · cover: covered (a building) · vehicles: yes · on foot: A 1 min, B 1.5 min", facts["C"]);
            Assert.Empty(TerrainFacts.Compute(places, _ => 0f, null, null, null, _ => false));   // no services ⇒ nothing guessed
        }

        /// <summary>
        /// ⭐ C-4 — a place is judged where a man STANDS at it: a spot behind a wall (hidden at its centre, open 2 m in front) reads NOT seen; a building
        /// (its centre not standable) is judged at the standable points around it. 📌 Found by the hand check on <c>llm-platoon-attack</c>: a 6 m ring read
        /// the objective (behind the Low Wall, "cover from the house") as seen from the house. 🔴 Red-proof: the old ring ⇒ "seen from: A".
        /// </summary>
        [Fact]
        public void CE4684_TerrainFacts_JudgeAPlaceWhereAManStands_NotARingAroundIt()
        {
            var places = new List<(string, Vector2)> { ("A", new Vector2(0, 0)), ("wall", new Vector2(100, 0)), ("shed", new Vector2(0, 100)) };
            // A sees nothing within 1 m of the wall spot; anything else is in the open
            var sight = new FakeSight { Clear = (e, a) => !(Vector2.Distance(new Vector2(e.X, e.Y), Vector2.Zero) < 5f && Vector2.Distance(new Vector2(a.X, a.Y), new Vector2(100, 0)) < 1f) };
            // the shed's centre is inside it (not standable within 3 m)
            var facts = TerrainFacts.Compute(places, p => Vector2.Distance(p, new Vector2(0, 100)) < 3f ? null : 0f, sight, null, null, p => p.Y > 99 && p.X < 3);
            Assert.StartsWith("seen from: shed", facts["wall"]);     // ⛔ not A: the spot behind the wall is hidden from it
            Assert.StartsWith("seen from: A, wall", facts["shed"]);  // the shed's walls are seen
        }

        // ── C-5 — enemy confidence ───────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐ C-5 — one squad's OLD report reads LOW, one squad's fresh report MED, two squads' fresh reports of the same enemy HIGH.
        /// 🔴 Red-proof: the second squad never counted ⇒ HIGH reads MED.
        /// </summary>
        [Fact]
        public void CE4684_EnemyConfidence_FromHowManySquadsReportedItAndHowLongAgo()
        {
            var pic = new CommandPicture();
            pic.Elements.Items[0] = new ReportedElement { At = new Vector3(100, 100, 0), FromNetId = 1, ObservedAt = new SimInstant(10) };
            pic.Elements.Count = 1;
            Assert.Equal("LOW", CommanderFeedSystem.Confidence(in pic, 0, now: 100));
            Assert.Equal("MED", CommanderFeedSystem.Confidence(in pic, 0, now: 40));
            pic.Elements.Items[1] = new ReportedElement { At = new Vector3(110, 105, 0), FromNetId = 2, ObservedAt = new SimInstant(95) };
            pic.Elements.Count = 2;
            Assert.Equal("HIGH", CommanderFeedSystem.Confidence(in pic, 0, now: 100));
            Assert.Equal("HIGH", CommanderFeedSystem.Confidence(in pic, 1, now: 100));
            pic.Elements.Items[1].At = new Vector3(300, 300, 0);   // far away: another enemy
            Assert.Equal("LOW", CommanderFeedSystem.Confidence(in pic, 0, now: 100));
        }
    }
}
