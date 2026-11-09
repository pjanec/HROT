using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Time.Domain;
using Fdp.Toolkit.Time.Messages;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Panels;
using Hrot.UI.Common.RunMode;
using System.Text.Json.Nodes;
using Xunit;
using static Hrot.Presentation.Tests.RunChoiceRig;

namespace Hrot.Presentation.Tests;

/// <summary>
/// ⭐⭐ <c>CE-3405</c> + <c>CE-3413</c> (AQ108 §0 — R1′ switch now, R2″ Live ends in two steps): one rail per move. The rig is the REAL adapter on a REAL bus with a run choice and a fake session
/// (<see cref="RunChoiceRig"/>); the exercise's end is faked by <see cref="Ender"/> (the replicated <c>ExerciseResult.Phase</c> the rules bundle's End control reads).
/// </summary>
[Collection(ImGuiTestCollection.Name)]   // one global ImGui context (the Render rail)
public sealed class RunControlsSwitchNowTests
{
    private sealed class Ender : IExerciseEnder
    {
        public bool CanEnd { get; set; } = true;
        public bool HasEnded { get; set; }
        public int Ended;
        public bool End() { Ended++; return true; }
    }

    private static MainToolbarTimeControlSection Bar(Rig rig) => new(rig.F, choice: rig.Choice);

    /// <summary>A running Live with an ender, 12:34 in; <paramref name="doorSession"/> gives the session its ONE door (Leave is then recorded as <c>Leave:Close</c>).</summary>
    private static (Rig Rig, Ender Ender) RunningLive(double clock = 754, bool running = true, bool doorSession = true, bool runNotRecorded = false)
    {
        var rig = With(ClusterState.OperatingLive, RunArmed.Live, clock: clock, running: running, withDoor: doorSession, runNotRecorded: runNotRecorded);
        var ender = new Ender();
        rig.F.Ender = ender;
        return (rig, ender);
    }

    private static void End(Rig rig, Ender ender) { ender.HasEnded = true; ender.CanEnd = false; rig.Choice.Update(); }

    // ── R-1 (CE-3405) ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Edit → Live: the switch loads Live paused at 0:00 (the briefing) and the cluster stays there — NO resume; ▶ then resumes (one <c>ResumeTimeIntent</c>), and ■ asks nothing until the clock ran.
    /// 🔴 Mutations: <c>GoLive</c> setting <c>_resumeWhenLive</c> ⇒ the first Resumes assert is red; <c>SwitchToLive</c> calling <c>Arm</c> before the door (a cancelled switch armed) is the Cancel row of CE3405_PlayFromEdit…
    /// </summary>
    [Fact]
    public void R1_EditToLive_LandsInTheBriefing_PausedAtZero_AndPlayThenResumes()
    {
        var rig = With(ClusterState.OperatingEdit, RunArmed.Preview);
        rig.Choice.Choose(RunArmed.Live);
        Assert.Equal(new[] { "LoadForLive:hill-attack" }, rig.C.Calls);
        Hear(rig, ClusterState.OperatingLive);
        Assert.Empty(Resumes(rig));                           // the briefing: paused at 0:00
        Assert.True(rig.F.IsPaused);
        Assert.True(rig.Choice.LiveBriefing);
        Assert.Equal(RunArmed.Live, rig.Choice.Shown);
        Assert.True(Bar(rig).IsPlayEnabled);
        Bar(rig).OnPlayPause();                               // ▶ in a paused Live = the facade's own resume
        Assert.Single(Sent<ResumeTimeIntent>(rig));
    }

    /// <summary>Edit → AAR: the last exercise opens at its start through the door (unsaved ⇒ Save / Discard / Cancel first); no exercise yet ⇒ the recording picker.
    /// 🔴 Mutation: <c>SwitchToAar</c> opening the recording without the door (<c>_control.OpenRecording</c>) ⇒ the Changed row opens unasked.</summary>
    [Fact]
    public void R1_EditToAar_OpensTheLastExerciseThroughTheDoor_OrThePicker()
    {
        var live = With(ClusterState.OperatingLive, RunArmed.Live, UnsavedAnswer.Changed);
        live.F.Stop();                                          // no ender: today's ■ — the run is LastExercise
        live.C.Calls.Clear();
        Hear(live, ClusterState.OperatingEdit);
        live.Choice.Choose(RunArmed.AAR);
        Assert.Empty(live.C.Calls);                             // the edits are unsaved: the door asks first
        Assert.Equal(new[] { "Save", "Discard", "Cancel" }, live.Choice.Pending!.Buttons.Select(b => b.Label));
        live.Choice.Pending.Resolve(live.Choice.Pending.Buttons[1]);   // Discard
        Assert.Equal(new[] { $"OpenRecording:{RunId}" }, live.C.Calls);
        Assert.Equal(RunArmed.AAR, live.Choice.Armed);

        var none = With(ClusterState.OperatingEdit, RunArmed.Preview);
        int picked = 0;
        none.Choice.PickRecording = () => picked++;
        none.Choice.Choose(RunArmed.AAR);
        Assert.Equal(1, picked);
        Assert.Empty(none.C.Calls);
    }

