using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fdp.Core;
using Fdp.ModuleHost.Time;
using Fdp.Toolkit.Orchestration;
using Xunit;
using NedClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;
using FdpNodeOpType   = Fdp.Toolkit.Orchestration.NodeOpType;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// ⭐⭐ <c>CE-3329</c> P3 — During Action Review at the MASTER: a review starts only from a live run, enters through the replay's seek
/// round (at the live point unless a moment is named), refuses a scenario save while a recording is shown, and its clock pauses on the
/// barrier, remembers L and the rate, and snaps back to L on the return. 📄 docs/DESIGN_Replay_Time_Axis_And_Review.md §3a, §3c.
/// </summary>
[Collection("OrchestratorTests")]
public sealed class ClusterMasterReviewTests
{
    private static ClusterConfiguration Config() => new()
    {
        Mandatory = Array.Empty<string>(), HeartbeatTimeoutSeconds = 60f, TransactionHistoryCapacity = 10,
    };

    private static void Set(ClusterMaster m, string field, object value)
        => typeof(ClusterMaster).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(m, value);

    private static ClusterMaster MasterWithOneNode(FdpEventBus bus)
    {
        var master = new ClusterMaster(bus, Config());
        master.RegisterAggregator(new ReplayProcessManager(bus, new StillClock()).CreateReviewAggregator());
        bus.PublishManaged(new NodeHeartbeatEvent
        {
            NodeId = 1, SubsystemName = "SimHost", LocalStateId = (int)ClusterState.OperatingLive, WallTicksUtc = DateTimeOffset.UtcNow.Ticks,
        });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
        return master;
    }

    [Fact(Timeout = 10_000)]
    public void A_review_starts_only_from_a_live_run()
    {
        var bus = new FdpEventBus();
        using var master = MasterWithOneNode(bus);
        var refused = Guid.NewGuid();
        bus.PublishManaged(new TransitionStateIntent { TransactionId = refused, TargetState = ClusterState.OperatingReview });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
        Assert.Contains(bus.ReadManaged<ClusterOpCompletedEvent>(), e => e.RequestId == refused && e.StatusCode == OrchestrationStatusCode.Rejected);
        Assert.DoesNotContain(bus.ReadManaged<ExecuteNodeOpIntent>(), i => i.Operation == FdpNodeOpType.PrepareReview);   // ⛔ never "load live to reach it"
    }

    /// <summary>⭐ From a live run: ONE transition fans out PrepareReview; when its round completes the master seeks every node to the LIVE
    /// POINT (no moment named) — the end of what every node recorded.</summary>
    [Fact(Timeout = 10_000)]
    public void From_live_a_review_prepares_every_node_then_seeks_to_the_live_point()
    {
        var bus = new FdpEventBus();
        using var master = MasterWithOneNode(bus);
        Set(master, "_currentDsmState", NedClusterState.OperatingLive);

        bus.PublishManaged(new TransitionStateIntent { TransactionId = Guid.NewGuid(), TargetState = ClusterState.OperatingReview });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
        var ops = bus.ReadManaged<ExecuteNodeOpIntent>().Where(i => i.Operation != FdpNodeOpType.CommitState).ToList();
        Assert.Contains(ops, i => i.Operation == FdpNodeOpType.PrepareReview);   // LoadingReview → PrepareReview (and OperatingReview → PrepareState)
        foreach (var op in ops)
            bus.PublishManaged(new NodeOpCompletedEvent
            {
                TransactionId = op.TransactionId, Operation = op.Operation, NodeId = 1, StatusCode = OrchestrationStatusCode.Success,
                IsParticipating = true,
                ResultPayload = op.Operation == FdpNodeOpType.PrepareReview
                    ? new Fdp.Toolkit.Orchestration.Handlers.ReplayPrepareResult(0, 12f, 4_000_000L, 124_000_000L) : null,
            });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
        var seek = Assert.Single(bus.ReadManaged<SeekReplayIntent>());
        Assert.Equal(long.MaxValue, seek.TargetWallTicks);   // the live point (the node saturates "the end")
        Assert.Equal(4_000_000L, master.ReplayAnchorWallTicks);
    }

