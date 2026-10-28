using System;
using System.Linq;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.Toolkit.Orchestration;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Panels;
using Hrot.UI.Common.RunMode;

namespace Hrot.Presentation.Tests;

/// <summary>
/// Tests for <see cref="MainToolbarTimeControlSection"/> — headless logic seams
/// and gating behaviour, using a fake <see cref="ITimeTransportFacade"/>.
/// </summary>
[Xunit.Collection(Hrot.Presentation.Tests.ImGuiTestCollection.Name)]   // ⭐ one global ImGui context
public class MainToolbarTimeControlTests
{
    [Fact]
    public void PlayPause_Click_CallsTogglePlayPause()
    {
        var fake = new FakeTimeTransportFacade { IsPlayPauseEnabled = true };
        var section = new MainToolbarTimeControlSection(fake);

        section.OnPlayPause();

        Assert.Equal(1, fake.TogglePlayPauseCallCount);
    }

    [Fact]
    public void PlayPause_Click_WhenDisabled_NoOp()
    {
        var fake = new FakeTimeTransportFacade { IsPlayPauseEnabled = false };
        var section = new MainToolbarTimeControlSection(fake);

        section.OnPlayPause();

        Assert.Equal(0, fake.TogglePlayPauseCallCount);
    }

    [Fact]
    public void Step_Click_CallsStep_GatedByIsStepEnabled()
    {
        var fake = new FakeTimeTransportFacade { IsStepEnabled = true };
        var section = new MainToolbarTimeControlSection(fake);

        section.OnStep();

        Assert.Equal(1, fake.StepCallCount);
    }

    [Fact]
    public void Step_Click_WhenDisabled_NoOp()
    {
        var fake = new FakeTimeTransportFacade { IsStepEnabled = false };
        var section = new MainToolbarTimeControlSection(fake);

        section.OnStep();

        Assert.Equal(0, fake.StepCallCount);
    }

    [Fact]
    public void Stop_Click_CallsStop_GatedByIsStopEnabled()
    {
        var fake = new FakeTimeTransportFacade { IsStopEnabled = true };
        var section = new MainToolbarTimeControlSection(fake);

        section.OnStop();

        Assert.Equal(1, fake.StopCallCount);
    }

    [Fact]
    public void Stop_Click_WhenDisabled_NoOp()
    {
        var fake = new FakeTimeTransportFacade { IsStopEnabled = false };
        var section = new MainToolbarTimeControlSection(fake);

        section.OnStop();

        Assert.Equal(0, fake.StopCallCount);
    }

    [Fact]
    public void PlayPauseFace_ReflectsIsPaused()
    {
        // Paused → Play icon (so the user can resume).
        Assert.Equal(TransportIcons.BtnShape.Play,
            MainToolbarTimeControlSection.PlayPauseFace(isPaused: true));

        // Running → Pause icon (so the user can pause).
        Assert.Equal(TransportIcons.BtnShape.Pause,
            MainToolbarTimeControlSection.PlayPauseFace(isPaused: false));
    }

    [Fact]
    public void TimeText_FormatsTotalTime()
    {
        // 3661.234 seconds = 1h 1m 1s 234ms.
        Assert.Equal("01:01:01.234", TransportIcons.FormatTime(3661.234));

        // Exactly 2h 30m 45s = 9045 seconds.
        Assert.Equal("02:30:45.000", TransportIcons.FormatTime(9045.0));

        // Sub-second.
        Assert.Equal("00:00:00.500", TransportIcons.FormatTime(0.5));
    }

    [Fact]
    public void RateButton_OpensSelector_SetsTimeScale()
    {
        var fake = new FakeTimeTransportFacade();
        var section = new MainToolbarTimeControlSection(fake);

        section.OnSelectRate(2.0f);

        Assert.Equal(2.0f, fake.LastSetTimeScale);

        section.OnSelectRate(0.5f);

        Assert.Equal(0.5f, fake.LastSetTimeScale);
    }

    [Fact]
    public void Render_Headless_NoThrow()
    {
        using var fixture = new ImGuiTestFixture();
        fixture.NewFrame();

        var fake = new FakeTimeTransportFacade
        {
            IsPaused = true,
            IsPlayPauseEnabled = true,
            IsStepEnabled = true,
            IsStopEnabled = true,
            TotalTime = 3661.234,
            TimeScale = 1.5f,
        };

        var section = new MainToolbarTimeControlSection(fake);

        ImGuiNET.ImGui.Begin("test_toolbar", ImGuiNET.ImGuiWindowFlags.NoSavedSettings);
        ImGuiNET.ImGui.SetWindowSize(new System.Numerics.Vector2(600, 80));

        section.Render();

        ImGuiNET.ImGui.End();
        fixture.Render();
    }