    /// <summary>■ = the start of the SAME mode: Preview → back to Edit (the dry run's exit).
    /// 🔴 Mutation: <c>Stop</c>'s default arm doing nothing ⇒ no Edit transition.</summary>
    [Fact]
    public void R1_StopInPreview_GoesBackToEdit()
    {
        var rig = With(ClusterState.OperatingPreview, RunArmed.Live, clock: 5);
        Bar(rig).OnStop();
        Assert.Equal(ClusterState.OperatingEdit, Sent<TransitionStateIntent>(rig).Single().TargetState);
        Assert.Empty(rig.C.Calls);
    }

    private static readonly ScenarioPreset Easy = new("Easy", PresetVariant.AsAuthored, new JsonObject { ["count"] = 5 });

    /// <summary>The ▾ presets: drawn in Edit and in the Live briefing only (never in a running Live, a DAR or an AAR); in the briefing a preset RELOADS Live with it (still paused), in a running Live it only arms.
    /// 🔴 Mutation: <c>ShowsPresetArrow</c> true for every Live ⇒ the running row; <c>ArmPreset</c> not reloading ⇒ the briefing row.</summary>
    [Fact]
    public void R1_ThePresetArrow_ShowsInEditAndTheBriefing_AndTheBriefingReloadsWithIt()
    {
        PresetRig(With(ClusterState.OperatingEdit, RunArmed.Preview), out var edit);
        Assert.True(edit.ShowsPresetArrow);

        var brief = With(ClusterState.OperatingLive, RunArmed.Live);                      // clock 0, paused: the briefing
        var barBrief = PresetRig(brief, out _);
        Assert.True(barBrief.ShowsPresetArrow);
        brief.Choice.ArmPreset("Easy");
        Assert.Equal(new[] { "LoadForLive:hill-attack:{\"count\":5}" }, brief.C.Calls);  // reloaded with it

        var running = With(ClusterState.OperatingLive, RunArmed.Live, clock: 30, running: true);
        var barRun = PresetRig(running, out _);
        Assert.False(barRun.ShowsPresetArrow);
        running.Choice.ArmPreset("Easy");
        Assert.Empty(running.C.Calls);                                                   // armed only

        foreach (var s in new[] { ClusterState.OperatingReview, ClusterState.OperatingReplay })
            Assert.False(PresetRig(With(s, RunArmed.Live), out _).ShowsPresetArrow);
    }

    private static MainToolbarTimeControlSection PresetRig(Rig rig, out MainToolbarTimeControlSection bar)
    {
        rig.Choice.PresetsOf = _ => new[] { new ScenarioPreset(ScenarioPreset.DefaultName, PresetVariant.AsAuthored), Easy };
        bar = Bar(rig);
        return bar;
    }

    // ── R-2 (CE-3413) ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>■ in a RUNNING Live asks <i>End the exercise?</i>; Cancel (= keep running) changes nothing; the end asks the ender — and the world STAYS in Live (nothing is published, nothing leaves).
    /// 🔴 Mutation: the End button also calling <c>StopLive()</c> (the old L-3) ⇒ <c>OpenForEdit</c> appears.</summary>
    [Fact]
    public void R2_StopInARunningLive_AsksToEnd_AndTheWorldStaysInLive()
    {
        var (rig, ender) = RunningLive();
        Bar(rig).OnStop();
        var ask = rig.Choice.Pending!;
        Assert.Equal("End the exercise?", ask.Title);
        Assert.Equal(new[] { "End the exercise", "Keep running" }, ask.Buttons.Select(b => b.Label));
        ask.Resolve(ask.Buttons[1]);
        Assert.Equal(0, ender.Ended);
        Bar(rig).OnStop();
        rig.Choice.Pending!.Resolve(rig.Choice.Pending.Buttons[0]);
        Assert.Equal(1, ender.Ended);
        End(rig, ender);
        for (int i = 0; i < 300; i++) rig.Choice.Update();
        Assert.Empty(rig.C.Calls);
        Assert.Empty(Sent<TransitionStateIntent>(rig));
        Assert.Equal(TransportMode.Live, rig.F.Mode);
    }