    [Fact(Timeout = 10_000)]
    public void A_save_is_refused_while_the_cluster_shows_a_recording()
    {
        var bus = new FdpEventBus();
        using var master = MasterWithOneNode(bus);
        Set(master, "_currentDsmState", NedClusterState.OperatingReview);
        var save = Guid.NewGuid();
        bus.PublishManaged(new ExecuteStorageOpIntent { RequestId = save, Operation = StorageOpType.SaveScenario, ScenarioName = "x" });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
        Assert.Contains(bus.ReadManaged<ClusterOpCompletedEvent>(), e => e.RequestId == save && e.StatusCode == OrchestrationStatusCode.Rejected);
    }

    /// <summary>⭐ CE-4090 (AQ108 D22) — a scenario save is refused in a LIVE run as well (the recording case above is CE-3329 P3's): that world is not
    /// the scenario. The refusal names why; an EDIT session still saves (the control).</summary>
    [Fact(Timeout = 10_000)]
    public void CE4090_A_save_is_refused_in_a_live_run_too_but_not_while_editing()
    {
        var bus = new FdpEventBus();
        using var master = MasterWithOneNode(bus);
        Set(master, "_currentDsmState", NedClusterState.OperatingLive);
        var save = Guid.NewGuid();
        bus.PublishManaged(new ExecuteStorageOpIntent { RequestId = save, Operation = StorageOpType.SaveScenario, ScenarioName = "x" });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
        var refused = Assert.Single(bus.ReadManaged<ClusterOpCompletedEvent>().Where(e => e.RequestId == save).ToList());
        Assert.Equal(OrchestrationStatusCode.Rejected, refused.StatusCode);
        Assert.Equal(RunRecordingGates.SaveRefusedInRunReason, refused.ResultPayload);
        Assert.DoesNotContain(bus.ReadManaged<ExecuteNodeOpIntent>(), i => i.Operation == FdpNodeOpType.SerializeLocal);

        Set(master, "_currentDsmState", NedClusterState.OperatingEdit);
        bus.PublishManaged(new ExecuteStorageOpIntent { RequestId = Guid.NewGuid(), Operation = StorageOpType.SaveScenario, ScenarioName = "x" });
        bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
        Assert.Contains(bus.ReadManaged<ExecuteNodeOpIntent>(), i => i.Operation == FdpNodeOpType.SerializeLocal);
    }

    /// <summary>⭐⭐ CE-4090 (AQ108 D14, §5a) — DAR needs the run's recording: a review of a run that was not recorded is refused at the MASTER with
    /// the reason, no <c>PrepareReview</c> reaches a node — and the review's CLOCK does not pause the live run under a review that will never open
    /// (⛔ the old order paused first, then threw in every node's prepare).</summary>
    [Fact(Timeout = 10_000)]
    public void CE4090_A_review_of_a_run_that_was_not_recorded_is_refused_and_pauses_nothing()
    {
        var bus = new FdpEventBus();
        using var master = MasterWithOneNode(bus);
        Set(master, "_currentDsmState", NedClusterState.OperatingLive);
        Set(master, "_runNotRecorded", true);

        var clock = new StillClock { Mode = TimeMode.Continuous, Now = new GlobalTime { TotalTime = 5 } };
        var cmds  = new Commands();
        var pm    = new ReviewProcessManager(bus, clock, cmds, () => ClusterState.OperatingLive, runNotRecorded: () => master.RunNotRecorded);

        var review = Guid.NewGuid();
        bus.PublishManaged(new TransitionStateIntent { TransactionId = review, TargetState = ClusterState.OperatingReview, TargetSimSeconds = 3 });
        bus.SwapBuffers();
        pm.Tick();                                                   // the clock first, as the orchestrator ticks it
        master.Tick(); bus.SwapBuffers();

        Assert.Equal(0, cmds.Pauses);                                // ⛔ the live clock was not paused …
        Assert.False(pm.InReview);                                   // … and no review is "open"
        var refused = Assert.Single(bus.ReadManaged<ClusterOpCompletedEvent>().Where(e => e.RequestId == review).ToList());
        Assert.Equal(OrchestrationStatusCode.Rejected, refused.StatusCode);
        Assert.Equal(RunRecordingGates.NotRecordedReason, refused.ResultPayload);
        Assert.DoesNotContain(bus.ReadManaged<ExecuteNodeOpIntent>(), i => i.Operation == FdpNodeOpType.PrepareReview);
    }

