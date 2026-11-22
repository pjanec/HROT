using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using Hrot.CGF.Commander;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐⭐ <c>CE-4679</c> (DESIGN_LLM_Commander §3 "the HTTP surface", L-2) — the commander seats over HTTP, so an outside mind (the Haiku worker first)
    /// and a person debugging see EXACTLY what the model sees: the seats, the briefing, the feed (long-poll and server-sent events), the tools, and the
    /// answer. ⭐ Every read is the seat's own channel (thread-safe) — nothing here reads the world for the mind; the one write that touches the world
    /// (switching a seat's mind) runs on the main thread.
    /// </summary>
    public sealed partial class DebugApiService
    {
        /// <summary>The most a long-poll waits (s).</summary>
        public const double MaxFeedWaitSeconds = 60;

        private CommanderSeats? Seats => CommanderSeats.Peek(_world);

        /// <summary><c>GET /commanders</c> — the seats: commander, side, mind, mode, and where the feed is.</summary>
        public JsonNode GetCommanders()
        {
            var list = new JsonArray();
            foreach (var seat in Seats?.Snapshot ?? Array.Empty<SeatRuntime>())
                list.Add(new JsonObject
                {
                    ["id"] = seat.NetworkId, ["name"] = seat.Name, ["side"] = seat.Side, ["mind"] = seat.Mind.ToString(), ["mode"] = seat.Mode.ToString(),
                    ["latestSeq"] = seat.Channel.LatestSeq, ["answered"] = seat.Channel.Answered, ["thinking"] = seat.Channel.Thinking,
                    ["holdsClock"] = seat.HoldsClock, ["modelLog"] = seat.Log?.Path,
                });
            return new JsonObject { ["commanders"] = list };
        }

        /// <summary>⭐ CE-4679 — some seat holds the clock for its mind now: a run that STEPS the clock (verify, run-to) withholds its next step.</summary>
        public bool AnyCommanderHoldsClock() => Seats?.AnyHoldsClock ?? false;

        /// <summary>The seat, or why not (a 404).</summary>
        public SeatRuntime? CommanderSeat(int id, out string? why)
        {
            var seat = Seats?.ByNetworkId(id);
            why = seat == null ? $"no commander seat {id} — GET /commanders lists them (a seat opens when its scenario runs)" : null;
            return seat;
        }

        /// <summary><c>GET /commanders/{id}/briefing</c> — the context as the mind is given it: the text a model gets, byte for byte, and whether it waits on an answer.</summary>
        public JsonNode CommanderBriefing(SeatRuntime seat)
        {
            var external = seat.Runner?.Mind as ExternalMind;
            var briefing = external?.Pending ?? seat.Runner?.LastBriefing;
            return new JsonObject
            {
                ["id"] = seat.NetworkId, ["waitingForAnswer"] = external?.Pending != null, ["throughSeq"] = briefing?.ThroughSeq,
                ["text"] = briefing?.Text() ?? seat.Prefix,
            };
        }

        /// <summary>
        /// <c>GET /commanders/{id}/feed?after=&amp;wait=</c> — the items after <paramref name="after"/>; ⭐ a LONG-POLL: at once when there are any, else
        /// up to <paramref name="waitSeconds"/>. Called OFF the main thread (the channel is thread-safe; the frame never waits for a reader).
        /// </summary>
        public JsonNode CommanderFeed(SeatRuntime seat, long after, double waitSeconds, CancellationToken ct)
        {
            if (waitSeconds > 0)
                seat.Channel.WaitForItems(after, TimeSpan.FromSeconds(Math.Min(waitSeconds, MaxFeedWaitSeconds)), ct);
            var items = new List<FeedItem>();
            seat.Channel.ReadAfter(after, items);
            var arr = new JsonArray();
            foreach (var item in items) arr.Add(FeedItemJson(item));
            bool waiting = (seat.Runner?.Mind as ExternalMind)?.Pending != null;
            return new JsonObject
            {
                ["id"] = seat.NetworkId, ["items"] = arr, ["latestSeq"] = seat.Channel.LatestSeq, ["waitingForAnswer"] = waiting,
            };
        }

        public static JsonObject FeedItemJson(FeedItem item) => new()
        {
            ["seq"] = item.Seq, ["t"] = Math.Round(item.SimTime, 2), ["kind"] = KindName(item.Kind), ["wake"] = item.Wake, ["radio"] = item.Radio,
            ["data"] = JsonNode.Parse(item.Json),
        };

        private static string KindName(FeedKind kind) => kind switch
        {
            FeedKind.OrderState => "order-state",
            _ => kind.ToString().ToLowerInvariant(),
        };

        /// <summary>
        /// <c>POST /commanders/{id}/act {seq, actions:[…]}</c> — an outside mind's answer to the pending wake. A malformed action list is refused whole
        /// (400, nothing changes); a well-formed one goes to the action system, which validates EVERY action and answers each as an <c>order-state</c>
        /// item. ⛔ Only an <see cref="ExternalMind"/> seat takes an answer over HTTP.
        /// </summary>
        public (JsonNode? Result, int Status, string? Error) CommanderAct(SeatRuntime seat, JsonNode? body)
        {
            if (seat.Runner?.Mind is not ExternalMind external)
                return (null, 409, $"commander {seat.NetworkId}'s mind is {seat.Mind}, not External — POST /commanders/{seat.NetworkId}/mind {{\"kind\":\"External\"}} first");
            var problems = new List<string>();
            var actions = CommanderTools.Parse(body?["actions"], problems);
            if (problems.Count > 0) return (null, 400, "the action list was refused, nothing changed: " + string.Join("; ", problems));
            if (external.Submit(actions, body?.ToJsonString()) is { } why) return (null, 409, why);
            return (new JsonObject { ["id"] = seat.NetworkId, ["queued"] = actions.Count, ["note"] = "each action is answered by an order-state item in the feed" }, 200, null);
        }

        /// <summary><c>POST /commanders/{id}/mind {kind, mode}</c> — main thread: switch the seat's mind and / or mode at run time.</summary>
        public (JsonNode? Result, int Status, string? Error) CommanderSwitchMind(SeatRuntime seat, string? kind, string? mode)
        {
            CommanderMind? mind = null;
            CommanderMode? md = null;
            if (kind != null)
            {
                if (!Enum.TryParse<CommanderMind>(kind, ignoreCase: true, out var m)) return (null, 400, $"kind '{kind}' is not a mind — Scripted, External, Ollama");
                mind = m;
            }
            if (mode != null)
            {
                if (!Enum.TryParse<CommanderMode>(mode, ignoreCase: true, out var x)) return (null, 400, $"mode '{mode}' is not a mode — ThinkPause, Continuous");
                md = x;
            }
            if (Seats is not { } seats) return (null, 404, "no seats on this node");
            if (seats.SwitchMind(_world, seat, mind, md) is { } why) return (null, 409, why);
            return (new JsonObject { ["id"] = seat.NetworkId, ["mind"] = seat.Mind.ToString(), ["mode"] = seat.Mode.ToString() }, 200, null);
        }
    }
}
