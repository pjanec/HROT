using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
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
            Assert.Equal(Path.Combine(root, "scenario-alpha"), dir);
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
            foreach (var e in eventBus.ConsumeManaged<NodeOpCompletedEvent>())
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
}
