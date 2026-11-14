using System;
using System.Collections.Generic;

namespace Hrot.Presentation.Edits;

/// <summary>
/// ⭐⭐ CE-4218 (rules view R5a, docs/DESIGN_Rules_View_UI.md §7 / the measured undo row) — <b>THE ONE editor undo history</b>: a small linear list of named steps, each with its apply (redo) and its undo. Extracted from the Parameters window's
/// <c>ParamsEditHistory</c> (CE-4210 P2): the Parameters window AND the rule editor push to it, so one <i>Undo</i> reaches both.
/// <para>⭐ B-14 / Edit-mode undo U2 + U5 (📄 docs/UX/DESIGN_Edit_Mode_Undo.md §2.1, §2.2): world steps (<see cref="WorldUndoRecorder"/>) push here too — a
/// world step's undo / redo is a restore from the snapshot ring, and a step whose snapshot the ring evicted is NOT available (greyed, never a partial undo).
/// <see cref="MarkSaved"/> is the clean point: <see cref="IsDirty"/> = the position is not the saved one. <see cref="Clear"/> on open / create / Live.</para>
/// </summary>
public sealed class EditHistory : IUndoStepSink
{
    private sealed record Step(string Label, Action Redo, Action Undo, Func<bool>? Available, object? Owner, bool Dirties);

    private readonly List<Step> _done = new();
    private int _at;
    private int _savedAt;      // the position Save marked; -1 ⇒ the saved state is no longer reachable (a step after it was discarded)

    public bool CanUndo => _at > 0 && Runs(_done[_at - 1]);
    public bool CanRedo => _at < _done.Count && Runs(_done[_at]);

    /// <summary>⭐ U4 — who pushed the step an Undo would undo (null: no step, or an anonymous one). A surface that owns a KIND of step (the take editor's buttons) acts only on its own.</summary>
    public object? UndoOwner => _at > 0 ? _done[_at - 1].Owner : null;
    /// <summary>⭐ U4 — who pushed the step a Redo would redo.</summary>
    public object? RedoOwner => _at < _done.Count ? _done[_at].Owner : null;
    public string? UndoLabel => _at > 0 ? _done[_at - 1].Label : null;
    public string? RedoLabel => _at < _done.Count ? _done[_at].Label : null;
    public int Count => _done.Count;

    /// <summary>Bumped on every change of the list or the position (a UI caches by it).</summary>
    public int Version { get; private set; }

    /// <summary>⭐ U5 — the position is not the one <see cref="MarkSaved"/> marked: there are changes since the last save.</summary>
    public bool IsDirty
    {
        get
        {
            if (_savedAt < 0) return true;
            if (_at == _savedAt) return false;
            int lo = Math.Min(_at, _savedAt), hi = Math.Max(_at, _savedAt);
            for (int i = lo; i < hi; i++) if (_done[i].Dirties) return true;   // ⭐ U4 — a step of a document that is not the scenario (a take) does not dirty it
            return false;
        }
    }

    /// <summary>⭐ U5 — where the history stands: the number of steps done (0 = the state before the first step; <see cref="Count"/> = the newest).</summary>
    public int Position => _at;

    /// <summary>⭐ U5 — the label of step <paramref name="index"/> (0-based, oldest first).</summary>
    public string StepLabel(int index) => _done[index].Label;

    /// <summary>⭐ U5 — step <paramref name="index"/> can still be undone / redone (a world step whose snapshot the ring evicted, or a take that was closed, cannot).</summary>
    public bool StepAvailable(int index) => Runs(_done[index]);

    /// <summary>⭐ U5 — the history can be moved to <paramref name="position"/> by repeated Undo / Redo: every step on the way is available.</summary>
    public bool CanGoTo(int position)
    {
        if (position < 0 || position > _done.Count) return false;
        if (position < _at) { for (int i = _at - 1; i >= position; i--) if (!Runs(_done[i])) return false; }
        else { for (int i = _at; i < position; i++) if (!Runs(_done[i])) return false; }
        return true;
    }

    /// <summary>⭐ U5 — repeated Undo / Redo to <paramref name="position"/> (a click on the History list); false, and nothing moved, when a step on the way is unavailable.</summary>
    public bool GoTo(int position)
    {
        if (!CanGoTo(position)) return false;
        while (_at > position) if (!Undo()) return false;   // never loops without progress (a step that refuses)
        while (_at < position) if (!Redo()) return false;
        return true;
    }

    /// <summary>Applies the step and records it (a later step discards the redo tail).</summary>
    public void Do(string label, Action apply, Action undo)
    {
        Run(apply);
        Push(label, apply, undo);
    }

    /// <summary>⭐ CE-3386 — true while a step's apply / undo / redo runs: a world write made by it is PART of that step, so the world recorder records nothing
    /// (a Push from inside an Undo would cut the redo tail the undo stands in).</summary>
    public bool Applying => _applying > 0;
    private int _applying;

    private void Run(Action a) { _applying++; try { a(); } finally { _applying--; } }

    /// <summary>Records a step that has ALREADY been applied (its apply was the caller's own, e.g. one that can be refused).</summary>
    public void Push(string label, Action redo, Action undo) => Push(label, redo, undo, null, null, true);

    /// <summary>Records an applied step that can stop being available (<paramref name="available"/> false ⇒ greyed: a world step whose snapshot was evicted).
    /// <paramref name="owner"/> tags a kind of step (see <see cref="UndoOwner"/>); <paramref name="dirties"/> false ⇒ the step belongs to a document that is not the scenario (<see cref="IsDirty"/> ignores it).</summary>
    public void Push(string label, Action redo, Action undo, Func<bool>? available, object? owner = null, bool dirties = true)
    {
        if (_savedAt > _at) _savedAt = -1;   // the saved state was in the discarded redo tail
        _done.RemoveRange(_at, _done.Count - _at);
        _done.Add(new Step(label, redo, undo, available, owner, dirties));
        _at = _done.Count;
        Version++;
    }

    /// <inheritdoc/> — a single-threaded host: the step goes straight onto this history.
    void IUndoStepSink.Push(string label, Action redo, Action undo, long beforeSlot, Func<bool> available) => Push(label, redo, undo, available);

    public bool Undo() { if (!CanUndo) return false; Run(_done[--_at].Undo); Version++; return true; }
    public bool Redo() { if (!CanRedo) return false; Run(_done[_at++].Redo); Version++; return true; }

    /// <summary>⭐ U5 — the current position is the saved state.</summary>
    public void MarkSaved() { _savedAt = _at; Version++; }

    /// <summary>Forgets every step (a scenario opened / created, the station gone Live); the state now is the clean one.</summary>
    public void Clear() { _done.Clear(); _at = 0; _savedAt = 0; Version++; }

    private static bool Runs(Step s) => s.Available?.Invoke() ?? true;
}
