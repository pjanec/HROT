using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Time;
using Fdp.Toolkit.Time;
using Fdp.Toolkit.Time.Controllers;
using Hrot.Editor.UI;
using Hrot.UI.Common.Facades;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// ⭐ CE-4051 / CE-4052 — in a replay the editor's time controls drive the REPLAY clock (the master's), never the recorded frame's.
/// 🔴 Measured live (`2026-10-26`): the world's <see cref="GlobalTime"/> is the played-back frame's, a stepped frame carries
/// <c>DeltaTime = 1/60</c>, so the facade read "running" while the master stood paused — every <c>POST /sim/step</c> became a
/// <c>Pause()</c> that never landed (504 after 20 s), and <c>totalTime</c> showed the recorded sim time, not the seconds after the zero.
/// </summary>
public sealed class EditorTimeTransportFacadeReplayTests
{
    private sealed class InPreview : IPreviewController
    {
        public bool IsInPreviewMode { get; set; } = true;
        public void EnterPreviewMode(bool startPaused = false) => IsInPreviewMode = true;
        public void ExitPreviewMode() => IsInPreviewMode = false;
        public void ForgetPreview() => IsInPreviewMode = false;
    }

    private static (EditorTimeTransportFacade Facade, MasterSyncController Master, System.Func<long> Tick) Replay(bool replaying)
    {
        long ticks = 0;
        var master = new MasterSyncController(new FdpEventBus(), new HashSet<int>(), new TimeConfig { LookaheadWallTicks = 0 }, () => ticks);
        master.SwitchToDeterministic(new HashSet<int>());       // the replay entered paused (a seek round pauses the cluster)
        ticks += 1;
        master.Update();

        var world = new EntityRepository();
        // the PLAYED-BACK frame's clock: recorded while the live run was stepped
        world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 7.5, DeltaTime = 1f / 60f, TimeScale = 1f });
        var facade = new EditorTimeTransportFacade(new InPreview(), master, world, isReplaying: () => replaying);
        return (facade, master, () => { ticks += 1; master.Update(); return ticks; });
    }

    [Fact]
    public void InAReplay_TheControlsReadTheReplayClock_AndAStepReachesTheMaster()
    {
        var (facade, master, tick) = Replay(replaying: true);

        Assert.True(facade.IsPaused);                                                   // the master is paused
        Assert.Equal(master.GetCurrentState().TotalTime, facade.TotalTime, 6);          // the replay clock, not the recorded 7.5

        double before = facade.TotalTime;
        facade.StepBy(1f / 60f);
        tick();
        Assert.True(facade.TotalTime > before, $"the step did not reach the master: {before} → {facade.TotalTime}");
    }

    /// <summary>The control: the same world read as a live clock is what the replay used to see — "running", so a step only paused.</summary>
    [Fact]
    public void OutsideAReplay_TheWorldsClockIsRead_AsBefore()
    {
        var (facade, _, _) = Replay(replaying: false);

        Assert.False(facade.IsPaused);
        Assert.Equal(7.5, facade.TotalTime, 6);
    }
}
