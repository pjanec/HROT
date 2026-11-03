using System;
using System.Collections.Generic;
using Fdp.Core.FlightRecorder;
using Fdp.Toolkit.ReplayBrowser.Timeline;

namespace Fdp.Presentation.Panels.Timeline;

/// <summary>Where an item sits in the panel's x (pixels from the row's left edge); a key has <c>X1 == X0</c>.</summary>
public readonly record struct TrackItemSpan(float X0, float X1);

/// <summary>
/// ⭐ <c>CE-1364</c> T2 (docs/DESIGN_Director_Timeline_Editor.md §10) — the pure rules of the track rows, so they are rails without ImGui: an item's
/// SIM seconds ⇄ the active axis's ticks ⇄ x, on BOTH axes. Sim axis: ticks = seconds × <see cref="TimeSpan.TicksPerSecond"/>. Wall axis: through the
/// session's <see cref="ReplayTimeMap"/> (<c>WallAt</c> / <c>SimAt</c>) — a key inside a collapsed pause sits at the pause's wall position, and a
/// drop there converts back to the pause's sim instant; with no map (a host without sim time) an item cannot be placed at all.
/// </summary>
public static class TrackLayout
{
    public const float KeySlackPx = 5f;

    // ── ⭐ CE-4167 X2 — how an item is drawn (pure, so each style's geometry is a rail) ──────────────────────────────────────────────

    /// <summary>What to draw for one item: <c>Fill</c> a filled body · <c>Dashed</c> a dashed outline (else a solid one) · <c>Hatch</c> diagonal lines inside · <c>Handles</c> the two edge grips ·
    /// <c>Diamond</c> a diamond at the start · <c>HollowDiamond</c> the diamond unfilled · <c>Tick</c> a vertical tick through the row at the start.</summary>
    public readonly record struct ItemDraw(bool Fill, bool Dashed, bool Hatch, bool Handles, bool Diamond, bool HollowDiamond, bool Tick);

    /// <summary>The look of <paramref name="item"/> when its body is <paramref name="widthPx"/> wide. ⭐ <see cref="TimelineTrackItemStyle.Solid"/> is exactly the pre-X2 drawing.</summary>
    public static ItemDraw DrawOf(TimelineTrackItem item, float widthPx)
    {
        bool segment = item.Kind == TimelineTrackItemKind.Segment;
        if (!segment)
            return item.Style switch
            {
                TimelineTrackItemStyle.Expected => new(false, true, false, false, true, true, false),
                TimelineTrackItemStyle.Pinned => new(false, false, false, false, true, false, true),
                _ => new(false, false, false, false, true, false, false),   // Solid (and Hatched, which a key has no body for)
            };
        bool handles = widthPx >= 6f;
        return item.Style switch
        {
            TimelineTrackItemStyle.Expected => new(false, true, false, false, false, false, false),
            TimelineTrackItemStyle.Hatched => new(false, false, true, false, false, false, false),
            TimelineTrackItemStyle.Pinned => new(true, false, false, handles, true, false, true),
            _ => new(true, false, false, handles, false, false, false),
        };
    }

    /// <summary>The dashes of a line from <paramref name="a"/> to <paramref name="b"/>: <paramref name="dash"/> on, <paramref name="gap"/> off, the last one clipped to the end.</summary>
    public static List<(float A, float B)> Dashes(float a, float b, float dash = 4f, float gap = 3f)
    {
        var list = new List<(float, float)>();
        if (b <= a || dash <= 0f) return list;
        for (float x = a; x < b; x += dash + gap) list.Add((x, Math.Min(x + dash, b)));
        return list;
    }

    /// <summary>The diagonal hatch of a <paramref name="width"/> × <paramref name="height"/> rectangle (relative to its top-left): lines rising to the right every <paramref name="spacing"/> px,
    /// each clipped to the rectangle — (x0, y0) to (x1, y1).</summary>
    public static List<(float X0, float Y0, float X1, float Y1)> HatchLines(float width, float height, float spacing = 5f)
    {
        var list = new List<(float, float, float, float)>();
        if (width <= 0f || height <= 0f || spacing <= 0f) return list;
        for (float start = -height; start < width; start += spacing)
        {
            // the line y = height − (x − start), from (start, height) up to (start + height, 0), clipped to x in [0, width]
            float xa = Math.Max(start, 0f), xb = Math.Min(start + height, width);
            if (xb <= xa) continue;
            list.Add((xa, height - (xa - start), xb, height - (xb - start)));
        }
        return list;
    }