    // ── ⭐ CE-3330 E3 — mode indicator, menu, ■ → ⏏, F15, the axis icon (DAR §9a) ─────────────────────────────

    private static FakeTimeTransportFacade In(TransportMode mode, ReplayAxis axis = ReplayAxis.Sim, double total = 0, double? livePoint = null,
                                              ReplaySession? session = null)
        => new() { Mode = mode, Axis = axis, TotalTime = total, LivePoint = livePoint, Session = session };

    [Theory]
    [InlineData(TransportMode.Edit,    Fdp.Presentation.Fonts.IconsFontAwesome6.Pen, 0.60f, 0.60f, 0.60f)]
    [InlineData(TransportMode.Preview, Fdp.Presentation.Fonts.IconsFontAwesome6.CircleHalfStroke, 0.66f, 0.45f, 0.92f)]
    [InlineData(TransportMode.Live,    Fdp.Presentation.Fonts.IconsFontAwesome6.Circle, 0.25f, 0.80f, 0.30f)]
    [InlineData(TransportMode.Review,  Fdp.Presentation.Fonts.IconsFontAwesome6.ClockRotateLeft, 1.00f, 0.70f, 0.20f)]
    [InlineData(TransportMode.Replay,  Fdp.Presentation.Fonts.IconsFontAwesome6.Play, 0.30f, 0.60f, 1.00f)]
    public void ModeGlyph_IsTheFontAwesomeGlyphAndColourPerMode(TransportMode mode, string glyph, float r, float g, float b)
    {
        var ind = MainToolbarTimeControlSection.ModeGlyph(mode)!;
        Assert.Equal(glyph, ind.Glyph);
        Assert.Equal(new System.Numerics.Vector4(r, g, b, 1f), ind.Color);
    }

    [Theory]
    [InlineData(TransportMode.Edit,    "")]   // Pen
    [InlineData(TransportMode.Preview, "")]   // CircleHalfStroke
    [InlineData(TransportMode.Live,    "")]   // Circle
    [InlineData(TransportMode.Review,  "")]   // ClockRotateLeft
    [InlineData(TransportMode.Replay,  "")]   // Play
    public void ModeIcon_IsTheFontAwesomeGlyphPerMode(TransportMode mode, string expected)
        => Assert.Equal(expected, MainToolbarTimeControlSection.ModeIcon(mode));

    [Fact]
    public void ModeIcon_IdleHasNone()
        => Assert.Null(MainToolbarTimeControlSection.ModeIcon(TransportMode.Idle));

    [Fact]
    public void ModeGlyph_IdleHasNone()
        => Assert.Null(MainToolbarTimeControlSection.ModeGlyph(TransportMode.Idle));

    [Theory]
    [InlineData(TransportMode.Edit)]
    [InlineData(TransportMode.Preview)]
    [InlineData(TransportMode.Replay)]
    [InlineData(TransportMode.Idle)]
    public void ModeMenu_HasNoMenuOutsideLiveAndReview(TransportMode mode)
        => Assert.Null(new MainToolbarTimeControlSection(In(mode)).ModeMenu());

    [Fact]
    public void LiveMenu_OffersTheReviewWindowsAndTheStart()
    {
        var menu = new MainToolbarTimeControlSection(In(TransportMode.Live, total: 754)).ModeMenu()!;
        Assert.Equal(new[] { "Review the last 30 s", "Review the last 2 min", "Review the last 10 min", "Review from the start" },
                     menu.Items.Select(i => i.Label).ToArray());
        Assert.StartsWith("Live", menu.Header);
    }

    [Fact]
    public void LiveMenu_Review2Min_EntersBySimMinus120_ClampedAtZero()
    {
        var fake = In(TransportMode.Live, total: 754);
        new MainToolbarTimeControlSection(fake).ModeMenu()!.Items[1].Invoke!();
        Assert.Equal(754 - 120, fake.LastEnterReviewAtSim);

        var young = In(TransportMode.Live, total: 50);
        new MainToolbarTimeControlSection(young).ModeMenu()!.Items[1].Invoke!();
        Assert.Equal(0d, young.LastEnterReviewAtSim);

        var start = In(TransportMode.Live, total: 754);
        new MainToolbarTimeControlSection(start).ModeMenu()!.Items[3].Invoke!();
        Assert.Equal(0d, start.LastEnterReviewAtSim);
    }

