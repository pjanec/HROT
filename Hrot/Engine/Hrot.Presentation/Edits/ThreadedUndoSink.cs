using System;
using System.Collections.Generic;
using Fdp.Core.Threading;
using Fdp.Toolkit.Replay;

namespace Hrot.Presentation.Edits;

/// <summary>Where <see cref="WorldUndoRecorder"/> puts a recorded step: the history itself (a single-threaded host) or a threaded node's sink.</summary>
public interface IUndoStepSink
{
    /// <summary>An undo / redo is being applied (a write inside it is part of THAT step, never a new one).</summary>
    bool Applying { get; }
    /// <summary>Changes at every push, undo and redo (the recorder coalesces only when nothing happened in between).</summary>
    int Version { get; }
    /// <summary>One recorded step; <paramref name="beforeSlot"/> is the ring slot its undo restores (its availability).</summary>
    void Push(string label, Action redo, Action undo, long beforeSlot, Func<bool> available);
}

/// <summary>
/// ⭐ N-7 (CE-4469; U-1, D-5 — undo IS a world snapshot, path-independent) — the undo of a THREADED node. The user's ONE history stays on the UI
/// thread (it also holds the header / AI-edit / take steps); the world's steps are RECORDED on the sim thread, where every write now happens
/// (the recorder installed on S, its bracket's before-snapshot taken there), and handed to the UI history through the staging's publish queue —
/// each step's undo / redo a sim JOB that restores S from the ring. The step's availability is a UI-side token, cleared by the ring's
/// <see cref="WorldSnapshotRing.Evicted"/> through the same queue: the UI never reads the ring.
/// </summary>
public sealed class ThreadedUndoSink : IUndoStepSink
{
    private sealed class Token { public bool Held = true; }

    private readonly EditHistory _ui;
    private readonly IPresentationOutbox _toUi;
    private readonly Action<string, Action> _toSim;
    private readonly Dictionary<long, Token> _tokens = new();   // UI thread only
    private bool _applying;   // sim thread
    private int _version;     // sim thread

    public ThreadedUndoSink(EditHistory uiHistory, IPresentationOutbox toUi, Action<string, Action> toSim, WorldSnapshotRing ring)
    {
        _ui = uiHistory ?? throw new ArgumentNullException(nameof(uiHistory));
        _toUi = toUi ?? throw new ArgumentNullException(nameof(toUi));
        _toSim = toSim ?? throw new ArgumentNullException(nameof(toSim));
        if (ring is null) throw new ArgumentNullException(nameof(ring));
        ring.Evicted += slot => _toUi.Post(_ => { if (_tokens.Remove(slot, out var t)) t.Held = false; });   // the sim thread raises it
    }

    /// <inheritdoc/> — the sim thread's (a restore job running).
    public bool Applying => _applying;
    /// <inheritdoc/> — the sim thread's.
    public int Version => _version;

    /// <inheritdoc/> — the sim thread, at a bracket's end: the step goes to the UI history with the next publish.
    public void Push(string label, Action redo, Action undo, long beforeSlot, Func<bool> available)
    {
        _version++;
        _toUi.Post(_ =>
        {
            var token = new Token();
            _tokens[beforeSlot] = token;
            _ui.Push(label,
                redo: () => _toSim("redo: " + label, () => Run(redo)),
                undo: () => _toSim("undo: " + label, () => Run(undo)),
                available: () => token.Held);
        });
    }

    /// <summary>The sim thread: the scenario was saved — the UI history's clean point.</summary>
    public void MarkSaved() => _toUi.Post(_ => _ui.MarkSaved());

    /// <summary>The sim thread: a scenario was opened / cleared — the UI history starts over.</summary>
    public void Cleared() => _toUi.Post(_ => { _ui.Clear(); _tokens.Clear(); });

    private void Run(Action apply)
    {
        _applying = true;
        _version++;
        try { apply(); }
        finally { _applying = false; }
    }
}