    // ── ⭐ CE-4167 X3 — tree rows ────────────────────────────────────────────────────────────────────────────────────────────────────

    public const float IndentPx = 12f, ArrowPx = 12f, IconPx = 14f, LabelPadPx = 4f;

    /// <summary>The label column's width: the flat <paramref name="flat"/> one unless some track has a <see cref="TrackHeader"/> (a tree needs room), then <paramref name="tree"/>.</summary>
    public static float LabelWidthFor(IReadOnlyList<ITimelineTrack>? tracks, float flat, float tree)
    {
        if (tracks != null) foreach (var t in tracks) if (t.Header != null) return tree;
        return flat;
    }

    /// <summary>Where a header's parts sit in a label column <paramref name="labelWidth"/> wide: the arrow, the icon, the text (x from the column's left) and the status (right-aligned by
    /// <paramref name="statusWidth"/>, null when there is none or it would not fit after the text).</summary>
    public readonly record struct HeaderPlan(float? ArrowX, float? IconX, float TextX, float? StatusX);

    public static HeaderPlan PlanHeader(TrackHeader h, float labelWidth, float textWidth, float statusWidth)
    {
        float x = LabelPadPx + Math.Max(0, h.Depth) * IndentPx;
        float? arrow = null, icon = null;
        if (h.Collapsible) { arrow = x; x += ArrowPx; }
        else if (h.Depth > 0 || h.Icon != null) x += 0f;
        if (h.Icon is { Length: > 0 }) { icon = x; x += IconPx; }
        float textX = x;
        float? statusX = null;
        if (h.Status is { Length: > 0 })
        {
            float sx = labelWidth - LabelPadPx - statusWidth;
            if (sx >= textX + textWidth + LabelPadPx) statusX = sx;
        }
        return new HeaderPlan(arrow, icon, textX, statusX);
    }

    /// <summary>The tracks to DRAW: in the given (tree) order, minus those hidden by a collapsed node — a collapsible header whose id is in <paramref name="collapsed"/> hides the tracks after it that are
    /// DEEPER, up to the next track at its depth or shallower (siblings stay; a header-less track is depth 0, so it ends any collapse). No header anywhere ⇒ the very same list.</summary>
    public static IReadOnlyList<ITimelineTrack> Visible(IReadOnlyList<ITimelineTrack> tracks, ICollection<string> collapsed)
    {
        if (collapsed.Count == 0) return tracks;
        var list = new List<ITimelineTrack>(tracks.Count);
        int hideBelow = int.MaxValue;   // while a collapsed node is open: its depth
        foreach (var t in tracks)
        {
            int depth = t.Header?.Depth ?? 0;
            if (hideBelow != int.MaxValue && depth > hideBelow) continue;
            hideBelow = int.MaxValue;
            list.Add(t);
            if (t.Header is { Collapsible: true } && collapsed.Contains(t.Id)) hideBelow = depth;
        }
        return list;
    }

    // ── ⭐ CE-4167 RV-2 — an extent from the tracks ──────────────────────────────────────────────────────────────────────────────

    /// <summary>The shortest extent the tracks alone give (s): the view's own minimum span stays untouched.</summary>
    public const double MinTrackExtentSeconds = 10.0;

    /// <summary>
    /// ⭐ RV-2 — with no recording the panel has no extent, so the TRACKS give one: <c>[min(0, every item's From), max(now, every item's To (else From))]</c> in sim seconds, at least
    /// <see cref="MinTrackExtentSeconds"/> wide. Null when no track holds an item (nothing to draw) — unless <paramref name="extentWithoutItems"/> and there are tracks: then [0, now ∨ 10 s]. ⛔ Only used when the index has none — a recording keeps its own extent.
    /// </summary>
    public static (double From, double To)? ExtentOfTracks(IReadOnlyList<ITimelineTrack>? tracks, double nowSim, bool extentWithoutItems = false)
    {
        if (tracks == null) return null;
        double from = 0d, to = Math.Max(0d, nowSim);
        bool any = false;
        foreach (var t in tracks)
            foreach (var it in t.Items)
            {
                any = true;
                from = Math.Min(from, it.From);
                to = Math.Max(to, it.To ?? it.From);
                to = Math.Max(to, it.From);
            }
        if (!any && !(extentWithoutItems && tracks.Count > 0)) return null;   // ⭐ CE-4216 RV-3a: a host opts in to a ruler over tracks that hold no items yet (authored rules, not running)
        if (to - from < MinTrackExtentSeconds) to = from + MinTrackExtentSeconds;
        return (from, to);
    }

