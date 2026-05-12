using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Fdp.Core;
using Fdp.Core.Serialization;
using Fdp.Toolkit.Orchestration;

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
                _currentRecording = new RecordingLedgerEntry(ev.ExerciseId, _pendingScenarioId, DateTime.UtcNow);
            }
            else if (_lastState == ClusterState.OperatingLive && ev.CurrentState != ClusterState.OperatingLive)
            {
                if (_currentRecording != null && _currentRecording.ExerciseId != Guid.Empty)
                {
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

        foreach (var ev in _bus.ReadManaged<StorageOpCompletedEvent>())
        {
            if (ev.StatusCode == OrchestrationStatusCode.Success &&
                _pendingExports.TryGetValue(ev.RequestId, out var exerciseId))
            {
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
            var archivedExercises = _gateway.ScanNasExercises(exercisesNasPath);
            var localExercises    = _unarchivedLedger.Keys.Select(id => id.ToString()).ToArray();
            var unarchived        = localExercises.Except(archivedExercises).ToArray();

            _bus.PublishManaged(new AssetInventoryUpdateEvent
            {
                LocalScenarios           = localScenarios.ToArray(),
                LocalExercises           = localExercises,
                ArchivedExercises        = archivedExercises.ToArray(),
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
}
