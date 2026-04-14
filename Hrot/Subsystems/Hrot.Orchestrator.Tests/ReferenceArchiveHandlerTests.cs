using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Kernel;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// Unit tests for <see cref="ReferenceArchiveHandler"/> (CGF1-S0505 success conditions).
/// </summary>
[Collection("OrchestratorTests")]
public sealed class ReferenceArchiveHandlerTests
{
    private static ExecuteNodeOpIntent MakeCmd(string? exerciseId, Guid? txId = null) =>
        new()
        {
            TransactionId = txId ?? Guid.NewGuid(),
            TargetNodeId  = 1,
            Operation     = NodeOpType.SerializeLocal,
            DomainPayload = new ArchiveHandlerPayload(exerciseId),
        };

    // ── CGF1-S0505 Success Condition 2 ────────────────────────────────────────

    /// <summary>
    /// When the .fdp file exists, <see cref="ReferenceArchiveHandler"/> dispatched through
    /// ClusterSlave must publish a NodeOpCompletedEvent whose ResultPayload is a
    /// FileManifestResult[] containing the expected SourceUnc and RelativeDest.
    /// </summary>
    [Fact]
    public void Commit_ProducesManifestJson_WhenFdpExists()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        const string exerciseId = "test_exercise_01";
        const int    nodeId  = 5;

        var exerciseDir  = Path.Combine(tempRoot, exerciseId);
        var fdpFile   = Path.Combine(exerciseDir, $"node_{nodeId}.fdp");
        Directory.CreateDirectory(exerciseDir);
        File.WriteAllText(fdpFile, "fake-fdp-data");

        try
        {
            var eventBus = new FdpEventBus();
            var handler  = new ReferenceArchiveHandler(tempRoot, nodeId);
            var txId     = Guid.NewGuid();
            var cmd      = MakeCmd(exerciseId, txId);

            using var slave = new ClusterSlave(nodeId, "Test", eventBus);
            slave.RegisterHandler(handler);
            eventBus.PublishManaged(cmd);
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

            // Check ResultPayload is a FileManifestResult array.
            var entries = status.ResultPayload as FileManifestResult[];
            Assert.NotNull(entries);
            Assert.Single(entries!);
            var entry = entries![0];
            Assert.Equal(fdpFile, entry.SourceUnc);
            Assert.Equal(Path.Combine(exerciseId, $"node_{nodeId}.fdp"), entry.RelativeDest);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    // ── CGF1-S0505 Success Condition 3 ────────────────────────────────────────

    /// <summary>
    /// When the .fdp file exists and <see cref="ReferenceArchiveHandler.Abort"/> is called,
    /// the file must be deleted from disk.
    /// </summary>
    [Fact]
    public void Abort_DeletesPartialFdpFile()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        const string exerciseId = "abort_exercise";
        const int    nodeId  = 3;

        var exerciseDir = Path.Combine(tempRoot, exerciseId);
        var fdpFile  = Path.Combine(exerciseDir, $"node_{nodeId}.fdp");
        Directory.CreateDirectory(exerciseDir);
        File.WriteAllText(fdpFile, "partial-data");

        try
        {
            Assert.True(File.Exists(fdpFile), "Pre-condition: file must exist before Abort.");

            var handler = new ReferenceArchiveHandler(tempRoot, nodeId);
            var cmd     = MakeCmd(exerciseId);

            handler.Abort(cmd, null);

            Assert.False(File.Exists(fdpFile), "Abort must delete the partial .fdp file.");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    // ── Guard: no ExerciseId in payload ──────────────────────────────────────────

    /// <summary>
    /// When the payload has no ExerciseId, PrepareAsync returns null and no event is published.
    /// </summary>
    [Fact]
    public void Commit_SkipsGracefully_WhenNoExerciseId()
    {
        var handler = new ReferenceArchiveHandler(@"C:\FDP_Temp", nodeId: 1);
        var cmd     = MakeCmd(null); // null exerciseId → handler skips gracefully

        var ex = Record.Exception(() => handler.PrepareAsync(cmd, default).GetAwaiter().GetResult());

        Assert.Null(ex);
    }

    // ── CanHandle ─────────────────────────────────────────────────────────────

    [Fact]
    public void CanHandle_ReturnsTrue_ForSerializeLocalId()
    {
        var handler = new ReferenceArchiveHandler(@"C:\FDP_Temp", 1);
        Assert.True(handler.CanHandle(NodeOpType.SerializeLocal));
    }

    [Fact]
    public void CanHandle_ReturnsFalse_ForOtherIds()
    {
        var handler = new ReferenceArchiveHandler(@"C:\FDP_Temp", 1);
        Assert.False(handler.CanHandle(NodeOpType.TakeSnapshot));   // TakeSnapshot
        Assert.False(handler.CanHandle((NodeOpType)0));
    }

}
