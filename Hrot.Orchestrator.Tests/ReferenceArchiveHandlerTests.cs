using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FDP.Toolkit.Orchestration;
using FDP.Toolkit.Orchestration.Handlers;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// Unit tests for <see cref="ReferenceArchiveHandler"/> (CGF1-S0505 success conditions).
/// </summary>
[Collection("OrchestratorTests")]
public sealed class ReferenceArchiveHandlerTests
{
    // ── Stub transport ──────────────────────────────────────────────────────

    private sealed class StubTransport : IOrchestrationTransport
    {
        public readonly List<OrchestrationStatus> Published = new();

        public void PublishHeartbeat(int nodeId, string subsystemName, int localStateId, long wallTicksUtc) { }
        public void PublishStatus(OrchestrationStatus status) => Published.Add(status);
        public bool TryDequeueCommand(out OrchestrationCommand cmd) { cmd = default; return false; }
        public void Dispose() { }
    }

    private static OrchestrationCommand MakeCmd(string payload, Guid? txId = null) =>
        new(TransactionId: txId ?? Guid.NewGuid(),
            TargetNodeId:  1,
            OperationId:   ReferenceArchiveHandler.SerializeLocalOperationId,
            PayloadJson:   payload);

    // ── CGF1-S0505 Success Condition 2 ────────────────────────────────────────

    /// <summary>
    /// When the .fdp file exists, <see cref="ReferenceArchiveHandler.Commit"/> must
    /// publish a status with a ResultJson that deserialises to a FileManifestEntry[]
    /// containing the expected SourceUnc and RelativeDest.
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
            var transport = new StubTransport();
            var handler   = new ReferenceArchiveHandler(transport, tempRoot, nodeId);
            var txId      = Guid.NewGuid();
            var cmd       = MakeCmd($"{{\"ExerciseId\":\"{exerciseId}\"}}", txId);

            handler.Commit(cmd, null);

            Assert.Single(transport.Published);
            var status = transport.Published[0];
            Assert.Equal(txId,                          status.TransactionId);
            Assert.Equal(nodeId,                        status.NodeId);
            Assert.Equal(OrchestrationStatusCode.Success, status.StatusCode);

            // Deserialise ResultJson back to manifest shape.
            var entries = JsonSerializer.Deserialize<List<ManifestDto>>(status.ResultJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
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

            var handler = new ReferenceArchiveHandler(transport: null, tempRoot, nodeId);
            var cmd     = MakeCmd($"{{\"ExerciseId\":\"{exerciseId}\"}}");

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
    /// When the payload has no "ExerciseId" key, <see cref="ReferenceArchiveHandler.Commit"/>
    /// must exit silently without publishing any status and without throwing.
    /// </summary>
    [Fact]
    public void Commit_SkipsGracefully_WhenNoExerciseId()
    {
        var transport = new StubTransport();
        var handler   = new ReferenceArchiveHandler(transport, @"C:\FDP_Temp", nodeId: 1);
        var cmd       = MakeCmd("{\"SomeOtherKey\":\"value\"}");

        var ex = Record.Exception(() => handler.Commit(cmd, null));

        Assert.Null(ex);
        Assert.Empty(transport.Published);
    }

    // ── CanHandle ─────────────────────────────────────────────────────────────

    [Fact]
    public void CanHandle_ReturnsTrue_ForSerializeLocalId()
    {
        var handler = new ReferenceArchiveHandler(null, @"C:\FDP_Temp", 1);
        Assert.True(handler.CanHandle(ReferenceArchiveHandler.SerializeLocalOperationId));
    }

    [Fact]
    public void CanHandle_ReturnsFalse_ForOtherIds()
    {
        var handler = new ReferenceArchiveHandler(null, @"C:\FDP_Temp", 1);
        Assert.False(handler.CanHandle(4));   // TakeSnapshot
        Assert.False(handler.CanHandle(0));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>DTO mirroring the FileManifestEntry wire shape for JSON deserialisation.</summary>
    private sealed class ManifestDto
    {
        public string SourceUnc    { get; set; } = string.Empty;
        public string RelativeDest { get; set; } = string.Empty;
    }
}