    private static ReplaySession SessionWithCheckpoint() => new()
    {
        Active = true, IsReview = true, ZeroWallTicks = 100 * TimeSpan.TicksPerSecond, LivePointSimSeconds = 600,
        Checkpoints = new[] { new CheckpointMark { Label = "cp-1", TotalTime = 42.5, WallTicks = 160 * TimeSpan.TicksPerSecond } },
    };

    [Fact]
    public void ReviewMenu_Items_HeaderAndBackToLive()
    {
        var fake = In(TransportMode.Review, total: 273, livePoint: 600, session: SessionWithCheckpoint());
        var menu = new MainToolbarTimeControlSection(fake).ModeMenu()!;
        Assert.Equal("Review · 5:27 behind live", menu.Header);
        Assert.Equal(new[] { "Restart from 30 s before live", "Restart from 2 min before live", "Restart from a checkpoint", "⏏ Back to live" },
                     menu.Items.Select(i => i.Label).ToArray());

        menu.Items[3].Invoke!();
        Assert.Equal(1, fake.ExitReviewCallCount);

        menu.Items[1].Invoke!();
        Assert.Equal(600 - 120, fake.LastSeek);
    }

    [Fact]
    public void ReviewMenu_WithoutALivePoint_OffersNoRestartFromLive()
    {
        var menu = new MainToolbarTimeControlSection(In(TransportMode.Review, livePoint: null)).ModeMenu()!;
        Assert.DoesNotContain(menu.Items, i => i.Label.StartsWith("Restart from 30"));
        Assert.False(menu.Items.Single(i => i.Label == "Restart from a checkpoint").Enabled);   // no checkpoints either
    }

    [Theory]
    [InlineData(ReplayAxis.Sim, 42.5)]    // Sim: the checkpoint's sim time
    [InlineData(ReplayAxis.Wall, 60.0)]   // Wall: seconds after the session's zero ((160 - 100) s)
    public void ReviewMenu_CheckpointSeeksToTheValueOnTheActiveAxis(ReplayAxis axis, double expected)
    {
        var fake = In(TransportMode.Review, axis, total: 10, livePoint: 600, session: SessionWithCheckpoint());
        var sub = new MainToolbarTimeControlSection(fake).ModeMenu()!.Items.Single(i => i.Label == "Restart from a checkpoint");
        Assert.True(sub.Enabled);
        var item = Assert.Single(sub.Children!);
        Assert.Equal("cp-1", item.Label);
        item.Invoke!();
        Assert.Equal(expected, fake.LastSeek);
    }

    [Fact]
    public void StopFace_InAReviewIsBackToLive_ElsewhereStop()
    {
        Assert.Equal(MainToolbarTimeControlSection.StopFaceKind.BackToLive, MainToolbarTimeControlSection.StopFace(TransportMode.Review));
        foreach (var m in new[] { TransportMode.Live, TransportMode.Replay, TransportMode.Preview, TransportMode.Edit, TransportMode.Idle })
            Assert.Equal(MainToolbarTimeControlSection.StopFaceKind.Stop, MainToolbarTimeControlSection.StopFace(m));
    }

    [Fact]
    public void StopTooltip_PerMode()
    {
        Assert.Equal("Stop the exercise", new MainToolbarTimeControlSection(In(TransportMode.Live)).StopTooltip());
        Assert.Equal("Leave the replay", new MainToolbarTimeControlSection(In(TransportMode.Replay)).StopTooltip());
        Assert.Equal("Back to edit", new MainToolbarTimeControlSection(In(TransportMode.Preview)).StopTooltip());
        Assert.Equal("Back to live — 5:27 ahead",
            new MainToolbarTimeControlSection(In(TransportMode.Review, total: 273, livePoint: 600)).StopTooltip());
    }

    [Fact]
    public void F15_StopIsIgnoredFor3SecondsAfterAReviewReturnsToLive_ThenStops()
    {
        var clock = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
        var fake = In(TransportMode.Review);
        var section = new MainToolbarTimeControlSection(fake, () => clock);
        section.Observe();                 // a frame in the review

        fake.Mode = TransportMode.Live;    // the return landed
        section.Observe();                 // the next frame sees it
        clock = clock.AddSeconds(2.9);
        Assert.Contains("Just returned", section.StopTooltip());
        section.OnStop();
        Assert.Equal(0, fake.StopCallCount);   // the second press of a double press: ignored

        clock = clock.AddSeconds(0.2);     // 3.1 s after the return
        Assert.Equal("Stop the exercise", section.StopTooltip());
        section.OnStop();
        Assert.Equal(1, fake.StopCallCount);
    }

