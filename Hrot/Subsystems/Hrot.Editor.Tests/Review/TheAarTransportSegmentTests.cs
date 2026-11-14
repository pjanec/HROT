using System;
using System.IO;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Icons;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Runner;
using Hrot.Editor;
using Hrot.Editor.Review;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.DebugApi;
using Moq;
using Xunit;
using ImGui = ImGuiNET.ImGui;

namespace Hrot.Editor.Tests.Review;

/// <summary>
/// ⭐ R-3 / V3 (docs/UX/DESIGN_Review_Perspective.md §3 D8, §4 V3) — the ONE AAR transport: the editor's segment is the Replay Browser's drawer (<see cref="ReplayTransportDrawer"/>) over
/// <see cref="OwnPartReplayTransport"/> (THE seek, <see cref="OwnPartReplay"/>), registered as its OWN toolbar entry and drawn only in AAR, with the <see cref="ReplayTransportParts.AarSegment"/> profile
/// (history · step back with scrub · the seek bar — ▶ ⏸ ■ · rate · axis · step forward are the time control's). Frame rails run under <c>xvfb-run -a</c> (a skip is not a pass).
/// 🔴 Red-proofs (compile): the segment profile = <c>All</c> (a duplicate ▶) ⇒ the profile rail red; <c>OwnPartReplayTransport.Step</c> not seeking ⇒ the step rail red.
/// </summary>
[Collection(Hrot.Editor.Tests.Windows.PanelSnapshotTestCollection.Name)]
public sealed class TheAarTransportSegmentTests : IDisposable
{
    private static readonly long Zero = TimeSpan.FromSeconds(1).Ticks;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hrot-aar-transport-" + Guid.NewGuid().ToString("N"));

