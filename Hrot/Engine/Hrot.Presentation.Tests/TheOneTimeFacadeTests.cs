using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Time.Domain;
using Fdp.Toolkit.Time.Messages;
using Hrot.UI.Common.Adapters;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.RunMode;
using static Hrot.Presentation.Tests.RunChoiceRig;
using Xunit;

namespace Hrot.Presentation.Tests;

/// <summary>
/// ⭐⭐⭐ CE-3329 row 21 (user, `2026-10-26`: <i>"One facade."</i>) — <see cref="ClusterTimeTransportAdapter"/> is THE time facade on every
/// host, the editor included: Play / Step / Stop of a dry run are the cluster's transition, ■ in a review returns to live (R-3060), and the
/// session members publish the intents the debug HTTP routes publish (<see cref="ReplaySessionIntents"/>). Also the replay rails of the
/// retired <c>EditorTimeTransportFacade</c> (CE-4051 / CE-4052): the readout is the master's clock and a step reaches the master.
/// 📄 docs/DESIGN_Replay_Time_Axis_And_Review.md §12m.
/// </summary>
public sealed class TheOneTimeFacadeTests
{
    private static (ClusterTimeTransportAdapter Facade, FdpEventBus Bus) In(ClusterState state, ReplayAxis axis = ReplayAxis.Sim,
                                                                           ReplaySession? session = null, double clock = 0, bool running = false)
    {
        var bus = new FdpEventBus();
        var facade = new ClusterTimeTransportAdapter(bus, () => clock);
        bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = state, ReplayAxis = axis });
        if (session != null) bus.PublishManaged(new ReplaySessionUpdateEvent { Session = session });
        bus.Publish(new SwitchTimeModeEvent { TargetMode = running ? Fdp.ModuleHost.Time.TimeMode.Continuous : Fdp.ModuleHost.Time.TimeMode.Deterministic });   // paused unless `running`
        bus.SwapBuffers();
        facade.Update();
        bus.SwapBuffers();   // drop the setup; what the facade publishes next is all the read buffer holds
        return (facade, bus);
    }

    private static T Published<T>(FdpEventBus bus)
    {
        bus.SwapBuffers();
        var all = bus.ReadManaged<T>().ToList();
        Assert.True(all.Count == 1, $"expected exactly one {typeof(T).Name}, saw {all.Count}");
        return all[0];
    }

    [Fact]
    public void PlayFromEdit_IsTheDryRun_Running_StepFromEdit_IsTheDryRun_Paused()
    {
        var (facade, bus) = In(ClusterState.OperatingEdit);
        facade.TogglePlayPause();
        var play = Published<TransitionStateIntent>(bus);
        Assert.Equal(ClusterState.OperatingPreview, play.TargetState);
        Assert.Equal("Continuous", play.TimeMode);

        var (stepper, bus2) = In(ClusterState.OperatingEdit);
        stepper.Step();
        var step = Published<TransitionStateIntent>(bus2);
        Assert.Equal(ClusterState.OperatingPreview, step.TargetState);
        Assert.Equal("Deterministic", step.TimeMode);
    }

    /// <summary>
    /// ⭐ <c>CE-4054</c> (second half) — Play / Pause in a LIVE run, a replay or a review drive the CLUSTER'S clock (resume / pause intent) and
    /// NEVER start a dry run on top of the run; only Edit's Play is the dry run (the test above). 📄 docs/DESIGN_Replay_Time_Axis_And_Review.md §9a.
    /// 🔴 Red-proof: the Edit guard in <c>TogglePlayPause</c> widened to every state but Idle ⇒ these rows publish a dry-run Enter.
    /// </summary>
    [Theory]
    [InlineData(ClusterState.OperatingLive)]
    [InlineData(ClusterState.OperatingReplay)]
    [InlineData(ClusterState.OperatingReview)]
    public void PlayAndPause_InARun_DriveTheClusterClock_NeverTheDryRun(ClusterState state)
    {
        var (paused, pausedBus) = In(state);
        paused.TogglePlayPause();
        pausedBus.SwapBuffers();
        Assert.Empty(pausedBus.ReadManaged<TransitionStateIntent>());   // no dry run entered on top of the run
        Assert.Single(pausedBus.ReadManaged<ResumeTimeIntent>());

        var (running, runningBus) = In(state, running: true);
        running.TogglePlayPause();
        runningBus.SwapBuffers();
        Assert.Empty(runningBus.ReadManaged<TransitionStateIntent>());
        Assert.Single(runningBus.ReadManaged<PauseTimeIntent>());
    }

    [Theory]
    [InlineData(ClusterState.OperatingPreview, ClusterState.OperatingEdit)]   // a dry run goes back to edit
    [InlineData(ClusterState.OperatingReview,  ClusterState.OperatingLive)]   // R-3060 — ■ in a review = back to live
    [InlineData(ClusterState.OperatingLive,    ClusterState.Idle)]
    [InlineData(ClusterState.OperatingReplay,  ClusterState.Idle)]
    public void Stop_LeavesWhatRuns(ClusterState from, ClusterState to)
    {
        var (facade, bus) = In(from);
        Assert.True(facade.IsStopEnabled);
        facade.Stop();
        Assert.Equal(to, Published<TransitionStateIntent>(bus).TargetState);
    }

    [Fact]
    public void Stop_IsNotOfferedInEdit_LeavingEditIsUnloading()
        => Assert.False(In(ClusterState.OperatingEdit).Facade.IsStopEnabled);

    [Theory]
    [InlineData(ClusterState.OperatingEdit,    TransportMode.Edit)]
    [InlineData(ClusterState.OperatingPreview, TransportMode.Preview)]
    [InlineData(ClusterState.OperatingLive,    TransportMode.Live)]
    [InlineData(ClusterState.OperatingReview,  TransportMode.Review)]
    [InlineData(ClusterState.LoadingReview,    TransportMode.Review)]
    [InlineData(ClusterState.OperatingReplay,  TransportMode.Replay)]
    [InlineData(ClusterState.Idle,             TransportMode.Idle)]
    public void Mode_FollowsTheClusterState(ClusterState state, TransportMode mode)
        => Assert.Equal(mode, In(state).Facade.Mode);

    [Fact]
    public void EnterReview_AtTheLivePoint_NamesNoMoment()
    {
        var (facade, bus) = In(ClusterState.OperatingLive);
        facade.EnterReview();
        var intent = Published<TransitionStateIntent>(bus);
        Assert.Equal(ClusterState.OperatingReview, intent.TargetState);
        Assert.Equal(0, intent.TargetWallTicks);
        Assert.Null(intent.TargetSimSeconds);
    }

    [Fact]
    public void EnterReview_AndSeek_MeanTheActiveAxis()
    {
        var (sim, simBus) = In(ClusterState.OperatingLive, ReplayAxis.Sim);
        sim.EnterReview(12.5);
        var simEntry = Published<TransitionStateIntent>(simBus);
        Assert.Equal(12.5, simEntry.TargetSimSeconds);
        Assert.Equal(0, simEntry.TargetWallTicks);

        var (wall, wallBus) = In(ClusterState.OperatingReview, ReplayAxis.Wall);
        wall.Seek(3.0);
        var seek = Published<SeekReplayIntent>(wallBus);
        Assert.Null(seek.TargetSimSeconds);
        Assert.Equal(3 * TimeSpan.TicksPerSecond, seek.TargetWallTicks);

        var (simSeeker, simSeekBus) = In(ClusterState.OperatingReplay, ReplayAxis.Sim);
        simSeeker.Seek(7.25);
        Assert.Equal(7.25, Published<SeekReplayIntent>(simSeekBus).TargetSimSeconds);
    }

    /// <summary>⭐ CE-3330 E3 — a review from LIVE goes by the run's sim seconds (no session, no wall zero there), on either axis.</summary>
    [Theory]
    [InlineData(ReplayAxis.Sim)]
    [InlineData(ReplayAxis.Wall)]
    public void EnterReviewAtSim_PublishesAReviewEntryWithTheSimTarget_OnEitherAxis(ReplayAxis axis)
    {
        var (facade, bus) = In(ClusterState.OperatingLive, axis);
        facade.EnterReviewAtSim(95.5);
        var intent = Published<TransitionStateIntent>(bus);
        Assert.Equal(ClusterState.OperatingReview, intent.TargetState);
        Assert.Equal(95.5, intent.TargetSimSeconds);
        Assert.Equal(0, intent.TargetWallTicks);
    }

    [Fact]
    public void TheFacadeExposesTheSessionLayer()
    {
        var session = new ReplaySession { Active = true, IsReview = true };
        Assert.Same(session, In(ClusterState.OperatingReview, ReplayAxis.Sim, session).Facade.Session);
        Assert.Null(In(ClusterState.OperatingLive).Facade.Session);
    }

    [Fact]
    public void ExitReview_AndSetAxis_AreTheMastersIntents()
    {
        var (facade, bus) = In(ClusterState.OperatingReview);
        facade.ExitReview();
        Assert.Equal(ClusterState.OperatingLive, Published<TransitionStateIntent>(bus).TargetState);

        var (axisFacade, axisBus) = In(ClusterState.OperatingReplay);
        axisFacade.SetAxis(ReplayAxis.Wall);
        Assert.Equal(ReplayAxis.Wall, Published<SetReplayAxisIntent>(axisBus).Axis);
    }

    /// <summary>⭐ CE-3329 §12n — a loop is asked of the master in the ACTIVE axis's unit: sim bounds on Sim (no wall bounds), wall bounds
    /// on Wall (no sim bounds); clearing is the all-null request. The facade holds nothing — the master does.</summary>
    [Fact]
    public void SetLoop_AndClearLoop_AreTheMastersIntents_OnTheActiveAxis()
    {
        var (sim, simBus) = In(ClusterState.OperatingReplay, ReplayAxis.Sim);
        sim.SetLoop(2, 5);
        var onSim = Published<SetReplayLoopIntent>(simBus);
        Assert.Equal(2, onSim.FromSimSeconds); Assert.Equal(5, onSim.ToSimSeconds);
        Assert.Null(onSim.FromSeconds); Assert.Null(onSim.ToSeconds);

        var (wall, wallBus) = In(ClusterState.OperatingReplay, ReplayAxis.Wall);
        wall.SetLoop(2, 5);
        var onWall = Published<SetReplayLoopIntent>(wallBus);
        Assert.Equal(2, onWall.FromSeconds); Assert.Equal(5, onWall.ToSeconds);
        Assert.Null(onWall.FromSimSeconds); Assert.Null(onWall.ToSimSeconds);

        var (clearer, clearBus) = In(ClusterState.OperatingReplay, ReplayAxis.Sim);
        clearer.ClearLoop();
        var cleared = Published<SetReplayLoopIntent>(clearBus);
        Assert.Null(cleared.FromSeconds); Assert.Null(cleared.ToSeconds);
        Assert.Null(cleared.FromSimSeconds); Assert.Null(cleared.ToSimSeconds);
    }

    /// <summary>⭐ CE-3329 §12n — <c>Loop</c> reads the session's bounds (wall seconds after the zero): as they are on Wall, through the
    /// session's map on Sim, and as wall when there is no sim axis; nothing without a loop or a session.</summary>
    [Fact]
    public void Loop_IsTheSessionsBounds_OnTheActiveAxis()
    {
        const long zero = 1_000_000L;
        var map = Fdp.Core.FlightRecorder.ReplayTimeMap.FromFrames(
            new[] { zero, zero + 10 * TimeSpan.TicksPerSecond }, new[] { 0.0, 5.0 },     // sim runs at half the wall's pace
            new[] { Fdp.Core.FlightRecorder.FrameFlags.None, Fdp.Core.FlightRecorder.FrameFlags.None });
        Assert.True(map.HasSimTime);
        var session = new ReplaySession { Active = true, ZeroWallTicks = zero, TimeMap = map, LoopFromSeconds = 2, LoopToSeconds = 4 };

        Assert.Equal((2.0, 4.0), In(ClusterState.OperatingReplay, ReplayAxis.Wall, session).Facade.Loop);
        var onSim = In(ClusterState.OperatingReplay, ReplayAxis.Sim, session).Facade.Loop!.Value;
        Assert.Equal(1.0, onSim.From, 6); Assert.Equal(2.0, onSim.To, 6);

        var noMap = new ReplaySession { Active = true, ZeroWallTicks = zero, LoopFromSeconds = 2, LoopToSeconds = 4 };
        Assert.Equal((2.0, 4.0), In(ClusterState.OperatingReplay, ReplayAxis.Sim, noMap).Facade.Loop);   // v6: the wall values

        Assert.Null(In(ClusterState.OperatingReplay, ReplayAxis.Sim, new ReplaySession { Active = true, ZeroWallTicks = zero, TimeMap = map }).Facade.Loop);
        Assert.Null(In(ClusterState.OperatingReplay, ReplayAxis.Sim).Facade.Loop);                       // no session yet
    }

    [Fact]
    public void LivePoint_IsTheSessionsOnTheActiveAxis_AndNothingOutsideAReview()
    {
        var review = new ReplaySession { Active = true, IsReview = true, LivePointSimSeconds = 31.4, LivePointSeconds = 38.2 };
        Assert.Equal(31.4, In(ClusterState.OperatingReview, ReplayAxis.Sim, review).Facade.LivePoint);
        Assert.Equal(38.2, In(ClusterState.OperatingReview, ReplayAxis.Wall, review).Facade.LivePoint);

        var replay = new ReplaySession { Active = true, IsReview = false };
        Assert.Null(In(ClusterState.OperatingReplay, ReplayAxis.Sim, replay).Facade.LivePoint);
        Assert.Null(In(ClusterState.OperatingLive).Facade.LivePoint);
    }

    /// <summary>The retired editor facade's replay rails (CE-4051 / CE-4052), on the one facade: the readout is the clock it is given (the
    /// master's), not a recorded frame's, and a step while paused is a step intent for the master — not another pause.</summary>
    [Fact]
    public void InAReplay_TheReadoutIsTheMastersClock_AndAPausedStepReachesTheMaster()
    {
        var (facade, bus) = In(ClusterState.OperatingReplay, clock: 42.5);
        Assert.Equal(42.5, facade.TotalTime, 6);
        Assert.True(facade.IsPaused);
        facade.StepBy(1f / 60f);
        Assert.Equal(1f / 60f, Published<StepTimeIntent>(bus).DeltaSeconds, 5);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // ⭐⭐⭐ CE-4097 (AQ108 §2.8) — the run choice: ▶ / ■ from Edit follow the ARMED mode (R1–R3), through the ONE facade.
    // The scenario actions are the session's (IRunScenarioControl, faked here); the cluster is the real adapter on a real bus.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐ AQ108 §3 / §2.8 ③ — ▶ from Edit follows the ARMED mode: Preview → the dry run (unchanged); Live + unchanged → exactly one
    /// <c>LoadForLive(loaded scenario)</c>; Live + changed → "Save and go live / Cancel" (Save = SaveCurrent THEN LoadForLive, Cancel = nothing);
    /// never saved → "Save as… and go live / Cancel"; AAR → <c>OpenRecording(LastExercise)</c>.
    /// 🔴 Red-proof: PlayLive's Unchanged arm calling nothing / the Save arm dropping SaveCurrent / the Preview arm not entering the dry run.
    /// </summary>
    [Fact]
    public void CE4097_PlayFromEdit_FollowsTheArmedMode()
    {
        var preview = With(ClusterState.OperatingEdit, RunArmed.Preview);
        preview.F.TogglePlayPause();
        var dry = Sent<TransitionStateIntent>(preview).Single();
        Assert.Equal(ClusterState.OperatingPreview, dry.TargetState);
        Assert.Empty(preview.C.Calls);

        var live = With(ClusterState.OperatingEdit, RunArmed.Live, UnsavedAnswer.Unchanged);
        live.F.TogglePlayPause();
        Assert.Equal(new[] { "LoadForLive:hill-attack" }, live.C.Calls);   // exactly one
        Assert.Null(live.Choice.Pending);
        Assert.Empty(Sent<TransitionStateIntent>(live));                   // the session publishes the load, the facade no dry run

        var changed = With(ClusterState.OperatingEdit, RunArmed.Live, UnsavedAnswer.Changed);
        changed.F.TogglePlayPause();
        Assert.Empty(changed.C.Calls);                                      // nothing until the user answers
        var ask = changed.Choice.Pending!;
        Assert.Equal(new[] { "Save and go live", "Cancel" }, ask.Buttons.Select(b => b.Label));
        ask.Resolve(ask.Buttons[1]);                                        // Cancel
        Assert.Empty(changed.C.Calls);
        Assert.Null(changed.Choice.Pending);
        changed.F.TogglePlayPause();
        changed.Choice.Pending!.Resolve(changed.Choice.Pending.Buttons[0]); // Save
        Assert.Equal(new[] { "SaveCurrent", "LoadForLive:hill-attack" }, changed.C.Calls);   // in THAT order

        var never = With(ClusterState.OperatingEdit, RunArmed.Live, UnsavedAnswer.NeverSaved);
        string? asked = null;
        never.Choice.SaveAsPrompt = onNamed => { asked = "prompted"; onNamed("fresh-one"); };
        never.F.TogglePlayPause();
        var naming = never.Choice.Pending!;
        Assert.Equal(new[] { "Save as… and go live", "Cancel" }, naming.Buttons.Select(b => b.Label));
        Assert.Empty(never.C.Calls);
        naming.Resolve(naming.Buttons[0]);
        Assert.Equal("prompted", asked);
        Assert.Equal(new[] { "LoadForLive:fresh-one" }, never.C.Calls);   // the host's dialog saved; the choice loads THAT name live

        // AAR armed: the last exercise (set by ■ in a live run) at its start
        var aar = With(ClusterState.OperatingLive, RunArmed.Live);
        aar.F.Stop();                                                       // LastExercise = RunId
        aar.C.Calls.Clear();
        Hear(aar, ClusterState.OperatingEdit);
        aar.Choice.Choose(RunArmed.AAR);
        aar.F.TogglePlayPause();
        Assert.Equal(new[] { $"OpenRecording:{RunId}" }, aar.C.Calls);
    }

    /// <summary>⭐ ▶ asked while a confirmation is open does not stack a second question or load (the dialog is modal).</summary>
    [Fact]
    public void CE4097_PlayWhileAConfirmationIsOpen_IsIgnored()
    {
        var rig = With(ClusterState.OperatingEdit, RunArmed.Live, UnsavedAnswer.Changed);
        rig.F.TogglePlayPause();
        var first = rig.Choice.Pending;
        rig.F.TogglePlayPause();
        Assert.Same(first, rig.Choice.Pending);
        Assert.Equal(1, rig.Q.Asked);
    }

    /// <summary>
    /// ⭐ R2 / AQ108 D5 — ■ goes back to where the run came from, the armed mode stays: Preview → the dry run's exit and Review → live (both TODAY's
    /// intents); Live → <c>OpenForEdit(the active exercise's scenario)</c> with Live still armed, <c>LastExercise</c> = the run, and the cluster
    /// NEVER sent to plain Idle; Replay → seek to 0, still AAR. The scenario is the INVENTORY's, not whatever the session last loaded.
    /// 🔴 Red-proof: StopLive's <c>OpenForEdit</c> replaced by <c>_transport.StopRaw()</c> ⇒ the Live row publishes an Idle transition.
    /// </summary>
    [Fact]
    public void CE4097_Stop_FollowsR2()
    {
        var preview = With(ClusterState.OperatingPreview, RunArmed.Preview);
        preview.F.Stop();
        Assert.Equal(ClusterState.OperatingEdit, Sent<TransitionStateIntent>(preview).Single().TargetState);
        Assert.Empty(preview.C.Calls);

        var review = With(ClusterState.OperatingReview, RunArmed.Live);
        review.F.Stop();
        Assert.Equal(ClusterState.OperatingLive, Sent<TransitionStateIntent>(review).Single().TargetState);
        Assert.Empty(review.C.Calls);

        var live = With(ClusterState.OperatingLive, RunArmed.Live, clock: 754, inventoryScenario: "from-the-inventory");
        live.C.LoadedScenarioName = "something-else";
        live.F.Stop();
        Assert.Equal(new[] { "OpenForEdit:from-the-inventory" }, live.C.Calls);
        Assert.Empty(Sent<TransitionStateIntent>(live));                    // ⛔ never Idle
        Assert.Equal(RunArmed.Live, live.Choice.Armed);
        Assert.Equal(RunId, live.Choice.LastExercise);
        Assert.Equal("Exercise recorded (12:34) — Replay it", live.Choice.Toast!.Text);

        var replay = With(ClusterState.OperatingReplay, RunArmed.AAR);
        replay.F.Stop();
        var seek = Sent<SeekReplayIntent>(replay).Single();
        Assert.Equal(0, seek.TargetWallTicks);
        Assert.Equal(0, seek.TargetSimSeconds ?? 0);
        Assert.Empty(Sent<TransitionStateIntent>(replay));
        Assert.Equal(RunArmed.AAR, replay.Choice.Armed);
    }

    /// <summary>⭐ D14 — with recording OFF the same ■ ends the run with the plain note and offers no replay.</summary>
    [Fact]
    public void CE4097_StopInAnUnrecordedLiveRun_SaysTheRunEnded_WithNoReplay()
    {
        var live = With(ClusterState.OperatingLive, RunArmed.Live, runNotRecorded: true);
        live.F.Stop();
        Assert.Equal("Live run ended", live.Choice.Toast!.Text);
        Assert.Null(live.Choice.Toast!.ReplayExercise);
    }

    /// <summary>⭐ A host with NO scenario session keeps today's ■ in a live run (to Idle) — hosts without a session are unchanged.</summary>
    [Fact]
    public void CE4097_AHostWithNoSession_KeepsTodaysStopInLive()
    {
        var live = With(ClusterState.OperatingLive, RunArmed.Live, withControl: false);
        live.F.Stop();
        Assert.Equal(ClusterState.Idle, Sent<TransitionStateIntent>(live).Single().TargetState);
    }

    /// <summary>
    /// ⭐ R1 — no ▶ / Step / ■ ever changes the armed mode; only <c>Choose</c> does. Checked in every running state with Live armed.
    /// 🔴 Red-proof: StopLive setting <c>Armed = RunArmed.Preview</c>.
    /// </summary>
    [Theory]
    [InlineData(ClusterState.OperatingEdit)]
    [InlineData(ClusterState.OperatingPreview)]
    [InlineData(ClusterState.OperatingLive)]
    [InlineData(ClusterState.OperatingReview)]
    [InlineData(ClusterState.OperatingReplay)]
    public void CE4097_R1_NoPlayStepOrStopChangesTheArmedMode(ClusterState state)
    {
        var rig = With(state, RunArmed.Live);
        rig.F.TogglePlayPause();
        rig.F.Step();
        if (state != ClusterState.OperatingEdit) rig.F.Stop();
        Assert.Equal(RunArmed.Live, rig.Choice.Armed);

        var edit = With(ClusterState.OperatingEdit, RunArmed.Live);
        edit.Choice.Choose(RunArmed.AAR);                                   // …and Choose is the one thing that does
        Assert.Equal(RunArmed.AAR, edit.Choice.Armed);
    }

    /// <summary>
    /// ⭐ D12 — opening a scenario for editing resets the armed mode to the HOST's default (editor Preview, CGF Live), however it was opened (the
    /// cluster reaching Edit through Idle); the exceptions are the choice's own returns to editing (R2: ■ in Live keeps Live) and a dry run's exit.
    /// 🔴 Red-proofs: the reset removed ⇒ these rows; the keep-flag removed ⇒ the ■ row of the next test; the dry-run exclusion removed ⇒ its preview row.
    /// </summary>
    [Theory]
    [InlineData(RunArmed.Preview, RunArmed.Live)]   // the editor: was Live, a scenario is opened ⇒ Preview
    [InlineData(RunArmed.Live, RunArmed.Preview)]   // CGF: was Preview, a scenario is opened ⇒ Live
    public void CE4097_D12_OpeningAScenarioResetsTheArmedModeToTheHostDefault(RunArmed hostDefault, RunArmed chosen)
    {
        var rig = With(ClusterState.OperatingEdit, hostDefault);
        rig.Choice.Choose(chosen);
        Assert.Equal(chosen, rig.Choice.Armed);
        Hear(rig, ClusterState.Idle);                                       // the load's unload …
        Assert.Equal(chosen, rig.Choice.Armed);                             // (Idle alone is not an open)
        Hear(rig, ClusterState.OperatingEdit);                              // … and the scenario is up for editing
        Assert.Equal(hostDefault, rig.Choice.Armed);
    }

    [Fact]
    public void CE4097_D12_OurOwnReturnToEditing_KeepsTheArmedMode_AndADryRunExitDoesNotReset()
    {
        // ■ in Live (editor: default Preview, but Live is armed): back to editing keeps Live
        var live = With(ClusterState.OperatingEdit, RunArmed.Preview);
        live.Choice.Choose(RunArmed.Live);
        Hear(live, ClusterState.OperatingLive);
        live.F.Stop();
        Hear(live, ClusterState.Idle);
        Hear(live, ClusterState.OperatingEdit);
        Assert.Equal(RunArmed.Live, live.Choice.Armed);
        Hear(live, ClusterState.Idle);                                      // …but the NEXT, user-made open resets it
        Hear(live, ClusterState.OperatingEdit);
        Assert.Equal(RunArmed.Preview, live.Choice.Armed);

        // a dry run's exit is Edit again, not a new scenario: the mode armed through the preview's confirmation stays
        var preview = With(ClusterState.OperatingEdit, RunArmed.Preview);
        Hear(preview, ClusterState.OperatingPreview);
        preview.Choice.Choose(RunArmed.Live);
        preview.Choice.Pending!.Resolve(preview.Choice.Pending.Buttons[0]);
        Hear(preview, ClusterState.OperatingEdit);
        Assert.Equal(RunArmed.Live, preview.Choice.Armed);
    }

    /// <summary>⭐ §2.8 ⑥ — a host with no scenario session: Live-armed ▶ is disabled with its reason (no load, no dry run); arming Preview enables it.</summary>
    [Fact]
    public void CE4097_AHostWithNoSession_HasLiveArmedPlayDisabledWithAReason()
    {
        var rig = With(ClusterState.OperatingEdit, RunArmed.Live, withControl: false);
        Assert.False(rig.F.IsPlayPauseEnabled);
        Assert.False(rig.F.IsStepEnabled);
        Assert.False(string.IsNullOrWhiteSpace(rig.Choice.PlayDisabledReason));
        rig.F.TogglePlayPause();
        Assert.Empty(Sent<TransitionStateIntent>(rig));

        rig.Choice.Choose(RunArmed.Preview);
        Assert.True(rig.F.IsPlayPauseEnabled);
        Assert.Null(rig.Choice.PlayDisabledReason);
        rig.F.TogglePlayPause();
        Assert.Equal(ClusterState.OperatingPreview, Sent<TransitionStateIntent>(rig).Single().TargetState);
    }

    /// <summary>⭐ A facade with NO run choice attached keeps today's Step from Edit (hosts that attach none are unchanged).</summary>
    [Fact]
    public void CE4097_WithoutAChoice_StepFromEditIsStillTheDryRunPaused()
    {
        var (facade, bus) = In(ClusterState.OperatingEdit);
        Assert.Null(facade.RunChoice);
        facade.Step();
        Assert.Equal("Deterministic", Published<TransitionStateIntent>(bus).TimeMode);
    }

    /// <summary>
    /// ⭐ The readings the choice and the indicator need come off the broadcasts the adapter already drains: <c>RecordsLiveRuns</c> (a run: not
    /// <c>RunNotRecorded</c>; before one: the setting, not <c>RecordLiveRunsOff</c>) and <c>ActiveExerciseScenario</c> (broadcast exercise → inventory item).
    /// 🔴 Red-proof: <c>RecordsLiveRuns</c> ignoring <c>_runNotRecorded</c>.
    /// </summary>
    [Fact]
    public void CE4097_TheFacadeReadsRecordingAndTheRunsScenarioFromTheBroadcasts()
    {
        Assert.True(With(ClusterState.OperatingLive).F.RecordsLiveRuns);
        Assert.False(With(ClusterState.OperatingLive, runNotRecorded: true).F.RecordsLiveRuns);
        Assert.True(With(ClusterState.OperatingEdit, recordLiveRunsOff: false).F.RecordsLiveRuns);
        Assert.False(With(ClusterState.OperatingEdit, recordLiveRunsOff: true).F.RecordsLiveRuns);
        Assert.True(With(ClusterState.OperatingLive, runNotRecorded: false, recordLiveRunsOff: true).F.RecordsLiveRuns);   // a run's own flag, not the setting

        Assert.Equal("hill-attack", With(ClusterState.OperatingLive).F.ActiveExerciseScenario);
        Assert.Null(With(ClusterState.OperatingLive, inventoryScenario: null).F.ActiveExerciseScenario);
        Assert.Equal(RunId, With(ClusterState.OperatingLive).F.ActiveExercise);
    }

    /// <summary>
    /// ⭐ §2.3 — ▶ in Live armed loads PAUSED (the session's load), and ▶ means run: once the cluster reaches Live the clock resumes — exactly once, and
    /// ONLY for a live load this ▶ started (a <c>POST /scenario/load/live</c> from elsewhere does not get a surprise resume).
    /// 🔴 Red-proof: <c>_resumeWhenLive</c> never cleared / never set.
    /// </summary>
    [Fact]
    public void CE4097_PlayInLiveArmed_ResumesTheClockOnceTheLiveLoadIsUp()
    {
        var rig = With(ClusterState.OperatingEdit, RunArmed.Live);
        rig.F.TogglePlayPause();
        Hear(rig, ClusterState.Idle);
        Assert.Empty(Resumes(rig));
        Hear(rig, ClusterState.OperatingLive);
        Assert.Single(Resumes(rig));
        Hear(rig, ClusterState.OperatingReview);
        Hear(rig, ClusterState.OperatingLive);                              // back from a DAR: no second resume
        Assert.Empty(Resumes(rig));

        var other = With(ClusterState.OperatingEdit, RunArmed.Live);
        Hear(other, ClusterState.OperatingLive);                            // someone else's live load
        Assert.Empty(Resumes(other));
    }

    /// <summary>⭐ The production default of the unsaved seam (until backend's CE-4093 master round): no loaded name ⇒ NeverSaved, else Unchanged.</summary>
    [Fact]
    public void CE4097_TheDefaultUnsavedQuery_AnswersByTheLoadedName()
    {
        UnsavedAnswer? Ask(string? name) { UnsavedAnswer? got = null; new ScenarioNameUnsavedChangesQuery(() => name).Ask(a => got = a); return got; }
        Assert.Equal(UnsavedAnswer.NeverSaved, Ask(null));
        Assert.Equal(UnsavedAnswer.NeverSaved, Ask("  "));
        Assert.Equal(UnsavedAnswer.Unchanged, Ask("hill-attack"));
    }
}
