using System;
using System.IO;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Toolkit.ReplayBrowser;
using Fdp.Toolkit.ReplayBrowser.Federation;
using Xunit;

namespace Fdp.Presentation.ReplayBrowser.Foundation;

/// <summary>
/// RBF-P5T2: ReplayTimelinePanel drives FederatedReplayManager directly.
/// Verifies that the panel holds no ReplayBrowserContext field, and that
/// seek/step operations translate to SetBaseWallTicks calls on the manager.
/// </summary>
public sealed class RBF_P5T2_TimelinePanelTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"RBF_P5T2_{Guid.NewGuid():N}");

    public RBF_P5T2_TimelinePanelTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

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

    /// <summary>
    /// Creates a 2-frame .fdp recording (keyframe + one data frame).
    /// </summary>
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

    private static ReplayTimelinePanel MakePanel(FederatedReplayManager? manager, Func<int>? getNodeId = null)
    {
        return new ReplayTimelinePanel(
            manager,
            getNodeId ?? (() => manager?.LocalEntitiesProviderNodeId ?? 0),
            new StubExportService(),
            new StubFileDialogService(),
            new PlaybackHistoryTracker(),
            new InspectorState());
    }

    // ── Tests ─────────────────────────────────────────────────────────────

    /// <summary>
    /// RBF-P5T2: ReplayTimelinePanel must not hold any field of type ReplayBrowserContext.
    /// </summary>
    [Fact]
    public void RBF_P5T2_Panel_NoContextField()
    {
        var fields = typeof(ReplayTimelinePanel)
            .GetFields(System.Reflection.BindingFlags.Instance
                      | System.Reflection.BindingFlags.NonPublic
                      | System.Reflection.BindingFlags.Public);
        bool hasContextField = System.Linq.Enumerable.Any(fields,
            f => f.FieldType == typeof(ReplayBrowserContext));
        Assert.False(hasContextField,
            "ReplayTimelinePanel must not hold a ReplayBrowserContext field (DESIGN §6.4)");
    }

    /// <summary>
    /// RBF-P5T2: SeekToFrameForTest translates frame N to wall ticks and calls SetBaseWallTicks.
    /// </summary>
    [Fact]
    public void RBF_P5T2_SliderMove_CallsSetBaseWallTicks()
    {
        var path    = CreateTwoFrameRecording(nodeId: 1);
        using var manager = FederatedReplayManager.LoadGroup(new[] { path });
        var panel   = MakePanel(manager);

        int nodeId  = manager.LocalEntitiesProviderNodeId;
        var ctx     = manager.Contexts[nodeId];
        long frame1Ticks = ctx.Playback!.GetFrameMetadata(1).WallClockTicks;

        panel.SeekToFrameForTest(1);

        Assert.Equal(frame1Ticks, manager.BaseWallTicks);
    }

    /// <summary>
    /// RBF-P5T2: StepForwardForTest advances manager BaseWallTicks to the next frame.
    /// </summary>
    [Fact]
    public void RBF_P5T2_StepForward_AdvancesBaseWallTicks()
    {
        var path    = CreateTwoFrameRecording(nodeId: 1);
        using var manager = FederatedReplayManager.LoadGroup(new[] { path });
        var panel   = MakePanel(manager);

        // Start at frame 0
        int nodeId  = manager.LocalEntitiesProviderNodeId;
        var ctx     = manager.Contexts[nodeId];
        long frame0Ticks = ctx.Playback!.GetFrameMetadata(0).WallClockTicks;
        long frame1Ticks = ctx.Playback.GetFrameMetadata(1).WallClockTicks;
        manager.SetBaseWallTicks(frame0Ticks);

        panel.StepForwardForTest();

        Assert.Equal(frame1Ticks, manager.BaseWallTicks);
    }

    /// <summary>
    /// RBF-P5T2: StepBackwardForTest rewinds manager BaseWallTicks to the previous frame.
    /// </summary>
    [Fact]
    public void RBF_P5T2_StepBackward_RewindsBaseWallTicks()
    {
        var path    = CreateTwoFrameRecording(nodeId: 1);
        using var manager = FederatedReplayManager.LoadGroup(new[] { path });
        var panel   = MakePanel(manager);

        int nodeId  = manager.LocalEntitiesProviderNodeId;
        var ctx     = manager.Contexts[nodeId];
        long frame0Ticks = ctx.Playback!.GetFrameMetadata(0).WallClockTicks;
        long frame1Ticks = ctx.Playback.GetFrameMetadata(1).WallClockTicks;
        // Start at frame 1
        manager.SetBaseWallTicks(frame1Ticks);

        panel.StepBackwardForTest();

        Assert.Equal(frame0Ticks, manager.BaseWallTicks);
    }

    /// <summary>
    /// RBF-P5T2: OnLoadGroup is called exactly once when LoadFdpAsync is invoked;
    /// no per-file LoadRecording happens.
    /// </summary>
    [Fact]
    public async Task RBF_P5T2_LoadGroup_DoesNotDoubleLoad()
    {
        var stub = new StubFileDialogService_WithPaths(new[] { "/fake.fdp" });
        var panel = new ReplayTimelinePanel(
            null, () => 0,
            new StubExportService(), stub,
            new PlaybackHistoryTracker(), new InspectorState());

        int callCount = 0;
        panel.OnLoadGroup = paths => { callCount++; return null; };

        await panel.LoadFdpAsync();

        Assert.Equal(1, callCount);
    }

    /// <summary>
    /// RBF-P5T2: When OnLoadGroup returns a rejection string, it is stored in
    /// LoadGroupRejectionReason for modal display.
    /// </summary>
    [Fact]
    public async Task RBF_P5T2_LoadGroup_RejectionStillShowsModal()
    {
        var stub = new StubFileDialogService_WithPaths(new[] { "/fake.fdp" });
        var panel = new ReplayTimelinePanel(
            null, () => 0,
            new StubExportService(), stub,
            new PlaybackHistoryTracker(), new InspectorState());

        panel.OnLoadGroup = _ => "exercise mismatch";

        await panel.LoadFdpAsync();

        Assert.Equal("exercise mismatch", panel.LoadGroupRejectionReason);
    }

    // ── Extra stub needed for LoadGroup tests ─────────────────────────────

    private sealed class StubFileDialogService_WithPaths : IFileDialogService
    {
        private readonly string[]? _paths;
        public StubFileDialogService_WithPaths(string[]? paths) => _paths = paths;

        public Task<string?> ShowOpenFileDialogAsync(string callSiteId, string extensionFilter)
            => Task.FromResult<string?>(null);
        public Task<string?> ShowSaveAsDialogAsync(string callSiteId, string defaultFileName, string extensionFilter)
            => Task.FromResult<string?>(null);
        public Task<string[]?> ShowOpenMultipleFilesDialogAsync(string callSiteId, string extensionFilter)
            => Task.FromResult(_paths);
    }
}
