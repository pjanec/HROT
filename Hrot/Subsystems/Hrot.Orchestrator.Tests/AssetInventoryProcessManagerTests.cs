using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// <summary>
    /// ⭐ CE-4054 — the run in progress is in the inventory of what can be replayed (the master replays it when none is named), its
    /// length so far; ⛔ not among the unarchived, so nothing offers to export a run that is still recording.
    /// </summary>
    [Fact]
    public void CE4054_TheRunInProgress_IsReplayable_ButNotOfferedForExport()
    {
        var nasRoot = Path.Combine(Path.GetTempPath(), "fdp_ce4054_nas_" + Guid.NewGuid().ToString("N"));
        var staging = Path.Combine(Path.GetTempPath(), "fdp_ce4054_stg_" + Guid.NewGuid().ToString("N"));
        try
        {
            var exerciseId = Guid.NewGuid();
            var bus     = new FdpEventBus();
            var manager = new AssetInventoryProcessManager(bus, new StorageGatewayModule(), nasRoot, staging, 400);

            bus.PublishManaged(new TransitionStateIntent { TargetState = ClusterState.OperatingLive, ScenarioId = "town-raid", ExerciseId = exerciseId });
            bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = ClusterState.OperatingLive, ExerciseId = exerciseId });
            bus.SwapBuffers();
            manager.Tick();                                                    // the first scan publishes at once
            bus.SwapBuffers();

            AssetInventoryUpdateEvent? inventory = null;
            foreach (var ev in bus.ReadManaged<AssetInventoryUpdateEvent>()) inventory = ev;
            Assert.NotNull(inventory);
            var published = inventory!.Value;
            var live = Assert.Single(published.LocalExercises!);
            Assert.Equal(exerciseId, live.ExerciseId);
            Assert.Equal("town-raid", live.ScenarioId);
            Assert.Empty(published.UnarchivedLocalExercises!);
        }
        finally
        {
            if (Directory.Exists(nasRoot)) Directory.Delete(nasRoot, recursive: true);
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

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

    /// <summary>
    /// ⭐⭐ CE-4090 (AQ108 D14, §5a) — THE ONE GATE AT LEDGER CREATION: a live run the master loaded with "Record live runs" OFF
    /// (<c>ClusterStateUpdateEvent.RunNotRecorded</c>) creates no ledger entry, copies no scenario and is not in the inventory — before and after
    /// it ends — so nothing offers "AAR the last exercise" / keep / export for a recording that does not exist. The same run with the flag unset
    /// (today's default) is the control.
    /// </summary>
    [Fact]
    public void CE4090_ALiveRunWithRecordingOff_CreatesNoLedger_NoScenarioCopy_AndIsNotInTheInventory()
    {
        var nasRoot = Path.Combine(Path.GetTempPath(), "fdp_ce4090_nas_" + Guid.NewGuid().ToString("N"));
        var staging = Path.Combine(Path.GetTempPath(), "fdp_ce4090_stg_" + Guid.NewGuid().ToString("N"));
        const int nodeId = 400;
        try
        {
            const string scenarioId = "town-raid";
            var scenarioDir = Path.Combine(nasRoot, OrchestrationConstants.ScenariosDirectoryName, scenarioId);
            Directory.CreateDirectory(scenarioDir);
            File.WriteAllText(Path.Combine(scenarioDir, "SimHost.json"), "as-started");
            var ledgerDir = Path.Combine(OrchestrationConstants.GetNodeStagingRoot(staging, nodeId), "recording_ledger");

            AssetInventoryUpdateEvent? Run(Guid exercise, bool notRecorded)
            {
                var bus     = new FdpEventBus();
                var manager = new AssetInventoryProcessManager(bus, new StorageGatewayModule(), nasRoot, staging, nodeId);
                bus.PublishManaged(new TransitionStateIntent { TargetState = ClusterState.OperatingLive, ScenarioId = scenarioId, ExerciseId = exercise });
                bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = ClusterState.OperatingLive, ExerciseId = exercise, RunNotRecorded = notRecorded });
                bus.SwapBuffers();
                manager.Tick();                                   // the live run in progress (the first scan publishes at once)
                bus.SwapBuffers();
                AssetInventoryUpdateEvent? during = null;
                foreach (var ev in bus.ReadManaged<AssetInventoryUpdateEvent>()) during = ev;

                bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = ClusterState.Idle, ExerciseId = exercise, RunNotRecorded = notRecorded });
                bus.SwapBuffers();
                manager.Tick();                                   // the run ends
                return during;
            }

            // ── OFF: nothing is created ──
            var off = Guid.NewGuid();
            var duringOff = Run(off, notRecorded: true);
            Assert.NotNull(duringOff);
            Assert.Empty(duringOff!.Value.LocalExercises!);                                          // not replayable while it runs …
            Assert.False(File.Exists(Path.Combine(ledgerDir, $"{off}.json")), "no ledger entry after the run ends");
            Assert.False(Directory.Exists(Path.Combine(ledgerDir, $"{off}.scenario")), "no scenario copy");

            // ── the control: the flag unset records exactly as before ──
            var on = Guid.NewGuid();
            var duringOn = Run(on, notRecorded: false);
            Assert.Single(duringOn!.Value.LocalExercises!);
            Assert.True(File.Exists(Path.Combine(ledgerDir, $"{on}.json")), "a recorded run still gets its ledger entry");
            Assert.True(Directory.Exists(Path.Combine(ledgerDir, $"{on}.scenario")), "… and its scenario copy");
        }
        finally
        {
            if (Directory.Exists(nasRoot)) Directory.Delete(nasRoot, recursive: true);
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    // ── CE-4097 (AQ108 D5) — a branched run's parent and branch point on the inventory ─────────────────────────────────────────────

    private static AssetInventoryUpdateEvent? LastInventory(FdpEventBus bus)
    {
        AssetInventoryUpdateEvent? last = null;
        foreach (var ev in bus.ReadManaged<AssetInventoryUpdateEvent>()) last = ev;
        return last;
    }

    /// <summary>Runs one live run through a manager on <paramref name="staging"/>: optionally a branch answer (before or after the state), then the run ends.
    /// Returns what the inventory listed WHILE the run was live (null when the answer came before the state: the first scan had already published).</summary>
    private static ExerciseInventoryItem? RunLive(string nasRoot, string staging, Guid exercise, Guid? parent, long point, bool answerBeforeState, int nodeId = 400)
    {
        var bus     = new FdpEventBus();
        var manager = new AssetInventoryProcessManager(bus, new StorageGatewayModule(), nasRoot, staging, nodeId);
        var answer  = new ClusterOpCompletedEvent
        {
            RequestId = Guid.NewGuid(), StatusCode = OrchestrationStatusCode.Success,
            ResultPayload = new LiveBranchResult(new GlobalTime { TotalWallTicks = 42 }, true, parent ?? Guid.Empty, point),
        };
        if (parent != null && answerBeforeState)
        {
            bus.PublishManaged(answer);
            bus.SwapBuffers(); manager.Tick();                        // the nodes answered the branch; the cluster is not live yet
        }
        bus.PublishManaged(new TransitionStateIntent { TargetState = ClusterState.OperatingLive, ScenarioId = "town-raid", ExerciseId = exercise });
        bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = ClusterState.OperatingLive, ExerciseId = exercise });
        if (parent != null && !answerBeforeState) bus.PublishManaged(answer);   // the answer lands with / after the state
        bus.SwapBuffers();
        manager.Tick();
        bus.SwapBuffers();
        var live = LastInventory(bus) is { } inv ? inv.LocalExercises!.FirstOrDefault(e => e.ExerciseId == exercise) : (ExerciseInventoryItem?)null;

        bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = ClusterState.Idle, ExerciseId = exercise });
        bus.SwapBuffers();
        manager.Tick();                                               // the run ends: the ledger entry is written
        return live;
    }

    /// <summary>The unarchived inventory a RESTARTED manager lists from the ledger on disk (its first scan publishes at once).</summary>
    private static ExerciseInventoryItem FromLedger(string nasRoot, string staging, Guid exercise, int nodeId = 400)
    {
        var bus = new FdpEventBus();
        var manager = new AssetInventoryProcessManager(bus, new StorageGatewayModule(), nasRoot, staging, nodeId);
        bus.SwapBuffers(); manager.Tick(); bus.SwapBuffers();
        return Assert.Single(LastInventory(bus)!.Value.UnarchivedLocalExercises!, e => e.ExerciseId == exercise);
    }

    /// <summary>
    /// ⭐⭐ CE-4097 (success 1, 2) — a live run BRANCHED from a recording lists its parent's id and the branch point, live and (from the ledger) once it
    /// has ended; the answer may reach the manager before or after the cluster state does. A run from a LOAD lists empty, and so does the
    /// next load after a branch that never went live. 🔴 Mutation: the manager ignoring <c>LiveBranchResult</c> ⇒ the parent is empty ⇒ red.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CE4097_ABranchedLiveRun_ListsItsParentAndBranchPoint_AFreshLoadListsNone(bool answerBeforeState)
    {
        var nasRoot = Path.Combine(Path.GetTempPath(), "fdp_ce4097_nas_" + Guid.NewGuid().ToString("N"));
        var staging = Path.Combine(Path.GetTempPath(), "fdp_ce4097_stg_" + Guid.NewGuid().ToString("N"));
        try
        {
            var parent = Guid.NewGuid();
            var branched = Guid.NewGuid();
            var live = RunLive(nasRoot, staging, branched, parent, 987_654_321L, answerBeforeState);
            if (!answerBeforeState)   // answer-before: the first scan had published before the run existed; the ledger below is the check
            {
                Assert.Equal(branched, live!.Value.ExerciseId);
                Assert.Equal((parent, 987_654_321L), (live.Value.ParentExerciseId, live.Value.BranchWallTicks));   // listed while the run is live
            }
            var kept = FromLedger(nasRoot, staging, branched);
            Assert.Equal((parent, 987_654_321L), (kept.ParentExerciseId, kept.BranchWallTicks));                 // … and after it, from the ledger file

            // a run from a load: empty
            var loaded = Guid.NewGuid();
            var liveLoaded = RunLive(nasRoot, staging, loaded, parent: null, point: 0, answerBeforeState);
            Assert.Equal(loaded, liveLoaded!.Value.ExerciseId);
            Assert.Equal((Guid.Empty, 0L), (liveLoaded.Value.ParentExerciseId, liveLoaded.Value.BranchWallTicks));
            var keptLoaded = FromLedger(nasRoot, staging, loaded);
            Assert.Equal((Guid.Empty, 0L), (keptLoaded.ParentExerciseId, keptLoaded.BranchWallTicks));
        }
        finally
        {
            if (Directory.Exists(nasRoot)) Directory.Delete(nasRoot, recursive: true);
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    /// <summary>⭐ CE-4097 — a branch answer whose run never went live (the cluster fell back to a replay / Idle) must not label the NEXT load; and
    /// a ledger entry written before the fields existed reads as not-a-branch (default = not a branch).</summary>
    [Fact]
    public void CE4097_AnAbandonedBranch_DoesNotLabelTheNextLoad_AndAnOldLedgerEntryIsNotABranch()
    {
        var nasRoot = Path.Combine(Path.GetTempPath(), "fdp_ce4097b_nas_" + Guid.NewGuid().ToString("N"));
        var staging = Path.Combine(Path.GetTempPath(), "fdp_ce4097b_stg_" + Guid.NewGuid().ToString("N"));
        const int nodeId = 400;
        try
        {
            var bus     = new FdpEventBus();
            var manager = new AssetInventoryProcessManager(bus, new StorageGatewayModule(), nasRoot, staging, nodeId);
            bus.PublishManaged(new ClusterOpCompletedEvent
            {
                RequestId = Guid.NewGuid(), StatusCode = OrchestrationStatusCode.Success,
                ResultPayload = new LiveBranchResult(new GlobalTime { TotalWallTicks = 42 }, true, Guid.NewGuid(), 77),
            });
            bus.SwapBuffers(); manager.Tick();                                                                 // the nodes answered …
            bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = ClusterState.OperatingReplay });   // … and the branch fell back
            bus.SwapBuffers(); manager.Tick();

            var load = Guid.NewGuid();
            bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = ClusterState.OperatingLive, ExerciseId = load });
            bus.SwapBuffers(); manager.Tick();
            bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = ClusterState.Idle, ExerciseId = load });
            bus.SwapBuffers(); manager.Tick();
            var item = FromLedger(nasRoot, staging, load);
            Assert.Equal((Guid.Empty, 0L), (item.ParentExerciseId, item.BranchWallTicks));

            // an entry written by an older build: no such fields in the file
            var old = Guid.NewGuid();
            var ledgerDir = Path.Combine(OrchestrationConstants.GetNodeStagingRoot(staging, nodeId), "recording_ledger");
            File.WriteAllText(Path.Combine(ledgerDir, $"{old}.json"),
                $"{{\"exerciseId\":\"{old}\",\"scenarioId\":\"a\",\"startTimeUtc\":\"2026-10-01T08:00:00Z\",\"duration\":\"00:01:00\"}}");
            var oldItem = FromLedger(nasRoot, staging, old);
            Assert.Equal((Guid.Empty, 0L), (oldItem.ParentExerciseId, oldItem.BranchWallTicks));
        }
        finally
        {
            if (Directory.Exists(nasRoot)) Directory.Delete(nasRoot, recursive: true);
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    /// <summary>
    /// ⭐⭐ CE-4097 (success 3) — ONE SOURCE: for a finished exercise the inventory item is read OFF the recording's sidecar, so a rail that writes the
    /// sidecar (the real <c>MetadataSerializer</c> form) and reads both back finds them equal — in the archive scan (<c>ScanNasExercises</c>) and in the
    /// local scan; a node's later-branching recording does not win over the earliest; an exercise with no branch in any sidecar lists empty.
    /// 🔴 Mutation: the scans not reading the sidecar's parent ⇒ empty ⇒ red.
    /// </summary>
    [Fact]
    public void CE4097_AFinishedExercise_ListsTheParentAndPointOfItsRecordingMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "fdp_ce4097c_" + Guid.NewGuid().ToString("N"));
        try
        {
            var gateway = new StorageGatewayModule();
            var parent  = Guid.NewGuid();
            var branched = Guid.NewGuid();
            var plain    = Guid.NewGuid();
            var meta = new Fdp.Core.FlightRecorder.Metadata.RecordingMetadata
            {
                ExerciseId = branched, ParentExerciseId = parent, BranchWallTicks = 639_000_000_000_000_000L, Duration = TimeSpan.FromSeconds(90),
            };
            void Write(Guid exercise, string node, Fdp.Core.FlightRecorder.Metadata.RecordingMetadata m)
            {
                var dir = Path.Combine(root, exercise.ToString());
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, $"{node}.fdp"), "fdp");
                File.WriteAllText(Path.Combine(dir, $"{node}.fdp.meta.json"), Fdp.Core.FlightRecorder.Metadata.MetadataSerializer.Serialize(m));
            }
            Write(branched, "node_1", meta);
            Write(branched, "node_2", new Fdp.Core.FlightRecorder.Metadata.RecordingMetadata
            {
                ExerciseId = branched, ParentExerciseId = parent, BranchWallTicks = meta.BranchWallTicks + 5_000, Duration = TimeSpan.FromSeconds(90),
            });
            Write(plain, "node_1", new Fdp.Core.FlightRecorder.Metadata.RecordingMetadata { ExerciseId = plain, Duration = TimeSpan.FromSeconds(30) });

            foreach (var scanned in new[] { gateway.ScanNasExercises(root), gateway.ScanLocalExercises(root) })
            {
                var item = Assert.Single(scanned, e => e.ExerciseId == branched);
                Assert.Equal((meta.ParentExerciseId, meta.BranchWallTicks), (item.ParentExerciseId, item.BranchWallTicks));   // the inventory = the metadata
                var flat = Assert.Single(scanned, e => e.ExerciseId == plain);
                Assert.Equal((Guid.Empty, 0L), (flat.ParentExerciseId, flat.BranchWallTicks));
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// ⭐ CE-4090 (AQ108 D15) — discarding an unarchived recording goes through the retention code's delete (<c>ExerciseRetention.Delete</c>,
    /// CE-3218): the ledger entry (memory and file), the scenario copy and every <c>exercises/&lt;id&gt;</c> directory under the staging root
    /// go; a stranger's id deletes nothing and answers false; another exercise's directory is untouched.
    /// </summary>
    [Fact]
    public void CE4090_DeleteExercise_RemovesTheLedgerAndTheStagingCopies_AndRefusesAnUnknownId()
    {
        var nasRoot = Path.Combine(Path.GetTempPath(), "fdp_ce4090d_nas_" + Guid.NewGuid().ToString("N"));
        var staging = Path.Combine(Path.GetTempPath(), "fdp_ce4090d_stg_" + Guid.NewGuid().ToString("N"));
        const int nodeId = 400;
        try
        {
            var doomed = Guid.NewGuid();
            var kept   = Guid.NewGuid();
            var ledgerDir = Path.Combine(OrchestrationConstants.GetNodeStagingRoot(staging, nodeId), "recording_ledger");
            Directory.CreateDirectory(ledgerDir);
            foreach (var id in new[] { doomed, kept })
            {
                File.WriteAllText(Path.Combine(ledgerDir, $"{id}.json"), JsonSerializer.Serialize(
                    new RecordingLedgerEntry(id, "hill-attack", DateTime.UtcNow.AddMinutes(-2), TimeSpan.FromSeconds(60)), FdpJsonOptionsRegistry.Indented));
                // the recordings: one directory in the shared exercises folder and one in each node's
                foreach (var dir in new[]
                {
                    Path.Combine(staging, OrchestrationConstants.ExercisesDirectoryName, id.ToString()),
                    Path.Combine(OrchestrationConstants.GetNodeStagingRoot(staging, nodeId), OrchestrationConstants.ExercisesDirectoryName, id.ToString()),
                })
                {
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, "rec.fdp"), "bytes");
                }
            }
            Directory.CreateDirectory(Path.Combine(ledgerDir, $"{doomed}.scenario"));

            var manager = new AssetInventoryProcessManager(new FdpEventBus(), new StorageGatewayModule(), nasRoot, staging, nodeId);   // hydrates both

            Assert.False(manager.DeleteExercise(Guid.NewGuid()), "an id the ledger does not hold is not deleted");
            Assert.True(Directory.Exists(Path.Combine(staging, OrchestrationConstants.ExercisesDirectoryName, doomed.ToString())));

            Assert.True(manager.DeleteExercise(doomed));
            Assert.False(File.Exists(Path.Combine(ledgerDir, $"{doomed}.json")));
            Assert.False(Directory.Exists(Path.Combine(ledgerDir, $"{doomed}.scenario")));
            Assert.False(Directory.Exists(Path.Combine(staging, OrchestrationConstants.ExercisesDirectoryName, doomed.ToString())));
            Assert.False(Directory.Exists(Path.Combine(OrchestrationConstants.GetNodeStagingRoot(staging, nodeId), OrchestrationConstants.ExercisesDirectoryName, doomed.ToString())));
            Assert.False(manager.DeleteExercise(doomed), "gone: a second discard knows nothing of it");

            // the other exercise is untouched
            Assert.True(File.Exists(Path.Combine(ledgerDir, $"{kept}.json")));
            Assert.True(Directory.Exists(Path.Combine(staging, OrchestrationConstants.ExercisesDirectoryName, kept.ToString())));
        }
        finally
        {
            if (Directory.Exists(nasRoot)) Directory.Delete(nasRoot, recursive: true);
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    // ── ⭐⭐ CE-4091 (AQ108 D7, D22; M2, M3) — the recording keeps its scenario: the WHOLE folder, stamped at go-live, inherited by a branch,
    //    and opened for editing as a new scenario ───────────────────────────────────────────────────────────────────────────────────────

    private static string Scratch(string what) => Path.Combine(Path.GetTempPath(), $"fdp_ce4091_{what}_" + Guid.NewGuid().ToString("N"));

    private static string LedgerCopy(string staging, Guid exercise, int nodeId = 400)
        => Path.Combine(OrchestrationConstants.GetNodeStagingRoot(staging, nodeId), "recording_ledger", $"{exercise}.scenario");

    /// <summary>A scenario folder as a distributed save leaves it (M2): the merged file, foreign slices, the director's takes, a nested file — and the working folders.</summary>
    private static string WriteFullScenario(string nasRoot, string name, string scenarioJson)
    {
        var dir = Path.Combine(nasRoot, OrchestrationConstants.ScenariosDirectoryName, name);
        Directory.CreateDirectory(Path.Combine(dir, "foreign"));
        Directory.CreateDirectory(Path.Combine(dir, "takes"));
        Directory.CreateDirectory(Path.Combine(dir, "extras", "deep"));
        Directory.CreateDirectory(Path.Combine(dir, ".slices"));
        File.WriteAllText(Path.Combine(dir, "scenario.json"), scenarioJson);
        File.WriteAllText(Path.Combine(dir, "checks.json"), "{\"checks\":1}");
        File.WriteAllText(Path.Combine(dir, "foreign", "node_7.json"), "{\"foreign\":7}");
        File.WriteAllText(Path.Combine(dir, "foreign", "index.json"), "{\"index\":true}");
        File.WriteAllText(Path.Combine(dir, "takes", "main.take.json"), "{\"take\":\"main\"}");
        File.WriteAllText(Path.Combine(dir, "extras", "deep", "note.txt"), "nested");
        File.WriteAllText(Path.Combine(dir, ".slices", "node_1.json"), "scratch");
        return dir;
    }

    /// <summary>Every file under <paramref name="dir"/> as relative path → content (the comparison a "copy equals" claim needs).</summary>
    private static SortedDictionary<string, string> Contents(string dir)
        => new(Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(dir, f).Replace('\\', '/'), File.ReadAllText), StringComparer.Ordinal);

    private static void GoLive(AssetInventoryProcessManager manager, FdpEventBus bus, Guid exercise, string? scenario)
    {
        bus.PublishManaged(new TransitionStateIntent { TargetState = ClusterState.OperatingLive, ScenarioId = scenario, ExerciseId = exercise });
        bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = ClusterState.OperatingLive, ExerciseId = exercise });
        bus.SwapBuffers();
        manager.Tick();
    }

    private static void Cleanup(params string[] dirs)
    {
        foreach (var d in dirs) if (Directory.Exists(d)) Directory.Delete(d, recursive: true);
    }

    /// <summary>
    /// ⭐ CE-4091 success 1 — the copy is stamped AT GO-LIVE from the SAVED file: scenario "loaded" as v1, edited and saved as v2, then live ⇒ the stamp
    /// carries v2's hash (the one rule, <c>ScenarioDescription.HashOf</c>), the scenario's name and the time it was recorded — never v1's.
    /// 🔴 Mutation: the stamp's hash left empty (or taken before the copy) ⇒ red.
    /// </summary>
    [Fact]
    public void CE4091_TheCopyIsStamped_AtGoLive_FromTheSavedFile_NotTheLoadedOne()
    {
        var nasRoot = Scratch("nas"); var staging = Scratch("stg");
        try
        {
            const string scenario = "town-raid";
            var dir = WriteFullScenario(nasRoot, scenario, "{\"v\":\"as-loaded\"}");
            var loadedHash = Fdp.Toolkit.Scenario.ScenarioDescription.HashOf(File.ReadAllText(Path.Combine(dir, "scenario.json")));
            File.WriteAllText(Path.Combine(dir, "scenario.json"), "{\"v\":\"edited-and-saved\"}");   // edit → save, before go-live
            var savedHash = Fdp.Toolkit.Scenario.ScenarioDescription.HashOf("{\"v\":\"edited-and-saved\"}");

            var bus = new FdpEventBus();
            var manager = new AssetInventoryProcessManager(bus, new StorageGatewayModule(), nasRoot, staging, 400);
            var before = DateTime.UtcNow;
            var exercise = Guid.NewGuid();
            GoLive(manager, bus, exercise, scenario);

            var stamp = Fdp.Toolkit.Scenario.RecordedScenarioStamp.Read(LedgerCopy(staging, exercise));
            Assert.NotNull(stamp);
            Assert.Equal(savedHash, stamp!.ScenarioHash);
            Assert.NotEqual(loadedHash, stamp.ScenarioHash);
            Assert.Equal(scenario, stamp.ScenarioName);
            Assert.InRange(stamp.RecordedAt, before.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
            Assert.Equal(DateTimeKind.Utc, stamp.RecordedAt.Kind);
        }
        finally { Cleanup(nasRoot, staging); }
    }

    /// <summary>
    /// ⭐ CE-4091 success 2 (M2) — the copy holds EVERY subfolder (<c>foreign/</c>, <c>takes/</c>, a nested file), in the ledger while the run is
    /// unarchived and in the archive after the export; the save's working folder (<c>.slices</c>) is not copied.
    /// 🔴 Mutation: <c>CopyScenarioFiles</c> back to the top-level files only ⇒ red.
    /// </summary>
    [Fact]
    public void CE4091_TheCopyHoldsEverySubfolder_InTheLedgerAndInTheArchive()
    {
        var nasRoot = Scratch("nas"); var staging = Scratch("stg");
        try
        {
            const string scenario = "town-raid";
            var source = WriteFullScenario(nasRoot, scenario, "{\"v\":1}");
            var bus = new FdpEventBus();
            var manager = new AssetInventoryProcessManager(bus, new StorageGatewayModule(), nasRoot, staging, 400);
            var exercise = Guid.NewGuid();
            GoLive(manager, bus, exercise, scenario);

            var expected = Contents(source);
            foreach (var k in expected.Keys.Where(k => k.StartsWith(".slices/", StringComparison.Ordinal)).ToList()) expected.Remove(k);
            var ledgerCopy = Contents(LedgerCopy(staging, exercise));
            Assert.True(ledgerCopy.Remove("recorded.stamp"), "the stamp rides with the copy");
            Assert.Equal(expected, ledgerCopy);
            foreach (var must in new[] { "foreign/node_7.json", "foreign/index.json", "takes/main.take.json", "extras/deep/note.txt" })
                Assert.True(ledgerCopy.ContainsKey(must), $"{must} is part of the scenario");

            // the run ends and is exported: the archive's copy is the same, stamp included
            bus.PublishManaged(new ClusterStateUpdateEvent { CurrentState = ClusterState.Idle, ExerciseId = exercise });
            bus.SwapBuffers(); manager.Tick();
            var nasExercise = Path.Combine(nasRoot, OrchestrationConstants.ExercisesDirectoryName, exercise.ToString());
            Directory.CreateDirectory(nasExercise);
            File.WriteAllText(Path.Combine(nasExercise, "rec_node_400.fdp"), "bytes");
            var requestId = Guid.NewGuid();
            bus.PublishManaged(new ExecuteStorageOpIntent { RequestId = requestId, Operation = StorageOpType.Export, ExerciseId = exercise });
            bus.SwapBuffers(); manager.Tick();
            bus.PublishManaged(new ClusterOpCompletedEvent { RequestId = requestId, StatusCode = OrchestrationStatusCode.Success });
            bus.SwapBuffers(); manager.Tick();

            var archived = Contents(Path.Combine(nasExercise, AssetInventoryProcessManager.ArchivedScenarioDirectoryName));
            Assert.True(archived.Remove("recorded.stamp"));
            Assert.Equal(expected, archived);
            Assert.False(Directory.Exists(LedgerCopy(staging, exercise)), "the ledger's copy goes with the export, as before");
        }
        finally { Cleanup(nasRoot, staging); }
    }

    /// <summary>
    /// ⭐ CE-4091 success 4 (D7) — a BRANCH's copy EQUALS its parent's (every file, subfolder and the stamp), not the scenario file as it is now —
    /// whether the nodes' branch answer reaches the manager before or after the cluster state does. 🔴 Mutation: the late answer (or the early one)
    /// no longer inheriting ⇒ the branch keeps the changed file ⇒ red.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CE4091_ABranchesCopy_EqualsItsParents_WhateverTheFileSaysNow(bool answerBeforeState)
    {
        var nasRoot = Scratch("nas"); var staging = Scratch("stg");
        try
        {
            var dir = WriteFullScenario(nasRoot, "town-raid", "{\"v\":\"parent-ran-this\"}");
            var parent = Guid.NewGuid(); var branch = Guid.NewGuid();
            RunLive(nasRoot, staging, parent, null, 0, false);

            File.WriteAllText(Path.Combine(dir, "scenario.json"), "{\"v\":\"changed-since\"}");   // saved over after the parent ran
            File.WriteAllText(Path.Combine(dir, "extras", "later.txt"), "added since");
            RunLive(nasRoot, staging, branch, parent, 5, answerBeforeState);

            var parentCopy = Contents(LedgerCopy(staging, parent));
            Assert.Equal("{\"v\":\"parent-ran-this\"}", parentCopy["scenario.json"]);
            Assert.Equal(parentCopy, Contents(LedgerCopy(staging, branch)));
        }
        finally { Cleanup(nasRoot, staging); }
    }

    /// <summary>⭐ CE-4091 (D7) — the same when the parent is ARCHIVED (its copy is on the NAS, its ledger snapshot gone) and the branch names no scenario of its own.</summary>
    [Fact]
    public void CE4091_ABranchInheritsAnArchivedParentsCopy_EvenWithNoScenarioNamed()
    {
        var nasRoot = Scratch("nas"); var staging = Scratch("stg");
        try
        {
            var parent = Guid.NewGuid(); var branch = Guid.NewGuid();
            var archived = Path.Combine(nasRoot, OrchestrationConstants.ExercisesDirectoryName, parent.ToString(), AssetInventoryProcessManager.ArchivedScenarioDirectoryName);
            Directory.CreateDirectory(Path.Combine(archived, "takes"));
            File.WriteAllText(Path.Combine(archived, "scenario.json"), "{\"v\":\"archived\"}");
            File.WriteAllText(Path.Combine(archived, "takes", "t.take.json"), "{}");
            Fdp.Toolkit.Scenario.RecordedScenarioStamp.Write(archived, "hill-attack", DateTime.UtcNow.AddDays(-1));

            var bus = new FdpEventBus();
            var manager = new AssetInventoryProcessManager(bus, new StorageGatewayModule(), nasRoot, staging, 400);
            bus.PublishManaged(new ClusterOpCompletedEvent
            {
                RequestId = Guid.NewGuid(), StatusCode = OrchestrationStatusCode.Success,
                ResultPayload = new LiveBranchResult(new GlobalTime { TotalWallTicks = 42 }, true, parent, 9),
            });
            bus.SwapBuffers(); manager.Tick();
            GoLive(manager, bus, branch, null);   // a branch names no scenario

            Assert.Equal(Contents(archived), Contents(LedgerCopy(staging, branch)));
            Assert.Equal("hill-attack", Fdp.Toolkit.Scenario.RecordedScenarioStamp.Read(LedgerCopy(staging, branch))!.ScenarioName);
        }
        finally { Cleanup(nasRoot, staging); }
    }

    /// <summary>
    /// ⭐⭐ CE-4091 success 3 (D7, D22) — OPENING A RECORDED COPY AS A NEW SCENARIO: the copy appears under the reserved new name
    /// <c>Recipes/Recorded/&lt;id&gt;</c> with every file and subfolder but WITHOUT its stamp (it is a new scenario, not a recording), and the original
    /// scenario folder, the recording's ledger copy and the NAS exercise are byte- and mtime-identical afterwards. Any other name is not this op's;
    /// an exercise with no copy fails loudly. 🔴 Mutations: staging into the original scenario's folder, or writing the stamp along ⇒ red.
    /// </summary>
    [Fact]
    public void CE4091_OpeningARecordedCopy_StagesItAsANewName_AndWritesNeitherTheOriginalNorTheRecording()
    {
        var nasRoot = Scratch("nas"); var staging = Scratch("stg");
        try
        {
            const string scenario = "town-raid";
            var original = WriteFullScenario(nasRoot, scenario, "{\"v\":\"ran\"}");
            var bus = new FdpEventBus();
            var manager = new AssetInventoryProcessManager(bus, new StorageGatewayModule(), nasRoot, staging, 400);
            var exercise = Guid.NewGuid();
            GoLive(manager, bus, exercise, scenario);
            var nasExercise = Path.Combine(nasRoot, OrchestrationConstants.ExercisesDirectoryName, exercise.ToString());
            Directory.CreateDirectory(nasExercise);
            File.WriteAllText(Path.Combine(nasExercise, "rec_node_400.fdp"), "bytes");

            string Snapshot(params string[] dirs) => string.Join("\n", dirs.SelectMany(d => Directory.GetFiles(d, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal)
                .Select(f => $"{f}|{File.GetLastWriteTimeUtc(f).Ticks}|{File.ReadAllText(f)}")));
            var ledgerCopy = LedgerCopy(staging, exercise);
            var before = Snapshot(original, ledgerCopy, nasExercise);

            var name = RecordedScenarioNames.ForExercise(exercise);
            Assert.True(manager.StageRecordedScenario(name));

            var staged = Path.Combine(nasRoot, OrchestrationConstants.ScenariosDirectoryName, "Recipes", "Recorded", exercise.ToString());
            var expected = Contents(ledgerCopy);
            Assert.True(expected.Remove("recorded.stamp"));
            Assert.Equal(expected, Contents(staged));
            Assert.Equal("{\"v\":\"ran\"}", File.ReadAllText(Path.Combine(staged, "scenario.json")));
            Assert.True(File.Exists(Path.Combine(staged, "foreign", "node_7.json")));
            Assert.False(File.Exists(Path.Combine(staged, "recorded.stamp")), "a new scenario is not a recording");

            Assert.Equal(before, Snapshot(original, ledgerCopy, nasExercise));   // ⛔ neither the original scenario nor the recording was written

            Assert.True(manager.StageRecordedScenario(name), "again: the staging is regenerated from the copy, never kept half-edited");
            Assert.False(manager.StageRecordedScenario("town-raid"), "an ordinary scenario name is not this op's");
            Assert.Throws<DirectoryNotFoundException>(() => manager.StageRecordedScenario(RecordedScenarioNames.ForExercise(Guid.NewGuid())));
        }
        finally { Cleanup(nasRoot, staging); }
    }

    /// <summary>
    /// ⭐ CE-4091 — the PREFETCH runs the stager before it stages a name to the nodes: a load of <c>Recipes/Recorded/&lt;id&gt;</c> that exists nowhere on
    /// the NAS succeeds and lands the copy in the node's staging folder; with no copy it FAILS (PrefetchStagingCompletedEvent.IsSuccess false).
    /// 🔴 Mutation: the prefetch not calling <c>SourceStager</c> ⇒ the first half fails.
    /// </summary>
    [Fact]
    public void CE4091_ThePrefetchOfARecordedName_StagesTheCopyFirst_AndFailsWhenThereIsNone()
    {
        var nasRoot = Scratch("nas"); var staging = Scratch("stg");
        try
        {
            var bus = new FdpEventBus();
            var inventory = new AssetInventoryProcessManager(bus, new StorageGatewayModule(), nasRoot, staging, 400);
            WriteFullScenario(nasRoot, "town-raid", "{\"v\":\"ran\"}");
            var exercise = Guid.NewGuid();
            GoLive(inventory, bus, exercise, "town-raid");
            var prefetch = new AssetPrefetchProcessManager(bus, new StorageGatewayModule(), nasRoot, staging) { SourceStager = inventory.StageRecordedScenario };

            PrefetchStagingCompletedEvent Prefetch(string scenarioName)
            {
                bus.PublishManaged(new ExecutePrefetchIntent { RequestId = Guid.NewGuid(), ScenarioId = scenarioName, ActiveNodeIds = new List<int> { 1 } });
                bus.SwapBuffers();
                prefetch.Tick();
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (DateTime.UtcNow < deadline)
                {
                    bus.SwapBuffers();
                    foreach (var done in bus.ReadManaged<PrefetchStagingCompletedEvent>()) return done;
                    System.Threading.Thread.Sleep(20);
                }
                throw new TimeoutException("the prefetch did not complete");
            }

            Assert.True(Prefetch(RecordedScenarioNames.ForExercise(exercise)).IsSuccess);
            var node = Path.Combine(OrchestrationConstants.GetNodeStagingRoot(staging, 1), OrchestrationConstants.ScenariosDirectoryName,
                "Recipes", "Recorded", exercise.ToString());
            Assert.Equal("{\"v\":\"ran\"}", File.ReadAllText(Path.Combine(node, "scenario.json")));

            Assert.False(Prefetch(RecordedScenarioNames.ForExercise(Guid.NewGuid())).IsSuccess, "a recording with no copy fails the load like a scenario that is not there");
        }
        finally { Cleanup(nasRoot, staging); }
    }
}
