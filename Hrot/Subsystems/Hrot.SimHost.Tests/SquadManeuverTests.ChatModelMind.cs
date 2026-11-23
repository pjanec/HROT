using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Threading;
using Hrot.CGF.Commander;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4681</c> (DESIGN_LLM_Commander §5a-3, L-3 ①②③) — the chat-model mind against the stand-in chat server: the request it sends, the
    /// stable prefix, its answer turned into orders, and every way an answer can go wrong. On the command chain's own platoon fixture.
    /// </summary>
    public partial class SquadManeuverTests
    {
        private static Briefing ChatBriefing(double t, string newLine) =>
            new("Platoon", t, "You command Platoon.\nPLACES: house, objective, support, start\nSQUADS: Alpha, Bravo", null,
                new[] { new FeedItem(1, t, FeedKind.Report, true, newLine, "{}") }, Array.Empty<string>(),
                new[] { new FeedItem(1, t, FeedKind.Report, true, newLine, "{}") }, 1);

        /// <param name="decisions">⭐ CE-4684 — the decision contract (the default since L-3c); the L-3 rails pin L-3a's tool calls (<c>false</c>).</param>
        private static ChatModelMind Chat(StandInChatServer server, double timeoutS = 10, bool decisions = false)
            => new(new ChatModelSettings { Endpoint = server.Endpoint, Model = "stand-in", NumCtx = 12288, KeepAlive = "5m", Timeout = TimeSpan.FromSeconds(timeoutS), Decisions = decisions });

        /// <summary>
        /// ⭐ ① — on the seat: the request carries the model, the briefing (system = the prefix, user = what changed), the commander's TOOLS and
        /// <c>stream: false</c> / <c>num_ctx</c> / <c>keep_alive</c>; the answer's tool call becomes an ORDER on the squad at origin Superior.
        /// 🔴 Red-proof: the tool calls not read (Read returns nothing) ⇒ no order.
        /// </summary>
        [Fact]
        public void CE4681_TheChatMind_SendsTheBriefingAndTheTools_AndAToolCallBecomesAnOrder()
        {
            using var server = new StandInChatServer(StandInChatServer.Calls(("order", "{\"squad\":\"Alpha\",\"kind\":\"Move\",\"where\":\"objective\"}")));
            using var mind = Chat(server);
            var s = new Seated(mind);
            var sw = Stopwatch.StartNew();
            while (s.P.OrdersTo(s.P.Alpha, "Order.Move") == 0 && sw.Elapsed < TimeSpan.FromSeconds(10)) { s.Tick(); Thread.Sleep(5); }
            Assert.Equal(1, s.P.OrdersTo(s.P.Alpha, "Order.Move"));
            Assert.Equal(Fdp.Toolkit.Behavior.Components.BehaviorOrigin.Superior, s.P.S.Orders.Last(o => o.Entity.Equals(s.P.Alpha)).Origin);

            var request = server.Requests[0];
            Assert.Equal("stand-in", (string?)request["model"]);
            Assert.False((bool)request["stream"]!);
            Assert.Equal(12288, (int)request["options"]!["num_ctx"]!);
            Assert.Equal("5m", (string?)request["keep_alive"]);
            var messages = (JsonArray)request["messages"]!;
            Assert.Equal("system", (string?)messages[0]!["role"]);
            Assert.StartsWith(s.Seat.Prefix, (string)messages[0]!["content"]!);
            Assert.Equal("user", (string?)messages[1]!["role"]);
            Assert.Contains("SNAPSHOT", (string)messages[1]!["content"]!);
            var tools = ((JsonArray)request["tools"]!).Select(t => (string?)t!["function"]!["name"]).ToArray();
            Assert.Equal(new[] { "run_play", "order", "hold", "ask_sitrep", "stop_play", "note" }, tools);   // ⭐ CE-4683 ③ — the play first
            s.Seats.Dispose();
        }

        /// <summary>
        /// ⭐ ③ — two calls share the system message BYTE FOR BYTE (a server reuses its cache); only the user message changes.
        /// 🔴 Red-proof: the sim time put in the system message ⇒ the two differ.
        /// </summary>
        [Fact]
        public void CE4681_TwoCalls_ShareTheSystemMessageByteForByte()
        {
            using var server = new StandInChatServer(StandInChatServer.Text("ok"));
            using var mind = Chat(server);
            mind.ThinkAsync(ChatBriefing(10, "00:00:10 ALPHA: spot"), default).GetAwaiter().GetResult();
            mind.ThinkAsync(ChatBriefing(40, "00:00:40 BRAVO: sitrep"), default).GetAwaiter().GetResult();
            Assert.Equal(2, server.Requests.Count);
            string Msg(int r, int m) => (string)((JsonArray)server.Requests[r]["messages"]!)[m]!["content"]!;
            Assert.Equal(Msg(0, 0), Msg(1, 0));
            Assert.NotEqual(Msg(0, 1), Msg(1, 1));
        }

        /// <summary>
        /// ⭐ ② — a malformed call (arguments not JSON) and an unknown tool are sent back ONCE as tool messages; the good call of the first answer is kept,
        /// the second answer's good call is added, its bad one dropped — never a third request.
        /// 🔴 Red-proof: no retry ⇒ one request and no ask_sitrep.
        /// </summary>
        [Fact]
        public void CE4681_AMalformedOrUnknownCall_IsRetriedOnce_ThenDropped()
        {
            using var server = new StandInChatServer(
                StandInChatServer.Calls(("hold", "{\"squad\":\"Bravo\"}"), ("order", "not json at all"), ("fly", "{\"squad\":\"Alpha\"}")),
                StandInChatServer.Calls(("ask_sitrep", "{\"squad\":\"Alpha\"}"), ("teleport", "{}")));
            using var mind = Chat(server);
            var actions = mind.ThinkAsync(ChatBriefing(10, "00:00:10 ALPHA: spot"), default).GetAwaiter().GetResult();
            Assert.Equal(new[] { CommanderActionKind.Hold, CommanderActionKind.AskSitrep }, actions.Select(a => a.Kind).ToArray());
            Assert.Equal(2, server.Requests.Count);
            var retry = (JsonArray)server.Requests[1]["messages"]!;
            Assert.Equal("assistant", (string?)retry[2]!["role"]);
            var toolMessages = retry.Where(m => (string?)m!["role"] == "tool").Select(m => (string)m!["content"]!).ToArray();
            Assert.Equal(2, toolMessages.Length);
            Assert.All(toolMessages, m => Assert.StartsWith("error:", m));
            Assert.Contains(toolMessages, m => m.Contains("'fly' is not a tool", StringComparison.Ordinal));
            Assert.True(mind.LastRetried);
            Assert.Equal(3, mind.LastMalformed);
            Assert.Contains("teleport", mind.LastRaw);   // both answers in the transcript
        }

        /// <summary>⭐ ② — plain text with no tool call ⇒ no orders, no retry. 🔴 Red-proof: plain text read as a malformed answer ⇒ a second request.</summary>
        [Fact]
        public void CE4681_PlainText_IsNoOrders()
        {
            using var server = new StandInChatServer(StandInChatServer.Text("I would attack the house."));
            using var mind = Chat(server);
            var actions = mind.ThinkAsync(ChatBriefing(10, "00:00:10 ALPHA: spot"), default).GetAwaiter().GetResult();
            Assert.Empty(actions);
            Assert.Single(server.Requests);
            Assert.Contains("I would attack", mind.LastRaw);
        }

        /// <summary>⭐ ② — a model that does not answer in time, and a server that is down: no orders, at once after the limit — nothing hangs.
        /// 🔴 Red-proof: no per-call limit ⇒ the slow answer is awaited (5 s).</summary>
        [Fact]
        public void CE4681_ATimeoutOrAServerDown_IsNoOrders_AndNothingHangs()
        {
            using var slow = new StandInChatServer(StandInChatServer.Calls(("hold", "{\"squad\":\"Alpha\"}")) with { Delay = TimeSpan.FromSeconds(5) });
            using var late = Chat(slow, timeoutS: 0.5);
            var sw = Stopwatch.StartNew();
            Assert.Empty(late.ThinkAsync(ChatBriefing(10, "x"), default).GetAwaiter().GetResult());
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"took {sw.Elapsed}");
            Assert.Contains("no answer in", late.LastRaw);

            using var down = new ChatModelMind(new ChatModelSettings { Endpoint = $"http://localhost:{StandInChatServer.FreePort()}", Timeout = TimeSpan.FromSeconds(5) });
            sw.Restart();
            Assert.Empty(down.ThinkAsync(ChatBriefing(10, "x"), default).GetAwaiter().GetResult());
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"took {sw.Elapsed}");
            Assert.Contains("not reachable", down.LastRaw);
        }

        /// <summary>
        /// ⭐ ② — a well-formed order the action system REFUSES (a squad not his) comes back as a refusal item in the feed — a wake, so the model is asked
        /// again with the reason; the mind itself does not retry it. 🔴 Red-proof: the refusal not published ⇒ no refused item.
        /// </summary>
        [Fact]
        public void CE4681_ARefusedOrder_ComesBackAsARefusalWake()
        {
            using var server = new StandInChatServer(StandInChatServer.Calls(("order", "{\"squad\":\"Charlie\",\"kind\":\"Move\",\"where\":\"objective\"}")),
                                                     StandInChatServer.Text("noted"));
            using var mind = Chat(server);
            var s = new Seated(mind);
            s.Tick();   // the first frame opens the seat
            var sw = Stopwatch.StartNew();
            while (!s.Items().Any(i => i.Kind == FeedKind.OrderState && i.Radio.Contains("Charlie")) && sw.Elapsed < TimeSpan.FromSeconds(10)) { s.Tick(); Thread.Sleep(5); }
            var refusal = s.Items().First(i => i.Kind == FeedKind.OrderState && i.Radio.Contains("Charlie"));
            Assert.True(refusal.Wake);
            Assert.Contains("refused", refusal.Json);
            while (server.Requests.Count < 2 && sw.Elapsed < TimeSpan.FromSeconds(10)) { s.Tick(); Thread.Sleep(5); }
            Assert.True(server.Requests.Count >= 2, "the refusal wakes the mind again");
            s.Seats.Dispose();
        }
    }
}
