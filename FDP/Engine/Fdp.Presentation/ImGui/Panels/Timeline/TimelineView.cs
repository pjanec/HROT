using System;
using System.Collections.Generic;

namespace Fdp.Presentation.Panels.Timeline;

/// <summary>How the window follows time by itself (DAR §10b "follow modes").</summary>
public enum TimelineFollow : byte
{
    /// <summary>The window stays where the user put it.</summary>
    None = 0,
    /// <summary>⭐ The window is pinned to the extent's END (the live edge) and slides as it grows — until the user pans or zooms away.</summary>
    Tail = 1,
}

/// <summary>
/// ⭐ <c>CE-3330</c> (docs/DESIGN_Replay_Time_Axis_And_Review.md §10; docs/DESIGN_Director_Timeline_Editor.md §2d) — the timeline's VIEW as
/// pure state, tested without ImGui: the visible window, the extent, the follow mode, a selected range, the zoom history. Every rule of
/// §10 that is not drawing lives here:
/// <list type="bullet">
/// <item><b>T1</b> — a growing extent never resets the view: a fitted or tailing view keeps tailing, any other is only clamped;</item>
/// <item><b>T5/T7</b> — a zoom is proportional to the wheel (<see cref="WheelFactor"/>) and stops at <see cref="MinSpanFrames"/> frames;</item>
/// <item><b>T12</b> — while playing, <see cref="KeepInView"/> pages the window so the playhead stays in it;</item>
/// <item>fit, the last N seconds, a range, zoom history (Backspace).</item>
/// </list>
/// The view is the VIEWER's (§10b: two operators zoom independently) — the host keeps one per panel.
/// </summary>
public sealed class TimelineView
{
    /// <summary>The closest zoom: about this many frames across the panel (§10c, T7).</summary>
    public const int MinSpanFrames = 10;

    /// <summary>A page during play leaves the playhead this far into the new window.</summary>
    public const float PageLead = 0.1f;

    private const int HistoryDepth = 32;
    private readonly List<TimelineWindow> _history = new();
    private bool _fitted;

    public TimelineWindow Window { get; private set; }
    public long ExtentFrom { get; private set; }
    public long ExtentTo { get; private set; }
    public TimelineFollow Follow { get; set; } = TimelineFollow.Tail;

    /// <summary>A selected span (Shift+drag), or null.</summary>
    public (long From, long To)? Range { get; set; }

    /// <summary>The closest span allowed (ticks) — <see cref="MinSpanFrames"/> frames at the recording's frame spacing.</summary>
    public long MinSpan { get; set; } = TimeSpan.TicksPerSecond / 6;

    public bool HasExtent => ExtentTo > ExtentFrom;

    /// <summary>
    /// The extent this frame (it grows on a live source). ⛔ T1: the view is NOT reset to it. The first extent fits; a fitted view stays
    /// fitted; a tailing view keeps its span and slides so its end is the extent's end; any other view is only clamped.
    /// </summary>
    public void OnExtent(long from, long to)
    {
        if (to <= from) { (ExtentFrom, ExtentTo) = (from, to); return; }
        bool first = !HasExtent;
        bool tailing = Follow == TimelineFollow.Tail && HasExtent && Window.To >= ExtentTo;
        (ExtentFrom, ExtentTo) = (from, to);
        if (first && Window.Span > 1 && Window.To > from && Window.From < to)
        {
            // a window SHOWN before the first extent (a host's jump) is kept, clamped — not refitted
            Window = TimelineLayout.Clamp(Window, from, to);
            _fitted = Window.Span >= to - from;
            Follow = Window.To >= to ? TimelineFollow.Tail : TimelineFollow.None;
            return;
        }
        if (first || _fitted || Window.Span <= 1) { Window = new TimelineWindow(from, to); _fitted = true; return; }
        if (tailing) { Window = TimelineLayout.Clamp(new TimelineWindow(to - Window.Span, to), from, to); return; }
        Window = TimelineLayout.Clamp(Window, from, to);
    }

    /// <summary>Shows <paramref name="window"/> (a host jumping to an event, a rail); remembered in the history.</summary>
    public void Show(TimelineWindow window)
    {
        Set(HasExtent ? TimelineLayout.Clamp(window, ExtentFrom, ExtentTo) : window, remember: true);
        _fitted = HasExtent && Window.Span >= ExtentTo - ExtentFrom;   // a shown window is the user's, not a fit (it pages, it is kept)
        Follow = HasExtent && Window.To >= ExtentTo ? TimelineFollow.Tail : TimelineFollow.None;
    }

    /// <summary>⭐ §10c — the zoom factor for a wheel delta: <c>1.0015^(−delta×120)</c> — a notch ≈ 16 %, a trackpad's small deltas smooth (T5).</summary>
    public static double WheelFactor(float wheelDelta) => Math.Pow(1.0015, -wheelDelta * 120.0);

    /// <summary>Zoom by <paramref name="factor"/> keeping the tick under <paramref name="x"/> where it is (T5, T7).</summary>
    public void Zoom(float x, float width, double factor)
    {
        if (!HasExtent) return;
        Set(TimelineLayout.ZoomAbout(Window, x, width, (float)factor, ExtentFrom, ExtentTo, MinSpan), remember: false);
        _fitted = Window.Span >= ExtentTo - ExtentFrom;
        if (!_fitted && Window.To < ExtentTo) Follow = TimelineFollow.None;   // zoomed away from the live edge
    }

