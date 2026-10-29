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
        preview.F.UserPlayPause();
        var dry = Sent<TransitionStateIntent>(preview).Single();
        Assert.Equal(ClusterState.OperatingPreview, dry.TargetState);
        Assert.Empty(preview.C.Calls);

        var live = With(ClusterState.OperatingEdit, RunArmed.Live, UnsavedAnswer.Unchanged);
        live.F.UserPlayPause();
        Assert.Equal(new[] { "LoadForLive:hill-attack" }, live.C.Calls);   // exactly one
        Assert.Null(live.Choice.Pending);
        Assert.Empty(Sent<TransitionStateIntent>(live));                   // the session publishes the load, the facade no dry run

        var changed = With(ClusterState.OperatingEdit, RunArmed.Live, UnsavedAnswer.Changed);
        changed.F.UserPlayPause();
        Assert.Empty(changed.C.Calls);                                      // nothing until the user answers
        var ask = changed.Choice.Pending!;
        Assert.Equal(new[] { "Save and go live", "Cancel" }, ask.Buttons.Select(b => b.Label));
        ask.Resolve(ask.Buttons[1]);                                        // Cancel
        Assert.Empty(changed.C.Calls);
        Assert.Null(changed.Choice.Pending);
        changed.F.UserPlayPause();
        changed.Choice.Pending!.Resolve(changed.Choice.Pending.Buttons[0]); // Save
        Assert.Equal(new[] { "SaveCurrent", "LoadForLive:hill-attack" }, changed.C.Calls);   // in THAT order

        var never = With(ClusterState.OperatingEdit, RunArmed.Live, UnsavedAnswer.NeverSaved);
        string? asked = null;
        never.Choice.SaveAsPrompt = onNamed => { asked = "prompted"; onNamed("fresh-one"); };
        never.F.UserPlayPause();
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
        aar.F.UserPlayPause();
        Assert.Equal(new[] { $"OpenRecording:{RunId}" }, aar.C.Calls);
    }

    /// <summary>⭐ ▶ asked while a confirmation is open does not stack a second question or load (the dialog is modal).</summary>
    [Fact]
    public void CE4097_PlayWhileAConfirmationIsOpen_IsIgnored()
    {
        var rig = With(ClusterState.OperatingEdit, RunArmed.Live, UnsavedAnswer.Changed);
        rig.F.UserPlayPause();
        var first = rig.Choice.Pending;
        rig.F.UserPlayPause();
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
        Assert.Equal("Exercise recorded (12:34) — open the AAR", live.Choice.Toast!.Text);

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
        rig.F.UserPlayPause();
        rig.F.UserStep();
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
        Assert.False(rig.F.IsUserPlayEnabled());
        Assert.False(rig.F.IsUserStepEnabled());
        Assert.False(string.IsNullOrWhiteSpace(rig.Choice.PlayDisabledReason));
        rig.F.UserPlayPause();
        Assert.Empty(Sent<TransitionStateIntent>(rig));

        rig.Choice.Choose(RunArmed.Preview);
        Assert.True(rig.F.IsUserPlayEnabled());
        Assert.Null(rig.Choice.PlayDisabledReason);
        rig.F.UserPlayPause();
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
        rig.F.UserPlayPause();
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

    // ══ ⭐ CE-4097 part two ═══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐ AQ108 D22 — the facade's RAW Play and Step from Edit are the dry run on every host WHATEVER mode is armed: this is what <c>POST /sim/play</c> and <c>/sim/step</c> call,
    /// and a route does what it names. With Live armed an HTTP play must never load a live run (the core had made the facade follow the armed mode).
    /// 🔴 Red-proof: <c>TogglePlayPause</c> / <c>StepBy</c> calling <c>_run.Play(…)</c> again ⇒ Live armed publishes no dry-run intent and calls <c>LoadForLive</c>.
    /// </summary>
    [Theory]
    [InlineData(RunArmed.Live)]
    [InlineData(RunArmed.AAR)]
    [InlineData(RunArmed.Preview)]
    public void CE4097_D22_TheFacadesRawPlayAndStepFromEdit_AreTheDryRun_WhateverIsArmed(RunArmed armed)
    {
        var rig = With(ClusterState.OperatingEdit, armed, UnsavedAnswer.Unchanged);
        rig.Choice.Choose(armed);                                           // (a no-op for the host default; arms the others)

        rig.F.TogglePlayPause();                                            // POST /sim/play
        var play = Sent<TransitionStateIntent>(rig).Single();
        Assert.Equal(ClusterState.OperatingPreview, play.TargetState);
        Assert.Equal("Continuous", play.TimeMode);

        rig.F.Step();                                                       // POST /sim/step
        var step = Sent<TransitionStateIntent>(rig).Single();
        Assert.Equal(ClusterState.OperatingPreview, step.TargetState);
        Assert.Equal("Deterministic", step.TimeMode);

        Assert.Empty(rig.C.Calls);                                          // never a live load, never a recording
        Assert.Equal(0, rig.Q.Asked);                                       // and no unsaved check: a dry run leaves nothing
        Assert.True(rig.F.IsPlayPauseEnabled);                              // the raw answer: a host with no scenario session is enabled too …
        Assert.True(With(ClusterState.OperatingEdit, RunArmed.Live, withControl: false).F.IsPlayPauseEnabled);
        Assert.False(With(ClusterState.OperatingEdit, RunArmed.Live, withControl: false).F.IsUserPlayEnabled());   // … only the USER's ▶ is disabled there
    }

    /// <summary>
    /// ⭐ D22 — ■ keeps delegating (<c>POST /sim/stop</c> = the same Stop as ■), and a USER's ▶ with Live armed IS the live load (the toolbar's, the status bar's and Space share
    /// <see cref="UserTransport"/>). 🔴 Red-proof: <c>UserPlayPause</c> calling <c>f.TogglePlayPause()</c> ⇒ Live armed publishes a dry run instead of loading.
    /// </summary>
    [Fact]
    public void CE4097_D22_AUsersPlayWithLiveArmed_IsTheLiveLoad_AndTheFacadesStopStillFollowsR2()
    {
        var live = With(ClusterState.OperatingEdit, RunArmed.Live, UnsavedAnswer.Unchanged);
        live.F.UserPlayPause();
        Assert.Equal(new[] { "LoadForLive:hill-attack" }, live.C.Calls);
        Assert.Empty(Sent<TransitionStateIntent>(live));

        var running = With(ClusterState.OperatingLive, RunArmed.Live);
        running.F.Stop();                                                   // what POST /sim/stop calls
        Assert.Equal(new[] { "OpenForEdit:hill-attack" }, running.C.Calls); // R2 — back to the scenario, never plain Idle
        Assert.Empty(Sent<TransitionStateIntent>(running));
    }

    /// <summary>
    /// ⭐ AQ108 D20 / CE-4093 — ▶ in Live armed asks the MASTER'S round: ONE <c>CheckUnsavedIntent</c> on the host's bus, no decision until the answer arrives (polled from the
    /// choice's per-frame update — nothing blocks), then unchanged ⇒ the load; changed ⇒ <i>Save and go live / Cancel</i>. 🔴 Red-proof: <c>ClusterUnsavedChangesQuery.Ask</c> replying
    /// <c>Unchanged</c> at once (the old name-only answer) ⇒ the load happens before any answer and a changed world loads unprompted.
    /// </summary>
    [Fact]
    public void CE4097_TheUnsavedCheck_IsTheMastersOneRound_PolledNeverWaitedOn()
    {
        var rig = WithCheckRound(ClusterState.OperatingEdit, RunArmed.Live);
        rig.F.UserPlayPause();
        var asked = Sent<CheckUnsavedIntent>(rig);
        Assert.Single(asked);                                               // ONE round for the move
        Assert.Empty(rig.C.Calls);                                          // undecided until the answer
        Assert.Null(rig.Choice.Pending);
        Frame(rig); Frame(rig);                                             // frames pass with no answer: still waiting, still nothing
        Assert.Empty(rig.C.Calls);
        rig.F.UserPlayPause();                                              // ▶ again while the round is out does not ask a second round
        Assert.Empty(Sent<CheckUnsavedIntent>(rig));

        rig.R.Answer(asked, changed: false);
        Frame(rig);
        Assert.Equal(new[] { "LoadForLive:hill-attack" }, rig.C.Calls);

        var edited = WithCheckRound(ClusterState.OperatingEdit, RunArmed.Live);
        edited.F.UserPlayPause();
        edited.R.Answer(Sent<CheckUnsavedIntent>(edited), changed: true);
        Frame(edited);
        Assert.Empty(edited.C.Calls);
        Assert.Equal(new[] { "Save and go live", "Cancel" }, edited.Choice.Pending!.Buttons.Select(b => b.Label));
    }

    /// <summary>
    /// ⭐ NOTE rules, each a row: a host that observes nothing (<c>ObservesUnsavedChecks</c> false) asks NO round and counts the scenario as unchanged; NeverSaved stays decided by an empty loaded
    /// name — also without asking; a master's refusal (not editing) reads as unchanged; ⚠ no answer within the deadline fails CLOSED to the Save-first prompt.
    /// 🔴 Red-proof: dropping the <c>Observes()</c> guard publishes a round nobody can answer (the ▶ then waits 15 s); dropping the deadline arm never answers.
    /// </summary>
    [Fact]
    public void CE4097_TheUnsavedCheck_NotObserved_NeverSaved_Refused_AndTheDeadline()
    {
        var blind = WithCheckRound(ClusterState.OperatingEdit, RunArmed.Live, observes: false);
        blind.F.UserPlayPause();
        Assert.Empty(Sent<CheckUnsavedIntent>(blind));                      // asks nothing …
        Assert.Equal(new[] { "LoadForLive:hill-attack" }, blind.C.Calls);   // … and goes on as unchanged

        var fresh = WithCheckRound(ClusterState.OperatingEdit, RunArmed.Live, loadedName: null);
        string? named = null;
        fresh.Choice.SaveAsPrompt = onNamed => { named = "prompted"; };
        fresh.F.UserPlayPause();
        Assert.Empty(Sent<CheckUnsavedIntent>(fresh));                      // the world has no file: nothing to compare
        Assert.Equal(new[] { "Save as… and go live", "Cancel" }, fresh.Choice.Pending!.Buttons.Select(b => b.Label));

        var refused = WithCheckRound(ClusterState.OperatingEdit, RunArmed.Live);
        refused.F.UserPlayPause();
        refused.R.Answer(Sent<CheckUnsavedIntent>(refused), changed: null);
        Frame(refused);
        Assert.Equal(new[] { "LoadForLive:hill-attack" }, refused.C.Calls);

        var clock = DateTime.UtcNow;
        var bus = new FdpEventBus();
        var master = new Hrot.Presentation.DebugApi.OrchestrationBusMasterControl(() => bus);
        var query = new ClusterUnsavedChangesQuery(() => "hill-attack", master, new FakeReads().Reads, () => clock);
        UnsavedAnswer? got = null;
        query.Ask(a => got = a);
        query.Poll();
        Assert.Null(got);
        clock += ClusterUnsavedChangesQuery.Deadline + TimeSpan.FromSeconds(1);
        query.Poll();
        Assert.Equal(UnsavedAnswer.Changed, got);                           // fail closed: Save first
    }

    /// <summary>
    /// ⭐ ONLY a move that LEAVES EDITING asks (the master refuses a round elsewhere): opening a recording from a Live run, a DAR or an AAR publishes no round; from Edit it asks ONE and, with
    /// unsaved edits, offers <i>Save / Discard / Cancel</i> (Save = SaveCurrent THEN the open; Discard = the open; Cancel = nothing). Opening another scenario asks the same way.
    /// 🔴 Red-proof: <c>LeavingEditing</c> without its <c>Mode != Edit</c> guard ⇒ the Live row publishes a round; the Save arm dropping <c>SaveCurrent</c>.
    /// </summary>
    [Fact]
    public void CE4097_OnlyAMoveThatLeavesEditing_AsksTheRound_AndTheChangedAnswerOffersSaveDiscardCancel()
    {
        foreach (var state in new[] { ClusterState.OperatingLive, ClusterState.OperatingReplay, ClusterState.OperatingPreview })
        {
            var running = WithCheckRound(state);
            running.Choice.OpenRecording(RunId);
            Assert.Empty(Sent<CheckUnsavedIntent>(running));                // not editing: nothing of the scenario to lose
            Assert.Equal(new[] { $"OpenRecording:{RunId}" }, running.C.Calls);
        }

        var edit = WithCheckRound(ClusterState.OperatingEdit);
        edit.Choice.OpenRecording(RunId);
        edit.R.Answer(Sent<CheckUnsavedIntent>(edit), changed: true);
        Frame(edit);
        Assert.Empty(edit.C.Calls);
        var ask = edit.Choice.Pending!;
        Assert.Equal(new[] { "Save", "Discard", "Cancel" }, ask.Buttons.Select(b => b.Label));
        ask.Resolve(ask.Buttons[0]);
        Assert.Equal(new[] { "SaveCurrent", $"OpenRecording:{RunId}" }, edit.C.Calls);   // in THAT order
        Assert.Equal(RunArmed.AAR, edit.Choice.Armed);

        var discard = WithCheckRound(ClusterState.OperatingEdit);
        discard.Choice.OpenScenario("other");
        discard.R.Answer(Sent<CheckUnsavedIntent>(discard), changed: true);
        Frame(discard);
        discard.Choice.Pending!.Resolve(discard.Choice.Pending.Buttons[1]);
        Assert.Equal(new[] { "OpenForEdit:other" }, discard.C.Calls);       // Discard opens without saving

        var cancel = WithCheckRound(ClusterState.OperatingEdit);
        cancel.Choice.OpenScenario("other");
        cancel.R.Answer(Sent<CheckUnsavedIntent>(cancel), changed: true);
        Frame(cancel);
        cancel.Choice.Pending!.Resolve(cancel.Choice.Pending.Buttons[^1]);
        Assert.Empty(cancel.C.Calls);
    }

    /// <summary>
    /// ⭐ D14 — a run that was NOT recorded offers no AAR: ■ says <i>Live run ended</i> (no button), the indicator's "AAR the last exercise" (▶ in AAR armed) is greyed
    /// <i>this run was not recorded</i>, <c>ReplayLastExercise</c> opens nothing, and DAR is greyed with the same reason in the Live menu. A RECORDED run's toast button opens
    /// the run at its start through the guarded open (D6). 🔴 Red-proof: <c>_lastExerciseRecorded</c> never cleared ⇒ the unrecorded row offers the run.
    /// </summary>
    [Fact]
    public void CE4097_AnUnrecordedRun_OffersNoAarAndNoDar_ARecordedOneOffersItsToastButton()
    {
        var unrecorded = With(ClusterState.OperatingLive, RunArmed.Live, runNotRecorded: true);
        Assert.Equal("this run was not recorded", unrecorded.Choice.DarDisabledReason);
        unrecorded.F.Stop();
        Assert.Null(unrecorded.Choice.Toast!.ReplayExercise);               // "Live run ended" — no AAR button
        Hear(unrecorded, ClusterState.OperatingEdit);
        unrecorded.Choice.Choose(RunArmed.AAR);
        Assert.Equal("this run was not recorded", unrecorded.Choice.PlayDisabledReason);
        unrecorded.C.Calls.Clear();
        unrecorded.Choice.ReplayLastExercise();
        unrecorded.F.UserPlayPause();
        Assert.Empty(unrecorded.C.Calls);

        var recorded = With(ClusterState.OperatingLive, RunArmed.Live);
        Assert.Null(recorded.Choice.DarDisabledReason);
        recorded.F.Stop();
        var toast = recorded.Choice.Toast!;
        Assert.Equal(RunId, toast.ReplayExercise);
        Assert.StartsWith("Exercise recorded (0:00)", toast.Text);
        Assert.DoesNotContain("Replay", toast.Text);                        // the user's word is AAR (D17)
        Hear(recorded, ClusterState.OperatingEdit);
        recorded.C.Calls.Clear();
        recorded.Choice.ReplayLastExercise();                               // the toast's button
        Assert.Equal(new[] { $"OpenRecording:{RunId}" }, recorded.C.Calls);
        Assert.Equal(RunArmed.AAR, recorded.Choice.Armed);
    }

    /// <summary>
    /// ⭐ AQ108 D8 — Save in a run is a CHECKPOINT with the toast <i>Checkpoint taken</i>: in Live and in a DAR it publishes <c>TakeCheckpoint</c> and never <c>SaveCurrent</c>; in an AAR (no recorder to
    /// take it with) and in an unrecorded run nothing is published and the toast says why. In Edit the save goes through. 🔴 Red-proof: <c>TryCheckpointInsteadOfSave</c> returning false.
    /// </summary>
    [Fact]
    public void CE4097_SaveInARun_IsACheckpoint_NeverAScenarioWrite()
    {
        foreach (var state in new[] { ClusterState.OperatingLive, ClusterState.OperatingReview })
        {
            var run = With(state, RunArmed.Live);
            Assert.True(SaveRoute(run.Choice, run.C));
            Assert.Equal(new[] { "TakeCheckpoint" }, run.C.Calls);
            Assert.Equal("Checkpoint taken", run.Choice.Toast!.Text);
        }

        var aar = With(ClusterState.OperatingReplay, RunArmed.Live);
        Assert.True(SaveRoute(aar.Choice, aar.C));
        Assert.Empty(aar.C.Calls);                                          // nothing published, nothing written
        Assert.Contains("recording cannot take a checkpoint", aar.Choice.Toast!.Text);

        var unrecorded = With(ClusterState.OperatingLive, RunArmed.Live, runNotRecorded: true);
        Assert.True(SaveRoute(unrecorded.Choice, unrecorded.C));
        Assert.Empty(unrecorded.C.Calls);
        Assert.Contains("this run was not recorded", unrecorded.Choice.Toast!.Text);

        var edit = With(ClusterState.OperatingEdit, RunArmed.Live);
        Assert.False(SaveRoute(edit.Choice, edit.C));                       // editing: the ordinary save
    }

    /// <summary>The route a session's <c>SaveCurrent</c> takes: the intercept the choice installed on <paramref name="control"/> (true = handled).</summary>
    private static bool SaveRoute(RunModeChoice choice, FakeControl control)
        => control.SaveIntercept?.Invoke() == true;

    /// <summary>⭐ D20 — the choice's constructor installs the save intercept on a session that accepts one (<see cref="ISaveInterception"/>), and only then.</summary>
    [Fact]
    public void CE4097_TheChoiceInstallsTheSaveInterceptOnAnInterceptableSession()
    {
        var rig = With(ClusterState.OperatingLive, RunArmed.Live);
        Assert.NotNull(rig.C.SaveIntercept);
        Assert.True(rig.C.SaveIntercept!());
    }

    /// <summary>
    /// ⭐ D21 — "Go live from here": the seek goes out FIRST and the branch intent TWO frames later (the master reads the seek and, leaving a replay, drains the seeks in flight before it
    /// branches — <c>ClusterMaster</c> F8), so the new run starts at the clicked moment; Live becomes armed; ⛔ outside an AAR it does nothing. 🔴 Red-proof: the branch published at once
    /// (<c>_branchInFrames = 0</c> path) ⇒ the first assertion sees it before the frames pass.
    /// </summary>
    [Fact]
    public void CE4097_GoLiveFromHere_SeeksFirst_ThenBranchesTwoFramesLater()
    {
        var rig = With(ClusterState.OperatingReplay, RunArmed.Live);
        var order = new List<string>();
        rig.Choice.GoLiveFromHere(() => order.Add("seek"));
        Assert.Equal(new[] { "seek" }, order);
        Assert.Equal(RunArmed.Live, rig.Choice.Armed);
        Assert.Empty(Sent<TransitionStateIntent>(rig));                     // not yet: the seek is still in flight
        Hear(rig, ClusterState.OperatingReplay);
        Assert.Empty(Sent<TransitionStateIntent>(rig));
        Hear(rig, ClusterState.OperatingReplay);
        var branch = rig.Bus.ReadManaged<TransitionStateIntent>().Single();   // published DURING the last Hear frame (its closing swap exposed it)
        Assert.Equal(ClusterState.OperatingLive, branch.TargetState);       // the built branch (ReplaySessionIntents.BranchLive)

        var edit = With(ClusterState.OperatingEdit, RunArmed.Preview);
        edit.Choice.GoLiveFromHere(() => order.Add("never"));
        Assert.DoesNotContain("never", order);
        Assert.Equal("Only an AAR can go live from a moment", edit.Choice.GoLiveFromHereDisabledReason);
    }

    /// <summary>
    /// ⭐ AQ108 D5 / R2, branch case (backend's <c>ExerciseInventoryItem.ParentExerciseId</c> + <c>BranchWallTicks</c>) — ■ on a run that branched from a recording returns to the PARENT's AAR (not
    /// the scenario's start), Live stays armed, nothing is sent to Edit/Idle; and once the parent's session (its wall zero) is known the choice seeks to the branch point
    /// (wall ticks → the session's axis: seconds after the zero). A run that began at a load still returns to its scenario.
    /// 🔴 Red-proof: StopLive ignoring <c>ActiveExerciseBranch</c> ⇒ <c>OpenForEdit</c>; the seek line dropped ⇒ no <c>SeekReplayIntent</c>.
    /// </summary>
    [Fact]
    public void CE4097_D5_StopOnABranchedRun_ReturnsToTheParentsAarAtTheBranchPoint()
    {
        var parent = Guid.Parse("00000000-0000-0000-0000-0000000000b9");
        const long zero = 5_000_000_000, branch = zero + 70_000_000;     // 7 s into the parent
        var rig = With(ClusterState.OperatingLive, RunArmed.Live, inventoryScenario: null);
        rig.Bus.PublishManaged(new AssetInventoryUpdateEvent { LocalExercises = new[] { new ExerciseInventoryItem(RunId, DateTime.UtcNow, TimeSpan.Zero, "hill-attack", parent, branch) } });
        Hear(rig, ClusterState.OperatingLive);

        rig.F.Stop();
        Assert.Equal(new[] { $"OpenRecording:{parent}" }, rig.C.Calls);   // the parent, at its start …
        Assert.Equal(RunArmed.Live, rig.Choice.Armed);                      // … Live stays armed (R2)
        Assert.Empty(Sent<TransitionStateIntent>(rig));
        Assert.Equal(RunId, rig.Choice.LastExercise);

        Hear(rig, ClusterState.OperatingReplay);                            // the replay is up but its session is not known yet: no seek
        Assert.Empty(rig.Bus.ReadManaged<SeekReplayIntent>());
        rig.Bus.PublishManaged(new ReplaySessionUpdateEvent { Session = new ReplaySession { Active = true, ZeroWallTicks = zero, LengthSeconds = 30 } });
        Hear(rig, ClusterState.OperatingReplay);
        var seek = rig.Bus.ReadManaged<SeekReplayIntent>().Single();
        Assert.Equal(7.0, seek.TargetSimSeconds);                           // (branch - zero) = 7 s after the zero (a map-less session: the cursor's own conversion, carried on the Sim axis)

        var plain = With(ClusterState.OperatingLive, RunArmed.Live);
        plain.F.Stop();
        Assert.Equal(new[] { "OpenForEdit:hill-attack" }, plain.C.Calls);   // no branch: the scenario's start
    }

    /// <summary>
    /// ⭐ CE-4091 / AQ108 §3 — AAR → Preview from the indicator: when the host says the saved scenario differs from the one the recording ran, the confirmation offers
    /// <i>Open the recorded version (as a copy)</i> (→ <c>OpenForEdit(Recipes/Recorded/&lt;exercise&gt;)</c>) and <i>Open the current scenario</i>; when it does not (or cannot tell) it is the plain one.
    /// 🔴 Red-proof: the recorded button opening the current scenario's name ⇒ the name assertion; <c>RecordedVersionDiffers</c> ignored ⇒ the plain row grows two buttons.
    /// </summary>
    [Fact]
    public void CE4091_AarToPreview_WhenTheSavedHashDiffers_OffersTheRecordedCopyAndTheCurrentScenario()
    {
        var differs = With(ClusterState.OperatingReplay, RunArmed.Live);
        differs.Choice.RecordedVersionDiffers = (id, name) => id == RunId && name == "hill-attack";
        differs.Choice.Choose(RunArmed.Preview);
        var ask = differs.Choice.Pending!;
        Assert.Equal(new[] { "Open the recorded version (as a copy)", "Open the current scenario", "Cancel" }, ask.Buttons.Select(b => b.Label));
        ask.Resolve(ask.Buttons[0]);
        Assert.Equal(new[] { $"OpenForEdit:{RecordedScenarioNames.ForExercise(RunId)}" }, differs.C.Calls);
        Assert.Equal(RunArmed.Preview, differs.Choice.Armed);

        var current = With(ClusterState.OperatingReplay, RunArmed.Live);
        current.Choice.RecordedVersionDiffers = (_, _) => true;
        current.Choice.Choose(RunArmed.Preview);
        current.Choice.Pending!.Resolve(current.Choice.Pending.Buttons[1]);
        Assert.Equal(new[] { "OpenForEdit:hill-attack" }, current.C.Calls);

        foreach (var seam in new Func<Guid, string, bool>?[] { null, (_, _) => false })
        {
            var same = With(ClusterState.OperatingReplay, RunArmed.Live);
            same.Choice.RecordedVersionDiffers = seam;
            same.Choice.Choose(RunArmed.Preview);
            Assert.Equal(new[] { "End AAR, edit scenario", "Cancel" }, same.Choice.Pending!.Buttons.Select(b => b.Label));
        }
    }

    /// <summary>⭐ D21 — "AAR from here" (Live): ends the live run into its recording AT the moment (sim seconds), arms AAR, no confirmation; refused (with the reason) outside a recorded live run.</summary>
    [Fact]
    public void CE4097_AarFromHere_OpensTheRunsRecordingAtTheMoment()
    {
        var rig = With(ClusterState.OperatingLive, RunArmed.Live);
        Assert.Null(rig.Choice.AarFromHereDisabledReason);
        rig.Choice.AarFromHere(42.5);
        Assert.Equal(new[] { $"OpenRecording:{RunId}@42.5" }, rig.C.Calls);
        Assert.Equal(RunArmed.AAR, rig.Choice.Armed);
        Assert.Null(rig.Choice.Pending);

        Assert.Equal("this run was not recorded", With(ClusterState.OperatingLive, runNotRecorded: true).Choice.AarFromHereDisabledReason);
        Assert.NotNull(With(ClusterState.OperatingReplay).Choice.AarFromHereDisabledReason);
        var edit = With(ClusterState.OperatingEdit);
        edit.Choice.AarFromHere(1);
        Assert.Empty(edit.C.Calls);
    }
}
