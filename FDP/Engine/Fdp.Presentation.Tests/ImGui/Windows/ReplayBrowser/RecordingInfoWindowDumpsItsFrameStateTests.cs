using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Presentation.Tests.ImGui.Panels;
using Fdp.Presentation.Windows.ReplayBrowser;
using Fdp.Toolkit.ReplayBrowser;
using Fdp.Toolkit.ReplayBrowser.Federation;
using Xunit;

namespace Fdp.Presentation.Tests.ImGui.Windows.ReplayBrowser;

/// <summary>
/// ⭐ B-7 part 3 — the retired <c>ReplayTimelineWindow</c>'s panel snapshot, now carried by <c>RecordingInfoWindow</c> (File ▸ Recording info) — the same kind, the same view model
/// (<c>ReplayTimelinePanel.BuildViewModel</c>); published every frame, open or closed.
/// ⭐⭐⭐ <b>U-obs-5 — <c>ReplayTimelinePanel</c> converted to the
/// <c>PanelSnapshot</c> contract.</b> 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example ·
/// <c>docs/blueprints/batches/QUEUE_Panel_Observability_Sweep.md</c> group 4 (the ReplayBrowser set).
/// Mirrors <c>RBF_P5T2_TimelinePanelTests</c>'s recording-fixture and stub-service helpers.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class RecordingInfoWindowDumpsItsFrameStateTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"ReplayTimelineWindowTests_{Guid.NewGuid():N}");

    public RecordingInfoWindowDumpsItsFrameStateTests()
    {
        Directory.CreateDirectory(_tempDir);
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    private sealed class StubExportService : IRecordingExportService
    {
        public void ExportToJson(string input, string output, JsonExportOptions opts) { }
    }

    private sealed class StubFileDialogService : IFileDialogService
    {
        public Task<string?> ShowOpenFileDialogAsync(string callSiteId, string extensionFilter)
            => Task.FromResult<string?>(null);
        public Task<string?> ShowSaveAsDialogAsync(string callSiteId, string defaultFileName, string extensionFilter)
            => Task.FromResult<string?>(null);
        public Task<string[]?> ShowOpenMultipleFilesDialogAsync(string callSiteId, string extensionFilter)
            => Task.FromResult<string[]?>(null);
    }

    private string CreateTwoFrameRecording(int nodeId, Guid? exercise = null)
    {
        var path = Path.Combine(_tempDir, $"node{nodeId}.fdp");
        var exerciseId = exercise ?? Guid.NewGuid();
        var meta = new RecordingMetadata { ExerciseId = (Fdp.Core.ExerciseId)(exerciseId), NodeId = (Fdp.Core.NodeId)(nodeId) };
        using var repo = new EntityRepository();
        using var recorder = new Fdp.Core.FlightRecorder.AsyncRecorder(path, meta);
        recorder.CaptureKeyframe(repo, 1_000_000L, blocking: true, eventBus: repo.Bus);
        recorder.CaptureFrame(repo, 0u, 2_000_000L, blocking: true, eventBus: repo.Bus);
        return path;
    }

    private static ReplayTimelinePanel MakePanel(FederatedReplayManager? manager, Func<int>? getNodeId = null) =>
        new ReplayTimelinePanel(
            manager, getNodeId ?? (() => manager?.LocalEntitiesProviderNodeId ?? 0),
            new StubExportService(), new StubFileDialogService(),
            new PlaybackHistoryTracker(), new InspectorState());

    // ── Rail 1 — instrumented at construction, on the PRODUCTION object ─────────────────────────

    [Fact]
    public void ConstructingTheWindow_DeclaresItInstrumented_BeforeItHasEverDrawn()
    {
        Assert.DoesNotContain(RecordingInfoWindow.WindowId, PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        var window = new RecordingInfoWindow("test-perspective", MakePanel(null), new Vector4(1, 1, 1, 1));

        Assert.Contains(RecordingInfoWindow.WindowId, PanelSnapshot.RegisteredPanels);
        Assert.DoesNotContain(RecordingInfoWindow.WindowId, PanelSnapshot.CapturedPanels);
        Assert.Null(PanelSnapshot.TryGet(RecordingInfoWindow.WindowId));
        Assert.NotNull(window);
    }

    // ── Rail 2 — the dump carries a real field ───────────────────────────────────────────────────

    [Fact]
    public void AfterABuild_TheDumpCarriesTheFrameCount()
    {
        PanelSnapshot.CaptureEnabled = true;
        var path = CreateTwoFrameRecording(nodeId: 1);
        using var manager = FederatedReplayManager.LoadGroup(new[] { path });
        var window = new RecordingInfoWindow("test-perspective", MakePanel(manager), new Vector4(1, 1, 1, 1));

        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet(RecordingInfoWindow.WindowId);
        Assert.NotNull(vm);
        Assert.Equal(RecordingInfoWindow.WindowId, vm!.PanelId);
        Assert.Equal(RecordingInfoWindow.Kind, vm.PanelKind);

        var dump = vm.Dump();
        Assert.True(dump["hasRecording"]!.GetValue<bool>());
        Assert.Equal(2, dump["totalFrames"]!.GetValue<int>());
    }

    // ── Rail 3 — the flag gates the DUMP, not the BUILD ──────────────────────────────────────────

    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing()
    {
        var window = new RecordingInfoWindow("test-perspective", MakePanel(null), new Vector4(1, 1, 1, 1));   // CaptureEnabled stays false

        var vm = window.SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains(RecordingInfoWindow.WindowId, PanelSnapshot.RegisteredPanels);
        Assert.NotNull(vm);   // ⭐ the BUILD is unaffected by the flag
        Assert.False(vm.HasRecording);
    }

    // ── B-7 part 3 — the dialog's content, the tooltip's text, the shared frame times ─────────────────────────

    /// <summary>⭐ <i>File ▸ Recording info</i> lists the exercise, and per node the file, the frames and the length — read from a REAL two-node recording.
    /// ✅ Red-proof: <c>DescribeRecording</c> dropping the file path (or the length) ⇒ red.</summary>
    [Fact]
    public void TheDialog_ListsTheFilesPerNode_TheLength_AndTheExercise_OfARealRecording()
    {
        var exercise = Guid.NewGuid();                                       // one exercise: a group of two nodes' files
        var a = CreateTwoFrameRecording(nodeId: 1, exercise);
        var b = CreateTwoFrameRecording(nodeId: 2, exercise);
        using var manager = FederatedReplayManager.LoadGroup(new[] { a, b });
        var panel = MakePanel(manager);

        var info = panel.DescribeRecording();

        Assert.Equal(manager.ExerciseId.Value, info.ExerciseId);
        Assert.Equal(new[] { 1, 2 }, info.Files.Select(f => f.NodeId).ToArray());
        Assert.Equal(new[] { a, b }, info.Files.Select(f => f.Path).ToArray());
        Assert.All(info.Files, f => { Assert.Equal(2, f.Frames); Assert.True(f.WallSeconds > 0d, "the length is the first-to-last frame's wall time"); });
        Assert.Empty(MakePanel(null).DescribeRecording().Files);   // nothing loaded: an empty dialog, not a crash
    }

    /// <summary>The seek bar's tooltip text, as the retired window's readout: frame (spaced thousands) · wall · sim; and the panel's frame times are the ONE source the snapshot uses.</summary>
    [Fact]
    public void TheTooltipText_IsFrameWallSim_AndTheFrameTimesAreTheSnapshotsOwn()
    {
        Assert.Equal("frame 2 103 · wall 40.602 s · sim 39.997 s", ReplayTimelinePanel.DetailText(2103, 40.602, 39.997));
        Assert.Equal("frame 0 · wall 0.000 s · sim 0.000 s", ReplayTimelinePanel.DetailText(0, 0, 0));

        PanelSnapshot.CaptureEnabled = true;
        var path = CreateTwoFrameRecording(nodeId: 1);
        using var manager = FederatedReplayManager.LoadGroup(new[] { path });
        var panel = MakePanel(manager);
        manager.Contexts[1].SeekToFrame(1);
        panel.FrameTimes(out long ticks, out double wall, out double sim);
        var vm = panel.BuildViewModel("x", "replay-timeline");
        Assert.Equal((ticks, wall, sim), (vm.WallClockTicks, vm.RelativeWallSeconds, vm.SimTimeSeconds));
        Assert.True(wall > 0d);
    }

    /// <summary>⭐ The snapshot an agent reads is published whether the dialog is open or CLOSED (it is closed by default).</summary>
    [Fact]
    public void TheSnapshot_IsPublished_EvenWhileTheDialogIsClosed()
    {
        PanelSnapshot.CaptureEnabled = true;
        var window = new RecordingInfoWindow("test-perspective", MakePanel(null), new Vector4(1, 1, 1, 1));
        Assert.False(window.IsOpen);
        window.GetType().GetMethod("OnRenderWhileClosed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, null);
        Assert.NotNull(PanelSnapshot.TryGet(RecordingInfoWindow.WindowId));
    }
}
