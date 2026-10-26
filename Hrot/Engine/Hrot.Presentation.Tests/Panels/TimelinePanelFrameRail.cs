using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Presentation.Panels.Timeline;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Tests.Map3D;
using ImGuiNET;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Panels;

/// <summary>
/// ⭐ <c>CE-1302</c> (docs/DESIGN_Director_Timeline_Editor.md §9a) — the Event Timeline in a REAL ImGui frame: the whole run (density
/// strips), a zoomed window (glyphs, clusters with counts), and a half-scanned index (the unscanned part shaded). The index is synthetic —
/// ten minutes shaped like a three-contact firefight — so every lane has something to draw. Screenshots land in
/// <c>HROT_TIMELINE_SHOTS</c> (else the temp folder): evidence for a human, ⛔ never compared as images.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class TimelinePanelFrameRail
{
    private const int Hz = 60;
    private const long Tick = TimeSpan.TicksPerSecond / Hz;
    private static readonly long T0 = new DateTime(2026, 10, 26, 9, 0, 0, DateTimeKind.Utc).Ticks;

    private sealed class Source : ITimelineSource
    {
        public TimelineIndex Index { get; init; } = TimelineIndex.Empty;
        public float Progress => Index.TotalFrames == 0 ? 0 : (float)Index.ScannedFrames / Index.TotalFrames;
        public bool IsComplete => Index.IsComplete;
        public string? Error => null;
    }

    private sealed class Cursor : ITimelineCursor
    {
        public long ExtentFrom { get; init; }
        public long ExtentTo { get; init; }
        public long Now { get; set; }
        public bool CanSeek => true;
        public void Seek(long ticks) => Now = ticks;
    }

    /// <summary>Ten minutes: twelve units at the start, three contacts (2:00, 4:30, 7:00) with bursts, hits, blasts and deaths, the
    /// commander's orders before each and the reports after, brain changes and decision switches around them.</summary>
    private static TimelineIndex Firefight(int scannedFrames = -1)
    {
        const int frames = 10 * 60 * Hz;
        var rng = new Random(26);
        var events = new List<TimelineEvent>();
        void Add(int f, TimelineLane lane, long who, long other = 0, float v = 0) => events.Add(new TimelineEvent(f, lane, who, other, 0, 0, v));
        int S(double seconds) => (int)(seconds * Hz);

        for (long u = 1; u <= 12; u++) Add(0, TimelineLane.Spawned, u);
        foreach (var (start, length, dead) in new[] { (120.0, 40.0, new long[] { 7 }), (270.0, 55.0, new long[] { 3, 9, 11 }), (420.0, 30.0, new long[] { 5 }) })
        {
            Add(S(start - 20), TimelineLane.Order, 1, 2); Add(S(start - 19), TimelineLane.Order, 1, 3);
            Add(S(start - 8), TimelineLane.Decision, 2, 4, 0x2067);
            for (int b = 0; b < (int)(length * 1.5); b++)                 // bursts: a few rounds each, all through the contact
            {
                double t = start + rng.NextDouble() * length;
                long shooter = rng.Next(2, 13);
                for (int r = 0; r < rng.Next(2, 6); r++) Add(S(t + r * 0.12), TimelineLane.Shot, shooter, rng.Next(2, 13), 1);
                if (rng.NextDouble() < 0.18) Add(S(t + 0.4), TimelineLane.Hit, rng.Next(2, 13));
                if (rng.NextDouble() < 0.12) Add(S(t + 0.2), TimelineLane.Change, shooter, 0, 2);
            }
            for (int k = 0; k < 3; k++) Add(S(start + rng.NextDouble() * length), TimelineLane.Blast, rng.Next(2, 13), 0, 0.6f);
            foreach (var d in dead) Add(S(start + length - 4 + rng.NextDouble() * 3), TimelineLane.Destroyed, d);
            Add(S(start + length + 5), TimelineLane.Report, 1, 2); Add(S(start + length + 9), TimelineLane.Report, 1, 3);
            Add(S(start + length + 3), TimelineLane.Decision, 2, 6, 0x2067);
        }
        for (int k = 0; k < 40; k++) Add(rng.Next(frames), TimelineLane.Change, rng.Next(1, 13), 0, 1);

        int scanned = scannedFrames < 0 ? frames : scannedFrames;
        var lanes = new TimelineEvent[TimelineLanes.Count][];
        for (int i = 0; i < lanes.Length; i++)
            lanes[i] = events.Where(e => (int)e.Lane == i && e.Frame < scanned).OrderBy(e => e.Frame).ToArray();
        var ticks = Enumerable.Range(0, scanned).Select(f => T0 + f * Tick).ToArray();
        return new TimelineIndex(lanes, ticks, scanned, frames);
    }

    private static string ShotPath(string name)
    {
        string dir = Environment.GetEnvironmentVariable("HROT_TIMELINE_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-timeline-rail");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name);
    }

    /// <summary>Renders the panel full-window for a few frames, screenshots it, and counts pixels of <paramref name="lane"/>'s colour.</summary>
    private static int DrawAndShoot(UiFrameSession frame, TimelinePanel panel, ITimelineSource source, ITimelineCursor cursor, string name, TimelineLane lane)
    {
        frame.StepN(4, () =>
        {
            ImGui.SetNextWindowPos(new Vector2(0, 0));
            ImGui.SetNextWindowSize(new Vector2(1280, 300));
            ImGui.Begin("Event Timeline", ImGuiWindowFlags.NoCollapse);
            panel.Draw(source, cursor);
            ImGui.End();
        });
        string path = ShotPath(name);
        frame.Screenshot(path);
        var image = Raylib.LoadImage(path);
        try
        {
            uint c = TimelinePanel.ColorOf(lane);                         // ABGR
            int r = (int)(c & 0xFF), g = (int)((c >> 8) & 0xFF), b = (int)((c >> 16) & 0xFF), hits = 0;
            for (int y = 0; y < image.Height; y += 2)
                for (int x = 0; x < image.Width; x += 2)
                {
                    var p = Raylib.GetImageColor(image, x, y);
                    if (Math.Abs(p.R - r) + Math.Abs(p.G - g) + Math.Abs(p.B - b) < 60) hits++;
                }
            return hits;
        }
        finally { Raylib.UnloadImage(image); }
    }

    [SkippableFact]
    public void CE1302_TheEventTimeline_DrawsTheRun_AZoomedContact_AndAHalfScannedIndex()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);

        var full = new Source { Index = Firefight() };
        var cursor = new Cursor { ExtentFrom = T0, ExtentTo = T0 + (10 * 60 * Hz - 1) * Tick, Now = T0 + 290 * TimeSpan.TicksPerSecond };

        using var frame = UiFrameHarness.Begin(1280, 300);

        var whole = new TimelinePanel();
        int shotPixels = DrawAndShoot(frame, whole, full, cursor, "timeline-1-whole-run.png", TimelineLane.Shot);
        Assert.True(shotPixels > 20, $"the Shot lane drew nothing on the whole run ({shotPixels} px)");

        var zoomed = new TimelinePanel();
        zoomed.Show(new TimelineWindow(T0 + 268 * TimeSpan.TicksPerSecond, T0 + 332 * TimeSpan.TicksPerSecond));   // the second contact
        int destroyedPixels = DrawAndShoot(frame, zoomed, full, cursor, "timeline-2-zoomed-contact.png", TimelineLane.Destroyed);
        Assert.True(destroyedPixels > 4, $"the three deaths of the second contact drew nothing ({destroyedPixels} px)");
        Assert.True(zoomed.Window.Span < 70 * TimeSpan.TicksPerSecond, "the zoom did not hold");

        var half = new Source { Index = Firefight(scannedFrames: 5 * 60 * Hz) };
        int halfShots = DrawAndShoot(frame, new TimelinePanel(), half, cursor, "timeline-3-half-scanned.png", TimelineLane.Shot);
        Assert.True(halfShots > 10 && halfShots < shotPixels, $"a half-scanned index draws only its scanned half ({halfShots} vs {shotPixels} px)");
    }
}
