using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Scenario;
using Hrot.Editor.AiShared.Review;
using Hrot.Editor.UiFrameRail;
using ImGuiNET;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Review;

/// <summary>
/// ⭐ R-2 / V2 (docs/UX/DESIGN_Review_Perspective.md §4 V2) — the review windows over a host's own-part recording FILE (<see cref="FileReview"/>): outside AAR the analysis windows say so; a search hit SEEKS the
/// host to its frame and SELECTS its entity (retried until the world holds it); the diff reads the file; a steady frame costs 0 B.
/// 🔴 Red-proofs (compile): <c>SeekToHit</c> not calling <c>Seek</c> ⇒ the hit rail red; an allocation in <c>Update</c> ⇒ the 0 B rail red.
/// </summary>
[Collection(Hrot.Editor.AiShared.Tests.PanelSnapshotTestCollection.Name)]
public sealed class TheFileReviewTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hrot-file-review-" + Guid.NewGuid().ToString("N"));
    private readonly RecordingFileReader _reader = new();

    public TheFileReviewTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        _reader.Dispose();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    /// <summary>A node file of 4 frames; one unit (network id 100) whose owner id changes 1 → 2 at frame 2.</summary>
    private string Record()
    {
        string path = Path.Combine(_dir, "node_1.fdp");
        using var repo = new EntityRepository();
        repo.RegisterComponent<NetworkIdentity>();
        repo.RegisterComponent<NetworkAuthority>();
        var e = repo.CreateEntity();
        repo.AddComponent(e, new NetworkIdentity { Value = 100 });
        repo.AddComponent(e, new NetworkAuthority(1, 1));
        using var rec = new AsyncRecorder(path, new RecordingMetadata { ExerciseId = (Fdp.Core.ExerciseId)Guid.NewGuid(), NodeId = (Fdp.Core.NodeId)1 });
        long t0 = TimeSpan.FromSeconds(1).Ticks;
        rec.CaptureKeyframe(repo, t0, blocking: true, eventBus: repo.Bus);
        for (uint f = 1; f <= 3; f++)
        {
            repo.Tick();
            if (f == 2) repo.GetComponentRW<NetworkAuthority>(e).PrimaryOwnerId = 2;
            rec.CaptureFrame(repo, f - 1, t0 + TimeSpan.FromSeconds(f).Ticks, blocking: true, eventBus: repo.Bus);
        }
        return path;
    }

    private sealed class Host
    {
        public string[] Files = Array.Empty<string>();
        public long Frame;
        public int? Selected;
        public readonly List<long> Seeks = new();
        public readonly List<int> Selects = new();
        public bool HoldsUnit;

        public FileReview Build(RecordingFileReader reader) => new(new ReviewFileServices(
            RecordingFiles: () => Files, CurrentFrame: () => Frame, Seek: f => Seeks.Add(f),
            Selection: () => Selected, Select: id => { Selects.Add(id); return HoldsUnit; },
            Serializer: new ScenarioSerializerBuilder("test").Build(), Behaviors: null, Blueprints: null, Reader: reader));
    }

    [Fact]
    public void V2_OutsideAAR_TheTwoAnalysisWindowsSayTheyNeedARecording()
    {
        var host = new Host();
        var review = host.Build(_reader);
        Assert.Null(review.FilePath);
        Assert.Equal(FileReview.NoRecording, review.DiffPanel.UnavailableReason!());
        Assert.Equal(FileReview.NoRecording, review.SearchPanel.UnavailableReason!());
        Assert.Contains("Open an exercise recording (AAR)", FileReview.NoRecording);

        host.Files = new[] { Record() };
        Assert.Null(review.DiffPanel.UnavailableReason!());
        Assert.Null(review.SearchPanel.UnavailableReason!());
        review.Update(diffOpen: false);
        Assert.Equal(host.Files[0], review.SearchPanel.CurrentFilePath);   // the search reads the host's file (D4)
    }

    [Fact]
    public void V2_AHit_SeeksTheHostToItsFrame_AndSelectsItsEntity_RetriedUntilTheWorldHoldsIt()
    {
        var host = new Host();
        host.Files = new[] { Record() };
        var review = host.Build(_reader);

        var ctx = _reader.Of(host.Files[0])!;
        ctx.SeekToFrame(2, suppressHistory: true);
        var hit = NetworkIdResolver.FindEntityByNetworkId(ctx.SandboxRepo, 100);
        Assert.False(hit.IsNull);

        review.SeekToHit(2, hit);                       // what a click on a result row calls
        Assert.Equal(new long[] { 2 }, host.Seeks);     // the ONE seek, called with the frame
        Assert.Equal(new[] { 100 }, host.Selects);      // the selection asked for the unit's network id
        host.HoldsUnit = true;                          // the seek landed; the world now holds the unit
        review.Update(diffOpen: false);
        Assert.Equal(new[] { 100, 100 }, host.Selects); // retried once, then done
        review.Update(diffOpen: false);
        Assert.Equal(2, host.Selects.Count);
    }

    [Fact]
    public void V2_TheDiff_ReadsTheFile_TheUnitAtTheFrameAgainstTheFrameBefore()
    {
        var host = new Host();
        host.Files = new[] { Record() };
        var review = host.Build(_reader);

        var diffs = review.ComputeDiff(host.Files[0], 2, 100);
        Assert.NotEmpty(diffs);                          // the owner id moved 1 → 2 between frames 1 and 2

        host.Frame = 2; host.Selected = 100;
        review.Update(diffOpen: true);
        Assert.NotEmpty(review.DiffPanel.CurrentDiffs);  // the per-frame step fills the panel from (file, frame, unit)
    }

    /// <summary>⭐ V2's 0 B rail — both windows' panels drawn open with no input, and the per-frame step, cost nothing on a steady frame.</summary>
    [SkippableFact]
    public void V2_EachWindowOpenWithNoInput_CostsZeroBytesPerSteadyFrame()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var host = new Host();
        host.Files = new[] { Record() };
        host.Frame = 2; host.Selected = 100;
        var review = host.Build(_reader);

        using var frame = UiFrameHarness.Begin(640, 480);
        long update = -1, diff = -1, search = -1, outsideDiff = -1, outsideSearch = -1;
        for (int i = 0; i < 10; i++)
            frame.Step(() =>
            {
                ImGui.SetNextWindowPos(new Vector2(8, 8)); ImGui.SetNextWindowSize(new Vector2(600, 200));
                ImGui.Begin("Diff##v2");
                long b = GC.GetAllocatedBytesForCurrentThread(); review.Update(diffOpen: true); update = GC.GetAllocatedBytesForCurrentThread() - b;
                b = GC.GetAllocatedBytesForCurrentThread(); review.DiffPanel.DrawContent(); diff = GC.GetAllocatedBytesForCurrentThread() - b;
                ImGui.End();
                ImGui.SetNextWindowPos(new Vector2(8, 220)); ImGui.SetNextWindowSize(new Vector2(600, 240));
                ImGui.Begin("Search##v2");
                b = GC.GetAllocatedBytesForCurrentThread(); review.SearchPanel.DrawContent(); search = GC.GetAllocatedBytesForCurrentThread() - b;
                ImGui.End();
            });
        Console.WriteLine($"V2 steady-frame bytes: update {update}, diff {diff}, search {search}");
        Assert.Equal(0, update);
        Assert.Equal(0, diff);
        Assert.Equal(0, search);

        host.Files = Array.Empty<string>();             // outside AAR: the "say so" draw is free too
        for (int i = 0; i < 6; i++)
            frame.Step(() =>
            {
                ImGui.SetNextWindowPos(new Vector2(8, 8)); ImGui.SetNextWindowSize(new Vector2(600, 200));
                ImGui.Begin("Diff##v2");
                review.Update(diffOpen: true);
                long b = GC.GetAllocatedBytesForCurrentThread(); review.DiffPanel.DrawContent(); outsideDiff = GC.GetAllocatedBytesForCurrentThread() - b;
                ImGui.End();
                ImGui.Begin("Search##v2");
                b = GC.GetAllocatedBytesForCurrentThread(); review.SearchPanel.DrawContent(); outsideSearch = GC.GetAllocatedBytesForCurrentThread() - b;
                ImGui.End();
            });
        Assert.Equal(0, outsideDiff);
        Assert.Equal(0, outsideSearch);
    }
}
