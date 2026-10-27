using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Core.Orchestration;
using Xunit;

namespace Fdp.Toolkit.Orchestration.Tests;

/// <summary>
/// Unit tests for G0404 reference handlers.
/// </summary>
public sealed class ReferenceHandlerTests
{
    // ── LocalDiskStorageProvider ──────────────────────────────────────────────

    /// <summary>
    /// EnsureStagingDirectory creates the directory and returns the path.
    /// </summary>
    [Fact]
    public void LocalDiskStorageProvider_EnsureStagingDirectory_CreatesDir()
    {
        var root = Path.Combine(Path.GetTempPath(), $"TkOrcTests_{Guid.NewGuid():N}");
        try
        {
            var provider = new LocalDiskStorageProvider(root);
            var dir = provider.EnsureStagingDirectory("scenario-alpha");

            Assert.True(Directory.Exists(dir));
            // Production places scenarios under a "scenarios" subdirectory.
            Assert.Equal(Path.Combine(root, "scenarios", "scenario-alpha"), dir);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    // ── ReferencePrefetchHandler — G0404 success condition ────────────────────

    /// <summary>
    /// Fact: ReferencePrefetchHandler dispatched via ClusterSlave publishes
    /// NodeOpCompletedEvent with Success on the event bus.
    /// </summary>
    [Fact]
    public async Task ReferencePrefetchHandler_PublishesNodeOpCompletedEvent_OnCommit()
    {
        var root = Path.Combine(Path.GetTempPath(), $"TkOrcTests_{Guid.NewGuid():N}");
        try
        {
            var provider     = new LocalDiskStorageProvider(root);
            const int nodeId = 7;
            var handler      = new ReferencePrefetchHandler(provider);
            var eventBus     = new FdpEventBus();

            var txId = Guid.NewGuid();
            var intent = new ExecuteNodeOpIntent
            {
                TransactionId = txId,
                TargetNodeId  = nodeId,
                Operation     = NodeOpType.PrefetchFiles,
                DomainPayload = new PrefetchHandlerPayload("test-scenario"),
            };

            using var slave = new ClusterSlave(nodeId, "Test", eventBus);
            slave.RegisterHandler(handler);
            eventBus.PublishManaged(intent);
            eventBus.SwapBuffers();
            slave.Tick();
            eventBus.SwapBuffers();

            var completed = new List<NodeOpCompletedEvent>();
            foreach (var e in eventBus.ReadManaged<NodeOpCompletedEvent>())
                completed.Add(e);

            Assert.Single(completed);
            var status = completed[0];
            Assert.Equal(txId,                            status.TransactionId);
            Assert.Equal(nodeId,                          status.NodeId);
            Assert.Equal(OrchestrationStatusCode.Success, status.StatusCode);
            Assert.True(status.IsParticipating);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Handler with no bus does not throw — PrepareAsync + Commit is safe with null bus.
    /// </summary>
    [Fact]
    public async Task ReferencePrefetchHandler_NullBus_NoException()
    {
        var root = Path.Combine(Path.GetTempPath(), $"TkOrcTests_{Guid.NewGuid():N}");
        try
        {
            var provider = new LocalDiskStorageProvider(root);
            var handler  = new ReferencePrefetchHandler(provider);
            var intent   = new ExecuteNodeOpIntent
            {
                TransactionId = Guid.NewGuid(),
                TargetNodeId  = 1,
                Operation     = NodeOpType.PrefetchFiles,
                DomainPayload = new PrefetchHandlerPayload("s1"),
            };

            await handler.PrepareAsync(intent, CancellationToken.None);
            handler.Commit(intent, repo: null); // must not throw
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    // ── CE-497 — "seek to the end" must not wrap ────────────────────────────────

    /// <summary>
    /// ⭐ <c>CE-497</c> — a relative seek of <c>long.MaxValue</c> ("the end": the handler's own default and what scripts
    /// send) reaches the controller as the END, not as a wrapped negative tick. 🔴 Before: <c>start + MaxValue</c>
    /// overflowed, the playback clamped the negative target to frame 0, and "seek to the end" restored the FIRST frame.
    /// </summary>
    [Theory]
    [InlineData(long.MaxValue)]
    [InlineData(long.MaxValue - 1)]
    public async Task ReplaySeek_ToTheEnd_DoesNotWrapNegative(long relativeTicks)
    {
        var controller = new SeekCapturingController { ActiveRecordingStartWallTicks = 638_000_000_000_000_000L };
        var handler    = new ReferenceReplayLoadHandler(controller, null, null, null, null, null, Path.GetTempPath());

        await handler.PrepareAsync(new ExecuteNodeOpIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetNodeId  = 1,
            Operation     = NodeOpType.NodeReplaySeek,
            DomainPayload = new ReplaySeekPayload(relativeTicks),
        }, CancellationToken.None);

        Assert.Equal(long.MaxValue, controller.LastSeekTarget);
    }

    /// <summary>An ordinary relative seek is still start + offset.</summary>
    [Fact]
    public async Task ReplaySeek_RelativeOffset_IsAddedToTheRecordingStart()
    {
        var controller = new SeekCapturingController { ActiveRecordingStartWallTicks = 1_000_000L };
        var handler    = new ReferenceReplayLoadHandler(controller, null, null, null, null, null, Path.GetTempPath());

        await handler.PrepareAsync(new ExecuteNodeOpIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetNodeId  = 1,
            Operation     = NodeOpType.NodeReplaySeek,
            DomainPayload = new ReplaySeekPayload(5_000L),
        }, CancellationToken.None);

        Assert.Equal(1_005_000L, controller.LastSeekTarget);
    }

    // ── R-4012 (Q91 §4-P) — one replay timeline; a node outside its recording listens ──────────

    /// <summary>
    /// 🔴 Before C9 every node added its OWN recording start to the relative target, so a node that began recording late
    /// (a late Player) restored a moment shifted by its join delay.
    /// </summary>
    [Fact]
    public async Task R4012_A_seek_carrying_the_cluster_anchor_is_measured_from_it_not_from_this_nodes_start()
    {
        var controller = new SeekCapturingController { ActiveRecordingStartWallTicks = 10_000_000L };
        var handler    = new ReferenceReplayLoadHandler(controller, null, null, null, null, null, Path.GetTempPath());

        var result = await handler.PrepareAsync(new ExecuteNodeOpIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetNodeId  = 1,
            Operation     = NodeOpType.NodeReplaySeek,
            DomainPayload = new ReplaySeekPayload(7_000_000L, AnchorWallTicks: 4_000_000L),
        }, CancellationToken.None);

        Assert.Equal(11_000_000L, controller.LastSeekTarget);
        Assert.Equal(0.7, Assert.IsType<ReplaySeekResult>(result).RestoredTime.TotalTime, 6);
    }

    [Fact]
    public async Task R4012_A_seek_outside_this_nodes_recording_reports_no_landed_time()
    {
        var controller = new SeekCapturingController { ActiveRecordingStartWallTicks = 10_000_000L, Covers = false };
        var handler    = new ReferenceReplayLoadHandler(controller, null, null, null, null, null, Path.GetTempPath());

        var result = await handler.PrepareAsync(new ExecuteNodeOpIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetNodeId  = 1,
            Operation     = NodeOpType.NodeReplaySeek,
            DomainPayload = new ReplaySeekPayload(0L, AnchorWallTicks: 4_000_000L),
        }, CancellationToken.None);

        Assert.Equal(0L, Assert.IsType<ReplaySeekResult>(result).RestoredTime.TotalWallTicks);
    }

    [Fact]
    public async Task R4012_PrepareReplay_reports_this_nodes_recorded_span()
    {
        var controller = new SeekCapturingController { ActiveRecordingStartWallTicks = 10_000_000L, ActiveRecordingEndWallTicks = 90_000_000L };
        var handler    = new ReferenceReplayLoadHandler(controller, null, null, null, null, null, Path.GetTempPath());

        var result = await handler.PrepareAsync(new ExecuteNodeOpIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetNodeId  = 1,
            Operation     = NodeOpType.PrepareReplay,
            DomainPayload = Guid.NewGuid(),
        }, CancellationToken.None);

        var span = Assert.IsType<ReplayPrepareResult>(result);
        Assert.Equal(10_000_000L, span.FirstFrameWallTicks);
        Assert.Equal(90_000_000L, span.LastFrameWallTicks);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3329</c> PW — §12c W2: a replay seek holds this node's egress BEFORE the world swaps and keeps it held after the node
    /// answers; only the master's next cluster-state broadcast (sent when every node answered) releases it — once.
    /// </summary>
    [Fact]
    public async Task W2_A_replay_seek_holds_egress_until_the_masters_commit_broadcast()
    {
        var controller = new SeekCapturingController { ActiveRecordingStartWallTicks = 1_000_000L };
        var handler    = new ReferenceReplayLoadHandler(controller, null, null, null, null, null, Path.GetTempPath());

        handler.OnClusterStateUpdate(ClusterState.OperatingReplay, masterGone: false);   // a broadcast before any seek: nothing
        Assert.Equal(0, controller.Releases);

        await handler.PrepareAsync(new ExecuteNodeOpIntent
        {
            TransactionId = Guid.NewGuid(),
            TargetNodeId  = 1,
            Operation     = NodeOpType.NodeReplaySeek,
            DomainPayload = new ReplaySeekPayload(5_000L),
        }, CancellationToken.None);
        Assert.Equal(new[] { "hold" }, controller.HoldLog);   // held before the seek, still held after the answer

        handler.OnClusterStateUpdate(ClusterState.OperatingReplay, masterGone: false);   // the master's commit
        handler.OnClusterStateUpdate(ClusterState.OperatingReplay, masterGone: false);
        Assert.Equal(new[] { "hold", "release" }, controller.HoldLog);
    }

    // ── CE-3329 P3 — During Action Review on a node ─────────────────────────────

    private sealed class SpyEpisodeRecorder : Fdp.Toolkit.Replay.IReviewSuspendable
    {
        public (string? Label, double At)? Suspended;
        public double? ResumedAt;
        public void SuspendForReview(string? label = null, double atSimSeconds = 0) => Suspended = (label, atSimSeconds);
        public void ResumeAfterReview(double? atSimSeconds = null) => ResumedAt = atSimSeconds;
    }

    private sealed class CapturingRegistry : Fdp.ModuleHost.Abstractions.ISystemRegistry
    {
        public List<Fdp.ModuleHost.Abstractions.IEcsModuleSystem> Systems { get; } = new();
        public void RegisterSystem<T>(T system) where T : Fdp.ModuleHost.Abstractions.IEcsModuleSystem => Systems.Add(system);
        public Fdp.ModuleHost.Abstractions.IEcsModuleSystem RegisterManualSystem<T>(T system) where T : Fdp.ModuleHost.Abstractions.IEcsModuleSystem { Systems.Add(system); return system; }
    }

    /// <summary>
    /// ⭐⭐⭐ CE-3329 P3 (§3c, F1, F11, D-B, D-C) — live → review → live leaves the world IDENTICAL, singletons included: the review
    /// forces a labelled keyframe of the live point into the recording, suspends the recorder (nothing of the review is written),
    /// keeps the FULL checkpoint in RAM, and the return puts it back with the checkpoint's semantics and resumes the SAME recording.
    /// 🔴 The dry run's capture (NodeDryRun) would have dropped the singleton and come back with the replayed value.
    /// </summary>
    [Fact(Timeout = 20_000)]
    public async Task CE3329_Live_review_live_leaves_the_world_identical_and_the_recording_resumable()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ce3329-review-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using var world = new EntityRepository();
            world.RegisterComponent<Fdp.Toolkits.Tests.Replay.KeyframeTestPos>();
            var unit = world.CreateEntity();
            world.AddComponent(unit, new Fdp.Toolkits.Tests.Replay.KeyframeTestPos { X = 7f });
            world.SetSingletonUnmanaged(new Fdp.Toolkits.Tests.Replay.KeyframeTestWorldState { Value = 42 });
            world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 12.0, DeltaTime = 0f, TimeScale = 1f, TotalWallTicks = 5_000 });   // paused