    /// <summary>Live · ended is DERIVED (OperatingLive + the result's phase Ended): ▶ greyed with its words, ■ becomes ⏏; not in any other mode, and not before the result says so.
    /// 🔴 Mutations: <c>LiveEnded</c> returning <c>_ended</c> alone ⇒ the Edit row; <c>PlayDisabledReason</c> without the LiveEnded line ⇒ ▶ stays enabled.</summary>
    [Fact]
    public void R2_LiveEnded_IsDerived_PlayGreyed_StopBecomesEject()
    {
        var (rig, ender) = RunningLive();
        var bar = Bar(rig);
        Assert.False(rig.Choice.LiveEnded);
        Assert.True(bar.IsPlayEnabled);
        Assert.Equal(MainToolbarTimeControlSection.StopFaceKind.Stop, bar.StopFaceNow);

        End(rig, ender);
        Assert.True(rig.Choice.LiveEnded);
        Assert.Equal(TransportMode.Live, rig.F.Mode);                      // still Live: no FSM state of its own
        Assert.False(bar.IsPlayEnabled);
        Assert.Equal("the exercise has ended — restart from a checkpoint to train again", rig.Choice.PlayDisabledReason);
        Assert.Equal(rig.Choice.PlayDisabledReason, bar.PlayTooltip(isPaused: true));
        Bar(rig).OnPlayPause();
        Assert.Empty(Sent<ResumeTimeIntent>(rig));                         // ▶ does nothing
        Assert.Equal(MainToolbarTimeControlSection.StopFaceKind.BackToLive, bar.StopFaceNow);   // the ⏏ face
        Assert.True(bar.IsStopEnabled);
        Assert.Contains("Close the session", bar.StopTooltip());

        // the same "ended" fact outside Live is nothing
        var edit = With(ClusterState.OperatingEdit, RunArmed.Live);
        var e2 = new Ender { HasEnded = true };
        edit.F.Ender = e2;
        edit.Choice.Update();
        Assert.False(edit.Choice.LiveEnded);
        Assert.Null(edit.Choice.PlayDisabledReason);
    }

    /// <summary>⭐⭐ CE-4330 ③ (docs/DESIGN_Recording_Storage.md §3 P1, Q1; mockup ③) — ⏏ in a RECORDED run asks <i>Close the live session</i> with what becomes of its
    /// recording: Archive to NAS (preselected) · Keep on the nodes · Discard, then <i>Close session</i> / Cancel. Cancel keeps the session; Close = the door's Leave
    /// (<c>Leave:Close</c>) to Idle — never <c>OpenForEdit</c> — then the answer's ONE storage request (<c>SessionClose.IntentFor</c>), named by this station, and the
    /// toast. ⛔ <i>Keep this recording?</i> is NOT asked after it (it was answered), even with the cluster's ask-to-keep on.
    /// 🔴 Mutations: Close calling <c>StopLive()</c> ⇒ <c>OpenForEdit</c>, no Leave; the intent not sent ⇒ the Archive row; <c>asked</c> not passed ⇒ a Keep question after Close.</summary>
    [Fact]
    public void CE4330_Eject_AsksWhatBecomesOfTheRecording_ThenLeavesToIdle_AndArchives()
    {
        var (rig, ender) = RunningLive();
        rig.Choice.AskToKeep = () => true;
        rig.Choice.StationName = () => "IOS-1";
        End(rig, ender);
        Bar(rig).OnStop();
        var ask = rig.Choice.Pending!;
        Assert.Equal("Close the live session", ask.Title);
        Assert.Equal("hill-attack · exercise — 12:34, recorded. Every station returns to the Start screen.", ask.Text);
        Assert.Equal(new[] { "Archive to NAS", "Keep on the nodes", "Discard" }, ask.Choices.Select(c => c.Label));
        Assert.Equal(new[] { "storage/archive", "storage/keep", "storage/delete" }, ask.Choices.Select(c => c.IconKey));
        Assert.Equal(0, ask.Selected);                                     // Q1: Archive is the default
        Assert.Equal(new[] { "Close session", RunModeConfirmation.CancelLabel }, ask.Buttons.Select(b => b.Label));
        ask.Resolve(ask.Buttons[1]);                                       // Cancel
        Assert.Empty(rig.C.Calls);
        Assert.Empty(Sent<ExecuteStorageOpIntent>(rig));
        Assert.True(rig.Choice.LiveEnded);

        Bar(rig).OnStop();
        rig.Choice.Pending!.Resolve(rig.Choice.Pending.Buttons[0]);       // Close session, Archive
        Assert.Equal(new[] { "Leave:Close" }, rig.C.Calls);                // the door's leave to Idle, not an edit open
        Assert.Equal(RunId, rig.Choice.LastExercise);
        var archive = Assert.Single(Sent<ExecuteStorageOpIntent>(rig));
        Assert.Equal(StorageOpType.Export, archive.Operation);
        Assert.Equal(RunId, archive.ExerciseId);
        Assert.Equal("IOS-1", archive.AskedBy);
        Assert.Null(rig.Choice.Pending);                                   // ⛔ no "Keep this recording?" after it
        Assert.Equal("Exercise recorded (12:34) — open the AAR", rig.Choice.Toast!.Title);
    }

