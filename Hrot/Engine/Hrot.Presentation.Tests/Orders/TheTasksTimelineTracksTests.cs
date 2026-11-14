using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Panels.Timeline;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.ReplayBrowser.Timeline;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Orders;
using Hrot.Presentation.Tests.Map3D;
using ImGuiNET;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Orders;

/// <summary>
/// ⭐ W-1 / P-8 8c-2 (📄 docs/UX/DESIGN_Mission_Plan_Editor.md §5.8 E4, §5.12; mockup <c>mission-plan-detail-visual.svg</c> C) — the Event Timeline's
/// Tasks rows: <i>all units</i> (a coloured marker per start / Done / Failed, naming the unit and task) and the SELECTION's bars; a thousand units
/// stay ONE row; the rows are added to the panel BESIDE another owner's tracks (the director's), never replacing them.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class TheTasksTimelineTracksTests
{
    private sealed class Source : ITimelineSource
    {
        public TimelineIndex Index { get; init; } = TimelineIndex.Empty;
        public float Progress => 1f;
        public bool IsComplete => true;
        public string? Error => null;
    }

    private sealed class Cursor : ITimelineCursor
    {
        public long ExtentFrom { get; init; }
        public long ExtentTo { get; init; }
        public long Now { get; set; }
        public bool CanSeek => true;
        public void Seek(long ticks) => Now = ticks;
        public Fdp.Core.FlightRecorder.ReplayTimeMap? TimeMap { get; init; }   // the hosts' cursors carry one: track items are in SIM seconds
    }

    private const int Frames = 600;   // one per second
    private static TimelineEvent Move(int frame, int unit, int current, int count, MissionPhaseOutcome latest = MissionPhaseOutcome.None)
        => new(frame, BehaviourTimelineLanes.Tasks.Id, unit, 0, (float)latest, count, current);

    /// <summary>Alpha 1-1 (unit 11): Move · Hold · Attack — 1 Done at 100 s, 2 Failed at 300 s; Bravo (12) one task, done at 50 s.</summary>
    private static Source Plan(int extraUnits = 0)
    {
        var events = new List<TimelineEvent> { Move(0, 11, 0, 3), Move(0, 12, 0, 1), Move(50, 12, 1, 1, MissionPhaseOutcome.Done), Move(100, 11, 1, 3, MissionPhaseOutcome.Done), Move(300, 11, 2, 3, MissionPhaseOutcome.Failed) };
        for (int k = 0; k < extraUnits; k++) events.Add(Move(10, 1000 + k, 0, 2));
        events.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        var lanes = new Dictionary<string, SharedList<TimelineEvent>> { [BehaviourTimelineLanes.Tasks.Id] = new(events.ToArray()) };
        var ticks = Enumerable.Range(0, Frames).Select(f => (long)f * TimeSpan.TicksPerSecond).ToArray();
        var sim = Enumerable.Range(0, Frames).Select(f => (double)f).ToArray();
        return new Source { Index = new TimelineIndex(lanes, new SharedList<long>(ticks), Frames, Frames, null, new SharedList<double>(sim)) };
    }

    /// <summary>
    /// ⭐ The rows: <i>Tasks</i> header, <i>all units</i> (a marker per start / end, in the kind's colour, naming the unit and the task), and the
    /// selection's bars (from → to in sim seconds, coloured by how each ended; a running one to the last frame). 🔴 Red-proof: the Failed colour
    /// mapped to Done ⇒ the colour rows ⇒ red.
    /// </summary>
    [Fact]
    public void P8c2_AllUnitsMarkers_AndTheSelectionsBars()
    {
        int selected = 11; var picked = new List<int>();
        var tracks = new TasksTimelineTracks(() => Plan(), () => null, () => selected, picked.Add);
        var rows = tracks.Tracks();
        Assert.Equal(new[] { "tasks", "tasks:all", "tasks:sel" }, rows.Select(r => r.Id));
        var all = rows[1].Items;
        Assert.Equal(7, all.Count);   // 11: start 1, done 1, start 2, failed 2, start 3 · 12: start 1, done 1
        Assert.Contains(all, i => i.Label == "#11 · task 2 Failed" && i.From == 300 && i.Colour == TasksTimelineTracks.FailedColour);
        Assert.Contains(all, i => i.Label == "#12 · task 1 done" && i.From == 50 && i.Colour == TasksTimelineTracks.DoneColour);
        Assert.All(all.Where(i => i.Label.EndsWith("started")), i => Assert.Equal(TasksTimelineTracks.StartedColour, i.Colour));

        var bars = rows[2].Items;
        Assert.Equal(new[] { (0d, (double?)100d), (100d, (double?)300d), (300d, (double?)599d) }, bars.Select(b => (b.From, b.To)));
        Assert.Equal(new uint?[] { TasksTimelineTracks.DoneColour, TasksTimelineTracks.FailedColour, TasksTimelineTracks.RunningColour }, bars.Select(b => b.Colour));
        Assert.Equal("#11 ▸", rows[2].Header!.Text);

        rows[1].Select(all.First(i => i.Label.StartsWith("#12")).Id);   // a click on a marker selects its unit
        Assert.Equal(new[] { 12 }, picked);

        selected = 0;
        Assert.Equal(2, tracks.Tracks().Count);   // no selection: no bars row
    }

    /// <summary>⭐ R-4161 — a thousand units: ONE <i>all units</i> row holding every unit's marker; the items are rebuilt only when the index or the selection changes.</summary>
    [Fact]
    public void P8c2_AThousandUnits_AreOneRow_BuiltOnce()
    {
        var source = Plan(extraUnits: 1000);
        var tracks = new TasksTimelineTracks(() => source, () => null, () => 0);
        var rows = tracks.Tracks();
        Assert.Equal(2, rows.Count);
        Assert.Equal(1000 + 7, rows[1].Items.Count);
        var first = rows[1].Items[0];
        Assert.Same(first, tracks.Tracks()[1].Items[0]);   // a steady frame rebuilds nothing
    }

    /// <summary>⭐ The panel draws the Tasks rows AFTER another owner's tracks (the director's <c>Tracks</c>), on a real frame; the red Failed marker is
    /// drawn. Renders <c>tasks-timeline.png</c> under <c>HROT_RAIL_SHOTS</c>.</summary>
    [SkippableFact]
    public void P8c2_TheRowsOnARealFrame_BesideAnotherOwnersTracks()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var source = Plan();
        var tracks = new TasksTimelineTracks(() => source, () => null, () => 11);
        var panel = new TimelinePanel { Tracks = () => Array.Empty<ITimelineTrack>() };   // another owner's (empty here) — kept
        panel.AddTracks(tracks.Tracks);
        var wall = Enumerable.Range(0, Frames).Select(f => (long)f * TimeSpan.TicksPerSecond).ToArray();
        var simCol = Enumerable.Range(0, Frames).Select(f => (double)f).ToArray();
        var cursor = new Cursor { ExtentFrom = 0, ExtentTo = (Frames - 1) * TimeSpan.TicksPerSecond, Now = 400 * TimeSpan.TicksPerSecond,
            TimeMap = Fdp.Core.FlightRecorder.ReplayTimeMap.FromFrames(wall, simCol, new Fdp.Core.FlightRecorder.FrameFlags[Frames]) };
        var lanes = new TimelineLaneCatalog().Register(new BehaviourTimelineLanes()).Lanes(new TimelineLaneContext(Recording: true));
        using var frame = UiFrameHarness.Begin(1200, 260);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        frame.StepN(5, () =>
        {
            ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new Vector2(1200, 260));
            ImGui.Begin("Event Timeline", ImGuiWindowFlags.NoCollapse);
            panel.Draw(source, cursor, lanes);
            ImGui.End();
        });
        string dir = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-tasks-rail");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "tasks-timeline.png");
        frame.Screenshot(path);
        var image = Raylib.LoadImage(path);
        int red = 0;
        try
        {
            uint c = TasksTimelineTracks.FailedColour;
            int r = (int)(c & 0xFF), g = (int)((c >> 8) & 0xFF), b = (int)((c >> 16) & 0xFF);
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++) { var p = Raylib.GetImageColor(image, x, y); if (Math.Abs(p.R - r) + Math.Abs(p.G - g) + Math.Abs(p.B - b) < 40) red++; }
        }
        finally { Raylib.UnloadImage(image); }
        Assert.True(red > 10, $"the Failed marker / bar is drawn ({red} red pixels)");
    }
}