            var recording = new Fdp.Toolkit.Replay.RecordingModule(new Fdp.Toolkit.Replay.RecordingConfiguration
            {
                FilePath = Path.Combine(dir, "node.fdp"), ExerciseId = Guid.NewGuid(), NodeId = 1, Blocking = true,
            });
            var registry = new CapturingRegistry();
            recording.RegisterSystems(registry);
            void Frame() { world.Tick(); foreach (var sys in registry.Systems) sys.Execute(world, 0f); }
            Frame();   // the recording's first (key)frame

            var controller = new SeekCapturingController();
            var gates = new ReferenceReplayLoadHandler(controller, null, null, null, null, null, dir);
            var episode = new SpyEpisodeRecorder();   // ⭐ row 44
            var review = new ReferenceReviewHandler(controller, () => recording, gates, world, accumulator: null, worldReplaced: null,
                                                    resumeSchedule: null, storageDirectory: dir, episodeRecorders: () => new[] { episode });

            // ── ENTER: the handler waits for its keyframe — the "frames" keep coming meanwhile ──
            var enter = new ExecuteNodeOpIntent { TransactionId = Guid.NewGuid(), TargetNodeId = 1, Operation = NodeOpType.PrepareReview };
            var prepare = review.PrepareAsync(enter, CancellationToken.None);
            while (!prepare.IsCompleted) { Frame(); await Task.Delay(2); }
            Assert.IsType<ReplayPrepareResult>(await prepare);
            review.Commit(enter, null);
            Assert.True(review.InReview);
            Assert.Equal("review-1", review.LastReviewLabel);
            Assert.NotNull(recording.FindCheckpoint("review-1"));                                   // D-C: the live point, recorded
            Assert.True(Fdp.Toolkit.Replication.Services.EgressHold.IsHeld(world));                // W2
            Assert.Equal(("review-1", 12.0), episode.Suspended);                                    // ⭐ row 44 — an episode's history too

