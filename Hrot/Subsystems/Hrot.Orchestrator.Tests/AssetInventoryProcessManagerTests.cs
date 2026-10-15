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

    /// <summary>
    /// ⭐ CE-3119 (mgmt-1 §13.3a) — the archive carries a copy of the scenario the exercise RAN: snapshotted when it
    /// goes live, filed under <c>exercises/&lt;id&gt;/scenario/</c> on export. A Save Scenario over the same folder after
    /// the start does not leak into the archive, and the local snapshot goes with the ledger entry.
    /// </summary>
    [Fact]
    public void CE3119_TheArchive_CarriesTheScenarioAsItWasWhenTheExerciseStarted()
    {
        var nasRoot = Path.Combine(Path.GetTempPath(), "fdp_ce3119_nas_" + Guid.NewGuid().ToString("N"));
        var staging = Path.Combine(Path.GetTempPath(), "fdp_ce3119_stg_" + Guid.NewGuid().ToString("N"));
        const int nodeId = 400;

        try
        {
            var exerciseId = Guid.NewGuid();
            const string scenarioId = "town-raid";
            var scenarioDir = Path.Combine(nasRoot, OrchestrationConstants.ScenariosDirectoryName, scenarioId);
            Directory.CreateDirectory(scenarioDir);
            File.WriteAllText(Path.Combine(scenarioDir, "SimHost.json"), "as-started");
            File.WriteAllText(Path.Combine(scenarioDir, "Orchestrator.json"), "{}");

            var bus     = new FdpEventBus();
            var manager = new AssetInventoryProcessManager(bus, new StorageGatewayModule(), nasRoot, staging, nodeId);

            // The exercise goes live from the scenario.
            bus.PublishManaged(new TransitionStateIntent { TargetState = ClusterState.OperatingLive, ScenarioId = scenarioId, ExerciseId = exerciseId });
            bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = ClusterState.OperatingLive, ExerciseId = exerciseId });
            bus.SwapBuffers();
            manager.Tick();

            // The scenario is saved over during the run, then the exercise stops.
            File.WriteAllText(Path.Combine(scenarioDir, "SimHost.json"), "saved-over");
            bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = ClusterState.Idle, ExerciseId = exerciseId });
            bus.SwapBuffers();
            manager.Tick();

            // Export: the recordings are pulled (as the gateway would), then the export completes.
            var nasExerciseDir = Path.Combine(nasRoot, OrchestrationConstants.ExercisesDirectoryName, exerciseId.ToString());
            Directory.CreateDirectory(nasExerciseDir);
            File.WriteAllText(Path.Combine(nasExerciseDir, "rec_node_400.fdp"), "fdp-bytes");
            var requestId = Guid.NewGuid();
            bus.PublishManaged(new ExecuteStorageOpIntent { RequestId = requestId, Operation = StorageOpType.Export, ExerciseId = exerciseId });
            bus.SwapBuffers();
            manager.Tick();
            bus.PublishManaged(new ClusterOpCompletedEvent { RequestId = requestId, StatusCode = OrchestrationStatusCode.Success });
            bus.SwapBuffers();
            manager.Tick();

            var archived = Path.Combine(nasExerciseDir, AssetInventoryProcessManager.ArchivedScenarioDirectoryName);
            Assert.Equal("as-started", File.ReadAllText(Path.Combine(archived, "SimHost.json")));
            Assert.True(File.Exists(Path.Combine(archived, "Orchestrator.json")), "every scenario file is copied");
            Assert.True(File.Exists(Path.Combine(nasExerciseDir, "Orchestrator.json")), "the CE-278 sidecar is still written");

            var ledgerDir = Path.Combine(OrchestrationConstants.GetNodeStagingRoot(staging, nodeId), "recording_ledger");
            Assert.False(Directory.Exists(Path.Combine(ledgerDir, $"{exerciseId}.scenario")), "the snapshot goes with the ledger entry");
            Assert.Single(new StorageGatewayModule().ScanNasExercises(Path.Combine(nasRoot, OrchestrationConstants.ExercisesDirectoryName)));
        }
        finally
        {
            if (Directory.Exists(nasRoot)) Directory.Delete(nasRoot, recursive: true);
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    /// <summary>
    /// ⭐ CE-3119 — a ledger entry from before the snapshot existed still archives a scenario copy: the one on the NAS now.
    /// </summary>
    [Fact]
    public void CE3119_ALedgerEntryWithNoSnapshot_ArchivesTheCurrentScenario()
    {
        var nasRoot = Path.Combine(Path.GetTempPath(), "fdp_ce3119b_nas_" + Guid.NewGuid().ToString("N"));
        var staging = Path.Combine(Path.GetTempPath(), "fdp_ce3119b_stg_" + Guid.NewGuid().ToString("N"));
        const int nodeId = 400;

        try
        {
            var exerciseId = Guid.NewGuid();
            const string scenarioId = "hill-attack";
            var scenarioDir = Path.Combine(nasRoot, OrchestrationConstants.ScenariosDirectoryName, scenarioId);
            Directory.CreateDirectory(scenarioDir);
            File.WriteAllText(Path.Combine(scenarioDir, "SimHost.json"), "current");

            var ledgerDir = Path.Combine(OrchestrationConstants.GetNodeStagingRoot(staging, nodeId), "recording_ledger");
            Directory.CreateDirectory(ledgerDir);
            File.WriteAllText(Path.Combine(ledgerDir, $"{exerciseId}.json"), JsonSerializer.Serialize(
                new RecordingLedgerEntry(exerciseId, scenarioId, DateTime.UtcNow.AddMinutes(-2), TimeSpan.FromSeconds(60)),
                FdpJsonOptionsRegistry.Indented));

            var bus     = new FdpEventBus();
            var manager = new AssetInventoryProcessManager(bus, new StorageGatewayModule(), nasRoot, staging, nodeId);
            var requestId = Guid.NewGuid();
            bus.PublishManaged(new ExecuteStorageOpIntent { RequestId = requestId, Operation = StorageOpType.Export, ExerciseId = exerciseId });
            bus.SwapBuffers();
            manager.Tick();
            bus.PublishManaged(new ClusterOpCompletedEvent { RequestId = requestId, StatusCode = OrchestrationStatusCode.Success });
            bus.SwapBuffers();
            manager.Tick();

            var archived = Path.Combine(nasRoot, OrchestrationConstants.ExercisesDirectoryName, exerciseId.ToString(),
                AssetInventoryProcessManager.ArchivedScenarioDirectoryName, "SimHost.json");
            Assert.Equal("current", File.ReadAllText(archived));
        }
        finally
        {
            if (Directory.Exists(nasRoot)) Directory.Delete(nasRoot, recursive: true);
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }
}
