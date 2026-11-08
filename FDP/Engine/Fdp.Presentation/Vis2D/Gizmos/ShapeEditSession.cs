using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;

namespace Fdp.Toolkit.Vis2D.Gizmos;

/// <summary>What a drawing command does — the three rows of the drawing menu, and the keys behind them.</summary>
public enum ShapeCommand { Finish, RemoveLast, Cancel }

/// <summary>How a <see cref="ShapeEditSession"/> command ended.</summary>
public enum ShapeOutcome
{
    /// <summary>Nothing ended (a point added or removed, a key that is not the session's).</summary>
    None,
    /// <summary>Finished with enough points — the gizmo hands the points over and removes itself.</summary>
    Finished,
    /// <summary>Cancelled — the gizmo removes itself and hands nothing over.</summary>
    Cancelled,
    /// <summary>Finish was asked with too few points — <see cref="ShapeEditSession.Reason"/> says why; the tool STAYS armed.</summary>
    TooFew,
}

/// <summary>The drawing menu a host attaches to a session: the menu JSON (composed from the action catalogue) and what its ids mean.</summary>
public sealed record ShapeMenu(string Json, Func<int, ShapeCommand?> Map);

/// <summary>
/// ⭐⭐ B-17 P2 (📄 docs/UX/DESIGN_Map_Interaction.md §2.1, §2.2, §2.5 — D11) — THE ONE drawing core: a list of points and the finish / cancel convention.
/// Enter, the on-map ✓ or the menu's <i>Finish</i> all reach <see cref="Finish"/>; Esc reaches <see cref="Cancel"/>; Backspace is <see cref="RemoveLast"/>;
/// right-click is the MENU (the gizmo does nothing with it); too few points ⇒ <see cref="Finish"/> says why and the tool stays armed. Pure — no
/// drawing, no input device: each gizmo (<see cref="PointSequenceGizmo"/>, <see cref="AreaShapeGizmo"/>, and the editors of a drawn shape and a route in
/// Hrot.Presentation) keeps only its own drawing and its own result type. ⛔ A second copy of any of this in a gizmo is the defect the class exists to prevent.
/// </summary>
public sealed class ShapeEditSession
{
    private readonly List<Vector2> _points = new();
    private readonly List<float> _heights = new();
    private string? _reason;

    /// <param name="minPoints">The fewest points a finish needs.</param>
    /// <param name="closed">Whether the shape closes (an area: clicking its first point finishes it).</param>
    /// <param name="noun">What is being drawn, with its article — <i>"an area"</i>, <i>"a route"</i> — for the too-few reason.</param>
    public ShapeEditSession(int minPoints, bool closed, string noun = "a shape")
    {
        MinPoints = Math.Max(1, minPoints);
        Closed = closed;
        Noun = noun;
    }

    public int MinPoints { get; }
    public bool Closed { get; }
    public string Noun { get; }

    /// <summary>The points so far, in click order.</summary>
    public IReadOnlyList<Vector2> Points => _points;

    /// <summary>The points as an array — what a finish hands over.</summary>
    public Vector2[] ToArray() => _points.ToArray();

    /// <summary>Each point's level height (0 = the ground); parallel to <see cref="Points"/>.</summary>
    public IReadOnlyList<float> Heights => _heights;

    /// <summary>The point an editor has selected (-1 = none) — what <see cref="DeleteSelected"/> removes.</summary>
    public int Selected { get; set; } = -1;

    /// <summary>The last finish's refusal ("an area needs 3 points"), shown in place of the hint until the next click; null otherwise.</summary>
    public string? Reason
    {
        get => _reason;
        private set { if (_reason == value) return; _reason = value; ReasonChanged?.Invoke(value); }
    }

    /// <summary>Raised when <see cref="Reason"/> appears or clears — the host puts it on the tool's hint line (<c>ToolController.SetHint</c>).</summary>
    public event Action<string?>? ReasonChanged;