    /// <summary>⭐ CE-4330 ③ — Keep sends nothing (the recording stays on the nodes); Discard sends ONE delete of the unarchived run and the toast says so (no AAR offer).</summary>
    [Theory]
    [InlineData(1, null)]
    [InlineData(2, StorageOpType.Delete)]
    public void CE4330_Eject_KeepSendsNothing_DiscardDeletes(int choice, StorageOpType? sent)
    {
        var (rig, ender) = RunningLive();
        End(rig, ender);
        Bar(rig).OnStop();
        var ask = rig.Choice.Pending!;
        ask.Selected = choice;
        ask.Resolve(ask.Buttons[0]);
        var intents = Sent<ExecuteStorageOpIntent>(rig);
        if (sent is null) { Assert.Empty(intents); Assert.Equal("Exercise recorded (12:34) — open the AAR", rig.Choice.Toast!.Title); }
        else
        {
            var one = Assert.Single(intents);
            Assert.Equal(sent, one.Operation);
            Assert.False(one.FromArchive);
            Assert.Equal("Live run ended — its recording is being deleted", rig.Choice.Toast!.Title);
        }
    }

    /// <summary>⭐ CE-4330 ③ (Q3) — an UNRECORDED run asks nothing about a recording: the plain <i>Close the live session?</i>, and Close sends no storage request.</summary>
    [Fact]
    public void CE4330_Eject_OnAnUnrecordedRun_AsksNoRecordingQuestion()
    {
        var (rig, ender) = RunningLive(runNotRecorded: true);
        End(rig, ender);
        Bar(rig).OnStop();
        var ask = rig.Choice.Pending!;
        Assert.Equal("Close the live session?", ask.Title);
        Assert.Empty(ask.Choices);
        Assert.Equal("This run was not recorded. Every station returns to the Start screen.", ask.Text);
        ask.Resolve(ask.Buttons[0]);
        Assert.Equal(new[] { "Leave:Close" }, rig.C.Calls);
        Assert.Empty(Sent<ExecuteStorageOpIntent>(rig));
    }

    /// <summary>A host whose session has no door OUT closes through the same Idle intent (<c>StopRaw</c>).</summary>
    [Fact]
    public void R2_Close_OnASessionWithNoLeave_PublishesTheIdleIntent()
    {
        var (rig, ender) = RunningLive(doorSession: false);
        End(rig, ender);
        rig.F.Stop();
        rig.Choice.Pending!.Resolve(rig.Choice.Pending.Buttons[0]);
        Assert.Equal(ClusterState.Idle, Sent<TransitionStateIntent>(rig).Single().TargetState);
        Assert.Empty(rig.C.Calls);
    }

    /// <summary>Run again: the same scenario loads into the briefing (a new exercise); only from Live · ended.
    /// 🔴 Mutation: <c>RunAgain</c> without the <c>LiveEnded</c> guard ⇒ the running row loads.</summary>
    [Fact]
    public void R2_RunAgain_ReloadsTheSameScenarioIntoTheBriefing()
    {
        var (rig, ender) = RunningLive();
        rig.Choice.RunAgain();
        Assert.Empty(rig.C.Calls);                                         // not ended: nothing
        End(rig, ender);
        rig.Choice.RunAgain();
        Assert.Equal(new[] { "LoadForLive:hill-attack" }, rig.C.Calls);
        Assert.Equal(RunArmed.Live, rig.Choice.Armed);
        Hear(rig, ClusterState.Idle);
        Hear(rig, ClusterState.OperatingLive);
        Assert.Empty(Resumes(rig));                                        // the briefing again: paused
    }