    /// <summary>Whether items can be placed on <paramref name="axis"/> at all (Sim: always; Wall: only with a map that has sim time).</summary>
    public static bool Drawable(TimelineAxis axis, ReplayTimeMap? map) => axis.IsSim || map is { HasSimTime: true };

    /// <summary>A sim time on the axis's ticks; null when not <see cref="Drawable"/>.</summary>
    public static long? AxisTicks(double simSeconds, TimelineAxis axis, ReplayTimeMap? map)
    {
        if (axis.IsSim) return (long)Math.Round(simSeconds * TimeSpan.TicksPerSecond);
        return map is { HasSimTime: true } ? map.WallAt(simSeconds) : null;
    }

    /// <summary>The axis's ticks as sim seconds (a wall time inside a pause is the pause's instant); null when not <see cref="Drawable"/>.</summary>
    public static double? SimSeconds(long axisTicks, TimelineAxis axis, ReplayTimeMap? map)
    {
        if (axis.IsSim) return axisTicks / (double)TimeSpan.TicksPerSecond;
        return map is { HasSimTime: true } ? map.SimAt(axisTicks) : null;
    }

    /// <summary>Where <paramref name="item"/> sits in a window <paramref name="width"/> px wide, or null (not placeable, or wholly outside the window).</summary>
    public static TrackItemSpan? SpanOf(TimelineTrackItem item, TimelineWindow window, float width, TimelineAxis axis, ReplayTimeMap? map)
    {
        if (AxisTicks(item.From, axis, map) is not { } a) return null;
        long b = item.To is { } to && AxisTicks(to, axis, map) is { } bt ? Math.Max(bt, a) : a;
        float x0 = window.XOf(a, width), x1 = window.XOf(b, width);
        return x1 < 0 || x0 > width ? null : new TrackItemSpan(x0, x1);
    }