    [Fact]
    public void F15_AStopInLiveThatFollowsNoReview_IsNeverGuarded()
    {
        var fake = In(TransportMode.Live);
        var section = new MainToolbarTimeControlSection(fake, () => DateTime.UnixEpoch);
        section.Observe();
        section.OnStop();
        Assert.Equal(1, fake.StopCallCount);
    }

    [Fact]
    public void AxisToggle_CallsSetAxisWithTheOtherAxis_AndIsDisabledInEdit()
    {
        var live = In(TransportMode.Live, ReplayAxis.Sim);
        var s = new MainToolbarTimeControlSection(live);
        Assert.Equal("S", MainToolbarTimeControlSection.AxisFace(ReplayAxis.Sim));
        Assert.Equal("W", MainToolbarTimeControlSection.AxisFace(ReplayAxis.Wall));
        Assert.Equal("Sim time — pauses skipped", MainToolbarTimeControlSection.AxisTooltip(ReplayAxis.Sim));
        Assert.Equal("Wall time — pauses shown", MainToolbarTimeControlSection.AxisTooltip(ReplayAxis.Wall));
        Assert.True(s.IsAxisEnabled);
        s.OnToggleAxis();
        Assert.Equal(ReplayAxis.Wall, live.Axis);
        s.OnToggleAxis();
        Assert.Equal(ReplayAxis.Sim, live.Axis);

        foreach (var m in new[] { TransportMode.Edit, TransportMode.Preview, TransportMode.Idle })
        {
            var f = In(m, ReplayAxis.Sim);
            var sec = new MainToolbarTimeControlSection(f);
            Assert.False(sec.IsAxisEnabled);
            sec.OnToggleAxis();
            Assert.Equal(ReplayAxis.Sim, f.Axis);
        }
    }

