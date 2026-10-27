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
    /// <summary>A new camera key. CR-6: <paramref name="Anchor"/> is the index event the key's <c>At</c> hangs on — the take gets the anchor and its baked time in the SAME edit (one undo step).
    /// ⭐ <c>CE-4059</c> / CR-7 (§11q K3): <paramref name="Segment"/> (an index into <see cref="Take.Segments"/>) scopes the key to that segment — it gets an id in the same edit when it has none.</summary>
    public sealed record AddKey(CameraKey Key, EventAnchor? Anchor = null, int? Segment = null) : TakeEdit;
    public sealed record RemoveKey(int Index) : TakeEdit;
    /// <summary>Segment <paramref name="Index"/>: <paramref name="Edge"/> now at <paramref name="ToSim"/> (<see cref="SegmentEdge.Whole"/>: its start, same length).</summary>
    public sealed record MoveSegment(int Index, SegmentEdge Edge, double ToSim) : TakeEdit;
    /// <summary>A new segment (CE-1364): appended, speed and gap as <paramref name="Segment"/> says.</summary>
    public sealed record AddSegment(TimeSegment Segment) : TakeEdit;
    public sealed record RemoveSegment(int Index) : TakeEdit;
    /// <summary>⭐ <c>CE-1303</c> P2b (§11o S12) — a copy of segment <paramref name="Index"/> right after it: the film plays that stretch again (an instant replay).</summary>
    public sealed record ReplaySegment(int Index) : TakeEdit;
    /// <summary>⭐ P2b — segment <paramref name="Index"/>'s speed and gap (its times kept as <paramref name="Segment"/> says).</summary>
    public sealed record SetSegment(int Index, TimeSegment Segment) : TakeEdit;
    /// <summary>⭐ P2b (§11o S11) — "update key from view": camera key <paramref name="Index"/> takes <paramref name="Camera"/> (and its lens); its time is kept.</summary>
    public sealed record SetKeyCamera(int Index, CameraSpec Camera) : TakeEdit;
    /// <summary>⭐ P2b — CR-11: subtitle <paramref name="Index"/>'s <paramref name="Edge"/> now at <paramref name="ToSim"/> (<see cref="SegmentEdge.Whole"/>: its start, same length).</summary>
    public sealed record MoveSubtitle(int Index, SegmentEdge Edge, double ToSim) : TakeEdit;
    public sealed record AddSubtitle(Subtitle Subtitle) : TakeEdit;
    public sealed record RemoveSubtitle(int Index) : TakeEdit;
    /// <summary>⭐ CR-11 — subtitle <paramref name="Index"/>'s text, position, fade (its times as <paramref name="Subtitle"/> says).</summary>
    public sealed record SetSubtitle(int Index, Subtitle Subtitle) : TakeEdit;
    /// <summary>⭐ <c>CE-1304</c> P6 (§11t V3) — view item <paramref name="Index"/> now at <paramref name="ToSim"/> (its anchor kept).</summary>
    public sealed record MoveView(int Index, double ToSim) : TakeEdit;
    public sealed record AddView(ViewChange View) : TakeEdit;
    public sealed record RemoveView(int Index) : TakeEdit;
    /// <summary>⭐ CE-1304 P6 — view item <paramref name="Index"/>'s map and layers (its time as <paramref name="View"/> says).</summary>
    public sealed record SetView(int Index, ViewChange View) : TakeEdit;
    /// <summary>⭐ <c>CE-1305</c> P6 (§11t G4) — graphic <paramref name="Index"/>'s <paramref name="Edge"/> now at <paramref name="ToSim"/> (the span rule of a subtitle).</summary>
    public sealed record MoveGraphic(int Index, SegmentEdge Edge, double ToSim) : TakeEdit;
    public sealed record AddGraphic(FilmGraphic Graphic) : TakeEdit;
    public sealed record RemoveGraphic(int Index) : TakeEdit;
    public sealed record SetGraphic(int Index, FilmGraphic Graphic) : TakeEdit;
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
    private Take? _saved;

    /// <param name="unsaved">⭐ <c>CE-4059</c> (§11q D2) — the take was never written (a draft): it is <see cref="Dirty"/> from the start, so Save writes it.</param>
    public TakeEditor(TakeStore? store, Take take, GizmoUndoStack undo, bool unsaved = false)
    {
        _store = store;
        Take = take ?? throw new ArgumentNullException(nameof(take));
        _saved = unsaved ? null : take;
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
        // a v1 take's first edit of a KIND writes that kind as it plays: its shots as keys (camera edits), its captions as subtitles (subtitle edits)
        var basis = edit switch
        {
            TakeEdit.MoveKey or TakeEdit.AddKey or TakeEdit.RemoveKey or TakeEdit.SetKeyCamera => before.Keys.Count > 0 ? before : WithKeys(before),
            TakeEdit.MoveSubtitle or TakeEdit.AddSubtitle or TakeEdit.RemoveSubtitle or TakeEdit.SetSubtitle => before.Subtitles.Count > 0 ? before : WithSubtitles(before),
            // ⭐ CE-1304 P6 (§11t V2) — and its layers as views (the take's from its start, each shot's laid over them), so the first view edit changes nothing else
            TakeEdit.MoveView or TakeEdit.AddView or TakeEdit.RemoveView or TakeEdit.SetView => before.Views.Count > 0 ? before : WithViews(before),
            _ => before,
        };
        Take? after;
        string what;
        switch (edit)
        {
            case TakeEdit.MoveKey m: after = MoveKey(basis, m); what = "Move camera key"; break;
            case TakeEdit.AddKey a: after = AddKey(WithAnchor(basis, a.Anchor), a); what = "Add camera key"; break;
            case TakeEdit.RemoveKey r: after = RemoveKey(basis, r); what = "Delete camera key"; break;
            case TakeEdit.SetKeyCamera c: after = SetKeyCamera(basis, c); what = "Update key from view"; break;
            case TakeEdit.MoveSegment s: after = MoveSegment(basis, s); what = "Move time segment"; break;
            case TakeEdit.AddSegment a: after = AddSegment(basis, a.Segment); what = "Add time segment"; break;
            case TakeEdit.RemoveSegment r: after = RemoveSegment(basis, r); what = "Delete time segment"; break;
            case TakeEdit.ReplaySegment r: after = ReplaySegment(basis, r); what = "Replay time segment"; break;
            case TakeEdit.SetSegment s: after = (uint)s.Index < (uint)basis.Segments.Count && basis.Segments[s.Index] != s.Segment && s.Segment.Speed > 0
                                                ? basis with { Segments = Replace(basis.Segments, s.Index, s.Segment) } : null; what = "Change time segment"; break;
            case TakeEdit.MoveSubtitle m: after = MoveSubtitle(basis, m); what = "Move subtitle"; break;
            case TakeEdit.AddSubtitle a: after = basis with { Subtitles = basis.Subtitles.Append(a.Subtitle).ToList() }; what = "Add subtitle"; break;
            case TakeEdit.RemoveSubtitle r: after = (uint)r.Index < (uint)basis.Subtitles.Count
                                                ? basis with { Subtitles = basis.Subtitles.Where((_, i) => i != r.Index).ToList() } : null; what = "Delete subtitle"; break;
            case TakeEdit.SetSubtitle s: after = (uint)s.Index < (uint)basis.Subtitles.Count && basis.Subtitles[s.Index] != s.Subtitle
                                                ? basis with { Subtitles = Replace(basis.Subtitles, s.Index, s.Subtitle) } : null; what = "Change subtitle"; break;
            case TakeEdit.MoveView m: after = MoveView(basis, m); what = "Move view change"; break;
            case TakeEdit.AddView a: after = basis with { Views = basis.Views.Append(a.View).ToList() }; what = "Add view change"; break;
            case TakeEdit.RemoveView r: after = (uint)r.Index < (uint)basis.Views.Count
                                                ? basis with { Views = basis.Views.Where((_, i) => i != r.Index).ToList() } : null; what = "Delete view change"; break;
            case TakeEdit.SetView s: after = (uint)s.Index < (uint)basis.Views.Count && basis.Views[s.Index] != s.View
                                                ? basis with { Views = Replace(basis.Views, s.Index, s.View) } : null; what = "Change view"; break;
            case TakeEdit.MoveGraphic m: after = MoveGraphic(basis, m); what = "Move graphic"; break;
            case TakeEdit.AddGraphic a: after = basis with { Graphics = basis.Graphics.Append(a.Graphic).ToList() }; what = "Add graphic"; break;
            case TakeEdit.RemoveGraphic r: after = (uint)r.Index < (uint)basis.Graphics.Count
                                                ? basis with { Graphics = basis.Graphics.Where((_, i) => i != r.Index).ToList() } : null; what = "Delete graphic"; break;
            case TakeEdit.SetGraphic s: after = (uint)s.Index < (uint)basis.Graphics.Count && basis.Graphics[s.Index] != s.Graphic
                                                ? basis with { Graphics = Replace(basis.Graphics, s.Index, s.Graphic) } : null; what = "Change graphic"; break;
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

    /// <summary>⭐ CR-7 (§11q K3) — the key appended; scoped to segment <c>a.Segment</c> when given (null when there is no such segment), which gets an id here if it has none.</summary>
    private static Take? AddKey(Take take, TakeEdit.AddKey a)
    {
        var key = a.Key;
        if (a.Segment is { } i)
        {
            if ((uint)i >= (uint)take.Segments.Count) return null;
            var seg = take.Segments[i];
            if (seg.Id is null)
            {
                int n = 1;
                while (take.Segments.Any(s => s.Id == "s" + n)) n++;
                seg = seg with { Id = "s" + n };
                take = take with { Segments = Replace(take.Segments, i, seg) };
            }
            key = key with { Segment = seg.Id };
        }
        return take with { Keys = take.Keys.Append(key).ToList() };
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
        var gone = segments[r.Index].Id;
        segments.RemoveAt(r.Index);
        // ⭐ CR-7 (§11q K4) — the keys of a deleted segment go with it (same undo step): no other pass could ever show them
        return take with { Segments = segments, Keys = gone is null ? take.Keys : take.Keys.Where(k => k.Segment != gone).ToList() };
    }

    private static Take? MoveSegment(Take take, TakeEdit.MoveSegment s)
    {
        if ((uint)s.Index >= (uint)take.Segments.Count) return null;
        var seg = take.Segments[s.Index];
        if (MoveSpan(take, seg.From, seg.To, s.Edge, s.ToSim) is not { } span) return null;
        var moved = seg with { From = span.From, To = span.To };
        return moved == seg ? null : take with { Segments = Replace(take.Segments, s.Index, moved) };
    }

    private static Take? MoveSubtitle(Take take, TakeEdit.MoveSubtitle s)
    {
        if ((uint)s.Index >= (uint)take.Subtitles.Count) return null;
        var sub = take.Subtitles[s.Index];
        if (MoveSpan(take, sub.At, sub.Until, s.Edge, s.ToSim) is not { } span) return null;
        var moved = sub with { At = span.From, Until = span.To };
        return moved == sub ? null : take with { Subtitles = Replace(take.Subtitles, s.Index, moved) };
    }

    private static Take? MoveView(Take take, TakeEdit.MoveView m)
    {
        if ((uint)m.Index >= (uint)take.Views.Count) return null;
        var view = take.Views[m.Index];
        var at = Retimed(take, view.At, m.ToSim);
        return at == view.At ? null : take with { Views = Replace(take.Views, m.Index, view with { At = at }) };
    }

    private static Take? MoveGraphic(Take take, TakeEdit.MoveGraphic s)
    {
        if ((uint)s.Index >= (uint)take.Graphics.Count) return null;
        var g = take.Graphics[s.Index];
        if (MoveSpan(take, g.At, g.Until, s.Edge, s.ToSim) is not { } span) return null;
        var moved = g with { At = span.From, Until = span.To };
        return moved == g ? null : take with { Graphics = Replace(take.Graphics, s.Index, moved) };
    }

    /// <summary>⭐ CE-1304 P6 (§11t V2) — a take whose views are its layers READ as views gets them written, as they play, by its first view edit.</summary>
    private static Take WithViews(Take take)
        => take with { Views = TakeViews.Of(take, TakeTimeline.Plan(take, n => take.Baked is { } b && b.Times.TryGetValue(n, out var t) ? t : null)).ToList() };

    /// <summary>⭐ CE-1364 / P2b (§11o S10) — the ONE span rule of a segment and a subtitle: the body moves both ends (same length), an edge only itself and
    /// it stops <see cref="MinSegmentSeconds"/> short of the other; each end keeps its anchor (the offset changes). Null when an end has no time.</summary>
    private static (AtExpr From, AtExpr To)? MoveSpan(Take take, AtExpr fromAt, AtExpr toAt, SegmentEdge edge, double toSim)
    {
        if (Resolve(take, fromAt) is not { } from || Resolve(take, toAt) is not { } to) return null;
        return edge switch
        {
            SegmentEdge.Whole => (Retimed(take, fromAt, toSim), Retimed(take, toAt, toSim + (to - from))),
            SegmentEdge.From => (Retimed(take, fromAt, Math.Min(toSim, to - MinSegmentSeconds)), toAt),
            _ => (fromAt, Retimed(take, toAt, Math.Max(toSim, from + MinSegmentSeconds))),
        };
    }

    /// <summary>⭐ P2b (§11o S12) — a new segment goes where the source order puts it (before the first segment that starts later), so a segment added
    /// EARLIER on the axis is not silently a replay; a replay is made on purpose (<see cref="TakeEdit.ReplaySegment"/>).</summary>
    private static Take AddSegment(Take take, TimeSegment segment)
    {
        var list = take.Segments.ToList();
        double at = Resolve(take, segment.From) ?? segment.From.Offset;
        int index = list.FindIndex(s => Resolve(take, s.From) is { } f && f > at + 1e-9);
        list.Insert(index < 0 ? list.Count : index, segment);
        return take with { Segments = list };
    }

    private static Take? ReplaySegment(Take take, TakeEdit.ReplaySegment r)
    {
        if ((uint)r.Index >= (uint)take.Segments.Count) return null;
        var list = take.Segments.ToList();
        list.Insert(r.Index + 1, list[r.Index] with { Gap = SegmentGap.Cut, Id = null });   // ⭐ §11q — a copy has no id: the original's keys stay the original's
        return take with { Segments = list };
    }

    private static Take? SetKeyCamera(Take take, TakeEdit.SetKeyCamera c)
    {
        if ((uint)c.Index >= (uint)take.Keys.Count) return null;
        var key = take.Keys[c.Index];
        var updated = key with { Camera = c.Camera, Fov = c.Camera.Fov };
        return updated == key ? null : take with { Keys = Replace(take.Keys, c.Index, updated) };
    }

    /// <summary>⭐ CR-11 (§11o S8) — a take whose subtitles are its captions READ as subtitles gets them written, as they play, by its first subtitle edit.</summary>
    private static Take WithSubtitles(Take take)
        => take with { Subtitles = TakeSubtitles.Of(take, TakeTimeline.Plan(take, n => take.Baked is { } b && b.Times.TryGetValue(n, out var t) ? t : null)).ToList() };

    private static List<T> Replace<T>(IReadOnlyList<T> list, int index, T item)
    {
        var copy = list.ToList();
        copy[index] = item;
        return copy;
    }
}
