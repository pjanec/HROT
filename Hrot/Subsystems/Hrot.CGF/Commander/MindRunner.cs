using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Hrot.CGF.Commander;

/// <summary>
/// ⭐ <c>CE-4676</c> (DESIGN_LLM_Commander §3 "the context keeper") — what a mind sees on one call: the stable prefix (role, rules, tools, gazetteer,
/// order of battle), the last snapshot, every radio line since it, the commander's own notes, and the items new since the last call. ⛔ No world.
/// </summary>
public sealed record Briefing(string Commander, double SimTime, string Prefix, FeedItem? Snapshot, IReadOnlyList<FeedItem> Log,
                              IReadOnlyList<string> Notes, IReadOnlyList<FeedItem> New, long ThroughSeq)
{
    /// <summary>⭐ CE-4684 C-6 — the ids the seat may use (himself, his squads, his places); null = a mind that does not need them.</summary>
    public SeatVocabulary? Vocabulary { get; init; }

    /// <summary>⭐ CE-4684 C-3 — the cycle this briefing is: the seq of the newest item it carries. A decision echoes it.</summary>
    public long Cycle => ThroughSeq;

    /// <summary>The briefing as one text, as a language model would get it (L-2's <c>GET /commanders/{id}/briefing</c>).</summary>
    public string Text() => Prefix + Environment.NewLine + Changing();

    /// <summary>⭐ <c>CE-4681</c> (L-3, §5a-3) — the part after the stable prefix: the snapshot, the radio log, the notes. A chat model gets the prefix as
    /// its system message (byte-identical every call, so a server reuses its cache) and this as the user message — the same text, split.</summary>
    public string Changing()
    {
        var sb = new StringBuilder();
        // ⭐ the duel (§8) — the snapshot says how OLD it is: 📌 Blue read a 65 s old "ORDERS IN FORCE: none" as now and re-ran its play
        if (Snapshot != null)
            sb.Append("SNAPSHOT (").Append(Math.Max(0d, SimTime - Snapshot.SimTime).ToString("0", System.Globalization.CultureInfo.InvariantCulture))
              .AppendLine(" s ago — the radio log below is newer)").AppendLine(Snapshot.Radio);
        sb.AppendLine("RADIO LOG");
        foreach (var item in Log) sb.AppendLine(item.Radio);
        // ⭐ CE-4683 ① — ONE running estimate (his last note, replaced each time he writes one); ⛔ was every note of the run (CE-3501)
        if (Notes.Count > 0) { sb.AppendLine("YOUR ESTIMATE (your last note — a new note replaces it)"); foreach (var n in Notes) sb.AppendLine(n); }
        return sb.ToString();
    }
}

/// <summary>
/// ⭐ <c>CE-4683</c> ④ (§5a-4) — what one call costs a small model: characters and an estimate of tokens (≈ chars / 4 for English text and JSON — a
/// RULE OF THUMB, not a tokenizer). ⭐ <see cref="Limit"/> is the budget a whole run of <c>llm-platoon-attack</c> must stay under: an 8 B model at
/// <c>num_ctx</c> 8 k must hold the request AND its answer.
/// </summary>
public static class BriefingBudget
{
    public const int Limit = 4000;
    public static int Tokens(int chars) => (chars + 3) / 4;

    /// <summary>The request a transcript sent, else the briefing's text + the tool list (what a chat model would be sent).</summary>
    public static int CharsOf(Briefing briefing, IMindTranscript? transcript)
        => transcript?.LastRequest is { Length: > 0 } request ? request.Length : briefing.Text().Length + ToolsChars;

    private static readonly int ToolsChars = ChatModelMind.Tools().ToJsonString().Length;
}

/// <summary>⭐ <c>CE-4681</c> — a mind whose exchange the model log keeps: the request it made (null when it made none — an outside mind) and the raw
/// answer(s), plus how many tool calls were malformed and whether it retried. Shared by <see cref="ExternalMind"/> and <see cref="ChatModelMind"/>.</summary>
public interface IMindTranscript
{
    string? LastRequest { get; }
    string? LastRaw { get; }
    int LastMalformed { get; }
    bool LastRetried { get; }
}

