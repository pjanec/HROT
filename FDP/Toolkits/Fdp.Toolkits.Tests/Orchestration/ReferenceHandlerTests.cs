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

    private sealed class SeekCapturingController : IRecordReplayController
    {
        public System.Collections.Generic.List<string> HoldLog { get; } = new();
        public int Releases => HoldLog.FindAll(x => x == "release").Count;
        public void HoldEgress(string reason) => HoldLog.Add(LastSeekTarget == 0 ? "hold" : "hold-after-seek");
        public void ReleaseEgress() => HoldLog.Add("release");
        public long LastSeekTarget { get; private set; }
        public long ActiveRecordingStartWallTicks { get; set; }
        public long ActiveRecordingEndWallTicks { get; set; }
        private long _anchor;
        /// <summary>Like the real controller: the cluster's anchor once set, else this recording's start.</summary>
        public long ReplayAnchorWallTicks { get => _anchor != 0 ? _anchor : ActiveRecordingStartWallTicks; set => _anchor = value; }
        public string? ActiveReplayFilePath => null;
        public bool IsRestoringReplay { get; private set; }
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
