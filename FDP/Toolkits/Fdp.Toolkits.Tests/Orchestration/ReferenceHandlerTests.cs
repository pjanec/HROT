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

    private sealed class SeekCapturingController : IRecordReplayController
    {
        public long LastSeekTarget { get; private set; }
        public long ActiveRecordingStartWallTicks { get; set; }
        public Task<GlobalTime> SeekToTimeAsync(long targetWallClockTicks)
        {
            LastSeekTarget = targetWallClockTicks;
            return Task.FromResult(new GlobalTime { TotalWallTicks = ActiveRecordingStartWallTicks });
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