    [Theory]
    [InlineData(TransportMode.Edit)]
    [InlineData(TransportMode.Live)]
    [InlineData(TransportMode.Review)]
    [InlineData(TransportMode.Replay)]
    public void Render_EachMode_NoThrow(TransportMode mode)
    {
        using var fixture = new ImGuiTestFixture();
        fixture.NewFrame();
        var fake = In(mode, total: 12, livePoint: 600, session: SessionWithCheckpoint());
        var section = new MainToolbarTimeControlSection(fake);
        ImGuiNET.ImGui.Begin("test_toolbar_modes", ImGuiNET.ImGuiWindowFlags.NoSavedSettings);
        ImGuiNET.ImGui.SetWindowSize(new System.Numerics.Vector2(700, 80));
        section.Render();
        ImGuiNET.ImGui.End();
        fixture.Render();
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // ⭐⭐⭐ CE-4097 (AQ108 §2.8) — the toolbar over the run choice: the indicator, the menu's moves with their confirmations, ■ = the facade's Stop.
    // Real adapter + real bus + a run choice (RunChoiceRig); the model is asserted without ImGui, the draw only for "does not throw".
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════

    private static MainToolbarTimeControlSection Bar(RunChoiceRig.Rig rig) => new(rig.F, choice: rig.Choice);

    private static RunArmed Armed(RunChoiceRig.Rig rig) => rig.Choice.Armed;

    private static ClusterState StateOf(Hrot.UI.Common.Facades.TransportMode mode) => mode switch
    {
        TransportMode.Edit => ClusterState.OperatingEdit, TransportMode.Preview => ClusterState.OperatingPreview, TransportMode.Live => ClusterState.OperatingLive,
        TransportMode.Review => ClusterState.OperatingReview, _ => ClusterState.OperatingReplay,
    };

    /// <summary>
    /// ⭐ AQ108 D1 / §1a — the indicator is DIM before a run (the armed mode, what ▶ will start), BLINKS while a run plays, is BRIGHT and steady while it
    /// is paused; the words are the user's (Preview / Live / DAR / AAR).
    /// 🔴 Red-proof: <c>Indicator</c>'s level swapped (paused ⇒ Blink) / a word changed.
    /// </summary>
    [Theory]
    [InlineData(TransportMode.Edit,    RunArmed.Preview, false, "Preview", IndicatorLevel.Dim)]
    [InlineData(TransportMode.Edit,    RunArmed.Live,    false, "Live",    IndicatorLevel.Dim)]
    [InlineData(TransportMode.Edit,    RunArmed.AAR,     false, "AAR",     IndicatorLevel.Dim)]
    [InlineData(TransportMode.Preview, RunArmed.Preview, true,  "Preview", IndicatorLevel.Blink)]
    [InlineData(TransportMode.Preview, RunArmed.Preview, false, "Preview", IndicatorLevel.Bright)]
    [InlineData(TransportMode.Live,    RunArmed.Live,    true,  "Live",    IndicatorLevel.Blink)]
    [InlineData(TransportMode.Live,    RunArmed.Live,    false, "Live",    IndicatorLevel.Bright)]
    [InlineData(TransportMode.Review,  RunArmed.Live,    true,  "DAR",     IndicatorLevel.Blink)]
    [InlineData(TransportMode.Replay,  RunArmed.AAR,     false, "AAR",     IndicatorLevel.Bright)]
    public void CE4097_TheIndicator_IsDimInEdit_BlinksWhilePlaying_SteadyWhilePaused_WithTheUsersWords(
        TransportMode mode, RunArmed host, bool running, string word, IndicatorLevel level)
    {
        var rig = RunChoiceRig.With(StateOf(mode), host == RunArmed.AAR ? RunArmed.Preview : host, running: running);
        if (host == RunArmed.AAR && mode == TransportMode.Edit) rig.Choice.Choose(RunArmed.AAR);
        var ind = rig.Choice.Indicator;
        Assert.Equal(word, ind.Word);
        Assert.Equal(level, ind.Level);
        Assert.False(string.IsNullOrEmpty(ind.Glyph));
    }

    /// <summary>⭐ CE-4090 / D14 — Live is RED when runs are recorded, GREEN when not: in a run from the run's own flag, before one (armed) from the setting.
    /// 🔴 Red-proof: the colours swapped in <c>Of(Live)</c>.</summary>
    [Fact]
    public void CE4097_TheLiveIndicator_IsRedWhenRecording_GreenWhenNot()
    {
        static bool Red(RunChoiceRig.Rig r) { var c = r.Choice.Indicator.Color; return c.X > 0.8f && c.Y < 0.4f; }
        static bool Green(RunChoiceRig.Rig r) { var c = r.Choice.Indicator.Color; return c.Y > 0.7f && c.X < 0.4f; }

        Assert.True(Red(RunChoiceRig.With(ClusterState.OperatingLive, RunArmed.Live)));
        Assert.True(Green(RunChoiceRig.With(ClusterState.OperatingLive, RunArmed.Live, runNotRecorded: true)));
        Assert.True(Red(RunChoiceRig.With(ClusterState.OperatingEdit, RunArmed.Live)));                                   // armed: the setting is ON
        Assert.True(Green(RunChoiceRig.With(ClusterState.OperatingEdit, RunArmed.Live, recordLiveRunsOff: true)));        // armed: the setting is OFF
    }

    /// <summary>The blink pulses between 0.3 and 1; dim and bright are steady.</summary>
    [Fact]
    public void CE4097_TheIndicatorAlpha_DimAndBrightAreSteady_BlinkPulses()
    {
        Assert.Equal(RunModeIndicator.Alpha(IndicatorLevel.Dim, 0), RunModeIndicator.Alpha(IndicatorLevel.Dim, 5.3));
        Assert.Equal(1f, RunModeIndicator.Alpha(IndicatorLevel.Bright, 1.7));
        var samples = Enumerable.Range(0, 100).Select(i => RunModeIndicator.Alpha(IndicatorLevel.Blink, i * 0.01)).ToList();
        Assert.True(samples.Max() > 0.95f && samples.Min() < 0.4f, $"blink must pulse (min {samples.Min()}, max {samples.Max()})");
        Assert.True(RunModeIndicator.Alpha(IndicatorLevel.Dim, 0) < RunModeIndicator.Alpha(IndicatorLevel.Bright, 0));
    }

    /// <summary>
    /// ⭐ AQ108 §3 / D10 — the moves that START from the current state, each with its exact confirmation: Live running → Preview / AAR; Preview running →
    /// Live / AAR; AAR → Live / Preview; Edit → all three with none (R3); a DAR has none of its own here. A live run that was not recorded cannot open AAR.
    /// 🔴 Red-proof: any one text edited / a confirmation dropped from a move.
    /// </summary>
    [Fact]
    public void CE4097_TheMenusMoves_AndTheirConfirmationTexts_PerAQ108Section3()
    {
        const string previewStop = "Stops the preview and returns to the scenario's start";

        var live = RunChoiceRig.With(ClusterState.OperatingLive, RunArmed.Live).Choice.Moves();
        Assert.Equal("Ends the live session; you return to editing the scenario at its start", live.Single(m => m.Target == RunArmed.Preview).ConfirmText);
        Assert.Equal("Ends the live session; its recording opens for AAR", live.Single(m => m.Target == RunArmed.AAR).ConfirmText);
        Assert.True(live.Single(m => m.Target == RunArmed.Live).IsArmed);
        Assert.Null(live.Single(m => m.Target == RunArmed.Live).ConfirmText);   // the current mode is no move

        var notRecorded = RunChoiceRig.With(ClusterState.OperatingLive, RunArmed.Live, runNotRecorded: true).Choice.Moves().Single(m => m.Target == RunArmed.AAR);
        Assert.False(notRecorded.Enabled);
        Assert.Equal("this run was not recorded", notRecorded.DisabledReason);

        var preview = RunChoiceRig.With(ClusterState.OperatingPreview, RunArmed.Preview).Choice.Moves();
        Assert.Equal(previewStop, preview.Single(m => m.Target == RunArmed.Live).ConfirmText);
        Assert.Equal(previewStop, preview.Single(m => m.Target == RunArmed.AAR).ConfirmText);

        var replay = RunChoiceRig.With(ClusterState.OperatingReplay, RunArmed.AAR, clock: 424).Choice.Moves();
        Assert.Equal("A new exercise starts from 7:04; this recording stays unchanged", replay.Single(m => m.Target == RunArmed.Live).ConfirmText);
        Assert.Equal("Ends the AAR; you return to editing hill-attack at its start", replay.Single(m => m.Target == RunArmed.Preview).ConfirmText);

        var edit = RunChoiceRig.With(ClusterState.OperatingEdit, RunArmed.Preview).Choice.Moves();
        Assert.Equal(3, edit.Count);
        Assert.All(edit, m => { Assert.Null(m.ConfirmText); Assert.True(m.Enabled); });

        Assert.Empty(RunChoiceRig.With(ClusterState.OperatingReview, RunArmed.Live).Choice.Moves());
    }

    /// <summary>
    /// ⭐ R3 + R1 — a move that ends something ASKS first and changes nothing until its button is pressed (Cancel leaves everything); a move from Edit and
    /// ■ never ask. Each confirmed move does exactly its §3 action: Live → Preview = <c>OpenForEdit(the run's scenario)</c>; Live → AAR = <c>OpenRecording(the run)</c>;
    /// Preview running → Live = the dry run's exit, Live armed; AAR → Live = the branch intent; AAR → Preview = <c>OpenForEdit(the recording's scenario)</c>.
    /// 🔴 Red-proof: <c>Choose</c> arming at once without the confirmation / the confirmed action dropped.
    /// </summary>
    [Fact]
    public void CE4097_AMoveThatEndsARun_AsksFirst_AndOnlyItsButtonActs()
    {
        // Live → Preview
        var live = RunChoiceRig.With(ClusterState.OperatingLive, RunArmed.Live);
        live.Choice.Choose(RunArmed.Preview);
        var ask = live.Choice.Pending!;
        Assert.Equal("Ends the live session; you return to editing the scenario at its start", ask.Text);
        Assert.Equal("Cancel", ask.Buttons[^1].Label);
        Assert.Equal(RunArmed.Live, live.Choice.Armed);                     // nothing yet (R1: Armed changes only when the move happens)
        Assert.Empty(live.C.Calls);
        ask.Resolve(ask.Buttons[^1]);
        Assert.Empty(live.C.Calls); Assert.Equal(RunArmed.Live, live.Choice.Armed); Assert.Null(live.Choice.Pending);
        live.Choice.Choose(RunArmed.Preview);
        live.Choice.Pending!.Resolve(live.Choice.Pending.Buttons[0]);
        Assert.Equal(new[] { "OpenForEdit:hill-attack" }, live.C.Calls);
        Assert.Equal(RunArmed.Preview, live.Choice.Armed);
        Assert.Equal(RunChoiceRig.RunId, live.Choice.LastExercise);

        // Live → AAR
        var toAar = RunChoiceRig.With(ClusterState.OperatingLive, RunArmed.Live);
        toAar.Choice.Choose(RunArmed.AAR);
        toAar.Choice.Pending!.Resolve(toAar.Choice.Pending.Buttons[0]);
        Assert.Equal(new[] { $"OpenRecording:{RunChoiceRig.RunId}" }, toAar.C.Calls);
        Assert.Equal(RunArmed.AAR, toAar.Choice.Armed);

        // Preview running → Live
        var pv = RunChoiceRig.With(ClusterState.OperatingPreview, RunArmed.Preview);
        pv.Choice.Choose(RunArmed.Live);
        Assert.Equal(RunArmed.Preview, pv.Choice.Armed);
        Assert.Empty(RunChoiceRig.Sent<TransitionStateIntent>(pv));
        pv.Choice.Pending!.Resolve(pv.Choice.Pending.Buttons[0]);
        Assert.Equal(ClusterState.OperatingEdit, RunChoiceRig.Sent<TransitionStateIntent>(pv).Single().TargetState);   // the dry run's exit
        Assert.Equal(RunArmed.Live, pv.Choice.Armed);

        // AAR → Live (the branch) and AAR → Preview
        var branch = RunChoiceRig.With(ClusterState.OperatingReplay, RunArmed.AAR);
        branch.Choice.Choose(RunArmed.Live);
        branch.Choice.Pending!.Resolve(branch.Choice.Pending.Buttons[0]);
        Assert.Equal(ClusterState.OperatingLive, RunChoiceRig.Sent<TransitionStateIntent>(branch).Single().TargetState);
        Assert.Equal(RunArmed.Live, branch.Choice.Armed);
        var edit = RunChoiceRig.With(ClusterState.OperatingReplay, RunArmed.AAR);
        edit.Choice.Choose(RunArmed.Preview);
        edit.Choice.Pending!.Resolve(edit.Choice.Pending.Buttons[0]);
        Assert.Equal(new[] { "OpenForEdit:hill-attack" }, edit.C.Calls);

        // R3: from Edit nothing asks; ■ in Live never asks
        var rest = RunChoiceRig.With(ClusterState.OperatingEdit, RunArmed.Preview);
        rest.Choice.Choose(RunArmed.Live);
        Assert.Null(rest.Choice.Pending);
        Assert.Equal(RunArmed.Live, rest.Choice.Armed);
        var stop = RunChoiceRig.With(ClusterState.OperatingLive, RunArmed.Live);
        stop.F.Stop();
        Assert.Null(stop.Choice.Pending);
    }

    /// <summary>A confirmation resolves once: a second press is ignored (no second action), a foreign button is refused.</summary>
    [Fact]
    public void CE4097_AConfirmationResolvesOnce()
    {
        var rig = RunChoiceRig.With(ClusterState.OperatingLive, RunArmed.Live);
        rig.Choice.Choose(RunArmed.Preview);
        var ask = rig.Choice.Pending!;
        ask.Resolve(ask.Buttons[0]);
        ask.Resolve(ask.Buttons[0]);
        Assert.Single(rig.C.Calls);
        Assert.Throws<ArgumentException>(() => ask.Resolve(new RunModeConfirmation.Button("Other", null)));
    }

    /// <summary>
    /// ⭐ AQ108 D22 / success 3 — the toolbar's ■ and <c>facade.Stop()</c> (what <c>POST /sim/stop</c> ends in) are ONE rail: the same session calls and the same
    /// intents, in every state ■ works from.
    /// 🔴 Red-proof: <c>ClusterTimeTransportAdapter.Stop</c> bypassing the choice ⇒ the Live row publishes Idle instead of <c>OpenForEdit</c>.
    /// </summary>
    [Theory]
    [InlineData(TransportMode.Live)]
    [InlineData(TransportMode.Preview)]
    [InlineData(TransportMode.Review)]
    [InlineData(TransportMode.Replay)]
    public void CE4097_TheToolbarsStop_AndTheFacadesStop_AreTheSameCalls(TransportMode mode)
    {
        var viaFacade = RunChoiceRig.With(StateOf(mode), RunArmed.Live);
        var viaToolbar = RunChoiceRig.With(StateOf(mode), RunArmed.Live);

        viaFacade.F.Stop();
        Bar(viaToolbar).OnStop();

        Assert.Equal(viaFacade.C.Calls, viaToolbar.C.Calls);
        Assert.Equal(RunChoiceRig.Sent<TransitionStateIntent>(viaFacade).Select(i => i.TargetState),
                     RunChoiceRig.Sent<TransitionStateIntent>(viaToolbar).Select(i => i.TargetState));
        Assert.Equal(viaFacade.Choice.LastExercise, viaToolbar.Choice.LastExercise);
        if (mode == TransportMode.Live) Assert.Equal(new[] { "OpenForEdit:hill-attack" }, viaToolbar.C.Calls);   // and it is R2, not a plain Idle
    }

    /// <summary>
    /// ⭐ The section's menu with a choice: Edit offers the three modes (the armed one selected, choosing one arms it); a live run keeps the DAR windows AND adds the
    /// moves (the confirmation opens as the pending dialog); without a choice the menu is the pre-4097 one (no menu in Edit).
    /// </summary>
    [Fact]
    public void CE4097_TheSectionsMenu_IsTheChoicesMoves()
    {
        var rig = RunChoiceRig.With(ClusterState.OperatingEdit, RunArmed.Preview);
        var menu = Bar(rig).ModeMenu()!;
        Assert.Equal("▶ will start: Preview", menu.Header);
        Assert.Equal(new[] { "Preview", "Live", "AAR" }, menu.Items.Select(i => i.Label));
        Assert.Equal(new[] { true, false, false }, menu.Items.Select(i => i.Selected));
        menu.Items.Single(i => i.Label == "Live").Invoke!();
        Assert.Equal(RunArmed.Live, Armed(rig));
        Assert.True(Bar(rig).ModeMenu()!.Items.Single(i => i.Label == "Live").Selected);

        var live = RunChoiceRig.With(ClusterState.OperatingLive, RunArmed.Live, clock: 90);
        var liveMenu = Bar(live).ModeMenu()!;
        Assert.Contains(liveMenu.Items, i => i.Label.StartsWith("Review the last 30 s"));   // the DAR windows stay
        liveMenu.Items.Single(i => i.Label == "AAR").Invoke!();
        Assert.Equal("Ends the live session; its recording opens for AAR", live.Choice.Pending!.Text);

        Assert.Null(new MainToolbarTimeControlSection(RunChoiceRig.With(ClusterState.OperatingEdit).F).ModeMenu());   // the section built WITHOUT a choice: as before
        // a DAR keeps its own menu: ⏏ Back to live is still there, no move was added
        var review = RunChoiceRig.With(ClusterState.OperatingReview, RunArmed.Live);
        Assert.Contains(Bar(review).ModeMenu()!.Items, i => i.Label == "⏏ Back to live");
        Assert.DoesNotContain(Bar(review).ModeMenu()!.Items, i => i.Label == "AAR");
    }

    /// <summary>The ▶ tooltip carries the reason when Live is armed on a host with no session (§2.8 ⑥) — and the ▶ button's enabled state is the facade's.</summary>
    [Fact]
    public void CE4097_PlayOnAHostWithNoSession_IsDisabledAtTheToolbar()
    {
        var rig = RunChoiceRig.With(ClusterState.OperatingEdit, RunArmed.Live, withControl: false);
        Assert.False(rig.F.IsPlayPauseEnabled);
        Bar(rig).OnPlayPause();
        Assert.Empty(RunChoiceRig.Sent<TransitionStateIntent>(rig));
        Assert.Empty(rig.C.Calls);
    }

    /// <summary>The draw with a pending confirmation and a toast does not throw (the modal and the note render headless).</summary>
    [Fact]
    public void CE4097_Render_WithAPendingConfirmationAndAToast_NoThrow()
    {
        using var fixture = new ImGuiTestFixture();
        fixture.NewFrame();
        var rig = RunChoiceRig.With(ClusterState.OperatingEdit, RunArmed.Live, UnsavedAnswer.Changed);
        rig.F.TogglePlayPause();                                            // → pending "Save and go live / Cancel"
        Assert.NotNull(rig.Choice.Pending);
        var done = RunChoiceRig.With(ClusterState.OperatingLive, RunArmed.Live, clock: 12);
        done.F.Stop();                                                      // → the toast
        Assert.NotNull(done.Choice.Toast);

        foreach (var r in new[] { rig, done })
        {
            var section = Bar(r);
            ImGuiNET.ImGui.Begin("test_toolbar_choice_" + ReferenceEquals(r, rig), ImGuiNET.ImGuiWindowFlags.NoSavedSettings);
            ImGuiNET.ImGui.SetWindowSize(new System.Numerics.Vector2(900, 80));
            section.Render();
            ImGuiNET.ImGui.End();
        }
        fixture.Render();
    }
}
