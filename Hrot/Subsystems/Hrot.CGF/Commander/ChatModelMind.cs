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

    /// <summary><c>HROT_COMMANDER_ENDPOINT</c> · <c>_MODEL</c> · <c>_NUM_CTX</c> · <c>_KEEP_ALIVE</c> · <c>_TIMEOUT_S</c>; an unset or unreadable one keeps its default.</summary>
    public static ChatModelSettings FromEnvironment(Func<string, string?>? env = null)
    {
        env ??= Environment.GetEnvironmentVariable;
        var s = new ChatModelSettings();
        if (env("HROT_COMMANDER_ENDPOINT") is { Length: > 0 } endpoint) s = s with { Endpoint = endpoint.TrimEnd('/') };
        if (env("HROT_COMMANDER_MODEL") is { Length: > 0 } model) s = s with { Model = model };
        if (int.TryParse(env("HROT_COMMANDER_NUM_CTX"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int ctx) && ctx > 0) s = s with { NumCtx = ctx };
        if (env("HROT_COMMANDER_KEEP_ALIVE") is { Length: > 0 } keep) s = s with { KeepAlive = keep };
        if (double.TryParse(env("HROT_COMMANDER_TIMEOUT_S"), NumberStyles.Float, CultureInfo.InvariantCulture, out double t) && t > 0) s = s with { Timeout = TimeSpan.FromSeconds(t) };
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
public sealed class ChatModelMind : IMind, IMindTranscript, IDisposable
{
    /// <summary>The fixed line after the prefix in the system message: how to answer.</summary>
    public const string Instruction =
        "Answer ONLY with tool calls — one or more per turn. Name squads and places exactly as the briefing does. If nothing needs doing, call note() with your estimate.";

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
    public string SystemMessage(Briefing briefing) => _system ??= briefing.Prefix + "\n" + Instruction;

    /// <summary>The user message: what changed since the prefix, and the time now.</summary>
    public static string UserMessage(Briefing briefing)
        => briefing.Changing() + "NOW " + TimeSpan.FromSeconds(Math.Max(0, briefing.SimTime)).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
           + " — your orders.";

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
