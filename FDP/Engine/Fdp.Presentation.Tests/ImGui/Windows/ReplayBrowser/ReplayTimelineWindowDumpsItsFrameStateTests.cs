using System;
using System.IO;
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
/// ⭐⭐⭐ <b>U-obs-5 — <c>ReplayTimelinePanel</c>/<c>ReplayTimelineWindow</c> converted to the
/// <c>PanelSnapshot</c> contract.</b> 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example ·
/// <c>docs/blueprints/batches/QUEUE_Panel_Observability_Sweep.md</c> group 4 (the ReplayBrowser set).
/// Mirrors <c>RBF_P5T2_TimelinePanelTests</c>'s recording-fixture and stub-service helpers.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class ReplayTimelineWindowDumpsItsFrameStateTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"ReplayTimelineWindowTests_{Guid.NewGuid():N}");

    public ReplayTimelineWindowDumpsItsFrameStateTests()
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

    private string CreateTwoFrameRecording(int nodeId)
    {
        var path = Path.Combine(_tempDir, $"node{nodeId}.fdp");
        var exerciseId = Guid.NewGuid();
        var meta = new RecordingMetadata { ExerciseId = exerciseId, NodeId = nodeId };
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
        Assert.DoesNotContain("replay_timeline_test", PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        var window = new ReplayTimelineWindow("replay_timeline_test", "Timeline", "test-perspective", MakePanel(null), new Vector4(1, 1, 1, 1));

        Assert.Contains("replay_timeline_test", PanelSnapshot.RegisteredPanels);
        Assert.DoesNotContain("replay_timeline_test", PanelSnapshot.CapturedPanels);
        Assert.Null(PanelSnapshot.TryGet("replay_timeline_test"));
        Assert.NotNull(window);
    }

    // ── Rail 2 — the dump carries a real field ───────────────────────────────────────────────────

    [Fact]
    public void AfterABuild_TheDumpCarriesTheFrameCount()
    {
        PanelSnapshot.CaptureEnabled = true;
        var path = CreateTwoFrameRecording(nodeId: 1);
        using var manager = FederatedReplayManager.LoadGroup(new[] { path });
        var window = new ReplayTimelineWindow(
            "replay_timeline_test", "Timeline", "test-perspective", MakePanel(manager), new Vector4(1, 1, 1, 1));

        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("replay_timeline_test");
        Assert.NotNull(vm);
        Assert.Equal("replay_timeline_test", vm!.PanelId);
        Assert.Equal(ReplayTimelineWindow.Kind, vm.PanelKind);

        var dump = vm.Dump();
        Assert.True(dump["hasRecording"]!.GetValue<bool>());
        Assert.Equal(2, dump["totalFrames"]!.GetValue<int>());
    }

    // ── Rail 3 — the flag gates the DUMP, not the BUILD ──────────────────────────────────────────

    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing()
    {
        var window = new ReplayTimelineWindow(
            "replay_timeline_test", "Timeline", "test-perspective", MakePanel(null), new Vector4(1, 1, 1, 1));   // CaptureEnabled stays false

        var vm = window.SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains("replay_timeline_test", PanelSnapshot.RegisteredPanels);
        Assert.NotNull(vm);   // ⭐ the BUILD is unaffected by the flag
        Assert.False(vm.HasRecording);
    }
}
