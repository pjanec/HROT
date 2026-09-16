using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Core.Serialization;
using Fdp.Core.Serialization.Migrations;
using Fdp.Toolkit.Orchestration;
using Hrot.Common.Scenario;

namespace Hrot.Orchestrator;

/// <summary>
/// Process Manager responsible for broadcasting
/// <see cref="AssetInventoryUpdateEvent"/> on the event bus.
///
/// <para>Extracted from <see cref="ClusterMaster"/> so the 2PC orchestration engine has
/// zero knowledge of file systems, NAS paths, or storage gateways (SRP / CGF1-S0506).</para>
/// </summary>
public sealed class AssetInventoryProcessManager
{
    private readonly FdpEventBus _bus;
    private readonly StorageGatewayModule _gateway;
    private readonly string _nasBasePath;
    private readonly string _ledgerDirectory;
    private DateTime _lastInventoryScan = DateTime.MinValue;
    private ClusterState _lastState = ClusterState.Idle;
    private string? _pendingScenarioId;
    private RecordingLedgerEntry? _currentRecording;
    private readonly Dictionary<Guid, Guid> _pendingExports = new();
    private readonly Dictionary<Guid, RecordingLedgerEntry> _unarchivedLedger = new();

    public AssetInventoryProcessManager(
        FdpEventBus bus,
        StorageGatewayModule gateway,
        string nasBasePath,
        string localStagingRoot,
        int nodeId)
    {
        _bus              = bus              ?? throw new ArgumentNullException(nameof(bus));
        _gateway          = gateway          ?? throw new ArgumentNullException(nameof(gateway));
        _nasBasePath      = nasBasePath      ?? throw new ArgumentNullException(nameof(nasBasePath));
        _ = localStagingRoot ?? throw new ArgumentNullException(nameof(localStagingRoot));
        _ledgerDirectory = Path.Combine(
            OrchestrationConstants.GetNodeStagingRoot(localStagingRoot, nodeId),
            "recording_ledger");
        HydrateLedger();
    }

    /// <summary>
    /// Tracks local recording state and publishes
    /// <see cref="AssetInventoryUpdateEvent"/> when the interval elapses.
    /// Call once per frame from the Update loop.
    /// </summary>
    public void Tick()
    {
        foreach (var intent in _bus.ReadManaged<TransitionStateIntent>())
        {
            if (intent.TargetState == ClusterState.OperatingLive)
                _pendingScenarioId = intent.ScenarioId;
        }

        foreach (var ev in _bus.ReadManaged<ClusterStateUpdateEvent>())
        {
            if (_lastState != ClusterState.OperatingLive && ev.CurrentState == ClusterState.OperatingLive)
            {
                _currentRecording = new RecordingLedgerEntry(ev.ExerciseId, _pendingScenarioId, DateTime.UtcNow, TimeSpan.Zero);
            }
            else if (_lastState == ClusterState.OperatingLive && ev.CurrentState != ClusterState.OperatingLive)
            {
                if (_currentRecording != null && _currentRecording.ExerciseId != Guid.Empty)
                {
                    var duration = DateTime.UtcNow - _currentRecording.StartTimeUtc;
                    _currentRecording = _currentRecording with { Duration = duration };
                    _unarchivedLedger[_currentRecording.ExerciseId] = _currentRecording;
                    SaveLedgerEntry(_currentRecording);
                }

                _currentRecording = null;
                _pendingScenarioId = null;
            }

            _lastState = ev.CurrentState;
        }

        foreach (var intent in _bus.ReadManaged<ExecuteStorageOpIntent>())
        {
            if (intent.Operation == StorageOpType.Export)
                _pendingExports[intent.RequestId] = intent.ExerciseId;
        }

        // CE-278 §5: on export success, persist the archived-exercise metadata sidecar next to the pulled
        // .fdp files on NAS BEFORE dropping the local ledger entry, then evict. This re-homes the
        // Orchestrator.json sidecar onto the Export path (previously written only by the retired
        // SaveScenario=2 op); StorageGatewayModule.ScanNasExercises reads it back for the Archived
        // Exercises list. ⚠ Keyed on ClusterOpCompletedEvent — the real production completion signal
        // (PublishOpStatus / StorageProcessManager). StorageOpCompletedEvent has NO production publisher
        // (only the test-only EventDrivenStorageGateway), so the previous StorageOpCompletedEvent branch
        // never fired outside tests and the ledger was never evicted in production. See CE-278 design §5.
        foreach (var ev in _bus.ReadManaged<ClusterOpCompletedEvent>())
        {
            if (ev.StatusCode == OrchestrationStatusCode.Success &&
                _pendingExports.TryGetValue(ev.RequestId, out var exerciseId))
            {
                if (_unarchivedLedger.TryGetValue(exerciseId, out var ledgerEntry))
                    WriteExerciseSidecar(exerciseId, ledgerEntry);

                _unarchivedLedger.Remove(exerciseId);
                DeleteLedgerEntry(exerciseId);
                _pendingExports.Remove(ev.RequestId);
            }
        }

        if ((DateTime.UtcNow - _lastInventoryScan).TotalSeconds >= 5.0)
        {
            var scenariosNasPath  = Path.Combine(_nasBasePath, OrchestrationConstants.ScenariosDirectoryName);
            var exercisesNasPath  = Path.Combine(_nasBasePath, OrchestrationConstants.ExercisesDirectoryName);
            var localScenarios    = _gateway.ScanLocalScenarios(scenariosNasPath);
            var localExercises = _unarchivedLedger.Values
                .Select(e => new ExerciseInventoryItem(e.ExerciseId, e.StartTimeUtc, e.Duration, e.ScenarioId))
                .OrderByDescending(e => e.StartTimeUtc)
                .ToArray();
            var archivedExercises = _gateway.ScanNasExercises(exercisesNasPath)
                .OrderByDescending(e => e.StartTimeUtc)
                .ToArray();
            var unarchived = localExercises
                .ExceptBy(archivedExercises.Select(a => a.ExerciseId), e => e.ExerciseId)
                .ToArray();

            _bus.PublishManaged(new AssetInventoryUpdateEvent
            {
                LocalScenarios           = localScenarios.ToArray(),
                LocalExercises           = localExercises,
                ArchivedExercises        = archivedExercises,
                UnarchivedLocalExercises = unarchived,
            });
            _lastInventoryScan = DateTime.UtcNow;
        }
    }

