using System;
using System.Collections.Generic;

namespace Fdp.Presentation.Panels.Timeline;

/// <summary>One ruler tick: where, whether it is a labelled (major) one, and its label.</summary>
public readonly record struct RulerTick(float X, long Ticks, bool Major, string? Label);

/// <summary>
/// ⭐ <c>CE-3330</c> T8 (docs/DESIGN_Replay_Time_Axis_And_Review.md §10c "the ruler") — the ruler as pure maths: 1·2·5 steps from a frame up
/// to 12 h, minor ticks between majors, labels measured so they never overlap, and the format chosen by the step AND the extent —
/// <c>h:mm:ss</c> once the extent passes an hour (⛔ before: past an hour the hours vanished), <c>m:ss.f</c> below a second, <c>+frame</c> at the
/// closest zoom.
/// </summary>
public static class TimelineRuler
{
    /// <summary>The steps, in seconds (§10c): sub-second 1·2·5, then the clock's 1, 2, 5, 10, 15, 30 s, 1, 2, 5, 10, 15, 30 min, 1, 2, 6, 12 h.</summary>
    private static readonly double[] Steps =
    {
        0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200, 21600, 43200,
    };

    /// <summary>The smallest step (ticks) whose labels sit at least <paramref name="minPx"/> apart.</summary>
    public static long MajorStep(TimelineWindow window, float width, float minPx = 80f)
    {
        foreach (double s in Steps)
        {
            long step = (long)(s * TimeSpan.TicksPerSecond);
            if (step / (double)window.Span * width >= minPx) return step;
        }
        return (long)(Steps[^1] * TimeSpan.TicksPerSecond);
    }

    /// <summary>The label of <paramref name="ticks"/> after the extent's start, for a ruler stepping <paramref name="step"/> over an extent of
    /// <paramref name="extentSpan"/>. <paramref name="frameTicks"/> &gt; 0 marks the closest zoom: the label adds the frame within its second.</summary>
    public static string Label(long ticks, long step, long extentSpan, long frameTicks = 0)
    {
        var t = TimeSpan.FromTicks(Math.Max(0, ticks));
        bool hours = extentSpan >= TimeSpan.TicksPerHour;
        if (step < TimeSpan.TicksPerSecond)
        {
            string s = hours ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
            if (frameTicks > 0) return s + "+" + (t.Ticks % TimeSpan.TicksPerSecond) / frameTicks;
            return step < TimeSpan.TicksPerSecond / 10
                ? s + "." + (t.Ticks % TimeSpan.TicksPerSecond / (TimeSpan.TicksPerSecond / 100)).ToString("00")
                : s + "." + t.Ticks % TimeSpan.TicksPerSecond / (TimeSpan.TicksPerSecond / 10);
        }
        return hours ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    }

    /// <summary>
    /// The ticks to draw: majors at <see cref="MajorStep"/> (labelled unless the label would collide with the previous one —
    /// <paramref name="measure"/> gives a label's width), five minors between two majors (unlabelled).
    /// </summary>
    /// <param name="frameTicks">The recording's frame spacing; at a zoom where a major step is under two frames, labels show <c>+frame</c>.</param>
    public static List<RulerTick> Ticks(TimelineWindow window, float width, long extentFrom, long extentTo, Func<string, float> measure,
        long frameTicks = 0, List<RulerTick>? into = null)
    {
        var ticks = into ?? new List<RulerTick>();
        ticks.Clear();
        if (width <= 0 || window.Span <= 1) return ticks;
        long step = MajorStep(window, width);
        long minor = Math.Max(1, step / 5);
        long extentSpan = Math.Max(0, extentTo - extentFrom);
        long fine = frameTicks > 0 && step <= 2 * frameTicks ? frameTicks : 0;
        float lastLabelEnd = float.NegativeInfinity;
        long first = extentFrom + (long)Math.Floor((window.From - extentFrom) / (double)minor) * minor;
        for (long t = first; t <= window.To; t += minor)
        {
            if (t < window.From) continue;
            float x = window.XOf(t, width);
            bool major = (t - extentFrom) % step == 0;
            string? label = null;
            if (major)
            {
                string text = Label(t - extentFrom, step, extentSpan, fine);
                float w = measure(text);
                if (x + 2 >= lastLabelEnd + 6 && x + 2 + w <= width) { label = text; lastLabelEnd = x + 2 + w; }
            }
            ticks.Add(new RulerTick(x, t, major, label));
        }
        return ticks;
    }
}
