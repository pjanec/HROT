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
public sealed class EditHistory
{
    private sealed record Step(string Label, Action Redo, Action Undo, Func<bool>? Available);

    private readonly List<Step> _done = new();
    private int _at;
    private int _savedAt;      // the position Save marked; -1 ⇒ the saved state is no longer reachable (a step after it was discarded)

    public bool CanUndo => _at > 0 && Runs(_done[_at - 1]);
    public bool CanRedo => _at < _done.Count && Runs(_done[_at]);
    public string? UndoLabel => _at > 0 ? _done[_at - 1].Label : null;
    public string? RedoLabel => _at < _done.Count ? _done[_at].Label : null;
    public int Count => _done.Count;

    /// <summary>Bumped on every change of the list or the position (a UI caches by it).</summary>
    public int Version { get; private set; }

    /// <summary>⭐ U5 — the position is not the one <see cref="MarkSaved"/> marked: there are changes since the last save.</summary>
    public bool IsDirty => _at != _savedAt;

    /// <summary>Applies the step and records it (a later step discards the redo tail).</summary>
    public void Do(string label, Action apply, Action undo)
    {
        apply();
        Push(label, apply, undo);
    }

    /// <summary>Records a step that has ALREADY been applied (its apply was the caller's own, e.g. one that can be refused).</summary>
    public void Push(string label, Action redo, Action undo) => Push(label, redo, undo, null);

    /// <summary>Records an applied step that can stop being available (<paramref name="available"/> false ⇒ greyed: a world step whose snapshot was evicted).</summary>
    public void Push(string label, Action redo, Action undo, Func<bool>? available)
    {
        if (_savedAt > _at) _savedAt = -1;   // the saved state was in the discarded redo tail
        _done.RemoveRange(_at, _done.Count - _at);
        _done.Add(new Step(label, redo, undo, available));
        _at = _done.Count;
        Version++;
    }

    public bool Undo() { if (!CanUndo) return false; _done[--_at].Undo(); Version++; return true; }
    public bool Redo() { if (!CanRedo) return false; _done[_at++].Redo(); Version++; return true; }

    /// <summary>⭐ U5 — the current position is the saved state.</summary>
    public void MarkSaved() { _savedAt = _at; Version++; }

    /// <summary>Forgets every step (a scenario opened / created, the station gone Live); the state now is the clean one.</summary>
    public void Clear() { _done.Clear(); _at = 0; _savedAt = 0; Version++; }

    private static bool Runs(Step s) => s.Available?.Invoke() ?? true;
}
