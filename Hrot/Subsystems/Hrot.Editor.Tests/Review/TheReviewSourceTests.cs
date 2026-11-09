using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.Presentation.Icons;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.ReplayBrowser.Federation;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Runner;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.ScenarioRules;
using Hrot.Editor;
using Hrot.Editor.Review;
using Hrot.Editor.UiFrameRail;
using Hrot.UI.Common.Facades;
using Moq;
using Xunit;
using ImGui = ImGuiNET.ImGui;

namespace Hrot.Editor.Tests.Review;

/// <summary>
/// ⭐ R-5 / V6 (docs/UX/DESIGN_Review_Perspective.md §3 D6 / D7 / D9, §4 V6) — <b>a participant GROUP with no part of this node opens MERGED in the all-in-one editor</b> (the EMPTY world fixed): four participant
/// files of one exercise (two CGFs, a SimHost, a Player — built here, as the Federation tests build theirs) under a staging root this node has no part in. The source fills the editor's ONE world from the merge
/// (<see cref="TransientMasterBuilder.BuildInto"/> — the merge's own <c>DeserializeWith</c>, ⛔ no second load path); a component only CGF's file holds is in it; <c>SelectPart</c> fills from one part and a part plays;
/// Merged steps / scrubs and its ▶ is greyed with the reason; the status segment says each state; an exercise with this node's part stays today's AAR.
/// 🔴 Red-proofs (compile): <c>OnRecordingOpened</c> never taking a group (the branch removed ⇒ the empty world returns) ⇒ the open rails red; <c>ReviewSourceTransport.CanPlay</c> true in Merged ⇒ the ▶ rail red.
/// </summary>
[Collection(Hrot.Editor.Tests.Windows.PanelSnapshotTestCollection.Name)]
public sealed class TheReviewSourceTests : IDisposable
{
    private static readonly long Zero = TimeSpan.FromSeconds(1).Ticks;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hrot-review-source-" + Guid.NewGuid().ToString("N"));
    private readonly Guid _exercise = Guid.NewGuid();
    private readonly ScenarioSerializer _serializer;