    /// <summary>The ended menu: Run again · Restart from a checkpoint ▸ (greyed — no live checkpoint list yet — unless the host supplies one) · DAR — look back… ▸ · AAR — full replay of this exercise · separator · Close the session.
    /// 🔴 Mutation: <c>ModeMenuBase</c> not routing to <c>EndedMenu</c> ⇒ the running menu's Preview / AAR moves appear.</summary>
    [Fact]
    public void R2_TheEndedMenu_OffersWhatIsNext()
    {
        var (rig, ender) = RunningLive();
        End(rig, ender);
        var menu = Bar(rig).ModeMenu()!;
        Assert.StartsWith("Live · ended", menu.Header);
        Assert.Equal(new[] { "Run again (same scenario → the briefing)", "Restart from a checkpoint — none listed for this run", "DAR — look back…", "AAR — full replay of this exercise", "", "Close the session" },
                     menu.Items.Select(i => i.Label));
        Assert.True(menu.Items[4].IsSeparator);
        Assert.False(menu.Items[1].Enabled);
        Assert.NotEmpty(menu.Items[2].Children!);

        var checkpoints = Bar(rig);
        string? restored = null;
        rig.Choice.CheckpointLabels = () => new[] { "before the bridge" };
        rig.Choice.RestoreCheckpoint = l => restored = l;
        var cp = checkpoints.ModeMenu()!.Items[1];
        Assert.True(cp.Enabled);
        cp.Children!.Single().Invoke!();
        Assert.Equal("before the bridge", restored);

        menu.Items[3].Invoke!();                                           // AAR — the full replay of this exercise
        Assert.Equal(new[] { $"OpenRecording:{RunId}" }, rig.C.Calls);
        Assert.Equal(RunArmed.AAR, rig.Choice.Armed);

        var (again, e2) = RunningLive();
        End(again, e2);
        Bar(again).ModeMenu()!.Items[0].Invoke!();                         // Run again
        Assert.Equal(new[] { "LoadForLive:hill-attack" }, again.C.Calls);

        var (close, e3) = RunningLive();
        End(close, e3);
        Bar(close).ModeMenu()!.Items[5].Invoke!();                         // Close the session
        Assert.Equal("Close the live session", close.Choice.Pending!.Title);

        var (unrecorded, e4) = RunningLive(runNotRecorded: true);
        End(unrecorded, e4);
        var aar = Bar(unrecorded).ModeMenu()!.Items[3];
        Assert.False(aar.Enabled);
        Assert.EndsWith("this run was not recorded", aar.Label);
    }

    /// <summary>During the briefing (Live, clock never advanced) ■ and ⏪ Review are greyed with their reasons; once the clock has run (running or paused) both are on; the call itself (<c>POST /sim/stop</c>) still answers.
    /// 🔴 Mutation: <c>LiveBriefing</c> always false ⇒ both greys disappear.</summary>
    [Fact]
    public void R2_TheBriefing_GreysStopAndReview_WithTheirReasons()
    {
        var brief = With(ClusterState.OperatingLive, RunArmed.Live);
        var bar = Bar(brief);
        Assert.True(bar.ShowsReviewButton);
        Assert.False(bar.IsReviewEnabled);
        Assert.Equal("nothing to review yet", brief.Choice.ReviewDisabledReason);
        Assert.False(bar.IsStopEnabled);
        Assert.Contains("nothing to stop", bar.StopTooltip());
        bar.OnStop();
        Assert.Null(brief.Choice.Pending);
        Assert.Empty(brief.C.Calls);                                       // the toolbar's ■ did nothing

        var ran = With(ClusterState.OperatingLive, RunArmed.Live, clock: 12);                  // paused, but it ran
        Assert.True(Bar(ran).IsReviewEnabled);
        Assert.True(Bar(ran).IsStopEnabled);
        var going = With(ClusterState.OperatingLive, RunArmed.Live, clock: 12, running: true);
        Assert.True(Bar(going).IsStopEnabled && Bar(going).IsReviewEnabled);

        brief.F.Stop();                                                    // POST /sim/stop is not the toolbar: it still answers
        Assert.Equal(new[] { "OpenForEdit:hill-attack" }, brief.C.Calls);
    }

    /// <summary>The ended Live draws headless without throwing (the ⏏ face, the greyed ▶, the menu and the Close modal).</summary>
    [Fact]
    public void R2_TheEndedToolbar_Renders()
    {
        using var fixture = new ImGuiTestFixture();
        fixture.NewFrame();
        var (rig, ender) = RunningLive();
        End(rig, ender);
        Bar(rig).OnStop();                                                 // the Close modal is pending
        Assert.NotNull(rig.Choice.Pending);
        var section = Bar(rig);
        ImGuiNET.ImGui.Begin("test_toolbar_ended", ImGuiNET.ImGuiWindowFlags.NoSavedSettings);
        ImGuiNET.ImGui.SetWindowSize(new System.Numerics.Vector2(900, 80));
        section.Render();
        ImGuiNET.ImGui.End();
        fixture.Render();
    }

