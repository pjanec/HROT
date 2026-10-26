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
                                                                           ReplaySession? session = null, double clock = 0)
    {
        var bus = new FdpEventBus();
        var facade = new ClusterTimeTransportAdapter(bus, () => clock);
        bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = state, ReplayAxis = axis });
        if (session != null) bus.PublishManaged(new ReplaySessionUpdateEvent { Session = session });
        bus.Publish(new SwitchTimeModeEvent { TargetMode = Fdp.ModuleHost.Time.TimeMode.Deterministic });   // paused
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
