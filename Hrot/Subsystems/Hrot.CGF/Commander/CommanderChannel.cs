using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Hrot.CGF.Commander;

/// <summary>What a feed item is (DESIGN_LLM_Commander §3).</summary>
public enum FeedKind : byte { Snapshot = 1, Report = 2, OrderState = 3, Event = 4, Heartbeat = 5 }

/// <summary>
/// ⭐ <c>CE-4676</c> — one item of a seat's feed: a sequence number, the sim time, a JSON form (for rails and tools) and ONE radio line (for the model).
/// Immutable: written on the frame thread, read on the mind's.
/// </summary>
public sealed record FeedItem(long Seq, double SimTime, FeedKind Kind, bool Wake, string Radio, string Json);

/// <summary>What a mind asks for (DESIGN_LLM_Commander §4 — the tools).</summary>
public enum CommanderActionKind : byte { Order = 1, Hold = 2, AskSitrep = 3, Note = 4, RunPlay = 5, StopPlay = 6, Decision = 7 }   // ⭐ CE-4680 B-1 — the platoon plays · ⭐ CE-4684 — one decision (§5c)

/// <summary>
/// ⭐ <c>CE-4676</c> — one action a mind took, made on the feed item <see cref="MadeOnSeq"/>. Plain text fields, exactly as a model's tool call gives them:
/// the action system resolves and validates every one (<see cref="CommanderActionSystem"/>).
/// </summary>
public sealed record CommanderAction(CommanderActionKind Kind, string? Squad = null, string? Order = null, string? Where = null, string? Target = null,
                                     string? Text = null)
{
    public long MadeOnSeq { get; init; }
    /// <summary>⭐ CE-4684 (§5c) — a <see cref="CommanderActionKind.Decision"/>'s content; the seat's ids it was validated against ride with it.</summary>
    public Decision? Plan { get; init; }
    public SeatVocabulary? Vocabulary { get; init; }
    /// <summary>⭐ CE-4684 C-2 — an action the condition watcher gave (a started order, a contingency, an abort): never a cycle check.</summary>
    public bool FromWatcher { get; init; }
}

/// <summary>
/// ⭐⭐ <c>CE-4676</c> (DESIGN_LLM_Commander §2, §5) — the ONE meeting point of a seat's two sides: the feed ring (out of the simulation, numbered) and the
/// action queue (into it). Thread-safe. ⛔ Nothing in it refers to the world: the frame thread writes items and drains actions; the mind's thread reads
/// items and writes actions.
/// </summary>
public sealed class CommanderChannel
{
    public const int Capacity = 512;

    private readonly object _gate = new();
    private readonly FeedItem?[] _ring = new FeedItem?[Capacity];
    private long _last;              // the newest seq written (0 = none)
    private long _lastWake;          // the newest seq that asks for an answer
    private long _answered;          // the newest seq the mind has answered (its actions are in the queue)
    private readonly ConcurrentQueue<CommanderAction> _actions = new();

    /// <summary>The newest item's seq (0 = none yet).</summary>
    public long LatestSeq { get { lock (_gate) return _last; } }

    /// <summary>The newest seq a mind must answer before a think-pause frame goes on (a wake item, or a heartbeat).</summary>
    public long LatestWake { get { lock (_gate) return _lastWake; } }

    /// <summary>The newest seq the mind answered.</summary>
    public long Answered { get { lock (_gate) return _answered; } }

    /// <summary>Frame thread: appends an item; returns its seq. A wake (or a heartbeat) asks the mind to think.</summary>
    public long Publish(FeedKind kind, double simTime, bool wake, string radio, string json)
    {
        long seq;
        lock (_gate)
        {
            seq = ++_last;
            _ring[(int)((seq - 1) % Capacity)] = new FeedItem(seq, simTime, kind, wake, radio, json);
            if (wake || kind == FeedKind.Heartbeat) _lastWake = seq;
            Monitor.PulseAll(_gate);   // ⭐ CE-4679 — every waiter (the mind's runner, an HTTP long-poll) wakes
        }
        Fdp.Core.Logging.FdpLog<CommanderChannel>.Info("[Commander] #{0} {1}", seq, radio);   // ⭐ the radio log in the node's log (on an item, never per frame)
        return seq;
    }

    /// <summary>Any thread: the items after <paramref name="after"/>, oldest first (an item the ring overwrote is gone — the reader is told by the gap in seq).</summary>
    public int ReadAfter(long after, List<FeedItem> into)
    {
        lock (_gate)
        {
            long from = Math.Max(after + 1, _last - Capacity + 1);
            int n = 0;
            for (long s = from; s <= _last; s++)
                if (_ring[(int)((s - 1) % Capacity)] is { } item && item.Seq == s) { into.Add(item); n++; }
            return n;
        }
    }

    /// <summary>The mind's thread: waits until an item newer than <paramref name="after"/> exists, or the timeout / cancellation.</summary>
    public bool WaitForItems(long after, TimeSpan timeout, CancellationToken ct)
    {
        var until = DateTime.UtcNow + timeout;
        lock (_gate)
        {
            while (_last <= after)
            {
                if (ct.IsCancellationRequested) return false;
                var left = until - DateTime.UtcNow;
                if (left <= TimeSpan.Zero) return false;
                Monitor.Wait(_gate, left < TimeSpan.FromMilliseconds(250) ? left : TimeSpan.FromMilliseconds(250));   // a slice, so a cancel is seen
            }
            return true;
        }
    }

    /// <summary>⭐ CE-4679 — a wake (or heartbeat) the mind has not answered yet.</summary>
    public bool Thinking { get { lock (_gate) return _answered < _lastWake; } }

    /// <summary>The mind's thread: its actions for the items through <paramref name="throughSeq"/>, then the mark that it answered them.</summary>
    public void Answer(long throughSeq, IReadOnlyList<CommanderAction> actions)
    {
        for (int i = 0; i < actions.Count; i++) _actions.Enqueue(actions[i] with { MadeOnSeq = throughSeq });
        lock (_gate)
            if (throughSeq > _answered) _answered = throughSeq;
    }

    /// <summary>⭐ CE-4684 C-2 — frame thread: an action the seat's condition watcher gives (no model call); the action system takes it like any.</summary>
    public void Inject(CommanderAction action) => _actions.Enqueue(action with { FromWatcher = true });

    /// <summary>Frame thread: the next action, if any.</summary>
    public bool TryTake(out CommanderAction action) => _actions.TryDequeue(out action!);

    /// <summary>Frame thread, think-pause: waits (bounded) until the mind answered every wake so far. False on the timeout.</summary>
    public bool WaitAnswered(TimeSpan timeout)
    {
        long want = LatestWake;
        if (Answered >= want) return true;
        var until = DateTime.UtcNow + timeout;
        var spin = new SpinWait();
        while (Answered < want)
        {
            if (DateTime.UtcNow >= until) return false;
            if (spin.NextSpinWillYield) Thread.Sleep(1); else spin.SpinOnce();
        }
        return true;
    }
}
