using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.Core.FlightRecorder.Metadata;
using System.Linq;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Toolkit.ReplayBrowser.Federation;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Scenario;
using Hrot.Editor.AiShared.Review;
using Hrot.Editor.UiFrameRail;
using ImGuiNET;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Review;

/// <summary>⭐ G-7 S5 — the value the federated search rail's units carry.</summary>
[ComponentId(485)]
public struct SearchProbe { public float X; }

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

    /// <summary>⭐ CE-4432 W-1 — a file that fails to load (here: not a recording) is tried ONCE, not every frame; a rewrite of it is tried again.
    /// 🔴 Red-proof: drop the failed-path check in <c>RecordingFileReader.Of</c> ⇒ 10 attempts.</summary>
    [Fact]
    public void AFileThatFailsToLoad_IsTriedOnce_NotEveryFrame_UntilItIsRewritten()
    {
        string path = Path.Combine(_dir, "broken.fdp");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
        for (int i = 0; i < 10; i++) Assert.Null(_reader.Of(path));
        Assert.Equal(1, _reader.LoadAttempts);

        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));   // the file changed ⇒ worth one more try
        Assert.Null(_reader.Of(path));
        Assert.Equal(2, _reader.LoadAttempts);

        string good = Record();                                            // another path is loaded as before
        Assert.NotNull(_reader.Of(good));
        Assert.Same(_reader.Of(good), _reader.Of(good));
        Assert.Equal(3, _reader.LoadAttempts);
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

    // ── S5 · I6 — the federated search in the review panel (§6.12 S5) ────────────────────────────────────────

    /// <summary>Two node files of one exercise: unit 100 owned by node 1, unit 101 by node 2; EACH node holds BOTH (X = 10 × frame, so X &gt; 24 from frame 3), so a plain per-file search finds each unit twice.</summary>
    private static readonly long Zero = TimeSpan.FromSeconds(1).Ticks;

    private (string P1, string P2) RecordSearchPair()
    {
        var exercise = Guid.NewGuid();
        string Rec(int node)
        {
            string path = Path.Combine(_dir, $"search_{node}.fdp");
            var meta = new RecordingMetadata { ExerciseId = (Fdp.Core.ExerciseId)exercise, NodeId = (Fdp.Core.NodeId)node };
            using var repo = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>(); repo.RegisterComponent<NetworkAuthority>(); repo.RegisterComponent<SearchProbe>();
            var units = new List<Entity>();
            foreach (var (net, owner) in new[] { (100, 1), (101, 2) })
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new NetworkIdentity { Value = net });
                repo.AddComponent(e, new NetworkAuthority(owner, node));
                repo.AddComponent(e, new SearchProbe { X = 0 });
                units.Add(e);
            }
            using (var rec = new AsyncRecorder(path, meta))
            {
                repo.Tick();
                rec.CaptureKeyframe(repo, Zero, blocking: true, eventBus: repo.Bus);
                uint prev = repo.GlobalVersion;
                for (int f = 1; f <= 5; f++)
                {
                    repo.Tick();
                    foreach (var e in units) repo.GetComponentRW<SearchProbe>(e).X = f * 10;
                    rec.CaptureFrame(repo, prev, Zero + TimeSpan.FromSeconds(f).Ticks, blocking: true, eventBus: repo.Bus);
                    prev = repo.GlobalVersion;
                }
            }
            File.WriteAllText(path + ".meta.json", MetadataSerializer.Serialize(meta));
            return path;
        }
        return (Rec(1), Rec(2));
    }

    private static PropertyMatchDto XOver24() => new()
    {
        ComponentType = typeof(SearchProbe), PropertyPath = "X", Operator = SearchOperator.Equals,
        Predicate = new NumericPredicateDto { MinValue = 24, MaxValue = 1000 },
    };

    /// <summary>
    /// ⭐ G-7 S5 (I6) — the review's search panel, in the MERGED view, is NOT disabled and runs per participant file: a hit on a component its node did not own is dropped (each unit appears once, from its owner's file),
    /// owned hits from the two nodes are unioned in ONE list, and a click on a hit seeks the host and selects the unit BY NETWORK ID. 🔴 Red-proofs: the owner filter off ⇒ 12 hits ⇒ red; the panel's early return in
    /// Merged restored ⇒ the drawn-rows rail red.
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task G7S5_TheSearch_InMerged_RunsPerFile_DropsNonOwnedHits_UnionsTheOwnedOnes_AndAClickSelectsByNetworkId()
    {
        var (p1, p2) = RecordSearchPair();
        using var manager = FederatedReplayManager.LoadGroup(new[] { p1, p2 });
        manager.SetBaseWallTicks(Zero);
        var host = new Host { Files = new[] { p1 } };
        var review = new FileReview(new ReviewFileServices(
            RecordingFiles: () => host.Files, CurrentFrame: () => host.Frame, Seek: f => host.Seeks.Add(f),
            Selection: () => host.Selected, Select: id => { host.Selects.Add(id); return true; },
            Serializer: new ScenarioSerializerBuilder("test").Build(), Behaviors: null, Blueprints: null, Reader: _reader,
            Review: Fdp.Toolkit.ReplayBrowser.Federation.InlineReviewChannel.OverManager(manager)));
        review.SearchPanel.IsMergedViewActive = true;
        review.SearchPanel.PrepareForTest();
        review.SearchPanel.FriendlyCondition = XOver24();

        review.SearchPanel.StartSearch();
        await review.SearchPanel.SearchTaskForTest!;

        var hits = review.SearchPanel.Hits;
        Assert.Equal(6, hits.Count);                                                            // 2 units × frames 3..5 — NOT 12: the replicas' hits are dropped
        Assert.Equal(new long[] { 100, 101 }, hits.Select(h => h.NetworkId).Distinct().OrderBy(n => n).ToArray());   // ⭐ both units, unioned in one list
        Assert.Equal(new[] { 3, 3, 4, 4, 5, 5 }, hits.Select(h => h.Frame).ToArray());

        Assert.True(review.SearchPanel.ActivateHit(2));                                         // a click on a hit: frame 4, unit 100 or 101
        Assert.Equal(new long[] { 4 }, host.Seeks);                                             // the host seeks the frame …
        Assert.Equal(new[] { (int)hits[2].NetworkId }, host.Selects);                           // … and selects the unit BY NETWORK ID (the hit's entity is a handle in a participant's file)
    }

    [SkippableFact]
    public void G7S5_TheSearchPanel_InMerged_StillDrawsItsEditor()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var host = new Host { Files = new[] { Record() } };
        var review = host.Build(_reader);
        review.SearchPanel.IsMergedViewActive = true;                                           // the host shows the Merged view
        review.SearchPanel.ExpertView = true;
        using var frame = UiFrameHarness.Begin(640, 480);
        for (int i = 0; i < 6; i++)
            frame.Step(() =>
            {
                ImGui.SetNextWindowPos(new Vector2(8, 8)); ImGui.SetNextWindowSize(new Vector2(600, 400));
                ImGui.Begin("Search##s5");
                review.SearchPanel.DrawContent();
                ImGui.End();
            });
        Assert.True(review.SearchPanel.TreeRowsDrawn > 0, "the predicate editor is drawn in Merged (the old overlay said \"Search is not available in Merged View\" and returned)");
    }
}