    /// <summary>How the session ended (<see cref="ShapeOutcome.None"/> while it runs; the first end sticks).</summary>
    public ShapeOutcome Outcome { get; private set; }

    /// <summary>True once finished or cancelled.</summary>
    public bool Ended => Outcome is ShapeOutcome.Finished or ShapeOutcome.Cancelled;

    /// <summary>The drawing menu the host attached (null = right-click opens nothing).</summary>
    public ShapeMenu? Menu { get; set; }

    /// <summary>The keys, as the hint line says them — the minimum and the finish, undo and cancel keys.</summary>
    public string KeysHint => $"click: add point · Enter / ✓: finish ({MinPoints}+) · Backspace: undo point · Esc: cancel";

    // ── editing the points ──────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Appends a point (a click). Clears any too-few reason.</summary>
    public void Add(Vector2 point, float height = 0f)
    {
        if (Ended) return;
        _points.Add(point);
        _heights.Add(height);
        Reason = null;
    }

    /// <summary>Removes the last point (Backspace). False when there is none.</summary>
    public bool RemoveLast()
    {
        if (Ended || _points.Count == 0) return false;
        _points.RemoveAt(_points.Count - 1);
        _heights.RemoveAt(_heights.Count - 1);
        if (Selected >= _points.Count) Selected = -1;
        Reason = null;
        return true;
    }

    /// <summary>Inserts <paramref name="at"/> after point <paramref name="segment"/> (the segment from that point to the next; the closing segment of a
    /// closed shape appends). Returns the new point's index.</summary>
    public int InsertOnSegment(int segment, Vector2 at)
    {
        int index = Math.Clamp(segment + 1, 0, _points.Count);
        float h = _heights.Count > 0 ? _heights[Math.Clamp(segment, 0, _heights.Count - 1)] : 0f;
        _points.Insert(index, at);
        _heights.Insert(index, h);
        Selected = index;
        return index;
    }

    /// <summary>Inserts the midpoint of segment <paramref name="segment"/> (to the next point, wrapping on a closed shape). Returns the index, -1 with fewer than 2 points.</summary>
    public int InsertMidpointAfter(int segment)
    {
        if (_points.Count < 2 || segment < 0 || segment >= _points.Count) return -1;
        int next = (segment + 1) % _points.Count;
        return InsertOnSegment(segment, (_points[segment] + _points[next]) * 0.5f);
    }

    /// <summary>Removes the selected point. Returns its index, -1 when none is selected.</summary>
    public int DeleteSelected()
    {
        int i = Selected;
        if (i < 0 || i >= _points.Count) return -1;
        _points.RemoveAt(i);
        _heights.RemoveAt(i);
        Selected = -1;
        return i;
    }

    /// <summary>Moves every point by <paramref name="delta"/> (the centre handle: the WHOLE shape moves).</summary>
    public void MoveAll(Vector2 delta)
    {
        for (int i = 0; i < _points.Count; i++) _points[i] += delta;
    }

    /// <summary>Replaces point <paramref name="index"/> (a vertex drag).</summary>
    public void Set(int index, Vector2 point)
    {
        if (index >= 0 && index < _points.Count) _points[index] = point;
    }

    /// <summary>Loads the points of an existing shape (an editor starts from what is there).</summary>
    public void Load(IEnumerable<Vector2> points)
    {
        _points.Clear(); _heights.Clear();
        foreach (var p in points) { _points.Add(p); _heights.Add(0f); }
        Selected = -1;
    }

    // ── the convention ──────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ THE finish — Enter, the ✓ and the menu's <i>Finish</i> all come here. Enough points ⇒ <see cref="ShapeOutcome.Finished"/>; too few ⇒
    /// <see cref="ShapeOutcome.TooFew"/> with <see cref="Reason"/> set, and the session stays open (a silent cancel loses the user's work).
    /// </summary>
    public ShapeOutcome Finish()
    {
        if (Ended) return Outcome;
        if (_points.Count < MinPoints)
        {
            Reason = MinPoints == 1 ? $"click to place {Noun} first" : $"{Noun} needs {MinPoints} points ({_points.Count} so far)";
            return ShapeOutcome.TooFew;
        }
        Outcome = ShapeOutcome.Finished;
        return Outcome;
    }

