using System;
using System.IO;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Toolkit.ReplayBrowser.Federation;
using Xunit;

namespace Fdp.Presentation.ReplayBrowser.Federation;

/// <summary>
/// ⭐⭐ <b>U-obs-5 — <c>FederationPanel.BuildViewModel</c>, the BUILD half only.</b>
/// 📄 <c>docs/blueprints/batches/QUEUE_Panel_Observability_Sweep.md</c> group 4.
///
/// <para>⚠ <b>No <c>PanelSnapshot</c> rails here on purpose</b> — measured zero calls to
/// <see cref="FederationPanel.DrawContent"/> anywhere in the tree (<c>ReplayBrowserSubsystem</c>
/// constructs the panel and wires its event, but never draws it), so there is no host to declare/
/// register from. See the view-model's own remarks. Mirrors <c>RBF_P4T2_FederationPanelTests</c>'s
/// recording-fixture helpers.</para>
/// </summary>
public sealed class FederationPanelBuildsItsModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Guid _exerciseId = Guid.NewGuid();

    public FederationPanelBuildsItsModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FedPanelBuildTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string MakeMinimalRecording(int nodeId, Guid exerciseId)
    {
        string path = Path.Combine(_tempDir, $"node{nodeId}_{Guid.NewGuid():N}.fdp");
        var meta = new RecordingMetadata { ExerciseId = exerciseId, NodeId = nodeId };
        using var repo = new EntityRepository();
        using (var rec = new AsyncRecorder(path, meta))
            rec.CaptureKeyframe(repo, 1_000_000L, blocking: true, eventBus: repo.Bus);
        return path;
    }

    private FederatedReplayManager MakeTwoNodeManager(int nodeId1, int nodeId2)
    {
        string path1 = MakeMinimalRecording(nodeId1, _exerciseId);
        string path2 = MakeMinimalRecording(nodeId2, _exerciseId);
        return FederatedReplayManager.LoadGroup(new[] { path1, path2 });
    }

    [Fact]
    public void TheDump_CarriesEachNodesOffset()
    {
        using var manager = MakeTwoNodeManager(1, 2);
        var panel = new FederationPanel(manager);
        panel.SetNodeOffset(2, 500L);

        var vm = panel.BuildViewModel("federation-test", "federation");

        Assert.Equal(2, vm.Nodes.Count);
        Assert.True(vm.HasNonZeroOffset);
        Assert.Contains(vm.Nodes, n => n.NodeId == 2 && n.OffsetTicks == 500L);
    }
}