    public TheAarTransportSegmentTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    /// <summary>A node file: a keyframe at 1 s then frames one second apart (0..4).</summary>
    private string Record()
    {
        string path = Path.Combine(_dir, "node_1.fdp");
        using var repo = new EntityRepository();
        repo.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
        var unit = repo.CreateEntity();
        repo.AddComponent(unit, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = 1042 });
        using var rec = new Fdp.Core.FlightRecorder.AsyncRecorder(path,
            new Fdp.Core.FlightRecorder.Metadata.RecordingMetadata { ExerciseId = (Fdp.Core.ExerciseId)Guid.NewGuid(), NodeId = (Fdp.Core.NodeId)1 });
        repo.Tick();
        rec.CaptureKeyframe(repo, Zero, blocking: true, eventBus: repo.Bus);
        uint prev = repo.GlobalVersion;
        for (int f = 1; f <= 4; f++)
        {
            repo.Tick();
            rec.CaptureFrame(repo, prev, Zero + TimeSpan.FromSeconds(f).Ticks, blocking: true, eventBus: repo.Bus);
            prev = repo.GlobalVersion;
        }
        return path;
    }

    private sealed class Rig : IDisposable
    {
        public readonly Mock<IClusterMasterControl> Master = new();
        public readonly System.Collections.Generic.List<long> Seeks = new();
        public long Now;
        public bool InAar = true;
        public readonly OwnPartReplay Replay;
        public readonly OwnPartReplayTransport Transport;
        public readonly AarTransportSegment Segment;

        public Rig(string path, int atFrame)
        {
            Now = Zero + TimeSpan.FromSeconds(atFrame).Ticks;
            Master.Setup(m => m.SeekReplay(It.IsAny<SeekReplayIntent>())).Callback<SeekReplayIntent>(i => Seeks.Add(i.TargetWallTicks));
            Replay = new OwnPartReplay(() => path, () => new ReplayPosition(Zero, Now), () => Master.Object, () => ClusterState.OperatingReplay);
            Transport = new OwnPartReplayTransport(Replay);
            Segment = new AarTransportSegment(Transport, () => InAar);
        }
        public void Dispose() => Replay.Dispose();
    }

    private static void InBar(UiFrameSession frame, Action draw, Action<ImGuiNET.ImGuiIOPtr>? inject = null)
        => frame.Step(() =>
        {
            inject?.Invoke(ImGui.GetIO());
            if (ImGui.BeginMainMenuBar())
            {
                ImGui.TextUnformatted("File  Edit  View");
                ImGui.SameLine();
                draw();
                ImGui.EndMainMenuBar();
            }
        });

    [Fact]
    public void V3_StepBack_IsAClusterSeekToThePreviousFrame_ThroughOwnPartReplay()
    {
        using var rig = new Rig(Record(), atFrame: 3);
        Assert.Equal(3, rig.Transport.CurrentFrame);
        Assert.Equal(5, rig.Transport.TotalFrames);

        rig.Transport.Step(-1);
        Assert.Equal(TimeSpan.FromSeconds(2).Ticks, rig.Seeks[0]);   // frame 2's moment: 2 s after the replay's zero

        rig.Transport.Step(1);
        Assert.Equal(TimeSpan.FromSeconds(4).Ticks, rig.Seeks[1]);
        Assert.Equal(2, rig.Seeks.Count);

        rig.Now = Zero;                                   // at the first frame a step back does nothing; at the last, a step forward
        rig.Transport.Step(-1);
        rig.Now = Zero + TimeSpan.FromSeconds(4).Ticks;
        rig.Transport.Step(1);
        Assert.Equal(2, rig.Seeks.Count);
    }

    [Fact]
    public void V3_TheEditorRegistersTheSegmentAsItsOwnToolbarEntry_WithTheAarProfile()
    {
        var editor = new EditorSubsystem();
        try
        {
            editor.Initialize(new SubsystemConfig { Headless = true });
            var windows = new Fdp.Presentation.WindowManager.WindowManager(new IconAtlas(IntPtr.Zero, 512, 512));
            editor.RegisterWindows(windows);

            Assert.True(windows.MainToolbar.ContainsEntry(AarTransportSegment.EntryId), "the segment is its own main-toolbar entry");
            var segment = editor.AarTransportForTest;
            Assert.NotNull(segment);
            Assert.Equal(ReplayTransportParts.AarSegment, segment!.Drawer.Parts);
            Assert.IsType<OwnPartReplayTransport>(Assert.IsType<ReplayTransportSelector>(segment.Transport).Active);   // V6: the segment draws the source's transport while a group is open, this one otherwise
        }
        finally { editor.Shutdown(); }
    }

    [SkippableFact]
    public void V3_TheSegmentDrawsOnlyItsProfile_AndNothingOutsideAAR()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var rig = new Rig(Record(), atFrame: 3);
        using var frame = UiFrameHarness.Begin(1240, 120);

        float hiddenAdvance = 0f;
        rig.InAar = false;
        for (int i = 0; i < 3; i++)
            InBar(frame, () => { float x = ImGui.GetCursorPosX(); rig.Segment.Draw(); hiddenAdvance = ImGui.GetCursorPosX() - x; });
        Assert.Equal(0f, hiddenAdvance);                                                  // outside AAR: not a pixel
        Assert.Equal(ReplayTransportParts.None, rig.Segment.Drawer.LastDrawn);
        Assert.Null(rig.Segment.Drawer.LastSeekBar);

        rig.InAar = true;
        for (int i = 0; i < 3; i++) InBar(frame, rig.Segment.Draw);
        var drawn = rig.Segment.Drawer.LastDrawn;
        Assert.Equal(ReplayTransportParts.History | ReplayTransportParts.StepBack | ReplayTransportParts.SeekBar, drawn);   // history · step back · the seek bar — and NO ▶ ⏸ ■, rate, axis, step forward (the time control's)
        var bar = Assert.IsType<(Vector2 Min, Vector2 Max, float Fraction, string Now, string End)>(rig.Segment.Drawer.LastSeekBar);
        Assert.Equal(3f / 4f, bar.Fraction, 3);
        Assert.Equal("0:03.0", bar.Now);
        Assert.Equal("0:04.0", bar.End);
        Assert.True(bar.Max.X - bar.Min.X <= AarTransportSegment.SeekBarMaxWidth + 0.5f, "the bar shares the toolbar: capped");

        // ⭐ the frame of the editor's toolbar in AAR — for the side-by-side with docs/UX/review-perspective-visual.svg (⛔ never committed)
        var shot = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS");
        if (!string.IsNullOrEmpty(shot)) { Directory.CreateDirectory(shot); frame.Screenshot(Path.Combine(shot, "v3-aar-transport-segment.png")); }
    }

    [SkippableFact]
    public void V3_ASeekThroughTheSeam_IsAHistoryWaypoint_AndBackGoesThere()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var rig = new Rig(Record(), atFrame: 3);
        using var frame = UiFrameHarness.Begin(1240, 120);
        for (int i = 0; i < 3; i++) InBar(frame, rig.Segment.Draw);
        Assert.False(rig.Transport.Waypoints.CanGoBack);
        var bar = rig.Segment.Drawer.LastSeekBar!.Value;

        // a click on the bar's far end (frame 4): hover first, then press
        var at = new Vector2(bar.Max.X - 1f, (bar.Min.Y + bar.Max.Y) / 2f);
        void Pos(ImGuiNET.ImGuiIOPtr io) { io.AddMousePosEvent(at.X, at.Y); Raylib_cs.Raylib.SetMousePosition((int)Math.Round(at.X), (int)Math.Round(at.Y)); }
        InBar(frame, rig.Segment.Draw, Pos);
        InBar(frame, rig.Segment.Draw, io => { Pos(io); io.AddMouseButtonEvent(0, true); });
        InBar(frame, rig.Segment.Draw, Pos);
        InBar(frame, rig.Segment.Draw, io => { Pos(io); io.AddMouseButtonEvent(0, false); });

        Assert.Equal(TimeSpan.FromSeconds(4).Ticks, rig.Seeks[0]);                        // the click seeked through THE seek
        Assert.True(rig.Transport.Waypoints.CanGoBack, "a seek through the seam is a history waypoint");
        int seeksBefore = rig.Seeks.Count;
        rig.Transport.Waypoints.GoBack();                                                 // ◀ goes back to where the scrub began (frame 3)
        Assert.Equal(seeksBefore + 1, rig.Seeks.Count);
        Assert.Equal(TimeSpan.FromSeconds(3).Ticks, rig.Seeks[^1]);
    }

    [SkippableFact]
    public void V3_TheSegment_CostsZeroBytesPerSteadyFrame()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var rig = new Rig(Record(), atFrame: 3);
        using var frame = UiFrameHarness.Begin(1240, 120);
        long bytes = -1, hidden = -1;
        for (int i = 0; i < 12; i++)
            InBar(frame, () => { long b = GC.GetAllocatedBytesForCurrentThread(); rig.Segment.Draw(); bytes = GC.GetAllocatedBytesForCurrentThread() - b; });
        Console.WriteLine($"V3 steady-frame bytes: segment {bytes}");
        Assert.Equal(0, bytes);

        rig.InAar = false;
        for (int i = 0; i < 4; i++)
            InBar(frame, () => { long b = GC.GetAllocatedBytesForCurrentThread(); rig.Segment.Draw(); hidden = GC.GetAllocatedBytesForCurrentThread() - b; });
        Assert.Equal(0, hidden);
    }
}