    /// <summary>⭐⭐ §3c, F3, F12, F19 — the review's clock: a BARRIER pause at entry (never a snap), L remembered once the pause lands,
    /// and the return puts the clock back to L and the live rate.</summary>
    [Fact]
    public void The_review_clock_pauses_remembers_L_and_snaps_back_with_the_live_rate()
    {
        var bus = new FdpEventBus();
        var clock = new StillClock { Mode = TimeMode.Continuous, Rate = 2f, Now = new GlobalTime { TotalTime = 61.5, FrameNumber = 615, TotalWallTicks = 100 } };
        var cmds = new Commands();
        var state = ClusterState.OperatingLive;
        var pm = new ReviewProcessManager(bus, clock, cmds, () => state);

        bus.PublishManaged(new TransitionStateIntent { TransactionId = Guid.NewGuid(), TargetState = ClusterState.OperatingReview });
        bus.SwapBuffers(); pm.Tick();
        Assert.Equal(1, cmds.Pauses);
        Assert.Empty(cmds.Snaps);                                 // ⛔ never a snap on the way in (F3)
        Assert.True(pm.InReview);

        clock.Mode = TimeMode.Deterministic;                      // the barrier landed
        pm.Tick();
        Assert.Equal(61.5, pm.LivePoint.TotalTime);

        clock.Now = new GlobalTime { TotalTime = 61.5, TotalWallTicks = 150 };   // paused frames went on being recorded: the wall moved on
        bus.PublishManaged(new ClusterStateTransitionedEvent { NewStateId = ClusterState.OperatingReview });   // every recorder has stopped
        bus.SwapBuffers(); pm.Tick();

        state = ClusterState.OperatingReview;
        clock.Now = new GlobalTime { TotalTime = 12.0, TotalWallTicks = 5 };   // the review moved the clock into the past
        cmds.Rates.Clear();
        bus.PublishManaged(new ClusterStateTransitionedEvent { NewStateId = ClusterState.OperatingLive });
        bus.SwapBuffers(); pm.Tick();
        var back = Assert.Single(cmds.Snaps);
        Assert.Equal(61.5, back.TotalTime);                       // live, paused, at L
        Assert.Equal(150, back.TotalWallTicks);                   // ⭐ F12 — the wall resumes where the recording stopped, never at L's (100)
        Assert.Equal(new[] { 2f }, cmds.Rates);                   // F19 — the live rate, not the review's
        Assert.False(pm.InReview);
    }

    private sealed class StillClock : ITimeController
    {
        public GlobalTime Now;
        public TimeMode Mode = TimeMode.Continuous;
        public float Rate = 1f;
        public GlobalTime Update() => Now;
        public void SetTimeScale(float scale) => Rate = scale;
        public float GetTimeScale() => Rate;
        public TimeMode GetMode() => Mode;
        public GlobalTime GetCurrentState() => Now;
        public void SeedState(GlobalTime state) { }
        public void Dispose() { }
    }

    private sealed class Commands : Fdp.Toolkit.Time.ITimeCommands
    {
        public int Pauses;
        public List<GlobalTime> Snaps { get; } = new();
        public List<float> Rates { get; } = new();
        public void Pause() => Pauses++;
        public void Resume() { }
        public void StepOneTick() { }
        public void StepBy(float seconds) { }
        public void SetTimeScale(float scale) => Rates.Add(scale);
        public void SnapTo(GlobalTime position) => Snaps.Add(position);
    }
}
