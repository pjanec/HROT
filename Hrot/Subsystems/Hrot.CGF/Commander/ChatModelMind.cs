using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Hrot.CGF.Commander;

/// <summary>
/// ⭐ <c>CE-4681</c> (DESIGN_LLM_Commander §5a-3) — where a <see cref="ChatModelMind"/> sends its requests and how. Read when a seat opens; every value has
/// a default so a laptop with Ollama on its default port needs nothing set.
/// </summary>
public sealed record ChatModelSettings
{
    public string Endpoint { get; init; } = "http://localhost:11434";
    public string Model { get; init; } = "qwen2.5:7b-instruct";
    /// <summary>The context the server keeps (tokens) — an 8 B model on 8 GB fits 8–16 k (§6).</summary>
    public int NumCtx { get; init; } = 8192;
    /// <summary>How long the server keeps the model loaded between calls (Ollama's <c>keep_alive</c>).</summary>
    public string KeepAlive { get; init; } = "30m";
    /// <summary>How long one call may take (wall time) — the think-pause hold waits for it; past it the seat goes on with no orders.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(120);
    /// <summary>⭐ CE-4684 (§5c C-1) — the answer's shape: ONE decision object decoded against the seat's schema (the default), or L-3a's free tool
    /// calls (<c>HROT_COMMANDER_FORMAT=tools</c> — kept for the side-by-side baseline).</summary>
    public bool Decisions { get; init; } = true;

    /// <summary><c>HROT_COMMANDER_ENDPOINT</c> · <c>_MODEL</c> · <c>_NUM_CTX</c> · <c>_KEEP_ALIVE</c> · <c>_TIMEOUT_S</c>; an unset or unreadable one keeps its default.</summary>
    public static ChatModelSettings FromEnvironment(Func<string, string?>? env = null)
    {
        env ??= name => Fdp.Core.EnvVars.Find(name) is { } declared ? Fdp.Core.EnvVars.Read(declared) : null;   // ⭐ CE-4586 — every read through the one table
        var s = new ChatModelSettings();
        if (env("HROT_COMMANDER_ENDPOINT") is { Length: > 0 } endpoint) s = s with { Endpoint = endpoint.TrimEnd('/') };
        if (env("HROT_COMMANDER_MODEL") is { Length: > 0 } model) s = s with { Model = model };
        if (int.TryParse(env("HROT_COMMANDER_NUM_CTX"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int ctx) && ctx > 0) s = s with { NumCtx = ctx };
        if (env("HROT_COMMANDER_KEEP_ALIVE") is { Length: > 0 } keep) s = s with { KeepAlive = keep };
        if (double.TryParse(env("HROT_COMMANDER_TIMEOUT_S"), NumberStyles.Float, CultureInfo.InvariantCulture, out double t) && t > 0) s = s with { Timeout = TimeSpan.FromSeconds(t) };
        if (env("HROT_COMMANDER_FORMAT") is { Length: > 0 } format) s = s with { Decisions = !format.Equals("tools", StringComparison.OrdinalIgnoreCase) };
        return s;
    }
}

/// <summary>
/// ⭐⭐ <c>CE-4681</c> (DESIGN_LLM_Commander §4 "OllamaMind", §5a-3) — a language model behind an Ollama-style chat server: on each wake ONE
/// <c>POST /api/chat</c> (<c>messages</c>, <c>tools</c> = the commander's tool list, <c>stream: false</c>, <c>options.num_ctx</c>, <c>keep_alive</c>); the
/// answer's <c>tool_calls</c> are read by the ONE reader (<see cref="CommanderTools.Parse"/>) and go to <see cref="CommanderActionSystem"/> like every mind's.
/// A malformed or unknown call is sent back ONCE as a tool message; plain text, a timeout or a server that is down ⇒ no orders, the run goes on.
/// ⛔ It never sees the world: only the briefing. Runs on the runner's thread.
/// </summary>
public sealed class ChatModelMind : IMind, IMindTranscript, IDecisionTranscript, IDecisionMind, IDisposable
{
    /// <summary>The fixed line after the prefix in the system message: how to answer.</summary>
    public const string Instruction =
        "Answer ONLY with tool calls — one or more per turn. Name squads and places exactly as the briefing does. If nothing needs doing, call note() with your estimate.";

