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
    private readonly Dictionary<int, int> _localBase = new();   // ⭐ S5 — node → the progress base of ONE local (no network id) entity that node's file also holds (0: none)

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
        if (_localBase.TryGetValue(node, out int localBase))
        {
            var local = repo.CreateEntity();
            repo.AddComponent(local, new ObjectiveStatus { ProgressCur = localBase, ProgressNeed = 100, State = ObjectiveState.Active });
            repo.SetAuthority<ObjectiveStatus>(local, true);
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

        public Rig(string root, ScenarioSerializer serializer, int thisNode = 1, Func<int, string>? names = null, bool noFacade = false, Func<ITimeTransportFacade?>? facade = null)
        {
            World.RegisterComponent<NetworkIdentity>(); World.RegisterComponent<NetworkAuthority>(); World.RegisterComponent<ObjectiveStatus>();
            Facade.SetupGet(f => f.IsPaused).Returns(true);
            Facade.SetupGet(f => f.TimeScale).Returns(1f);
            Facade.SetupGet(f => f.IsPlayPauseEnabled).Returns(true);
            var builder = new TransientMasterBuilder(serializer);
            Source = new ReviewSource(() => World, () => builder, () => { Clears++; World.SoftClear(); }, () => root, thisNode,
                ready: () => Ready, inReplay: () => InReplay, selection: () => Selected, select: n => { Selects.Add(n); return true; }, nodeName: names, facade: facade ?? (noFacade ? () => null : () => Facade.Object));   // noFacade: a steady 0 B measurement without Moq's per-call allocation (no facade = paused)
        }

        private ReviewUi? _ui;
        /// <summary>⭐ CE-4496 R-2 — the review UI over this source, as the editor composes it (switch off): an inline channel, the ONE Federation panel.</summary>
        public ReviewUi Ui => _ui ??= new ReviewUi(new InlineReviewChannel(Source, new ReviewModelBuilder(null)), () => Facade.Object);

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
        var panel = rig.Ui.Panel;

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

        var t = rig.Ui.Transport;
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

    /// <summary>
    /// ⭐ G-7 S4 rail (d) / M-7 — ▶ is ENABLED in Merged and the reason is gone; one played frame re-merges (the world follows the recording at the played frame), through the same path a step uses.
    /// 🔴 Mutation: <c>ReviewSource.Update</c> not calling <c>AdvanceMerged</c> ⇒ nothing plays ⇒ red; <c>CanPlay</c> back to <c>IsPart &amp;&amp; …</c> ⇒ red.
    /// </summary>
    [Fact]
    public void V6_Merged_PlayIsEnabled_AndAPlayedFrameReMerges()
    {
        FourParticipants();
        using var rig = new Rig(_root, _serializer);
        rig.Open(_exercise);
        var t = rig.Ui.Transport;
        Assert.Equal(ReviewSourceKind.Merged, rig.Source.Kind);
        Assert.True(t.CanPlay);                                        // ⭐ ▶ enabled in Merged …
        Assert.Null(t.PlayDisabledReason);                             // … and no reason to give
        Assert.False(t.IsPlaying);                                     // (the toolbar is paused)

        int merges = 0;
        rig.Source.Manager!.OnTimeChanged += () => merges++;
        rig.Source.Update(1.0);                                        // paused: nothing moves
        Assert.Equal(0, t.CurrentFrame); Assert.Equal(0, merges);

        rig.Facade.SetupGet(f => f.IsPaused).Returns(false);           // the toolbar's ▶ is pressed
        Assert.True(t.IsPlaying);
        rig.Source.Update(2.0);                                        // two seconds at 1× = two recorded frames
        Assert.Equal(2, t.CurrentFrame);
        Assert.Equal(2, merges);                                       // ⭐ every played frame merged (one step each)
        Assert.Equal(2, ProgressOfNet(rig.World, 100));                // … the world is the merge at frame 2
        Assert.Equal(12, ProgressOfNet(rig.World, 200));
        Assert.Equal(4, rig.World.EntityCount);

        rig.Source.Update(10.0);                                       // on to the end, and no further
        Assert.Equal(4, t.CurrentFrame);
        Assert.Equal(4, ProgressOfNet(rig.World, 100));
        Assert.Equal(14, ProgressOfNet(rig.World, 200));
    }

    [Fact]
    public void V6_Merged_Steps_AndScrubs()
    {
        FourParticipants();
        using var rig = new Rig(_root, _serializer);
        rig.Open(_exercise);
        var t = rig.Ui.Transport;

        Assert.True(t.HasRecording);
        Assert.Equal(5, t.TotalFrames);
        Assert.Equal(0, t.CurrentFrame);
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
        rig.Ui.Transport.Step(1);
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
        Assert.Equal("Merged · 4 participants (SimHost-2, SimHost-3, SimHost-4, SimHost-5) — step / scrub", rig.Source.StatusText());

        rig.Source.SelectPart(2);
        Assert.Equal("Part: SimHost-2", rig.Source.StatusText());
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
        Assert.Equal(new[] { PathOf(2) }, rig.Ui.RecordingFiles());   // Merged: the primary participant's (the lowest node)
        rig.Source.SelectPart(4);
        Assert.Equal(new[] { PathOf(4) }, rig.Ui.RecordingFiles());
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
        var selector = new ReplayTransportSelector(own.Object, rig.Ui);
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
        var segment = new AarTransportSegment(new ReplayTransportSelector(new Mock<IReplayTransport>().Object, rig.Ui), () => true, () => rig.Source.Kind == ReviewSourceKind.Merged);
        var combo = new ReviewSourceToolbarCombo(rig.Ui);
        var status = new ReviewSourceStatusSection(rig.Ui);
        using var frame = UiFrameHarness.Begin(1240, 140);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
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
        Assert.Equal("Merged · 4 participants", combo.Label);   // ⭐ mockup ③ — the face says the count
        Assert.True(combo.Drawn);
        Assert.True(status.TextRect.Max.X > status.TextRect.Min.X, "the segment is drawn at the left");
        Assert.True(status.TextRect.Min.X < 40f, "…at the bar's LEFT end " + status.TextRect);

        var shot = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS");
        if (!string.IsNullOrEmpty(shot)) { Directory.CreateDirectory(shot); frame.Screenshot(Path.Combine(shot, "v6-merged-status-and-toolbar.png")); }
    }

    // ── ⭐ CE-4498 R-4 — the review across threads (mode 3): commands are sim jobs, the model rides the pull ─────────

    /// <summary>A dedicated "sim thread": work runs on it, one item at a time, and the caller waits (as the staged editor's sim worker runs SimStep).</summary>
    private sealed class SimThread : IDisposable
    {
        private readonly System.Collections.Concurrent.BlockingCollection<Action> _work = new();
        private readonly System.Threading.Thread _thread;
        public int Id { get; private set; }

        public SimThread()
        {
            var started = new System.Threading.ManualResetEventSlim();
            _thread = new System.Threading.Thread(() => { Id = Environment.CurrentManagedThreadId; started.Set(); foreach (var a in _work.GetConsumingEnumerable()) a(); }) { IsBackground = true, Name = "review-sim" };
            _thread.Start();
            started.Wait();
        }

        public void Run(Action a)
        {
            using var done = new System.Threading.ManualResetEventSlim();
            Exception? error = null;
            _work.Add(() => { try { a(); } catch (Exception e) { error = e; } finally { done.Set(); } });
            done.Wait();
            if (error != null) throw new InvalidOperationException("sim step failed", error);
        }

        public void Dispose() { _work.CompleteAdding(); _thread.Join(5000); }
    }

    /// <summary>The source's commands, recording the thread each one ran on.</summary>
    private sealed class ThreadRecorder : IReviewCommands
    {
        private readonly ReviewSource _s;
        public readonly List<int> Threads = new();
        public ThreadRecorder(ReviewSource s) => _s = s;
        private void Note() { lock (Threads) Threads.Add(Environment.CurrentManagedThreadId); }
        public void SelectMerged() { Note(); _s.SelectMerged(); }
        public void SelectPart(int node) { Note(); _s.SelectPart(node); }
        public void SeekFrame(int frame) { Note(); _s.SeekFrame(frame); }
        public void StepFrame(int delta) { Note(); _s.StepFrame(delta); }
        public void SetNodeOffsetTicks(int node, long t) { Note(); _s.SetNodeOffsetTicks(node, t); }
        public void SetBaseWallTicks(long t) { Note(); _s.SetBaseWallTicks(t); }
        public void SetLocalEntitiesShown(int node, bool shown) { Note(); _s.SetLocalEntitiesShown(node, shown); }
        public void SetLocalEntitiesProvider(int node) { Note(); _s.SetLocalEntitiesProvider(node); }
    }

    /// <summary>The threaded review as the staged editor composes it: the channel posts to a queue the sim step drains; the sim step updates the source and publishes a NEW model only; the pull applies it.</summary>
    private sealed class ThreadedRig : IDisposable
    {
        public readonly SimThread Sim = new();
        public readonly System.Collections.Concurrent.ConcurrentQueue<Action> Jobs = new();
        public readonly ReviewModelPublisher Publisher;
        public readonly ThreadedReviewChannel Ui;
        public readonly ThreadRecorder Commands;
        private readonly ReviewSource _source;
        private ReviewReadModel? _published;
        public int Publishes;

        public ThreadedRig(ReviewSource source, ReplicaViews? views)
        {
            _source = source;
            Commands = new ThreadRecorder(source);
            Publisher = new ReviewModelPublisher(new ReviewModelBuilder(views));
            Ui = new ThreadedReviewChannel(Commands, Publisher, Jobs.Enqueue);
        }

        /// <summary>One sim step: drain the jobs, the source's Update (the node frame's call), publish when changed.</summary>
        public void Step(double dt = 0) => Sim.Run(() =>
        {
            while (Jobs.TryDequeue(out var job)) job();
            _source.Update(dt);
            if (Publisher.PublishIfChanged(_source) is { } m) { _published = m; Publishes++; }
        });

        /// <summary>The UI frame's start: the newest published model, applied on the UI thread.</summary>
        public void Pull() { if (_published is { } m) { Ui.Apply(m); _published = null; } }

        public void Dispose() => Sim.Dispose();
    }

    /// <summary>
    /// ⭐ CE-4498 R-4 (docs/DESIGN_Threaded_Review_Source.md §1.2) — mode 3's review channel: a UI command is NOT applied on the UI thread — it runs on the sim thread at the next step; the UI sees
    /// its effect only after the pull that follows (the model is built sim-side, carried, applied); a By-node ask rides up once and its view comes back; a steady step publishes nothing (0 B).
    /// 🔴 Red-proof: <c>ThreadedReviewChannel.StepFrame</c> calling the source directly ⇒ the thread assert names the UI thread ⇒ red.
    /// </summary>
    [Fact]
    public void R4_TheThreadedChannel_RunsCommandsOnTheSimThread_AndTheModelReachesTheUiOnlyAfterThePull()
    {
        FourParticipants();
        using var rig = new Rig(_root, _serializer, noFacade: true);
        using var t = new ThreadedRig(rig.Source, new ReplicaViews(new OwnershipSource(), _serializer));
        int uiThread = Environment.CurrentManagedThreadId;

        t.Sim.Run(() => Assert.True(rig.Source.OnRecordingOpened(_exercise)));   // the recording-opened branch: sim-side (D-7)
        t.Step();
        Assert.False(t.Ui.Model.IsOpen);                                          // published, not yet pulled
        t.Pull();
        Assert.True(t.Ui.Model.IsMerged);
        Assert.Equal(0, t.Ui.Model.CurrentFrame);
        Assert.Equal(4, t.Ui.Model.Participants.Count);

        t.Ui.StepFrame(+1);                                                       // ▶| on the UI
        Assert.Single(t.Jobs);                                                    // a job, not a call
        Assert.Empty(t.Commands.Threads);
        Assert.Equal(0, t.Ui.Model.CurrentFrame);
        t.Step();
        Assert.Equal(new[] { t.Sim.Id }, t.Commands.Threads.ToArray());           // ⭐ applied on the SIM thread
        Assert.NotEqual(uiThread, t.Sim.Id);
        Assert.Equal(0, t.Ui.Model.CurrentFrame);                                 // not before the pull …
        t.Pull();
        Assert.Equal(1, t.Ui.Model.CurrentFrame);                                 // … and after it

        t.Ui.SelectPart(3); t.Step(); t.Pull();
        Assert.Equal(ReviewKind.Part, t.Ui.Model.Kind);
        Assert.Equal(3, t.Ui.Model.Facts.PartNode);
        t.Ui.SelectMerged(); t.Ui.SetNodeOffsetTicks(2, 1_000 * TimeSpan.TicksPerMillisecond); t.Step(); t.Pull();
        Assert.True(t.Ui.Model.IsMerged);
        Assert.Equal(1000d, t.Ui.Model.Participant(2)!.OffsetMs, 3);

        t.Ui.Requests.AskByNode(100, typeof(ObjectiveStatus));
        t.Ui.EndFrame();                                                          // the ask rides up once …
        t.Ui.Requests.AskByNode(100, typeof(ObjectiveStatus));
        t.Ui.EndFrame();                                                          // … and not again
        Assert.Single(t.Jobs);
        t.Step(); t.Pull();
        Assert.True(t.Ui.Model.TryByNode(100, typeof(ObjectiveStatus), out _));   // the view came back in the model

        int publishes = t.Publishes;
        long simBytes = -1;
        t.Step(); t.Step();                                                       // warm
        t.Sim.Run(() =>
        {
            long b0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++) { rig.Source.Update(0); t.Publisher.PublishIfChanged(rig.Source); }
            simBytes = GC.GetAllocatedBytesForCurrentThread() - b0;
        });
        Assert.Equal(publishes, t.Publishes);                                     // a steady paused step publishes nothing …
        Assert.Equal(0, simBytes);                                                // … and allocates nothing
    }

    /// <summary>
    /// ⭐ CE-4498 R-4 — the STRICT world guard (mode 3's: any read of S off the sim thread is a race) stays silent while the UI drives the review through the threaded channel and draws its
    /// Source menu from the model. Needs <c>HROT_WORLD_GUARD=1</c> in the test process (the guard is read once); run as <c>HROT_WORLD_GUARD=1 dotnet test … --filter R4_</c>.
    /// </summary>
    [SkippableFact]
    public void R4_TheStrictWorldGuard_StaysSilent_WhileTheUiDrivesTheThreadedReview()
    {
        Skip.IfNot(Fdp.Core.WorldGuard.On, "run with HROT_WORLD_GUARD=1 (the guard is read once per process)");
        FourParticipants();
        using var rig = new Rig(_root, _serializer);
        using var t = new ThreadedRig(rig.Source, new ReplicaViews(new OwnershipSource(), _serializer));
        using var presentation = new EntityRepository();
        int before = Fdp.Core.WorldGuard.DistinctHits;
        Fdp.Core.WorldGuard.Arm(rig.World, presentation, t.Sim.Id);
        bool wasStrict = Fdp.Core.WorldGuard.Strict;
        Fdp.Core.WorldGuard.Strict = true;
        try
        {
            t.Sim.Run(() => rig.Source.OnRecordingOpened(_exercise));
            t.Step(); t.Pull();
            var ui = new ReviewUi(t.Ui);
            for (int i = 0; i < 3; i++)
            {
                t.Ui.StepFrame(+1); t.Ui.SetLocalEntitiesShown(4, i % 2 == 0);
                t.Ui.Requests.AskByNode(100, typeof(ObjectiveStatus)); t.Ui.EndFrame();
                t.Step(); t.Pull();
                _ = ui.Transport.CurrentFrame; _ = ui.RecordingFiles(); _ = ui.Model.Facts.StatusText; _ = ui.Panel.BuildViewModel("p", "k");
            }
            t.Ui.SelectPart(3); t.Step(); t.Pull(); t.Ui.SelectMerged(); t.Step(); t.Pull();
            Assert.True(t.Ui.Model.IsMerged);
        }
        finally { Fdp.Core.WorldGuard.Disarm(); Fdp.Core.WorldGuard.Strict = wasStrict; }
        Assert.Equal(before, Fdp.Core.WorldGuard.DistinctHits);   // ⭐ not one read of S off the sim thread
    }

    /// <summary>
    /// ⭐⭐ CE-4499 R-5 (docs/DESIGN_Threaded_Review_Source.md §3 ① ⑤) — mode 3 END TO END on the real machinery: the review source on S on a dedicated sim thread, the REAL job queue
    /// (<c>SimJobQueue</c>) carrying the UI's commands, the REAL staging (<c>StagingWorlds</c>) publishing S and carrying the model, A pulled on the UI thread — as <c>EditorSubsystem.SimStep</c> /
    /// <c>PullStaging</c> do. Every action of the acceptance, each checked on A (what the map draws) AND on the model (what the panels draw): Merged · step · scrub · ▶ / rate · the Source switch to a
    /// part and back · an offset · a locals toggle · By node · a watch item · a search hit's seek. ⭐ A still holds the merged entities after N pulls (the pull does not erase the merge).
    /// 0 B per steady paused frame, on BOTH threads. With <c>HROT_WORLD_GUARD=1</c> the strict guard is armed too and must stay silent.
    /// 🔴 Red-proofs (reasoned): the source merging into A (R-3 undone) ⇒ the next pull overwrites A with an S that holds no merge ⇒ the "after N pulls" assert red; a command applied on the UI
    /// thread ⇒ the strict guard's hit count moves.
    /// </summary>
    [Fact(Timeout = 120_000)]
    public void R5_Mode3_TheReviewOverTheRealStagingAndJobQueue_EveryActionReachesA_AndTheMergeSurvivesThePulls()
    {
        _localBase[4] = 904;
        FourParticipants();
        ITimeTransportFacade? facade = null;   // paused (no ▶) until the play step; null also keeps the steady measurement free of Moq's per-call bytes
        using var rig = new Rig(_root, _serializer, facade: () => facade);
        using var a = new EntityRepository();
        a.RegisterComponent<NetworkIdentity>(); a.RegisterComponent<NetworkAuthority>(); a.RegisterComponent<ObjectiveStatus>();
        using var staging = new Fdp.Core.Threading.StagingWorlds();
        staging.TableMask.SetBit(Fdp.Core.ComponentType<NetworkIdentity>.ID);
        staging.TableMask.SetBit(Fdp.Core.ComponentType<NetworkAuthority>.ID);
        staging.TableMask.SetBit(Fdp.Core.ComponentType<ObjectiveStatus>.ID);
        staging.RegisterStreamsFrom(rig.World.Bus);
        var jobs = new Fdp.Core.Threading.SimJobQueue { Outbox = staging };
        var publisher = new ReviewModelPublisher(new ReviewModelBuilder(new ReplicaViews(new OwnershipSource(), _serializer)));
        var channel = new ThreadedReviewChannel(rig.Source, publisher, act => jobs.Post(_ => act(), "review command"));
        var ui = new ReviewUi(channel, () => facade);
        using var sim = new SimThread();
        long tick = 0;
        ReviewReadModel? toApply = null;
        Action<EntityRepository> apply = _ => { if (toApply is { } m) channel.Apply(m); };
        void SimStep(double dt) => sim.Run(() =>
        {
            rig.World.Tick();                                                                // the kernel's tick of S (ModuleHostKernel.cs:596) — every node frame stamps a new version, so the staging copies what changed
            jobs.Drain(rig.World);
            rig.Source.Update(dt);                                                           // the node frame's ONE call site (R-3)
            if (publisher.PublishIfChanged(rig.Source) is { } m) { toApply = m; staging.Post(apply); }   // PublishReviewModel (R-4)
            staging.Publish(rig.World, ++tick);
        });
        void Frame(double dt = 0) { SimStep(dt); staging.Pull(a); channel.EndFrame(); }     // the UI frame: pull, then the panels' asks ride up

        bool guard = Fdp.Core.WorldGuard.On;
        int hitsBefore = guard ? Fdp.Core.WorldGuard.DistinctHits : 0;
        bool wasStrict = Fdp.Core.WorldGuard.Strict;
        if (guard) { Fdp.Core.WorldGuard.Arm(rig.World, a, sim.Id); Fdp.Core.WorldGuard.Strict = true; }
        try
        {
            // ① Merged — opened sim-side (the recording-opened branch runs with the editor logic, D-7)
            sim.Run(() => Assert.True(rig.Source.OnRecordingOpened(_exercise)));
            Frame();
            Assert.True(ui.Model.IsMerged);
            Assert.Equal(0, ProgressOfNet(a, 100)); Assert.Equal(10, ProgressOfNet(a, 200));
            Assert.Equal(int.MinValue, ProgressOfNet(a, 300)); Assert.Equal(int.MinValue, ProgressOfNet(a, 400));
            for (int i = 0; i < 10; i++) Frame();                                            // ⭐ N pulls later the merge is still in A
            Assert.Equal(0, ProgressOfNet(a, 100)); Assert.Equal(10, ProgressOfNet(a, 200)); Assert.Equal(int.MinValue, ProgressOfNet(a, 400));

            // ② step ③ scrub (a seek)
            ui.Transport.Step(+1); Frame();
            Assert.True(ui.Model.CurrentFrame == 1, $"frame {ui.Model.CurrentFrame}; jobs posted {jobs.Posted} ran {jobs.Executed} faults {jobs.Faults}: {jobs.LastFault}");
            Assert.Equal(1, ProgressOfNet(a, 100)); Assert.Equal(11, ProgressOfNet(a, 200));
            ui.Transport.Seek(3); Frame();
            Assert.Equal(3, ui.Model.CurrentFrame); Assert.Equal(3, ProgressOfNet(a, 100));
            ui.Transport.Seek(0); Frame();

            // ④ ▶ / rate — the toolbar's ▶ at 2×: one wall second plays two recorded seconds
            facade = rig.Facade.Object;
            rig.Facade.SetupGet(f => f.IsPaused).Returns(false); rig.Facade.SetupGet(f => f.TimeScale).Returns(2f);
            Frame(0); Frame(1.0); Frame(0);
            Assert.True(ui.Transport.IsPlaying);
            Assert.Equal(2, ui.Model.CurrentFrame); Assert.Equal(2, ProgressOfNet(a, 100));
            rig.Facade.SetupGet(f => f.IsPaused).Returns(true); rig.Facade.SetupGet(f => f.TimeScale).Returns(1f);
            Frame();
            facade = null;

            // ⑤ the Source switch to a part (CGF-B) and back
            ui.Channel.SelectPart(3); Frame();
            Assert.Equal(ReviewKind.Part, ui.Model.Kind); Assert.Equal(3, ui.Model.Facts.PartNode);
            Assert.Equal(int.MaxValue, ProgressOfNet(a, 100));                               // A shows CGF-B's file only …
            Assert.Equal(12, ProgressOfNet(a, 200));                                         // … at the same moment (frame 2)
            ui.Channel.SelectMerged(); Frame();
            Assert.True(ui.Model.IsMerged);
            Assert.Equal(2, ProgressOfNet(a, 100)); Assert.Equal(int.MinValue, ProgressOfNet(a, 400));
            ui.Transport.Seek(0); Frame();

            // ⑥ an offset (the Federation slider): CGF-A at +2 s ⇒ its unit shows two seconds on
            ui.Panel.SetNodeOffsetMs(2, 2000d); Frame();
            Assert.Equal(2000d, ui.Model.Participant(2)!.OffsetMs, 3);
            Assert.Equal(2, ProgressOfNet(a, 100)); Assert.Equal(10, ProgressOfNet(a, 200));
            ui.Panel.SetNodeOffsetMs(2, 0d); Frame();
            Assert.Equal(0, ProgressOfNet(a, 100));

            // ⑦ a locals toggle: SimHost-4's local entity, tagged by the model's local origins, leaves A and comes back
            Assert.Equal(new[] { 904 }, LocalsInWorld(ui, a).Select(l => l.Progress).ToArray());
            ui.Panel.SetLocalEntitiesShown(4, false); Frame();
            Assert.Empty(LocalsInWorld(ui, a));
            ui.Panel.SetLocalEntitiesShown(4, true); Frame();
            Assert.Equal(new[] { 904 }, LocalsInWorld(ui, a).Select(l => l.Progress).ToArray());

            // ⑧ By node ⑨ a watch item — asked by the UI, computed sim-side, in the next model
            var item = new ReplicaWatchItem("ObjectiveStatus.ProgressCur", 0, true, 0.5, 0u, "Progress");
            channel.Requests.AskByNode(100, typeof(NetworkIdentity)); channel.Requests.AskWatch(100, item);
            Frame(); Frame();
            Assert.True(ui.Model.TryByNode(100, typeof(NetworkIdentity), out var byNode));
            Assert.NotNull(byNode);
            Assert.Equal(new[] { 2, 4 }, byNode!.Rows.Select(r => r.NodeId).ToArray());       // the owner (CGF-A) and the SimHost's ghost
            Assert.True(ui.Model.TryWatch(100, item, out _));

            // ⑩ a search hit's seek — the file review's Seek is the transport's (EditorSubsystem's ReviewFileServices)
            Action<long> searchSeek = frame => ui.Transport.Seek((int)frame);
            Assert.Equal(3, ui.Model.PrimaryNode);                                           // the last part shown stays the primary (SelectPart sets it; Merged keeps it — as with the switch off)
            Assert.Equal(new[] { PathOf(3) }, ui.RecordingFiles());                          // the search reads the primary's file
            searchSeek(4); Frame();
            Assert.Equal(4, ui.Model.CurrentFrame); Assert.Equal(4, ProgressOfNet(a, 100));

            // ⑪ 0 B per steady paused frame — the sim step (jobs, source, publish) and the UI frame (pull, asks, the panels' reads)
            for (int i = 0; i < 5; i++) { channel.Requests.AskByNode(100, typeof(NetworkIdentity)); channel.Requests.AskWatch(100, item); Frame(); }
            long simBytes = 0, uiBytes = 0, pullBytes = 0, askBytes = 0, endBytes = 0, readBytes = 0;
            for (int i = 0; i < 20; i++)
            {
                sim.Run(() =>
                {
                    long b0 = GC.GetAllocatedBytesForCurrentThread();
                    rig.World.Tick(); jobs.Drain(rig.World); rig.Source.Update(0);
                    if (publisher.PublishIfChanged(rig.Source) is { } m) { toApply = m; staging.Post(apply); }
                    staging.Publish(rig.World, ++tick);
                    simBytes += GC.GetAllocatedBytesForCurrentThread() - b0;
                });
                long u0 = GC.GetAllocatedBytesForCurrentThread();                            // the UI frame's own work (the rig's SimThread.Run hand-off allocates on this thread, not the editor)
                staging.Pull(a);
                long u1 = GC.GetAllocatedBytesForCurrentThread(); pullBytes += u1 - u0;
                channel.Requests.AskByNode(100, typeof(NetworkIdentity)); channel.Requests.AskWatch(100, item);
                long u2 = GC.GetAllocatedBytesForCurrentThread(); askBytes += u2 - u1;
                channel.EndFrame();
                long u3 = GC.GetAllocatedBytesForCurrentThread(); endBytes += u3 - u2;
                _ = ui.Model.Facts.StatusText; _ = ui.Transport.CurrentFrame; _ = ui.RecordingFiles(); _ = ui.Panel.HasNonZeroOffset;
                _ = ui.Model.TryByNode(100, typeof(NetworkIdentity), out _);
                readBytes += GC.GetAllocatedBytesForCurrentThread() - u3;
                uiBytes += GC.GetAllocatedBytesForCurrentThread() - u0;
            }
            Assert.Equal(0, simBytes);
            Assert.True(uiBytes == 0, $"UI frame bytes over 20: pull {pullBytes} · asks {askBytes} · end {endBytes} · reads {readBytes}");   // 🔴 measured red before BUG-3: end 640 (32 B per frame)
            Assert.Equal(0, jobs.Faults);
        }
        finally { if (guard) { Fdp.Core.WorldGuard.Disarm(); Fdp.Core.WorldGuard.Strict = wasStrict; } }
        if (guard) Assert.Equal(hitsBefore, Fdp.Core.WorldGuard.DistinctHits);               // ⭐ strict: not one read of S off the sim thread
    }

    // ── S5 · I7 — local entities from EVERY node, tagged, the Source menu's checkboxes (mockup ③) ─────────

    private static List<(string Note, int Progress)> LocalsInWorld(ReviewUi ui, EntityRepository world)
    {
        var found = new List<(string, int)>();
        for (int i = 0; i <= world.MaxEntityIndex; i++)
        {
            var e = new Entity(i, world.GetMetadata(i).Generation);
            if (world.IsAlive(e) && ui.LocalOriginNote(e) is { } note) found.Add((note, world.GetComponent<ObjectiveStatus>(e).ProgressCur));
        }
        found.Sort();
        return found;
    }

    /// <summary>
    /// ⭐ G-7 S5 (I7, M-6) — the editor's Merged world holds a local (no network id) entity from EACH node that has one, tagged <i>"local to &lt;node&gt;"</i> (Details reads the tag); unchecking a node in the Source menu's
    /// <i>Local entities shown from</i> (the SAME Federation panel's checkbox write) removes ITS locals from the editor's world at once and leaves the others. 🔴 Red-proof: the merger taking the single "provider" node's locals ⇒ red.
    /// </summary>
    [Fact]
    public void G7S5_LocalEntitiesFromEveryNode_AreTaggedByNode_AndTheSourceMenuCheckboxRemovesANodesLocals()
    {
        _localBase[2] = 902; _localBase[4] = 904;
        FourParticipants();
        using var rig = new Rig(_root, _serializer);
        rig.Open(_exercise);

        Assert.Equal(new[] { ("local to SimHost-2", 902), ("local to SimHost-4", 904) }, LocalsInWorld(rig.Ui, rig.World));   // ⭐ one from each node, each tagged with its own

        rig.Ui.Panel.SetLocalEntitiesShown(4, false);                                                            // the checkbox
        Assert.Equal(new[] { ("local to SimHost-2", 902) }, LocalsInWorld(rig.Ui, rig.World));
        Assert.NotEqual(int.MaxValue, ProgressOfNet(rig.World, 300));                                                      // its networked unit stays

        rig.Ui.Panel.SetLocalEntitiesShown(4, true);
        Assert.Equal(2, LocalsInWorld(rig.Ui, rig.World).Count);
        Assert.Null(rig.Ui.LocalOriginNote(Entity.Null));
    }

    [SkippableFact]
    public void G7S5_TheSourceMenu_LocalEntitiesCheckboxes_AreOnePerNode_AlignedAndDefaultOn_Frame()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        FourParticipants();
        using var rig = new Rig(_root, _serializer, names: n => n switch { 2 => "CGF-1", 3 => "CGF-2", 4 => "SimHost-1", _ => "Player-1" });
        rig.Open(_exercise);
        var panel = rig.Ui.Panel;
        panel.CaptureLayout = true;
        using var frame = UiFrameHarness.Begin(420, 400);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        void Draw() => frame.StepN(4, () =>
        {
            ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new Vector2(420, 400));
            ImGui.Begin("Source", ImGuiNET.ImGuiWindowFlags.NoCollapse);
            ReviewSourceSwitch.Draw(rig.Ui);
            ImGui.End();
        });
        Draw();

        Assert.Equal(ReviewKind.Merged, rig.Ui.Model.Kind);                                     // the group opens Merged: the switch shows it (CE-4496: the model says so)
        Assert.Equal(new[] { 2, 3, 4, 5 }, panel.LocalsRows.Select(r => r.Node).ToArray());    // ⭐ one checkbox per participant …
        Assert.All(panel.LocalsRows, r => Assert.True(r.On));                                  // … every node's on by default
        Assert.Single(panel.LocalsRows.Select(r => r.X).Distinct());                           // aligned: ONE x (the mockup's column of boxes)
        var ys = panel.LocalsRows.Select(r => r.Y).ToArray();
        Assert.True(ys.Zip(ys.Skip(1), (a, b) => b - a).All(d => d > 10f && Math.Abs(d - (ys[1] - ys[0])) < 1f), "an even row pitch: " + string.Join(",", ys));
        string dir = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } sd ? sd : "";
        if (dir != "") { Directory.CreateDirectory(dir); frame.Screenshot(Path.Combine(dir, "g7s5-source-menu.png")); }

        panel.SetLocalEntitiesShown(4, false); panel.SetLocalEntitiesShown(5, false);
        Draw();
        Assert.Equal(new[] { true, true, false, false }, panel.LocalsRows.Select(r => r.On).ToArray());
        if (dir != "") frame.Screenshot(Path.Combine(dir, "g7s5-source-menu-unchecked.png"));
    }

    /// <summary>
    /// ⭐ G-7 S5 follow-up (mockup ③ of <c>docs/UX/better-merge-visual.svg</c>, user: <i>"rebuild the popup to match mockup"</i>) — the Source menu, measured on a REAL frame: the header, <i>Merged — every part</i> then one
    /// <i>"&lt;name&gt;'s part"</i> per participant with its ROLE icon (CGF robot blue · SimHost cube amber) in a cell at ONE x before the text; the <i>Local entities shown from</i> header and one checkbox per node; <i>Offsets…</i> last.
    /// ⛔ NO Single-Node / Merged radio anywhere in it; clicking <i>Offsets…</i> asks the host to open the Federation window, whose own panel draws no radio either. The icon cells hold their colour on the screen.
    /// 🔴 Red-proofs (compile): <c>ReviewSourceSwitch.Draw</c> calling <c>panel.DrawContent()</c> again (the radio back) ⇒ the radio assertions red; a row's icon drawn in the text colour ⇒ the pixel rail red.
    /// Render (never in git): <c>HROT_RAIL_SHOTS=&lt;dir&gt;</c> ⇒ <c>popup-source-menu.png</c>.
    /// </summary>
    [SkippableFact]
    public void G7S5_TheSourceMenu_IsMockup3_RolesIconsAtOneX_NoRadio_OffsetsOpensTheFederationWindow_Frame()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        FourParticipants();
        using var rig = new Rig(_root, _serializer, names: n => n switch { 2 => "CGF-1", 3 => "CGF-2", 4 => "SimHost-1", _ => "Player-1" });
        rig.Open(_exercise);
        var panel = rig.Ui.Panel;
        panel.CaptureLayout = true;
        int offsetsAsked = 0; rig.Ui.OffsetsRequested += () => offsetsAsked++;
        var registered = new List<Fdp.Presentation.WindowManager.ManagedWindow>();
        var host = new ReviewFederationWindowHost(rig.Ui, registered.Add, "Review", Vector4.One);
        Assert.Empty(registered);    // ⭐ CE-4496 R-2 — the window over the review's ONE panel is registered on the first Offsets…, once
        ReviewSourceSwitch.CaptureLayout = true;
        try
        {
            long drawBytes = -1;
            using var frame = UiFrameHarness.Begin(460, 330);
            new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
            frame.StepN(4, () =>
            {
                ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new Vector2(460, 330));
                ImGui.Begin("Source", ImGuiNET.ImGuiWindowFlags.NoDecoration | ImGuiNET.ImGuiWindowFlags.NoMove | ImGuiNET.ImGuiWindowFlags.NoSavedSettings);
                long b0 = GC.GetAllocatedBytesForCurrentThread();
                ReviewSourceSwitch.Draw(rig.Ui);
                drawBytes = GC.GetAllocatedBytesForCurrentThread() - b0;
                ImGui.End();
            });
            Assert.Equal(0, drawBytes);   // ⭐ HOT PATH — the open popup allocates nothing in the steady state

            Assert.Equal(new[]
            {
                ReviewSourceSwitch.Header, ReviewSourceSwitch.MergedRow, "CGF-1's part", "CGF-2's part", "SimHost-1's part", "Player-1's part",
                Fdp.Presentation.Panels.ReplayBrowser.FederationPanel.LocalsHeader, ReviewSourceSwitch.OffsetsRow,
            }, ReviewSourceSwitch.DrawnTexts.ToArray());                                                       // the mockup's order; no "Single-Node" / "Merged View" row
            Assert.Equal(5, ReviewSourceSwitch.Rows.Count);
            Assert.Single(ReviewSourceSwitch.Rows.Select(r => r.IconX).Distinct());                           // ONE x for every icon cell
            Assert.Single(ReviewSourceSwitch.Rows.Select(r => r.TextX).Distinct());                           // … and one x for every row's text, after the cell
            Assert.True(ReviewSourceSwitch.Rows[0].TextX - ReviewSourceSwitch.Rows[0].IconX > 12f, "the icon cell precedes the text");
            Assert.False(panel.ModeRadioDrawn);                                                                // ⛔ the radio is not in the popup (one control per choice)
            Assert.Equal(new[] { 2, 3, 4, 5 }, panel.LocalsRows.Select(r => r.Node).ToArray());              // the shared checkbox component, one per node
            Assert.Equal("node/cgf", rig.Source.RoleKeyOf(2)); Assert.Equal("node/simhost", rig.Source.RoleKeyOf(4));
            Assert.Equal(NodeEditor.Core.Widgets.IconPalette.Blue, NodeEditor.Core.Widgets.Icons.Get("node/cgf").Color);       // the mockup: CGF blue …
            Assert.Equal(NodeEditor.Core.Widgets.IconPalette.Amber, NodeEditor.Core.Widgets.Icons.Get("node/simhost").Color);  // … SimHost amber
            Assert.Equal("\uf544", NodeEditor.Core.Widgets.Icons.Get("node/cgf").Glyph); Assert.Equal("\uf1b2", NodeEditor.Core.Widgets.Icons.Get("node/simhost").Glyph);

            // the colours are on the SCREEN: the merged row's cell holds teal pixels, the CGF row's blue, the SimHost row's amber
            if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) { Directory.CreateDirectory(dir); frame.Screenshot(Path.Combine(dir, "popup-source-menu.png")); }
            var shot = Raylib_cs.Raylib.LoadImageFromScreen();
            try
            {
                int Px(int row, System.Numerics.Vector4 want)
                {
                    var r = ReviewSourceSwitch.Rows[row]; int n = 0;
                    for (int x = (int)r.IconX; x < (int)r.TextX; x++)
                        for (int y = (int)r.Y; y < (int)r.Y + 16; y++)
                        {
                            var c = Raylib_cs.Raylib.GetImageColor(shot, x, y);
                            if (Math.Abs(c.R - want.X * 255f) <= 40 && Math.Abs(c.G - want.Y * 255f) <= 40 && Math.Abs(c.B - want.Z * 255f) <= 40) n++;
                        }
                    return n;
                }
                Assert.True(Px(0, NodeEditor.Core.Widgets.IconPalette.Teal) >= 8, "Merged's sitemap is teal");
                Assert.True(Px(1, NodeEditor.Core.Widgets.IconPalette.Blue) >= 8, "CGF-1's robot is blue");
                Assert.True(Px(3, NodeEditor.Core.Widgets.IconPalette.Amber) >= 8, "SimHost-1's cube is amber");
            }
            finally { Raylib_cs.Raylib.UnloadImage(shot); }
        }
        finally { ReviewSourceSwitch.CaptureLayout = false; }

        // Offsets… asks the host; the host's window hosts the panel with NO radio and NO checkboxes (they are in the menu)
        Assert.Null(host.Window);                   // not there until asked for
        rig.Ui.RequestOffsets();
        Assert.Equal(1, offsetsAsked);
        Assert.Single(registered);
        Assert.True(host.Window!.IsOpen);           // Offsets… opened the Federation window
        rig.Ui.RequestOffsets();
        Assert.Single(registered);                  // … registered once
        Assert.False(panel.ShowModeRadio); Assert.False(panel.ShowLocals);
    }

    /// <summary>The face says what the world is, cached (0 B per drawn frame): <i>Merged · 4 participants</i> → <i>CGF-1's part</i>.</summary>
    [Fact]
    public void G7S5_TheSourceFace_SaysMergedWithTheCount_OrThePart()
    {
        FourParticipants();
        using var rig = new Rig(_root, _serializer, names: n => n == 2 ? "CGF-1" : "Node " + n);
        Assert.Null(rig.Source.FaceLabel);
        rig.Open(_exercise);
        Assert.Equal("Merged · 4 participants", rig.Source.FaceLabel); Assert.Equal("review/merged", rig.Source.FaceIconKey);
        rig.Source.SelectPart(2);
        Assert.Equal("CGF-1's part", rig.Source.FaceLabel); Assert.Equal("node/cgf", rig.Source.FaceIconKey);
        Assert.Same(rig.Source.FaceLabel, rig.Source.FaceLabel);   // cached: the same instance every frame
        Assert.Same(rig.Source.PartLabel(3), rig.Source.PartLabel(3));
    }

    // ── S5 · I5 — Details ▸ a component ▸ By node (mockup ①) ─────────────────────────────────────────────

    /// <summary>Unit 100 is owned by CGF-A (node 2: objective progress 0 + frame); node 3 holds a replica that agrees, node 4 one that believes 7 more.</summary>
    private void ReplicaGroup()
    {
        Record(2, (100, true, true, 0));
        Record(3, (100, false, true, 0));
        Record(4, (100, false, true, 7));
    }

    private static Entity UnitOf(EntityRepository world, int net)
    {
        for (int i = 0; i <= world.MaxEntityIndex; i++)
        {
            var e = new Entity(i, world.GetMetadata(i).Generation);
            if (world.IsAlive(e) && world.HasComponent<NetworkIdentity>(e) && world.GetComponent<NetworkIdentity>(e).Value == net) return e;
        }
        return Entity.Null;
    }

    /// <summary>⭐ CE-4496 R-2 — the section over the review's channel (inline, with the replica lens), as the editor composes it.</summary>
    private (ByNodeSection Section, InlineReviewChannel Channel) ByNode(Rig rig)
    {
        var channel = new InlineReviewChannel(rig.Source, new ReviewModelBuilder(new ReplicaViews(new OwnershipSource(), _serializer)));
        return (new ByNodeSection(channel, e => rig.World.IsAlive(e) && rig.World.HasComponent<NetworkIdentity>(e) ? rig.World.GetComponent<NetworkIdentity>(e).Value : 0, rig.Source.NameOf), channel);
    }

    /// <summary>A view as a drawn frame gets it: asked this frame, in the model after the frame's end (the request rides up only when it changes).</summary>
    private static ReplicaView? ViewNow(ByNodeSection section, InlineReviewChannel channel, Entity e, Type t)
    {
        section.ViewOf(e, t);
        channel.EndFrame();
        return section.ViewOf(e, t);
    }

    /// <summary>
    /// ⭐ G-7 S5 (I5, M-5) — Details ▸ a component ▸ <i>By node</i>: the OWNER (CGF-A, from the recorded ownership) first, then each replica; the replica that believes 7 more is marked DIFFERING (amber, Δ 7), the one that
    /// agrees is not; shown only in the MERGED view (a part has no By node). 🔴 Red-proof: <c>ReplicaViews.Compute</c> never marking a difference ⇒ red.
    /// </summary>
    [Fact]
    public void G7S5_ByNode_TheOwnerFirst_TheReplicaThatDiffersIsMarked_OnlyInMerged()
    {
        ReplicaGroup();
        using var rig = new Rig(_root, _serializer);
        rig.Open(_exercise);
        var (section, channel) = ByNode(rig);
        var unit = UnitOf(rig.World, 100);

        var view = ViewNow(section, channel, unit, typeof(ObjectiveStatus))!;

        Assert.Equal(2, view.OwnerNode);
        Assert.Equal(new[] { 2, 3, 4 }, view.Rows.Select(r => r.NodeId).ToArray());
        Assert.Equal(new[] { true, false, false }, view.Rows.Select(r => r.IsOwner).ToArray());
        Assert.Equal(new[] { false, false, true }, view.Rows.Select(r => r.Differs).ToArray());        // ⭐ only node 4's copy differs from the owner's
        Assert.Equal(7d, view.Rows[2].Delta, 3);
        Assert.Null(ViewNow(section, channel, Entity.Null, typeof(ObjectiveStatus)));

        rig.Source.SelectPart(3);
        Assert.Null(ViewNow(section, channel, unit, typeof(ObjectiveStatus)));                                    // a part: no By node
    }

    [SkippableFact]
    public void G7S5_ByNode_OnARealDetailsFrame_TheCrownedOwnerFirst_ColumnsAligned_AmberWhereItDiffers()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        ReplicaGroup();
        using var rig = new Rig(_root, _serializer, names: n => n switch { 2 => "CGF-1 (Brain)", 3 => "SimHost-1 (Muscle)", _ => "IG-1 (Map2D)" });
        rig.Open(_exercise);
        var (section, channel) = ByNode(rig);
        section.Capture = true;
        var panel = new Fdp.Presentation.Panels.EntityInspectorPanel();
        panel.Reflector.ComponentFooter = section.Draw;
        var session = new Fdp.Presentation.Adapters.RepositoryAdapter(rig.World);
        var unit = UnitOf(rig.World, 100);
        using var frame = UiFrameHarness.Begin(640, 760);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        panel.Reflector.ForceExpandAll = true;
        frame.StepN(5, () =>
        {
            ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new Vector2(640, 760));
            ImGui.Begin("Details", ImGuiNET.ImGuiWindowFlags.NoCollapse);
            panel.DrawComponentsFor(session, unit);
            ImGui.End();
            channel.EndFrame();   // the frame's asks become the request (the editor's frame overlay does this)
        });

        var rows = section.Rows;   // the LAST component drawn (ObjectiveStatus sorts last) — three rows
        Assert.Equal(new[] { 2, 3, 4 }, rows.Select(r => r.Node).ToArray());
        Assert.Equal(new[] { true, false, false }, rows.Select(r => r.Owner).ToArray());               // the crown on the owner's row
        Assert.Equal(new[] { false, false, true }, rows.Select(r => r.Amber).ToArray());               // amber where it differs
        Assert.Single(rows.Select(r => r.NameX).Distinct());                                           // ⭐ the mockup's aligned columns: one x per column
        Assert.Single(rows.Select(r => r.ValueX).Distinct());
        Assert.Single(rows.Select(r => r.NoteX).Distinct());
        Assert.True(rows[0].NameX < rows[0].ValueX && rows[0].ValueX < rows[0].NoteX);
        Assert.True(rows[0].Y < rows[1].Y && rows[1].Y < rows[2].Y);                                   // owner first, top to bottom
        string dir = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } sd ? sd : "";
        if (dir != "") { Directory.CreateDirectory(dir); frame.Screenshot(Path.Combine(dir, "g7s5-details-by-node.png")); }
    }

    /// <summary>
    /// ⭐ §6.13 RW-6 (mockup ① "Strength — by node") — a WATCHED attribute shows its per-node values: the owner (crown) first, a replica beyond the item's tolerance amber with its Δ. The tolerance decides what is amber (7 apart: amber at 0.5, not at 10). 🔴 Red-proof: ignore the tolerance in <c>ReplicaViews.ComputeItem</c> ⇒ the tolerance-10 half shows node 4 amber.
    /// </summary>
    [SkippableFact]
    public void RW6_AWatchedAttribute_ShowsItsPerNodeValues_AmberBeyondTolerance()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        ReplicaGroup();
        using var rig = new Rig(_root, _serializer, names: n => n switch { 2 => "CGF-1 (Brain)", 3 => "SimHost-1 (Muscle)", _ => "IG-1 (Map2D)" });
        rig.Open(_exercise);
        var (section, channel) = ByNode(rig);
        section.Capture = true;
        var watch = new ReplicaWatchList();
        section.Watch = watch;
        var unit = UnitOf(rig.World, 100);
        var item = watch.Add(100, "ObjectiveStatus.ProgressCur", 0, expert: true, label: "Progress");

        var views = new ReplicaViews(new OwnershipSource(), _serializer);
        var view = views.ComputeItem(rig.Source.MergedManager!, 100, item)!;
        Assert.Equal(new[] { 2, 3, 4 }, view.Rows.Select(r => r.NodeId).ToArray());
        Assert.Equal(new[] { true, false, false }, view.Rows.Select(r => r.IsOwner).ToArray());
        Assert.Equal(new[] { false, false, true }, view.Rows.Select(r => r.Differs).ToArray());
        Assert.Equal(7d, view.Rows[2].Delta, 3);
        watch.SetTolerance(100, "ObjectiveStatus.ProgressCur", 0, 10);
        Assert.DoesNotContain(views.ComputeItem(rig.Source.MergedManager!, 100, watch.ItemsOf(100)[0])!.Rows, r => r.Differs);   // within the tolerance: nothing amber
        watch.SetTolerance(100, "ObjectiveStatus.ProgressCur", 0, 0.5);

        using var frame = UiFrameHarness.Begin(640, 260);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        frame.StepN(5, () =>
        {
            ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new Vector2(640, 260));
            ImGui.Begin("Details", ImGuiNET.ImGuiWindowFlags.NoCollapse);
            section.DrawAttribute(unit, watch.ItemsOf(100)[0]);
            ImGui.End();
            channel.EndFrame();
        });
        Assert.Equal(new[] { 2, 3, 4 }, section.Rows.Select(r => r.Node).ToArray());
        Assert.Equal(new[] { false, false, true }, section.Rows.Select(r => r.Amber).ToArray());
        string dir = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } sd ? sd : "";
        if (dir != "") { Directory.CreateDirectory(dir); frame.Screenshot(Path.Combine(dir, "rw6-attribute-by-node.png")); }
    }
}
