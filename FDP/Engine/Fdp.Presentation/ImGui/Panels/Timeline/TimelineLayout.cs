using System;
using System.Collections.Generic;
using Fdp.Toolkit.ReplayBrowser.Timeline;

namespace Fdp.Presentation.Panels.Timeline;

/// <summary>⭐ <c>CE-1302</c> R1-b — the visible span of a timeline, in the recording's wall-clock ticks (§9 TL-4).</summary>
public readonly record struct TimelineWindow(long From, long To)
{
    public long Span => Math.Max(1, To - From);
    public float XOf(long ticks, float width) => (float)((ticks - From) / (double)Span * width);
    public long TicksAt(float x, float width) => From + (long)(x / Math.Max(1f, width) * Span);
}

public enum TimelineMarkKind : byte { Density, Glyph }

/// <summary>
/// ⭐ <c>CE-1302</c> R1-b — one thing to draw on a lane row: a density bin (<see cref="Intensity"/> 0…1, log-scaled) or a glyph standing for
/// <see cref="Count"/> events (a cluster when &gt; 1). <see cref="Top"/> is the highest-priority lane in it (§2b "priority on top");
/// <see cref="FromFrame"/>…<see cref="ToFrame"/> (exclusive) are the frames it covers, so a tooltip asks the index for its events.
/// </summary>
public readonly record struct TimelineMark(TimelineMarkKind Kind, float X0, float X1, int Count, TimelineLane Top, float Intensity, int FromFrame, int ToFrame);

/// <summary>
/// ⭐ <c>CE-1302</c> R1-b (docs/DESIGN_Director_Timeline_Editor.md §2b, §9) — §2b's drawing rules as pure maths, tested without ImGui:
/// <list type="bullet">
/// <item><b>density when crowded, glyphs when sparse</b> — more events in the window than pixels ⇒ bins of <c>binPx</c>, intensity
///   log-scaled against the busiest bin; else a glyph per event;</item>
/// <item><b>cluster, never overlap</b> — glyphs closer than <c>glyphPx</c> merge into one with a count;</item>
/// <item><b>priority on top</b> — a cluster or bin over several lanes (a collapsed group) shows its highest-priority lane;</item>
/// <item><b>zoom about the cursor</b> / pan / the overview bracket.</item>
/// </list>
/// ⭐ Cost is pixels × lanes × log(events): a bin is two binary searches (<see cref="TimelineIndex.Count"/>), never a walk.
/// </summary>
public static class TimelineLayout
{
    public const float DefaultGlyphPx = 7f;
    public const float DefaultBinPx = 2f;

    /// <summary>The frames [from, to) whose ticks fall inside <paramref name="window"/>.</summary>
    public static (int From, int To) FramesIn(TimelineIndex index, TimelineWindow window)
    {
        var t = index.FrameTicks;
        if (t.Count == 0) return (0, 0);
        int from = index.FrameAt(window.From);
        if (t[from] < window.From) from++;
        int to = index.FrameAt(window.To) + 1;
        if (t[to - 1] > window.To) to--;
        return (Math.Min(from, t.Count), Math.Max(Math.Min(to, t.Count), Math.Min(from, t.Count)));
    }

    /// <summary>The marks for one row — a single lane, or a collapsed group of lanes.</summary>
    public static List<TimelineMark> Marks(TimelineIndex index, IReadOnlyList<TimelineLane> lanes, TimelineWindow window, float width,
        float glyphPx = DefaultGlyphPx, float binPx = DefaultBinPx, List<TimelineMark>? into = null)
    {
        var marks = into ?? new List<TimelineMark>();
        marks.Clear();
        if (width <= 0 || lanes.Count == 0 || index.FrameTicks.Count == 0) return marks;
        var (f0, f1) = FramesIn(index, window);
        if (f1 <= f0) return marks;

        int total = 0;
        for (int i = 0; i < lanes.Count; i++) total += index.Count(lanes[i], f0, f1);
        if (total == 0) return marks;
        if (total > width) Density(index, lanes, window, width, binPx, f0, f1, marks);
        else Glyphs(index, lanes, window, width, glyphPx, f0, f1, marks);
        return marks;
    }