    /// <summary>
    /// ⭐ Hot path (CE-3409): the toolbar draws every frame; with no menu open, no modal and no toast its steady-state frame allocates <b>0 B</b> in every state (Edit, the briefing, a running Live, Live · ended).
    /// 🔴 Mutations: <c>ModeMenu()</c> built per frame ⇒ ~1.7 KB / frame; <c>TransportIcons.FormatTime</c> / <c>FormatRate</c> uncached ⇒ ~390 B / frame (measured).
    /// </summary>
    [Fact]
    public void Hot_TheToolbarFrame_AllocatesNothingSteady_InEveryState()
    {
        using var fixture = new ImGuiTestFixture();
        fixture.NewFrame();
        var cases = new List<(string Name, Rig Rig)>
        {
            ("edit", With(ClusterState.OperatingEdit, RunArmed.Preview)),
            ("briefing", With(ClusterState.OperatingLive, RunArmed.Live)),
            ("running", With(ClusterState.OperatingLive, RunArmed.Live, clock: 30, running: true)),
        };
        var (ended, ender) = RunningLive();
        End(ended, ender);
        cases.Add(("ended", ended));
        var bytesOf = new Dictionary<string, long>();
        foreach (var (name, rig) in cases)
        {
            var section = Bar(rig);
            var window = "alloc_toolbar_" + name;   // built once, outside the measured frames
            void Frame()
            {
                ImGuiNET.ImGui.Begin(window, ImGuiNET.ImGuiWindowFlags.NoSavedSettings);
                section.Render();
                ImGuiNET.ImGui.End();
            }
            for (int i = 0; i < 40; i++) Frame();                    // warm-up: caches, fonts, ids
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) Frame();
            bytesOf[name] = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        fixture.Render();
        Assert.True(bytesOf.Values.All(b => b == 0), string.Join(" ", bytesOf.Select(kv => $"{kv.Key}={kv.Value}")));
    }

    // ── CE-3411 (backend's REQUEST) — Choose with the CALLER's asker: an HTTP switch never leaves a question in this host's UI ─────────────────────────

    /// <summary>Edit with unsaved edits, <c>Choose(Live, httpAsker, Changed)</c>: the CALLER's asker gets the unsaved question (refused), nothing loads, nothing is pending here; with
    /// <c>discardUnsaved</c> it loads Live at once. 🔴 Mutation: <c>SwitchToLive</c> passing <c>null</c> for the asker ⇒ the bound asker's Save / Discard / Cancel lands on <c>Pending</c> ⇒ red.</summary>
    [Fact]
    public void CE3411_ChooseWithTheCallersAsker_TheQuestionGoesToTheCaller_NotToThisHostsUi()
    {
        var rig = With(ClusterState.OperatingEdit, RunArmed.Preview, UnsavedAnswer.Changed);
        var http = new RefusingAsker();
        rig.Choice.Choose(RunArmed.Live, http, UnsavedAnswer.Changed);
        Assert.Null(rig.Choice.Pending);                      // nothing standing in the operator's UI
        Assert.NotNull(http.Refusal);                         // the caller was told why
        Assert.Empty(rig.C.Calls);

        var discard = new RefusingAsker(discardUnsaved: true);
        rig.Choice.Choose(RunArmed.Live, discard, UnsavedAnswer.Changed);
        Assert.Null(discard.Refusal);
        Assert.Equal(new[] { "LoadForLive:hill-attack" }, rig.C.Calls);
    }

    /// <summary>The bare <c>Choose(Live)</c> keeps the operator's own question (the overload changed nothing for the toolbar).</summary>
    [Fact]
    public void CE3411_TheBareChoose_StillAsksInThisHostsUi()
    {
        var rig = With(ClusterState.OperatingEdit, RunArmed.Preview, UnsavedAnswer.Changed);
        rig.Choice.Choose(RunArmed.Live);
        Assert.Equal(new[] { "Save", "Discard", "Cancel" }, rig.Choice.Pending!.Buttons.Select(b => b.Label));
    }

    /// <summary>
    /// ⭐ CE-3413 + backend's ANSWER — Live · ended's <i>Restart from a checkpoint ▸</i> lists the live run's checkpoints from the session layer (<c>ClusterView.ReplaySession.Checkpoints</c>) and a pick publishes
    /// <c>RestoreCheckpointIntent { Label }</c>; the labels are rebuilt only when a new array arrives. 🔴 Mutation: <c>AttachRunChoice</c> not setting <c>CheckpointLabels</c> ⇒ empty ⇒ red.
    /// </summary>
    [Fact]
    public void R2_RestartFromACheckpoint_ListsTheLiveRunsCheckpoints_AndRestoresThePick()
    {
        var (rig, _) = RunningLive();
        var marks = new[] { new Fdp.Core.FlightRecorder.Metadata.CheckpointMark { Label = "before the bridge" }, new Fdp.Core.FlightRecorder.Metadata.CheckpointMark { Label = "contact" } };
        rig.Bus.PublishManaged(new Fdp.Toolkit.Orchestration.ReplaySessionUpdateEvent { Session = new Fdp.Toolkit.Orchestration.ReplaySession { Checkpoints = marks } });
        Hear(rig, ClusterState.OperatingLive);
        var labels = rig.Choice.CheckpointLabels!();
        Assert.Equal(new[] { "before the bridge", "contact" }, labels);
        Assert.Same(labels, rig.Choice.CheckpointLabels!());   // steady: the same array, nothing rebuilt
        Sent<Fdp.Toolkit.Orchestration.RestoreCheckpointIntent>(rig);
        rig.Choice.RestoreCheckpoint!("contact");
        Assert.Equal("contact", Assert.Single(Sent<Fdp.Toolkit.Orchestration.RestoreCheckpointIntent>(rig)).Label);
    }

