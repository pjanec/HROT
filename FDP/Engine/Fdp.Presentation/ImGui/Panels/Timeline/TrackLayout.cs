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

    /// <summary>The sim seconds a drop at <paramref name="x"/> means (clamped into the window); <paramref name="snapTo"/> snaps it to the nearest frame of
    /// the (axis-projected) index first. Null when not <see cref="Drawable"/>.</summary>
    public static double? SimAtX(float x, TimelineWindow window, float width, TimelineAxis axis, ReplayTimeMap? map, TimelineIndex? snapTo = null)
    {
        long ticks = window.TicksAt(Math.Clamp(x, 0, width), width);
        if (snapTo != null) ticks = TimelineLayout.SnapToFrame(snapTo, ticks);
        return SimSeconds(ticks, axis, map);
    }

    /// <summary>Offers <paramref name="key"/> to the track that holds the selected item (the first such); true when it took it. Nothing selected ⇒ false, no call.</summary>
    public static bool OfferKey(IReadOnlyList<ITimelineTrack> tracks, char key, double atSimSeconds)
    {
        foreach (var t in tracks)
            foreach (var it in t.Items)
                if (it.Selected) return t.Key(key, atSimSeconds);
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
    private string? _itemId;
    private float _pressX, _grab;
    private bool _moved;
    private double _lastSim;

    public bool Active => _track != null;
    public ITimelineTrack? Track => _track;

    /// <param name="x">The mouse x in the row.</param><param name="itemX">The item's x (the grab offset: the item does not jump under the mouse).</param>
    public void Press(ITimelineTrack track, TimelineTrackItem? item, float x, float itemX)
    {
        if (item == null) return;   // ⛔ a drag on empty track space does nothing
        track.Select(item.Id);
        _track = track; _itemId = item.Id; _pressX = x; _grab = x - itemX; _moved = false;
    }

    /// <summary>The mouse is held at <paramref name="x"/>; <paramref name="simAt"/> maps an item x to sim seconds (null = not placeable).</summary>
    public void Update(float x, Func<float, double?> simAt)
    {
        if (_track == null || _itemId == null) return;
        if (!_moved && Math.Abs(x - _pressX) < ThresholdPx) return;   // a click with jitter is still a click
        if (simAt(x - _grab) is not { } sim) return;
        _moved = true;
        _lastSim = sim;
        _track.Move(_itemId, sim, commit: false);
    }

    /// <summary>The button went up — wherever the mouse is. Commits once if the item moved; returns whether it did.</summary>
    public bool Release()
    {
        bool commit = _track != null && _itemId != null && _moved;
        if (commit) _track!.Move(_itemId!, _lastSim, commit: true);
        _track = null; _itemId = null; _moved = false;
        return commit;
    }
}
