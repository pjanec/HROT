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
        Assert.Equal(new[] { "End the exercise", RunModeConfirmation.CancelLabel }, ask.Buttons.Select(b => b.Label));
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

    /// <summary>⏏ asks <i>Close the live session?</i> (the exercise is kept, every station returns to Start); Cancel keeps the session; Close = the door's Leave (<c>Leave:Close</c>) to Idle — never <c>OpenForEdit</c> —
    /// then the toast and (when the cluster asks to keep recordings) <i>Keep this recording?</i> — moved here from ■.
    /// 🔴 Mutations: Close calling <c>StopLive()</c> ⇒ <c>OpenForEdit</c>, no Leave; the keep question dropped from <c>AfterLiveEnded</c> ⇒ no Pending after Close.</summary>
    [Fact]
    public void R2_Eject_AsksToClose_ThenLeavesToIdle_AndKeepsTheRecording()
    {
        var (rig, ender) = RunningLive();
        rig.Choice.AskToKeep = () => true;
        End(rig, ender);
        Bar(rig).OnStop();
        var ask = rig.Choice.Pending!;
        Assert.Equal("Close the live session?", ask.Title);
        Assert.Equal("This exercise of hill-attack is kept (12:34). Every station returns to the Start screen.", ask.Text);
        Assert.Equal(new[] { "Close the session", RunModeConfirmation.CancelLabel }, ask.Buttons.Select(b => b.Label));
        ask.Resolve(ask.Buttons[1]);                                       // Cancel
        Assert.Empty(rig.C.Calls);
        Assert.True(rig.Choice.LiveEnded);

        Bar(rig).OnStop();
        rig.Choice.Pending!.Resolve(rig.Choice.Pending.Buttons[0]);
        Assert.Equal(new[] { "Leave:Close" }, rig.C.Calls);                // the door's leave to Idle, not an edit open
        Assert.Equal(RunId, rig.Choice.LastExercise);
        var keep = rig.Choice.Pending!;                                    // CE-4100's question now, at the Close step
        Assert.Equal("Keep this recording? (12:34, hill-attack)", keep.Text);
        keep.Resolve(keep.Buttons[0]);
        Assert.Equal("Exercise recorded (12:34) — open the AAR", rig.Choice.Toast!.Title);
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
        Assert.Equal("Close the live session?", close.Choice.Pending!.Title);

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
}
