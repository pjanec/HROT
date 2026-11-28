using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hrot.CGF.Commander;

/// <summary>
/// ⭐⭐ <c>CE-4679</c> (DESIGN_LLM_Commander §4, L-2) — a mind OUTSIDE the process: each wake publishes its briefing (<see cref="Pending"/>, served as
/// <c>GET /commanders/{id}/briefing</c>) and waits for <c>POST /commanders/{id}/act</c> (<see cref="Submit"/>) — bounded by <see cref="Timeout"/>, after
/// which it answers "no orders". The posted actions take the SAME road as any mind's: the runner answers the channel with them and
/// <see cref="CommanderActionSystem"/> validates every one. ⛔ It never sees the world: only the briefing it was given.
/// </summary>
public sealed class ExternalMind : IMind, IMindTranscript
{
    string? IMindTranscript.LastRequest => null;   // an outside mind fetches its briefing itself
    int IMindTranscript.LastMalformed => 0;
    bool IMindTranscript.LastRetried => false;

    /// <summary>How long a wake waits for an answer before the seat goes on with no orders (wall time).</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

    public TimeSpan Timeout { get; init; } = DefaultTimeout;

    private readonly object _gate = new();
    private TaskCompletionSource<IReadOnlyList<CommanderAction>>? _waiting;
    private Briefing? _pending, _last;
    private string? _lastRaw;

    /// <summary>The briefing a wake is waiting on an answer to; null between wakes.</summary>
    public Briefing? Pending { get { lock (_gate) return _pending; } }

    /// <summary>The last briefing given (pending or answered).</summary>
    public Briefing? Last { get { lock (_gate) return _last; } }

    /// <summary>The last answer as it was posted (the model log keeps it).</summary>
    public string? LastRaw { get { lock (_gate) return _lastRaw; } }

    public async Task<IReadOnlyList<CommanderAction>> ThinkAsync(Briefing briefing, CancellationToken ct)
    {
        TaskCompletionSource<IReadOnlyList<CommanderAction>> answer;
        lock (_gate)
        {
            _pending = briefing;
            _last = briefing;
            _lastRaw = null;
            _waiting = answer = new TaskCompletionSource<IReadOnlyList<CommanderAction>>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        try
        {
            var done = await Task.WhenAny(answer.Task, Task.Delay(Timeout, ct)).ConfigureAwait(false);
            if (done == answer.Task) return await answer.Task.ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            _lastRaw = $"no answer in {Timeout.TotalSeconds:0} s — no orders";   // ⭐ CE-4683: the model log says it; ⛔ a note would replace his estimate
            return Array.Empty<CommanderAction>();
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_waiting, answer)) _waiting = null;
                _pending = null;
            }
        }
    }

    /// <summary>
    /// The outside agent's answer to the pending wake (any thread). Null when taken; else why not (nothing is waiting — the agent should long-poll
    /// the feed for the next wake). The actions are not validated here: the action system does, on the frame thread, and answers each.
    /// </summary>
    public string? Submit(IReadOnlyList<CommanderAction> actions, string? raw = null)
    {
        lock (_gate)
        {
            if (_waiting is not { } waiting)
                return "no question is waiting — long-poll GET /commanders/{id}/feed?after=<seq>&wait=<s> for the next wake, then answer it";
            _waiting = null;
            _lastRaw = raw;
            waiting.TrySetResult(actions);
            return null;
        }
    }
}