            // ── THE REVIEW: the playback rewrites the world (a moved unit, a unit that did not exist at L, a past singleton) ──
            world.GetComponentRW<Fdp.Toolkits.Tests.Replay.KeyframeTestPos>(unit).X = -1f;
            var ghost = world.CreateEntity();
            world.AddComponent(ghost, new Fdp.Toolkits.Tests.Replay.KeyframeTestPos { X = 99f });
            world.SetSingletonUnmanaged(new Fdp.Toolkits.Tests.Replay.KeyframeTestWorldState { Value = 3 });
            long sizeBefore = new FileInfo(Path.Combine(dir, "node.fdp")).Length;
            Frame(); Frame();
            recording.WaitForWrites();
            Assert.Equal(sizeBefore, new FileInfo(Path.Combine(dir, "node.fdp")).Length);          // R8: nothing of the review written

            // ── RETURN ──
            var exit = new ExecuteNodeOpIntent { TransactionId = Guid.NewGuid(), TargetNodeId = 1, Operation = NodeOpType.FinalizeReview };
            await review.PrepareAsync(exit, CancellationToken.None);
            review.Commit(exit, null);

            Assert.False(review.InReview);
            Assert.Equal(1, controller.ExitReleases);                                                // ⭐ W5 — the gates' release discards the readers
            Assert.Equal(12.0, episode.ResumedAt);                                                   // ⭐ row 44 — resumed at L
            Assert.Equal(7f, world.GetComponentRO<Fdp.Toolkits.Tests.Replay.KeyframeTestPos>(unit).X);
            Assert.False(world.IsAlive(ghost));
            Assert.Equal(42, world.GetSingletonUnmanaged<Fdp.Toolkits.Tests.Replay.KeyframeTestWorldState>().Value);   // F1

