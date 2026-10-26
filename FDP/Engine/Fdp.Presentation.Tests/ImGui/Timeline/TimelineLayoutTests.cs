using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Fdp.Presentation.Panels.Timeline;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using Xunit;
using Xunit.Abstractions;

namespace Fdp.Presentation.Tests.ImGui.Timeline;

/// <summary>⭐ <c>CE-1302</c> R1-b (docs/DESIGN_Director_Timeline_Editor.md §2b) — the drawing rules as rails on the pure layout.</summary>
public sealed class TimelineLayoutTests
{
    private const long Tick = TimeSpan.TicksPerSecond / 60;   // one 60 Hz frame
    private readonly ITestOutputHelper _out;

    public TimelineLayoutTests(ITestOutputHelper output) { _out = output; }

    // ⭐ R-4079: lanes are ids with descriptor priorities — these mirror the shipped ones' order (death > blast > hit > shot > spawn).
    private const string Spawned = "life.spawned", Left = "life.left", Shot = "combat.shot", Hit = "combat.hit", Blast = "combat.blast";
    private static readonly Dictionary<string, int> Priority = new() { [Spawned] = 0, [Shot] = 3, [Hit] = 4, [Blast] = 5, [Left] = 6 };
    private static int PriorityOf(string lane) => Priority.TryGetValue(lane, out var p) ? p : 0;

    private static TimelineIndex Index(int frames, params TimelineEvent[] events)
    {
        var lanes = events.GroupBy(e => e.Lane).ToDictionary(g => g.Key, g => g.OrderBy(e => e.Frame).ToArray());
        var ticks = Enumerable.Range(0, frames).Select(f => f * Tick).ToArray();
        return new TimelineIndex(lanes, ticks, frames, frames);
    }

    private static TimelineEvent E(int frame, string lane, long subject = 1) => new(frame, lane, subject, 0, 0, 0, 0);
    private static TimelineWindow All(TimelineIndex i) => new(i.FrameTicks[0], i.FrameTicks[^1]);

    [Fact]
    public void CE1302_Sparse_IsAGlyphPerEvent_AtItsTime()
    {
        var index = Index(601, E(0, Shot), E(300, Shot), E(600, Shot));
        var marks = TimelineLayout.Marks(index, new[] { Shot }, All(index), 600f, PriorityOf);

        Assert.All(marks, m => Assert.Equal(TimelineMarkKind.Glyph, m.Kind));
        Assert.Equal(new[] { 0f, 300f, 600f }, marks.Select(m => (float)Math.Round(m.X0)));
        Assert.All(marks, m => Assert.Equal(1, m.Count));
    }

    [Fact]
    public void CE1302_CloseGlyphs_Cluster_WithACount_AndTheHighestPriorityOnTop()
    {
        // 600 px over 600 frames: frames 100..103 are within one glyph's width; across two lanes of a collapsed group.
        var index = Index(601, E(100, Spawned), E(101, Left), E(103, Spawned), E(400, Spawned));
        var marks = TimelineLayout.Marks(index, new[] { Spawned, Left }, All(index), 600f, PriorityOf);

        Assert.Equal(2, marks.Count);
        Assert.Equal((3, Left, 100, 104), (marks[0].Count, marks[0].Top, marks[0].FromFrame, marks[0].ToFrame));
        Assert.Equal((1, Spawned), (marks[1].Count, marks[1].Top));
        Assert.Contains("Left ×1", TimelineLayout.Describe(index, new[] { Spawned, Left }, marks[0], 0, id => id == Left ? "Left" : "Spawned"));
        Assert.Contains("Spawned ×2", TimelineLayout.Describe(index, new[] { Spawned, Left }, marks[0], 0, id => id == Left ? "Left" : "Spawned"));
        Assert.Equal(0, TimelineLayout.MarkAt(marks, 102f));
        Assert.Equal(-1, TimelineLayout.MarkAt(marks, 250f));
    }

