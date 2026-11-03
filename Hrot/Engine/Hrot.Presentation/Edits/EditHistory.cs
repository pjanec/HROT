using System;
using System.Collections.Generic;

namespace Hrot.Presentation.Edits;

/// <summary>
/// ⭐⭐ CE-4218 (rules view R5a, docs/DESIGN_Rules_View_UI.md §7 / the measured undo row) — <b>THE ONE editor undo history</b>: a small linear list of named steps, each with its apply (redo) and its undo. Extracted from the Parameters window's
/// <c>ParamsEditHistory</c> (CE-4210 P2): the Parameters window AND the rule editor push to it, so one <i>Undo</i> reaches both. ⛔ There is still no scenario-wide undo (measured: none exists); the remaining private stacks are listed on tracker row CE-4219 to fold later.
/// </summary>
public sealed class EditHistory
{
    private readonly List<(string Label, Action Redo, Action Undo)> _done = new();
    private int _at;

    public bool CanUndo => _at > 0;
    public bool CanRedo => _at < _done.Count;
    public string? UndoLabel => CanUndo ? _done[_at - 1].Label : null;
    public string? RedoLabel => CanRedo ? _done[_at].Label : null;
    public int Count => _done.Count;

    /// <summary>Applies the step and records it (a later step discards the redo tail).</summary>
    public void Do(string label, Action apply, Action undo)
    {
        apply();
        Push(label, apply, undo);
    }

    /// <summary>Records a step that has ALREADY been applied (its apply was the caller's own, e.g. one that can be refused).</summary>
    public void Push(string label, Action redo, Action undo)
    {
        _done.RemoveRange(_at, _done.Count - _at);
        _done.Add((label, redo, undo));
        _at = _done.Count;
    }

    public bool Undo() { if (!CanUndo) return false; _done[--_at].Undo(); return true; }
    public bool Redo() { if (!CanRedo) return false; _done[_at++].Redo(); return true; }
}