    /// <summary>⭐ CE-4684 (§5c C-1) — the fixed line after the prefix when the answer is a decision object.</summary>
    public const string DecisionInstruction =
        "Answer with ONE JSON decision object, fields in this order: cycle (copy the CYCLE number of this briefing), assessment, intent, orders, "
        + "contingencies, next_decision. Think in that order: what is happening, what you intend, then the orders. A unit you do not order keeps its "
        + "orders in force (orders may be an empty list). Use ONLY the ids listed: units, places. Conditions: {on, unit, at, time_s, percent} with on = "
        + "IMMEDIATE | AT_TIME (time_s) | UNIT_AT (unit, at) | UNIT_TASK_COMPLETE (unit) | CONTACT (unit) | STRENGTH_BELOW (unit, percent) | "
        + "ENEMY_SEEN_AT (at). An order may wait (start), end (complete_when) or be called off (abort_when); contingencies (at most 3) fire their orders "
        + "when their trigger happens, without asking you. next_decision.not_later_than_s: when you want to be asked again (10-300 s); wake_on: up to 3 "
        + "conditions that should wake you sooner.";

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private string? _system;   // built once per seat: byte-identical on every call (§5a-3 — a server reuses its cache)

    public ChatModelSettings Settings { get; }

    public ChatModelMind(ChatModelSettings settings, HttpClient? http = null)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _ownsHttp = http == null;
        _http = http ?? new HttpClient();
        _http.Timeout = System.Threading.Timeout.InfiniteTimeSpan;   // the call is bounded by Settings.Timeout below, per request
    }

    // ── the transcript (the model log) ─────────────────────────────────────────────────────────────
    public string? LastRequest { get; private set; }
    public string? LastRaw { get; private set; }
    public int LastMalformed { get; private set; }
    public bool LastRetried { get; private set; }

    /// <summary>The system message: the seat's prefix and <see cref="Instruction"/>.</summary>
    public string SystemMessage(Briefing briefing) => _system ??= briefing.Prefix + "\n" + (Contract(briefing) ? DecisionInstruction : Instruction);

    /// <summary>⭐ CE-4684 — the seat answers with decisions (the default format).</summary>
    public bool AnswersWithDecisions => Settings.Decisions;

    /// <summary>⭐ CE-4684 — this call answers with a decision object: the setting is on and the seat gave its ids.</summary>
    private bool Contract(Briefing briefing) => Settings.Decisions && briefing.Vocabulary != null;

    /// <summary>⭐ CE-4684 C-1/C-6 — the schema the answer is decoded against, built once per seat (its ids are enums).</summary>
    private JsonObject? _schema;

    // ── ⭐ CE-4684 — the decision transcript ──
    public IReadOnlyList<string>? LastValidation { get; private set; }
    public bool LastRepaired { get; private set; }
    public bool LastFellBack { get; private set; }
    public JsonNode? LastDecision { get; private set; }

    /// <summary>The user message: what changed since the prefix, and the time now.</summary>
    public static string UserMessage(Briefing briefing)
        => briefing.Changing() + "NOW " + TimeSpan.FromSeconds(Math.Max(0, briefing.SimTime)).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
           + " — your orders.";

    /// <summary>⭐ CE-4684 C-3 — the user message of a decision call: the CYCLE it must echo first, and the clock in seconds too (AT_TIME takes seconds).</summary>
    public static string DecisionMessage(Briefing briefing)
        => "CYCLE " + briefing.Cycle.ToString(CultureInfo.InvariantCulture) + "\n" + briefing.Changing() + "NOW "
           + TimeSpan.FromSeconds(Math.Max(0, briefing.SimTime)).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
           + " (" + Math.Round(Math.Max(0, briefing.SimTime)).ToString(CultureInfo.InvariantCulture) + " s) — your decision for cycle "
           + briefing.Cycle.ToString(CultureInfo.InvariantCulture) + ".";

    /// <summary>The commander's tools in the chat API's shape: each <see cref="CommanderTools.Schema"/> entry wrapped as a function.</summary>
    public static JsonArray Tools()
    {
        var tools = new JsonArray();
        foreach (var t in CommanderTools.Schema()) tools.Add(new JsonObject { ["type"] = "function", ["function"] = t!.DeepClone() });
        return tools;
    }

    /// <summary>The request body for <paramref name="messages"/>.</summary>
    public JsonObject Request(JsonArray messages) => new()
    {
        ["model"] = Settings.Model, ["messages"] = messages.DeepClone(), ["tools"] = Tools(), ["stream"] = false,   // a copy: the retry adds to the list
        ["options"] = new JsonObject { ["num_ctx"] = Settings.NumCtx }, ["keep_alive"] = Settings.KeepAlive,
    };