/// <summary>⭐ <c>CE-4684</c> C-1 — a mind that answers with ONE decision object: the last call's validation problems (the first answer's, then the
/// repair's), whether it was repaired, whether it fell back to CONTINUE, and the decoded decision as JSON (the model log keeps all four per cycle).</summary>
public interface IDecisionTranscript
{
    IReadOnlyList<string>? LastValidation { get; }
    bool LastRepaired { get; }
    bool LastFellBack { get; }
    System.Text.Json.Nodes.JsonNode? LastDecision { get; }
}

/// <summary>⭐ <c>CE-4676</c> (DESIGN_LLM_Commander §4) — a commander's mind: one call at a time, on the runner's thread; ⛔ it never sees the world.</summary>
public interface IMind
{
    Task<IReadOnlyList<CommanderAction>> ThinkAsync(Briefing briefing, CancellationToken ct);
}

/// <summary>
/// ⭐⭐ <c>CE-4676</c> (DESIGN_LLM_Commander §2, §3) — one per seat, on its own background thread: waits for feed items, keeps the context, and calls the
/// mind on a WAKE item or a heartbeat — one call at a time; items that arrive meanwhile wait for the next call. Its only contact with the simulation is
/// the <see cref="CommanderChannel"/>.
/// </summary>
public sealed class MindRunner : IDisposable
{
    private readonly CommanderChannel _channel;
    private readonly IMind _mind;
    private readonly string _commander;
    private readonly string _prefix;
    private readonly CancellationTokenSource _stop = new();
    private Thread? _thread;

    // the context keeper — this thread only
    private FeedItem? _snapshot;
    private readonly List<FeedItem> _log = new();
    private string? _estimate;   // ⭐ CE-4683 ① — the commander's running estimate: his last answer's notes, replacing the one before

    /// <summary>⭐ CE-4683 ① — how long the estimate may be (characters); longer is cut with <see cref="Cut"/>.</summary>
    public const int EstimateLimit = 800;
    public const string Cut = " …[cut]";

    /// <summary>⭐ CE-4683 ① — the estimate an answer leaves: its notes joined, capped; null = the answer wrote none (the old one stays).</summary>
    public static string? EstimateFrom(IReadOnlyList<CommanderAction> actions)
    {
        StringBuilder? sb = null;
        foreach (var a in actions)
            if (a.Kind == CommanderActionKind.Note && a.Text is { Length: > 0 } text)
                (sb ??= new StringBuilder()).Append(sb.Length > 0 ? " " : "").Append(text.Trim());
        if (sb == null) return null;
        return sb.Length <= EstimateLimit ? sb.ToString() : sb.ToString(0, EstimateLimit - Cut.Length) + Cut;
    }
    private long _seen;

    /// <summary>⭐ CE-4679 — the mind this runner calls (the HTTP routes reach an <see cref="ExternalMind"/> through it).</summary>
    public IMind Mind => _mind;

    /// <summary>⭐ CE-4679 — the briefing of the last call (or, before any, the prefix and what has come) — <c>GET /commanders/{id}/briefing</c>.</summary>
    public Briefing? LastBriefing => Volatile.Read(ref _lastBriefing);
    private Briefing? _lastBriefing;

    /// <summary>⭐ CE-4679 — the seat's model log (each call's new lines and the answer); null = none.</summary>
    public ModelLog? Log { get; init; }

    /// <summary>⭐ CE-4684 C-6 — the seat's ids, handed to each briefing (a decision is validated against them).</summary>
    public SeatVocabulary? Vocabulary { get; init; }

