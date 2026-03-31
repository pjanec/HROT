using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hrot.Common.Orchestration;
using FDP.Toolkit.Orchestration;
using FDP.Toolkit.Orchestration.Handlers;
using Xunit;

namespace FDP.Toolkit.Orchestration.Tests;

/// <summary>
/// Unit tests for G0404 reference handlers.
/// </summary>
public sealed class ReferenceHandlerTests
{
    // ── Stubs ─────────────────────────────────────────────────────────────────

    private sealed class CapturingTransport : IOrchestrationTransport
    {
        public readonly List<OrchestrationStatus> PublishedStatuses = new();

        public void PublishHeartbeat(int nodeId, string subsystemName, int localStateId, long wallTicksUtc) { }
        public void PublishStatus(OrchestrationStatus status) => PublishedStatuses.Add(status);
        public bool TryDequeueCommand(out OrchestrationCommand cmd) { cmd = default; return false; }
        public void Dispose() { }
    }

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
    /// Fact: ReferencePrefetchHandler ACKs via transport.
    /// PrepareAsync + Commit causes PublishStatus to be called with
    /// StatusCode = OrchestrationStatusCode.Success.
    /// </summary>
    [Fact]
    public async Task ReferencePrefetchHandler_AcksViaTransport_OnCommit()
    {
        var root = Path.Combine(Path.GetTempPath(), $"TkOrcTests_{Guid.NewGuid():N}");
        try
        {
            var transport    = new CapturingTransport();
            var provider     = new LocalDiskStorageProvider(root);
            const int nodeId = 7;
            var handler      = new ReferencePrefetchHandler(transport, nodeId, provider);

            var txId = Guid.NewGuid();
            var cmd  = new OrchestrationCommand(
                txId, TargetNodeId: nodeId,
                OperationId: ReferencePrefetchHandler.PrefetchFilesOperationId,
                PayloadJson: "{\"ScenarioId\":\"test-scenario\"}");

            await handler.PrepareAsync(cmd, CancellationToken.None);
            handler.Commit(cmd, repo: null);

            Assert.Single(transport.PublishedStatuses);
            var status = transport.PublishedStatuses[0];
            Assert.Equal(txId,                             status.TransactionId);
            Assert.Equal(nodeId,                           status.NodeId);
            Assert.Equal(OrchestrationStatusCode.Success,  status.StatusCode);
            Assert.True(status.IsParticipating);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Handler does not ACK when transport is null (test-only wiring without DDS).
    /// </summary>
    [Fact]
    public async Task ReferencePrefetchHandler_NullTransport_NoException()
    {
        var root = Path.Combine(Path.GetTempPath(), $"TkOrcTests_{Guid.NewGuid():N}");
        try
        {
            var provider = new LocalDiskStorageProvider(root);
            var handler  = new ReferencePrefetchHandler(transport: null, nodeId: 1, provider);
            var cmd      = new OrchestrationCommand(
                Guid.NewGuid(), 1,
                ReferencePrefetchHandler.PrefetchFilesOperationId,
                "{\"ScenarioId\":\"s1\"}");

            await handler.PrepareAsync(cmd, CancellationToken.None);
            handler.Commit(cmd, repo: null); // must not throw
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
