using System;
using Fdp.Core;
using Hrot.Common.Infrastructure;
using Hrot.Editor.AiShared.Rules;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Rules;

/// <summary>
/// ⭐ CE-4292 (docs/UX/DESIGN_Rules_Bundle.md §3, §4 R2 / R3) — who may edit rules is the SEAT, never the host: Author and Instructor get the rule editor
/// services with no seat refusal, every other seat gets the refusal (greyed with its reason, R-4119); a recording gets no services at all and no
/// Game master. 🔴 Red-proof (compiles): <c>MayEdit</c> returning true for every seat ⇒ the Reviewer row ⇒ red.
/// </summary>
[Collection(Hrot.Editor.AiShared.Tests.PanelSnapshotTestCollection.Name)]
public sealed class TheRulesBundleTests
{
    private static RulesHostServices Host(Seat seat, bool recording = false, bool canCreate = false, EntityRepository? w = null, bool trainee = false)
    {
        var world = w ?? new EntityRepository();
        return new RulesHostServices(
            World: () => world, TimelineSource: () => null, Cursor: () => null,
            IdPrefix: "t_", Perspective: "Scenario", Colour: System.Numerics.Vector4.One,
            SelectEntity: _ => { }, ShowOnMap: _ => { }, Seat: () => seat,
            Blocked: recording ? () => "a recording is read-only" : null,
            EntityCreation: canCreate ? () => null : null,
            Recording: recording, TraineeOnly: trainee);
    }

    [Theory]
    [InlineData(Seat.Author, true)]
    [InlineData(Seat.Instructor, true)]
    [InlineData(Seat.SideController, false)]
    [InlineData(Seat.Player, false)]
    [InlineData(Seat.Observer, false)]
    [InlineData(Seat.Reviewer, false)]
    public void CE4292_R2_TheSeatDecidesWhoMayEditRules(Seat seat, bool mayEdit)
    {
        var svc = RuleEditorServicesFactory.Build(Host(seat));
        Assert.NotNull(svc);                                       // a live host always gets the services …
        Assert.Equal(mayEdit, svc!.SeatRefusal!() is null);       // … the SEAT decides, read every frame
        if (!mayEdit) Assert.Contains(seat.ToString(), svc.SeatRefusal!());
        Assert.NotNull(svc.Live); Assert.NotNull(svc.MacroPreview);   // the one factory: every host gets the live + macro services
    }

    [Fact]
    public void CE4292_R3_ARecordingGetsNoEditorAndNoGameMaster_ALiveHostThatCreatesGetsTheGameMaster()
    {
        var onRecording = new RulesBundle(Host(Seat.Reviewer, recording: true));
        Assert.Null(onRecording.Editor);
        Assert.Null(onRecording.GameMaster);
        Assert.Equal("t_rules", onRecording.Rules.Id);
        Assert.Equal("t_mission_summary", onRecording.MissionSummary.Id);
        Assert.Equal("t_trainee_debrief", onRecording.TraineeDebrief.Id);
        Assert.Equal("t_game_summary", onRecording.GameSummary.Id);

        var live = new RulesBundle(Host(Seat.Instructor, canCreate: true));
        Assert.NotNull(live.Editor);
        Assert.NotNull(live.GameMaster);
        Assert.Equal("t_game_master", live.GameMaster!.Id);

        var cannotCreate = new RulesBundle(Host(Seat.Author));
        Assert.Null(cannotCreate.GameMaster);   // a capability the host lacks: no hand-run actions
    }