            // 📌 measured live: the kernel's clock push resumes before the MASTER snaps the clock back — the review's clock reaches the
            //   world for a few frames (and the FIRST frame carries L from the restored singleton). Nothing may be recorded until the clock
            //   STANDS at L.
            Frame();                                                                                  // the restored singleton: L, once
            world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 3.5, TotalWallTicks = 6_000 });
            Frame(); Frame();
            recording.WaitForWrites();
            Assert.Equal(sizeBefore, new FileInfo(Path.Combine(dir, "node.fdp")).Length);
            world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 12.0, TotalWallTicks = 6_100 });   // the master's SnapTo(L) landed
            for (int i = 0; i < Fdp.Toolkit.Replay.RecorderTickSystem.FramesAtLivePointToResume; i++) Frame();
            recording.WaitForWrites();
            Assert.True(new FileInfo(Path.Combine(dir, "node.fdp")).Length > sizeBefore);            // the SAME recording goes on
            recording.Dispose();
            // ⭐ row 17 — the meta says where the review was: its label, the live point and how long it lasted
            var meta = Fdp.Core.FlightRecorder.Metadata.MetadataSerializer.Deserialize(File.ReadAllText(Path.Combine(dir, "node.fdp") + ".meta.json"));
            var mark = Assert.Single(meta.Reviews);
            Assert.Equal("review-1", mark.Label);
            Assert.Equal(12.0, mark.AtSimSeconds);
            Assert.True(mark.AtWallTicks > 0);
            Assert.NotNull(mark.EndedUtc);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    /// <summary>
    /// ⭐⭐ CE-3329 F6 (DESIGN_Replay_Time_Axis_And_Review §12u) — the QUIESCE: what was in flight when the cluster paused lands in the
    /// live point, because the entry lets <see cref="ReferenceReviewHandler.QuiesceFrames"/> paused frames pass before it captures.
    /// Here a sample sent before the pause is taken in on the SECOND paused frame; the review then rewrites the unit, and the return
    /// must bring back the landed value. 🔴 Captured at once, the live point misses it and the return restores the stale one.
    /// </summary>
    [Fact(Timeout = 20_000)]
    public async Task CE3329_F6_The_entry_quiesces_so_what_was_in_flight_lands_in_the_live_point()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ce3329-f6-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using var world = new EntityRepository();
            world.RegisterComponent<Fdp.Toolkits.Tests.Replay.KeyframeTestPos>();
            var unit = world.CreateEntity();
            world.AddComponent(unit, new Fdp.Toolkits.Tests.Replay.KeyframeTestPos { X = 7f });
            world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 12.0, DeltaTime = 0f, TimeScale = 1f, TotalWallTicks = 5_000 });   // paused

            var recording = new Fdp.Toolkit.Replay.RecordingModule(new Fdp.Toolkit.Replay.RecordingConfiguration
            {
                FilePath = Path.Combine(dir, "node.fdp"), ExerciseId = Guid.NewGuid(), NodeId = 1, Blocking = true,
            });
            var registry = new CapturingRegistry();
            recording.RegisterSystems(registry);
            void Frame() { world.Tick(); foreach (var sys in registry.Systems) sys.Execute(world, 0f); }
            Frame();

            var controller = new SeekCapturingController();
            var review = new ReferenceReviewHandler(controller, () => recording, new ReferenceReplayLoadHandler(controller, null, null, null, null, null, dir),
                                                    world, accumulator: null, worldReplaced: null, resumeSchedule: null, storageDirectory: dir);

            var enter = new ExecuteNodeOpIntent { TransactionId = Guid.NewGuid(), TargetNodeId = 1, Operation = NodeOpType.PrepareReview };
            var prepare = review.PrepareAsync(enter, CancellationToken.None);
            int paused = 0;
            while (!prepare.IsCompleted)
            {
                await Task.Delay(40);   // ⚠ the handler polls every 10 ms: it reacts to each frame before the next one
                if (++paused == 2) world.GetComponentRW<Fdp.Toolkits.Tests.Replay.KeyframeTestPos>(unit).X = 55f;   // the in-flight sample lands
                Frame();
            }
            await prepare;
            review.Commit(enter, null);

            world.GetComponentRW<Fdp.Toolkits.Tests.Replay.KeyframeTestPos>(unit).X = -1f;   // the review's playback
            var exit = new ExecuteNodeOpIntent { TransactionId = Guid.NewGuid(), TargetNodeId = 1, Operation = NodeOpType.FinalizeReview };
            await review.PrepareAsync(exit, CancellationToken.None);
            review.Commit(exit, null);

            Assert.Equal(55f, world.GetComponentRO<Fdp.Toolkits.Tests.Replay.KeyframeTestPos>(unit).X);
            Assert.True(paused > ReferenceReviewHandler.QuiesceFrames);
            recording.Dispose();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    /// <summary>
    /// ⭐⭐ CE-3337 — a branch from a replay is a restore IN PLACE (D18): its commit counts a restore, so every per-process counter a
    /// restore resumes from the world does — here the muscle's route handles, which go on from the branch frame's recorded copy, not from
    /// where the original live run left the process. 🔴 Before: no restore was counted; the coordinator's H3 saw the branch number its next
    /// routes 0x40000026 where the straight run stood at 0x40000004.
    /// </summary>
    [Fact]
    public void CE3337_A_branch_commit_counts_a_restore_and_the_route_handles_resume_from_the_branch_frame()
    {
        using var world = new EntityRepository();
        var controller = new SeekCapturingController();
        var handler = new ReferenceReplayLoadHandler(controller, null, null, null, null, null, ".", world: world);
        using var pool = new global::CarKinem.Trajectory.TrajectoryPoolManager();
        var restore = new global::CarKinem.Systems.TrajectoryRestoreSystem(pool);

        // the live run went on far past the branch frame; the recorded copy at the frame on screen (5 s) stood lower
        int liveEnd = Fdp.Toolkit.Navigation.NavigationHandleAllocator.Allocate() + 5000;
        Fdp.Toolkit.Navigation.NavigationHandleAllocator.EnsureAbove(liveEnd);
        int atBranch = liveEnd - 30;
        world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 5.0 });
        world.SetSingletonUnmanaged(new Fdp.Toolkit.Navigation.NavigationHandleMark { Last = atBranch, At = 5.0 });
        // ⚠ the system does not run during the replay (its group is off), so it never mirrored the live counter over the recorded copy
        int before = Fdp.Toolkit.Replication.Services.WorldEpoch.RestoresOf(world);

        // ⚠ repo: null — exactly as ClusterSlave commits every op (my first rail passed the world here and hid that the live path got none)
        handler.Commit(new ExecuteNodeOpIntent { TransactionId = Guid.NewGuid(), TargetNodeId = 1, Operation = NodeOpType.PrepareLive }, repo: null);

        Assert.Equal(before + 1, Fdp.Toolkit.Replication.Services.WorldEpoch.RestoresOf(world));   // ⭐ a restore, counted
        restore.Execute(world, 1f / 60f);                                                             // the branch's first step
        Assert.Equal(atBranch + 1, Fdp.Toolkit.Navigation.NavigationHandleAllocator.Allocate());      // ⭐ numbered on from the branch frame
    }

    /// <summary>
    /// ⭐⭐ CE-3337 — a node that restores the branch frame from its own recording answers the branch with that frame's RECORDED step count
    /// (<c>FrameNumber</c>, the counter perception stamps sightings with), and says so; a listening node does not claim it.
    /// 🔴 Before: the replay clock's own frame count went back, and every sighting after the branch was stamped on a shifted count.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CE3337_A_branch_answers_with_the_recorded_step_count_of_the_frame_on_screen(bool restoring)
    {
        using var world = new EntityRepository();
        world.SetSingletonUnmanaged(new GlobalTime { TotalTime = 5.0, FrameNumber = 300 });   // the recorded frame: step 300
        var controller = new SeekCapturingController { IsRestoringReplay = restoring };
        var handler = new ReferenceReplayLoadHandler(controller, null, null, null, null, null, ".", world: world);

        var result = await handler.PrepareAsync(new ExecuteNodeOpIntent
        {
            TransactionId = Guid.NewGuid(), TargetNodeId = 1, Operation = NodeOpType.PrepareLive, DomainPayload = Guid.NewGuid(),
        }, CancellationToken.None);

        var branch = Assert.IsType<LiveBranchResult>(result);
        Assert.Equal(restoring, branch.FromRecording);
        if (restoring) Assert.Equal(300, branch.HistoricalTime.FrameNumber);
    }

    /// <summary>⭐ CE-3329 D-C / F4 — a node that drops to Idle during a review abandons it: the RAM checkpoint dropped, the gates off,
    /// the egress released; the way back is the recording's labelled keyframe.</summary>
    [Fact]
    public void CE3329_A_review_never_entered_ignores_idle_and_its_ops_are_its_own()
    {
        var controller = new SeekCapturingController();
        using var world = new EntityRepository();
        var review = new ReferenceReviewHandler(controller, () => null, new ReferenceReplayLoadHandler(controller, null, null, null, null, null, "."),
                                                world, null, null, null, ".");
        Assert.True(review.CanHandle(NodeOpType.PrepareReview));
        Assert.True(review.CanHandle(NodeOpType.FinalizeReview));
        Assert.False(review.CanHandle(NodeOpType.PrepareReplay));
        review.OnEnteredIdle();
        Assert.False(review.InReview);
    }

    private sealed class SeekCapturingController : IRecordReplayController
    {
        public System.Collections.Generic.List<string> HoldLog { get; } = new();
        public int Releases => HoldLog.FindAll(x => x == "release").Count;
        public void HoldEgress(string reason) => HoldLog.Add(LastSeekTarget == 0 ? "hold" : "hold-after-seek");
        public void ReleaseEgress() => HoldLog.Add("release");
        public int ExitReleases { get; private set; }
        public void ReleaseReplayExit() => ExitReleases++;   // ⭐ CE-3329 W5
        public long LastSeekTarget { get; private set; }
        public long ActiveRecordingStartWallTicks { get; set; }
        public long ActiveRecordingEndWallTicks { get; set; }
        private long _anchor;
        /// <summary>Like the real controller: the cluster's anchor once set, else this recording's start.</summary>
        public long ReplayAnchorWallTicks { get => _anchor != 0 ? _anchor : ActiveRecordingStartWallTicks; set => _anchor = value; }
        public string? ActiveReplayFilePath => null;
        public bool IsRestoringReplay { get; set; }
        /// <summary>R-4012 — when false the fake's recording does not cover the moment: it restores nothing.</summary>
        public bool Covers { get; set; } = true;
        public Task<GlobalTime> SeekToTimeAsync(long targetWallClockTicks)
        {
            LastSeekTarget = targetWallClockTicks;
            IsRestoringReplay = Covers;
            return Task.FromResult(Covers ? new GlobalTime { TotalWallTicks = targetWallClockTicks } : default);
        }
        public Task PrepareRecordingAsync(Guid exerciseId, string storageDirectory) => Task.CompletedTask;
        public Task FinalizeRecordingAsync(long maxNetworkId = 0) => Task.CompletedTask;
        public Task PrepareReplayAsync(Guid exerciseId, string storageDirectory) => Task.CompletedTask;
        public void ProcessPlaybackTick(GlobalTime currentTime) { }
        public Task TeardownReplayAsync() => Task.CompletedTask;
        public bool IsReplayActive => true;
        public GlobalTime GetCurrentReplayTime() => default;
        public float ActiveReplayDurationSeconds => 0f;
        public long ActiveMaxNetworkId => 0;
    }
}
