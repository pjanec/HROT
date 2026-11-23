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
    /// <summary>The briefing as one text, as a language model would get it (L-2's <c>GET /commanders/{id}/briefing</c>).</summary>
    public string Text() => Prefix + Environment.NewLine + Changing();

    /// <summary>⭐ <c>CE-4681</c> (L-3, §5a-3) — the part after the stable prefix: the snapshot, the radio log, the notes. A chat model gets the prefix as
    /// its system message (byte-identical every call, so a server reuses its cache) and this as the user message — the same text, split.</summary>
    public string Changing()
    {
        var sb = new StringBuilder();
        if (Snapshot != null) sb.AppendLine("SNAPSHOT").AppendLine(Snapshot.Radio);
        sb.AppendLine("RADIO LOG");
        foreach (var item in Log) sb.AppendLine(item.Radio);
        if (Notes.Count > 0) { sb.AppendLine("YOUR NOTES"); foreach (var n in Notes) sb.AppendLine(n); }
        return sb.ToString();
    }
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
    private readonly List<string> _notes = new();
    private long _seen;

    /// <summary>⭐ CE-4679 — the mind this runner calls (the HTTP routes reach an <see cref="ExternalMind"/> through it).</summary>
    public IMind Mind => _mind;

    /// <summary>⭐ CE-4679 — the briefing of the last call (or, before any, the prefix and what has come) — <c>GET /commanders/{id}/briefing</c>.</summary>
    public Briefing? LastBriefing => Volatile.Read(ref _lastBriefing);
    private Briefing? _lastBriefing;

    /// <summary>⭐ CE-4679 — the seat's model log (each call's new lines and the answer); null = none.</summary>
    public ModelLog? Log { get; init; }

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
                var briefing = new Briefing(_commander, fresh[^1].SimTime, _prefix, _snapshot, _log.ToArray(), _notes.ToArray(), fresh.ToArray(), _seen);
                Volatile.Write(ref _lastBriefing, briefing);
                long started = System.Diagnostics.Stopwatch.GetTimestamp();
                actions = _mind.ThinkAsync(briefing, ct).GetAwaiter().GetResult();
                LogCall(briefing, actions, System.Diagnostics.Stopwatch.GetElapsedTime(started));
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                // the mind failing is news for the commander's log, never a crash of the simulation
                actions = new[] { new CommanderAction(CommanderActionKind.Note, Text: $"the mind failed: {ex.GetType().Name}: {ex.Message}") };
            }
            foreach (var a in actions)
                if (a.Kind == CommanderActionKind.Note && a.Text is { Length: > 0 } note) _notes.Add(note);
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
        };
        if (_mind is IMindTranscript transcript)   // ⭐ CE-4681 — every mind that talks to something keeps its exchange in the log
        {
            if (transcript.LastRequest is { } request) line["request"] = request;
            if (transcript.LastRaw is { } raw) line["raw"] = raw;
            if (transcript.LastMalformed > 0) line["malformed"] = transcript.LastMalformed;
            if (transcript.LastRetried) line["retried"] = true;
        }
        Log.Write(line);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _thread?.Join(TimeSpan.FromSeconds(2));
    }
}