    // ── ⭐ CE-4099 (AQ108 §4c A7) — the recovery prompt: "Recover unsaved changes from HH:mm?" through the door's ONE asker ─────────────────────────────

    private static readonly DateTime AutosaveAt = new(2026, 11, 8, 14, 41, 0, DateTimeKind.Utc);

    /// <summary>A door-owning rig (Idle, Preview armed ⇒ Load = open for editing) whose door reads <paramref name="status"/> as the scenario's autosave; <paramref name="lookups"/> counts the reads.</summary>
    private static (Rig Rig, ScenarioDoor Door, int[] Lookups) RecoveryRig(AutosaveFiles.Status status, ClusterState state = ClusterState.Idle, UnsavedAnswer answer = UnsavedAnswer.Unchanged)
    {
        var rig = With(state, RunArmed.Preview, answer, withDoor: true);
        var door = ((FakeDoorControl)rig.C).Door;
        var lookups = new[] { 0 };
        door.AutosaveOf = name => { lookups[0]++; return status; };
        return (rig, door, lookups);
    }

    private static AutosaveFiles.Status Exists(bool stale = false) => new(true, AutosaveAt, stale, "autosave.json");

    /// <summary>
    /// An open for editing of a scenario with a recoverable autosave ASKS — <i>Recover unsaved changes from HH:mm?</i> (the autosave's time on the operator's clock), buttons <i>Recover</i> / <i>Open the saved one</i> + the house Cancel, on the
    /// ONE pending confirmation — and nothing opens until an answer. File ▸ Open, Recent and the pickers all call <c>Load</c>, which resolves to this open (Preview armed).
    /// 🔴 Mutations: <c>OfferRecovery</c> always opening (no question) ⇒ <c>Pending</c> null; the text without the time ⇒ the text assert; a third button or no Cancel ⇒ the labels assert.
    /// </summary>
    [Fact]
    public void CE4099_AnOpenWithARecoverableAutosave_AsksWithTheTime_AndOpensNothingYet()
    {
        var (rig, door, lookups) = RecoveryRig(Exists());
        door.Load("alpha");                                                         // the File ▸ Open / Recent / picker path: Load ⇒ Open in Preview armed
        Assert.Equal(1, lookups[0]);
        var q = rig.Choice.Pending!;
        Assert.Equal($"Recover unsaved changes from {AutosaveAt.ToLocalTime():HH:mm}?", q.Text);
        Assert.Equal(new[] { "Recover", "Open the saved one", "Cancel" }, q.Buttons.Select(b => b.Label));
        Assert.Empty(rig.C.Calls);
    }

    /// <summary><i>Recover</i> ⇒ the open carries the recovery flag (and a preset's args when it had them).
    /// 🔴 Mutation: the Recover button opening without the flag ⇒ the call is the plain <c>OpenForEdit:alpha</c>.</summary>
    [Fact]
    public void CE4099_Recover_OpensWithTheRecoveryFlag()
    {
        var (rig, door, _) = RecoveryRig(Exists());
        door.Open("alpha");
        rig.Choice.Pending!.Resolve(rig.Choice.Pending.Buttons[0]);
        Assert.Equal(new[] { "OpenForEdit:alpha::recover=True" }, rig.C.Calls);
        Assert.Null(rig.Choice.Pending);

        rig.C.Calls.Clear();
        door.Open("beta", argsJson: "{\"variant\":\"x\"}");
        rig.Choice.Pending!.Resolve(rig.Choice.Pending.Buttons[0]);
        Assert.Equal(new[] { "OpenForEdit:beta:{\"variant\":\"x\"}:recover=True" }, rig.C.Calls);
    }