    public TheReviewSourceTests()
    {
        Directory.CreateDirectory(_root);
        // the component tables the serializer compiles its copies for (as the Federation tests prime theirs) — registered, never cleared (the registry is process-wide)
        using var prime = new EntityRepository();
        prime.RegisterComponent<NetworkIdentity>();
        prime.RegisterComponent<NetworkAuthority>();
        prime.RegisterComponent<ObjectiveStatus>();
        _serializer = new ScenarioSerializerBuilder("TestSubsystem").Build();
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    // ── the four files ───────────────────────────────────────────────────────────────────────

    private string PathOf(int node) => OrchestrationConstants.GetExerciseRecordingFilePath(OrchestrationConstants.GetNodeStagingRoot(_root, node), _exercise, node);

    /// <summary>One node's file: a keyframe at 1 s then four frames one second apart; <paramref name="owned"/> are (network id, owns the objective state, progress base).</summary>
    private string Record(int node, params (int Net, bool Owned, bool Objective, int Base)[] units)
    {
        string path = PathOf(node);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var meta = new RecordingMetadata { ExerciseId = (ExerciseId)_exercise, NodeId = (NodeId)node, ScenarioName = "night-attack", Timestamp = DateTime.UtcNow, Duration = TimeSpan.FromSeconds(4) };
        using var repo = new EntityRepository();
        repo.RegisterComponent<NetworkIdentity>(); repo.RegisterComponent<NetworkAuthority>(); repo.RegisterComponent<ObjectiveStatus>();
        var entities = new List<(Entity E, bool Objective, int Base)>();
        foreach (var (net, owned, objective, @base) in units)
        {
            var e = repo.CreateEntity();
            repo.AddComponent(e, new NetworkIdentity { Value = net });
            repo.SetAuthority<NetworkIdentity>(e, owned);
            if (owned)
            {
                repo.AddComponent(e, new NetworkAuthority { PrimaryOwnerId = node });
                repo.SetAuthority<NetworkAuthority>(e, true);
            }
            if (objective)
            {
                repo.AddComponent(e, new ObjectiveStatus { ProgressCur = @base, ProgressNeed = 100, State = ObjectiveState.Active });
                repo.SetAuthority<ObjectiveStatus>(e, owned);
            }
            entities.Add((e, objective, @base));
        }
        using (var rec = new AsyncRecorder(path, meta))
        {
            repo.Tick();
            rec.CaptureKeyframe(repo, Zero, blocking: true, eventBus: repo.Bus);
            uint prev = repo.GlobalVersion;
            for (int f = 1; f <= 4; f++)
            {
                repo.Tick();
                foreach (var (e, objective, @base) in entities)
                    if (objective) repo.SetComponent(e, new ObjectiveStatus { ProgressCur = @base + f, ProgressNeed = 100, State = ObjectiveState.Active });
                rec.CaptureFrame(repo, prev, Zero + TimeSpan.FromSeconds(f).Ticks, blocking: true, eventBus: repo.Bus);
                prev = repo.GlobalVersion;
            }
        }
        File.WriteAllText(path + ".meta.json", MetadataSerializer.Serialize(meta));
        return path;
    }

    /// <summary>Night attack: CGF-A (2) owns unit 100 and its objective state; CGF-B (3) owns unit 200 and its objective state; the SimHost (4) holds a ghost of 100 and owns 300; the Player (5) owns 400.</summary>
    private void FourParticipants()
    {
        Record(2, (100, true, true, 0));
        Record(3, (200, true, true, 10));
        Record(4, (100, false, false, 0), (300, true, false, 0));
        Record(5, (400, true, false, 0));
    }

    private sealed class Rig : IDisposable
    {
        public readonly EntityRepository World = new();
        public readonly ReviewSource Source;
        public readonly Mock<ITimeTransportFacade> Facade = new();
        public bool Ready = true, InReplay = true;
        public int? Selected;
        public readonly List<int> Selects = new();
        public int Clears;

        public Rig(string root, ScenarioSerializer serializer, int thisNode = 1)
        {
            World.RegisterComponent<NetworkIdentity>(); World.RegisterComponent<NetworkAuthority>(); World.RegisterComponent<ObjectiveStatus>();
            Facade.SetupGet(f => f.IsPaused).Returns(true);
            Facade.SetupGet(f => f.TimeScale).Returns(1f);
            Facade.SetupGet(f => f.IsPlayPauseEnabled).Returns(true);
            var builder = new TransientMasterBuilder(serializer);
            Source = new ReviewSource(() => World, () => builder, () => { Clears++; World.SoftClear(); }, () => root, thisNode,
                ready: () => Ready, inReplay: () => InReplay, selection: () => Selected, select: n => { Selects.Add(n); return true; }, facade: () => Facade.Object);
        }

        public void Open(Guid exercise)
        {
            Assert.True(Source.OnRecordingOpened(exercise));
            Source.Update(0);
        }

        public void Dispose() { Source.Dispose(); World.Dispose(); }
    }

    private static int ProgressOfNet(EntityRepository world, long net)
    {
        for (int i = 0; i <= world.MaxEntityIndex; i++)
        {
            var e = new Entity(i, world.GetMetadata(i).Generation);
            if (world.IsAlive(e) && world.HasComponent<NetworkIdentity>(e) && world.GetComponent<NetworkIdentity>(e).Value == net)
                return world.HasComponent<ObjectiveStatus>(e) ? world.GetComponent<ObjectiveStatus>(e).ProgressCur : int.MinValue;
        }
        return int.MaxValue;   // no such unit
    }

    // ── the branch ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void V6_AGroupWithNoPartOfThisNode_OpensMerged_TheWorldIsNotEmpty()
    {
        FourParticipants();
        using var rig = new Rig(_root, _serializer);

        Assert.Equal(0, rig.World.EntityCount);                       // the all-in-one editor's world: nothing
        Assert.True(rig.Source.OnRecordingOpened(_exercise));          // ⭐ the branch: a group with no part of this node ⇒ the source takes it
        Assert.True(rig.Source.IsPending);
        rig.Ready = false; rig.Source.Update(0);
        Assert.Equal(0, rig.World.EntityCount);                       // the replay is not up yet: nothing is written under it
        rig.Ready = true;  rig.Source.Update(0);

        Assert.True(rig.Source.IsOpen);
        Assert.Equal(ReviewSourceKind.Merged, rig.Source.Kind);        // Merged by default (Q7)
        using var manager = FederatedReplayManager.LoadGroup(new[] { PathOf(2), PathOf(3), PathOf(4), PathOf(5) });
        manager.SetBaseWallTicks(Zero);
        using var expected = new TransientMasterBuilder(_serializer).Build(manager);
        Assert.Equal(4, expected.EntityCount);
        Assert.Equal(expected.EntityCount, rig.World.EntityCount);     // ⭐ NOT empty: the entity count is the merge's — one unit per network id (the SimHost's ghost of 100 folds into CGF-A's)
        Assert.True(rig.Clears >= 1);                                  // cleared by the world boundary first (D7)
        foreach (var net in new long[] { 100, 200, 300, 400 })
            Assert.NotEqual(int.MaxValue, ProgressOfNet(rig.World, net));
    }

    /// <summary>
    /// ⭐ G-7 S3 (R-4183) — the editor's Review shows the SAME Federation panel (one slider implementation): an offset written through it re-merges the editor's world live — CGF-A's (node 2)
    /// frame moves +1 s, and the unit it owns shows the later progress; its row carries the participant's name. 🔴 Red-proof: the panel built without a re-merge hook / offset not applied ⇒ red.
    /// </summary>
    [Fact]
    public void V6_AnOffsetOnTheSwitchPanel_ReMergesTheWorldLive_ForThatNode()
    {
        FourParticipants();
        using var rig = new Rig(_root, _serializer);
        rig.Open(_exercise);
        var panel = rig.Source.SwitchPanel!;

        Assert.Equal(0, ProgressOfNet(rig.World, 100));
        Assert.Equal(rig.Source.NameOf(2), panel.NameOf(2));           // the participants' names (Q-B6), not a second naming
        panel.SetNodeOffsetMs(2, 2000d);                               // the slider: CGF-A at +2 s
        Assert.Equal(2, ProgressOfNet(rig.World, 100));                // ⭐ its component, merged from ITS frame two seconds on
        Assert.Equal(10, ProgressOfNet(rig.World, 200));               // CGF-B (node 3) stayed on its own frame
        panel.SetNodeOffsetMs(2, 0d);
        Assert.Equal(0, ProgressOfNet(rig.World, 100));
    }

    [Fact]
    public void V6_AComponentOnlyCgfsFileHolds_IsInTheMergedWorld()
    {
        FourParticipants();
        using var rig = new Rig(_root, _serializer);
        rig.Open(_exercise);

        Assert.Equal(0, ProgressOfNet(rig.World, 100));                // CGF-A's objective state — the SimHost's and the Player's files hold none of it
        Assert.Equal(10, ProgressOfNet(rig.World, 200));               // CGF-B's
        Assert.Equal(int.MinValue, ProgressOfNet(rig.World, 300));     // the SimHost's own unit carries no objective state: absent, not invented
    }

    [Fact]
    public void V6_AnExerciseWithThisNodesPart_IsTodaysAar_TheSourceStaysOut()
    {
        FourParticipants();
        Record(1, (500, true, false, 0));                              // this node's own file of the exercise
        using var rig = new Rig(_root, _serializer);

        Assert.False(rig.Source.OnRecordingOpened(_exercise));         // ⭐ unchanged: each node replays its own file (W5)
        Assert.False(rig.Source.IsPending);
        rig.Source.Update(0);
        Assert.False(rig.Source.IsOpen);
        Assert.Equal(0, rig.World.EntityCount);                        // the source wrote nothing
        Assert.Null(rig.Source.StatusText());                          // an ordinary AAR: nothing extra on the status bar
        Assert.False(rig.Source.OnRecordingOpened(Guid.NewGuid()));    // an exercise the inventory does not know: not the source's either
    }

    [Fact]
    public void V6_ThePickerSaysWhatYouGet()
    {
        FourParticipants();
        Record(1, (500, true, false, 0));
        var own = Guid.NewGuid();
        using var rig = new Rig(_root, _serializer);

        var group = RecordingGroupInventory.Scan(_root).Single(g => g.ExerciseId.Value == _exercise);
        Assert.Equal("this editor's run", rig.Source.PickerNote(rig.Source.Classify(group)));   // this node has its part

        // the same four participants, no part of this node ⇒ "4 participants — opens MERGED"
        File.Delete(PathOf(1)); File.Delete(PathOf(1) + ".meta.json");
        group = RecordingGroupInventory.Scan(_root).Single(g => g.ExerciseId.Value == _exercise);
        Assert.Equal("4 participants — opens MERGED", rig.Source.PickerNote(rig.Source.Classify(group)));
        Assert.Null(rig.Source.PickerNote(null));
        _ = own;
    }

    // ── the Source switch ────────────────────────────────────────────────────────────────────

    [Fact]
    public void V6_SelectPart_FillsFromOnePart_AndPlays()
    {
        FourParticipants();
        using var rig = new Rig(_root, _serializer);
        rig.Open(_exercise);

        rig.Source.SelectPart(3);                                      // CGF-B's part
        Assert.Equal(ReviewSourceKind.Part, rig.Source.Kind);
        Assert.Equal(1, rig.World.EntityCount);                        // THAT node's world only: its one unit
        Assert.Equal(10, ProgressOfNet(rig.World, 200));
        Assert.Equal(int.MaxValue, ProgressOfNet(rig.World, 100));     // CGF-A's unit is not in CGF-B's file

        var t = rig.Source.Transport;
        Assert.True(t.CanPlay);                                        // ▶ is available on a part
        Assert.Null(t.PlayDisabledReason);
        rig.Facade.SetupGet(f => f.IsPaused).Returns(false);           // the toolbar's ▶ is pressed
        Assert.True(t.IsPlaying);
        int before = t.CurrentFrame;
        rig.Source.Update(2.0);                                        // two seconds of playback at 1×
        Assert.True(t.CurrentFrame > before, "the part played");
        Assert.Equal(12, ProgressOfNet(rig.World, 200));               // frame 2 of CGF-B: base 10 + 2

        rig.Source.SelectMerged();                                     // and back
        Assert.Equal(ReviewSourceKind.Merged, rig.Source.Kind);
        Assert.Equal(4, rig.World.EntityCount);
    }

    [Fact]
    public void V6_Merged_Steps_AndPlayIsGreyedWithTheReason()
    {
        FourParticipants();
        using var rig = new Rig(_root, _serializer);
        rig.Open(_exercise);
        var t = rig.Source.Transport;

        Assert.True(t.HasRecording);
        Assert.Equal(5, t.TotalFrames);
        Assert.Equal(0, t.CurrentFrame);
        Assert.False(t.CanPlay);                                       // ⭐ ▶ greyed …
        Assert.Equal(ReviewSourceTransport.MergedPlayReason, t.PlayDisabledReason);   // … with the reason
        Assert.False(t.IsPlaying);

        t.Step(1);
        Assert.Equal(1, t.CurrentFrame);                               // ⭐ Step(+1) changes the frame …
        Assert.Equal(1, ProgressOfNet(rig.World, 100));                // … and the merge was rebuilt into the world at it
        t.Step(1);
        Assert.Equal(2, ProgressOfNet(rig.World, 100));
        t.Step(-1);                                                    // ⭐ Step(-1)
        Assert.Equal(1, t.CurrentFrame);
        Assert.Equal(1, ProgressOfNet(rig.World, 100));
        t.Seek(4);                                                     // scrub
        Assert.Equal(4, t.CurrentFrame);
        Assert.Equal(4, ProgressOfNet(rig.World, 100));
        Assert.Equal(14, ProgressOfNet(rig.World, 200));
        t.Step(1);                                                     // at the end: nothing
        Assert.Equal(4, t.CurrentFrame);
        Assert.Equal(4, rig.World.EntityCount);                        // every step left the world whole
    }

    [Fact]
    public void V6_TheSelection_FollowsItsUnitAcrossAStep()
    {
        FourParticipants();
        using var rig = new Rig(_root, _serializer);
        rig.Open(_exercise);
        rig.Selected = 200;
        rig.Source.Transport.Step(1);
        Assert.Contains(200, rig.Selects);                             // the merge makes new entities each step: the selection is re-landed by network id
    }

    // ── the status bar's words (D9) ──────────────────────────────────────────────────────────

    [Fact]
    public void V6_TheStatusSegment_SaysEachState()
    {
        FourParticipants();
        using var rig = new Rig(_root, _serializer);
        Assert.Null(rig.Source.StatusText());                                                       // an ordinary AAR / nothing open: nothing

        rig.Source.OnRecordingOpened(_exercise);
        Assert.Equal("Review: opening the group…", rig.Source.StatusText());                       // pending until the replay is up

        rig.Source.Update(0);
        Assert.Equal("Merged · 4 participants (Node 2, Node 3, Node 4, Node 5) — step / scrub", rig.Source.StatusText());

        rig.Source.SelectPart(2);
        Assert.Equal("Part: Node 2", rig.Source.StatusText());
        rig.Source.SelectMerged();
        Assert.StartsWith("Merged · 4 participants", rig.Source.StatusText());

        rig.InReplay = false; rig.Source.Update(0);                                                 // the cluster left AAR: the group closes
        Assert.False(rig.Source.IsOpen);
        Assert.Null(rig.Source.StatusText());
    }

    [Fact]
    public void V6_AGroupThatCannotBeLoaded_SaysWhy_NotAnEmptyWorldInSilence()
    {
        FourParticipants();
        File.Delete(PathOf(4) + ".meta.json");                         // the sidecar is gone: the inventory no longer counts that file
        File.WriteAllText(PathOf(5) + ".meta.json", "{ not json");     // and this one is corrupt
        using var rig = new Rig(_root, _serializer);
        rig.Source.OnRecordingOpened(_exercise);
        rig.Source.Update(0);
        Assert.NotNull(rig.Source.StatusText());                       // pending or a stated failure — never a silent empty world
    }

    [Fact]
    public void V6_TheFiles_TheReviewWindowsRead_AreTheSourcesPrimary_AndAPartsOwn()
    {
        FourParticipants();
        using var rig = new Rig(_root, _serializer);
        rig.Open(_exercise);
        Assert.Equal(new[] { PathOf(2) }, rig.Source.RecordingFiles());   // Merged: the primary participant's (the lowest node)
        rig.Source.SelectPart(4);
        Assert.Equal(new[] { PathOf(4) }, rig.Source.RecordingFiles());
        Assert.Same(rig.Source.Manager, rig.Source.Manager);
        Assert.NotNull(rig.Source.Manager);                              // Recording info / JSON export read this
    }

    // ── the editor composes it ───────────────────────────────────────────────────────────────

    [Fact(Timeout = 60_000)]
    public void V6_TheEditorComposesTheSource_TheStatusSegmentAtTheBarsLeft_TheComboAndTheSelector()
    {
        var editor = new EditorSubsystem();
        try
        {
            editor.Initialize(new SubsystemConfig { Headless = true });
            var windows = new Fdp.Presentation.WindowManager.WindowManager(new IconAtlas(IntPtr.Zero, 512, 512));
            editor.RegisterWindows(windows);

            Assert.NotNull(editor.ReviewSourceForTest);
            Assert.Contains(ReviewSourceStatusSection.SectionId, windows.StatusBar.SectionIds);   // its own section …
            Assert.Equal(0, ReviewSourceStatusSection.SortOrder);                                 // … at the LEFT end (the seat's is 200, the map cursor's 50)
            Assert.True(windows.MainToolbar.ContainsEntry(ReviewSourceToolbarCombo.EntryId));
            var segment = editor.AarTransportForTest!;
            var selector = Assert.IsType<ReplayTransportSelector>(segment.Transport);
            Assert.IsType<OwnPartReplayTransport>(selector.Active);                               // no group open: today's own-part transport
            Assert.NotNull(segment.MergedDrawer);
            Assert.Null(editor.ReviewSourceComboForTest!.Label);                                  // no group: no combo
            Assert.Null(editor.ReviewStatusForTest!.Text);                                        // and nothing on the status bar
        }
        finally { editor.Shutdown(); }
    }

    [Fact]
    public void V6_TheSelector_DrivesTheSourceWhileAGroupIsOpen_AndTheOwnPartOtherwise()
    {
        FourParticipants();
        using var rig = new Rig(_root, _serializer);
        var own = new Mock<IReplayTransport>();
        own.SetupGet(t => t.TotalFrames).Returns(99);
        var selector = new ReplayTransportSelector(own.Object, rig.Source);
        Assert.Equal(99, selector.TotalFrames);                        // nothing open: the own part's
        rig.Open(_exercise);
        Assert.Equal(5, selector.TotalFrames);                         // a group open: the source's
        selector.Step(1);
        Assert.Equal(1, selector.CurrentFrame);
        own.Verify(t => t.Step(It.IsAny<int>()), Times.Never);
    }

    // ── the frame: the status bar and the toolbar in Merged ─────────────────────────────────

    [SkippableFact]
    public void V6_TheStatusBarAndToolbar_InMerged_Frame()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        FourParticipants();
        using var rig = new Rig(_root, _serializer);
        rig.Open(_exercise);
        var segment = new AarTransportSegment(new ReplayTransportSelector(new Mock<IReplayTransport>().Object, rig.Source), () => true, () => rig.Source.Kind == ReviewSourceKind.Merged);
        var combo = new ReviewSourceToolbarCombo(rig.Source);
        var status = new ReviewSourceStatusSection(rig.Source);
        using var frame = UiFrameHarness.Begin(1240, 140);
        for (int i = 0; i < 4; i++)
            frame.Step(() =>
            {
                var vp = ImGui.GetMainViewport();
                if (ImGui.BeginMainMenuBar())
                {
                    ImGui.TextUnformatted("File  Edit  View");
                    ImGui.SameLine(); segment.Draw();
                    ImGui.SameLine(); combo.Draw();
                    ImGui.EndMainMenuBar();
                }
                float h = ImGui.GetFrameHeight() + ImGui.GetStyle().WindowPadding.Y * 2f;
                ImGui.SetNextWindowPos(new Vector2(vp.WorkPos.X, vp.WorkPos.Y + vp.WorkSize.Y - h));
                ImGui.SetNextWindowSize(new Vector2(vp.WorkSize.X, h));
                ImGui.Begin("##status", ImGuiNET.ImGuiWindowFlags.NoDecoration | ImGuiNET.ImGuiWindowFlags.NoMove | ImGuiNET.ImGuiWindowFlags.NoSavedSettings);
                status.Render();
                ImGui.End();
            });

        Assert.Equal(ReplayTransportParts.History | ReplayTransportParts.StepBack | ReplayTransportParts.SeekBar | ReplayTransportParts.PlayPause, segment.MergedDrawer!.LastDrawn);   // ▶ drawn (greyed) beside the segment's items
        Assert.Equal("Merged", combo.Label);
        Assert.True(combo.Drawn);
        Assert.True(status.TextRect.Max.X > status.TextRect.Min.X, "the segment is drawn at the left");
        Assert.True(status.TextRect.Min.X < 40f, "…at the bar's LEFT end " + status.TextRect);

        var shot = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS");
        if (!string.IsNullOrEmpty(shot)) { Directory.CreateDirectory(shot); frame.Screenshot(Path.Combine(shot, "v6-merged-status-and-toolbar.png")); }
    }
}