    /// <summary>The item under <paramref name="x"/>: a key within <paramref name="slackPx"/> (the nearest wins), else a segment containing it; −1 for empty space.</summary>
    public static int HitTest(IReadOnlyList<TimelineTrackItem> items, TimelineWindow window, float width, TimelineAxis axis, ReplayTimeMap? map,
        float x, float slackPx = KeySlackPx)
    {
        int best = -1;
        float bestD = float.MaxValue;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Kind != TimelineTrackItemKind.Key || SpanOf(items[i], window, width, axis, map) is not { } s) continue;
            float d = Math.Abs(x - s.X0);
            if (d <= slackPx && d < bestD) { best = i; bestD = d; }
        }
        if (best >= 0) return best;
        for (int i = 0; i < items.Count; i++)
            if (items[i].Kind == TimelineTrackItemKind.Segment && SpanOf(items[i], window, width, axis, map) is { } s && x >= s.X0 && x <= Math.Max(s.X1, s.X0 + 2f)) return i;
        return -1;
    }

    public const float EdgePx = 5f;

    /// <summary>⭐ <c>CE-1364</c> — which part of <paramref name="item"/> the pixel <paramref name="x"/> grabs: a segment's first / last <see cref="EdgePx"/> px (at most a
    /// third of its width, so a narrow segment keeps a body) are its edges; a key and everything else is the body.</summary>
    public static TrackItemPart HitPart(TimelineTrackItem item, TimelineWindow window, float width, TimelineAxis axis, ReplayTimeMap? map, float x)
    {
        if (item.Kind != TimelineTrackItemKind.Segment || SpanOf(item, window, width, axis, map) is not { } s) return TrackItemPart.Body;
        float edge = Math.Min(EdgePx, (s.X1 - s.X0) / 3f);
        if (edge < 2f) return TrackItemPart.Body;   // under 6 px wide: no room for handles
        if (x <= s.X0 + edge) return TrackItemPart.StartEdge;
        return x >= s.X1 - edge ? TrackItemPart.EndEdge : TrackItemPart.Body;
    }

    /// <summary>The sim seconds a drop at <paramref name="x"/> means (clamped into the window); <paramref name="snapTo"/> snaps it to the nearest frame of
    /// the (axis-projected) index first. Null when not <see cref="Drawable"/>.</summary>
    public static double? SimAtX(float x, TimelineWindow window, float width, TimelineAxis axis, ReplayTimeMap? map, TimelineIndex? snapTo = null)
    {
        long ticks = window.TicksAt(Math.Clamp(x, 0, width), width);
        if (snapTo != null) ticks = TimelineLayout.SnapToFrame(snapTo, ticks);
        return SimSeconds(ticks, axis, map);
    }

    /// <summary>Offers <paramref name="key"/> to the track that holds the selected item first; when it declines — or nothing is selected (K on an empty
    /// timeline adds a key at the playhead, §2c row 3) — to the other tracks in order. True when one took it. ⭐ <c>T3</c>: was "nothing selected ⇒ nobody
    /// asked", which left K with no way to add the first key.</summary>
    public static bool OfferKey(IReadOnlyList<ITimelineTrack> tracks, char key, double atSimSeconds)
    {
        ITimelineTrack? holder = null;
        foreach (var t in tracks)
        {
            foreach (var it in t.Items)
                if (it.Selected) { holder = t; break; }
            if (holder != null) break;
        }
        if (holder != null && holder.Key(key, atSimSeconds)) return true;
        foreach (var t in tracks)
            if (!ReferenceEquals(t, holder) && t.Key(key, atSimSeconds)) return true;
        return false;
    }
}

/// <summary>
/// ⭐ <c>CE-1364</c> T2 (§2c row 2) — one left press → drag → release on a track item, as a pure state machine: a press on an item SELECTS it (never moves,
/// never seeks); a drag past <see cref="ThresholdPx"/> previews <c>Move(commit:false)</c> each update; the release commits ONCE (and only if it moved);
/// a press on empty space starts nothing. The panel feeds it the mouse and the conversion; it holds no ImGui.
/// </summary>
public sealed class TrackGesture
{
    public const float ThresholdPx = 3f;

    private ITimelineTrack? _track;
    private TrackItemPart _part;
    private string? _itemId;
    private float _pressX, _grab;
    private bool _moved;
    private double _lastSim;

    public bool Active => _track != null;
    public ITimelineTrack? Track => _track;

    /// <param name="x">The mouse x in the row.</param><param name="itemX">The grabbed part's x (the grab offset: the item does not jump under the mouse).</param><param name="part">CE-1364: the part grabbed (a segment's edge).</param>
    public void Press(ITimelineTrack track, TimelineTrackItem? item, float x, float itemX, TrackItemPart part = TrackItemPart.Body)
    {
        if (item == null) return;   // ⛔ a drag on empty track space does nothing
        track.Select(item.Id);
        _track = track; _part = part; _itemId = item.Id; _pressX = x; _grab = x - itemX; _moved = false;
    }

    /// <summary>The mouse is held at <paramref name="x"/>; <paramref name="simAt"/> maps an item x to sim seconds (null = not placeable).</summary>
    public void Update(float x, Func<float, double?> simAt)
    {
        if (_track == null || _itemId == null) return;
        if (!_moved && Math.Abs(x - _pressX) < ThresholdPx) return;   // a click with jitter is still a click
        if (simAt(x - _grab) is not { } sim) return;
        _moved = true;
        _lastSim = sim;
        _track.Move(_itemId, sim, commit: false, _part);
    }

    /// <summary>The button went up — wherever the mouse is. Commits once if the item moved; returns whether it did.</summary>
    public bool Release()
    {
        bool commit = _track != null && _itemId != null && _moved;
        if (commit) _track!.Move(_itemId!, _lastSim, commit: true, _part);
        _track = null; _itemId = null; _moved = false;
        return commit;
    }
}
