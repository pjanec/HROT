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
        var meta = new RecordingMetadata { ExerciseId = (Fdp.Core.ExerciseId)(exerciseId), NodeId = (Fdp.Core.NodeId)(nodeId) };
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

    /// <summary>A node's file with THREE keyframes, 100 ms of wall time apart (wall ticks are TimeSpan ticks: 1 000 000 = 100 ms).</summary>
    private string MakeThreeFrameRecording(int nodeId)
    {
        string path = Path.Combine(_tempDir, $"node{nodeId}_{Guid.NewGuid():N}.fdp");
        var meta = new RecordingMetadata { ExerciseId = (Fdp.Core.ExerciseId)(_exerciseId), NodeId = (Fdp.Core.NodeId)(nodeId) };
        using var repo = new EntityRepository();
        using (var rec = new AsyncRecorder(path, meta))
            for (int i = 0; i < 3; i++)
                rec.CaptureKeyframe(repo, (i + 1) * 1_000_000L, blocking: true, eventBus: repo.Bus);
        File.WriteAllText(path + ".meta.json", MetadataSerializer.Serialize(meta));
        return path;
    }

    /// <summary>
    /// ⭐ G-7 S3 (R-4183, docs/UX/DESIGN_Review_Perspective.md §6.11a) — the Offsets slider's write: <c>SetNodeOffsetMs</c> (what the slider calls on a change) stores the offset in TICKS
    /// through <c>FederatedReplayManager.SetNodeOffset</c>, re-seeks at the current time (live: <c>OnTimeChanged</c> fires, which re-merges the view) and MOVES THAT NODE's chosen frame
    /// while the other stays; 0 ms puts it back. 🔴 Red-proof (compiles): <c>SetNodeOffsetMs</c> storing <c>ms</c> ticks instead of <c>ms × TicksPerMillisecond</c> ⇒ the node stays on its frame ⇒ red.
    /// </summary>
    [Fact]
    public void TheOffsetSlider_WritesSetNodeOffsetInMs_ReSeeksLive_AndMovesOnlyThatNodesFrame()
    {
        using var manager = FederatedReplayManager.LoadGroup(new[] { MakeThreeFrameRecording(1), MakeThreeFrameRecording(2) });
        var panel = new FederationPanel(manager);
        int changed = 0;
        manager.OnTimeChanged += () => changed++;

        manager.SetBaseWallTicks(1_000_000L);                                   // both on frame 0
        Assert.Equal(manager.Contexts[1].CurrentFrame, manager.Contexts[2].CurrentFrame);
        int frame0 = manager.Contexts[2].CurrentFrame;
        changed = 0;

        panel.SetNodeOffsetMs(2, 100d);                                         // the slider: node 2 at +100 ms

        Assert.Equal(1, changed);                                               // live: one re-seek / re-merge per change
        Assert.Equal(1_000_000L, manager.NodeOffsets[2]);                       // ms → wall ticks
        Assert.Equal(100d, panel.GetNodeOffsetMs(2));
        Assert.Equal(0d, panel.GetNodeOffsetMs(1));
        Assert.Equal(frame0 + 1, manager.Contexts[2].CurrentFrame);             // ⭐ node 2's chosen frame moved
        Assert.Equal(frame0, manager.Contexts[1].CurrentFrame);                 // node 1's did not
        Assert.True(panel.HasNonZeroOffset);

        panel.SetNodeOffsetMs(2, 0d);
        Assert.Equal(frame0, manager.Contexts[2].CurrentFrame);                 // back at 0 ms
        Assert.False(panel.HasNonZeroOffset);
    }

    [Fact]
    public void TheDump_CarriesTheRowNamesAndTheOffsetsInMs()
    {
        using var manager = MakeTwoNodeManager(1, 2);
        var panel = new FederationPanel(manager) { NodeName = n => n == 1 ? "Brain" : "Muscle" };
        panel.SetNodeOffsetMs(2, -40d);

        var vm = panel.BuildViewModel("federation-test", "federation");

        Assert.Contains(vm.Nodes, n => n.NodeId == 1 && n.Name == "Brain" && n.OffsetMs == 0d);
        Assert.Contains(vm.Nodes, n => n.NodeId == 2 && n.Name == "Muscle" && n.OffsetMs == -40d && n.OffsetTicks == -400_000L);
        Assert.Equal("Node 7", new FederationPanel(manager).NameOf(7));        // no role known ⇒ Node N
    }

    /// <summary>
    /// ⭐ G-7 S5 (I7, mockup ③) — the Source menu's <i>Local entities shown from</i> checkbox: default ON for every node; the checkbox's write (<c>SetLocalEntitiesShown</c>) flips ONLY that node on the
    /// manager and raises <c>OnTimeChanged</c> (both hosts re-merge); the dump carries it per row. 🔴 Red-proof (compiles): <c>SetLocalEntitiesShown</c> calling <c>SetLocalEntitiesProvider</c> instead ⇒ the flag never moves ⇒ red.
    /// </summary>
    [Fact]
    public void TheLocalEntitiesCheckbox_DefaultsOn_WritesTheManagerPerNode_AndIsInTheDump()
    {
        using var manager = MakeTwoNodeManager(1, 2);
        var panel = new FederationPanel(manager) { NodeName = n => n == 1 ? "CGF-1" : "SimHost-1" };
        int changed = 0;
        manager.OnTimeChanged += () => changed++;
        Assert.True(panel.IsLocalEntitiesShown(1) && panel.IsLocalEntitiesShown(2));
        Assert.All(panel.BuildViewModel("federation-test", "federation").Nodes, n => Assert.True(n.LocalsShown));

        panel.SetLocalEntitiesShown(2, false);

        Assert.Equal(1, changed);
        Assert.True(panel.IsLocalEntitiesShown(1));
        Assert.False(panel.IsLocalEntitiesShown(2));
        var vm = panel.BuildViewModel("federation-test", "federation");
        Assert.Contains(vm.Nodes, n => n.NodeId == 1 && n.LocalsShown);
        Assert.Contains(vm.Nodes, n => n.NodeId == 2 && !n.LocalsShown && n.Name == "SimHost-1");
    }
}