    private static void Density(TimelineIndex index, IReadOnlyList<TimelineLane> lanes, TimelineWindow window, float width, float binPx,
        int f0, int f1, List<TimelineMark> marks)
    {
        // Contiguous bins by frame edges: edge[b] is the first frame at or after the bin's left pixel, edge[bins] the window's end — so no
        // event falls between two bins, and a lane's position at each edge is ONE binary search (bins + 1 per lane, not 2 × bins).
        int bins = Math.Max(1, (int)(width / binPx));
        var edges = EdgesFor(index, window, width, bins, f0, f1);
        var counts = new int[bins];
        var tops = new int[bins];
        Array.Fill(tops, -1);
        foreach (var lane in lanes)
        {
            int p = TimelineLanes.PriorityOf(lane);
            int prev = index.Before(lane, edges[0]);
            for (int b = 0; b < bins; b++)
            {
                int next = index.Before(lane, edges[b + 1]);
                int c = next - prev;
                prev = next;
                if (c == 0) continue;
                counts[b] += c;
                if (p > tops[b]) tops[b] = p;
            }
        }
        int max = 0;
        foreach (int c in counts) max = Math.Max(max, c);
        double scale = Math.Log(1 + max);
        for (int b = 0; b < bins; b++)
        {
            if (counts[b] == 0) continue;
            var top = lanes[0];
            foreach (var lane in lanes) if (TimelineLanes.PriorityOf(lane) == tops[b]) { top = lane; break; }
            float intensity = scale <= 0 ? 1f : (float)(Math.Log(1 + counts[b]) / scale);
            marks.Add(new TimelineMark(TimelineMarkKind.Density, b * width / bins, (b + 1) * width / bins, counts[b], top, intensity, edges[b], edges[b + 1]));
        }
    }

    // The edges depend on the window, not the lane: every row of one draw shares them (one cached set per thread).
    [ThreadStatic] private static (TimelineIndex? Index, TimelineWindow Window, float Width, int Bins, int[]? Edges) _edgeCache;

    private static int[] EdgesFor(TimelineIndex index, TimelineWindow window, float width, int bins, int f0, int f1)
    {
        var c = _edgeCache;
        if (c.Edges != null && ReferenceEquals(c.Index, index) && c.Window == window && c.Width == width && c.Bins == bins) return c.Edges;
        var edges = new int[bins + 1];
        edges[0] = f0;
        edges[bins] = f1;
        var t = index.FrameTicks;
        for (int b = 1; b < bins; b++)
        {
            long ticks = window.TicksAt(b * width / bins, width);
            int f = index.FrameAt(ticks);
            if (t[f] < ticks) f++;
            edges[b] = Math.Clamp(f, edges[b - 1], f1);
        }
        _edgeCache = (index, window, width, bins, edges);
        return edges;
    }

    private static void Glyphs(TimelineIndex index, IReadOnlyList<TimelineLane> lanes, TimelineWindow window, float width, float glyphPx,
        int f0, int f1, List<TimelineMark> marks)
    {
        // A k-way merge of the lanes' frame-sorted spans, so a collapsed group clusters across its lanes in time order.
        var spans = new TimelineEvent[lanes.Count][];
        var offs = new int[lanes.Count];
        var ends = new int[lanes.Count];
        for (int i = 0; i < lanes.Count; i++)
        {
            var all = index.Lane(lanes[i]);
            spans[i] = all as TimelineEvent[] ?? new List<TimelineEvent>(all).ToArray();
            offs[i] = LowerBound(spans[i], f0);
            ends[i] = LowerBound(spans[i], f1);
        }
        bool open = false;
        TimelineMark cur = default;
        int curPriority = -1;
        while (true)
        {
            int pick = -1;
            for (int i = 0; i < lanes.Count; i++)
                if (offs[i] < ends[i] && (pick < 0 || spans[i][offs[i]].Frame < spans[pick][offs[pick]].Frame)) pick = i;
            if (pick < 0) break;
            var e = spans[pick][offs[pick]++];
            float x = window.XOf(index.FrameTicks[e.Frame], width);
            int p = TimelineLanes.PriorityOf(e.Lane);
            if (open && x - cur.X0 < glyphPx)
            {
                cur = cur with { X1 = x, Count = cur.Count + 1, ToFrame = e.Frame + 1 };
                if (p > curPriority) { curPriority = p; cur = cur with { Top = e.Lane }; }
                continue;
            }
            if (open) marks.Add(cur);
            cur = new TimelineMark(TimelineMarkKind.Glyph, x, x, 1, e.Lane, 1f, e.Frame, e.Frame + 1);
            curPriority = p;
            open = true;
        }
        if (open) marks.Add(cur);
    }

    /// <summary>The mark under <paramref name="x"/> (a glyph within half its width, a bin containing it), else -1.</summary>
    public static int MarkAt(IReadOnlyList<TimelineMark> marks, float x, float glyphPx = DefaultGlyphPx)
    {
        for (int i = 0; i < marks.Count; i++)
        {
            var m = marks[i];
            float pad = m.Kind == TimelineMarkKind.Glyph ? glyphPx / 2 : 0;
            if (x >= m.X0 - pad && x <= Math.Max(m.X1, m.X0) + pad) return i;
        }
        return -1;
    }

