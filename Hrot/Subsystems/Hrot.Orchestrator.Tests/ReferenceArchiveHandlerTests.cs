using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
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
    private static ExecuteNodeOpIntent MakeCmd(Guid exerciseId, Guid? txId = null) =>
        new()
        {
            TransactionId = txId ?? Guid.NewGuid(),
            // 0 = broadcast: a fixed non-zero id was dropped by any slave whose nodeId differed
            //   (ClusterSlave.Tick's TargetNodeId filter) — the pre-existing reason Commit saw an empty result.
            TargetNodeId  = 0,
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
        var exerciseId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        const int    nodeId  = 5;
        var exerciseIdText = exerciseId.ToString();

        // CE-279: the handler reads/reports under the `exercises/` segment (OrchestrationConstants); the test
        //   fixture must mirror that. (Pre-existing stale test — it omitted the segment the handler adopted.)
        var exerciseDir  = Path.Combine(tempRoot, OrchestrationConstants.ExercisesDirectoryName, exerciseIdText);
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
            foreach (var e in eventBus.ReadManaged<NodeOpCompletedEvent>())
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
            Assert.Equal(Path.Combine(OrchestrationConstants.ExercisesDirectoryName, exerciseIdText, $"node_{nodeId}.fdp"), entry.RelativeDest);
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
        var exerciseId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        const int    nodeId  = 3;
        var exerciseIdText = exerciseId.ToString();

        // CE-279: mirror the handler's `exercises/` segment (Abort deletes under it too).
        var exerciseDir = Path.Combine(tempRoot, OrchestrationConstants.ExercisesDirectoryName, exerciseIdText);
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
        var cmd     = MakeCmd(Guid.Empty); // null exerciseId → handler skips gracefully

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

    // ── CE-279 Layer A/C — payload-aware SerializeLocal selection via SerializeLocalRegistrar ────────────

    /// <summary>
    /// ⭐⭐⭐ REGRESSION: `SerializeLocal` is shared by the `.fdp` archive and the scenario-JSON save; the slave
    /// dispatches to the FIRST `CanHandle`-true handler. A payload-BLIND `CanHandle` let the archive handler
    /// SWALLOW every scenario slice (measured live 2026-09-14). This proves the CE-279 fix: registered together
    /// via <see cref="SerializeLocalRegistrar"/>, a scenario payload goes to the scenario handler and an archive
    /// payload goes to the archive handler — regardless of the archive handler being registered.
    /// </summary>
    [Fact]
    public void SerializeLocalRegistrar_RoutesByPayload_ArchiveDoesNotShadowScenario()
    {
        var archive  = new ReferenceArchiveHandler(Path.GetTempPath(), nodeId: 1);
        var scenario = new ScenarioPayloadClaimer();

        // The registrar puts scenario-save first, archive second — the canonical order every host now uses.
        var eventBus = new FdpEventBus();
        using var slave = new ClusterSlave(1, "Test", eventBus);
        SerializeLocalRegistrar.Register(slave, scenario, archive);

        // Payload-aware selection: a scenario payload is claimed by the scenario handler, NOT the archive one.
        Assert.False(archive.CanHandle(new ExecuteNodeOpIntent
        {
            Operation = NodeOpType.SerializeLocal,
            DomainPayload = new Fdp.Toolkit.Orchestration.Handlers.ScenarioSaveHandlerPayload("scn"),
        }), "archive handler must DECLINE a scenario payload");
        Assert.True(archive.CanHandle(new ExecuteNodeOpIntent
        {
            Operation = NodeOpType.SerializeLocal,
            DomainPayload = new ArchiveHandlerPayload(Guid.NewGuid()),
        }), "archive handler must still claim its OWN archive payload");
        Assert.True(scenario.CanHandle(new ExecuteNodeOpIntent
        {
            Operation = NodeOpType.SerializeLocal,
            DomainPayload = new Fdp.Toolkit.Orchestration.Handlers.ScenarioSaveHandlerPayload("scn"),
        }), "scenario handler must claim the scenario payload");
    }

    /// <summary>Minimal handler claiming ONLY a <c>ScenarioSaveHandlerPayload</c> (mirrors the real scenario
    /// save handlers' CE-279 override), to prove the archive handler yields to it.</summary>
    private sealed class ScenarioPayloadClaimer : IClusterStateHandler
    {
        public bool CanHandle(NodeOpType operation) => operation == NodeOpType.SerializeLocal;
        public bool CanHandle(ExecuteNodeOpIntent intent) =>
            intent.Operation == NodeOpType.SerializeLocal
            && intent.DomainPayload is Fdp.Toolkit.Orchestration.Handlers.ScenarioSaveHandlerPayload;
        public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct) => Task.FromResult<object?>(null);
        public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo) { }
        public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo) { }
    }

}