    /// <summary>Pan by <paramref name="dxPixels"/> (positive = earlier); panning away from the end stops the tail (§10b).</summary>
    public void Pan(float dxPixels, float width)
    {
        if (!HasExtent) return;
        Set(TimelineLayout.Pan(Window, dxPixels, width, ExtentFrom, ExtentTo), remember: false);
        _fitted = false;
        Follow = Window.To >= ExtentTo ? TimelineFollow.Tail : TimelineFollow.None;
    }

    /// <summary>Fit the whole extent (double-click the ruler, <c>F</c>); it then stays fitted as the extent grows.</summary>
    public void Fit()
    {
        Set(new TimelineWindow(ExtentFrom, ExtentTo), remember: true);
        _fitted = true;
        Follow = TimelineFollow.Tail;
    }

    /// <summary>The last <paramref name="span"/> ticks before the extent's end (presets 1–3: 30 s, 2 min, 10 min), tailing.</summary>
    public void Last(long span)
    {
        if (!HasExtent) return;
        Set(TimelineLayout.Clamp(new TimelineWindow(ExtentTo - span, ExtentTo), ExtentFrom, ExtentTo), remember: true);
        _fitted = Window.Span >= ExtentTo - ExtentFrom;
        Follow = TimelineFollow.Tail;
    }

    /// <summary>Zoom to the selected range (no-op without one).</summary>
    public void ZoomToRange()
    {
        if (Range is not { } r || r.To <= r.From) return;
        Set(TimelineLayout.Clamp(new TimelineWindow(r.From, Math.Max(r.To, r.From + MinSpan)), ExtentFrom, ExtentTo), remember: true);
        _fitted = false;
        Follow = TimelineFollow.None;
    }

    /// <summary>⭐ T12 — while playing, a playhead that leaves the window pages it so the playhead sits <see cref="PageLead"/> into the
    /// new one. Tailing needs nothing (the window follows the end).</summary>
    public void KeepInView(long now, bool playing)
    {
        if (!playing || !HasExtent || _fitted) return;
        if (now >= Window.From && now <= Window.To) return;
        long span = Window.Span;
        long from = now - (long)(span * PageLead);
        Set(TimelineLayout.Clamp(new TimelineWindow(from, from + span), ExtentFrom, ExtentTo), remember: false);
    }

    /// <summary>Bring <paramref name="ticks"/> into view, centred, at the current zoom (the off-screen playhead arrow).</summary>
    public void Reveal(long ticks)
    {
        long half = Window.Span / 2;
        Set(TimelineLayout.Clamp(new TimelineWindow(ticks - half, ticks + half), ExtentFrom, ExtentTo), remember: true);
        _fitted = false;
        Follow = Window.To >= ExtentTo ? TimelineFollow.Tail : TimelineFollow.None;
    }

    /// <summary>A window set by a drag (the overview's bracket or its edges): clamped, NOT remembered (the drag remembered once, at its
    /// start — <see cref="Remember"/>), at least <see cref="MinSpan"/> wide.</summary>
    public void DragTo(TimelineWindow window)
    {
        if (!HasExtent) return;
        if (window.Span < MinSpan) window = new TimelineWindow(window.From, window.From + MinSpan);
        Window = TimelineLayout.Clamp(window, ExtentFrom, ExtentTo);
        _fitted = Window.Span >= ExtentTo - ExtentFrom;
        Follow = Window.To >= ExtentTo ? TimelineFollow.Tail : TimelineFollow.None;
    }

    /// <summary>⭐ <c>CE-3330</c> (§10b) — the axis changed: the window and the range are mapped onto it (the same FRAMES stay in view);
    /// the history is dropped (it is in the old axis's numbers). The next <see cref="OnExtent"/> clamps them.</summary>
    public void Remap(Func<long, long> map)
    {
        var w = new TimelineWindow(map(Window.From), map(Window.To));
        if (w.To <= w.From) w = new TimelineWindow(w.From, w.From + MinSpan);
        Window = w;
        if (Range is { } r) Range = (map(r.From), map(r.To));
        _history.Clear();
        ExtentFrom = ExtentTo = 0;   // the extent is re-read on this axis next frame; a shown window is kept (OnExtent's first case)
    }

    /// <summary>Zoom history (Backspace): the previous remembered view; false when there is none.</summary>
    public bool Back()
    {
        if (_history.Count == 0) return false;
        var w = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        Window = TimelineLayout.Clamp(w, ExtentFrom, ExtentTo);
        _fitted = Window.Span >= ExtentTo - ExtentFrom;
        Follow = Window.To >= ExtentTo ? TimelineFollow.Tail : TimelineFollow.None;
        return true;
    }

    /// <summary>Remember the current view (before a drag-zoom or a jump), so <see cref="Back"/> returns to it.</summary>
    public void Remember()
    {
        if (_history.Count > 0 && _history[^1] == Window) return;
        _history.Add(Window);
        if (_history.Count > HistoryDepth) _history.RemoveAt(0);
    }

    private void Set(TimelineWindow window, bool remember)
    {
        if (remember && window != Window) Remember();
        Window = window;
    }
}
