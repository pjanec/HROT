using System;
using System.IO;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Presentation.Tests.ImGui.Panels;
using Fdp.Presentation.Windows.ReplayBrowser;
using Fdp.Toolkit.ReplayBrowser.Federation;
using Xunit;

namespace Fdp.Presentation.Tests.ImGui.Windows.ReplayBrowser;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 follow-up — <c>FederationPanel</c>/<c>FederationWindow</c> converted to the
/// <c>PanelSnapshot</c> contract.</b> 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example ·
/// <c>docs/designs/replay-browser-frankenstein/DESIGN.md</c> §8. Mirrors
/// <c>ReplaySearchWindowDumpsItsResultsTests</c> and reuses
/// <c>FederationPanelBuildsItsModelTests</c>'s recording-fixture helpers. The panel was one of the six
/// no-host panels measured by <c>BP-467</c> — <c>ReplayBrowserSubsystem</c> constructed it and wired its
/// event, but never drew it; this test suite makes that live.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class FederationWindowDumpsItsModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Guid _exerciseId = Guid.NewGuid();

    public FederationWindowDumpsItsModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FedWindowTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string MakeMinimalRecording(int nodeId)
    {
        string path = Path.Combine(_tempDir, $"node{nodeId}_{Guid.NewGuid():N}.fdp");
        var meta = new RecordingMetadata { ExerciseId = _exerciseId, NodeId = nodeId };
        using var repo = new EntityRepository();
        using (var rec = new AsyncRecorder(path, meta))
            rec.CaptureKeyframe(repo, 1_000_000L, blocking: true, eventBus: repo.Bus);
        return path;
    }

    private FederatedReplayManager MakeTwoNodeManager()
    {
        string path1 = MakeMinimalRecording(1);
        string path2 = MakeMinimalRecording(2);
        return FederatedReplayManager.LoadGroup(new[] { path1, path2 });
    }

    // ── Rail 1 — instrumented at construction, on the PRODUCTION object ─────────────────────────

    [Fact]
    public void ConstructingTheWindow_DeclaresItInstrumented_BeforeItHasEverDrawn()
    {
        Assert.DoesNotContain("rb_federation_test", PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        using var manager = MakeTwoNodeManager();
        var panel = new FederationPanel(manager);
        var window = new FederationWindow("rb_federation_test", "Federation", "test-perspective", panel, new Vector4(1, 1, 1, 1));

        Assert.Contains("rb_federation_test", PanelSnapshot.RegisteredPanels);
        Assert.DoesNotContain("rb_federation_test", PanelSnapshot.CapturedPanels);
        Assert.Null(PanelSnapshot.TryGet("rb_federation_test"));
        Assert.NotNull(window);
    }

    // ── Rail 2 — the dump carries a real field ───────────────────────────────────────────────────

    [Fact]
    public void AfterABuild_TheDumpCarriesEachNodesOffset()
    {
        PanelSnapshot.CaptureEnabled = true;
        using var manager = MakeTwoNodeManager();
        var panel = new FederationPanel(manager);
        panel.SetNodeOffset(2, 500L);
        var window = new FederationWindow("rb_federation_test", "Federation", "test-perspective", panel, new Vector4(1, 1, 1, 1));

        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("rb_federation_test");
        Assert.NotNull(vm);
        Assert.Equal("rb_federation_test", vm!.PanelId);
        Assert.Equal(FederationWindow.Kind, vm.PanelKind);

        var dump = vm.Dump();
        Assert.True(dump["hasNonZeroOffset"]!.GetValue<bool>());
        Assert.Equal(2, dump["nodes"]!.AsArray().Count);
    }

    // ── Rail 3 — the flag gates the DUMP, not the BUILD ──────────────────────────────────────────

    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing()
    {
        using var manager = MakeTwoNodeManager();
        var window = new FederationWindow("rb_federation_test", "Federation", "test-perspective", new FederationPanel(manager), new Vector4(1, 1, 1, 1));

        var vm = window.SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains("rb_federation_test", PanelSnapshot.RegisteredPanels);
        Assert.NotNull(vm);   // ⭐ the BUILD is unaffected by the flag
    }
}