    private void HydrateLedger()
    {
        if (!Directory.Exists(_ledgerDirectory))
        {
            Directory.CreateDirectory(_ledgerDirectory);
            return;
        }

        foreach (var file in Directory.GetFiles(_ledgerDirectory, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var entry = JsonSerializer.Deserialize<RecordingLedgerEntry>(json, FdpJsonOptionsRegistry.DefaultRelaxed);
                if (entry != null && entry.ExerciseId != Guid.Empty)
                    _unarchivedLedger[entry.ExerciseId] = entry;
            }
            catch
            {
            }
        }
    }

    private void SaveLedgerEntry(RecordingLedgerEntry entry)
    {
        try
        {
            if (!Directory.Exists(_ledgerDirectory))
                Directory.CreateDirectory(_ledgerDirectory);

            var path = Path.Combine(_ledgerDirectory, $"{entry.ExerciseId}.json");
            var json = JsonSerializer.Serialize(entry, FdpJsonOptionsRegistry.Indented);
            File.WriteAllText(path, json);
        }
        catch
        {
        }
    }

    private void DeleteLedgerEntry(Guid exerciseId)
    {
        try
        {
            var path = Path.Combine(_ledgerDirectory, $"{exerciseId}.json");
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    /// <summary>
    /// CE-278 §5 — writes the archived-exercise metadata sidecar
    /// (<c>&lt;nas&gt;/exercises/&lt;exerciseId&gt;/Orchestrator.json</c>) beside the pulled <c>.fdp</c>
    /// files, in the <see cref="GlobalContextDto"/> shape <see cref="StorageGatewayModule.ScanNasExercises"/>
    /// reads (start wall ticks + scenario id + duration). Best-effort: a failure must not abort the export
    /// lifecycle. The exercise dir uses <c>exerciseId.ToString()</c> to match <c>ReferenceArchiveHandler</c>'s
    /// NAS layout, so the sidecar lands in the same directory as the archived recordings.
    /// </summary>
    private void WriteExerciseSidecar(Guid exerciseId, RecordingLedgerEntry entry)
    {
        try
        {
            var dir = Path.Combine(
                _nasBasePath, OrchestrationConstants.ExercisesDirectoryName, exerciseId.ToString());
            Directory.CreateDirectory(dir);

            var dto = new GlobalContextDto
            {
                StartWallTicks      = entry.StartTimeUtc.Ticks,
                SceneId             = string.Empty,
                ScenarioId          = entry.ScenarioId ?? string.Empty,
                ScenarioTimeSeconds = entry.Duration.TotalSeconds,
            };

            var opts = new JsonSerializerOptions { WriteIndented = true };
            var dom  = JsonSerializer.SerializeToNode(dto, opts)!.AsObject();
            JsonEnvelope.Write(dom, new DocumentMeta(HrotDocumentTypes.OrchestratorContext, 2));
            File.WriteAllText(Path.Combine(dir, "Orchestrator.json"), dom.ToJsonString(opts));

            FdpLog<AssetInventoryProcessManager>.Info(
                "[AssetInventory] wrote exercise sidecar for {0} (scenarioId='{1}', duration={2:F1}s).",
                exerciseId, dto.ScenarioId, dto.ScenarioTimeSeconds);
        }
        catch (Exception ex)
        {
            FdpLog<AssetInventoryProcessManager>.Warn(
                "[AssetInventory] failed to write exercise sidecar for {0}: {1}", exerciseId, ex.Message);
        }
    }
}