    /// <summary>⭐ THE cancel — Esc and the menu's <i>Cancel</i>.</summary>
    public ShapeOutcome Cancel()
    {
        if (Ended) return Outcome;
        Outcome = ShapeOutcome.Cancelled;
        return Outcome;
    }

    /// <summary>Runs a menu command.</summary>
    public ShapeOutcome Apply(ShapeCommand command)
    {
        switch (command)
        {
            case ShapeCommand.Finish: return Finish();
            case ShapeCommand.Cancel: return Cancel();
            default: RemoveLast(); return ShapeOutcome.None;
        }
    }

    /// <summary>A menu row's id ⇒ its command, through the attached <see cref="Menu"/>; null for an id that is not one of its rows.</summary>
    public ShapeOutcome ApplyMenuAction(int actionId)
        => Menu?.Map(actionId) is { } command ? Apply(command) : ShapeOutcome.None;

    /// <summary>The keys of the convention: Enter finishes, Backspace removes the last point, Esc cancels. Anything else is not the session's.</summary>
    public ShapeOutcome OnKey(MapKeyboardKey key, bool isPressed)
    {
        if (!isPressed) return ShapeOutcome.None;
        switch (key)
        {
            case MapKeyboardKey.Enter: return Finish();
            case MapKeyboardKey.Escape: return Cancel();
            case MapKeyboardKey.Backspace: RemoveLast(); return ShapeOutcome.None;
            default: return ShapeOutcome.None;
        }
    }

    // ── the on-map ✓ and the closing click (world units; sized from the shape, as AreaShapeHandles does) ──────────────────

    /// <summary>The extent of the points: the larger side of their bounding box (0 with fewer than 2).</summary>
    public float Extent
    {
        get
        {
            if (_points.Count < 2) return 0f;
            Bounds(out var min, out var max);
            return MathF.Max(max.X - min.X, max.Y - min.Y);
        }
    }

    /// <summary>How near a click must be to the first point to close an area (m): a tenth of the extent, between 2 and 40.</summary>
    public float FirstPointRadius => Math.Clamp(Extent * 0.1f, 2f, 40f);

    /// <summary>The ✓'s half size (m).</summary>
    public float DoneHalfSize => Math.Clamp(FirstPointRadius * 0.8f, 2f, 12f);

    /// <summary>Where the ✓ sits: beside the bounding box's top-right corner, clear of every point. Meaningful with at least one point.</summary>
    public Vector2 DonePosition
    {
        get
        {
            if (_points.Count == 0) return Vector2.Zero;
            Bounds(out _, out var max);
            return max + new Vector2(DoneHalfSize * 3f, DoneHalfSize * 3f);
        }
    }

    /// <summary>Is <paramref name="at"/> on the ✓? (False with no points: nothing to finish.)</summary>
    public bool HitDone(Vector2 at)
    {
        if (_points.Count == 0) return false;
        var d = DonePosition;
        float h = DoneHalfSize;
        return MathF.Abs(at.X - d.X) <= h && MathF.Abs(at.Y - d.Y) <= h;
    }

    /// <summary>Does <paramref name="at"/> hit the first point of a CLOSED shape that already has enough points — the click that closes it? (With fewer
    /// than <see cref="MinPoints"/> a click there is just another point: it cannot close yet.)</summary>
    public bool HitsFirstPoint(Vector2 at)
        => Closed && _points.Count >= MinPoints && Vector2.Distance(at, _points[0]) <= FirstPointRadius;

    private void Bounds(out Vector2 min, out Vector2 max)
    {
        min = max = _points[0];
        for (int i = 1; i < _points.Count; i++)
        {
            min = Vector2.Min(min, _points[i]);
            max = Vector2.Max(max, _points[i]);
        }
    }
}