    /// <summary><i>Open the saved one</i> ⇒ the plain open, no flag; Cancel ⇒ nothing opens.
    /// 🔴 Mutations: the second button opening WITH the flag ⇒ the call differs; Cancel not being the last button / opening ⇒ a call appears.</summary>
    [Fact]
    public void CE4099_OpenTheSavedOne_HasNoFlag_AndCancelOpensNothing()
    {
        var (rig, door, _) = RecoveryRig(Exists());
        door.Open("alpha");
        rig.Choice.Pending!.Resolve(rig.Choice.Pending.Buttons[1]);
        Assert.Equal(new[] { "OpenForEdit:alpha" }, rig.C.Calls);

        rig.C.Calls.Clear();
        door.Open("alpha");
        rig.Choice.Pending!.Resolve(rig.Choice.Pending.Buttons[2]);   // the house Cancel
        Assert.Empty(rig.C.Calls);
        Assert.Null(rig.Choice.Pending);
    }

    /// <summary>A STALE autosave is not offered: the open proceeds as the plain open and the one toast says it was not offered and why (the refusal's words).
    /// 🔴 Mutation: the stale branch asking anyway ⇒ <c>Pending</c> not null; not notifying ⇒ the toast assert.</summary>
    [Fact]
    public void CE4099_AStaleAutosave_IsNotOffered_TheOpenGoesAheadAndTheToastSaysWhy()
    {
        var (rig, door, _) = RecoveryRig(Exists(stale: true));
        door.Open("alpha");
        Assert.Null(rig.Choice.Pending);
        Assert.Equal(new[] { "OpenForEdit:alpha" }, rig.C.Calls);
        var toast = rig.Choice.Toast!;
        Assert.Contains("alpha", toast.Title);
        Assert.Contains("not offered", toast.Title);
        Assert.Contains("stale", toast.Title);
    }

    /// <summary>No autosave (or a door with no lookup) ⇒ the plain open, no question, no toast.
    /// 🔴 Mutation: <c>if (!st.Exists)</c> removed ⇒ a question about a file that is not there.</summary>
    [Fact]
    public void CE4099_NoAutosave_IsThePlainOpen()
    {
        var (rig, door, lookups) = RecoveryRig(new AutosaveFiles.Status(false, default, false, "autosave.json"));
        door.Open("alpha");
        Assert.Equal(1, lookups[0]);
        Assert.Null(rig.Choice.Pending);
        Assert.Null(rig.Choice.Toast);
        Assert.Equal(new[] { "OpenForEdit:alpha" }, rig.C.Calls);

        var bare = With(ClusterState.Idle, RunArmed.Preview, withDoor: true);       // no lookup seam set
        ((FakeDoorControl)bare.C).Door.Open("alpha");
        Assert.Null(bare.Choice.Pending);
        Assert.Equal(new[] { "OpenForEdit:alpha" }, bare.C.Calls);
    }

    /// <summary>HTTP's asker (nobody to ask) never gets the question, and the door does not even read the autosave: the open is plain — HTTP keeps its explicit <c>recoverAutosave</c> flag.
    /// 🔴 Mutation: <c>CanPrompt</c> true on <c>RefusingAsker</c> ⇒ the autosave is read and the question is put to HTTP (its refusal fills, the open never goes).</summary>
    [Fact]
    public void CE4099_HttpAsker_NeverGetsTheQuestion_AndKeepsItsExplicitFlag()
    {
        var (rig, door, lookups) = RecoveryRig(Exists());
        var http = new RefusingAsker();
        door.Open("alpha", http, UnsavedAnswer.Unchanged);
        Assert.Equal(0, lookups[0]);
        Assert.Null(http.Refusal);
        Assert.Null(rig.Choice.Pending);
        Assert.Equal(new[] { "OpenForEdit:alpha" }, rig.C.Calls);
    }

    /// <summary>The door's own questions come first: over unsaved edits <i>Save / Discard / Cancel</i> is asked, and only after the answer that lets the open through does the recovery question follow.
    /// 🔴 Mutation: <c>OfferRecovery</c> called before <c>Admit</c> ⇒ the first question is the recovery one.</summary>
    [Fact]
    public void CE4099_TheUnsavedQuestionComesFirst_ThenTheRecoveryQuestion()
    {
        var (rig, door, lookups) = RecoveryRig(Exists(), ClusterState.OperatingEdit, UnsavedAnswer.Changed);
        door.Open("alpha");
        Assert.Equal(new[] { "Save", "Discard", "Cancel" }, rig.Choice.Pending!.Buttons.Select(b => b.Label));
        Assert.Equal(0, lookups[0]);
        rig.Choice.Pending.Resolve(rig.Choice.Pending.Buttons[1]);                  // Discard
        Assert.Equal(new[] { "Recover", "Open the saved one", "Cancel" }, rig.Choice.Pending!.Buttons.Select(b => b.Label));
        Assert.Empty(rig.C.Calls);
    }
}