    /// <summary>§2b "its tooltip lists them grouped by kind": the mark's time and its events counted per lane.</summary>
    public static string Describe(TimelineIndex index, IReadOnlyList<TimelineLane> lanes, in TimelineMark mark, long extentFrom)
    {
        var sb = new System.Text.StringBuilder();
        var t = index.FrameTicks;
        if (mark.FromFrame < t.Count)
            sb.Append(TimeSpan.FromTicks(t[mark.FromFrame] - extentFrom).ToString(@"h\:mm\:ss\.f"));
        if (mark.ToFrame - 1 > mark.FromFrame && mark.ToFrame - 1 < t.Count)
            sb.Append(" - ")   // ⚠ the editor font has no en dash (drawn as ?)
                .Append(TimeSpan.FromTicks(t[mark.ToFrame - 1] - extentFrom).ToString(@"h\:mm\:ss\.f"));
        foreach (var lane in lanes)
        {
            int n = index.Count(lane, mark.FromFrame, mark.ToFrame);
            if (n == 0) continue;
            sb.Append('\n').Append(lane).Append(" ×").Append(n);
            if (n == 1)
            {
                var e = index.Events(lane, mark.FromFrame, mark.ToFrame)[0];
                if (e.Subject != 0) sb.Append("  unit ").Append(e.Subject);
                if (e.Other != 0) sb.Append(" → ").Append(e.Other);
            }
        }
        return sb.ToString();
    }

    private static int LowerBound(TimelineEvent[] a, int frame)
    {
        int lo = 0, hi = a.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (a[mid].Frame < frame) lo = mid + 1; else hi = mid;
        }
        return lo;
    }

    /// <summary>Zoom by <paramref name="factor"/> (&lt; 1 in, &gt; 1 out) keeping the tick under <paramref name="cursorX"/> where it is;
    /// clamped to the recording's extent and to <paramref name="minSpan"/>.</summary>
    public static TimelineWindow ZoomAbout(TimelineWindow window, float cursorX, float width, float factor, long extentFrom, long extentTo, long minSpan = 1)
    {
        long anchor = window.TicksAt(cursorX, width);
        long extent = Math.Max(1, extentTo - extentFrom);
        long span = Math.Clamp((long)(window.Span * (double)factor), Math.Max(1, minSpan), extent);
        long from = anchor - (long)(cursorX / Math.Max(1f, width) * span);
        return Clamp(new TimelineWindow(from, from + span), extentFrom, extentTo);
    }

    /// <summary>Pan by <paramref name="dxPixels"/> (positive drags the content right ⇒ earlier time), clamped to the extent.</summary>
    public static TimelineWindow Pan(TimelineWindow window, float dxPixels, float width, long extentFrom, long extentTo)
    {
        long dt = (long)(dxPixels / Math.Max(1f, width) * window.Span);
        return Clamp(new TimelineWindow(window.From - dt, window.To - dt), extentFrom, extentTo);
    }

    /// <summary>Keeps the span, slides it inside [extentFrom, extentTo]; a span larger than the extent becomes the extent.</summary>
    public static TimelineWindow Clamp(TimelineWindow w, long extentFrom, long extentTo)
    {
        long span = Math.Min(w.To - w.From, extentTo - extentFrom);
        long from = Math.Clamp(w.From, extentFrom, extentTo - span);
        return new TimelineWindow(from, from + span);
    }

    /// <summary>The overview strip's bracket: where the zoomed window sits on the whole recording.</summary>
    public static (float X0, float X1) Bracket(TimelineWindow window, long extentFrom, long extentTo, float width)
    {
        var all = new TimelineWindow(extentFrom, extentTo);
        return (all.XOf(window.From, width), all.XOf(window.To, width));
    }

    private static readonly double[] RulerSeconds = { 0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200 };

    /// <summary>The ruler step, in ticks: the smallest "nice" step at least <paramref name="minPx"/> apart (§2b "the ruler's ticks adapt").</summary>
    public static long RulerStep(TimelineWindow window, float width, float minPx = 80f)
    {
        foreach (double s in RulerSeconds)
        {
            long step = (long)(s * TimeSpan.TicksPerSecond);
            if (step / (double)window.Span * width >= minPx) return step;
        }
        return (long)(RulerSeconds[^1] * TimeSpan.TicksPerSecond);
    }
}