    public MindRunner(CommanderChannel channel, IMind mind, string commander, string prefix)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _mind = mind ?? throw new ArgumentNullException(nameof(mind));
        _commander = commander;
        _prefix = prefix;
    }

    /// <summary>How many times the mind was called (a rail / diagnostics probe).</summary>
    public int Calls => Volatile.Read(ref _calls);
    private int _calls;

    /// <summary>The managed thread id the mind runs on (a rail: it is never the frame thread).</summary>
    public int ThreadId { get; private set; }

    public void Start()
    {
        if (_thread != null) return;
        _thread = new Thread(Loop) { IsBackground = true, Name = "commander-mind " + _commander };
        _thread.Start();
    }

    private void Loop()
    {
        ThreadId = Environment.CurrentManagedThreadId;
        var ct = _stop.Token;
        var fresh = new List<FeedItem>();
        while (!ct.IsCancellationRequested)
        {
            if (!_channel.WaitForItems(_seen, TimeSpan.FromMilliseconds(250), ct)) continue;
            fresh.Clear();
            _channel.ReadAfter(_seen, fresh);
            if (fresh.Count == 0) continue;
            bool think = false;
            foreach (var item in fresh)
            {
                if (item.Kind == FeedKind.Snapshot) { _snapshot = item; _log.Clear(); }
                else _log.Add(item);
                think |= item.Wake || item.Kind == FeedKind.Heartbeat;
            }
            _seen = fresh[^1].Seq;
            if (!think) { _channel.Answer(_seen, Array.Empty<CommanderAction>()); continue; }

            IReadOnlyList<CommanderAction> actions;
            try
            {
                Interlocked.Increment(ref _calls);
                var briefing = new Briefing(_commander, fresh[^1].SimTime, _prefix, _snapshot, _log.ToArray(),
                                            _estimate is { } est ? new[] { est } : Array.Empty<string>(), fresh.ToArray(), _seen) { Vocabulary = Vocabulary };
                Volatile.Write(ref _lastBriefing, briefing);
                long started = System.Diagnostics.Stopwatch.GetTimestamp();
                actions = _mind.ThinkAsync(briefing, ct).GetAwaiter().GetResult();
                LogCall(briefing, actions, System.Diagnostics.Stopwatch.GetElapsedTime(started));
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                // the mind failing is news for the logs, never a crash of the simulation — ⭐ CE-4683: and never a note (it would replace his estimate)
                Fdp.Core.Logging.FdpLog<MindRunner>.Warn("[Commander] {0}: the mind failed: {1}: {2}", _commander, ex.GetType().Name, ex.Message);
                Log?.Write(new System.Text.Json.Nodes.JsonObject { ["kind"] = "failure", ["seq"] = _seen, ["error"] = $"{ex.GetType().Name}: {ex.Message}" });
                actions = Array.Empty<CommanderAction>();
            }
            if (EstimateFrom(actions) is { } estimate) _estimate = estimate;
            _channel.Answer(_seen, actions);
        }
    }

    private void LogCall(Briefing briefing, IReadOnlyList<CommanderAction> actions, TimeSpan took)
    {
        if (Log == null) return;
        var lines = new System.Text.Json.Nodes.JsonArray();
        foreach (var item in briefing.New) lines.Add(item.Radio);
        var answer = new System.Text.Json.Nodes.JsonArray();
        foreach (var a in actions) answer.Add(CommanderTools.ToJson(a));
        var line = new System.Text.Json.Nodes.JsonObject
        {
            ["kind"] = "call", ["seq"] = briefing.ThroughSeq, ["t"] = Math.Round(briefing.SimTime, 2), ["new"] = lines, ["actions"] = answer,
            ["ms"] = (long)took.TotalMilliseconds,   // ⭐ CE-4681 — how long the mind took (wall): the answer time a laptop run reports
            // ⭐ CE-4683 ④ — the size the call cost: the request as sent (a chat model), else the briefing + the tools a model would get
            ["chars"] = BriefingBudget.CharsOf(briefing, _mind as IMindTranscript),
            ["tokens"] = BriefingBudget.Tokens(BriefingBudget.CharsOf(briefing, _mind as IMindTranscript)),
        };
        if (_mind is IMindTranscript transcript)   // ⭐ CE-4681 — every mind that talks to something keeps its exchange in the log
        {
            if (transcript.LastRequest is { } request) line["request"] = request;
            if (transcript.LastRaw is { } raw) line["raw"] = raw;
            if (transcript.LastMalformed > 0) line["malformed"] = transcript.LastMalformed;
            if (transcript.LastRetried) line["retried"] = true;
        }
        if (_mind is IDecisionTranscript dt)   // ⭐ CE-4684 C-1 — per cycle: the validation, repaired or fell back, the decision as decoded
        {
            if (dt.LastValidation is { Count: > 0 } v)
            {
                var problems = new System.Text.Json.Nodes.JsonArray();
                for (int i = 0; i < v.Count; i++) problems.Add(v[i]);
                line["validation"] = problems;
            }
            if (dt.LastRepaired) line["repaired"] = true;
            if (dt.LastFellBack) line["fallback"] = "CONTINUE";
            if (dt.LastDecision is { } d) line["decision"] = d.DeepClone();
        }
        Log.Write(line);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _thread?.Join(TimeSpan.FromSeconds(2));
    }
}
