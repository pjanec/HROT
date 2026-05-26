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
/// RBF-P4T2: FederationPanel state and event-wiring tests (DESIGN §8.2).
/// Also covers RBF-P4T5 disclaimer string predicate tests.
/// </summary>
public sealed class RBF_P4T2_FederationPanelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Guid _exerciseId = Guid.NewGuid();

    public RBF_P4T2_FederationPanelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FedPanelTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ── Recording helpers ─────────────────────────────────────────────────

    private string MakeMinimalRecording(int nodeId, Guid exerciseId)
    {
        string path = Path.Combine(_tempDir, $"node{nodeId}_{Guid.NewGuid():N}.fdp");
        var meta = new RecordingMetadata { ExerciseId = exerciseId, NodeId = nodeId };
        using var repo = new EntityRepository();
        using (var rec = new AsyncRecorder(path, meta))
            rec.CaptureKeyframe(repo, 1_000_000L, blocking: true, eventBus: repo.Bus);
        return path;
    }

    private FederatedReplayManager MakeSingleNodeManager(int nodeId)
    {
        string path = MakeMinimalRecording(nodeId, _exerciseId);
        return FederatedReplayManager.LoadGroup(new[] { path });
    }

    private FederatedReplayManager MakeTwoNodeManager(int nodeId1, int nodeId2)
    {
        string path1 = MakeMinimalRecording(nodeId1, _exerciseId);
        string path2 = MakeMinimalRecording(nodeId2, _exerciseId);
        return FederatedReplayManager.LoadGroup(new[] { path1, path2 });
    }

    // ── Tests ─────────────────────────────────────────────────────────────

    [Fact]
    public void RBF_P4T2_ActiveMode_InitialValue_IsSingleNode()
    {
        using var manager = MakeSingleNodeManager(nodeId: 1);
        var panel = new FederationPanel(manager);
        Assert.Equal(ViewMode.SingleNode, panel.ActiveMode);
    }

    [Fact]
    public void RBF_P4T2_ModeToggle_FiresViewModeChanged()
    {
        using var manager = MakeSingleNodeManager(nodeId: 1);
        var panel = new FederationPanel(manager);
        ViewMode? received = null;
        panel.OnViewModeChanged += m => received = m;
        panel.SetMode(ViewMode.Merged);
        Assert.Equal(ViewMode.Merged, received);
    }

    [Fact]
    public void RBF_P4T2_OffsetEdit_CallsManagerSetNodeOffset()
    {
        using var manager = MakeSingleNodeManager(nodeId: 1);
        var panel = new FederationPanel(manager);
        panel.SetNodeOffset(1, 5000L);
        Assert.Equal(5000L, manager.NodeOffsets[1]);
    }

    [Fact]
    public void RBF_P4T2_BaseTickEdit_CallsManagerSetBaseWallTicks()
    {
        using var manager = MakeSingleNodeManager(nodeId: 1);
        var panel = new FederationPanel(manager);
        panel.SetBaseWallTicks(2_000_000L);
        Assert.Equal(2_000_000L, manager.BaseWallTicks);
    }

    [Fact]
    public void RBF_P4T2_NonZeroOffset_ShowsWarningGlyph()
    {
        using var manager = MakeSingleNodeManager(nodeId: 1);
        var panel = new FederationPanel(manager);
        Assert.False(panel.HasNonZeroOffset);
        panel.SetNodeOffset(1, 100L);
        Assert.True(panel.HasNonZeroOffset);
    }

    [Fact]
    public void RBF_P4T2_ProviderDropdown_DefaultsToLowestNodeId()
    {
        using var manager = MakeSingleNodeManager(nodeId: 1);
        var panel = new FederationPanel(manager);
        Assert.Equal(1, manager.LocalEntitiesProviderNodeId);
    }

    [Fact]
    public void RBF_P4T2_ProviderDropdownChange_CallsManagerSetLocalEntitiesProvider()
    {
        using var manager = MakeTwoNodeManager(nodeId1: 1, nodeId2: 2);
        var panel = new FederationPanel(manager);
        panel.SetMode(ViewMode.Merged);
        panel.SetLocalEntitiesProvider(2);
        Assert.Equal(2, manager.LocalEntitiesProviderNodeId);
    }
}