    /// <summary>
    /// ⭐ L-2 / CE-4320 (LC-15, R-4166) — the BUNDLE opens Mission summary when Live starts and again when the exercise ends (was the editor's alone,
    /// CE-4240): never on first sight, never on a recording host, 0 B per steady frame. 🔴 Red-proof (compiles): the Live-start test dropped ⇒ the
    /// Edit → Live row ⇒ red.
    /// </summary>
    [Fact]
    public void L2_MissionSummaryOpensWhenLiveStarts_AndWhenTheExerciseEnds_OnEveryComposingHost()
    {
        var world = new EntityRepository();
        world.RegisterComponent<Fdp.Toolkit.ScenarioRules.ExerciseResult>();
        var mode = Hrot.Common.Editing.EditingMode.Live;
        Hrot.Common.Editing.StationMode.Install(world, () => mode);
        var bundle = new RulesBundle(Host(Seat.Instructor, w: world));
        var windows = new Fdp.Presentation.WindowManager.WindowManager(new Fdp.Presentation.Icons.IconAtlas(nint.Zero, 1, 1, 16f));
        bundle.RegisterInto(new Fdp.Toolkit.Runner.UiBundleContext(windows));
        Assert.Contains((Action)bundle.WatchTheRun, windows.FrameOverlays);    // watched every frame, by composing the bundle

        bundle.WatchTheRun();
        Assert.Equal(0, bundle.SummaryOpenings);                               // first sight of a running exercise: not "starting" it
        mode = Hrot.Common.Editing.EditingMode.Edit; bundle.WatchTheRun();
        mode = Hrot.Common.Editing.EditingMode.Live; bundle.WatchTheRun();
        Assert.Equal(1, bundle.SummaryOpenings);                               // Edit → Live: it opens
        Assert.True(bundle.MissionSummary.IsOpen);

        var result = world.CreateEntity();
        world.AddComponent(result, new Fdp.Toolkit.ScenarioRules.ExerciseResult { Phase = Fdp.Toolkit.ScenarioRules.ExercisePhase.Running });
        bundle.WatchTheRun();
        Assert.Equal(1, bundle.SummaryOpenings);
        world.GetComponentRW<Fdp.Toolkit.ScenarioRules.ExerciseResult>(result).Phase = Fdp.Toolkit.ScenarioRules.ExercisePhase.Ended;
        bundle.WatchTheRun(); bundle.WatchTheRun();
        Assert.Equal(2, bundle.SummaryOpenings);                               // Running → Ended: once

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50; i++) bundle.WatchTheRun();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);      // a steady frame

        var onRecording = new RulesBundle(Host(Seat.Reviewer, recording: true));
        var w2 = new Fdp.Presentation.WindowManager.WindowManager(new Fdp.Presentation.Icons.IconAtlas(nint.Zero, 1, 1, 16f));
        onRecording.RegisterInto(new Fdp.Toolkit.Runner.UiBundleContext(w2));
        Assert.DoesNotContain((Action)onRecording.WatchTheRun, w2.FrameOverlays);   // a recording opens nothing
    }

    /// <summary>⭐ L-2 / CE-4320 (LC-18, PH-6) — the Player's set: the Trainee debrief ALONE (no rules, no summaries, no editing), opened when the exercise
    /// ENDS — never when Live starts. 🔴 Red-proof: the trainee branch of the watcher dropped ⇒ it opens Mission summary (not registered) ⇒ red.</summary>
    [Fact]
    public void L2_ThePlayersSet_IsTheTraineeDebriefAlone_OpenedWhenTheExerciseEnds()
    {
        var world = new EntityRepository();
        world.RegisterComponent<Fdp.Toolkit.ScenarioRules.ExerciseResult>();
        var mode = Hrot.Common.Editing.EditingMode.Edit;
        Hrot.Common.Editing.StationMode.Install(world, () => mode);
        var bundle = new RulesBundle(Host(Seat.Player, canCreate: true, w: world, trainee: true));
        var windows = new Fdp.Presentation.WindowManager.WindowManager(new Fdp.Presentation.Icons.IconAtlas(nint.Zero, 1, 1, 16f));
        bundle.RegisterInto(new Fdp.Toolkit.Runner.UiBundleContext(windows));
        Assert.True(windows.TryGetWindow("t_trainee_debrief", out _));
        foreach (var id in new[] { "t_rules", "t_mission_summary", "t_game_summary", "t_game_master" })
            Assert.False(windows.TryGetWindow(id, out _), id + " is not a trainee's");
        Assert.Null(bundle.Editor);

        bundle.WatchTheRun();
        mode = Hrot.Common.Editing.EditingMode.Live; bundle.WatchTheRun();
        Assert.False(bundle.TraineeDebrief.IsOpen);                            // Live starting opens nothing for a trainee
        var result = world.CreateEntity();
        world.AddComponent(result, new Fdp.Toolkit.ScenarioRules.ExerciseResult { Phase = Fdp.Toolkit.ScenarioRules.ExercisePhase.Ended });
        bundle.WatchTheRun();
        Assert.True(bundle.TraineeDebrief.IsOpen);                             // the end: his debrief
        Assert.Equal(1, bundle.SummaryOpenings);
    }
}
