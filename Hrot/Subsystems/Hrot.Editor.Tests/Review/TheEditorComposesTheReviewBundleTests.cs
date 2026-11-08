using System;
using System.IO;
using Fdp.Core;
using Fdp.Presentation.Icons;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Runner;
using Hrot.Editor;
using Hrot.Editor.AiShared.Review;
using Hrot.Editor.Review;
using Hrot.Presentation.DebugApi;
using Moq;
using Xunit;

namespace Hrot.Editor.Tests.Review;

/// <summary>
/// ⭐ R-2 / V2 (docs/UX/DESIGN_Review_Perspective.md §4 V2) — the editor composes <see cref="ReviewBundle"/> over its own-part AAR: the four windows are registered (listed in every layout, closed until opened); outside AAR
/// the two analysis windows say so; a search hit goes through THE seek (<see cref="OwnPartReplay"/>, the very code the debug API's <c>POST /replay/seek</c> runs) with the frame's moment, and selects its unit.
/// 🔴 Red-proofs (compile): <c>EditorSubsystem</c> composing no review bundle ⇒ the window rail red; <c>OwnPartReplay.SeekToFrame</c> not publishing the seek ⇒ the hit rail red.
/// </summary>
[Collection(Hrot.Editor.Tests.Windows.PanelSnapshotTestCollection.Name)]
public sealed class TheEditorComposesTheReviewBundleTests : IDisposable
{
    private static readonly long Zero = TimeSpan.FromSeconds(1).Ticks;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hrot-editor-review-" + Guid.NewGuid().ToString("N"));

    public TheEditorComposesTheReviewBundleTests() => Directory.CreateDirectory(_dir);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    /// <summary>A node file: a keyframe at 1 s then frames one second apart; unit 1042 (network id) throughout.</summary>
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

    [Fact(Timeout = 60_000)]
    public void V2_TheEditorRegistersTheFourReviewWindows_ClosedUntilOpened_AndOutsideAARTheAnalysisOnesSaySo()
    {
        var editor = new EditorSubsystem();
        try
        {
            editor.Initialize(new SubsystemConfig { Headless = true });
            var windows = new Fdp.Presentation.WindowManager.WindowManager(new IconAtlas(IntPtr.Zero, 512, 512));
            editor.RegisterWindows(windows);

            var bundle = editor.ReviewBundleForTest;
            Assert.NotNull(bundle);
            foreach (var id in new[] { "editor_review_diff", "editor_review_search", "rb_recording_info", "rb_json_export" })
                Assert.True(windows.TryGetWindow(id, out var w) && !w.IsOpen, id + " is registered and closed");
            Assert.Equal("Review", bundle!.FrameDiff.OwningPerspective);   // R-4 / V4: the bundle belongs to the Review layout

            var review = editor.FileReviewForTest!;
            Assert.Null(review.FilePath);                                                    // no replay ⇒ no file
            Assert.Equal(FileReview.NoRecording, review.DiffPanel.UnavailableReason!());     // "Open an exercise recording (AAR) to search / diff."
            Assert.Equal(FileReview.NoRecording, review.SearchPanel.UnavailableReason!());
        }
        finally { editor.Shutdown(); }
    }

    [Fact]
    public void V2_AHit_GoesThroughTheOneSeek_WithTheFramesMoment_AndSelectsTheUnit()
    {
        string path = Record();
        var master = new Mock<IClusterMasterControl>();
        SeekReplayIntent sent = default; bool published = false;
        master.Setup(m => m.SeekReplay(It.IsAny<SeekReplayIntent>())).Callback<SeekReplayIntent>(i => { sent = i; published = true; });
        long now = Zero;
        using var replay = new OwnPartReplay(() => path, () => new ReplayPosition(Zero, now), () => master.Object, () => ClusterState.OperatingReplay);
        var selected = new System.Collections.Generic.List<int>();
        var review = new FileReview(new ReviewFileServices(
            RecordingFiles: replay.RecordingFiles, CurrentFrame: replay.CurrentFrame,
            Seek: f => replay.SeekToFrame((int)f, "review"), Selection: () => null,
            Select: id => { selected.Add(id); return true; },
            Serializer: new Fdp.Toolkit.Scenario.ScenarioSerializerBuilder("test").Build(), Behaviors: null, Blueprints: null, Reader: replay.Reader));

        Assert.Equal(0, replay.CurrentFrame());                                              // the keyframe: now = the zero
        var ctx = replay.Reader.Of(path)!;
        ctx.SeekToFrame(3, suppressHistory: true);
        var hit = NetworkIdResolver.FindEntityByNetworkId(ctx.SandboxRepo, 1042);

        review.SeekToHit(3, hit);                                                            // a click on a result row

        Assert.True(published);                                                              // ⭐ the master's seek round, published once
        Assert.Equal(TimeSpan.FromSeconds(3).Ticks, sent.TargetWallTicks);                  // frame 3's moment after the zero (3 s)
        Assert.Equal(new[] { 1042 }, selected);                                              // and its unit selected
        master.Verify(m => m.SeekReplay(It.IsAny<SeekReplayIntent>()), Times.Once);

        now = Zero + TimeSpan.FromSeconds(2.5).Ticks;
        Assert.Equal(2, replay.CurrentFrame());                                              // the shown frame follows the replay's moment
    }
}
