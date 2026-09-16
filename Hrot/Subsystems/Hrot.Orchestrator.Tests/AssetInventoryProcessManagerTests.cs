using System;
using System.IO;
using System.Text.Json;
using Fdp.Core;
using Fdp.Core.Serialization;
using Fdp.Toolkit.Orchestration;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// CE-278 §5 — the archived-exercise metadata sidecar (<c>Orchestrator.json</c>) is written on the Export
/// path, replacing the retired SaveScenario=2 op that used to write it. This is the feature's own rail for
/// the re-home (AssetInventoryProcessManager had no suite before).
/// </summary>
[Collection("OrchestratorTests")]
public sealed class AssetInventoryProcessManagerTests
{
    /// <summary>
    /// On ExportArchive success (the real production completion signal <see cref="ClusterOpCompletedEvent"/>),
    /// the manager writes the sidecar into the NAS exercise dir beside the <c>.fdp</c> recordings, in the
    /// <see cref="GlobalContextDto"/>/<c>$meta</c> shape <see cref="StorageGatewayModule.ScanNasExercises"/>
    /// reads back (scenarioId + duration), and then evicts the local ledger entry.
    /// </summary>
    [Fact]
    public void ExportComplete_WritesExerciseSidecar_ReadableByScanNasExercises()
    {
        var nasRoot = Path.Combine(Path.GetTempPath(), "fdp_ce278_nas_" + Guid.NewGuid().ToString("N"));
        var staging = Path.Combine(Path.GetTempPath(), "fdp_ce278_stg_" + Guid.NewGuid().ToString("N"));
        const int nodeId = 400;

        try
        {
            var exerciseId = Guid.NewGuid();
            const string scenarioId = "hill-attack";
            var start    = DateTime.UtcNow.AddMinutes(-3);
            var duration = TimeSpan.FromSeconds(150);

            // Arrange: a hydrated ledger entry, as if a recording had completed (start/duration/scenarioId).
            var ledgerDir = Path.Combine(
                OrchestrationConstants.GetNodeStagingRoot(staging, nodeId), "recording_ledger");
            Directory.CreateDirectory(ledgerDir);
            var entry = new RecordingLedgerEntry(exerciseId, scenarioId, start, duration);
            File.WriteAllText(
                Path.Combine(ledgerDir, $"{exerciseId}.json"),
                JsonSerializer.Serialize(entry, FdpJsonOptionsRegistry.Indented));

            // Arrange: the archived .fdp already pulled to the NAS exercise dir (ScanNasExercises requires one).
            var nasExerciseDir = Path.Combine(
                nasRoot, OrchestrationConstants.ExercisesDirectoryName, exerciseId.ToString());
            Directory.CreateDirectory(nasExerciseDir);
            File.WriteAllText(Path.Combine(nasExerciseDir, "rec_node_400.fdp"), "fdp-bytes");

            var bus     = new FdpEventBus();
            var gateway = new StorageGatewayModule();
            var manager = new AssetInventoryProcessManager(bus, gateway, nasRoot, staging, nodeId);

            var requestId = Guid.NewGuid();

            // Act 1: the operator's Export intent → the manager records requestId → exerciseId.
            bus.PublishManaged(new ExecuteStorageOpIntent
            {
                RequestId  = requestId,
                Operation  = StorageOpType.Export,
                ExerciseId = exerciseId,
            });
            bus.SwapBuffers();
            manager.Tick();

            // Act 2: the export completes — the production completion signal that drives the re-home.
            bus.PublishManaged(new ClusterOpCompletedEvent
            {
                RequestId  = requestId,
                StatusCode = OrchestrationStatusCode.Success,
            });
            bus.SwapBuffers();
            manager.Tick();

            // Assert: the sidecar was written beside the .fdp, in the GlobalContextDto/$meta shape.
            var sidecar = Path.Combine(nasExerciseDir, "Orchestrator.json");
            Assert.True(File.Exists(sidecar),
                "CE-278 §5: the exercise sidecar must be written on export success.");
            using (var doc = JsonDocument.Parse(File.ReadAllText(sidecar)))
            {
                var root = doc.RootElement;
                Assert.True(root.TryGetProperty("$meta", out var meta), "$meta envelope must be present");
                Assert.Equal("Hrot.OrchestratorContext", meta.GetProperty("docType").GetString());
                Assert.Equal(scenarioId, root.GetProperty("scenarioId").GetString());
            }

            // Assert: ScanNasExercises reads it back with the restored scenarioId + duration.
            var scanned = gateway.ScanNasExercises(
                Path.Combine(nasRoot, OrchestrationConstants.ExercisesDirectoryName));
            var item = Assert.Single(scanned);
            Assert.Equal(exerciseId, item.ExerciseId);
            Assert.Equal(scenarioId, item.ScenarioId);
            Assert.Equal(duration, item.Duration);
        }
        finally
        {
            if (Directory.Exists(nasRoot)) Directory.Delete(nasRoot, recursive: true);
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }
}
