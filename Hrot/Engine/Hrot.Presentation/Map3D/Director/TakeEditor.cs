using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Interfaces;
using Fdp.Toolkit.Diagnostics.Gizmos.UndoRedo;

namespace Hrot.UI.Common.Map3D.Director;

/// <summary>⭐ <c>CE-1364</c> T3 — which end of a <see cref="TimeSegment"/> an edit moves (<see cref="Whole"/> keeps the length).</summary>
public enum SegmentEdge : byte { Whole, From, To }

/// <summary>⭐ <c>CE-1364</c> T3 (docs/DESIGN_Director_Timeline_Editor.md §10) — one edit of a TAKE (never of the world, §2c row 2).</summary>
public abstract record TakeEdit
{
    /// <summary>Camera key <paramref name="Index"/> (into <see cref="Take.Keys"/>) now at <paramref name="ToSim"/> sim seconds.</summary>
    public sealed record MoveKey(int Index, double ToSim) : TakeEdit;
    /// <summary>A new camera key. CR-6: <paramref name="Anchor"/> is the index event the key's <c>At</c> hangs on — the take gets the anchor and its baked time in the SAME edit (one undo step).</summary>
    public sealed record AddKey(CameraKey Key, EventAnchor? Anchor = null) : TakeEdit;
    public sealed record RemoveKey(int Index) : TakeEdit;
    /// <summary>Segment <paramref name="Index"/>: <paramref name="Edge"/> now at <paramref name="ToSim"/> (<see cref="SegmentEdge.Whole"/>: its start, same length).</summary>
    public sealed record MoveSegment(int Index, SegmentEdge Edge, double ToSim) : TakeEdit;
    /// <summary>A new segment (CE-1364): appended, speed and gap as <paramref name="Segment"/> says.</summary>
    public sealed record AddSegment(TimeSegment Segment) : TakeEdit;
    public sealed record RemoveSegment(int Index) : TakeEdit;
}

/// <summary>
/// ⭐ <c>CE-1364</c> T3 (§10, T-L2) — one undo step of a take: the take before and after. Rides the editor's EXISTING
/// <see cref="GizmoUndoStack"/> (no fifth stack); the stack's <see cref="IEntityCommandBuffer"/> argument is unused by a take — the known wart.
/// </summary>
public sealed class TakeEditRecord : IGizmoUndoRecord
{
    private readonly TakeEditor _editor;
    private readonly Take _before, _after;

    public TakeEditRecord(TakeEditor editor, Take before, Take after, string description)
    {
        _editor = editor; _before = before; _after = after; Description = description;
    }

    public string Description { get; }
    public void Undo(IEntityCommandBuffer cmd) => _editor.Restore(_before);
    public void Redo(IEntityCommandBuffer cmd) => _editor.Restore(_after);
}

/// <summary>
/// ⭐ <c>CE-1364</c> T3 (§10, T-L2 / T-L3 / T-L4) — the ONE edit path of an open take: the timeline panel's tracks and the Director window both go through it.
/// It holds the take being edited, applies an edit (recording the before / after on the undo stack), keeps a PREVIEW (a drag shown without touching the
/// take) and saves through <see cref="TakeStore"/> — only when the take was edited (T-L4: a seeded v1 take stays a v1 file until touched).
/// </summary>
public sealed class TakeEditor
{
    /// <summary>CE-1364: the shortest a time segment may be (sim seconds) — an edge cannot cross the other edge.</summary>
    public const double MinSegmentSeconds = 0.1d;
    /// <summary>CE-1364: the length of a segment added from the menu (sim seconds).</summary>
    public const double DefaultSegmentSeconds = 2d;

    private readonly TakeStore? _store;
    private readonly GizmoUndoStack _undo;
    private Take _saved;

    public TakeEditor(TakeStore? store, Take take, GizmoUndoStack undo)
    {
        _store = store;
        Take = take ?? throw new ArgumentNullException(nameof(take));
        _saved = take;
        _undo = undo ?? throw new ArgumentNullException(nameof(undo));
    }

