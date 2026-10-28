using System;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Time.Domain;
using Fdp.Toolkit.Time.Messages;
using Hrot.UI.Common.Adapters;
using Hrot.UI.Common.Facades;
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
}
