// PreviewClockProcessManagerTests.cs
// ⭐ CE-3299 B2 / D5 — a cluster dry run puts the clock back with the world.

using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Time;
using Fdp.Toolkit.Time.Controllers;
using Xunit;

namespace Hrot.Orchestrator.Tests;

[Collection("OrchestratorTests")]
public sealed class PreviewClockProcessManagerTests
{
    private sealed class RecordingCommands : ITimeCommands
    {
        public readonly List<GlobalTime> Snaps = new();
        public void Pause() { }
        public void Resume() { }
        public void StepOneTick() { }
        public void StepBy(float seconds) { }
        public void SetTimeScale(float scale) { }
        public void SnapTo(GlobalTime position) => Snaps.Add(position);
    }

    private static void Transition(FdpEventBus bus, PreviewClockProcessManager mgr, ClusterState to)
    {
        bus.PublishManaged(new ClusterStateTransitionedEvent { NewStateId = to, SubsystemName = "Cluster" });
        bus.SwapBuffers();
        mgr.Tick();
        bus.SwapBuffers();
    }

    /// <summary>⭐ D5 — the position at LoadingPreview (before time runs) is what the clock is snapped to once the cluster is back in
    /// OperatingEdit after UnloadingPreview; time moved in between.</summary>
    [Fact(Timeout = 10_000)]
    public void ADryRun_SnapsTheClock_BackToWhereItStarted()
    {
        var bus = new FdpEventBus();
        bus.RegisterManaged<ClusterStateTransitionedEvent>();
        long ticks = 1000;
        var reads = new MasterSyncController(new FdpEventBus(), new HashSet<int>(), tickSource: () => ticks);
        var cmds = new RecordingCommands();
        var mgr = new PreviewClockProcessManager(bus, reads, cmds);

        Transition(bus, mgr, ClusterState.OperatingEdit);
        Transition(bus, mgr, ClusterState.LoadingPreview);
        var entered = mgr.EnteredAt;
        Assert.NotNull(entered);

        ticks += 50_000_000;   // the dry run runs
        Transition(bus, mgr, ClusterState.OperatingPreview);
        Transition(bus, mgr, ClusterState.UnloadingPreview);
        Assert.Empty(cmds.Snaps);   // not before the world is back
        Transition(bus, mgr, ClusterState.OperatingEdit);

        Assert.Single(cmds.Snaps);
        Assert.Equal(entered!.Value.TotalTime, cmds.Snaps[0].TotalTime);
        Assert.Equal(entered.Value.FrameNumber, cmds.Snaps[0].FrameNumber);
        Assert.Null(mgr.EnteredAt);
    }

    /// <summary>⭐ A dry run that ends some other way (a load) puts nothing back — the next one starts clean.</summary>
    [Fact(Timeout = 10_000)]
    public void ADryRunAbandonedByALoad_SnapsNothing()
    {
        var bus = new FdpEventBus();
        bus.RegisterManaged<ClusterStateTransitionedEvent>();
        var reads = new MasterSyncController(new FdpEventBus(), new HashSet<int>(), tickSource: () => 0L);
        var cmds = new RecordingCommands();
        var mgr = new PreviewClockProcessManager(bus, reads, cmds);

        Transition(bus, mgr, ClusterState.LoadingPreview);
        Transition(bus, mgr, ClusterState.LoadingEdit);
        Transition(bus, mgr, ClusterState.OperatingEdit);

        Assert.Empty(cmds.Snaps);
        Assert.Null(mgr.EnteredAt);
    }
}