    /// <summary>The take as edited so far.</summary>
    public Take Take { get; private set; }

    /// <summary>Counts every change of <see cref="Take"/> (an edit, an undo, a redo); a preview does not count.</summary>
    public int Version { get; private set; }

    /// <summary>Raised after every change of <see cref="Take"/>.</summary>
    public event Action? Changed;

    /// <summary>The take differs from the one last read or saved.</summary>
    public bool Dirty => !ReferenceEquals(Take, _saved);

    /// <summary>A drag in progress: the item id and where it is shown. ⛔ Not in <see cref="Take"/>, not undoable.</summary>
    public (string ItemId, double Sim, SegmentEdge Edge)? Preview { get; private set; }

    public void SetPreview(string itemId, double sim, SegmentEdge edge = SegmentEdge.Whole) => Preview = (itemId, sim, edge);
    public void ClearPreview() => Preview = null;

    /// <summary>A fixed time, or an anchor's baked time plus the offset; null when the anchor has no time.</summary>
    public double? Resolve(AtExpr at) => Resolve(Take, at);

    private static double? Resolve(Take take, AtExpr at) =>
        at.Resolve(n => take.Baked is { } b && b.Times.TryGetValue(n, out var t) ? t : null);

    /// <summary>Applies <paramref name="edit"/>; false (nothing changed) when it names no item or an anchor with no time.</summary>
    public bool Apply(TakeEdit edit)
    {
        var before = Take;
        var basis = before.Keys.Count > 0 || edit is TakeEdit.MoveSegment or TakeEdit.AddSegment or TakeEdit.RemoveSegment ? before : WithKeys(before);
        Take? after;
        string what;
        switch (edit)
        {
            case TakeEdit.MoveKey m: after = MoveKey(basis, m); what = "Move camera key"; break;
            case TakeEdit.AddKey a: after = WithAnchor(basis, a.Anchor) with { Keys = basis.Keys.Append(a.Key).ToList() }; what = "Add camera key"; break;
            case TakeEdit.RemoveKey r: after = RemoveKey(basis, r); what = "Delete camera key"; break;
            case TakeEdit.MoveSegment s: after = MoveSegment(basis, s); what = "Move time segment"; break;
            case TakeEdit.AddSegment a: after = basis with { Segments = basis.Segments.Append(a.Segment).ToList() }; what = "Add time segment"; break;
            case TakeEdit.RemoveSegment r: after = RemoveSegment(basis, r); what = "Delete time segment"; break;
            default: return false;
        }
        if (after is null || ReferenceEquals(after, before)) return false;
        Take = after;
        Preview = null;
        Version++;
        _undo.Push(new TakeEditRecord(this, before, after, what));
        Changed?.Invoke();
        return true;
    }

    /// <summary>Puts <paramref name="take"/> back (an undo or a redo); not an edit, so nothing is pushed.</summary>
    internal void Restore(Take take)
    {
        Take = take;
        Preview = null;
        Version++;
        Changed?.Invoke();
    }

    /// <summary>
    /// Writes the take to its file — format v2 (<c>keys[]</c> / <c>segments[]</c>); null and NOTHING written when the take was not edited (T-L4: a
    /// seeded v1 take is never re-written by looking at it).
    /// </summary>
    public string? Save()
    {
        if (!Dirty || _store == null) return null;
        var path = _store.Save(Take);
        _saved = Take;
        return path;
    }

    // ── the edits ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A v1 take edited for the first time: its shots become its keys — <b>as they PLAY</b> (<see cref="CameraKeys.Played"/>: a shot listed after one still
    /// running starts later than its <c>at</c>) but keeping the anchor of a key whose time the layout did not move (<see cref="CameraKeys.Of"/>), so a
    /// seeded take does not film differently — and does not lose its anchors — because one key was nudged.
    /// </summary>
    private static Take WithKeys(Take take)
    {
        var raw = CameraKeys.Of(take);
        var played = CameraKeys.Played(take, TakeTimeline.Plan(take, n => take.Baked is { } b && b.Times.TryGetValue(n, out var t) ? t : null));
        var keys = new List<CameraKey>(raw.Count);
        for (int i = 0; i < raw.Count; i++)
        {
            var key = raw[i];
            if (raw.Count == played.Count && Resolve(take, key.At) is { } a && Resolve(take, played[i].At) is { } p && Math.Abs(a - p) > 1e-9)
                key = played[i];
            keys.Add(key);
        }
        return take with { Keys = keys };
    }