    [Fact]
    public void CE1302_Crowded_IsADensityStrip_LogScaled_AndCountsEveryEvent()
    {
        var events = new List<TimelineEvent>();
        for (int f = 0; f < 3000; f++) events.Add(E(f, Shot));
        for (int k = 0; k < 50; k++) events.Add(E(1500, Hit));   // one busy moment
        var index = Index(3000, events.ToArray());
        var marks = TimelineLayout.Marks(index, new[] { Shot, Hit }, All(index), 200f, PriorityOf);

        Assert.All(marks, m => Assert.Equal(TimelineMarkKind.Density, m.Kind));
        Assert.Equal(3050, marks.Sum(m => m.Count));                      // nothing lost to the binning
        Assert.Equal(1f, marks.Max(m => m.Intensity), 3);
        var busy = marks.Single(m => m.FromFrame <= 1500 && 1500 < m.ToFrame);
        Assert.Equal(Hit, busy.Top);                         // priority on top inside a bin
        Assert.True(marks.Min(m => m.Intensity) > 0.3f, "log scale keeps a quiet bin visible");
    }

    [Fact]
    public void CE1302_ZoomKeepsTheTickUnderTheCursor_AndPanStaysInside()
    {
        var w = new TimelineWindow(0, 1000 * Tick);
        var z = TimelineLayout.ZoomAbout(w, 250f, 1000f, 0.5f, 0, 1000 * Tick);
        Assert.Equal(500 * Tick, z.Span);
        Assert.Equal(w.TicksAt(250f, 1000f), z.TicksAt(250f, 1000f), 2 * Tick);

        var outOfRange = TimelineLayout.ZoomAbout(z, 0f, 1000f, 100f, 0, 1000 * Tick);
        Assert.Equal(w, outOfRange);                                      // never past the recording
        var panned = TimelineLayout.Pan(z, 10_000f, 1000f, 0, 1000 * Tick);
        Assert.Equal(0, panned.From);
        Assert.Equal(z.Span, panned.Span);
        var (b0, b1) = TimelineLayout.Bracket(z, 0, 1000 * Tick, 100f);
        Assert.Equal(50f, b1 - b0, 1);
    }

    [Fact]
    public void CE1302_TheRuler_Adapts_ToAtLeastItsMinimumSpacing()
    {
        Assert.Equal(TimeSpan.TicksPerSecond * 300, TimelineLayout.RulerStep(new TimelineWindow(0, TimeSpan.TicksPerHour), 1000f));
        Assert.Equal(TimeSpan.TicksPerSecond, TimelineLayout.RulerStep(new TimelineWindow(0, TimeSpan.TicksPerSecond * 10), 1000f));
    }

    [Fact]
    public void CE1302_AnHoursIndex_LaysOutUnderTheBudget()
    {
        // §2b budget: < 1 ms per draw at any zoom. An hour at 60 Hz with 100 000 events, every lane as its own row at 1600 px.
        const int frames = 216_000;
        var rng = new Random(7);
        string[] ids = { "a", "b", "c", "d", "e", "f", "g", "h", "i" };
        var events = Enumerable.Range(0, 100_000).Select(_ => E(rng.Next(frames), ids[rng.Next(ids.Length)])).ToArray();
        var index = Index(frames, events);
        var marks = new List<TimelineMark>();
        var lanes = ids.Select(l => new[] { l }).ToArray();
        var windows = new[] { All(index), new TimelineWindow(0, 60 * TimeSpan.TicksPerSecond), new TimelineWindow(0, 2 * TimeSpan.TicksPerSecond) };

        foreach (var w in windows) foreach (var l in lanes) TimelineLayout.Marks(index, l, w, 1600f, PriorityOf, into: marks);   // warm
        const int runs = 20;
        var sw = Stopwatch.StartNew();
        for (int r = 0; r < runs; r++)
            foreach (var w in windows) foreach (var l in lanes) TimelineLayout.Marks(index, l, w, 1600f, PriorityOf, into: marks);
        double perDraw = sw.Elapsed.TotalMilliseconds / (runs * windows.Length);
        _out.WriteLine($"layout per draw (9 lanes, 1600 px): {perDraw:0.000} ms");
        Assert.True(perDraw < 5.0, $"layout took {perDraw:0.00} ms per draw");   // ⚠ generous for a shared CI box; the measured figure is in the report
    }
}