    public async Task<IReadOnlyList<CommanderAction>> ThinkAsync(Briefing briefing, CancellationToken ct)
    {
        LastRequest = LastRaw = null;
        LastMalformed = 0;
        LastRetried = false;
        LastValidation = null;
        LastRepaired = LastFellBack = false;
        LastDecision = null;
        if (Contract(briefing)) return await DecideAsync(briefing, ct).ConfigureAwait(false);
        var raws = new JsonArray();
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = SystemMessage(briefing) },
            new JsonObject { ["role"] = "user", ["content"] = UserMessage(briefing) },
        };
        var request = Request(messages);
        LastRequest = request.ToJsonString();
        try
        {
            var first = await CallAsync(LastRequest, raws, ct).ConfigureAwait(false);
            if (first is null) return Array.Empty<CommanderAction>();
            var problems = new List<string>();
            var actions = Read(first, problems, out var calls);
            LastMalformed = problems.Count;
            if (problems.Count == 0) return actions;

            // ONE retry: the answer as the assistant turn, one tool message per error naming what was taken
            LastRetried = true;
            var taken = new StringBuilder();
            foreach (var a in actions) taken.Append(taken.Length > 0 ? "; " : "").Append(CommanderTools.ToJson(a).ToJsonString());
            messages.Add(new JsonObject
            {
                ["role"] = "assistant", ["content"] = (string?)first["content"] ?? "", ["tool_calls"] = calls?.DeepClone() ?? new JsonArray(),
            });
            foreach (var p in problems)
                messages.Add(new JsonObject
                {
                    ["role"] = "tool",
                    ["content"] = $"error: {p}. Taken: {(taken.Length > 0 ? taken.ToString() : "nothing")}. Send ONLY the corrected call(s); the tools are order, hold, ask_sitrep, run_play, stop_play, note.",
                });
            var second = await CallAsync(Request(messages).ToJsonString(), raws, ct).ConfigureAwait(false);
            if (second is null) return actions;
            var again = new List<string>();
            var more = Read(second, again, out _);
            LastMalformed += again.Count;   // dropped: no second retry
            var all = new List<CommanderAction>(actions);
            all.AddRange(more);
            return all;
        }
        finally
        {
            LastRaw = raws.ToJsonString();
        }
    }

    /// <summary>
    /// ⭐⭐ CE-4684 (§5c C-1) — ONE decision: the request carries the seat's schema as Ollama's <c>format</c> (a server that decodes against it cannot
    /// answer anything else), the answer is read and validated (<see cref="DecisionContract.Read"/>); a decision with problems goes back ONCE with every
    /// problem listed, and a second failure is CONTINUE — nothing changes, the run goes on. Returns the estimate (a note) and the decision.
    /// </summary>
    private async Task<IReadOnlyList<CommanderAction>> DecideAsync(Briefing briefing, CancellationToken ct)
    {
        var vocab = briefing.Vocabulary!;
        _schema ??= DecisionContract.Schema(vocab);
        var raws = new JsonArray();
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = SystemMessage(briefing) },
            new JsonObject { ["role"] = "user", ["content"] = DecisionMessage(briefing) },
        };
        var problems = new List<string>();
        try
        {
            LastRequest = DecisionRequest(messages).ToJsonString();
            var first = await CallAsync(LastRequest, raws, ct).ConfigureAwait(false);
            if (first is null) { LastFellBack = true; return Array.Empty<CommanderAction>(); }
            string content = (string?)first["content"] ?? "";
            var decision = DecisionContract.Read(content, vocab, briefing.Cycle, problems);
            if (decision == null)
            {
                LastRetried = true;
                LastMalformed = problems.Count;
                messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = content });
                messages.Add(new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = "Your decision was REFUSED: " + string.Join("; ", problems) + ". Send the WHOLE decision again, corrected, for cycle "
                                  + briefing.Cycle.ToString(CultureInfo.InvariantCulture) + ".",
                });
                var second = await CallAsync(DecisionRequest(messages).ToJsonString(), raws, ct).ConfigureAwait(false);
                var again = new List<string>();
                decision = second is null ? null : DecisionContract.Read((string?)second["content"] ?? "", vocab, briefing.Cycle, again);
                problems.AddRange(again);
                if (decision == null) { LastFellBack = true; LastValidation = problems; return Array.Empty<CommanderAction>(); }   // CONTINUE
                LastRepaired = true;
            }
            LastValidation = problems;
            LastDecision = DecisionJson(decision);
            return new[]
            {
                new CommanderAction(CommanderActionKind.Note, Text: DecisionContract.Estimate(decision)),   // the running estimate (CE-4683 ①)
                new CommanderAction(CommanderActionKind.Decision) { Plan = decision, Vocabulary = vocab },
            };
        }
        finally { LastRaw = raws.ToJsonString(); }
    }

    /// <summary>The decision request: the messages, the schema as <c>format</c>, no tools.</summary>
    public JsonObject DecisionRequest(JsonArray messages) => new()
    {
        ["model"] = Settings.Model, ["messages"] = messages.DeepClone(), ["format"] = _schema!.DeepClone(), ["stream"] = false,
        ["options"] = new JsonObject { ["num_ctx"] = Settings.NumCtx }, ["keep_alive"] = Settings.KeepAlive,
    };

    /// <summary>The decision as the model log keeps it.</summary>
    public static JsonObject DecisionJson(Decision d)
    {
        JsonObject Order(UnitOrder o)
        {
            var j = new JsonObject { ["unit"] = o.Unit, ["task"] = o.Task, ["purpose"] = o.Purpose };
            if (o.Objective != null) j["objective"] = o.Objective;
            if (o.SupportAt != null) j["support_at"] = o.SupportAt;
            if (o.Start != null) j["start"] = o.Start.Text;
            if (o.CompleteWhen != null) j["complete_when"] = o.CompleteWhen.Text;
            if (o.AbortWhen.Count > 0) { var a = new JsonArray(); foreach (var c in o.AbortWhen) a.Add(c.Text); j["abort_when"] = a; }
            return j;
        }
        var orders = new JsonArray(); foreach (var o in d.Orders) orders.Add(Order(o));
        var cont = new JsonArray();
        foreach (var c in d.Contingencies) { var os = new JsonArray(); foreach (var o in c.Orders) os.Add(Order(o)); cont.Add(new JsonObject { ["trigger"] = c.Trigger.Text, ["orders"] = os }); }
        var wake = new JsonArray(); foreach (var w in d.WakeOn) wake.Add(w.Text);
        return new JsonObject
        {
            ["cycle"] = d.Cycle,
            ["assessment"] = new JsonObject { ["situation"] = d.Situation, ["enemy_likely_coa"] = d.EnemyLikelyCoa, ["main_risk"] = d.MainRisk },
            ["intent"] = new JsonObject { ["main_effort"] = d.MainEffort, ["scheme"] = d.Scheme, ["end_state"] = d.EndState },
            ["orders"] = orders, ["contingencies"] = cont,
            ["next_decision"] = new JsonObject { ["not_later_than_s"] = d.NextDecisionS, ["wake_on"] = wake },
        };
    }

    /// <summary>One HTTP call; the answer's <c>message</c>, or null (no answer — the reason is kept in the transcript).</summary>
    private async Task<JsonObject?> CallAsync(string body, JsonArray raws, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Settings.Timeout);
        try
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(Settings.Endpoint + "/api/chat", content, timeout.Token).ConfigureAwait(false);
            string text = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            raws.Add(text);
            if (!response.IsSuccessStatusCode) { raws.Add($"error: HTTP {(int)response.StatusCode}"); return null; }
            return JsonNode.Parse(text)?["message"] as JsonObject is { } message ? message : Fail(raws, "no message in the answer");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Fail(raws, $"no answer in {Settings.Timeout.TotalSeconds:0} s — no orders");
        }
        catch (HttpRequestException e) { return Fail(raws, $"the chat server is not reachable ({e.Message}) — no orders"); }
        catch (JsonException e) { return Fail(raws, $"the answer is not JSON ({e.Message}) — no orders"); }
    }

    private static JsonObject? Fail(JsonArray raws, string why)
    {
        raws.Add("error: " + why);
        Fdp.Core.Logging.FdpLog<ChatModelMind>.Warn("[Commander] chat model: {0}", why);
        return null;
    }

    /// <summary>The answer's tool calls through the one reader. A call whose arguments are not an object (nor a JSON string of one) is a problem.</summary>
    private static List<CommanderAction> Read(JsonObject message, List<string> problems, out JsonArray? calls)
    {
        calls = message["tool_calls"] as JsonArray;
        var list = new JsonArray();
        if (calls == null) return new List<CommanderAction>();   // plain text: no orders (the raw answer is in the log)
        for (int i = 0; i < calls.Count; i++)
        {
            var f = calls[i]?["function"] as JsonObject ?? calls[i] as JsonObject;
            var args = f?["arguments"];
            if (args is JsonValue v && v.TryGetValue(out string? s))
            {
                try { args = JsonNode.Parse(s); }
                catch (JsonException) { args = null; }
            }
            if (f == null || args is not JsonObject argObject)
            {
                problems.Add($"call {i} ({(string?)f?["name"] ?? "?"}): its arguments are not a JSON object");
                list.Add(new JsonObject { ["name"] = "\u0000skip" });   // keeps the indexes aligned; never a tool
                continue;
            }
            list.Add(new JsonObject { ["name"] = (string?)f["name"], ["arguments"] = argObject.DeepClone() });
        }
        var parsed = new List<string>();
        var actions = CommanderTools.Parse(list, parsed);
        foreach (var p in parsed)
            if (!p.Contains("\u0000skip", StringComparison.Ordinal)) problems.Add(p);
        return actions;
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}
