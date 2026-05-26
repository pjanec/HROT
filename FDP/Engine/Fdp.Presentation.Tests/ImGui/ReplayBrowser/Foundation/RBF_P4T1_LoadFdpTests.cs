using System;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Toolkit.ReplayBrowser;
using Xunit;

namespace Fdp.Presentation.ReplayBrowser.Foundation;

/// <summary>
/// RBF-P4T1: Multi-file open dialog wiring tests for ReplayTimelinePanel.
/// Verifies OnLoadGroup is invoked with all selected paths, and that a rejection
/// reason is stored for modal display.
/// </summary>
public sealed class RBF_P4T1_LoadFdpTests
{
    // ── Stub ─────────────────────────────────────────────────────────────

    private sealed class StubFileDialogService : IFileDialogService
    {
        private readonly string[]? _paths;
        public StubFileDialogService(string[]? paths) => _paths = paths;

        public Task<string?> ShowOpenFileDialogAsync(string callSiteId, string extensionFilter)
            => Task.FromResult<string?>(null);

        public Task<string?> ShowSaveAsDialogAsync(string callSiteId, string defaultFileName, string extensionFilter)
            => Task.FromResult<string?>(null);

        public Task<string[]?> ShowOpenMultipleFilesDialogAsync(string callSiteId, string extensionFilter)
            => Task.FromResult(_paths);
    }

    private static ReplayTimelinePanel MakePanel(IFileDialogService dialogService)
    {
        var exportService   = new StubExportService();
        var playbackHistory = new PlaybackHistoryTracker();
        var inspectorState  = new InspectorState();
        return new ReplayTimelinePanel(null, () => 0, exportService, dialogService, playbackHistory, inspectorState);
    }

    private sealed class StubExportService : IRecordingExportService
    {
        public void ExportToJson(string input, string output, JsonExportOptions opts) { }
    }

    // ── Tests ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RBF_P4T1_LoadFdpAsync_PassesAllPathsToManager()
    {
        var stub = new StubFileDialogService(new[] { "/a.fdp", "/b.fdp" });
        string[]? capturedPaths = null;
        var panel = MakePanel(stub);
        panel.OnLoadGroup = paths => { capturedPaths = paths; return null; };

        await panel.LoadFdpAsync();

        Assert.NotNull(capturedPaths);
        Assert.Equal(new[] { "/a.fdp", "/b.fdp" }, capturedPaths);
    }

    [Fact]
    public async Task RBF_P4T1_LoadFdpAsync_RejectionShowsModal()
    {
        var stub = new StubFileDialogService(new[] { "/x.fdp" });
        var panel = MakePanel(stub);
        panel.OnLoadGroup = _ => "Exercise mismatch: two exercise IDs found";

        await panel.LoadFdpAsync();

        Assert.Equal("Exercise mismatch: two exercise IDs found", panel.LoadGroupRejectionReason);
    }
}