    /// <summary>
    /// ⭐ CR-6 — the take with <paramref name="anchor"/> defined (its <see cref="AnchorSpec"/>) and baked (its event's sim time, the take's scenario hash and step rate)
    /// unless the take already has both; an existing bake keeps its time (the keys written against it stay where they are). Null anchor = the take unchanged.
    /// </summary>
    private Take WithAnchor(Take take, EventAnchor? anchor)
    {
        if (anchor is null) return take;
        var anchors = take.Anchors.ContainsKey(anchor.Name)
            ? take.Anchors
            : new Dictionary<string, AnchorSpec>(take.Anchors, StringComparer.Ordinal) { [anchor.Name] = anchor.Spec };
        var baked = take.Baked ?? new BakedAnchors { TickHz = take.TickHz, ScenarioHash = _store?.ScenarioHash(take.Scenario) ?? "" };
        if (!baked.Times.ContainsKey(anchor.Name))
            baked = baked with { Times = new Dictionary<string, double>(baked.Times, StringComparer.Ordinal) { [anchor.Name] = anchor.EventSim } };
        return take with { Anchors = anchors, Baked = baked };
    }

    private static AtExpr Retimed(Take take, AtExpr at, double sim)
    {
        sim = Math.Max(sim, 0d);
        if (at.Anchor is null) return new AtExpr(sim);
        return take.Baked is { } b && b.Times.TryGetValue(at.Anchor, out var anchor) ? new AtExpr(sim - anchor, at.Anchor) : at;
    }

    private static Take? MoveKey(Take take, TakeEdit.MoveKey m)
    {
        if ((uint)m.Index >= (uint)take.Keys.Count) return null;
        var keys = take.Keys.ToList();
        var key = keys[m.Index];
        var at = Retimed(take, key.At, m.ToSim);
        if (at == key.At) return null;
        keys[m.Index] = key with { At = at };
        return take with { Keys = keys };
    }

    private static Take? RemoveKey(Take take, TakeEdit.RemoveKey r)
    {
        if ((uint)r.Index >= (uint)take.Keys.Count) return null;
        var keys = take.Keys.ToList();
        keys.RemoveAt(r.Index);
        return take with { Keys = keys };
    }

    private static Take? RemoveSegment(Take take, TakeEdit.RemoveSegment r)
    {
        if ((uint)r.Index >= (uint)take.Segments.Count) return null;
        var segments = take.Segments.ToList();
        segments.RemoveAt(r.Index);
        return take with { Segments = segments };
    }

    private static Take? MoveSegment(Take take, TakeEdit.MoveSegment s)
    {
        if ((uint)s.Index >= (uint)take.Segments.Count) return null;
        var seg = take.Segments[s.Index];
        var from = Resolve(take, seg.From);
        var to = Resolve(take, seg.To);
        if (from is null || to is null) return null;
        TimeSegment moved;
        switch (s.Edge)
        {
            case SegmentEdge.Whole:
                moved = seg with { From = Retimed(take, seg.From, s.ToSim), To = Retimed(take, seg.To, s.ToSim + (to.Value - from.Value)) };
                break;
            case SegmentEdge.From:
                moved = seg with { From = Retimed(take, seg.From, Math.Min(s.ToSim, to.Value - MinSegmentSeconds)) };
                break;
            default:
                moved = seg with { To = Retimed(take, seg.To, Math.Max(s.ToSim, from.Value + MinSegmentSeconds)) };
                break;
        }
        if (moved == seg) return null;
        var segments = take.Segments.ToList();
        segments[s.Index] = moved;
        return take with { Segments = segments };
    }
}
